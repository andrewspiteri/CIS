using System.Text.Json;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private IReadOnlyList<string> ValidateBrowserJourneyCoverage(string repositoryPath, string cataloguePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> discovered, IReadOnlyList<string>? requirementIds)
    {
        var errors = new List<string>();
        try
        {
            var catalogue = BrowserJourneyRequirements.ReadCatalogue(repositoryPath, cataloguePath);
            foreach (var requirement in requirementIds ?? [])
                if (!catalogue.Any(item => item.RequirementId == requirement))
                    errors.Add($"Required browser journey catalogue has no case definition for current requirement {requirement}; refresh the authority catalogue.");
            if (errors.Count > 0) return errors;
            var required = catalogue
                .Where(item => item.FrontendType is "public" or "customer" or "backoffice").ToArray();
            if (required.Length == 0) return [];
            var workspace = _workspaceRegistry?.Resolve(repositoryPath);
            var paths = workspace is { IsSuccess: true, Workspace: not null }
                ? workspace.Workspace.Repositories.Where(item => item.IsProductOwned).Select(item => item.RepositoryPath)
                : [repositoryPath];
            var native = new List<(CisRepositoryContext Context, JsonElement Manifest, JsonElement Suite, DateTimeOffset Completed)>();
            var sources = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var context = _resolver.Resolve(path).Context;
                if (context is null || !required.Any(item => discovered.TryGetValue(item.Id, out var references)
                    && references.Any(reference => reference.StartsWith(context.RepositoryId + "::", StringComparison.Ordinal)))) continue;
                var roots = new[] { "runs", "scoped-runs" }
                    .Select(name => Path.Combine(context.RepositoryPath, ".cis/local/testing", name))
                    .Where(root => Directory.Exists(root) && !CisPathSafety.ContainsReparsePoint(context.RepositoryPath, root)).ToArray();
                if (roots.Length == 0) continue;
                var inputDigest = CisExecutionIdentity.Capture(context);
                sources[context.RepositoryId] = ManualTestAutomationScanner.ScanSourcePaths(context.RepositoryPath);
                foreach (var directory in roots.SelectMany(root => Directory.EnumerateDirectories(root)))
                {
                    var manifestPath = Path.Combine(directory, "manifest.json");
                    if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, manifestPath)
                        || !File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 32 * 1024 * 1024) continue;
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                        var manifest = document.RootElement;
                        var scoped = Path.GetFileName(Path.GetDirectoryName(directory)) == "scoped-runs";
                        var selected = scoped ? manifest.Deserialize<TestRunManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
                        if (scoped
                            ? selected?.Scope is null || selected.Scope.Id != Path.GetFileName(directory) || !selected.Scope.Matches(selected)
                            : Number(manifest, "schemaVersion") != 1 || manifest.TryGetProperty("scope", out var scope) && scope.ValueKind != JsonValueKind.Null) continue;
                        if (Text(manifest, "repositoryId") != context.RepositoryId
                            || Text(manifest, "inputDigest") != inputDigest
                            || !DateTimeOffset.TryParse(Text(manifest, "completedAtUtc"), out var completed)
                            || !manifest.TryGetProperty("suites", out var suites) || suites.ValueKind != JsonValueKind.Array) continue;
                        var retained = manifest.Clone();
                        foreach (var suite in retained.GetProperty("suites").EnumerateArray().Where(suite => Text(suite, "layer") == "browser"
                            && !string.IsNullOrWhiteSpace(Text(suite, "suiteId"))))
                            native.Add((context, retained, suite, completed));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                    { /* Invalid reports cannot contribute coverage. The missing-case diagnostic remains authoritative. */ }
                }
            }
            var latest = native.GroupBy(item => (item.Context.RepositoryId, SuiteId: Text(item.Suite, "suiteId")))
                .Select(group => group.OrderByDescending(item => item.Completed)
                    .ThenByDescending(item => Number(item.Manifest, "schemaVersion") == 2).First()).ToArray();
            foreach (var journey in required)
            {
                var matches = latest.Where(item => discovered.TryGetValue(journey.Id, out var references)
                    && references.Any(reference => reference.StartsWith(item.Context.RepositoryId + "::", StringComparison.Ordinal))
                    && item.Suite.TryGetProperty("cases", out var cases) && cases.ValueKind == JsonValueKind.Array
                    && cases.EnumerateArray().Any(test => Text(test, "id").Equals(journey.Id, StringComparison.OrdinalIgnoreCase)
                        || BrowserJourneyRequirements.Ids(Text(test, "name")).Contains(journey.Id, StringComparer.OrdinalIgnoreCase))).ToArray();
                if (matches.Length == 0)
                {
                    errors.Add($"Required browser journey {journey.Id} has no current executed native result; run and reconcile its browser suite against the current source and catalogue.");
                    continue;
                }
                foreach (var match in matches)
                {
                    var policy = CisEngineeringPolicy.Read(match.Context.RepositoryPath) ?? new EngineeringDefaultsPolicy(1, false, []);
                    var rejection = EngineeringTestEvidence.Rejection(match.Context.RepositoryPath, match.Manifest, "browser", policy,
                        documentationRoot: match.Context.DocumentationRoot,
                        requiredBrowserCaseIds: [journey.Id], browserSources: sources[match.Context.RepositoryId]);
                    if (rejection is not null)
                        errors.Add($"Required browser journey {journey.Id} in {match.Context.RepositoryId}/{Text(match.Suite, "suiteId")}: {rejection}.");
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { errors.Add("Required browser journey coverage could not be verified: " + error.Message); }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
}
