namespace Cis.Abstractions;

public sealed record CisWorkspace(
    string WorkspacePath,
    string ConfigurationPath,
    IReadOnlyList<CisWorkspaceRepository> Repositories)
{
    public CisWorkspaceRepository? AuthorityRepository => Repositories.SingleOrDefault(repository =>
        string.Equals(repository.Role, "authority", StringComparison.Ordinal));
}

public sealed record CisWorkspaceRepository(
    string Id,
    string RepositoryPath,
    string DocumentationRoot,
    string Role = "participant");

public sealed record CisWorkspaceResolution(
    CisWorkspace? Workspace,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Workspace is not null && Errors.Count == 0;
}
