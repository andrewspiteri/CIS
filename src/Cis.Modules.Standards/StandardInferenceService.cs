using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Standards;

public sealed class StandardInferenceService
{
    private readonly StandardPatternCatalogService _catalog;
    private readonly ICisGraphSnapshotReader? _graphReader;

    public StandardInferenceService(StandardPatternCatalogService catalog, ICisGraphSnapshotReader? graphReader)
    {
        _catalog = catalog;
        _graphReader = graphReader;
    }

    public StandardInferenceResult Infer(StandardInferenceRequest request)
    {
        if (request.EvidenceLimit is < 1 or > 100)
            return Result("invalid", 2, null, null, "unknown", [], [], [], ["Evidence limit must be between 1 and 100."]);
        if (_graphReader is null)
            return Result("unavailable", 4, null, null, "missing", [], [], [], ["No graph snapshot provider is loaded. Load the graph module and run `cis graph build`."]);
        var snapshot = _graphReader.Read(request.RepositoryPath);
        if (snapshot.ExitCode != 0 || snapshot.Graph is null || snapshot.Manifest is null)
            return Result(snapshot.Status, snapshot.ExitCode, snapshot.RepositoryPath, snapshot.Graph?.Build.Id, snapshot.Freshness, [], [], snapshot.Warnings, snapshot.Errors);

        var catalog = _catalog.Inventory(new StandardPatternCatalogRequest(request.RepositoryPath, request.Languages, ApplicableOnly: true, Strict: true));
        if (catalog.ExitCode != 0)
            return Result("invalid-pattern-catalog", catalog.ExitCode, snapshot.RepositoryPath, snapshot.Graph.Build.Id, snapshot.Freshness, [], [], catalog.Warnings, catalog.Errors);
        var requested = request.PatternIds.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableIds = catalog.Patterns.Select(item => item.Definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = requested.Where(id => !availableIds.Contains(id)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            return Result("invalid", 2, snapshot.RepositoryPath, snapshot.Graph.Build.Id, snapshot.Freshness, [], [], [], unknown.Select(id => $"Requested pattern '{id}' is not an applicable catalog entry.").ToArray());

        var selected = catalog.Patterns.Where(item => requested.Count == 0 || requested.Contains(item.Definition.Id)).ToArray();
        var nodes = snapshot.Graph.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
        var outgoing = snapshot.Graph.Edges.GroupBy(edge => edge.From, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var incoming = snapshot.Graph.Edges.GroupBy(edge => edge.To, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var componentFilter = request.Components.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evaluations = new List<StandardPatternEvaluation>();
        var candidates = new List<InferredStandardCandidate>();
        foreach (var item in selected)
        {
            var pattern = item.Definition;
            if (item.MissingGraphCapabilities.Count > 0)
            {
                evaluations.Add(new(pattern.Id, pattern.Version, "requires-graph-capability", 0, 0, 0, item.MissingGraphCapabilities, [], []));
                continue;
            }
            var includesGenerated = pattern.Subject.Facets.Contains("generated", StringComparer.OrdinalIgnoreCase)
                || pattern.Subject.PropertyEquals.TryGetValue("generated", out var generatedValue)
                    && generatedValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            var eligible = snapshot.Graph.Nodes
                .Where(node => includesGenerated || !node.Facets.Contains("generated", StringComparer.OrdinalIgnoreCase))
                .Where(node => Matches(node, pattern.Subject))
                .Where(node => componentFilter.Count == 0 || node.Properties.TryGetValue("component", out var component) && componentFilter.Contains(component))
                .OrderBy(node => node.Key, StringComparer.Ordinal).ToArray();
            var matching = new List<StandardPatternEvidence>();
            var counterexamples = new List<StandardPatternEvidence>();
            foreach (var subject in eligible)
            {
                var required = pattern.Requires.Select(relation => Related(subject, relation, nodes, outgoing, incoming)).ToArray();
                var forbidden = pattern.Forbids.Select(relation => Related(subject, relation, nodes, outgoing, incoming)).ToArray();
                var conforms = required.All(matches => matches.Count > 0) && forbidden.All(matches => matches.Count == 0);
                var related = required.SelectMany(matches => matches).Concat(forbidden.SelectMany(matches => matches)).Select(node => node.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(20).ToArray();
                var evidence = Evidence(subject, related);
                if (conforms) matching.Add(evidence); else counterexamples.Add(evidence);
            }
            var consistency = eligible.Length == 0 ? 0 : matching.Count / (double)eligible.Length;
            var status = eligible.Length < pattern.MinimumOccurrences ? "insufficient-occurrences"
                : consistency < pattern.MinimumConsistency ? "insufficient-consistency" : "candidate";
            var matchEvidence = matching.Take(request.EvidenceLimit).ToArray();
            var counterEvidence = counterexamples.Take(request.EvidenceLimit).ToArray();
            evaluations.Add(new(pattern.Id, pattern.Version, status, eligible.Length, matching.Count, consistency, [], matchEvidence, counterEvidence));
            if (status == "candidate")
                candidates.Add(new(
                    CandidateId(snapshot.Graph.Build.RepositoryId, pattern), pattern.Id, pattern.Version, "Unreviewed",
                    pattern.Candidate.Target, pattern.Candidate.ProposedLevel, pattern.Candidate.Wording,
                    pattern.Candidate.SuggestedEnforcement, pattern.Candidate.Verification,
                    eligible.Length, matching.Count, consistency, snapshot.Graph.Build.Id, matchEvidence, counterEvidence));
        }
        var result = Result(candidates.Count > 0 ? "candidates" : "observed", 0, snapshot.RepositoryPath, snapshot.Graph.Build.Id, snapshot.Freshness, evaluations, candidates, snapshot.Warnings, []);
        var persisted = result with { JsonPath = ".cis/local/standards/inference/report.json", MarkdownPath = ".cis/local/standards/inference/report.md" };
        Persist(snapshot.RepositoryPath!, persisted);
        return persisted;
    }

    private static IReadOnlyList<CisGraphNode> Related(
        CisGraphNode subject,
        CisStandardPatternRelation relation,
        IReadOnlyDictionary<string, CisGraphNode> nodes,
        IReadOnlyDictionary<string, CisGraphEdge[]> outgoing,
        IReadOnlyDictionary<string, CisGraphEdge[]> incoming)
    {
        var edges = relation.Direction == "incoming" ? incoming.GetValueOrDefault(subject.Key) ?? [] : outgoing.GetValueOrDefault(subject.Key) ?? [];
        return edges.Where(edge => edge.Type.Equals(relation.EdgeType, StringComparison.OrdinalIgnoreCase))
            .Select(edge => relation.Direction == "incoming" ? edge.From : edge.To)
            .Where(nodes.ContainsKey).Select(key => nodes[key]).Where(node => Matches(node, relation.Target)).ToArray();
    }

    private static bool Matches(CisGraphNode node, CisStandardPatternSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Kind) && !node.Kind.Equals(selector.Kind, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(selector.Subtype) && !node.Subtype.Equals(selector.Subtype, StringComparison.OrdinalIgnoreCase)) return false;
        if (selector.Facets.Any(facet => !node.Facets.Contains(facet, StringComparer.OrdinalIgnoreCase))) return false;
        foreach (var property in selector.PropertyEquals)
            if (!node.Properties.TryGetValue(property.Key, out var value) || !value.Equals(property.Value, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var property in selector.PropertyContains)
            if (!node.Properties.TryGetValue(property.Key, out var value) || !value.Contains(property.Value, StringComparison.OrdinalIgnoreCase)) return false;
        var paths = node.Locations.Select(location => location.Path.Replace('\\', '/')).ToArray();
        if (selector.PathPrefixes.Count > 0 && !paths.Any(path => selector.PathPrefixes.Any(prefix => path.StartsWith(prefix.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))) return false;
        if (paths.Any(path => selector.ExcludedPathPrefixes.Any(prefix => path.StartsWith(prefix.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))) return false;
        return true;
    }

    private static StandardPatternEvidence Evidence(CisGraphNode node, IReadOnlyList<string> related)
    {
        var location = node.Locations.FirstOrDefault();
        return new(node.Key, node.Label, location?.Path, location is null ? null : $"{location.LocatorKind}:{location.LocatorValue}", related);
    }

    private static string CandidateId(string repositoryId, CisStandardPatternDefinition pattern)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{repositoryId}\u001f{pattern.Id}\u001f{pattern.Version}")));
        return "STD-CAND-" + hash[..12];
    }

    private static void Persist(string repositoryPath, StandardInferenceResult result)
    {
        var root = Path.Combine(repositoryPath, ".cis", "local", "standards", "inference"); Directory.CreateDirectory(root);
        Write(Path.Combine(root, "report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
        var markdown = new StringBuilder().AppendLine("# Inferred standard candidates").AppendLine()
            .AppendLine($"- Graph build: `{result.GraphBuildId}`").AppendLine($"- Freshness: {result.Freshness}")
            .AppendLine($"- Patterns: {result.PatternCount}; evaluated: {result.EvaluatedCount}; candidates: {result.CandidateCount}").AppendLine();
        foreach (var evaluation in result.Evaluations)
            markdown.AppendLine($"- `{evaluation.PatternId}`: {evaluation.Status}; {evaluation.MatchingOccurrences}/{evaluation.EligibleOccurrences} ({evaluation.Consistency:P1})");
        foreach (var candidate in result.Candidates)
        {
            markdown.AppendLine().AppendLine($"## {candidate.Id}: {candidate.PatternId}").AppendLine()
                .AppendLine($"- Status: {candidate.Status}").AppendLine($"- Proposed level: {candidate.ProposedLevel}").AppendLine($"- Proposed rule: {candidate.ProposedWording}")
                .AppendLine($"- Evidence: {candidate.MatchingOccurrences}/{candidate.EligibleOccurrences} ({candidate.Consistency:P1})").AppendLine($"- Suggested enforcement: {candidate.SuggestedEnforcement}");
            foreach (var evidence in candidate.Evidence) markdown.AppendLine($"- Match: `{evidence.Path}` {evidence.Label}");
            foreach (var evidence in candidate.Counterexamples) markdown.AppendLine($"- Counterexample: `{evidence.Path}` {evidence.Label}");
        }
        markdown.AppendLine().AppendLine("Candidates are derived observations. They are not canonical standards and cannot be accepted without human review.");
        Write(Path.Combine(root, "report.md"), markdown.ToString());
    }

    private static StandardInferenceResult Result(
        string status, int exitCode, string? repositoryPath, string? graphBuildId, string freshness,
        IReadOnlyList<StandardPatternEvaluation> evaluations, IReadOnlyList<InferredStandardCandidate> candidates,
        IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        => new(status, exitCode, repositoryPath, graphBuildId, freshness, evaluations.Count, evaluations.Count(item => item.Status != "requires-graph-capability"), candidates.Count,
            null, null, evaluations, candidates, warnings.Distinct().ToArray(), errors.Distinct().ToArray());

    private static void Write(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, overwrite: true); }
}
