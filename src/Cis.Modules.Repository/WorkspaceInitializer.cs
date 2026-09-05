using Cis.Abstractions;
using System.Text.RegularExpressions;

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

        var ecosystem = Identity(request.EcosystemId, request.EcosystemName, "ecosystem");
        var product = Identity(request.ProductId, request.ProductName, "product");
        var identityErrors = ecosystem.Error is null && product.Error is null
            ? []
            : new[] { ecosystem.Error, product.Error }.Where(error => error is not null).Cast<string>().ToArray();
        if (identityErrors.Length > 0)
            return new WorkspaceInitResult("invalid", request.RepositoryPath, null, null,
                request.DocumentationRoot, null, [], [], identityErrors, false, false);
        var ecosystemIdentity = new CisEcosystem(ecosystem.Id!, ecosystem.Name!);
        var productIdentity = new CisProduct(product.Id!, product.Name!);

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
        if (workspace.Ecosystem is not null
            && (!string.Equals(workspace.Ecosystem.Id, ecosystemIdentity.Id, StringComparison.Ordinal)
                || !string.Equals(workspace.Ecosystem.Name, ecosystemIdentity.Name, StringComparison.Ordinal)))
            collisions.Add($"Workspace already belongs to ecosystem '{workspace.Ecosystem.Id}' ({workspace.Ecosystem.Name}).");
        if (workspace.Product is not null
            && (!string.Equals(workspace.Product.Id, productIdentity.Id, StringComparison.Ordinal)
                || !string.Equals(workspace.Product.Name, productIdentity.Name, StringComparison.Ordinal)))
            collisions.Add($"Workspace already governs product '{workspace.Product.Id}' ({workspace.Product.Name}).");
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
            "authority",
            "owned",
            "none",
            []);
        var repositories = workspace.Repositories
            .Where(repository => !PathComparer.Equals(repository.RepositoryPath, workspacePath))
            .Append(authorityEntry)
            .OrderBy(repository => repository.Id, StringComparer.Ordinal)
            .ToArray();
        var content = _registry.Serialize(workspacePath, ecosystemIdentity, productIdentity, repositories);
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
                Applied: false,
                ecosystemIdentity,
                productIdentity);
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
            Applied: true,
            ecosystemIdentity,
            productIdentity);
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

    private static (string? Id, string? Name, string? Error) Identity(string id, string? name, string kind)
    {
        var normalized = id.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalized, "^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant))
            return (null, null, $"A valid {kind} identity is required (lower-case letters, numbers, dot, underscore, or hyphen).");
        var display = string.IsNullOrWhiteSpace(name) ? normalized : name.Trim();
        return (normalized, display, null);
    }
}
