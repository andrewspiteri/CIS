using Cis.Abstractions;

namespace Cis.Modules.Context;

public sealed record ContextPackRequest(
    string RepositoryPath,
    string Id,
    string? Kind,
    string Purpose,
    string Verification,
    int Depth,
    int Limit,
    int MaxCharacters,
    bool IncludeProposed,
    string? OutputPath,
    bool Force,
    IReadOnlyList<ContextPackRoot>? AdditionalRoots = null);

public sealed record ContextPackRoot(
    string RepositoryPath,
    string Id,
    string? Kind);

public sealed record ContextPackResolvedRoot(
    string RepositoryPath,
    string RepositoryId,
    string RequestedId,
    CisGraphNode StartNode,
    CisGraphBuildMetadata Build,
    string Freshness);

public sealed record ContextPackSource(
    string RepositoryId,
    string RepositoryPath,
    string Path,
    IReadOnlyList<string> NodeKeys,
    IReadOnlyList<string> Reasons,
    string FactClass,
    int TotalCharacters,
    int SelectedCharacters,
    int IncludedCharacters,
    int EstimatedTokens,
    bool Truncated,
    int TotalEvidence,
    int OmittedEvidence,
    IReadOnlyList<ContextPackEvidence> Evidence,
    IReadOnlyList<ContextPackExcerpt> Excerpts);

public sealed record ContextPackEvidence(
    string? NodeKey,
    string? EdgeType,
    string? RelationshipState,
    string? Confidence,
    string Path,
    string LocatorKind,
    string LocatorValue,
    string? Method);

public sealed record ContextPackExcerpt(
    int StartLine,
    int EndLine,
    IReadOnlyList<string> Locators,
    int TotalCharacters,
    int IncludedCharacters,
    bool Truncated,
    string Content);

public sealed record ContextPackOmission(
    string? RepositoryId,
    string? RepositoryPath,
    string Path,
    string Reason,
    int? EstimatedCharacters);

public sealed record ContextPackResult(
    string Status,
    string? RepositoryPath,
    string? OutputPath,
    CisGraphBuildMetadata? Build,
    string Freshness,
    string Purpose,
    string Verification,
    CisGraphNode? StartNode,
    IReadOnlyList<ContextPackResolvedRoot> Roots,
    int Depth,
    int Limit,
    int MaxCharacters,
    int EstimatedTokens,
    IReadOnlyList<ContextPackSource> Sources,
    IReadOnlyList<ContextPackOmission> Omissions,
    bool GraphTruncated,
    bool ContentTruncated,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    bool Applied,
    bool RepositoryConfigurationValid,
    bool GraphAvailable,
    bool RequestValid)
{
    public int ExitCode => !RepositoryConfigurationValid || !RequestValid
        ? 2
        : !GraphAvailable
            ? 4
            : Diagnostics.Any(diagnostic => diagnostic.Severity == "error")
                ? 5
                : Status == "collision"
                    ? 4
                    : 0;
}
