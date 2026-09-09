using Cis.Abstractions;

namespace Cis.Modules.References;

public sealed partial class ReferenceGovernanceService
{
    // Keep the same row dimensions used by dictionary preparation. Separately encoded
    // dimensions preserve punctuation and boundaries that loose correlation aliases omit.
    private static string[] IdentityDimensions(string kind, IReadOnlyDictionary<string, string> values, string fallback)
    {
        string Value(string header, string otherwise = "") => values.GetValueOrDefault(header, otherwise);
        return kind switch
        {
            "data-dictionary" => [Value("Entity", fallback), Value("Field")],
            "erd" => [Value("Entity", fallback), Value("Relationship"), Value("Target")],
            "workflow-state-dictionary" => [Value("Workflow ID", Value("Workflow", fallback)), Value("State")],
            "configuration-dictionary" => [Value("Owner"), Value("Path", fallback)],
            "package-catalogue" => [Value("Package", fallback), Value("Component")],
            "screen-route-map" => [Value("Screen or route", fallback), Value("Component"), Value("Platform")],
            _ => [fallback],
        };
    }

    private static string DisplayIdentity(string kind, string[] dimensions, string fallback)
        => kind switch
        {
            "data-dictionary" => string.Join('.', dimensions.Where(value => value.Length > 0)),
            "erd" => string.Join(" / ", dimensions.Where(value => value.Length > 0)),
            "workflow-state-dictionary" => string.Join('/', dimensions.Where(value => value.Length > 0)),
            "configuration-dictionary" when dimensions[0].Length > 0 => string.Join('/', dimensions),
            "screen-route-map" => string.Join(" / ", dimensions.Where(value => value.Length > 0)),
            _ => fallback,
        };

    private static string EntryKey(CanonicalReferenceEntry entry) => entry.CanonicalKey ?? entry.Identity.ToUpperInvariant();
    private static bool IsLocal(CanonicalReferenceEntry entry, string repositoryId)
        => entry.RepositoryId is null || entry.RepositoryId.Equals(repositoryId, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string> ResolveEvidenceRoots(CisRepositoryContext context,
        IReadOnlyList<ReferenceFamilyState> families, List<ReferenceDiagnostic> diagnostics)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [context.RepositoryId] = context.RepositoryPath,
        };
        var scoped = families.SelectMany(family => family.CanonicalEntries)
            .Where(entry => !IsLocal(entry, context.RepositoryId)).GroupBy(entry => entry.RepositoryId!, StringComparer.OrdinalIgnoreCase).ToArray();
        if (scoped.Length == 0) return roots;

        var workspace = _workspaces.Resolve(context.RepositoryPath);
        if (workspace.IsSuccess && workspace.Workspace is { } registered)
            foreach (var repository in registered.Repositories.Where(repository => repository.IsProductOwned))
                roots.TryAdd(repository.Id, repository.RepositoryPath);

        foreach (var scope in scoped.Where(scope => !roots.ContainsKey(scope.Key)))
            diagnostics.Add(Diagnostic("CIS-REF-SCOPE-001", "error", "repository",
                $"Dictionary repository is not a registered product-owned repository: '{scope.Key}'.",
                "Set Repository to the owning repository ID in the valid workspace registry. Blank, unknown and dependency scopes cannot supply product dictionary evidence.",
                scope.Select(entry => $"{entry.Path}:{entry.Line}").Concat(workspace.Errors).ToArray()));
        return roots;
    }
}
