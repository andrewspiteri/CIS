using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    public CisFeatureNavigationResult Navigation(string workspacePath)
    {
        using var reads = CisReadScope.Enter();
        var resolved = workspaces.Resolve(workspacePath);
        if (!resolved.IsSuccess) return new(null, [], resolved.Errors);
        var features = Requests(workspacePath).Select(plan =>
        {
            var wizard = Wizard(workspacePath, plan.Slug);
            return new CisFeatureNavigationEntry(plan,
                wizard.Errors.Count > 0 ? "Needs attention" : wizard.Reviewed ? "Definition reviewed"
                    : wizard.Pages.Any(page => page.Id != "foundation" && page.Complete) ? "Definition in progress" : "Draft definition",
                wizard.Pages.Count(page => page.Complete), wizard.Pages.Count,
                wizard.Pages.FirstOrDefault(page => !page.Complete)?.Id ?? "review", wizard.RepositoryWork, wizard.Errors);
        }).ToArray();
        return new(resolved.Workspace, features, []);
    }

    private static IReadOnlyList<string> ValidateRepositoryWork(WizardState state, IReadOnlyList<CisFeatureRepositoryWork> items)
    {
        if (items.Count > 100) return ["A feature can contain at most 100 repository work items."];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id)
                || !System.Text.RegularExpressions.Regex.IsMatch(item.Id, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$") || !ids.Add(item.Id)
                || !SingleLine(item.Title, 200) || string.IsNullOrWhiteSpace(item.Scope) || item.Scope.Length > 24000
                || item.Title.Contains("<!-- cis:", StringComparison.Ordinal) || item.Scope.Contains("<!-- cis:", StringComparison.Ordinal)
                || item.Scope.Contains('\0') || item.DependsOn is null || item.ChangeIds is null
                || item.DependsOn.Count > 100 || item.ChangeIds.Count > 100)
                return ["Each repository feature needs a unique identifier, title, bounded scope and dependency/change lists."];
            if (!state.Workspace.DeliveryRepositories.Any(repo => repo.Id == item.RepositoryId))
                return [$"Work '{item.Id}' must target a product-owned implementation repository. External dependencies remain integration context."];
            foreach (var id in item.ChangeIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !CisPathSafety.TryResolveUnderRoot(state.Authority.RepositoryPath,
                        $"{state.Authority.DocumentationRoot}/changes/{id}/proposal.md", out var proposal)
                    || !SafeAbsolutePath(proposal) || !File.Exists(proposal)) return [$"Linked change '{id}' is not an existing product change dossier."];
            }
        }
        if (items.Any(item => item.DependsOn.Any(id => id == item.Id || !ids.Contains(id))))
            return ["Dependencies must refer to other work identifiers within this feature."];
        var remaining = items.ToDictionary(item => item.Id, item => item.DependsOn.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToArray();
            if (ready.Length == 0) return ["Repository work dependencies contain a cycle. Remove the circular dependency before saving."];
            foreach (var id in ready) remaining.Remove(id);
            foreach (var dependencies in remaining.Values) dependencies.ExceptWith(ready);
        }
        return [];
    }
}
