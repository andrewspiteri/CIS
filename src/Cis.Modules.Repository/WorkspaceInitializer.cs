using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed class WorkspaceInitializer
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly RepositoryInitializer _repositoryInitializer;
    private readonly WorkspaceRegistry _registry;

    public WorkspaceInitializer(
        RepositoryInitializer repositoryInitializer,
        WorkspaceRegistry registry)
    {
        _repositoryInitializer = repositoryInitializer;
        _registry = registry;
    }

    public WorkspaceInitResult Initialize(WorkspaceInitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var repositoryPlan = _repositoryInitializer.Initialize(new RepositoryInitRequest(
            request.RepositoryPath,
            request.DocumentationRoot,
            DryRun: true,
            Confirmed: true,
            WorkspaceAuthority: true));
        if (repositoryPlan.Errors.Count > 0 || repositoryPlan.Collisions.Count > 0)
        {
            return FromRepositoryPlan(request, repositoryPlan);
        }

        var workspacePath = repositoryPlan.RepositoryPath!;
        var workspaceResolution = _registry.ReadForImport(workspacePath);
        if (!workspaceResolution.IsSuccess || workspaceResolution.Workspace is null)
        {
            return new WorkspaceInitResult(
                "invalid",
                workspacePath,
                null,
                null,
                repositoryPlan.DocumentationRoot,
                repositoryPlan,
                repositoryPlan.Warnings,
                [],
                workspaceResolution.Errors,
                ConfirmationRequired: false,
                Applied: false);
        }

        var workspace = workspaceResolution.Workspace;
        var id = RepositoryInitializer.CreateRepositoryId(workspacePath);
        var authority = workspace.AuthorityRepository;
        var collisions = new List<string>();
        if (authority is not null && !PathComparer.Equals(authority.RepositoryPath, workspacePath))
        {
            collisions.Add(
                $"Workspace authority is already registered as '{authority.Id}' at {authority.RepositoryPath}.");
        }

        var sameId = workspace.Repositories.FirstOrDefault(repository =>
            string.Equals(repository.Id, id, StringComparison.Ordinal));
        if (sameId is not null && !PathComparer.Equals(sameId.RepositoryPath, workspacePath))
        {
            collisions.Add($"Repository id '{id}' is already registered at {sameId.RepositoryPath}.");
        }

        if (collisions.Count > 0)
        {
            return new WorkspaceInitResult(
                "collision",
                workspacePath,
                workspace.ConfigurationPath,
                id,
                repositoryPlan.DocumentationRoot,
                repositoryPlan,
                repositoryPlan.Warnings,
                collisions,
                [],
                ConfirmationRequired: false,
                Applied: false);
        }

        var normalizedRoot = (repositoryPlan.DocumentationRoot ?? request.DocumentationRoot)
            .Replace('\\', '/')
            .Trim('/');
        var authorityEntry = new CisWorkspaceRepository(
            id,
            workspacePath,
            normalizedRoot,
            "authority");
        var repositories = workspace.Repositories
            .Where(repository => !PathComparer.Equals(repository.RepositoryPath, workspacePath))
            .Append(authorityEntry)
            .OrderBy(repository => repository.Id, StringComparer.Ordinal)
            .ToArray();
        var content = _registry.Serialize(workspacePath, repositories);
        var registryChanged = !File.Exists(workspace.ConfigurationPath)
            || !Equivalent(File.ReadAllText(workspace.ConfigurationPath), content);
        var repositoryChanged = repositoryPlan.DirectoriesToCreate.Count > 0
            || repositoryPlan.FilesToCreate.Count > 0
            || repositoryPlan.FilesToUpdate.Count > 0;
        var hasChanges = registryChanged || repositoryChanged;
        var confirmationRequired = hasChanges && !request.DryRun && !request.Confirmed;
        if (request.DryRun || confirmationRequired || !hasChanges)
        {
            return new WorkspaceInitResult(
                request.DryRun
                    ? "dry-run"
                    : confirmationRequired
                        ? "confirmation-required"
                        : "unchanged",
                workspacePath,
                workspace.ConfigurationPath,
                id,
                normalizedRoot,
                repositoryPlan,
                repositoryPlan.Warnings,
                [],
                [],
                confirmationRequired,
                Applied: false);
        }

        var initialized = _repositoryInitializer.Initialize(new RepositoryInitRequest(
            workspacePath,
            normalizedRoot,
            DryRun: false,
            Confirmed: true,
            WorkspaceAuthority: true));
        if (initialized.ExitCode != 0)
        {
            return FromRepositoryPlan(request, initialized);
        }

        if (registryChanged)
        {
            WorkspaceRegistry.Write(workspace.ConfigurationPath, content);
        }

        return new WorkspaceInitResult(
            "initialized",
            workspacePath,
            workspace.ConfigurationPath,
            id,
            normalizedRoot,
            initialized,
            initialized.Warnings,
            [],
            [],
            ConfirmationRequired: false,
            Applied: true);
    }

    private static WorkspaceInitResult FromRepositoryPlan(
        WorkspaceInitRequest request,
        RepositoryInitResult plan)
        => new(
            plan.Errors.Count > 0 ? "invalid" : "collision",
            plan.RepositoryPath ?? request.RepositoryPath,
            null,
            null,
            plan.DocumentationRoot ?? request.DocumentationRoot,
            plan,
            plan.Warnings,
            plan.Collisions,
            plan.Errors,
            ConfirmationRequired: false,
            Applied: false);

    private static bool Equivalent(string left, string right)
        => string.Equals(
            left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            StringComparison.Ordinal);
}
