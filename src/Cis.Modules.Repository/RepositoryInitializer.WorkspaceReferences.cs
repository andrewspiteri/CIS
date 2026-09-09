using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed partial class RepositoryInitializer
{
    public IReadOnlyList<CisReferencePreparationResult> PrepareWorkspaceObservedReferences(string workspacePath, bool apply = true)
    {
        using var timing = CisPerformanceTrace.Start("references.workspace-prepare");
        var registry = new WorkspaceRegistry(new CisRepositoryContextResolver());
        var resolution = registry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace?.AuthorityRepository is not { } authority)
            return [new(workspacePath, [], [], resolution.Errors.Count > 0 ? resolution.Errors : ["A workspace authority is required."], false)];
        var plans = new List<(string Root, IReadOnlyList<RepositoryStarterArtifact> Artifacts)>();
        var combined = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
        foreach (var repository in resolution.Workspace.Repositories.Where(item => item.IsProductOwned).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var context = new CisRepositoryContextResolver().Resolve(repository.RepositoryPath);
            if (!context.IsSuccess || context.Context?.RepositoryId != repository.Id)
                return [new(repository.RepositoryPath, [], [], ["Registered repository identity could not be verified."], false)];
            var classification = _classifier.Classify(repository.RepositoryPath);
            var seeds = RepositoryReferenceSeeder.Seed(repository.RepositoryPath, classification);
            foreach (var family in seeds)
            {
                if (!combined.TryGetValue(family.Key, out var rows)) combined[family.Key] = rows = [];
                rows.AddRange(family.Value.Select(row => (IReadOnlyList<string>)row.Concat([repository.Id]).ToArray()));
            }
            if (repository.Role != "authority") plans.Add((repository.RepositoryPath,
                _starterBinder.Bind(repository.RepositoryPath, repository.Id, repository.DocumentationRoot, classification, observedSeeds: seeds).Artifacts));
        }
        plans.Add((authority.RepositoryPath, RepositoryStarterBinder.BindWorkspaceReferences(authority.Id, authority.DocumentationRoot,
            combined.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<IReadOnlyList<string>>)pair.Value, StringComparer.Ordinal))));
        var previews = plans.Select(plan => PrepareReferenceArtifacts(plan.Root, false, plan.Artifacts)).ToArray();
        if (!apply || previews.Any(item => item.Errors.Count > 0)) return previews;
        var results = new List<CisReferencePreparationResult>();
        foreach (var plan in plans)
        {
            var result = PrepareReferenceArtifacts(plan.Root, true, plan.Artifacts);
            results.Add(result);
            if (result.Errors.Count > 0) break;
        }
        return results;
    }
}
