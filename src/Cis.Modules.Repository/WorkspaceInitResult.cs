namespace Cis.Modules.Repository;

public sealed record WorkspaceInitResult(
    string Status,
    string? WorkspacePath,
    string? ConfigurationPath,
    string? AuthorityRepositoryId,
    string? DocumentationRoot,
    RepositoryInitResult? RepositoryInitialization,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> Errors,
    bool ConfirmationRequired,
    bool Applied,
    Cis.Abstractions.CisEcosystem? Ecosystem = null,
    Cis.Abstractions.CisProduct? Product = null)
{
    public int ExitCode => Errors.Count > 0
        ? 2
        : Collisions.Count > 0
            ? 4
            : ConfirmationRequired
                ? 3
                : 0;
}
