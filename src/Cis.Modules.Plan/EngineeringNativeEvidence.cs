using System.Text.Json;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

/// <summary>Routes native evidence to validators that preserve specific rejection reasons.</summary>
internal static class EngineeringNativeEvidence
{
    internal static IReadOnlyList<string> Review(string repository, EngineeringGateEvidence gate, string inputDigest,
        string reviewerRunId, string taskId, string taskContractDigest, string documentationRoot, EngineeringDefaultsPolicy policy,
        string? expectedTargetRepository = null, string? authorityInputDigest = null, string? authorityDossierDigest = null)
    {
        if (gate.Id == "performance") return BenchmarkEvidenceReview.Review(repository, gate.Artifacts, inputDigest);
        if (!EngineeringTestEvidence.Layers.Contains(gate.Id) && gate.Id is not ("build" or "lint" or "format" or "review")) return [];
        var reasons = new List<string>();
        foreach (var artifact in gate.Artifacts)
        {
            if (!CisPathSafety.TryResolveUnderRoot(repository, artifact.Path, out var path)
                || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path) || new FileInfo(path).Length > 32 * 1024 * 1024)
            { reasons.Add($"{artifact.Path}: missing, unsafe or oversized native artifact"); continue; }
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var root = json.RootElement;
                var expectedSchema = gate.Id is "build" or "lint" or "format" ? 2 : 1;
                string? rejection;
                if (Number(root, "schemaVersion") != expectedSchema) rejection = $"expected manifest schema {expectedSchema}";
                else if (Text(root, "inputDigest") != inputDigest) rejection = "execution input digest is missing or stale";
                else rejection = gate.Id switch
                {
                    "review" => EngineeringReviewEvidence.Rejection(repository, gate, root, reviewerRunId, taskId, taskContractDigest, expectedTargetRepository, authorityInputDigest, artifact.Path, authorityDossierDigest),
                    "build" or "lint" or "format" => WorkflowRejection(root, artifact.Selector ?? gate.Id),
                    "security" when root.TryGetProperty("profilePath", out _) => EngineeringSecurityEvidence.Rejection(repository, root, documentationRoot),
                    _ => EngineeringTestEvidence.Rejection(repository, root, gate.Id, policy, gate.State == EngineeringGateState.Inapplicable)
                };
                if (rejection is null) return [];
                reasons.Add($"{artifact.Path}: {rejection}");
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { reasons.Add($"{artifact.Path}: unreadable native evidence ({error.Message})"); }
        }
        return [$"Gate '{gate.Id}' lacks current successful native evidence: {string.Join("; ", reasons)}"];
    }

    private static string? WorkflowRejection(JsonElement root, string stepId)
    {
        if (Text(root, "status") != "succeeded") return "workflow did not succeed";
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array) return "workflow steps are missing";
        return steps.EnumerateArray().Any(step => Text(step, "id") == stepId && Text(step, "status") == "succeeded" && Number(step, "exitCode") == 0)
            ? null : $"workflow step '{stepId}' is absent or unsuccessful";
    }
}
