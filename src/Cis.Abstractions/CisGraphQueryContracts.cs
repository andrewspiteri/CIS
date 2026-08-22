namespace Cis.Abstractions;

public interface ICisGraphQueryService
{
    CisGraphQueryResult Find(string repositoryPath, CisGraphFindRequest request);

    CisGraphQueryResult Related(string repositoryPath, CisGraphRelatedRequest request);

    CisGraphTraceResult Trace(string repositoryPath, CisGraphTraceRequest request);
}

public sealed record CisGraphFindRequest(
    string? Id,
    string? Kind,
    string? Subtype,
    string? Facet,
    string? Text,
    int Limit);

public sealed record CisGraphRelatedRequest(
    string Id,
    string? Kind,
    IReadOnlyList<string> EdgeTypes,
    string Direction,
    int Depth,
    int Limit,
    bool IncludeProposed);

public sealed record CisGraphTraceRequest(
    string From,
    string To,
    string? FromKind,
    string? ToKind,
    IReadOnlyList<string> EdgeTypes,
    string Direction,
    int MaxDepth,
    int MaxPaths,
    bool IncludeProposed);

public sealed record CisGraphQueryResult(
    string Status,
    string? RepositoryPath,
    string? GraphPath,
    CisGraphBuildMetadata? Build,
    string Freshness,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyList<CisGraphNode> Nodes,
    IReadOnlyList<CisGraphTraversal> Traversals,
    bool Truncated,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    bool RepositoryConfigurationValid,
    bool GraphAvailable,
    bool QueryValid)
{
    public int ExitCode => !RepositoryConfigurationValid || !QueryValid
        ? 2
        : !GraphAvailable
            ? 4
            : Diagnostics.Any(diagnostic => diagnostic.Severity == "error")
                ? 5
                : 0;
}

public sealed record CisGraphTraversal(
    int Depth,
    string Direction,
    CisGraphEdge Edge);

public sealed record CisGraphPath(
    IReadOnlyList<CisGraphNode> Nodes,
    IReadOnlyList<CisGraphTraversal> Traversals);

public sealed record CisGraphTraceResult(
    string Status,
    string? RepositoryPath,
    string? GraphPath,
    CisGraphBuildMetadata? Build,
    string Freshness,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyList<CisGraphPath> Paths,
    bool Truncated,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    bool RepositoryConfigurationValid,
    bool GraphAvailable,
    bool QueryValid)
{
    public int ExitCode => !RepositoryConfigurationValid || !QueryValid
        ? 2
        : !GraphAvailable
            ? 4
            : Diagnostics.Any(diagnostic => diagnostic.Severity == "error")
                ? 5
                : 0;
}
