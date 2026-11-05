namespace Cis.Abstractions;

public sealed record CisStoryTaskWork(string WorkspacePath, string Slug, string StoryId, string PlanHash,
    string Definition, CisFeatureStoryTask Task, IReadOnlyList<string> StoryCriteria,
    IReadOnlyList<CisWorkspaceRepository> Repositories, string Actor)
{
    public int ContractVersion { get; init; } = 2;
    public bool NeedsVerificationRenewal { get; init; }
    public string Direction { get; init; } = "";
    public string Constraints { get; init; } = "";
    public CisStoryExecutionResult? PreviousExecution { get; init; }
    public IReadOnlyList<CisStoryReviewFeedback> ReviewResponses { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisStoryWorkBinding? CanonicalBinding { get; init; }

    public CisFeatureStoryTask ForCanonicalGate()
    {
        if (CanonicalBinding is null) return Task;
        if (Task.ChangeId is not null && Task.ChangeId != CanonicalBinding.ChangeId
            || Task.WorkOwnerTaskId is not null && Task.WorkOwnerTaskId != CanonicalBinding.WorkOwnerTaskId)
            throw new InvalidDataException("The recorded story association conflicts with its existing canonical task reference.");
        return Task with { ChangeId = CanonicalBinding.ChangeId, WorkOwnerTaskId = CanonicalBinding.WorkOwnerTaskId };
    }
}

/// <summary>An existing approved canonical task association, separate from implementation meaning.</summary>
public sealed record CisStoryWorkBinding(string ChangeId, string WorkOwnerTaskId);

public sealed record CisStoryWorkBindingRequest(string Slug, string StoryId, string TaskId, string ExpectedPlanHash,
    string ExpectedRevision, string ChangeId, string WorkOwnerTaskId, string Actor, string Reason);

public sealed record CisStoryWorkStatus(string Status, bool Required, bool BindingCurrent,
    CisAgentWorkIdentity? Identity, CisAgentWorkResult? Control, string NextAction, IReadOnlyList<string> Errors);

/// <summary>Structured task-review meaning with associations stamped by CIS.</summary>
public sealed record CisStoryReviewFinding(string Id, string Severity, string? Disposition, string Category,
    string Location, string Observation, string Recommendation, string RepositoryId, string RunId,
    string MaterialBasis, IReadOnlyList<string> BasisReferences)
{
    public bool RequiresDecision { get; init; }
}
public sealed record CisStoryReviewFeedback(string Id, string Finding, string? Answer, string? Actor, string? SavedAt)
{
    public string? SuggestedAnswer { get; init; }
    public string? SuggestionReason { get; init; }
    public string? SuggestionModel { get; init; }
    public string? BasisHash { get; init; }
    public IReadOnlyList<string> RunIds { get; init; } = [];
    public string ApplicabilityState { get; init; } = "unanswered";
    public string? ApplicabilityReason { get; init; }
    public string? RetainedAnswer { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Disposition { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresDecision { get; init; }
}
public sealed record CisStoryFeedbackRequest(string Slug, string StoryId, string TaskId, string ExpectedPlanHash,
    string ExpectedRevision, string Actor, Dictionary<string, string> Answers)
{
    public IReadOnlyDictionary<string, string>? ExpectedBasisHashes { get; init; }
}
public sealed record CisStoryModelChoice(string Provider, string Model, bool IsLocal);
public sealed record CisStoryExecutionPlan(string Hash, string Complexity, IReadOnlyList<string> Reasons,
    CisStoryModelChoice? Implementation, CisStoryModelChoice? Review, IReadOnlyList<string> Errors)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisStoryWorkStatus? Work { get; init; }
}
public sealed record CisStoryExecutionRun(string RunId, string RepositoryId, string Stage, string Provider,
    string Model, string Status, string WorkingDirectory, string Summary, IReadOnlyList<string> Validations)
{
    public int Round { get; init; } = 1;
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrectionRoundId { get; init; }
}
public sealed record CisStoryExecutionResult(string Status, CisStoryExecutionPlan Selection,
    IReadOnlyList<CisStoryExecutionRun> Runs, IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Findings,
    IReadOnlyList<string> Errors, bool Applied)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisStoryWorkStatus? Work { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CisStoryReviewFinding>? StructuredFindings { get; init; }
}

public interface ICisStoryTaskExecutor
{
    CisStoryWorkStatus InspectWork(CisStoryTaskWork work)
        => new("unavailable", false, false, null, null,
            "Load the story work-control service to inspect canonical ownership and allowance.", []);
    CisStoryExecutionPlan SelectModels(CisStoryTaskWork work);
    CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote,
        Func<bool> stillCurrent, CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress);
}
