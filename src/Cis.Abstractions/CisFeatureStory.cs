namespace Cis.Abstractions;

public sealed record CisFeatureStoryTask(string Id, string Title, string Description,
    IReadOnlyList<string> RepositoryIds, IReadOnlyList<int> RequirementNumbers,
    IReadOnlyList<string> AcceptanceCriteria, IReadOnlyList<string> DependsOn);

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
    public IReadOnlyList<CisStoryCompletionContext> CompletionContexts { get; init; } = [];
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

public sealed record CisFeatureStoryTaskProgress(string Id, string Status, bool CanStart, bool CanComplete,
    string? BlockedReason, string? Evidence)
{
    public CisStoryExecutionResult? Execution { get; init; }
    public bool CanExecute { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CisStoryReviewFeedback>? ReviewFeedback { get; init; }
}
public sealed record CisFeatureStoryTaskLink(string Id, string Title, string Status, IReadOnlyList<string> RepositoryIds);
