using Cis.Abstractions;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

public sealed class RepositoryImporter
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly RepositoryInitializer _initializer;
    private readonly WorkspaceRegistry _registry;

    public RepositoryImporter(RepositoryInitializer initializer, WorkspaceRegistry registry)
    {
        _initializer = initializer;
        _registry = registry;
    }

    public RepositoryImportResult Import(RepositoryImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var warnings = new List<string>();
        var collisions = new List<string>();
        var sources = ResolveSources(request.SourcePaths, errors);
        var participation = request.Participation.Trim().ToLowerInvariant();
        var relationship = request.Relationship.Trim().ToLowerInvariant();
        var components = (request.ComponentScope ?? [])
            .Select(component => component.Trim().ToLowerInvariant())
            .Where(component => component.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (participation is not ("owned" or "dependency"))
            errors.Add("Participation must be owned or dependency.");
        if (relationship is not ("none" or "producer" or "consumer" or "bidirectional"))
            errors.Add("Relationship must be none, producer, consumer, or bidirectional.");
        if (participation == "owned" && relationship != "none")
            errors.Add("Product-owned repositories must use relationship 'none'.");
        if (participation == "dependency" && relationship == "none")
            errors.Add("Dependency repositories must declare a producer, consumer, or bidirectional relationship.");
        foreach (var component in components.Where(component => !ValidIdentity(component)))
            errors.Add($"Component scope is invalid: {component}");
        if (sources.Count is < 1 or > 20)
        {
            errors.Add("Import requires between 1 and 20 distinct repository paths.");
        }

        var workspaceResolution = _registry.ReadForImport(request.WorkspacePath);
        if (!workspaceResolution.IsSuccess || workspaceResolution.Workspace is null)
        {
            errors.AddRange(workspaceResolution.Errors);
        }

        if (errors.Count > 0)
        {
            return Invalid(request, errors);
        }

        var workspace = workspaceResolution.Workspace!;
        var bootstrappingAuthority = workspace.Repositories.Count == 0
            && sources.Any(source => PathComparer.Equals(source, workspace.WorkspacePath));
        if (workspace.Repositories.Count == 0 && !bootstrappingAuthority)
            errors.Add("Initialize the product workspace authority before importing other repositories.");
        CisEcosystem? ecosystem = workspace.Ecosystem;
        if (ecosystem is null && Identity(request.EcosystemId, request.EcosystemName, "ecosystem", errors) is { } ecosystemIdentity)
            ecosystem = new CisEcosystem(ecosystemIdentity.Id, ecosystemIdentity.Name);
        CisProduct? product = workspace.Product;
        if (product is null && Identity(request.ProductId, request.ProductName, "product", errors) is { } productIdentity)
            product = new CisProduct(productIdentity.Id, productIdentity.Name);
        if (workspace.Ecosystem is not null && !string.IsNullOrWhiteSpace(request.EcosystemId)
            && !string.Equals(workspace.Ecosystem.Id, request.EcosystemId.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add($"Workspace belongs to ecosystem '{workspace.Ecosystem.Id}', not '{request.EcosystemId}'.");
        if (workspace.Product is not null && !string.IsNullOrWhiteSpace(request.ProductId)
            && !string.Equals(workspace.Product.Id, request.ProductId.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add($"Workspace governs product '{workspace.Product.Id}', not '{request.ProductId}'.");
        if (errors.Count > 0 || ecosystem is null || product is null)
            return Invalid(request, errors);
        bool IsAuthoritySource(string source) =>
            bootstrappingAuthority && PathComparer.Equals(source, workspace.WorkspacePath)
            || workspace.Repositories.Any(repository => repository.Role == "authority"
                && PathComparer.Equals(repository.RepositoryPath, source));
        var entries = new List<RepositoryImportEntryResult>();
        var plannedRepositories = new List<CisWorkspaceRepository>();
        var initializationPlans = new List<(string Source, RepositoryInitResult Result)>();
        foreach (var source in sources)
        {
            var isAuthoritySource = IsAuthoritySource(source);
            var plan = _initializer.Initialize(new RepositoryInitRequest(
                source,
                request.DocumentationRoot,
                DryRun: true,
                Confirmed: true,
                WorkspaceAuthority: isAuthoritySource));
            initializationPlans.Add((source, plan));
            warnings.AddRange(plan.Warnings.Select(warning => $"{source}: {warning}"));
            errors.AddRange(plan.Errors.Select(error => $"{source}: {error}"));
            collisions.AddRange(plan.Collisions.Select(collision => $"{source}: {collision}"));

            var id = RepositoryInitializer.CreateRepositoryId(source);
            var hasInitializationChanges = plan.DirectoriesToCreate.Count > 0
                || plan.FilesToCreate.Count > 0
                || plan.FilesToUpdate.Count > 0;
            entries.Add(new RepositoryImportEntryResult(
                id,
                source,
                request.DocumentationRoot.Replace('\\', '/').Trim('/'),
                isAuthoritySource ? "owned" : participation,
                isAuthoritySource ? "none" : relationship,
                isAuthoritySource ? [] : components,
                hasInitializationChanges ? "initialize" : "unchanged",
                plan.FilesToCreate,
                plan.FilesToUpdate,
                plan.Warnings));
            plannedRepositories.Add(new CisWorkspaceRepository(
                id,
                source,
                request.DocumentationRoot.Replace('\\', '/').Trim('/'),
                isAuthoritySource ? "authority" : "participant",
                isAuthoritySource ? "owned" : participation,
                isAuthoritySource ? "none" : relationship,
                isAuthoritySource ? [] : components));
        }

        DetectImportCollisions(workspace.Repositories, plannedRepositories, collisions);
        if (errors.Count > 0 || collisions.Count > 0)
        {
            return new RepositoryImportResult(
                errors.Count > 0 ? "invalid" : "collision",
                workspace.WorkspacePath,
                workspace.ConfigurationPath,
                entries,
                warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
                collisions.Distinct(StringComparer.Ordinal).Order().ToArray(),
                errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
                ConfirmationRequired: false,
                Applied: false);
        }

        var plannedIds = plannedRepositories.Select(repository => repository.Id).ToHashSet(StringComparer.Ordinal);
        var merged = workspace.Repositories
            .Where(repository => !plannedIds.Contains(repository.Id))
            .Concat(plannedRepositories)
            .OrderBy(repository => repository.Id, StringComparer.Ordinal)
            .ToArray();
        var registryContent = _registry.Serialize(workspace.WorkspacePath, ecosystem, product, merged);
        var registryChanged = !File.Exists(workspace.ConfigurationPath)
            || !Equivalent(File.ReadAllText(workspace.ConfigurationPath), registryContent);
        var initializationChanged = entries.Any(entry => entry.Status == "initialize");
        var hasChanges = registryChanged || initializationChanged;
        var confirmationRequired = hasChanges && !request.DryRun && !request.Confirmed;
        if (request.DryRun || confirmationRequired || !hasChanges)
        {
            return new RepositoryImportResult(
                request.DryRun
                    ? "dry-run"
                    : confirmationRequired
                        ? "confirmation-required"
                        : "unchanged",
                workspace.WorkspacePath,
                workspace.ConfigurationPath,
                entries,
                warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
                [],
                [],
                confirmationRequired,
                Applied: false);
        }

        var appliedEntries = new List<RepositoryImportEntryResult>();
        foreach (var plan in initializationPlans)
        {
            var isAuthoritySource = IsAuthoritySource(plan.Source);
            var result = _initializer.Initialize(new RepositoryInitRequest(
                plan.Source,
                request.DocumentationRoot,
                DryRun: false,
                Confirmed: true,
                WorkspaceAuthority: isAuthoritySource));
            if (result.ExitCode != 0)
            {
                errors.AddRange(result.Errors.Select(error => $"{plan.Source}: {error}"));
                collisions.AddRange(result.Collisions.Select(collision => $"{plan.Source}: {collision}"));
                break;
            }

            appliedEntries.Add(new RepositoryImportEntryResult(
                RepositoryInitializer.CreateRepositoryId(plan.Source),
                plan.Source,
                request.DocumentationRoot.Replace('\\', '/').Trim('/'),
                isAuthoritySource ? "owned" : participation,
                isAuthoritySource ? "none" : relationship,
                isAuthoritySource ? [] : components,
                result.Applied ? "initialized" : "unchanged",
                result.FilesToCreate,
                result.FilesToUpdate,
                result.Warnings));
        }

        if (errors.Count > 0 || collisions.Count > 0)
        {
            return new RepositoryImportResult(
                errors.Count > 0 ? "invalid" : "collision",
                workspace.WorkspacePath,
                workspace.ConfigurationPath,
                appliedEntries,
                warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
                collisions.Distinct(StringComparer.Ordinal).Order().ToArray(),
                errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
                ConfirmationRequired: false,
                Applied: appliedEntries.Any(entry => entry.Status == "initialized"));
        }

        if (registryChanged)
        {
            WorkspaceRegistry.Write(workspace.ConfigurationPath, registryContent);
        }

        return new RepositoryImportResult(
            "imported",
            workspace.WorkspacePath,
            workspace.ConfigurationPath,
            appliedEntries,
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
            [],
            [],
            ConfirmationRequired: false,
            Applied: true);
    }

    private static IReadOnlyList<string> ResolveSources(
        IReadOnlyList<string> sourcePaths,
        ICollection<string> errors)
    {
        var sources = new List<string>();
        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                errors.Add("Repository source path cannot be empty.");
                continue;
            }

            string resolved;
            try
            {
                resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"Repository source path is invalid: {exception.Message}");
                continue;
            }

            if (!Directory.Exists(resolved))
            {
                errors.Add($"Repository source directory does not exist: {resolved}");
                continue;
            }

            if (!sources.Contains(resolved, PathComparer))
            {
                sources.Add(resolved);
            }
        }

        return sources;
    }

    private static void DetectImportCollisions(
        IReadOnlyList<CisWorkspaceRepository> existing,
        IReadOnlyList<CisWorkspaceRepository> planned,
        ICollection<string> collisions)
    {
        foreach (var duplicate in planned.GroupBy(repository => repository.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            collisions.Add($"Imported repositories have the same repository id: {duplicate.Key}");
        }

        foreach (var candidate in planned)
        {
            var sameId = existing.FirstOrDefault(repository =>
                string.Equals(repository.Id, candidate.Id, StringComparison.Ordinal));
            if (sameId is not null
                && !PathComparer.Equals(sameId.RepositoryPath, candidate.RepositoryPath))
            {
                collisions.Add(
                    $"Repository id '{candidate.Id}' is already registered for {sameId.RepositoryPath}.");
            }

            var samePath = existing.FirstOrDefault(repository =>
                PathComparer.Equals(repository.RepositoryPath, candidate.RepositoryPath));
            if (samePath is not null
                && !string.Equals(samePath.Id, candidate.Id, StringComparison.Ordinal))
            {
                collisions.Add(
                    $"Repository path '{candidate.RepositoryPath}' is already registered as '{samePath.Id}'.");
            }
        }
    }

    private static bool Equivalent(string left, string right)
        => string.Equals(
            left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            StringComparison.Ordinal);

    private static (string Id, string Name)? Identity(
        string? requestedId,
        string? requestedName,
        string kind,
        ICollection<string> errors)
    {
        var id = requestedId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ValidIdentity(id))
        {
            errors.Add($"A valid {kind} identity is required when bootstrapping a product workspace.");
            return null;
        }
        return (id, string.IsNullOrWhiteSpace(requestedName) ? id : requestedName.Trim());
    }

    private static bool ValidIdentity(string value)
        => Regex.IsMatch(value, "^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);

    private static RepositoryImportResult Invalid(
        RepositoryImportRequest request,
        IReadOnlyList<string> errors)
        => new(
            "invalid",
            string.IsNullOrWhiteSpace(request.WorkspacePath) ? null : request.WorkspacePath,
            null,
            [],
            [],
            [],
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
            ConfirmationRequired: false,
            Applied: false);
}
