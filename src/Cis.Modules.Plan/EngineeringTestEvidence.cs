using System.Text.Json;
using System.Security.Cryptography;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

using static Cis.Modules.Plan.EngineeringArtifactEvidence;

internal static class EngineeringTestEvidence
{
    internal static readonly HashSet<string> Layers = new(StringComparer.Ordinal)
    { "unit", "component", "architecture", "integration", "business", "regression", "browser", "security", "api-compatibility", "coverage", "mutation" };
    internal static string? Rejection(string repository, JsonElement root, string layer, EngineeringDefaultsPolicy policy,
        bool inapplicable = false, string? documentationRoot = null,
        IReadOnlyList<string>? requiredBrowserCaseIds = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? browserSources = null)
    {
        if (Number(root, "schemaVersion") == 2)
        {
            var manifest = root.Deserialize<TestRunManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (Text(root, "status") != "scope-passed" || manifest?.Scope is null || !manifest.Scope.Matches(manifest))
                return "selected-suite manifest scope, identity or status is invalid";
            if (documentationRoot is null || !CisPathSafety.TryResolveUnderRoot(repository,
                documentationRoot.TrimEnd('/', '\\') + "/references/test-suite-profile.md", out var profile)
                || CisPathSafety.ContainsReparsePoint(repository, profile) || !File.Exists(profile) || new FileInfo(profile).Length > 1024 * 1024
                || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(profile))) != manifest.SuiteProfileDigest)
                return "selected-suite manifest profile is missing or stale";
        }
        else if (Text(root, "status") != "passed" || root.TryGetProperty("scope", out var scope) && scope.ValueKind != JsonValueKind.Null)
            return "test manifest status is not passed or uses an unsupported scope";
        if (!root.TryGetProperty("suites", out var suites) || suites.ValueKind != JsonValueKind.Array) return "native test suites are missing";
        if (!suites.EnumerateArray().Any(suite => SuitePasses(root, suite, layer, policy, inapplicable)))
            return $"no passing nonzero/non-skipped suite at layer {layer} with its own bound native report; coverage threshold={policy.MinimumCoverageLines}, mutation threshold={policy.MinimumMutationScore}";
        if (layer == "browser")
        {
            var rejection = BrowserCasesRejection(repository, root, requiredBrowserCaseIds, policy, browserSources);
            if (rejection is not null) return rejection;
        }
        return ArtifactsIntact(repository, root) ? null : "a native report is missing, changed, or has an incorrect size/hash";
    }

    private static string? BrowserCasesRejection(string repository, JsonElement manifest, IReadOnlyList<string>? requiredIds, EngineeringDefaultsPolicy policy,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? browserSources)
    {
        if (requiredIds is not { Count: > 0 })
            return "required browser journey identities are unresolved; bind the task to its frontend catalogue cases or list exact TC-* identities in its native browser verification obligation";
        var sources = browserSources ?? ManualTestAutomationScanner.ScanSourcePaths(repository);
        var suites = manifest.GetProperty("suites").EnumerateArray().Where(suite => Text(suite, "layer") == "browser").ToArray();
        foreach (var id in requiredIds)
        {
            var found = false;
            foreach (var suite in suites)
            {
                if (!suite.TryGetProperty("cases", out var cases) || cases.ValueKind != JsonValueKind.Array) continue;
                var matches = cases.EnumerateArray().Where(test => Text(test, "id").Equals(id, StringComparison.OrdinalIgnoreCase)
                    || BrowserJourneyRequirements.Ids(Text(test, "name")).Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0) continue;
                found = true;
                // Playwright JSON reports file paths. TRX does not retain that location;
                // preserve its existing TC identity join without inventing a source path.
                if (!sources.TryGetValue(id, out var sourcePaths) || Text(suite, "framework").Equals("playwright", StringComparison.OrdinalIgnoreCase)
                    && matches.Any(test => BrowserJourneyRequirements.SourcePath(repository, Text(test, "source")) is not { } path
                        || !sourcePaths.Any(source => source.Equals(path, CisPathSafety.PlatformComparison))))
                    return $"required browser journey {id} has no matching current test source for its native result; preserve the report's actual source path and the source's TC-* reference";
                if (!SuitePasses(manifest, suite, "browser", policy, false) || cases.GetArrayLength() != Number(suite, "total")
                    || string.IsNullOrWhiteSpace(Text(suite, "suiteId")) || string.IsNullOrWhiteSpace(Text(suite, "framework"))
                    || !suite.GetProperty("artifacts").EnumerateArray().Any(artifact => Text(artifact, "kind") == "test-result"
                        && Text(artifact, "suiteId") == Text(suite, "suiteId"))
                    || cases.EnumerateArray().Any(test => Text(test, "status") != "passed"))
                    return $"required browser journey {id} has a missing, non-passing or inconsistent native result; every reported project/parameter case must pass in its own bound browser suite";
            }
            if (!found) return $"required browser journey {id} has no executed native result in this browser evidence";
        }
        return null;
    }
    private static bool SuitePasses(JsonElement manifest, JsonElement suite, string layer, EngineeringDefaultsPolicy policy, bool inapplicable)
    {
        if (Text(suite, "status") != "passed" || Number(suite, "total") <= 0
            || Number(suite, "failed") != 0 || Number(suite, "skipped") != 0) return false;
        if (layer == "coverage") return Layers.Contains(Text(suite, "layer")) && Text(suite, "layer") != "mutation"
            && suite.TryGetProperty("coverage", out var coverage) && coverage.ValueKind == JsonValueKind.Object
            && (inapplicable
                ? Number(coverage, "changedProductionLines") >= 0 && Number(coverage, "uninstrumentedChangedFiles") == 0
                    && Number(coverage, "measuredLines") == 0 && Text(coverage, "baseRevision").Length is 40 or 64
                : Number(coverage, "lines") >= policy.MinimumCoverageLines && Number(coverage, "lines") <= 100 && Number(coverage, "measuredLines") > 0)
            && Text(coverage, "scope") == "changed-production"
            && HasBoundResult(manifest, suite, "test-result")
            && HasBoundResult(manifest, suite, "coverage-result", Text(coverage, "sourcePath"));
        if (layer == "mutation") return Text(suite, "layer") == "mutation"
            && suite.TryGetProperty("mutation", out var mutation) && mutation.ValueKind == JsonValueKind.Object
            && Number(mutation, "score") >= policy.MinimumMutationScore && Number(mutation, "score") <= 100 && Number(mutation, "killed") > 0
            && HasBoundResult(manifest, suite, "mutation-result", Text(mutation, "sourcePath"));
        return Text(suite, "layer") == layer && Number(suite, "passed") == Number(suite, "total")
            && HasBoundResult(manifest, suite, "test-result");
    }

}
