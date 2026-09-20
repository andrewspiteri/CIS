namespace Cis.Abstractions;

/// <summary>Read-only navigation and review state for a discovered BRD source.</summary>
public sealed record CisBrdSourceReview(
    string Id, string RepositoryId, string RepositoryPath, string Path,
    string Assessment, string Rationale, bool NeedsReview, int? AssessmentLine,
    IReadOnlyList<string> Issues)
{
    public bool RequiresReconciliation { get; init; }
    public string? ReviewToken { get; init; }
    public CisBrdSourceSummary? Summary { get; init; }
}

public sealed record CisBrdSourceSummary(string Text, string Kind, string ContentHash,
    bool CanGenerate, bool InputTruncated = false, string? Provider = null, string? Model = null);

public sealed record CisBrdSourceSummaryEntry(string Id, CisBrdSourceSummary Summary);

public sealed record CisBrdSourceSummariesResult(string Status, IReadOnlyList<CisBrdSourceSummaryEntry> Sources,
    int Generated, int Reused, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 2;
}

/// <summary>One explicit human source assessment, bound to the reviewed source and row.</summary>
public sealed record CisBrdSourceDecision(string Id, string Assessment, string Reason, string ReviewToken);
