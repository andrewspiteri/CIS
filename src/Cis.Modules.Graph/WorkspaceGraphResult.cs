namespace Cis.Modules.Graph;

public sealed record WorkspaceGraphBuildEntry(
    string Id,
    string RepositoryPath,
    string Role,
    GraphBuildResult Result);

public sealed record WorkspaceGraphBuildResult(
    string Status,
    string? WorkspacePath,
    IReadOnlyList<WorkspaceGraphBuildEntry> Repositories,
    IReadOnlyList<string> Errors)
{
    public int NodeCount => Repositories.Sum(repository => repository.Result.NodeCount);

    public int EdgeCount => Repositories.Sum(repository => repository.Result.EdgeCount);

    public int ExitCode => Errors.Count > 0
        ? 2
        : Repositories.Select(repository => repository.Result.ExitCode).DefaultIfEmpty(0).Max();
}

public sealed record WorkspaceGraphValidationEntry(
    string Id,
    string RepositoryPath,
    string Role,
    GraphValidationResult Result);

public sealed record WorkspaceGraphValidationResult(
    string Status,
    string? WorkspacePath,
    IReadOnlyList<WorkspaceGraphValidationEntry> Repositories,
    IReadOnlyList<string> Errors)
{
    public int NodeCount => Repositories.Sum(repository => repository.Result.NodeCount);

    public int EdgeCount => Repositories.Sum(repository => repository.Result.EdgeCount);

    public int ExitCode => Errors.Count > 0
        ? 2
        : Repositories.Select(repository => repository.Result.ExitCode).DefaultIfEmpty(0).Max();
}
