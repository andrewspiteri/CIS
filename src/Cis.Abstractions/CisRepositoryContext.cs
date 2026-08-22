namespace Cis.Abstractions;

public sealed record CisRepositoryContext(
    string RepositoryPath,
    string RepositoryId,
    string DocumentationRoot,
    string DocumentationPath,
    string CatalogPath);
