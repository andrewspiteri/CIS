namespace Cis.Modules.Brd;

public sealed record BrdBacklogItem(
    string Id,
    string RequirementId,
    string Outcome,
    string Priority,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<string> FrontendTypes,
    IReadOnlyList<string> DependsOn,
    string FeatureSpecification,
    string Notes);

public sealed record BrdBacklogValidation(
    bool Valid,
    bool Current,
    string EffectiveStatus,
    string DocumentStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record BrdBacklogResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? RelativePath,
    IReadOnlyList<BrdBacklogItem> Items,
    BrdBacklogValidation? Validation,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked"
        ? 5
        : Errors.Count > 0
        ? 2
        : Status == "missing"
            ? 4
            : Validation is { Valid: false } && Status is "validated" or "blocked"
                ? 5
                : 0;
}

public sealed record BrdFeatureSpecificationResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? ItemId,
    string? RequirementId,
    string? RelativePath,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked"
        ? 5
        : Errors.Count > 0
            ? 2
            : Status == "missing"
                ? 4
                : 0;
}

public sealed record BrdFeatureValidation(
    bool Valid,
    bool Current,
    string EffectiveStatus,
    string DocumentStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record BrdFeatureResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? ItemId,
    string? RequirementId,
    string? RelativePath,
    BrdFeatureValidation? Validation,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked"
        ? 5
        : Errors.Count > 0
            ? 2
            : Status == "missing"
                ? 4
                : Validation is { Valid: false } && Status == "validated"
                    ? 5
                    : 0;
}
