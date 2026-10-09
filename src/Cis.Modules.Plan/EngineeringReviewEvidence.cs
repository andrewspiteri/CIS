using System.Text.Json;
using System.Security.Cryptography;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

internal static class EngineeringReviewEvidence
{
    internal static string? Rejection(string repository, EngineeringGateEvidence gate, JsonElement root, string runId, string taskId, string contract,
        string? expectedTargetRepository = null, string? authorityInputDigest = null, string? artifactPath = null, string? authorityDossierDigest = null)
    {
        if (expectedTargetRepository is not null)
        {
            if (!Path.IsPathFullyQualified(Text(root, "targetRepositoryPath"))
                || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(Text(root, "targetRepositoryPath"))).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedTargetRepository)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return "review targets a different participant repository";
            if (string.IsNullOrWhiteSpace(authorityInputDigest) || Text(root, "authorityInputDigest") != authorityInputDigest) return "review authority context is missing or stale";
            if (string.IsNullOrWhiteSpace(authorityDossierDigest) || Text(root, "authorityDossierDigest") != authorityDossierDigest) return "review authority dossier context is missing or stale";
            if (string.IsNullOrWhiteSpace(Text(root, "providerSessionId"))) return "review provider session is missing";
            if (artifactPath?.Replace('\\', '/') != $".cis/local/agents/runs/{runId}/manifest.json")
                return "review must retain its native authority run location";
        }
        if (Text(root, "runId") != runId) return "review run ID does not match the assigned reviewer";
        if (Text(root, "status") != CisAgentRunStates.Succeeded || Text(root, "mode") != "review" || Text(root, "permission") != "read-only") return "review must be a successful read-only run";
        if (Text(root, "taskId") != taskId || Text(root, "taskContractDigest") != contract) return "review task or contract is stale";
        if (string.IsNullOrWhiteSpace(Text(root, "provider"))) return "review provider is missing";
        return ReviewResultPasses(repository, gate.Artifacts, root,
            expectedTargetRepository is null ? null : $".cis/local/agents/runs/{runId}/result.json") ? null : "review result is unbound, not ready, or has corrective findings";
    }
    private static bool ReviewResultPasses(string repository, IReadOnlyList<EngineeringEvidenceArtifact> artifacts, JsonElement manifest, string? nativePath)
    {
        var digest = Text(manifest, "resultDigest");
        if (string.IsNullOrWhiteSpace(digest)) return false;
        foreach (var artifact in artifacts.Where(item => item.Sha256 == digest || item.Sha256 == "sha256:" + digest))
        {
            if (nativePath is not null && artifact.Path.Replace('\\', '/') != nativePath) continue;
            if (!CisPathSafety.TryResolveUnderRoot(repository, artifact.Path, out var path)
                || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path) || new FileInfo(path).Length > 32 * 1024 * 1024) continue;
            using var result = JsonDocument.Parse(File.ReadAllText(path));
            var root = result.RootElement;
            if (Number(root, "schemaVersion") != 2 || string.IsNullOrWhiteSpace(Text(root, "envelopeId"))
                || Text(root, "envelopeId") != Text(manifest, "envelopeId") || Text(root, "status") != CisAgentRunStates.Succeeded
                || !root.TryGetProperty("review", out var review) || Text(review, "recommendation") != "ready"
                || !review.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array) continue;
            if (findings.EnumerateArray().All(finding => Text(finding, "severity") == "observation")) return true;
        }
        return false;
    }

}
