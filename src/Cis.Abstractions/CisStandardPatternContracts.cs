namespace Cis.Abstractions;

public interface ICisStandardPatternProvider
{
    string ProviderKey { get; }

    IReadOnlyList<CisStandardPatternDefinition> Patterns { get; }
}

public sealed record CisStandardPatternDefinition(
    string Id,
    int Version,
    string Title,
    string Description,
    string Status,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RequiredGraphCapabilities,
    CisStandardPatternSelector Subject,
    IReadOnlyList<CisStandardPatternRelation> Requires,
    IReadOnlyList<CisStandardPatternRelation> Forbids,
    int MinimumOccurrences,
    double MinimumConsistency,
    CisStandardPatternCandidate Candidate,
    string Provider,
    string Authority,
    string Source);

public sealed record CisStandardPatternSelector(
    string? Kind,
    string? Subtype,
    IReadOnlyList<string> Facets,
    IReadOnlyDictionary<string, string> PropertyEquals,
    IReadOnlyDictionary<string, string> PropertyContains,
    IReadOnlyList<string> PathPrefixes,
    IReadOnlyList<string> ExcludedPathPrefixes);

public sealed record CisStandardPatternRelation(
    string Direction,
    string EdgeType,
    CisStandardPatternSelector Target);

public sealed record CisStandardPatternCandidate(
    string Target,
    string ProposedLevel,
    string Wording,
    string SuggestedEnforcement,
    string Verification);
