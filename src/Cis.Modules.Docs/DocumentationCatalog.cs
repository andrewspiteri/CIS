namespace Cis.Modules.Docs;

public sealed record DocumentationCatalog(
    int SchemaVersion,
    IReadOnlyList<DocumentationCatalogEntry> Documents);

public sealed record DocumentationCatalogEntry(
    string Id,
    string Path,
    string Type,
    string Status,
    string Authority);

public sealed record DocumentationCatalogReadResult(
    DocumentationCatalog? Catalog,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Catalog is not null && Errors.Count == 0;
}
