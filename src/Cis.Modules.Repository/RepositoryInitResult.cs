namespace Cis.Modules.Repository;

public sealed record RepositoryInitResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    RepositoryClassification? Classification,
    IReadOnlyList<RepositoryStarterSelection> StarterSelections,
    IReadOnlyList<string> DirectoriesToCreate,
    IReadOnlyList<string> FilesToCreate,
    IReadOnlyList<string> FilesToUpdate,
    IReadOnlyList<string> QuarantinedPaths,
    IReadOnlyList<string> RetainedPaths,
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
