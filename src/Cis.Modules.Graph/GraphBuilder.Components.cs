using Cis.Abstractions;
using Cis.Modules.Docs;

namespace Cis.Modules.Graph;

public sealed partial class GraphBuilder
{
    private static bool IsComponentSheet(DocumentationCatalogEntry entry)
        => entry.Type.Equals("component-sheet", StringComparison.OrdinalIgnoreCase)
            || entry.Path.EndsWith("/component-sheet.md", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ComponentPaths(CisGraphNode component, string property)
    {
        if (component.Properties.TryGetValue(property, out var paths)
            && (paths.Length > 0 || property == "ownsPaths"
                && component.Properties.GetValueOrDefault("hasOwnershipDeclaration") == "true"))
            return paths.Split('\n').Select(NormalizeRoot);
        return [];
    }

    private static bool PathMatches(string path, string root)
        => root.Length == 0 || path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private static CisGraphNode MergeComponent(CisGraphNode existing, CisGraphNode incoming,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var authorityDiffers = (existing.Authority == "canonical") != (incoming.Authority == "canonical");
        var primary = incoming.Authority == "canonical" && existing.Authority != "canonical" ? incoming : existing;
        var secondary = ReferenceEquals(primary, existing) ? incoming : existing;
        var properties = new SortedDictionary<string, string>(primary.Properties.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);
        var conflicts = incoming.Properties.Where(pair => pair.Key is "classification" or "responsibility" or "owns" or "excludes" or "requirementIds"
            && pair.Value.Length > 0 && existing.Properties.TryGetValue(pair.Key, out var previous)
            && previous.Length > 0 && previous != pair.Value).Select(pair => pair.Key).ToArray();
        if (conflicts.Length > 0 || existing.Label != existing.LocalId && incoming.Label != incoming.LocalId && existing.Label != incoming.Label)
            diagnostics.Add(Diagnostic("CIS-GRAPH-COMPONENT-004", "warning",
                $"Component '{existing.LocalId}' has incompatible declarations; review retained source facts: {string.Join(", ", conflicts)}.",
                existing.Provenance.Concat(incoming.Provenance).Select(item => item.Path).Distinct().ToArray()));
        foreach (var pair in secondary.Properties.Where(pair => pair.Value.Length > 0
            && pair.Key is not ("ownsPaths" or "verificationPaths" or "hasOwnershipDeclaration")))
        {
            if (!properties.TryGetValue(pair.Key, out var previous) || previous.Length == 0) properties[pair.Key] = pair.Value;
            else if (!authorityDiffers && conflicts.Contains(pair.Key, StringComparer.Ordinal))
                properties[pair.Key] = previous + "\n" + pair.Value;
        }
        if (authorityDiffers && conflicts.Length > 0)
            properties["conflictingDeclarations"] = string.Join('\n', conflicts.Select(name => secondary.Authority + ":" + name + "=" + secondary.Properties[name]));
        foreach (var property in new[] { "ownsPaths", "verificationPaths" })
            properties[property] = string.Join('\n', ComponentPaths(existing, property).Concat(ComponentPaths(incoming, property))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
        properties["hasOwnershipDeclaration"] = (existing.Properties.GetValueOrDefault("hasOwnershipDeclaration") == "true"
            || incoming.Properties.GetValueOrDefault("hasOwnershipDeclaration") == "true").ToString().ToLowerInvariant();
        return existing with
        {
            Label = !authorityDiffers && primary.Label == primary.LocalId && secondary.Label != secondary.LocalId ? secondary.Label : primary.Label,
            Authority = primary.Authority,
            Lifecycle = primary.Lifecycle,
            Subtype = primary.Subtype,
            Properties = properties,
            Facets = existing.Facets.Concat(incoming.Facets).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Locations = existing.Locations.Concat(incoming.Locations).Distinct().ToArray(),
            Provenance = existing.Provenance.Concat(incoming.Provenance).Distinct().ToArray(),
        };
    }

    private static void DiagnoseComponentOwnership(IEnumerable<CisGraphNode> components, ICollection<CisGraphDiagnostic> diagnostics)
    {
        foreach (var group in components.SelectMany(component => ComponentPaths(component, "ownsPaths")
                .Select(path => (Component: component, Path: path)))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var owners = group.Select(item => item.Component).DistinctBy(component => component.Key).ToArray();
            if (owners.Length < 2) continue;
            diagnostics.Add(Diagnostic("CIS-GRAPH-COMPONENT-003", "warning",
                $"Equal-specificity component ownership is unresolved for '{(group.Key.Length == 0 ? "." : group.Key)}': "
                + string.Join(", ", owners.Select(component => component.LocalId).Order(StringComparer.Ordinal)),
                owners.SelectMany(component => component.Locations).Select(location => location.Path).Distinct().ToArray()));
        }
    }

    private static void LinkDeclaredComponentVerification(CisRepositoryContext context, string path,
        CisGraphNode test, IEnumerable<CisGraphNode> components, IDictionary<string, CisGraphEdge> edges)
    {
        foreach (var component in components.Where(component => ComponentPaths(component, "verificationPaths")
            .Any(prefix => PathMatches(path, prefix))))
        {
            var evidence = component.Provenance.Where(item => item.Method == "declared-path"
                && item.ConfidenceReason is { } reason && reason.StartsWith("verified-by:", StringComparison.Ordinal)
                && PathMatches(path, reason["verified-by:".Length..])).ToArray();
            AddEdge(context.RepositoryId, "verified-by", component.Key, test.Key, "declared", "high", evidence, edges,
                SortedProperties(("mapping", "explicit-path"), ("role", "declared-applicable-check"),
                    ("coverageAssessment", "not-assessed"), ("meaningAssessment", "not-assessed"), ("result", "not-recorded")));
        }
    }
}
