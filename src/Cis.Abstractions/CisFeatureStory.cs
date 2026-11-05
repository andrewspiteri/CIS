namespace Cis.Abstractions;

public sealed record CisFeatureStoryTask(string Id, string Title, string Description,
    IReadOnlyList<string> RepositoryIds, IReadOnlyList<int> RequirementNumbers,
    IReadOnlyList<string> AcceptanceCriteria, IReadOnlyList<string> DependsOn)
{
    // Missing classification is retained for legacy records. Resolution must not
    // silently convert an adopted task into low-complexity implementation work.
    public string? Category { get; init; }
    public string? Complexity { get; init; }
    public string? TaskTypeKey { get; init; }
    public string? TaskTypeVersion { get; init; }
    public IReadOnlyList<string> Targets { get; init; } = [];
    public IReadOnlyList<string> DecisionIds { get; init; } = [];
    public IReadOnlyList<string> NegativeCriteria { get; init; } = [];
    public IReadOnlyList<string> RequiredOutputs { get; init; } = [];
    public string? FrontendType { get; init; }
    public string? ApprovalGate { get; init; }
    public string? Validation { get; init; }
    public string? StoryId { get; init; }
    public string? InstanceKey { get; init; }
    public string? WorkOwnerTaskId { get; init; }
    public string? ChangeId { get; init; }
    public IReadOnlyList<CisTaskVerificationObligation> VerificationObligations { get; init; } = [];
}

/// <summary>Links acceptance to existing native, artifact or review evidence routes.</summary>
public sealed record CisTaskVerificationObligation(string Id, IReadOnlyList<string> Criteria,
    string Description, string EvidenceRoute, IReadOnlyList<string> EvidenceExpectations)
{
    public IReadOnlyList<string> SharedGateIds { get; init; } = [];
    public IReadOnlyList<string> BasisReferences { get; init; } = [];
}

public sealed record CisFeatureStoryResult(string Status, string Slug, string StoryId, string? InputHash,
    string FeatureTitle, string Definition, CisFeatureDeliveryStory? Story, IReadOnlyList<CisFeatureStoryTask> Tasks,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public bool Cached { get; init; }
    public string PlanState { get; init; } = "Proposed";
    public string? PlanHash { get; init; }
    public string? Revision { get; init; }
    public string? ApprovedBy { get; init; }
    public IReadOnlyList<CisFeatureStoryTaskProgress> TaskProgress { get; init; } = [];
    public CisStoryExecutionPlan? ExecutionPlan { get; init; }
    public bool? Applied { get; init; }
    public IReadOnlyList<CisStoryCompletionContext> CompletionContexts { get; init; } = [];
    public CisStorySizeAssessment? DeliverySize { get; init; }
    public string DeliverySizeState { get; init; } = "unknown";
    public string DeliverySizeReason { get; init; } = "";
    public string? PlanningBasisHash { get; init; }
    public string PlanningRevision { get; init; } = "none";
    public string? ParentStoryId { get; init; }
    public bool IsDeliveryContainer { get; init; }
    public bool IsFutureCandidate { get; init; }
    public bool IsRevisionProposal { get; init; }
    public IReadOnlyList<CisFeatureStoryChild> ChildStories { get; init; } = [];
    public IReadOnlyList<CisFeatureStoryPlanHistory> PreviousPlans { get; init; } = [];
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

public sealed record CisFeatureStoryTaskProgress(string Id, string Status, bool CanStart, bool CanComplete,
    string? BlockedReason, string? Evidence)
{
    public CisStoryExecutionResult? Execution { get; init; }
    public bool CanExecute { get; init; }
    public bool NeedsVerificationRenewal { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisStoryWorkStatus? Work { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CisStoryReviewFeedback>? ReviewFeedback { get; init; }
}
public sealed record CisFeatureStoryTaskLink(string Id, string Title, string Status, IReadOnlyList<string> RepositoryIds);
