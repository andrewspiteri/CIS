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

public sealed record TechnicalIntentQuestion(
    string Id,
    string Area,
    string Question,
    string Why,
    IReadOnlyList<string> CommonOptions,
    string SuggestedAnswer,
    string Status,
    string? Answer,
    string? AnsweredBy,
    string? AnsweredAtUtc,
    string ResolutionSource = "human",
    string? Confidence = null,
    IReadOnlyList<string>? Evidence = null);

public sealed record TechnicalIntentQuestionnaireResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? CanonicalPath,
    string? BrdVersion,
    bool Current,
    bool Complete,
    int AnsweredCount,
    int UnansweredCount,
    IReadOnlyList<TechnicalIntentQuestion> Questions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked" ? 5 : Errors.Count > 0 ? 2 : Status == "missing" ? 4 : Current ? 0 : 5;
}
