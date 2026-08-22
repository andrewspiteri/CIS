namespace Cis.Abstractions;

public sealed record CisGraphDocument(
    int SchemaVersion,
    CisGraphBuildMetadata Build,
    IReadOnlyList<CisGraphNode> Nodes,
    IReadOnlyList<CisGraphEdge> Edges,
    CisGraphDiagnosticSummary DiagnosticSummary);

public sealed record CisGraphBuildMetadata(
    string Id,
    string RepositoryId,
    string? Head,
    bool Dirty,
    string Status);

public sealed record CisGraphNode(
    string Key,
    string RepositoryId,
    string Kind,
    string Subtype,
    string LocalId,
    string Label,
    IReadOnlyList<string> Facets,
    string Authority,
    string Lifecycle,
    IReadOnlyList<CisGraphLocation> Locations,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<CisGraphEvidence> Provenance);

public sealed record CisGraphEdge(
    string Key,
    string Type,
    string From,
    string To,
    string State,
    string Confidence,
    IReadOnlyList<CisGraphEvidence> Observations,
    IReadOnlyDictionary<string, string> Properties);

public sealed record CisGraphLocation(
    string Path,
    string LocatorKind,
    string LocatorValue);

public sealed record CisGraphEvidence(
    string SourceKind,
    string Path,
    string LocatorKind,
    string LocatorValue,
    string? ContentHash,
    string Method,
    string Extractor,
    string Confidence,
    string? ConfidenceReason);

public sealed record CisGraphDiagnostic(
    string Code,
    string Severity,
    string Message,
    IReadOnlyList<string> Evidence);

public sealed record CisGraphDiagnosticSummary(
    int Errors,
    int Warnings,
    int Information);

public sealed record CisGraphManifest(
    int SchemaVersion,
    string BuildId,
    string RepositoryId,
    string DocumentationRoot,
    string? Head,
    bool Dirty,
    IReadOnlyList<CisGraphInput> Inputs,
    IReadOnlyList<string> Extractors);

public sealed record CisGraphInput(
    string Path,
    string Hash);
