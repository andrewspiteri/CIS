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
    internal static string? Rejection(string repository, JsonElement root, string layer, EngineeringDefaultsPolicy policy, bool inapplicable = false)
    {
        if (Text(root, "status") != "passed") return "test manifest status is not passed";
        if (!root.TryGetProperty("suites", out var suites) || suites.ValueKind != JsonValueKind.Array) return "native test suites are missing";
        if (!suites.EnumerateArray().Any(suite => SuitePasses(root, suite, layer, policy, inapplicable)))
            return $"no passing nonzero/non-skipped suite at layer {layer} with its own bound native report; coverage threshold={policy.MinimumCoverageLines}, mutation threshold={policy.MinimumMutationScore}";
        return ArtifactsIntact(repository, root) ? null : "a native report is missing, changed, or has an incorrect size/hash";
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
