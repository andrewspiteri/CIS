namespace Cis.Modules.Repository;

public sealed record WorkspaceInitRequest(
    string RepositoryPath,
    string DocumentationRoot,
    bool DryRun,
    bool Confirmed);
