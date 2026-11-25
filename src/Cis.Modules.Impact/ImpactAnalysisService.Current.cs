using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Impact;

public sealed partial class ImpactAnalysisService
{
    private ImpactResult AnalyseCurrent(ChangeDossier change, ImpactAnalyseRequest request, IReadOnlyList<ChangeRoot> roots)
    {
        CisInvocationTelemetry.AssociateTask(change.Id, null);
        if (_snapshots is null) return Error(change.Id, "Current-impact graph orchestration is unavailable; use the composed graph module.");
        if (roots.Count > 20) return Error(change.Id, "Current analysis supports at most 20 explicit roots per bounded decision.");

        var snapshot = _snapshots.ReadCurrent(request.RepositoryPath);
        if (snapshot is not { ExitCode: 0, Freshness: "fresh", Graph: not null, Manifest: not null }
            || snapshot.Graph.DiagnosticSummary.Errors > 0)
            return Error(change.Id, "Current impact requires a current trustworthy input inventory: " + string.Join("; ", snapshot.Errors));

        var findings = new Dictionary<string, ImpactFinding>(StringComparer.Ordinal);
        var diagnostics = snapshot.Diagnostics.Select(item => item.Severity.ToUpperInvariant() + ": " + item.Code + " " + item.Message)
            .Take(16).ToList();
        if (snapshot.Diagnostics.Count > 16) diagnostics.Add($"{snapshot.Diagnostics.Count - 16} additional diagnostics remain in the graph record.");
        var truncated = false;
        foreach (var root in roots)
        {
            var query = _graph.Related(request.RepositoryPath,
                new(root.Id, root.Kind, ImpactEdges, "both", request.Depth, request.Limit, request.IncludeProposed));
            if (query.ExitCode != 0 || query.Freshness != "fresh" || query.Build?.Id != snapshot.Graph.Build.Id)
                return Error(change.Id, "Current graph changed or could not support the requested traversal: "
                    + string.Join("; ", query.Diagnostics.Select(item => item.Message)));
            if (query.Nodes.Count == 0) return Error(change.Id, "Current impact root did not resolve exactly: " + root.Id);

            truncated |= query.Truncated;
            var rootKey = query.Nodes[0].Key;
            foreach (var node in query.Nodes)
            {
                if (findings.Count >= 1000 && !findings.ContainsKey(node.Key))
                {
                    truncated = true;
                    continue;
                }
                var traversal = query.Traversals.Where(item => item.Edge.From == node.Key || item.Edge.To == node.Key)
                    .OrderBy(item => item.Depth).ThenBy(item => item.Edge.Key, StringComparer.Ordinal).FirstOrDefault();
                findings[node.Key] = CreateFinding(node, rootKey, traversal);
            }
        }

        // This is a current bounded projection, never an amendment to an approved baseline
        // or its finding dispositions. Check freshness again without silently refreshing a race.
        CisReadScope.Invalidate();
        var final = _snapshots.ReadMetadata(request.RepositoryPath);
        if (final.ExitCode != 0 || final.Freshness != "fresh" || final.Build?.Id != snapshot.Graph.Build.Id)
            return Error(change.Id, "Inputs changed during current impact analysis; repeat the bounded decision after edits settle.");

        var incomplete = truncated || snapshot.Graph.Build.Status != "complete" || snapshot.Graph.DiagnosticSummary.Warnings > 0;
        diagnostics.Add("Current implementation projection; the original approved baseline and dispositions are unchanged.");
        if (incomplete) diagnostics.Add("Selection is bounded or extraction has limitations; these findings do not establish complete current impact.");
        var selected = findings.Values.OrderBy(item => CategoryOrder(item.Category)).ThenBy(item => item.Target, StringComparer.Ordinal).ToArray();
        CisInvocationTelemetry.RecordGraph("current-impact", snapshot.Graph.Build.Id, "current",
            incomplete ? "bounded-or-incomplete" : "current-inputs");
        return new(incomplete ? "current-partial" : "current-analysed", change.Id, snapshot.Graph.Build.Id,
            selected, CalculateCompleteness(selected, truncated), diagnostics, false)
        {
            AnalysisBasis = "current-implementation",
            DecisionSuitability = incomplete ? "incomplete-current" : "bounded-current"
        };
    }
}
