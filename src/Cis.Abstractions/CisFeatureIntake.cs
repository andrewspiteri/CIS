namespace Cis.Abstractions;

public sealed record CisFeatureIntakeRequest(
    string Title, string Slug, string SourcePath, string RepositoryMode, string RepositoryPath,
    string DocumentationRoot, IReadOnlyList<string> IntegrationRepositories, string Actor);

public sealed record CisFeatureIntakePlan(
    string Title, string Slug, string RepositoryPath, string RepositoryMode, string DocumentationRoot,
    string RequestPath, string SourcePath, string SourceHash, IReadOnlyList<string> IntegrationRepositories,
    IReadOnlyList<string> OpenDecisions, string? ProductDefinitionHash, string PlanHash);

public sealed record CisFeatureIntakeResult(
    string Status, CisFeatureIntakePlan? Plan, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, bool Applied, bool ConfirmationRequired = false)
{
    public int ExitCode => Errors.Count > 0 ? 5 : ConfirmationRequired ? 3 : 0;
}

public sealed record CisFeatureWizardField(string Id, string Label, string SuggestedAnswer, string? Answer, bool Required);
public sealed record CisFeatureWizardDocument(string Title, string Path, bool Exists);
public sealed record CisFeatureWizardPage(string Id, string Title, string Status, bool Complete,
    IReadOnlyList<string> Attention, IReadOnlyList<CisFeatureWizardField> Fields, IReadOnlyList<CisFeatureWizardDocument> Documents);
public sealed record CisFeatureWizardResult(string Status, CisFeatureIntakePlan? Plan, string? Revision,
    bool Reviewed, bool BaselineCurrent, IReadOnlyList<CisFeatureWizardPage> Pages, IReadOnlyList<string> Errors, bool Applied)
{
    public IReadOnlyList<CisFeatureRepositoryWork> RepositoryWork { get; init; } = [];
    public IReadOnlyList<CisWorkspaceRepository> Repositories { get; init; } = [];
    public IReadOnlyList<CisFeatureWizardDocument> SourceHistory { get; init; } = [];
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}
public sealed record CisFeatureWizardAnswer(string Slug, string Page, Dictionary<string, string> Answers, string Actor, string ExpectedRevision)
{
    public IReadOnlyList<CisFeatureRepositoryWork>? RepositoryWork { get; init; }
    public CisFeatureScreenReviewRequest? ScreenReview { get; init; }
}

public sealed record CisFeatureRepositoryWork(string Id, string RepositoryId, string Title, string Scope,
    IReadOnlyList<string> DependsOn, IReadOnlyList<string> ChangeIds);
public sealed record CisFeatureNavigationEntry(CisFeatureIntakePlan Plan, string Status, int ReviewedPages,
    int TotalPages, string NextPage, IReadOnlyList<CisFeatureRepositoryWork> RepositoryWork, IReadOnlyList<string> Errors);
public sealed record CisFeatureNavigationResult(CisWorkspace? Workspace, IReadOnlyList<CisFeatureNavigationEntry> Features,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

public sealed record CisFeatureSourceUpdatePlan(string Slug, string Title, string IncomingPath,
    string PreviousSourcePath, string SourcePath, string PreviousRequestPath, string PreviousSourceHash,
    string SourceHash, string PlanHash, long PreviousBytes, long IncomingBytes,
    IReadOnlyList<string> AddedDecisions, IReadOnlyList<string> RemovedDecisions,
    int RetainedDecisionAnswers, IReadOnlyList<string> PagesRequiringReview);

public sealed record CisFeatureSourceUpdateResult(string Status, CisFeatureSourceUpdatePlan? Plan,
    IReadOnlyList<string> Errors, bool Applied, bool ConfirmationRequired = false)
{
    public int ExitCode => Errors.Count > 0 ? 5 : ConfirmationRequired ? 3 : 0;
}
