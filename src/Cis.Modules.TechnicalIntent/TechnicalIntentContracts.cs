namespace Cis.Modules.TechnicalIntent;

public sealed record TechnicalIntentBaseline(
    string Kind,
    string Id,
    string Version,
    string Evidence);

public sealed record TechnicalIntentValidation(
    bool Valid,
    bool Current,
    string EffectiveStatus,
    string DocumentStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record TechnicalIntentResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? CanonicalPath,
    TechnicalIntentValidation? Validation,
    IReadOnlyList<TechnicalIntentBaseline> Baselines,
    IReadOnlyList<string> Warnings,
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
