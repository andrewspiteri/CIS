using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed record BrdCandidate(
    string Id,
    string Kind,
    string RepositoryId,
    string RepositoryPath,
    string Path,
    string ContentHash,
    IReadOnlyList<string> Signals,
    bool Canonical,
    string? DefaultAssessment = null,
    string? DefaultRationale = null,
    bool MaterialSourceDrift = false);

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
    public IReadOnlyList<string> DeferredRepositoryIds { get; init; } = [];

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
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<CisBrdSourceReview> SourceReviews { get; init; } = [];
    public IReadOnlyList<string> ContentIssues { get; init; } = [];
    public int UnansweredQuestionCount { get; init; }
}

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

public sealed record BrdQuestion(
    string Id,
    int Ordinal,
    string Question,
    string? Answer,
    string? AnsweredBy,
    string? AnsweredAtUtc,
    string Status);

public sealed record BrdQuestionsResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? CanonicalPath,
    IReadOnlyList<BrdQuestion> Questions,
    IReadOnlyList<string> Errors,
    bool Applied,
    string? AnswerDigest = null)
{
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
    public int UnansweredCount => Questions.Count(item => item.Status == "Unanswered");
}

public sealed record BrdQuestionContext(
    string Id,
    string Section,
    string Excerpt);

public sealed record BrdQuestionGuidance(
    string Id,
    int Ordinal,
    string Question,
    string? Answer,
    string? AnsweredBy,
    string? AnsweredAtUtc,
    string Status,
    IReadOnlyList<BrdQuestionContext> Context,
    string? SuggestedAnswer,
    string? SuggestionConfidence,
    string? SuggestionReason,
    IReadOnlyList<string> SuggestionContextIds);

public sealed record BrdQuestionSuggestion(
    string QuestionId,
    string? Answer,
    string Confidence,
    string Reason,
    IReadOnlyList<string> ContextIds);

public sealed record BrdQuestionSuggestionDocument(
    int SchemaVersion,
    string BrdSha256,
    string InputSha256,
    string Provider,
    string Model,
    string GeneratedAtUtc,
    IReadOnlyList<BrdQuestionSuggestion> Suggestions);

public sealed record BrdQuestionGuidanceResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? CanonicalPath,
    string SuggestionStatus,
    string? SuggestionProvider,
    string? SuggestionModel,
    string? SuggestedAtUtc,
    IReadOnlyList<BrdQuestionGuidance> Questions,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
    public int UnansweredCount => Questions.Count(item => item.Status == "Unanswered");
    public int SuggestedCount => Questions.Count(item => !string.IsNullOrWhiteSpace(item.SuggestedAnswer));
}

public sealed record BrdReviewDispositionResult(
    string Status,
    string? WorkspacePath,
    string? CanonicalPath,
    Cis.Abstractions.CisBrdReviewDispositionDocument? Disposition,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
    public int PendingCount => Disposition?.Findings.Count(item => item.Decision == "pending") ?? 0;
    public int AcceptedCount => Disposition?.Findings.Count(item => item.Decision == "accepted") ?? 0;
    public int RejectedCount => Disposition?.Findings.Count(item => item.Decision == "rejected") ?? 0;
}
