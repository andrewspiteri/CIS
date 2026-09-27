namespace Cis.Modules.Repository;

public sealed record RepositoryImportEntryResult(
    string Id,
    string RepositoryPath,
    string DocumentationRoot,
    string Participation,
    string Relationship,
    IReadOnlyList<string> ComponentScope,
    string Status,
    IReadOnlyList<string> FilesToCreate,
    IReadOnlyList<string> FilesToUpdate,
    IReadOnlyList<string> Warnings);

public sealed record RepositoryImportResult(
    string Status,
    string? WorkspacePath,
    string? ConfigurationPath,
    IReadOnlyList<RepositoryImportEntryResult> Repositories,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> Errors,
    bool ConfirmationRequired,
    bool Applied)
{
    public IReadOnlyList<RepositoryFileMerge> FileMerges { get; init; } = [];
    public string? MergeReviewHash { get; init; }
    public IReadOnlyList<Cis.Abstractions.CisAiProviderStatus> GuidanceProviders { get; init; } = [];
    public int RemoteReviewBatchCount { get; init; }
    public int RemoteReviewConcurrency { get; init; } = RepositoryGuidanceReviewRunner.RemoteConcurrency;
    public int ReviewBudgetSeconds { get; init; } = RepositoryGuidanceReviewSession.BudgetSeconds;
    public bool ReviewPaused { get; init; }
    public string GuidanceMode { get; init; } = "reconcile";
    public IReadOnlyList<RepositoryImportAssessment> Assessments { get; init; } = [];
    public IReadOnlyList<RepositoryImportPreview> Previews { get; init; } = [];

    public int ExitCode => Errors.Count > 0
        ? 2
        : Collisions.Count > 0
            ? 4
            : ConfirmationRequired
                ? 3
                : 0;
}
