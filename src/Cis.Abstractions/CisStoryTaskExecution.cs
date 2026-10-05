namespace Cis.Abstractions;

public sealed record CisStoryTaskWork(string WorkspacePath, string Slug, string StoryId, string PlanHash,
    string Definition, CisFeatureStoryTask Task, IReadOnlyList<string> StoryCriteria,
    IReadOnlyList<CisWorkspaceRepository> Repositories, string Actor)
{
    public string Direction { get; init; } = "";
    public string Constraints { get; init; } = "";
    public CisStoryExecutionResult? PreviousExecution { get; init; }
    public IReadOnlyList<CisStoryReviewFeedback> ReviewResponses { get; init; } = [];
}
public sealed record CisStoryReviewFeedback(string Id, string Finding, string? Answer, string? Actor, string? SavedAt)
{
    public string? SuggestedAnswer { get; init; }
    public string? SuggestionReason { get; init; }
    public string? SuggestionModel { get; init; }
}
public sealed record CisStoryFeedbackRequest(string Slug, string StoryId, string TaskId, string ExpectedPlanHash,
    string ExpectedRevision, string Actor, Dictionary<string, string> Answers);
public sealed record CisStoryModelChoice(string Provider, string Model, bool IsLocal);
public sealed record CisStoryExecutionPlan(string Hash, string Complexity, IReadOnlyList<string> Reasons,
    CisStoryModelChoice? Implementation, CisStoryModelChoice? Review, IReadOnlyList<string> Errors);
public sealed record CisStoryExecutionRun(string RunId, string RepositoryId, string Stage, string Provider,
    string Model, string Status, string WorkingDirectory, string Summary, IReadOnlyList<string> Validations)
{
    public int Round { get; init; } = 1;
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}
public sealed record CisStoryExecutionResult(string Status, CisStoryExecutionPlan Selection,
    IReadOnlyList<CisStoryExecutionRun> Runs, IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Findings,
    IReadOnlyList<string> Errors, bool Applied);

public interface ICisStoryTaskExecutor
{
    CisStoryExecutionPlan SelectModels(CisStoryTaskWork work);
    CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote,
        Func<bool> stillCurrent, CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress);
}
