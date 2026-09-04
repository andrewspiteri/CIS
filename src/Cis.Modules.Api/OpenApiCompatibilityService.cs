using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Api;

public sealed class OpenApiCompatibilityService(ICisRepositoryContextResolver contextResolver)
{
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "post", "put", "patch", "delete", "head", "options", "trace",
    };

    public ApiDiffResult Diff(string repositoryPath, string? baselinePath, string? currentPath)
    {
        var resolution = contextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return new ApiDiffResult("invalid", null, baselinePath, currentPath, 0, 0, 0, [], [], resolution.Errors, false);
        }

        var context = resolution.Context;
        var profile = ReadProfile(context);
        var baselinePaths = string.IsNullOrWhiteSpace(baselinePath)
            ? profile.OpenApiBaselines.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [baselinePath];
        var baselineLabel = string.Join(',', baselinePaths);
        currentPath = Select(currentPath, profile.OpenApiCurrent);
        if (baselinePaths.Length == 0 || currentPath is null)
        {
            return new ApiDiffResult("unavailable", context.RepositoryPath, baselineLabel, currentPath, 0, 0, 0, [], [],
                ["At least one supported baseline and one current OpenAPI path are required, either as options or in the API governance profile."], true);
        }

        var currentAbsolute = Resolve(context.RepositoryPath, currentPath);
        var errors = new List<string>();
        if (currentAbsolute is null || !File.Exists(currentAbsolute)) errors.Add($"Current OpenAPI document was not found: {currentPath}");
        var resolvedBaselines = baselinePaths.Select(path => (Path: path, Absolute: Resolve(context.RepositoryPath, path))).ToArray();
        foreach (var baseline in resolvedBaselines)
            if (baseline.Absolute is null || !File.Exists(baseline.Absolute)) errors.Add($"Baseline OpenAPI document was not found: {baseline.Path}");
        if (errors.Count > 0)
        {
            return new ApiDiffResult("unavailable", context.RepositoryPath, baselineLabel, currentPath, 0, 0, 0, [], [], errors, true);
        }

        try
        {
            using var current = JsonDocument.Parse(File.ReadAllText(currentAbsolute!));
            var newOperations = Operations(current.RootElement);
            var findings = new List<ApiCompatibilityFinding>();
            var comparisons = new List<ApiBaselineComparison>();
            foreach (var configuredBaseline in resolvedBaselines)
            {
                using var baseline = JsonDocument.Parse(File.ReadAllText(configuredBaseline.Absolute!));
                var baselineFindings = Compare(Operations(baseline.RootElement), newOperations)
                    .Select(finding => finding with
                    {
                        Evidence = finding.Evidence.Concat([$"baseline:{configuredBaseline.Path}"]).ToArray(),
                    }).ToArray();
                findings.AddRange(baselineFindings);
                var comparisonBreaking = baselineFindings.Count(item => item.Severity == "breaking");
                var comparisonPotential = baselineFindings.Count(item => item.Severity == "potentially-breaking");
                comparisons.Add(new ApiBaselineComparison(configuredBaseline.Path, currentPath,
                    comparisonBreaking > 0 ? "breaking" : comparisonPotential > 0 ? "review" : "compatible",
                    comparisonBreaking, comparisonPotential,
                    baselineFindings.Count(item => item.Severity == "non-breaking")));
            }

            findings = findings.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Method, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => string.Join('|', item.Evidence), StringComparer.Ordinal).ToList();
            var breaking = findings.Count(item => item.Severity == "breaking");
            var potential = findings.Count(item => item.Severity == "potentially-breaking");
            return new ApiDiffResult(breaking > 0 ? "breaking" : potential > 0 ? "review" : "compatible",
                context.RepositoryPath, baselineLabel, currentPath, breaking, potential,
                findings.Count(item => item.Severity == "non-breaking"), findings, comparisons, [], true);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ApiDiffResult("invalid", context.RepositoryPath, baselineLabel, currentPath, 0, 0, 0, [], [],
                [$"OpenAPI comparison failed: {exception.Message}"], true);
        }
    }

    private static IReadOnlyList<ApiCompatibilityFinding> Compare(
        IReadOnlyDictionary<string, OperationShape> oldOperations,
        IReadOnlyDictionary<string, OperationShape> newOperations)
    {
        var findings = new List<ApiCompatibilityFinding>();
        foreach (var old in oldOperations.Values)
        {
            if (!newOperations.TryGetValue(old.Key, out var newer))
            {
                findings.Add(Finding("CIS-API-DIFF-001", "breaking", "operation-removed", old.Method, old.Path,
                    "Operation was removed or its method/path changed.", "Preserve the operation or introduce a new major version and consumer migration.", old.Key));
                continue;
            }
            CompareOperation(old, newer, findings);
        }

        foreach (var added in newOperations.Values.Where(item => !oldOperations.ContainsKey(item.Key)))
        {
            findings.Add(Finding("CIS-API-DIFF-100", "non-breaking", "operation-added", added.Method, added.Path,
                "Operation was added.", "Add the governed API row, permissions/problems, consumers, and tests.", added.Key));
        }
        return findings;
    }

    private static void CompareOperation(OperationShape old, OperationShape current, ICollection<ApiCompatibilityFinding> findings)
    {
        foreach (var required in old.RequiredRequest.Except(current.RequiredRequest, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-101", "non-breaking", "request-required-relaxed", old.Method, old.Path,
                $"Request property is no longer required: {required}", "Confirm server behavior still accepts older requests.", required));
        }
        foreach (var required in current.RequiredRequest.Except(old.RequiredRequest, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-002", "breaking", "request-required-added", old.Method, old.Path,
                $"Required request property was added: {required}", "Make the property optional or introduce a new major version.", required));
        }
        foreach (var property in old.RequestProperties.Except(current.RequestProperties, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-010", "breaking", "request-property-removed", old.Method, old.Path,
                $"Request property was removed: {property}", "Continue accepting the older request shape or introduce a new major version.", property));
        }
        foreach (var property in old.ResponseProperties.Except(current.ResponseProperties, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-003", "breaking", "response-property-removed", old.Method, old.Path,
                $"Response property was removed: {property}", "Preserve the property during the compatibility window or introduce a new major version.", property));
        }
        foreach (var property in current.ResponseProperties.Except(old.ResponseProperties, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-011", "potentially-breaking", "response-property-added", old.Method, old.Path,
                $"Response property was added: {property}", "Confirm strict and generated consumers tolerate additional response fields.", property));
        }
        CompareTypes(old.RequestTypes, current.RequestTypes, "request", old, findings);
        CompareTypes(old.ResponseTypes, current.ResponseTypes, "response", old, findings);
        foreach (var parameter in current.RequiredParameters.Except(old.RequiredParameters, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-004", "breaking", "required-parameter-added", old.Method, old.Path,
                $"Required parameter/header was added: {parameter}", "Make it optional or introduce a new major version and migrate consumers.", parameter));
        }
        foreach (var status in old.ResponseStatuses.Except(current.ResponseStatuses, StringComparer.Ordinal))
        {
            findings.Add(Finding("CIS-API-DIFF-005", "potentially-breaking", "response-status-removed", old.Method, old.Path,
                $"Documented response status was removed: {status}", "Review consumer error handling and Problem Details compatibility.", status));
        }
        if (!old.Security.SetEquals(current.Security))
        {
            findings.Add(Finding("CIS-API-DIFF-006", "breaking", "security-changed", old.Method, old.Path,
                "Authentication/security requirements changed.", "Review the authorization boundary and version/migrate affected consumers.", old.Key));
        }
        if (old.NullableResponseProperties.Except(current.NullableResponseProperties, StringComparer.Ordinal).Any())
        {
            findings.Add(Finding("CIS-API-DIFF-007", "breaking", "response-nullability-narrowed", old.Method, old.Path,
                "One or more response properties changed from nullable to non-nullable.", "Preserve response compatibility or introduce a new major version.", old.Key));
        }
        foreach (var (name, oldValues) in old.Enums)
        {
            if (!current.Enums.TryGetValue(name, out var newValues)) continue;
            foreach (var removed in oldValues.Except(newValues, StringComparer.Ordinal))
            {
                findings.Add(Finding("CIS-API-DIFF-008", "breaking", "enum-value-removed", old.Method, old.Path,
                    $"Enum value was removed from {name}: {removed}", "Preserve the value or introduce a new major version.", $"{name}:{removed}"));
            }
            foreach (var added in newValues.Except(oldValues, StringComparer.Ordinal))
            {
                findings.Add(Finding("CIS-API-DIFF-009", "potentially-breaking", "response-enum-expanded", old.Method, old.Path,
                    $"Enum value was added to {name}: {added}", "Confirm all consumers tolerate unknown enum values.", $"{name}:{added}"));
            }
        }
    }

    private static Dictionary<string, OperationShape> Operations(JsonElement root)
    {
        var result = new Dictionary<string, OperationShape>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("paths", out var paths)) return result;
        foreach (var route in paths.EnumerateObject())
        {
            foreach (var method in route.Value.EnumerateObject().Where(item => Methods.Contains(item.Name)))
            {
                var key = method.Name.ToUpperInvariant() + " " + route.Name;
                var requestSchema = ResolveRequestSchema(root, method.Value);
                var responseSchemas = ResolveResponseSchemas(root, method.Value).ToArray();
                var parameters = Parameters(route.Value).Concat(Parameters(method.Value)).ToArray();
                var security = Security(method.Value, root);
                result[key] = new OperationShape(key, method.Name.ToUpperInvariant(), route.Name,
                    Required(requestSchema), Properties(requestSchema is null ? [] : [requestSchema.Value]), Properties(responseSchemas),
                    Types(requestSchema is null ? [] : [requestSchema.Value]), Types(responseSchemas), NullableProperties(responseSchemas),
                    parameters.Where(item => item.Required).Select(item => item.Location + ":" + item.Name).ToHashSet(StringComparer.Ordinal),
                    ResponseStatuses(method.Value), security, Enums(requestSchema, responseSchemas));
            }
        }
        return result;
    }

    private static JsonElement? ResolveRequestSchema(JsonElement root, JsonElement operation)
    {
        if (!operation.TryGetProperty("requestBody", out var body)) return null;
        body = Dereference(root, body);
        if (!body.TryGetProperty("content", out var content)) return null;
        foreach (var media in content.EnumerateObject()) if (media.Value.TryGetProperty("schema", out var schema)) return Dereference(root, schema);
        return null;
    }

    private static IEnumerable<JsonElement> ResolveResponseSchemas(JsonElement root, JsonElement operation)
    {
        if (!operation.TryGetProperty("responses", out var responses)) yield break;
        foreach (var response in responses.EnumerateObject().Where(item => item.Name.StartsWith('2')))
        {
            var value = Dereference(root, response.Value);
            if (!value.TryGetProperty("content", out var content)) continue;
            foreach (var media in content.EnumerateObject()) if (media.Value.TryGetProperty("schema", out var schema)) yield return Dereference(root, schema);
        }
    }

    private static JsonElement Dereference(JsonElement root, JsonElement element)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference))
        {
            var value = reference.GetString();
            if (value is null || !value.StartsWith("#/", StringComparison.Ordinal) || !visited.Add(value)) break;
            var current = root;
            foreach (var part in value[2..].Split('/')) current = current.GetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            element = current;
        }
        return element;
    }

    private static HashSet<string> Required(JsonElement? schema)
        => schema is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static HashSet<string> Properties(IEnumerable<JsonElement> schemas)
        => schemas.Where(schema => schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out _))
            .SelectMany(schema => schema.GetProperty("properties").EnumerateObject().Select(item => item.Name)).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> Types(IEnumerable<JsonElement> schemas)
        => schemas.Where(schema => schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out _))
            .SelectMany(schema => schema.GetProperty("properties").EnumerateObject())
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => TypeOf(group.First().Value), StringComparer.Ordinal);

    private static string TypeOf(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            return type.ValueKind == JsonValueKind.Array
                ? string.Join('|', type.EnumerateArray().Select(item => item.GetString()).Where(item => item is not null).Order(StringComparer.Ordinal))
                : type.GetString() ?? "unknown";
        }
        return schema.TryGetProperty("$ref", out var reference) ? reference.GetString() ?? "reference" : "unknown";
    }

    private static void CompareTypes(IReadOnlyDictionary<string, string> baseline, IReadOnlyDictionary<string, string> current,
        string surface, OperationShape operation, ICollection<ApiCompatibilityFinding> findings)
    {
        foreach (var (name, oldType) in baseline)
        {
            if (current.TryGetValue(name, out var newType) && !string.Equals(oldType, newType, StringComparison.Ordinal))
            {
                findings.Add(Finding("CIS-API-DIFF-012", "breaking", $"{surface}-type-changed", operation.Method, operation.Path,
                    $"{surface} property type changed for {name}: {oldType} -> {newType}",
                    "Preserve the original type or introduce a new major version and migrate consumers.", name));
            }
        }
    }

    private static HashSet<string> NullableProperties(IEnumerable<JsonElement> schemas)
        => schemas.Where(schema => schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out _))
            .SelectMany(schema => schema.GetProperty("properties").EnumerateObject())
            .Where(item => item.Value.TryGetProperty("nullable", out var nullable) && nullable.ValueKind == JsonValueKind.True
                || item.Value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(entry => entry.GetString() == "null"))
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string Name, string Location, bool Required)> Parameters(JsonElement container)
    {
        if (!container.TryGetProperty("parameters", out var parameters)) yield break;
        foreach (var parameter in parameters.EnumerateArray())
        {
            yield return (parameter.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                parameter.TryGetProperty("in", out var location) ? location.GetString() ?? "" : "",
                parameter.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True);
        }
    }

    private static HashSet<string> ResponseStatuses(JsonElement operation)
        => operation.TryGetProperty("responses", out var responses) ? responses.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal) : [];

    private static HashSet<string> Security(JsonElement operation, JsonElement root)
    {
        var source = operation.TryGetProperty("security", out var local) ? local : root.TryGetProperty("security", out var global) ? global : default;
        if (source.ValueKind != JsonValueKind.Array) return [];
        return source.EnumerateArray().SelectMany(item => item.EnumerateObject()).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, HashSet<string>> Enums(JsonElement? request, IEnumerable<JsonElement> responses)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var schema in (request is null ? [] : new[] { request.Value }).Concat(responses))
        {
            if (!schema.TryGetProperty("properties", out var properties)) continue;
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.TryGetProperty("enum", out var values)) result[property.Name] = values.EnumerateArray().Select(item => item.ToString()).ToHashSet(StringComparer.Ordinal);
            }
        }
        return result;
    }

    private ApiGovernanceProfile ReadProfile(CisRepositoryContext context)
        => new ApiGovernanceService(contextResolver).ReadProfile(context);
    private static string? Select(string? option, IReadOnlyList<string> configured) => string.IsNullOrWhiteSpace(option) ? configured.FirstOrDefault() : option;
    private static string? Resolve(string root, string path) { try { return CisPathSafety.TryResolveUnderRoot(root, path, out var value) && !CisPathSafety.ContainsReparsePoint(root, value) ? value : null; } catch { return null; } }
    private static ApiCompatibilityFinding Finding(string code, string severity, string classification, string method, string path, string message, string remediation, params string[] evidence)
        => new(code, severity, classification, method, path, message, remediation, evidence);

    private sealed record OperationShape(string Key, string Method, string Path, HashSet<string> RequiredRequest,
        HashSet<string> RequestProperties, HashSet<string> ResponseProperties,
        Dictionary<string, string> RequestTypes, Dictionary<string, string> ResponseTypes, HashSet<string> NullableResponseProperties,
        HashSet<string> RequiredParameters, HashSet<string> ResponseStatuses, HashSet<string> Security,
        Dictionary<string, HashSet<string>> Enums);
}
