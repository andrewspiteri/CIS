namespace Cis.Modules.Repository;

public sealed record WorkspaceInitRequest(
    string RepositoryPath,
    string DocumentationRoot,
    bool DryRun,
    bool Confirmed,
    string EcosystemId,
    string ProductId,
    string? EcosystemName = null,
    string? ProductName = null);
