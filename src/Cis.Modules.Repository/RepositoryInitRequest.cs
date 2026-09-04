namespace Cis.Modules.Repository;

public sealed record RepositoryInitRequest(
    string RepositoryPath,
    string DocumentationRoot,
    bool DryRun,
    bool Confirmed,
    bool AcceptCurrent = false,
    bool QuarantineObsolete = false,
    bool WorkspaceAuthority = false);

public sealed record RepositoryReferenceSeedResult(
    string Status,
    IReadOnlyList<string> CreatedPaths,
    IReadOnlyList<string> Collisions,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 || Collisions.Count > 0 ? 5 : 0;
}
