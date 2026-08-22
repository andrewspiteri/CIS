namespace Cis.Modules.Repository;

public sealed record RepositoryInitRequest(
    string RepositoryPath,
    string DocumentationRoot,
    bool DryRun,
    bool Confirmed,
    bool AcceptCurrent = false,
    bool QuarantineObsolete = false,
    bool WorkspaceAuthority = false);
