namespace Cis.Modules.Feedback;

public sealed record ToolUsageEntry(
    int SchemaVersion,
    string InvocationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    long ElapsedMilliseconds,
    string Command,
    IReadOnlyList<string> Options,
    int ExitCode,
    int StandardOutputCharacters,
    int StandardErrorCharacters,
    int OutputEstimatedTokens,
    int BaselineEstimatedTokens,
    int ActualEstimatedTokens,
    int PossibleTokenSavings,
    double PossibleTokenSavingsPercent,
    string SavingsBasis,
    string SavingsConfidence);

public sealed record FeedbackSummary(
    string Status,
    int ExitCode,
    string? LedgerPath,
    DateTimeOffset? SinceUtc,
    int InvocationCount,
    int SuccessfulCount,
    int FailedCount,
    long ElapsedMilliseconds,
    int OutputEstimatedTokens,
    int BaselineEstimatedTokens,
    int ActualEstimatedTokens,
    int PossibleTokenSavings,
    double PossibleTokenSavingsPercent,
    int EstimatedInvocationCount,
    IReadOnlyList<FeedbackCommandSummary> Commands,
    IReadOnlyList<string> Errors);

public sealed record FeedbackCommandSummary(
    string Command,
    int InvocationCount,
    int FailedCount,
    long ElapsedMilliseconds,
    int OutputEstimatedTokens,
    int PossibleTokenSavings);

public sealed record FeedbackUsageResult(
    string Status,
    int ExitCode,
    string? LedgerPath,
    IReadOnlyList<ToolUsageEntry> Entries,
    IReadOnlyList<string> Errors);

public sealed record FeedbackOpportunityResult(
    string Status,
    int ExitCode,
    string? LedgerPath,
    IReadOnlyList<FeedbackOpportunity> Opportunities,
    IReadOnlyList<string> Errors);

public sealed record FeedbackOpportunity(
    string Code,
    string Severity,
    string Command,
    string Message,
    string SuggestedAction,
    IReadOnlyList<string> Evidence);
