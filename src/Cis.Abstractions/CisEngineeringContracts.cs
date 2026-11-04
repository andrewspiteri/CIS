using System.Text.Json.Serialization;

namespace Cis.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<EngineeringGateState>))]
public enum EngineeringGateState { Passed, Failed, Missing, Stale, Skipped, Inapplicable }

public sealed record EngineeringGateRequirement(string Id, string Reason, bool AllowsInapplicability);
public sealed record EngineeringGateEvidence(string Id, EngineeringGateState State, string Rationale,
    IReadOnlyList<EngineeringEvidenceArtifact> Artifacts);
public sealed record EngineeringEvidenceArtifact(string Path, string Sha256, string? Selector = null);

/// <summary>Assessment derives obligations from current source and adopted policy, never from successful commands.</summary>
public interface ICisEngineeringAssessment
{
    EngineeringAssessment Assess(CisRepositoryContext context, string taskCategory);
}

public sealed record EngineeringAssessment(int SchemaVersion, bool Adopted,
    IReadOnlyList<EngineeringGateRequirement> RequiredGates, IReadOnlyList<string> Diagnostics);

public sealed record EngineeringDefaultsPolicy(int SchemaVersion, bool RequireTaskCompletion,
    IReadOnlyList<string> AdditionalGates, double MinimumCoverageLines = 95, double MinimumMutationScore = 80);

/// <summary>Reads the current plan-owned task contract without granting lifecycle authority.</summary>
public interface ICisEngineeringTaskContractReader
{
    string ReadTaskDigest(string repositoryPath, string changeId, string taskId);
    IReadOnlyList<string> ValidateTaskType(string repositoryPath, string key, string version, string category)
        => ["Task type validation is unavailable; load the existing planning task-type provider."];
    IReadOnlyList<CisTaskTypeDefinition> ReadTaskTypes(string repositoryPath) => [];
    CisFeatureStoryTask ResolveStoryTask(string repositoryPath, CisFeatureStoryTask task) => task;
    IReadOnlyList<string> ValidateStoryTaskGate(string repositoryPath, CisFeatureStoryTask task, bool completing = false)
        => string.IsNullOrWhiteSpace(task.ApprovalGate) || task.ApprovalGate.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? [] : [$"Task {task.Id} requires approval gate '{task.ApprovalGate}'; its existing approval authority is unavailable."];
}

public sealed record EngineeringPerformanceResult(int SchemaVersion, string Status, IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 4;
}

public sealed record CisStoryCompletionContext(string RepositoryId, string ReceiptPath, string? TemplateJson,
    IReadOnlyList<string> Errors)
{
    public string AdoptionStatus { get; init; } = "required";
}

public interface ICisStoryEngineeringCompletion
{
    IReadOnlyList<CisStoryCompletionContext> Assess(CisStoryTaskWork work, bool prepare);
}

public static class CisStoryEngineeringContract
{
    /// <summary>Legacy records stay readable; adopted execution needs an explicit, bounded task contract.</summary>
    public static IReadOnlyList<string> Validate(CisFeatureStoryTask task, bool requireClassification = true)
    {
        var errors = new List<string>();
        if (task.AcceptanceCriteria is not { Count: > 0 and <= 64 } || task.AcceptanceCriteria.Any(value => !BoundedText(value, 8000)))
            errors.Add($"Task {task.Id} needs bounded completion criteria.");
        foreach (var (name, values) in new (string, IReadOnlyList<string>?)[]
                 { ("targets", task.Targets), ("decisions", task.DecisionIds), ("negative criteria", task.NegativeCriteria), ("required outputs", task.RequiredOutputs) })
            if (values is null || values.Count > 64 || values.Any(value => !BoundedText(value, 2000)) || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
                errors.Add($"Task {task.Id} has malformed {name}; retain distinct bounded contract values.");
        foreach (var value in new[] { task.TaskTypeKey, task.TaskTypeVersion, task.FrontendType, task.ApprovalGate, task.StoryId, task.InstanceKey, task.WorkOwnerTaskId })
            if (value is not null && !BoundedText(value, 200)) errors.Add($"Task {task.Id} has an invalid task contract identity.");
        if (task.Validation is not null && !BoundedText(task.Validation, 8000)) errors.Add($"Task {task.Id} has invalid scoped validation instructions.");
        foreach (var (name, value) in new[] { ("category", task.Category), ("complexity", task.Complexity),
                     ("task type", task.TaskTypeKey), ("task type version", task.TaskTypeVersion) })
            if (requireClassification && string.IsNullOrWhiteSpace(value))
                errors.Add($"Task {task.Id} has unresolved {name}; reassess its persisted contract before adopted execution or completion.");
        if (!string.IsNullOrWhiteSpace(task.Category) && (task.Category.Length > 80
            || task.Category.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
            errors.Add($"Task {task.Id} has invalid category '{task.Category}'; resolve its actual task type/category.");
        if (!string.IsNullOrWhiteSpace(task.Complexity) && !IsExecutable(task))
            errors.Add($"Task {task.Id} is {task.Complexity}; decompose or reassess it to current low/medium complexity before execution or completion.");
        if (requireClassification && task.RequiredOutputs is not { Count: > 0 })
            errors.Add($"Task {task.Id} needs explicit required outputs before adopted execution or completion.");
        if (requireClassification && string.IsNullOrWhiteSpace(task.Validation))
            errors.Add($"Task {task.Id} needs its scoped validation contract before adopted execution or completion.");
        errors.AddRange(ValidateVerificationObligations(task.VerificationObligations, task.AcceptanceCriteria ?? [], requireClassification));
        return errors;
    }

    public static bool IsExecutable(CisFeatureStoryTask task)
        => task.Complexity?.Equals("low", StringComparison.OrdinalIgnoreCase) == true
            || task.Complexity?.Equals("medium", StringComparison.OrdinalIgnoreCase) == true;

    public static IReadOnlyList<string> ValidateVerificationObligations(IReadOnlyList<CisTaskVerificationObligation>? obligations,
        IReadOnlyList<string> criteria, bool required = true)
    {
        var errors = new List<string>();
        if (obligations is null || required && obligations.Count == 0)
            return ["Task verification obligations are unresolved; map every completion criterion to applicable native, artifact or review evidence."];
        if (obligations.Count == 0) return [];
        if (obligations.Count > 64) return ["Task verification obligations exceed the bounded contract limit."];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obligation in obligations)
        {
            if (obligation is null || !BoundedText(obligation.Id, 80) || !ids.Add(obligation.Id))
            { errors.Add("Task verification obligations need distinct nonempty instance identifiers."); continue; }
            if (obligation.Criteria is not { Count: > 0 and <= 64 } || obligation.Criteria.Any(value => !BoundedText(value, 8000) || !criteria.Contains(value, StringComparer.Ordinal))
                || obligation.Criteria.Distinct(StringComparer.Ordinal).Count() != obligation.Criteria.Count)
                errors.Add($"Verification obligation '{obligation.Id}' must reference exact current task completion criteria.");
            else covered.UnionWith(obligation.Criteria);
            if (!BoundedText(obligation.Description, 2000) || obligation.EvidenceExpectations is not { Count: > 0 and <= 32 }
                || obligation.EvidenceExpectations.Any(value => !BoundedText(value, 2000)))
                errors.Add($"Verification obligation '{obligation.Id}' needs its purpose and concrete evidence expectations.");
            if (obligation.EvidenceRoute is not ("native" or "artifact" or "review"))
                errors.Add($"Verification obligation '{obligation.Id}' has an unresolved evidence route; use native, artifact or review.");
            if (obligation.EvidenceRoute == "native" && obligation.SharedGateIds is not { Count: > 0 })
                errors.Add($"Native verification obligation '{obligation.Id}' must identify its existing engineering gates.");
            if (obligation.SharedGateIds is null || obligation.SharedGateIds.Count > 32 || obligation.SharedGateIds.Any(value => !BoundedText(value, 80))
                || obligation.BasisReferences is null || obligation.BasisReferences.Count > 64 || obligation.BasisReferences.Any(value => !BoundedText(value, 2000)))
                errors.Add($"Verification obligation '{obligation.Id}' has malformed gate or accepted-basis references.");
        }
        foreach (var missing in criteria.Where(criterion => !covered.Contains(criterion)))
            errors.Add($"Task completion criterion has no verification obligation: {missing}");
        return errors;
    }

    private static bool BoundedText(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Contains('\0');

    public static string Digest(CisStoryTaskWork work)
        => work.ContractVersion == 1 ? LegacyDigest(work) : "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                work.Slug,
                work.StoryId,
                work.Definition,
                work.Task,
                work.StoryCriteria,
                work.Direction,
                work.Constraints
            })));

    private static string LegacyDigest(CisStoryTaskWork work)
        => "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                work.Slug,
                work.StoryId,
                work.PlanHash,
                work.Definition,
                Task = new
                {
                    work.Task.Id,
                    work.Task.Title,
                    work.Task.Description,
                    work.Task.RepositoryIds,
                    work.Task.RequirementNumbers,
                    work.Task.AcceptanceCriteria,
                    work.Task.DependsOn
                },
                work.StoryCriteria,
                work.Direction,
                work.Constraints
            })));
}
