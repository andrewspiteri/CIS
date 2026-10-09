using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

/// <summary>Distinguishes a verified native implementation run from an explicitly declared human implementation.</summary>
internal static class EngineeringImplementationReview
{
    internal static IReadOnlyList<string> Review(string repository, EngineeringCompletionReceipt receipt, string taskId, string contract, string inputs,
        string? implementationTaskId = null, string? implementationRepository = null, string? reviewRepository = null)
    {
        try
        {
            if (receipt.Implementation is null) return ["Implementation provenance is missing; an arbitrary implementer run ID is insufficient."];
            var nativePath = $".cis/local/agents/runs/{receipt.ImplementerRunId}/manifest.json";
            var native = receipt.Implementation.Path.Replace('\\', '/') == nativePath;
            var recordRepository = native ? implementationRepository ?? repository : repository;
            using var document = Read(recordRepository, receipt.Implementation);
            var run = document.RootElement;
            var human = Text(run, "kind") == "human-implemented";
            if (Number(run, "schemaVersion") != 1 || Text(run, "runId") != receipt.ImplementerRunId
                || Text(run, "taskId") != (human ? taskId : implementationTaskId ?? taskId) || Text(run, "taskContractDigest") != contract
                || !DateTimeOffset.TryParse(Text(run, "completedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var completed)
                || completed > DateTimeOffset.UtcNow)
                return ["Implementation provenance does not identify the completed current task contract."];
            if (human)
            {
                if (string.IsNullOrWhiteSpace(Text(run, "actor")) || Text(run, "inputDigest") != inputs)
                    return ["Human implementation must explicitly name its author and the reviewed final input identity."];
            }
            else if (Text(run, "mode") != CisAgentRunModes.Implement || Text(run, "status") != CisAgentRunStates.Succeeded
                     || Text(run, "outputDigest") != inputs || string.IsNullOrWhiteSpace(Text(run, "providerSessionId"))
                     || string.IsNullOrWhiteSpace(Text(run, "provider")))
                return ["Implementation evidence must be a successful native implementation run bound to the final source and a provider session."];

            if (!human)
            {
                if (!native)
                    return ["Implementation evidence must resolve to its retained native run manifest."];
                if (implementationRepository is not null && (!Path.IsPathFullyQualified(Text(run, "targetRepositoryPath"))
                    || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Text(run, "targetRepositoryPath"))),
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    return ["Implementation run targets a different participant repository."];
                var resultDigest = Text(run, "resultDigest");
                if (!resultDigest.StartsWith("sha256:", StringComparison.Ordinal)) resultDigest = "sha256:" + resultDigest;
                using var result = Read(recordRepository, new(nativePath.Replace("/manifest.json", "/result.json", StringComparison.Ordinal), resultDigest));
                if (Number(result.RootElement, "schemaVersion") is not (1 or 2)
                    || Text(result.RootElement, "status") != CisAgentRunStates.Succeeded
                    || string.IsNullOrWhiteSpace(Text(run, "envelopeId")) || Text(result.RootElement, "envelopeId") != Text(run, "envelopeId"))
                    return ["Implementation native result does not match its successful run and envelope."];
            }

            var reviewGate = receipt.Gates.FirstOrDefault(gate => gate.Id == "review");
            if (reviewGate is null) return []; // Inventory enforcement separately requires review in production assessments.
            foreach (var artifact in reviewGate.Artifacts ?? [])
            {
                using var candidate = Read(reviewRepository ?? repository, artifact);
                var reviewer = candidate.RootElement;
                if (Text(reviewer, "runId") != receipt.ReviewerRunId) continue;
                if (!DateTimeOffset.TryParse(Text(reviewer, "startedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var started) || started < completed)
                    return ["The assigned review must start after the identified implementation completes."];
                if (!human && (Text(run, "runId") == Text(reviewer, "runId")
                    || Text(run, "provider") == Text(reviewer, "provider") && Text(run, "providerSessionId") == Text(reviewer, "providerSessionId")))
                    return ["Implementation and review must use separate native runs and provider sessions."];
                return [];
            }
            return ["Implementation provenance cannot be linked to the assigned review run."];
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return ["Invalid implementation provenance: " + error.Message]; }
    }

    private static JsonDocument Read(string repository, EngineeringEvidenceArtifact artifact)
    {
        if (!CisPathSafety.TryResolveUnderRoot(repository, artifact.Path, out var path) || CisPathSafety.ContainsReparsePoint(repository, path)
            || !File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Implementation/review records must be bounded regular repository artifacts.");
        var bytes = File.ReadAllBytes(path);
        if ("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)) != artifact.Sha256)
            throw new InvalidDataException("Implementation/review record hash does not match.");
        return JsonDocument.Parse(bytes);
    }
}
