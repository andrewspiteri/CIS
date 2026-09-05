using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed class WorkspaceGraphService
{
    private readonly GraphBuilder _builder;
    private readonly ICisWorkspaceRegistry _registry;
    private readonly GraphValidator _validator;

    public WorkspaceGraphService(
        ICisWorkspaceRegistry registry,
        GraphBuilder builder,
        GraphValidator validator)
    {
        _registry = registry;
        _builder = builder;
        _validator = validator;
    }

    public WorkspaceGraphBuildResult Build(string workspacePath, bool refresh = false)
    {
        var resolution = _registry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
        {
            return new WorkspaceGraphBuildResult("invalid", null, [], resolution.Errors);
        }

        var results = resolution.Workspace.Repositories
            .Select(repository => new WorkspaceGraphBuildEntry(
                repository.Id,
                repository.RepositoryPath,
                repository.Role,
                repository.Participation,
                repository.Relationship,
                repository.Components,
                _builder.Build(repository.RepositoryPath, refresh)))
            .ToArray();
        var status = results.Any(result => result.Result.ExitCode != 0)
            ? "failed"
            : results.Any(result => result.Result.WarningCount > 0)
                ? "partial"
                : "built";
        return new WorkspaceGraphBuildResult(
            status,
            resolution.Workspace.WorkspacePath,
            results,
            [],
            resolution.Workspace.Ecosystem,
            resolution.Workspace.Product);
    }

    public WorkspaceGraphValidationResult Validate(string workspacePath, bool strict)
    {
        var resolution = _registry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
        {
            return new WorkspaceGraphValidationResult("invalid", null, [], resolution.Errors);
        }

        var results = resolution.Workspace.Repositories
            .Select(repository => new WorkspaceGraphValidationEntry(
                repository.Id,
                repository.RepositoryPath,
                repository.Role,
                repository.Participation,
                repository.Relationship,
                repository.Components,
                _validator.Validate(repository.RepositoryPath, strict)))
            .ToArray();
        var status = results.Any(result => result.Result.ExitCode != 0)
            ? "failed"
            : results.Any(result => result.Result.WarningCount > 0)
                ? "warnings"
                : "valid";
        return new WorkspaceGraphValidationResult(
            status,
            resolution.Workspace.WorkspacePath,
            results,
            [],
            resolution.Workspace.Ecosystem,
            resolution.Workspace.Product);
    }

    public WorkspaceGraphValidationResult Status(string workspacePath)
    {
        var resolution = _registry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
        {
            return new WorkspaceGraphValidationResult("invalid", null, [], resolution.Errors);
        }

        var results = resolution.Workspace.Repositories
            .Select(repository => new WorkspaceGraphValidationEntry(
                repository.Id,
                repository.RepositoryPath,
                repository.Role,
                repository.Participation,
                repository.Relationship,
                repository.Components,
                _validator.Status(repository.RepositoryPath)))
            .ToArray();
        var status = results.Any(result => result.Result.ExitCode != 0)
            ? "failed"
            : results.Any(result => result.Result.WarningCount > 0) ? "warnings" : "valid";
        return new WorkspaceGraphValidationResult(status, resolution.Workspace.WorkspacePath, results, [],
            resolution.Workspace.Ecosystem, resolution.Workspace.Product);
    }
}
