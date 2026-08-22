namespace Cis.Modules.Docs;

public sealed record DocumentationInventoryItem(
    string Id,
    string Path,
    string Title,
    string Type,
    string Status,
    string Authority,
    string MetadataSource,
    bool Cataloged,
    bool HasFrontMatter,
    bool MalformedFrontMatter);

public sealed record DocumentationInventoryResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    IReadOnlyList<DocumentationInventoryItem> Documents,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => Errors.Count == 0
        ? 0
        : RepositoryConfigurationValid
            ? 5
            : 2;
}
