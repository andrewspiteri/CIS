namespace Cis.Abstractions;

public sealed record CisWorkspace(
    string WorkspacePath,
    string ConfigurationPath,
    IReadOnlyList<CisWorkspaceRepository> Repositories,
    CisEcosystem? Ecosystem = null,
    CisProduct? Product = null)
{
    public CisWorkspaceRepository? AuthorityRepository => Repositories.SingleOrDefault(repository =>
        string.Equals(repository.Role, "authority", StringComparison.Ordinal));

    public IReadOnlyList<CisWorkspaceRepository> DeliveryRepositories => Repositories
        .Where(repository => repository.IsProductOwned && repository.Role == "participant")
        .ToArray();

    public IReadOnlyList<CisWorkspaceRepository> DependencyRepositories => Repositories
        .Where(repository => repository.IsDependency)
        .ToArray();
}

public sealed record CisEcosystem(string Id, string Name);

public sealed record CisProduct(string Id, string Name);

public sealed record CisWorkspaceRepository(
    string Id,
    string RepositoryPath,
    string DocumentationRoot,
    string Role = "participant",
    string Participation = "owned",
    string Relationship = "none",
    IReadOnlyList<string>? ComponentScope = null)
{
    public bool IsProductOwned => string.Equals(Participation, "owned", StringComparison.Ordinal);

    public bool IsDependency => string.Equals(Participation, "dependency", StringComparison.Ordinal);

    public IReadOnlyList<string> Components => ComponentScope ?? [];
}

public sealed record CisWorkspaceResolution(
    CisWorkspace? Workspace,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Workspace is not null && Errors.Count == 0;
}
