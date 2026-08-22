namespace Cis.Modules.Brd;

public sealed record BrdCandidate(
    string Id,
    string Kind,
    string RepositoryId,
    string RepositoryPath,
    string Path,
    string ContentHash,
    IReadOnlyList<string> Signals,
    bool Canonical);

public sealed record BrdRepositoryBaseline(
    string RepositoryId,
    string RepositoryPath,
    string Role,
    string? BuildId,
    string? Head,
    bool Dirty,
    string Freshness,
    string GraphStatus);

public sealed record BrdDiscoveryResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? CanonicalPath,
    IReadOnlyList<BrdCandidate> Candidates,
    IReadOnlyList<BrdRepositoryBaseline> Baselines,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count > 0
        ? Errors.Any(error => error.Contains("graph", StringComparison.OrdinalIgnoreCase)) ? 4 : 2
        : 0;
}

public sealed record BrdValidation(
    bool Valid,
    bool Current,
    string EffectiveStatus,
    string DocumentStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record BrdResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? CanonicalPath,
    BrdDiscoveryResult? Discovery,
    BrdValidation? Validation,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "collision"
        ? 4
        : Errors.Count > 0
            ? 2
            : Status == "missing"
                ? 4
                : Validation is { Valid: false } && Status is "invalid" or "blocked" or "validated"
            ? 5
            : 0;
}
