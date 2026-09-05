namespace Cis.Modules.Repository;

public sealed record RepositoryImportEntryResult(
    string Id,
    string RepositoryPath,
    string DocumentationRoot,
    string Participation,
    string Relationship,
    IReadOnlyList<string> ComponentScope,
    string Status,
    IReadOnlyList<string> FilesToCreate,
    IReadOnlyList<string> FilesToUpdate,
    IReadOnlyList<string> Warnings);

public sealed record RepositoryImportResult(
    string Status,
    string? WorkspacePath,
    string? ConfigurationPath,
    IReadOnlyList<RepositoryImportEntryResult> Repositories,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> Errors,
    bool ConfirmationRequired,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0
        ? 2
        : Collisions.Count > 0
            ? 4
            : ConfirmationRequired
                ? 3
                : 0;
}
