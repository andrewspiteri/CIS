using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Plan;

public sealed record EngineeringCompletionReceipt(int SchemaVersion, string ChangeId, string TaskId,
    string TaskDigest, string GraphBuildId, string PolicyDigest, string InputDigest, IReadOnlyList<string> RequirementIds,
    IReadOnlyList<EngineeringGateEvidence> Gates, IReadOnlyList<string> UnresolvedBlockingFindings,
    string ImplementerRunId, string ReviewerRunId)
{
    public EngineeringEvidenceArtifact? Implementation { get; init; }
    public string? RepositoryId { get; init; }
    public string? ReceiptPath { get; init; }
}

/// <summary>Validates task verification evidence. It neither runs tests nor approves exceptions.</summary>
public static class EngineeringCompletionReview
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static EngineeringCompletionReceipt Template(string changeId, PlanWorkItem task, string policyPath,
        CisGraphMetadataReadResult graph, EngineeringAssessment assessment, string inputDigest, string? taskDocument = null, string? taskContractDigest = null)
        => new(1, changeId, task.Id, taskContractDigest ?? TaskDigest(task, taskDocument), graph.Build?.Id ?? "", Hash(policyPath), inputDigest, task.RequirementIds,
            assessment.RequiredGates.Select(gate => new EngineeringGateEvidence(gate.Id, EngineeringGateState.Missing, "", [])).ToArray(),
            [], "", "");

    public static IReadOnlyList<string> Review(string repository, string receiptPath, string changeId,
        PlanWorkItem task, string policyPath, CisGraphMetadataReadResult? graph, EngineeringAssessment assessment, string inputDigest, string documentationRoot, string? taskDocument = null,
        string? taskContractDigest = null, string? reviewTaskId = null, string? implementationTaskId = null, string? implementationRepository = null,
        string? reviewRepository = null, string? receiptRepository = null, string? expectedRepositoryId = null, string? authorityInputDigest = null,
        string? authorityDossierDigest = null)
    {
        var errors = new List<string>(assessment.Diagnostics);
        if (!assessment.Adopted) return errors;
        if (graph is not { ExitCode: 0, Freshness: "fresh", Build.Status: "complete" or "partial" })
            errors.Add("Iteration completion requires a fresh repository graph; inspect extraction limitations and rebuild affected inputs.");
        if (!File.Exists(receiptPath))
        {
            errors.Add($"Task closing evidence is missing: {Path.GetRelativePath(repository, receiptPath)}. Assess the required gate inventory, then record current verification and assigned-reviewer evidence.");
            return errors;
        }
        try
        {
            EnsureSafe(receiptRepository ?? repository, receiptPath, 512 * 1024);
            var receipt = JsonSerializer.Deserialize<EngineeringCompletionReceipt>(File.ReadAllText(receiptPath), JsonOptions);
            if (receipt is not { SchemaVersion: 1, Gates: not null, RequirementIds: not null, UnresolvedBlockingFindings: not null })
                throw new InvalidDataException("Completion receipt requires schemaVersion=1 and complete gate, requirement and finding collections.");
            if (receipt.Gates.Any(gate => gate is null || string.IsNullOrWhiteSpace(gate.Id) || gate.Artifacts?.Any(artifact => artifact is null) == true)
                || receipt.RequirementIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Completion receipt contains an empty gate, artifact or requirement identity.");
            if (receipt.ChangeId != changeId || receipt.TaskId != task.Id || receipt.TaskDigest != (taskContractDigest ?? TaskDigest(task, taskDocument)))
                errors.Add("Task closing evidence is stale or belongs to another task/requirement contract.");
            if (reviewRepository is not null && receipt.RepositoryId != expectedRepositoryId)
                errors.Add("Task closing evidence identifies a different participant repository.");
            if (receipt.GraphBuildId != graph?.Build?.Id || receipt.PolicyDigest != Hash(policyPath))
                errors.Add("Task closing evidence is stale after source, graph or engineering-policy changes.");
            if (receipt.InputDigest != inputDigest) errors.Add("Task closing evidence is stale after execution inputs changed, including uncommitted files.");
            if (!receipt.RequirementIds.Order(StringComparer.Ordinal).SequenceEqual(task.RequirementIds.Order(StringComparer.Ordinal)))
                errors.Add("Completion traceability does not cover the exact current task requirements.");
            if (receipt.UnresolvedBlockingFindings.Count > 0)
                errors.Add("Unresolved blocking findings prevent task completion: " + string.Join(", ", receipt.UnresolvedBlockingFindings));
            if (string.IsNullOrWhiteSpace(receipt.ImplementerRunId) || string.IsNullOrWhiteSpace(receipt.ReviewerRunId)
                || receipt.ImplementerRunId == receipt.ReviewerRunId)
                errors.Add("Completion requires separate identified implementation and assigned-reviewer runs.");
            errors.AddRange(EngineeringImplementationReview.Review(repository, receipt, task.Id, taskContractDigest ?? TaskDigest(task, taskDocument), inputDigest, implementationTaskId, implementationRepository, reviewRepository));
            var thresholds = JsonSerializer.Deserialize<EngineeringDefaultsPolicy>(File.ReadAllText(policyPath), JsonOptions)
                ?? throw new InvalidDataException("Engineering policy is missing.");
            CisEngineeringPolicy.ValidateThresholds(thresholds);
            foreach (var duplicate in receipt.Gates.GroupBy(gate => gate.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
                errors.Add($"Duplicate completion gate: {duplicate.Key}.");
            foreach (var required in assessment.RequiredGates)
            {
                var evidence = receipt.Gates.FirstOrDefault(gate => gate.Id == required.Id);
                var evidenceRepository = required.Id == "review" ? reviewRepository ?? repository : repository;
                ReviewGate(evidenceRepository, required, evidence, errors);
                if (evidence is { Artifacts.Count: > 0 } && (evidence.State == EngineeringGateState.Passed || evidence.Id == "coverage" && evidence.State == EngineeringGateState.Inapplicable))
                    errors.AddRange(EngineeringNativeEvidence.Review(evidenceRepository, evidence, inputDigest, receipt.ReviewerRunId,
                        reviewTaskId ?? task.Id, taskContractDigest ?? TaskDigest(task, taskDocument), documentationRoot, thresholds,
                        required.Id == "review" && reviewRepository is not null ? repository : null, authorityInputDigest, authorityDossierDigest));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { errors.Add($"Invalid completion evidence: {exception.Message}"); }
        return errors;
    }

    private static void ReviewGate(string repository, EngineeringGateRequirement required,
        EngineeringGateEvidence? evidence, ICollection<string> errors)
    {
        if (evidence is null) { errors.Add($"Required gate '{required.Id}' is missing, even if configured checks passed."); return; }
        if (evidence.State is not (EngineeringGateState.Passed or EngineeringGateState.Inapplicable))
        { errors.Add($"Required gate '{required.Id}' is {evidence.State.ToString().ToLowerInvariant()}; every applicable gate must pass."); return; }
        if (evidence.State == EngineeringGateState.Inapplicable && !required.AllowsInapplicability)
            errors.Add($"Required gate '{required.Id}' cannot be marked inapplicable.");
        if (string.IsNullOrWhiteSpace(evidence.Rationale) || evidence.Rationale.Trim().Length < 12)
            errors.Add($"Gate '{required.Id}' needs a concrete scope/result or inapplicability rationale.");
        if (evidence.Artifacts is not { Count: > 0 })
        { errors.Add($"Gate '{required.Id}' has no supporting evidence artifacts; a declared status is insufficient."); return; }
        foreach (var artifact in evidence.Artifacts)
        {
            if (!CisPathSafety.TryResolveUnderRoot(repository, artifact.Path, out var path))
            { errors.Add($"Gate '{required.Id}' artifact escapes the repository."); continue; }
            try
            {
                EnsureSafe(repository, path, 64 * 1024 * 1024);
                if (!string.Equals(Hash(path), artifact.Sha256, StringComparison.Ordinal))
                    errors.Add($"Gate '{required.Id}' evidence is stale or modified: {artifact.Path}.");
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { errors.Add($"Gate '{required.Id}' artifact '{artifact.Path}' is invalid: {exception.Message}"); }
        }
    }

    public static string Serialize(EngineeringCompletionReceipt receipt) => JsonSerializer.Serialize(receipt, JsonOptions);
    public static string TaskDigest(PlanWorkItem task, string? taskDocument = null)
    {
        // Transition bookkeeping must not invalidate the contract that authorized that transition.
        var content = ContractBody(taskDocument ?? "");
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(task with { Status = "" }, JsonOptions) + "\n" + content)));
    }

    private static string ContractBody(string document)
    {
        var lines = document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var retained = new List<string>();
        var frontmatter = lines.Length > 0 && lines[0] == "---";
        var completionSection = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (index > 0 && line == "---") frontmatter = false;
            if ((frontmatter || index == 0) && line.StartsWith("task_status:", StringComparison.Ordinal)) continue;
            if (line.StartsWith("## ", StringComparison.Ordinal)) completionSection = line == "## Completion evidence";
            if (completionSection && CisToolUsageSnapshot.IsTaskRow(line)) continue;
            retained.Add(line);
        }
        return string.Join('\n', retained).TrimEnd();
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void EnsureSafe(string repository, string path, long maximum)
    {
        if (!CisPathSafety.IsUnderRoot(repository, path) || CisPathSafety.ContainsReparsePoint(repository, path)
            || !File.Exists(path) || new FileInfo(path).Length > maximum)
            throw new InvalidDataException("Evidence must be an existing bounded file inside the repository without linked paths.");
    }
}
