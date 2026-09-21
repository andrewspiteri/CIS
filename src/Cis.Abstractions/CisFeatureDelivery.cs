namespace Cis.Abstractions;

public sealed record CisFeatureDeliveryEvidence(string Id, string RepositoryId, string Path, string ContentHash, string Excerpt);
public sealed record CisFeatureDeliveryStory(string Id, string Phase, string Title, string Treatment, string ExistingCapability,
    string RemainingWork, IReadOnlyList<string> Owners, IReadOnlyList<string> EvidenceIds, string? Conflict);
public sealed record CisFeatureDeliveryResult(string Status, string? InputHash, IReadOnlyList<CisFeatureDeliveryStory> Stories,
    IReadOnlyList<CisFeatureDeliveryEvidence> Evidence, IReadOnlyDictionary<string, string> SuggestedAnswers,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, bool Cached = false)
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}
