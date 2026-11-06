using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService : ICisOwnedStorageController
{
    CisOwnedStorageResult ICisOwnedStorageController.Inspect(string repositoryPath, CisNativeWorkOptions? association)
    {
        var diagnostics = new List<string>();
        try
        {
            var binding = ResolveNativeBinding(repositoryPath, association, diagnostics);
            if (binding is null || diagnostics.Count > 0) return new("unknown", true, null, diagnostics);
            if (!binding.Required) return new("unaccounted", false, null, []);
            if (binding.Identity is null) return new("unknown", true, null, ["Resolve the canonical work owner before inspecting its finite owned storage."]);
            ValidateCurrentStorageOwnership(binding.Authority, binding.Identity.ChangeId);
            return _workLedger.InspectStorage(binding.Authority, binding.Identity.ChangeId, binding.Identity.TaskId, binding.Identity.AncestorTaskIds);
        }
        catch (Exception error) when (WorkException(error)) { return new("unknown", true, null, [error.Message]); }
    }

    public CisOwnedStorageAdmission Reserve(string repositoryPath, CisAgentWorkReference reference, string operationId,
        long maximumGrowthBytes, IReadOnlyList<CisOwnedStoragePath>? claims = null,
        bool cleanup = false, bool clampToRemaining = false, string? actor = null)
    {
        var context = ResolveOwnedStorageAuthority(repositoryPath, reference, out var errors);
        if (context is null) return new(new("unknown", true, null, errors), null, 0);
        try
        {
            ValidateCurrentStorageOwnership(context, reference.ChangeId);
            return _workLedger.ReserveStorage(context, reference, operationId, maximumGrowthBytes, claims,
                cleanup, clampToRemaining, actor ?? "");
        }
        catch (Exception error) when (WorkException(error)) { return new(new("unknown", true, null, [error.Message]), null, 0); }
    }

    public CisOwnedStorageResult Settle(string repositoryPath, CisAgentWorkReference reference, string reservationId,
        bool safelyStopped, IReadOnlyList<string>? requiredPaths = null, IReadOnlyList<string>? supersededRequiredPaths = null)
    {
        var context = ResolveOwnedStorageAuthority(repositoryPath, reference, out var errors);
        if (context is null) return new("unknown", true, null, errors);
        try
        {
            ValidateCurrentStorageOwnership(context, reference.ChangeId);
            return _workLedger.SettleStorage(context, reference, reservationId, safelyStopped, requiredPaths, supersededRequiredPaths);
        }
        catch (Exception error) when (WorkException(error)) { return new("unknown", true, null, [error.Message]); }
    }

    private CisRepositoryContext? ResolveOwnedStorageAuthority(string repositoryPath, CisAgentWorkReference reference,
        out IReadOnlyList<string> errors)
    {
        var diagnostics = new List<string>();
        var context = Resolve(repositoryPath, out var resolutionErrors);
        diagnostics.AddRange(resolutionErrors);
        if (context is not null && context.RepositoryId != reference.AuthorityRepositoryId
            && _workspaceRegistry?.Resolve(repositoryPath).Workspace?.AuthorityRepository is { } authority)
        {
            context = Resolve(authority.RepositoryPath, out resolutionErrors);
            diagnostics.AddRange(resolutionErrors);
        }
        if (context?.RepositoryId != reference.AuthorityRepositoryId)
        { diagnostics.Add("The owned-storage reference does not belong to the resolved authority; restore its original association."); context = null; }
        errors = diagnostics;
        return context;
    }

    private CisOwnedStoragePolicy NormalizeOwnedStoragePolicy(CisRepositoryContext authority, CisOwnedStoragePolicy policy)
    {
        if (policy.Roots is null) throw new InvalidDataException("Supply explicit owned CIS roots with the finite storage ceiling.");
        var workspace = _workspaceRegistry?.Resolve(authority.RepositoryPath).Workspace;
        var roots = policy.Roots.Select(root =>
        {
            if (root is null || string.IsNullOrWhiteSpace(root.RepositoryPath) || string.IsNullOrWhiteSpace(root.Path))
                throw new InvalidDataException("Every owned root needs its exact repository and CIS local path.");
            var repository = Path.GetFullPath(root.RepositoryPath, authority.RepositoryPath);
            if (!repository.Equals(authority.RepositoryPath, CisPathSafety.PlatformComparison)
                && workspace?.Repositories.Any(candidate => candidate.IsProductOwned
                    && candidate.RepositoryPath.Equals(repository, CisPathSafety.PlatformComparison)) != true)
                throw new InvalidDataException("Owned storage can include only this authority and its exact registered product-owned repositories.");
            return new CisOwnedStorageRoot(repository, root.Path.Replace('\\', '/').TrimEnd('/')) { Scratch = root.Scratch };
        }).ToArray();
        // Archive ZIPs, manifests and retrieval/staging copies cannot escape the ceiling
        // just because a command displays only one live retention family.
        var repositories = roots.Select(root => root.RepositoryPath).Append(authority.RepositoryPath)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var inherited = new List<CisOwnedStorageRoot>();
        foreach (var repository in repositories)
        {
            inherited.Add(new(repository, ".cis/local/artifacts"));
            var agents = Path.GetFullPath(Path.Combine(repository, ".cis/local/agents"));
            var explicitlyOwned = roots.Any(root => !root.Scratch
                && CisPathSafety.TryResolveUnderRoot(root.RepositoryPath, root.Path, out var declared)
                && CisPathSafety.IsUnderRoot(declared, agents, allowRoot: true));
            if (!explicitlyOwned) inherited.Add(new(repository, ".cis/local/agents") { Scratch = true });
        }
        roots = roots.Concat(inherited).Distinct().ToArray();
        return policy with { Roots = roots };
    }

    private void ValidateCurrentStorageOwnership(CisRepositoryContext authority, string changeId)
    {
        var workspace = _workspaceRegistry?.Resolve(authority.RepositoryPath).Workspace;
        foreach (var root in _workLedger.StorageRoots(authority, changeId))
            if (!root.RepositoryPath.Equals(authority.RepositoryPath, CisPathSafety.PlatformComparison)
                && workspace?.Repositories.Any(repository => repository.IsProductOwned
                    && repository.RepositoryPath.Equals(root.RepositoryPath, CisPathSafety.PlatformComparison)) != true)
                throw new InvalidDataException("A retained storage root is no longer an exact registered owned repository. Resolve its original ownership and allocation before expansion or cleanup; its usage is unknown, not zero.");
    }
}
