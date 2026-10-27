using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private sealed record Selection(CisGraphSnapshotReadResult Snapshot, IncrementalTestBatch[] Batches);

    private Selection Select(Input input, IncrementalTestState state, bool force)
    {
        var snapshot = _graph.Read(input.Repository);
        Require(snapshot.ExitCode == 0 && snapshot.Freshness == "fresh" && snapshot.Graph?.Build.Status == "complete"
            && snapshot.Manifest is not null && snapshot.Graph.DiagnosticSummary.Errors == 0 && snapshot.Graph.DiagnosticSummary.Warnings == 0,
            "A fresh, complete graph without errors or warnings is required. Run cis graph build before planning. " + string.Join("; ", snapshot.Errors));
        var graph = snapshot.Graph!;
        Require(graph.Nodes.Count <= 250000 && graph.Edges.Count <= 1000000, "Graph exceeds the incremental planning bound.");
        var paths = graph.Nodes.ToDictionary(node => node.Key, node => node.Locations.Select(location => location.Path.Replace('\\', '/'))
            .Where(path => CisPathSafety.TryResolveUnderRoot(input.Repository, path, out var full) && File.Exists(full))
            .Distinct(PathComparer).ToArray(), StringComparer.Ordinal);
        var forward = new Dictionary<string, HashSet<string>>(PathComparer);
        var reverse = new Dictionary<string, HashSet<string>>(PathComparer);
        foreach (var edge in graph.Edges.Where(edge => edge.State is not ("proposed" or "rejected")
            && edge.Type is "calls" or "depends-on" or "references" or "implemented-by" or "implements" or "inherits-from"))
        {
            if (!paths.TryGetValue(edge.From, out var from) || !paths.TryGetValue(edge.To, out var to)) continue;
            foreach (var source in from) foreach (var target in to)
            {
                if (PathComparer.Equals(source, target)) continue;
                Add(forward, source, target);
                Add(reverse, target, source);
            }
        }
        var profile = input.Profile;
        var allTests = profile.TestFiles.ToHashSet(PathComparer);
        var testComponents = graph.Nodes.Where(node => paths[node.Key].Any(allTests.Contains))
            .Select(node => node.Properties.GetValueOrDefault("component")).Where(value => value is not null).ToHashSet(StringComparer.Ordinal);
        // New test files in the same graph components must enter selection without a manual inventory edit.
        allTests.UnionWith(graph.Nodes.Where(node => node.Kind == "test"
            && testComponents.Contains(node.Properties.GetValueOrDefault("component"))).SelectMany(node => paths[node.Key]));
        var represented = paths.Values.SelectMany(value => value).ToHashSet(PathComparer);
        var allInputs = snapshot.Manifest!.Inputs.Select(item => item.Path)
            .Concat(profile.SourceFiles).Concat(profile.TestFiles).Distinct(PathComparer).Order(StringComparer.Ordinal).ToArray();
        var result = new List<IncrementalTestBatch>();
        foreach (var source in profile.SourceFiles.OrderBy(file => !PathComparer.Equals(file, profile.CanarySourceFile)).ThenBy(file => file, StringComparer.Ordinal))
        {
            var callers = Closure([source], reverse);
            var tests = callers.Where(allTests.Contains).Order(StringComparer.Ordinal).ToArray();
            var classes = graph.Nodes.Where(node => node.Kind == "symbol" && node.Subtype == "class" && node.Facets.Contains("csharp", StringComparer.Ordinal)
                && paths[node.Key].Any(path => tests.Contains(path, PathComparer)))
                .Select(node => node.Properties.GetValueOrDefault("qualifiedName"))
                .Where(name => name is not null && Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_.+]*\z", RegexOptions.CultureInvariant))
                .Select(name => "FullyQualifiedName~" + name + ".").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var narrow = source.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && represented.Contains(source) && tests.Length > 0 && classes.Length > 0
                && snapshot.GraphCapabilities.Contains("compiler.calls", StringComparer.Ordinal);
            var filter = narrow ? "(" + profile.FullTestFilter + ")&(" + string.Join('|', classes) + ")" : profile.FullTestFilter;
            if (filter.Length > 32768) narrow = false;
            if (!narrow) { tests = allTests.Order(StringComparer.Ordinal).ToArray(); filter = profile.FullTestFilter; }
            // File-level closure includes shared fixtures and transitive implementation dependencies.
            // Inconclusive selection uses every graph input, rather than treating missing edges as independence.
            var dependencies = (narrow ? Closure(tests.Append(source), forward) : allInputs.ToHashSet(PathComparer))
                .Order(StringComparer.Ordinal).ToArray();
            var inputHashes = dependencies.Select(file => new { file, hash = HashFile(Safe(input.Repository, file)) }).ToArray();
            var relevantEdges = dependencies.SelectMany(file => forward.GetValueOrDefault(file, []).Select(target => file + "->" + target))
                .Order(StringComparer.Ordinal).ToArray();
            var identity = HashObject(new { input.CommonIdentity, source, tests, filter, inputHashes, relevantEdges,
                graph.SchemaVersion, snapshot.Manifest.Extractors, selection = narrow ? "graph" : "all-tests" });
            var unit = HashObject(new { identity, layer = "unit" });
            var mutation = HashObject(new { identity, layer = "mutation" });
            var id = "file-" + HashObject(source)[..20];
            state.Batches.TryGetValue(id, out var prior);
            result.Add(new(id, source, narrow ? "graph" : "all-tests", tests, filter, unit, mutation,
                !force && Current(input, source, prior?.Unit, unit, false),
                !force && profile.MutationCommand is not null && Current(input, source, prior?.Mutation, mutation, true), dependencies));
        }
        return new(snapshot, result.ToArray());
    }

    private static void Add(Dictionary<string, HashSet<string>> map, string from, string to)
    {
        if (!map.TryGetValue(from, out var targets)) map[from] = targets = new(PathComparer);
        targets.Add(to);
    }

    private static HashSet<string> Closure(IEnumerable<string> roots, Dictionary<string, HashSet<string>> links)
    {
        var found = roots.ToHashSet(PathComparer);
        var queue = new Queue<string>(found);
        while (queue.TryDequeue(out var current))
            if (links.TryGetValue(current, out var next))
                foreach (var target in next) if (found.Add(target)) queue.Enqueue(target);
        return found;
    }

    private static bool Current(Input input, string source, IncrementalStageResult? result, string fingerprint, bool mutation)
    {
        if (result is not { Completed: true } || result.Fingerprint != fingerprint) return false;
        try
        {
            var path = Safe(input.Root, result.ReportPath);
            if (!File.Exists(path) || HashFile(path) != result.ReportSha256) return false;
            if (!mutation)
            {
                var unit = ReadUnit(input, path);
                return unit is { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 }
                    && unit.Total == result.Total && unit.Passed == result.Passed && unit.Failed == result.Failed && unit.Skipped == result.Skipped;
            }
            var measured = ReadMutation(input, source, path).Mutation;
            return measured is { ValidEvidence: true, RuntimeErrors: 0, Pending: 0, Unknown: 0 }
                && HashObject(measured) == HashObject(result.Mutation);
        }
        catch (Exception error) when (Expected(error)) { return false; }
    }
}
