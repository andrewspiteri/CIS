namespace Cis.Abstractions;

public sealed record CisFeatureDeliveryEvidence(string Id, string RepositoryId, string Path, string ContentHash, string Excerpt)
{
    public IReadOnlyList<string> StoryIds { get; init; } = [];
    public string Kind { get; init; } = "related-code";
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> Symbols { get; init; } = [];
    public IReadOnlyList<int> RequirementNumbers { get; init; } = [];
}
public sealed record CisFeatureDeliveryStory(string Id, string Phase, string Title, string Treatment, string ExistingCapability,
    string RemainingWork, IReadOnlyList<string> Owners, IReadOnlyList<string> EvidenceIds, string? Conflict)
{
    public string AssessmentState { get; init; } = "not-assessed";
    public string AssessmentReason { get; init; } = "";
    public IReadOnlyList<string> Requirements { get; init; } = [];
    public IReadOnlyList<CisFeatureDeliveryRequirementCheck> RequirementChecks { get; init; } = [];
    public CisFeatureDeliveryReview? Review { get; init; }
    public bool ReviewCurrent { get; init; }
}

public sealed record CisFeatureDeliveryRequirementCheck(int Number, string Requirement, IReadOnlyList<string> EvidenceIds);
public sealed record CisFeatureDeliveryResult(string Status, string? InputHash, IReadOnlyList<CisFeatureDeliveryStory> Stories,
    IReadOnlyList<CisFeatureDeliveryEvidence> Evidence, IReadOnlyDictionary<string, string> SuggestedAnswers,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, bool Cached = false)
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? FeatureRepositoryId { get; init; }
    public IReadOnlyList<string> RepositoryIds { get; init; } = [];
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

public sealed record CisFeatureDeliveryReviewRequest(string StoryId, string Treatment, IReadOnlyList<string> Owners,
    string Plan, IReadOnlyList<string> EvidencePaths, string ExpectedInputHash);
public sealed record CisFeatureDeliveryReview(string StoryId, string Title, string Treatment, IReadOnlyList<string> Owners,
    string Plan, IReadOnlyDictionary<string, string> EvidenceHashes, string InputHash, string Actor, string SavedAt);
