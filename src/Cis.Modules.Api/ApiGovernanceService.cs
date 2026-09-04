using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

namespace Cis.Modules.Api;

public sealed class ApiGovernanceService(ICisRepositoryContextResolver contextResolver)
{
    public const string StatePath = ".cis/local/api/operations.json";
    public const string ManifestPath = ".cis/local/api/manifest.json";
    public const string DiagnosticsPath = ".cis/local/api/diagnostics.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cis", ".codex-tmp", ".git", ".idea", ".vs", "bin", "node_modules", "obj",
    };

    private static readonly Regex MinimalApi = new(
        "\\bMap(?<method>Get|Post|Put|Patch|Delete)\\s*\\(\\s*\"(?<path>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControllerRoute = new(
        "\\[Route\\s*\\(\\s*\"(?<path>[^\"]+)\"\\s*\\)\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControllerAction = new(
        "\\[(?:Http)?(?<method>Get|Post|Put|Patch|Delete)(?:Attribute)?(?:\\s*\\(\\s*\"(?<path>[^\"]*)\"[^)]*\\))?\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControllerClass = new(
        "\\bclass\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*Controller)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RouteHandler = new(
        "\\bexport\\s+(?:async\\s+)?function\\s+(?<method>GET|POST|PUT|PATCH|DELETE)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExpressRoute = new(
        "\\b(?:app|router)\\s*\\.\\s*(?<method>get|post|put|patch|delete|options|head)\\s*\\(\\s*['\"`](?<path>[^'\"`]+)['\"`]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Permission = new(
        "(?:RequireAuthorization\\s*\\(\\s*\"|Authorize\\s*\\(\\s*(?:Policy\\s*=\\s*)?\")(?<value>[^\"]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex RateLimit = new(
        "(?:RequireRateLimiting|EnableRateLimiting)\\s*\\(\\s*\"(?<value>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CachePolicy = new(
        "(?:CacheOutput|OutputCache)\\s*\\(?(?:PolicyName\\s*=\\s*)?\"?(?<value>[A-Za-z0-9_.:-]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DirectPersistence = new(
        "\\b(?:[A-Za-z0-9_]*DbContext|DbSet<|I[A-Za-z0-9_]*Repository|[A-Za-z0-9_]*Repository|Database\\.|ExecuteSql|FromSql)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ApiDiscoveryResult Discover(string repositoryPath, IReadOnlyList<string>? openApiOverrides = null)
    {
        var resolution = contextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return new ApiDiscoveryResult("invalid", null, null, 0, 0, 0, 0,
                resolution.Errors.Select(error => Diagnostic("CIS-API-REPO-001", "error", "API-DICT-01", error,
                    "Run `cis repo init --root <docs-root>` and then `cis repo doctor`.")).ToArray(),
                [], false, false);
        }

        var context = resolution.Context;
        var profile = ReadProfile(context);
        var inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dictionaryPath = Path.Combine(context.DocumentationPath, "references", "api-dictionary.md");
        var dictionary = ReadDictionary(context, dictionaryPath, inputs);
        var source = DiscoverSource(context, inputs);
        var openApiPaths = (openApiOverrides is { Count: > 0 } ? openApiOverrides : profile.OpenApiCurrent)
            .Where(path => !IsUnknown(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var openApiDiagnostics = new List<ApiDiagnostic>();
        var openApi = ReadOpenApi(context, openApiPaths, inputs, openApiDiagnostics);
        var operations = AssociateTests(context, Merge(dictionary, source, openApi), inputs);
        var diagnostics = new List<ApiDiagnostic>(openApiDiagnostics);
        foreach (var operation in operations.Where(item => item.SourceDiscovered && !item.DictionaryDeclared))
        {
            diagnostics.Add(Diagnostic("CIS-API-DICT-001", "warning", "API-DICT-01",
                $"Source operation is missing from the governed API dictionary: {operation.Method} {operation.Path}",
                "Add a stable API dictionary row in the same change as the endpoint.", operation.SourceLocations));
        }

        var inputDigests = inputs.Order(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .Select(path => new ApiInputDigest(Relative(context.RepositoryPath, path), Sha256(File.ReadAllBytes(path))))
                .ToArray();
        var generatedAt = inputs.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.UnixEpoch).Max()
            .ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var manifest = new ApiDiscoveryManifest(1, context.RepositoryId, generatedAt, inputDigests);
        var applied = WriteState(context.RepositoryPath, operations, diagnostics, manifest);
        return new ApiDiscoveryResult(applied ? "updated" : "unchanged", context.RepositoryPath, StatePath,
            operations.Count, operations.Count(item => item.SourceDiscovered),
            operations.Count(item => item.DictionaryDeclared), operations.Count(item => item.OpenApiDeclared),
            diagnostics, operations, applied, true);
    }

    public ApiInventoryResult Inventory(string repositoryPath, string? exposure = null, string? module = null)
    {
        var resolution = contextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return new ApiInventoryResult("invalid", null, [], resolution.Errors, false, false);
        }

        var path = Path.Combine(resolution.Context.RepositoryPath, StatePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return new ApiInventoryResult("missing", resolution.Context.RepositoryPath, [],
                ["API discovery state is unavailable. Run `cis api discover`."], true, false);
        }

        try
        {
            var items = JsonSerializer.Deserialize<ApiOperation[]>(File.ReadAllText(path), JsonOptions) ?? [];
            var filtered = items
                .Where(item => string.IsNullOrWhiteSpace(exposure) || string.Equals(item.Exposure, exposure, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(module) || string.Equals(item.Module, module, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ThenBy(item => item.Method, StringComparer.Ordinal)
                .ToArray();
            return new ApiInventoryResult("available", resolution.Context.RepositoryPath, filtered, [], true, true);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ApiInventoryResult("invalid", resolution.Context.RepositoryPath, [],
                [$"API discovery state is invalid: {exception.Message}"], true, false);
        }
    }

    public ApiValidationResult Validate(string repositoryPath, bool strict, bool refresh = true)
    {
        var discovery = refresh ? Discover(repositoryPath) : null;
        if (discovery is not null && !discovery.RepositoryConfigurationValid)
        {
            return Validation("invalid", null, [], discovery.Diagnostics, false, strict);
        }

        var resolution = contextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return Validation("invalid", null, [], resolution.Errors.Select(error =>
                Diagnostic("CIS-API-REPO-001", "error", "API-DICT-01", error,
                    "Initialize or repair the CIS repository configuration.")).ToArray(), false, strict);
        }

        IReadOnlyList<ApiOperation> operations;
        if (discovery is not null)
        {
            operations = discovery.Items;
        }
        else
        {
            var inventory = Inventory(repositoryPath);
            if (!inventory.StateAvailable)
            {
                return Validation("invalid", resolution.Context.RepositoryPath, [],
                    [Diagnostic("CIS-API-STATE-001", "error", "API-DICT-01",
                        "Normalized API operation state is unavailable; --no-refresh cannot validate an undiscovered repository.",
                        "Run `cis api discover --repo <path>` or omit --no-refresh before validating.")],
                    true, strict);
            }
            if (inventory.Errors.Count > 0)
            {
                return Validation("invalid", resolution.Context.RepositoryPath, [], inventory.Errors.Select(error =>
                    Diagnostic("CIS-API-STATE-002", "error", "API-DICT-01", error,
                        "Repair or regenerate `.cis/local/api/operations.json` with `cis api discover`.")).ToArray(),
                    true, strict);
            }
            operations = inventory.Items;
        }
        var diagnostics = new List<ApiDiagnostic>(discovery?.Diagnostics ?? []);
        var context = resolution.Context;
        var permissions = ReadIdentitySet(Path.Combine(context.DocumentationPath, "references", "permissions-dictionary.md"), "Permission code");
        var problems = ReadIdentitySet(Path.Combine(context.DocumentationPath, "references", "problem-details-catalogue.md"), "Problem ID");
        var profile = ReadProfile(context);
        var componentIds = ReadComponentIds(Path.Combine(context.DocumentationPath, "references", "repository-profile.md"));
        var supported = profile.SupportedVersions
            .Select(item => $"{item.Module}:{NormalizeMajor(item.MajorVersion)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (profile.DeprecationWindowDays <= 0)
        {
            diagnostics.Add(Diagnostic("CIS-API-PROFILE-001", "error", "API-VER-02", "The API deprecation window must be greater than zero days.",
                "Record a positive repository-approved compatibility window in the API governance profile."));
        }
        foreach (var version in profile.SupportedVersions.Where(item => item.Status.Equals("Deprecated", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsUnknown(version.Sunset) || IsUnknown(version.Consumers))
            {
                diagnostics.Add(Diagnostic("CIS-API-PROFILE-002", "error", "API-VER-03", $"Deprecated supported version lacks sunset or consumer disposition: {version.Module} {version.MajorVersion}",
                    "Record the sunset date, migration guidance, known consumers, and their disposition.", [version.Evidence]));
            }
        }

        foreach (var operation in operations)
        {
            var identity = $"{operation.Method} {operation.Path}";
            var evidence = operation.SourceLocations.Concat(SplitPathList(operation.Evidence)).ToArray();
            if (!operation.DictionaryDeclared)
            {
                diagnostics.Add(Diagnostic("CIS-API-DICT-002", "error", "API-DICT-01", $"Operation has no canonical API dictionary row: {identity}",
                    "Add and review a stable API dictionary row.", evidence));
                continue;
            }

            if (!ValidExposures.Contains(operation.Exposure))
            {
                diagnostics.Add(Diagnostic("CIS-API-EXP-001", "error", "API-EXP-01", $"Operation has no valid exposure class: {identity}",
                    "Set Exposure to public, identity-protocol, customer, backoffice, internal/service, webhook, or probe.", evidence));
            }

            if (!operation.SourceDiscovered && IsImplemented(operation.Status))
            {
                diagnostics.Add(Diagnostic("CIS-API-SOURCE-001", "error", "API-DICT-01", $"Implemented operation was not discovered in source: {identity}",
                    "Correct its evidence/route or restore the implementation; use Planned or Draft until implemented.", evidence));
            }

            if (IsExternal(operation.Exposure) && !operation.OpenApiDeclared && IsImplemented(operation.Status))
            {
                diagnostics.Add(Diagnostic("CIS-API-OAS-001", "error", "API-OAS-01", $"Externally reachable operation is absent from configured OpenAPI: {identity}",
                    "Export the operation into the governed current OpenAPI document.", evidence));
            }

            if (RequiresAuthentication(operation.Exposure) && IsUnknown(operation.Permission))
            {
                diagnostics.Add(Diagnostic("CIS-API-AUTHZ-001", "error", "API-AUTHZ-01", $"Protected operation has no permission or authentication decision: {identity}",
                    "Record its explicit policy/capability or authentication decision.", evidence));
            }
            else
            {
                foreach (var permission in SplitIdentities(operation.Permission, "PERM-"))
                {
                    if (!permissions.Contains(permission))
                    {
                        diagnostics.Add(Diagnostic("CIS-API-AUTHZ-002", "error", "API-AUTHZ-01", $"Permission is absent from the permissions dictionary: {permission} ({identity})",
                            "Add the permission semantic definition and role/capability mapping in the same change.", evidence));
                    }
                }
            }
            if (RequiresAuthentication(operation.Exposure) && IsUnknown(operation.TrustedScopeSource))
            {
                diagnostics.Add(Diagnostic("CIS-API-SCOPE-001", "error", "API-SCOPE-01", $"Protected operation has no trusted scope source: {identity}",
                    "Record the server-side identity claim/context used for customer, tenant, or service scope.", evidence));
            }

            foreach (var consumer in SplitConsumers(operation.Consumer))
            {
                if (componentIds.Count > 0 && !IsApprovedConsumer(consumer, componentIds))
                {
                    diagnostics.Add(Diagnostic("CIS-API-CONS-001", "error", "API-DICT-01", $"Consumer is not a known repository component or approved external class: {consumer} ({identity})",
                        "Correct the component identity or record an approved external consumer class and migration owner.", evidence));
                }
            }
            foreach (var problem in SplitIdentities(operation.ErrorContract, "PROB-"))
            {
                if (!problems.Contains(problem))
                {
                    diagnostics.Add(Diagnostic("CIS-API-ERR-001", "error", "API-ERR-02", $"Problem identity is absent from the Problem Details catalogue: {problem} ({identity})",
                        "Add its stable meaning, status, frontend handling, and sensitivity record.", evidence));
                }
            }

            if (IsUnknown(operation.RateLimitPolicy))
            {
                diagnostics.Add(Diagnostic("CIS-API-RATE-001", "error", "API-RATE-01", $"Operation has no named rate-limit policy: {identity}",
                    "Assign a named policy and document 429/retry behavior.", evidence));
            }

            if (string.Equals(operation.Exposure, "public", StringComparison.OrdinalIgnoreCase))
            {
                ValidatePublic(operation, identity, evidence, diagnostics);
            }
            else if (string.Equals(operation.Exposure, "identity-protocol", StringComparison.OrdinalIgnoreCase))
            {
                ValidateIdentityProtocol(operation, identity, evidence, diagnostics);
            }

            if (IsVerified(operation.Status))
            {
                foreach (var missing in RequiredVerifiedFields(operation))
                {
                    diagnostics.Add(Diagnostic("CIS-API-LIFE-001", "error", "API-DICT-01", $"Verified operation retains unknown governance field '{missing}': {identity}",
                        "Complete the field with reviewed evidence or return the operation to Draft/Planned.", evidence));
                }
                if (!ExistingTestPaths(context.RepositoryPath, operation.KnownTests).Any())
                {
                    diagnostics.Add(Diagnostic("CIS-API-TEST-001", "error", "API-DICT-01", $"Verified operation has no existing known contract test: {identity}",
                        "Record at least one repository-relative test path that verifies the API identity and positive/negative contract.", evidence));
                }
            }
            if (operation.Status.Equals("Deprecated", StringComparison.OrdinalIgnoreCase)
                && !ContainsMigrationDisposition(operation.Notes))
            {
                diagnostics.Add(Diagnostic("CIS-API-LIFE-002", "error", "API-VER-03", $"Deprecated operation lacks migration, replacement, or sunset disposition: {identity}",
                    "Record the replacement, migration guidance, sunset date/window, and known-consumer disposition.", evidence));
            }

            var major = NormalizeMajor(operation.Version);
            if (!IsUnknown(operation.Module) && !IsUnknown(major) && !supported.Contains($"{operation.Module}:{major}"))
            {
                diagnostics.Add(Diagnostic("CIS-API-VER-001", "warning", "API-VER-01", $"Operation version is absent from the supported-version table: {operation.Module} {major} ({identity})",
                    "Register the supported major version, lifecycle, sunset, consumers, and evidence in the API governance profile.", evidence));
            }
        }

        diagnostics = diagnostics
            .GroupBy(item => $"{item.Code}|{item.Message}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToList();
        var result = Validation(diagnostics.Any(item => item.Severity == "error") ? "invalid" : "valid",
            context.RepositoryPath, operations, diagnostics, true, strict);
        WriteAtomic(Path.Combine(context.RepositoryPath, DiagnosticsPath.Replace('/', Path.DirectorySeparatorChar)),
            JsonSerializer.Serialize(diagnostics, JsonOptions) + Environment.NewLine);
        return result;
    }

    public ApiGovernanceProfile ReadProfile(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "api-governance-profile.md");
        if (!File.Exists(path))
        {
            return DefaultProfile;
        }

        var content = File.ReadAllText(path);
        var tables = MarkdownTables(content);
        var settings = tables.FirstOrDefault(table => table.Headers.Contains("Setting", StringComparer.OrdinalIgnoreCase));
        var values = settings?.Rows.ToDictionary(row => Value(row, "Setting"), row => Value(row, "Value"), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var documents = tables.FirstOrDefault(table => table.Headers.Contains("Document role", StringComparer.OrdinalIgnoreCase));
        var current = documents?.Rows.Where(row => Value(row, "Document role").Equals("current", StringComparison.OrdinalIgnoreCase)
                && IsImplemented(Value(row, "Status")))
            .Select(row => Value(row, "Path")).Where(pathValue => !IsUnknown(pathValue)).ToArray() ?? [];
        var baselines = documents?.Rows.Where(row => Value(row, "Document role").Equals("baseline", StringComparison.OrdinalIgnoreCase)
                && IsImplemented(Value(row, "Status")))
            .Select(row => Value(row, "Path")).Where(pathValue => !IsUnknown(pathValue)).ToArray() ?? [];
        var versionTable = tables.FirstOrDefault(table => table.Headers.Contains("Major version", StringComparer.OrdinalIgnoreCase));
        var versions = versionTable?.Rows.Select(row => new ApiSupportedVersion(
            Value(row, "Module"), Value(row, "Major version"), Value(row, "Status"),
            Value(row, "Sunset"), Value(row, "Consumers"), Value(row, "Evidence"))).ToArray() ?? [];
        return new ApiGovernanceProfile(
            values.GetValueOrDefault("route-template", DefaultProfile.RouteTemplate),
            values.GetValueOrDefault("compatibility-mode", DefaultProfile.CompatibilityMode),
            int.TryParse(values.GetValueOrDefault("deprecation-window-days"), out var days) ? days : DefaultProfile.DeprecationWindowDays,
            values.GetValueOrDefault("production-openapi-exposure", DefaultProfile.ProductionOpenApiExposure),
            values.GetValueOrDefault("correlation-header", DefaultProfile.CorrelationHeader),
            values.GetValueOrDefault("client-header", DefaultProfile.ClientHeader),
            current, baselines, versions);
    }

    private static readonly ApiGovernanceProfile DefaultProfile = new(
        "/{module}/v{major}/{resource-path}", "forward-transitive", 180, "restricted",
        "repository-defined", "repository-defined", [], [], []);

    private static readonly HashSet<string> ValidExposures = new(StringComparer.OrdinalIgnoreCase)
    {
        "public", "identity-protocol", "customer", "backoffice", "internal/service", "webhook", "probe",
    };

    private static void ValidatePublic(ApiOperation operation, string identity, IReadOnlyList<string> evidence, ICollection<ApiDiagnostic> diagnostics)
    {
        if (IsUnknown(operation.CachePolicy))
        {
            diagnostics.Add(Diagnostic("CIS-API-PUBLIC-001", "error", "API-PUB-01", $"Public operation has no governed cache policy: {identity}",
                "Record cache key/Vary, TTL/freshness, invalidation, headers, stampede, miss, and failure behavior.", evidence));
        }
        if (IsUnknown(operation.DataAccessPath) || !ContainsIndirectCacheBoundary(operation.DataAccessPath))
        {
            diagnostics.Add(Diagnostic("CIS-API-PUBLIC-002", "error", "API-PUB-01", $"Public operation does not identify an indirect cache-population boundary: {identity}",
                "Record a background/precomputed cache population path; the endpoint must not load persistence on a miss.", evidence));
        }
        if (operation.DirectPersistenceEvidence)
        {
            diagnostics.Add(Diagnostic("CIS-API-PUBLIC-003", "error", "API-PUB-01", $"Public endpoint source contains direct persistence evidence: {identity}",
                "Move database/repository access behind an asynchronous or precomputed cache-population boundary.", evidence));
        }
    }

    private static void ValidateIdentityProtocol(ApiOperation operation, string identity, IReadOnlyList<string> evidence, ICollection<ApiDiagnostic> diagnostics)
    {
        if (IsUnknown(operation.CachePolicy)
            || !operation.CachePolicy.Contains("no-store", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Diagnostic("CIS-API-IDP-001", "error", "API-EXP-01", $"Identity-protocol operation is not explicitly no-store: {identity}",
                "Record a no-store response policy for credential-establishment and session protocol traffic.", evidence));
        }
        if (operation.DirectPersistenceEvidence)
        {
            diagnostics.Add(Diagnostic("CIS-API-IDP-002", "error", "API-EXP-01", $"Identity-protocol transport contains direct persistence evidence: {identity}",
                "Move persistence behind an application service or SDK boundary; transport adapters must not call a repository or database directly.", evidence));
        }
    }

    private static IReadOnlyList<string> RequiredVerifiedFields(ApiOperation operation)
    {
        var values = new Dictionary<string, string>
        {
            ["Version"] = operation.Version, ["Module"] = operation.Module, ["Host"] = operation.Host,
            ["Exposure"] = operation.Exposure, ["Consumer"] = operation.Consumer,
            ["Request contract"] = operation.RequestContract, ["Response contract"] = operation.ResponseContract,
            ["Permission / auth"] = operation.Permission, ["OpenAPI operation"] = operation.OpenApiOperation,
            ["Trusted scope source"] = operation.TrustedScopeSource,
            ["Rate-limit policy"] = operation.RateLimitPolicy,
            ["Idempotency / concurrency"] = operation.IdempotencyConcurrency,
            ["Collection semantics"] = operation.CollectionSemantics,
            ["Error contract"] = operation.ErrorContract,
            ["Evidence"] = operation.Evidence, ["Known tests"] = operation.KnownTests,
        };
        return values.Where(pair => IsUnknown(pair.Value)).Select(pair => pair.Key).ToArray();
    }

    private static ApiValidationResult Validation(string status, string? path, IReadOnlyList<ApiOperation> operations,
        IReadOnlyList<ApiDiagnostic> diagnostics, bool configured, bool strict)
        => new(status, path, operations.Count, diagnostics.Count(item => item.Severity == "error"),
            diagnostics.Count(item => item.Severity == "warning"), diagnostics, configured, strict);

    private static List<ApiOperation> ReadDictionary(CisRepositoryContext context, string path, ISet<string> inputs)
    {
        if (!File.Exists(path)) return [];
        inputs.Add(path);
        var table = MarkdownTables(File.ReadAllText(path)).FirstOrDefault(item => item.Headers.Contains("API ID", StringComparer.OrdinalIgnoreCase));
        if (table is null) return [];
        return table.Rows.Where(row => !IsUnknown(Value(row, "API ID"))).Select(row => new ApiOperation(
            Value(row, "API ID"), Value(row, "Version"), Value(row, "Method").ToUpperInvariant(), Value(row, "Path"),
            Value(row, "Module"), Value(row, "Host"), Value(row, "Exposure"), Value(row, "Consumer"),
            Value(row, "Request contract"), Value(row, "Response contract"), Value(row, "Permission / auth"),
            Value(row, "Trusted scope source"), Value(row, "OpenAPI operation"), Value(row, "Rate-limit policy"), Value(row, "Idempotency / concurrency"),
            Value(row, "Collection semantics"),
            Value(row, "Error contract"), Value(row, "Cache policy"), Value(row, "Data access path"), Value(row, "Status"),
            Value(row, "Evidence"), Value(row, "Known tests"), Value(row, "Notes"), EvidencePaths(context.RepositoryPath, Value(row, "Evidence")),
            null, false, true, false, false)).ToList();
    }

    private static List<ApiOperation> DiscoverSource(CisRepositoryContext context, ISet<string> inputs)
    {
        var result = new List<ApiOperation>();
        foreach (var path in EnumerateFiles(context.RepositoryPath).Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !IsTestSource(path)))
        {
            inputs.Add(path);
            var content = SafeRead(path);
            var relative = Relative(context.RepositoryPath, path);
            result.AddRange(DiscoverCSharp(content, relative));
        }
        foreach (var path in EnumerateFiles(context.RepositoryPath).Where(IsNextRouteHandler))
        {
            inputs.Add(path);
            var content = SafeRead(path);
            var relative = Relative(context.RepositoryPath, path);
            var route = NextRoute(relative);
            foreach (Match match in RouteHandler.Matches(content))
            {
                result.Add(SourceOperation(match.Groups["method"].Value, route, relative, Snippet(content, match.Index, 900)));
            }
        }
        foreach (var path in EnumerateFiles(context.RepositoryPath).Where(IsExpressSource))
        {
            inputs.Add(path);
            var content = SafeRead(path);
            var relative = Relative(context.RepositoryPath, path);
            foreach (Match match in ExpressRoute.Matches(content))
            {
                result.Add(SourceOperation(match.Groups["method"].Value, match.Groups["path"].Value,
                    relative, Snippet(content, match.Index, 900)));
            }
        }
        return result;
    }

    private static IEnumerable<ApiOperation> DiscoverCSharp(string content, string relative)
    {
        var root = CSharpSyntaxTree.ParseText(content).GetRoot();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => string.Empty,
            };
            if (!TryMapMethod(name, out var method)
                || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax literal
                || literal.Token.Value is not string route)
            {
                continue;
            }
            var snippet = invocation.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault()?.ToFullString()
                ?? invocation.Parent?.ToFullString() ?? invocation.ToFullString();
            yield return SourceOperation(method, route, relative, snippet);
        }

        foreach (var controller in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                     .Where(item => item.Identifier.ValueText.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)))
        {
            var prefix = AttributeString(controller.AttributeLists, "Route") ?? string.Empty;
            prefix = prefix.Replace("[controller]", controller.Identifier.ValueText[..^10], StringComparison.OrdinalIgnoreCase);
            foreach (var action in controller.Members.OfType<MethodDeclarationSyntax>())
            {
                foreach (var attribute in action.AttributeLists.SelectMany(list => list.Attributes))
                {
                    var attributeName = attribute.Name.ToString().Split('.').Last().Replace("Attribute", string.Empty, StringComparison.OrdinalIgnoreCase);
                    if (!TryControllerMethod(attributeName, out var method)) continue;
                    var suffix = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                        && literal.Token.Value is string value ? value : string.Empty;
                    yield return SourceOperation(method, CombineRoute(prefix, suffix), relative,
                        controller.AttributeLists.ToFullString() + action.ToFullString());
                }
            }
        }
    }

    private static bool TryMapMethod(string name, out string method)
    {
        method = name.StartsWith("Map", StringComparison.Ordinal) ? name[3..].ToUpperInvariant() : string.Empty;
        return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE";
    }

    private static bool TryControllerMethod(string name, out string method)
    {
        var normalized = name.StartsWith("Http", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
        method = normalized.ToUpperInvariant();
        return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE";
    }

    private static string? AttributeString(SyntaxList<AttributeListSyntax> lists, string name)
        => lists.SelectMany(list => list.Attributes)
            .Where(attribute => attribute.Name.ToString().Split('.').Last().Replace("Attribute", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(literal => literal.Token.Value as string)
            .FirstOrDefault(value => value is not null);

    private static ApiOperation SourceOperation(string method, string path, string source, string snippet)
    {
        var exposure = snippet.Contains("AllowAnonymous", StringComparison.OrdinalIgnoreCase) ? "public" :
            snippet.Contains("Authorize", StringComparison.OrdinalIgnoreCase) || snippet.Contains("RequireAuthorization", StringComparison.OrdinalIgnoreCase)
                ? "customer" : "unclassified";
        var permission = Permission.Match(snippet).Groups["value"].Value;
        if (string.IsNullOrWhiteSpace(permission)
            && (snippet.Contains("Authorize", StringComparison.OrdinalIgnoreCase)
                || snippet.Contains("RequireAuthorization", StringComparison.OrdinalIgnoreCase)))
        {
            permission = "authenticated";
        }
        var rate = RateLimit.Match(snippet).Groups["value"].Value;
        var cache = CachePolicy.Match(snippet).Groups["value"].Value;
        return new ApiOperation("", "unknown", method.ToUpperInvariant(), NormalizePath(path), "unknown", "unknown", exposure,
            "unknown", "unknown", "unknown", EmptyUnknown(permission), "unknown", "unknown", EmptyUnknown(rate), "unknown", "unknown", "unknown",
            EmptyUnknown(cache), "unknown", "Draft", source, "unknown", "Discovered from source.", [source], null, true, false, false,
            DirectPersistence.IsMatch(snippet));
    }

    private static List<ApiOperation> ReadOpenApi(CisRepositoryContext context, IEnumerable<string> paths, ISet<string> inputs,
        ICollection<ApiDiagnostic> diagnostics)
    {
        var result = new List<ApiOperation>();
        foreach (var configured in paths)
        {
            var absolute = ResolveInside(context.RepositoryPath, configured);
            if (absolute is null || !File.Exists(absolute))
            {
                diagnostics.Add(Diagnostic("CIS-API-OAS-002", "error", "API-OAS-01", $"Configured OpenAPI document was not found: {configured}",
                    "Correct the API governance profile path or export the governed current document.", [configured]));
                continue;
            }
            inputs.Add(absolute);
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(absolute));
                ReadSdkOwnedSurfaces(context, absolute, document.RootElement, inputs, diagnostics, result);
                if (!document.RootElement.TryGetProperty("paths", out var pathObject)) continue;
                foreach (var route in pathObject.EnumerateObject())
                {
                    foreach (var method in route.Value.EnumerateObject().Where(item => HttpMethods.Contains(item.Name)))
                    {
                        var operationId = method.Value.TryGetProperty("operationId", out var id) ? id.GetString() ?? "unknown" : "unknown";
                        result.Add(new ApiOperation("", "unknown", method.Name.ToUpperInvariant(), NormalizePath(route.Name), "unknown", "unknown",
                            "unclassified", "unknown", "unknown", "unknown", "unknown", "unknown", operationId, "unknown", "unknown", "unknown", "unknown",
                            "unknown", "unknown", "Draft", Relative(context.RepositoryPath, absolute), "unknown", "Discovered from OpenAPI.", [],
                            Relative(context.RepositoryPath, absolute), false, false, true, false));
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic("CIS-API-OAS-003", "error", "API-OAS-01", $"OpenAPI document is invalid: {configured}: {exception.Message}",
                    "Export a valid OpenAPI 3.x JSON document.", [configured]));
            }
        }
        return result;
    }

    private static void ReadSdkOwnedSurfaces(
        CisRepositoryContext context,
        string openApiPath,
        JsonElement root,
        ISet<string> inputs,
        ICollection<ApiDiagnostic> diagnostics,
        ICollection<ApiOperation> operations)
    {
        if (!root.TryGetProperty("x-sdk-owned-surfaces", out var surfaces)) return;
        if (surfaces.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Diagnostic("CIS-API-OAS-004", "error", "API-OAS-01",
                "OpenAPI x-sdk-owned-surfaces must be an array.",
                "Record each dynamic SDK route family with pathPrefix, owner, recipes, and repository-relative implementationEvidence.",
                [Relative(context.RepositoryPath, openApiPath)]));
            return;
        }

        foreach (var surface in surfaces.EnumerateArray())
        {
            var prefix = surface.ValueKind == JsonValueKind.Object && surface.TryGetProperty("pathPrefix", out var prefixNode)
                ? prefixNode.GetString() : null;
            var owner = surface.ValueKind == JsonValueKind.Object && surface.TryGetProperty("owner", out var ownerNode)
                ? ownerNode.GetString() : null;
            var recipesValid = surface.ValueKind == JsonValueKind.Object
                && surface.TryGetProperty("recipes", out var recipes)
                && recipes.ValueKind == JsonValueKind.Array
                && recipes.GetArrayLength() > 0;
            var evidence = surface.ValueKind == JsonValueKind.Object
                && surface.TryGetProperty("implementationEvidence", out var evidenceNode)
                && evidenceNode.ValueKind == JsonValueKind.Array
                    ? evidenceNode.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToArray()
                    : [];
            var resolvedEvidence = evidence.Select(item => ResolveInside(context.RepositoryPath, item)).ToArray();
            if (string.IsNullOrWhiteSpace(prefix) || !prefix.StartsWith("/", StringComparison.Ordinal)
                || prefix.Contains('*') || string.IsNullOrWhiteSpace(owner)
                || !recipesValid || evidence.Length == 0 || resolvedEvidence.Any(item => item is null || !File.Exists(item)))
            {
                diagnostics.Add(Diagnostic("CIS-API-OAS-004", "error", "API-OAS-01",
                    $"SDK-owned OpenAPI surface is incomplete or has missing implementation evidence: {prefix ?? "unknown"}",
                    "Record a normalized pathPrefix, version-pinned owner, non-empty recipes, and existing repository-relative implementationEvidence.",
                    [Relative(context.RepositoryPath, openApiPath), .. evidence]));
                continue;
            }

            foreach (var path in resolvedEvidence.OfType<string>()) inputs.Add(path);
            var relativeOpenApi = Relative(context.RepositoryPath, openApiPath);
            operations.Add(new ApiOperation("", "unknown", "ANY", $"{NormalizePath(prefix).TrimEnd('/')}/*", "unknown", "unknown",
                "unclassified", "unknown", "unknown", "unknown", "unknown", "unknown", $"SDK-owned:{owner}", "unknown", "unknown",
                "unknown", "unknown", "unknown", "unknown", "Draft", relativeOpenApi, "unknown", "Discovered from x-sdk-owned-surfaces.",
                evidence, relativeOpenApi, true, false, true, false));
        }
    }

    private static List<ApiOperation> Merge(IReadOnlyList<ApiOperation> dictionary, IReadOnlyList<ApiOperation> source, IReadOnlyList<ApiOperation> openApi)
    {
        var keys = dictionary.Concat(source).Concat(openApi).Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var result = new List<ApiOperation>();
        foreach (var key in keys)
        {
            var canonical = dictionary.FirstOrDefault(item => Key(item).Equals(key, StringComparison.OrdinalIgnoreCase));
            var discovered = source.Where(item => Key(item).Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
            var published = openApi.FirstOrDefault(item => Key(item).Equals(key, StringComparison.OrdinalIgnoreCase));
            var basis = canonical ?? discovered.FirstOrDefault() ?? published!;
            result.Add(basis with
            {
                ApiId = IsUnknown(basis.ApiId) ? CreateApiId(basis.Method, basis.Path) : basis.ApiId,
                SourceLocations = basis.SourceLocations.Concat(discovered.SelectMany(item => item.SourceLocations))
                    .Concat(published?.SourceLocations ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                OpenApiDocument = published?.OpenApiDocument ?? basis.OpenApiDocument,
                OpenApiOperation = !IsUnknown(basis.OpenApiOperation) ? basis.OpenApiOperation : published?.OpenApiOperation ?? "unknown",
                Exposure = !IsUnknown(basis.Exposure) ? basis.Exposure : discovered.Select(item => item.Exposure).FirstOrDefault(value => !IsUnknown(value)) ?? "unclassified",
                Permission = !IsUnknown(basis.Permission) ? basis.Permission : discovered.Select(item => item.Permission).FirstOrDefault(value => !IsUnknown(value)) ?? "unknown",
                RateLimitPolicy = !IsUnknown(basis.RateLimitPolicy) ? basis.RateLimitPolicy : discovered.Select(item => item.RateLimitPolicy).FirstOrDefault(value => !IsUnknown(value)) ?? "unknown",
                CachePolicy = !IsUnknown(basis.CachePolicy) ? basis.CachePolicy : discovered.Select(item => item.CachePolicy).FirstOrDefault(value => !IsUnknown(value)) ?? "unknown",
                SourceDiscovered = discovered.Length > 0 || published?.SourceDiscovered == true,
                DictionaryDeclared = canonical is not null,
                OpenApiDeclared = published is not null,
                DirectPersistenceEvidence = discovered.Any(item => item.DirectPersistenceEvidence),
            });
        }
        return result;
    }

    private static List<ApiOperation> AssociateTests(CisRepositoryContext context, IReadOnlyList<ApiOperation> operations, ISet<string> inputs)
    {
        var tests = EnumerateFiles(context.RepositoryPath)
            .Where(IsTestSource)
            .Select(path => (Path: path, Relative: Relative(context.RepositoryPath, path), Content: SafeRead(path)))
            .ToArray();
        foreach (var test in tests) inputs.Add(test.Path);
        return operations.Select(operation =>
        {
            var declared = SplitPathList(operation.KnownTests);
            var discovered = tests.Where(test => test.Content.Contains(operation.ApiId, StringComparison.Ordinal)
                    || test.Content.Contains(operation.Path, StringComparison.Ordinal))
                .Select(test => test.Relative);
            return operation with { KnownTests = string.Join("; ", declared.Concat(discovered).Distinct(StringComparer.OrdinalIgnoreCase)) };
        }).ToList();
    }

    private static HashSet<string> ReadComponentIds(string path)
    {
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return File.ReadLines(path).Where(line => line.StartsWith("### ", StringComparison.Ordinal))
            .Select(line => line[4..].Trim()).Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExistingTestPaths(string root, string value)
        => SplitPathList(value).Where(item => ResolveInside(root, item) is { } path && File.Exists(path));

    private static IEnumerable<string> SplitPathList(string value)
        => (value ?? string.Empty).Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !IsUnknown(item));

    private static IEnumerable<string> SplitConsumers(string value)
        => (value ?? string.Empty).Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !IsUnknown(item));

    private static bool IsTestSource(string path)
    {
        var normalized = path.Replace('\\', '/');
        return (normalized.Contains("/test/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Contains("Test", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Contains("Spec", StringComparison.OrdinalIgnoreCase))
            && (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNextRouteHandler(string path)
    {
        var normalized = path.Replace('\\', '/');
        return !IsTestSource(path)
            && (normalized.EndsWith("/route.ts", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/route.tsx", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/route.js", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/pages/api/", StringComparison.OrdinalIgnoreCase))
            && (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExpressSource(string path)
        => !IsTestSource(path)
            && (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase));

    private static string NextRoute(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        var marker = normalized.IndexOf("/app/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var route = normalized[(marker + 5)..];
            route = Regex.Replace(route, "/route\\.(?:ts|tsx|js|jsx)$", "", RegexOptions.IgnoreCase);
            return NormalizePath(route);
        }
        marker = normalized.IndexOf("/pages/api/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var route = "api/" + Regex.Replace(normalized[(marker + 11)..], "\\.(?:ts|tsx|js|jsx)$", "", RegexOptions.IgnoreCase);
            return NormalizePath(route.Replace("/index", "", StringComparison.OrdinalIgnoreCase));
        }
        return NormalizePath(normalized);
    }

    private static HashSet<string> ReadIdentitySet(string path, string header)
    {
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var table = MarkdownTables(File.ReadAllText(path)).FirstOrDefault(item => item.Headers.Contains(header, StringComparer.OrdinalIgnoreCase));
        return table?.Rows.Select(row => Value(row, header)).Where(value => !IsUnknown(value)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<MarkdownTable> MarkdownTables(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var tables = new List<MarkdownTable>();
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!IsTable(lines[index]) || !IsSeparator(ParseRow(lines[index + 1]))) continue;
            var headers = ParseRow(lines[index]);
            var rows = new List<IReadOnlyDictionary<string, string>>();
            for (var rowIndex = index + 2; rowIndex < lines.Length && IsTable(lines[rowIndex]); rowIndex++)
            {
                var cells = ParseRow(lines[rowIndex]);
                rows.Add(headers.Select((header, cell) => (header, value: cell < cells.Count ? cells[cell] : ""))
                    .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase));
                index = rowIndex;
            }
            tables.Add(new MarkdownTable(headers, rows));
        }
        return tables;
    }

    internal sealed record MarkdownTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

    private static bool WriteState(string root, IReadOnlyList<ApiOperation> operations, IReadOnlyList<ApiDiagnostic> diagnostics, ApiDiscoveryManifest manifest)
    {
        var values = new[]
        {
            (StatePath, JsonSerializer.Serialize(operations, JsonOptions) + Environment.NewLine),
            (ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine),
            (DiagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions) + Environment.NewLine),
        };
        var changed = false;
        foreach (var (relative, content) in values)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                WriteAtomic(path, content);
                changed = true;
            }
        }
        return changed;
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try { directories = Directory.EnumerateDirectories(current); files = Directory.EnumerateFiles(current); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var directory in directories.Where(directory => !ExcludedDirectories.Contains(Path.GetFileName(directory))
                                                                       && !CisPathSafety.IsReparsePoint(directory))) pending.Push(directory);
            foreach (var file in files) yield return file;
        }
    }

    private static string? ResolveInside(string root, string configured)
    {
        try
        {
            return CisPathSafety.TryResolveUnderRoot(root, configured, out var absolute)
                   && !CisPathSafety.ContainsReparsePoint(root, absolute) ? absolute : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static IReadOnlyList<string> EvidencePaths(string root, string value)
        => value.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Split('#')[0].Trim('`'))
            .Where(item => ResolveInside(root, item) is { } path && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string Value(IReadOnlyDictionary<string, string> row, string name)
        => row.GetValueOrDefault(name) ?? "unknown";
    private static bool IsUnknown(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase)
        || value.Trim().Equals("unclassified", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("TBD", StringComparison.OrdinalIgnoreCase)
        || value.Trim().Equals("TODO", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
    private static string EmptyUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    private static bool IsExternal(string value) => value.Equals("public", StringComparison.OrdinalIgnoreCase) || value.Equals("identity-protocol", StringComparison.OrdinalIgnoreCase) || value.Equals("customer", StringComparison.OrdinalIgnoreCase) || value.Equals("backoffice", StringComparison.OrdinalIgnoreCase);
    private static bool RequiresAuthentication(string value) => value.Equals("customer", StringComparison.OrdinalIgnoreCase) || value.Equals("backoffice", StringComparison.OrdinalIgnoreCase) || value.Equals("internal/service", StringComparison.OrdinalIgnoreCase);
    private static bool IsImplemented(string value) => value.Equals("Implemented", StringComparison.OrdinalIgnoreCase) || value.Equals("Verified", StringComparison.OrdinalIgnoreCase) || value.Equals("Active", StringComparison.OrdinalIgnoreCase) || value.Equals("LegacyImplemented", StringComparison.OrdinalIgnoreCase);
    private static bool IsVerified(string value) => value.Equals("Verified", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeMajor(string value)
    {
        var match = Regex.Match(value, "v?(?<n>[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? "v" + match.Groups["n"].Value : value;
    }
    private static bool ContainsIndirectCacheBoundary(string value) => value.Contains("background", StringComparison.OrdinalIgnoreCase) || value.Contains("precomput", StringComparison.OrdinalIgnoreCase) || value.Contains("populator", StringComparison.OrdinalIgnoreCase) || value.Contains("cache-only", StringComparison.OrdinalIgnoreCase) || value.Contains("materialized", StringComparison.OrdinalIgnoreCase);
    private static bool ContainsMigrationDisposition(string value) => value.Contains("migrat", StringComparison.OrdinalIgnoreCase)
        || value.Contains("replacement", StringComparison.OrdinalIgnoreCase) || value.Contains("sunset", StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<string> SplitIdentities(string value, string prefix) => Regex.Matches(value ?? "", $"\\b{Regex.Escape(prefix)}[A-Za-z0-9_.:-]+", RegexOptions.IgnoreCase).Select(match => match.Value);
    private static string Key(ApiOperation item) => item.Method.ToUpperInvariant() + " " + NormalizePath(item.Path);
    private static string NormalizePath(string value)
    {
        var path = value.Replace("[controller]", "", StringComparison.OrdinalIgnoreCase).Replace("//", "/", StringComparison.Ordinal);
        path = Regex.Replace(path, @"/:([A-Za-z_][A-Za-z0-9_]*)", "/{$1}", RegexOptions.CultureInvariant);
        path = Regex.Replace(path, @"/\[([A-Za-z_][A-Za-z0-9_]*)\]", "/{$1}", RegexOptions.CultureInvariant);
        return "/" + path.Trim('/');
    }
    private static string CombineRoute(string prefix, string suffix) => NormalizePath(prefix.Trim('/') + "/" + suffix.Trim('/'));
    private static string CreateApiId(string method, string path) => "API-" + method.ToUpperInvariant() + "-" + Regex.Replace(path.Trim('/'), "[^A-Za-z0-9]+", "-").Trim('-').ToUpperInvariant();
    private static string Snippet(string content, int start, int length) => content.Substring(Math.Max(0, start), Math.Min(length, content.Length - Math.Max(0, start)));
    private static string SafeRead(string path) { try { return File.ReadAllText(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return ""; } }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static ApiDiagnostic Diagnostic(string code, string severity, string rule, string message, string remediation, IEnumerable<string>? evidence = null)
        => new(code, severity, rule, message, remediation, evidence?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? []);
    private static bool IsTable(string value) => value.TrimStart().StartsWith('|') && value.TrimEnd().EndsWith('|');
    private static bool IsSeparator(IReadOnlyList<string> cells) => cells.Count > 0 && cells.All(cell => Regex.IsMatch(cell, "^:?-{3,}:?$"));
    private static IReadOnlyList<string> ParseRow(string line)
    {
        var body = line.Trim()[1..^1]; var cells = new List<string>(); var current = new StringBuilder();
        for (var index = 0; index < body.Length; index++) { if (body[index] == '\\' && index + 1 < body.Length && body[index + 1] == '|') { current.Append('|'); index++; } else if (body[index] == '|') { cells.Add(current.ToString().Trim()); current.Clear(); } else current.Append(body[index]); }
        cells.Add(current.ToString().Trim()); return cells;
    }
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase) { "get", "post", "put", "patch", "delete", "head", "options", "trace" };
    private static readonly HashSet<string> KnownExternalConsumers = new(StringComparer.OrdinalIgnoreCase)
    {
        "anonymous", "external", "partner", "third-party", "public", "unknown-external",
    };

    private static bool IsApprovedConsumer(string consumer, IReadOnlySet<string> componentIds)
        => componentIds.Contains(consumer)
            || KnownExternalConsumers.Contains(consumer)
            || Regex.IsMatch(consumer, "^external:[a-z0-9][a-z0-9._-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
