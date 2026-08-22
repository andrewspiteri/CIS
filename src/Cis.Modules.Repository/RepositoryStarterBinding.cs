namespace Cis.Modules.Repository;

public sealed record RepositoryStarterSelection(
    string Definition,
    string Reason,
    IReadOnlyList<string> Evidence);

internal sealed record RepositoryStarterArtifact(
    string Id,
    string Definition,
    string RelativePath,
    string Content,
    CatalogArtifactEntry? CatalogEntry);

public sealed record CatalogArtifactEntry(
    string Id,
    string Path,
    string Type,
    string Status,
    string Authority);

internal sealed record RepositoryStarterBinding(
    IReadOnlyList<RepositoryStarterSelection> Selections,
    IReadOnlyList<RepositoryStarterArtifact> Artifacts);
