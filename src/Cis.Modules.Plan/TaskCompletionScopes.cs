using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

internal sealed record TaskCompletionScope(CisRepositoryContext Context, string ReceiptPath);

/// <summary>Resolves declared task targets without treating external dependencies as delivery scope.</summary>
internal sealed class TaskCompletionScopes(ICisRepositoryContextResolver resolver, ICisWorkspaceRegistry? registry)
{
    internal IReadOnlyList<TaskCompletionScope> Resolve(ChangeDossier change, PlanWorkItem task, IReadOnlyList<string>? fallbackTargets)
    {
        var authority = resolver.Resolve(change.RepositoryPath).Context
            ?? throw new InvalidDataException("Task authority context is unavailable.");
        var targets = task.Targets is { Count: > 0 } ? task.Targets : fallbackTargets ?? [];
        var resolution = registry?.Resolve(change.RepositoryPath);
        var workspace = resolution?.Workspace;
        if (workspace is null)
        {
            if (File.Exists(Path.Combine(change.RepositoryPath, ".cis/workspace.yml")))
                throw new InvalidDataException("Task workspace cannot be resolved: " + string.Join("; ", resolution?.Errors ?? []));
            if (targets.Any(id => id != authority.RepositoryId))
                throw new InvalidDataException("Task targets require a registered product workspace.");
            return [Scope(authority)];
        }
        if (resolution!.Errors.Count > 0) throw new InvalidDataException(string.Join("; ", resolution.Errors));
        if (workspace.AuthorityRepository is not { } registeredAuthority || !SamePath(registeredAuthority.RepositoryPath, change.RepositoryPath))
            throw new InvalidDataException("Completion must be requested from the registered task authority.");
        if (targets.Count == 0)
        {
            if (workspace.DeliveryRepositories.Count > 0)
                throw new InvalidDataException("Task completion requires explicit repository targets in a participant workspace.");
            targets = [authority.RepositoryId];
        }
        if (targets.Distinct(StringComparer.Ordinal).Count() != targets.Count)
            throw new InvalidDataException("Task targets contain duplicate repository identities.");
        return targets.Select(id =>
        {
            var registered = workspace.Repositories.SingleOrDefault(repository => repository.Id == id)
                ?? throw new InvalidDataException("Task target is not registered: " + id);
            if (!registered.IsProductOwned) throw new InvalidDataException("Task target is not product-owned: " + id);
            if (id.Length == 0 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
                throw new InvalidDataException("Task target identity is unsafe for evidence routing.");
            var context = resolver.Resolve(registered.RepositoryPath).Context
                ?? throw new InvalidDataException("Task target context is unavailable: " + id);
            if (context.RepositoryId != id || context.DocumentationRoot != registered.DocumentationRoot)
                throw new InvalidDataException("Task target registration does not match repository configuration: " + id);
            return Scope(context);
        }).ToArray();

        TaskCompletionScope Scope(CisRepositoryContext context)
        {
            var name = context.RepositoryId == authority.RepositoryId ? task.Id + ".json" : task.Id + "/" + context.RepositoryId + ".json";
            var relative = Path.Combine(change.RelativePath, "verification", name).Replace('\\', '/');
            if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, relative, out _))
                throw new InvalidDataException("Task receipt path escapes the authority repository.");
            return new(context, relative);
        }
    }

    private static bool SamePath(string first, string second) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
