namespace Cis.Modules.Docs;

public sealed record DocumentationValidationResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    int CatalogEntries,
    int MarkdownDocuments,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool Strict,
    bool RepositoryConfigurationValid)
{
    public bool IsValid => Errors.Count == 0 && (!Strict || Warnings.Count == 0);

    public int ExitCode => !RepositoryConfigurationValid
        ? 2
        : IsValid
            ? 0
            : 5;
}
