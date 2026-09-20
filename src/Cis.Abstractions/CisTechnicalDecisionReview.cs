namespace Cis.Abstractions;

public sealed record CisTechnicalDecisionReview(
    string Id, string Decision, string RequiredBefore, string Status, string Rationale,
    bool NeedsReview, int? DocumentLine, IReadOnlyList<string> Issues)
{
    public string? ReviewToken { get; init; }
    public IReadOnlyList<CisTechnicalDecisionAnswer> RelatedAnswers { get; init; } = [];
    public bool QuestionnaireOverlap { get; init; }
    public string? RemainingReview { get; init; }
    public string? SuggestedResolution { get; init; }
    public string? RecordedResolution { get; init; }
    public string? RecordedReason { get; init; }
    public string? RecordedBy { get; init; }
}
public sealed record CisTechnicalDecisionAnswer(string Id, string Area, string Answer, string Status, string? AnsweredBy);
public sealed record CisTechnicalDecisionResolution(string Id, string Resolution, string? Reason, string ReviewToken, string Actor);
