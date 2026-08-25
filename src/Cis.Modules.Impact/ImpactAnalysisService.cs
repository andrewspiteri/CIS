using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Impact;

public sealed class ImpactAnalysisService
{
    private static readonly string[] ImpactEdges =
    [
        "declares", "references", "governed-by", "describes", "owns", "belongs-to",
        "implemented-by", "consumed-by", "produced-by", "depends-on", "calls",
        "publishes", "subscribes-to", "configured-by", "deployed-by", "verified-by",
        "generated-from", "supersedes",
    ];

    private readonly ChangeDossierStore _changes;
    private readonly ICisGraphQueryService _graph;

    public ImpactAnalysisService(ChangeDossierStore changes, ICisGraphQueryService graph)
    {
        _changes = changes;
        _graph = graph;
    }

    public ImpactResult Analyse(ImpactAnalyseRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null)
        {
            return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        }

        if (request.Depth is < 1 or > 10 || request.Limit is < 1 or > 1000)
        {
            return Error(change.Id, "Depth must be 1-10 and limit must be 1-1000.");
        }

        var roots = request.Roots.Count > 0 ? request.Roots : change.Roots;
        if (roots.Count == 0)
        {
            return Error(change.Id, "At least one impact root is required in the proposal or command.");
        }

        var prior = ReadImpact(change);
        var diagnostics = new List<string>();
        var allFindings = new Dictionary<string, ImpactFinding>(StringComparer.Ordinal);
        var truncated = false;
        foreach (var root in roots)
        {
            var query = _graph.Related(
                request.RepositoryPath,
                new CisGraphRelatedRequest(
                    root.Id,
                    root.Kind,
                    ImpactEdges,
                    "both",
                    request.Depth,
                    request.Limit,
                    request.IncludeProposed));
            if (query.ExitCode != 0)
            {
                return Error(change.Id, string.Join("; ", query.Diagnostics.Select(item => item.Message)));
            }

            if (!string.Equals(query.Build?.Id, change.GraphBuildId, StringComparison.Ordinal))
            {
                return Error(
                    change.Id,
                    $"Change baseline graph is {change.GraphBuildId}, but the current graph is {query.Build?.Id ?? "unavailable"}. " +
                    "Analyse before implementation or create a new explicit baseline.");
            }

            if (query.Nodes.Count == 0)
            {
                return Error(change.Id, $"Impact root did not resolve exactly: {root.Id}");
            }

            truncated |= query.Truncated;
            diagnostics.AddRange(query.Diagnostics.Select(item => $"{item.Severity.ToUpperInvariant()}: {item.Code} {item.Message}"));
            var rootKey = query.Nodes[0].Key;
            foreach (var node in query.Nodes)
            {
                var traversal = query.Traversals
                    .Where(item => item.Edge.From == node.Key || item.Edge.To == node.Key)
                    .OrderBy(item => item.Depth)
                    .ThenBy(item => item.Edge.Key, StringComparer.Ordinal)
                    .FirstOrDefault();
                var finding = CreateFinding(node, rootKey, traversal);
                allFindings[finding.Id] = PreserveDisposition(finding, prior.Findings);
            }

            foreach (var finding in CreateDeclaredTargetFindings(request.RepositoryPath, query.Nodes[0]))
            {
                allFindings[finding.Id] = PreserveDisposition(finding, prior.Findings);
            }
        }

        foreach (var reviewed in prior.Findings.Where(item => item.State != "proposed"))
        {
            allFindings.TryAdd(reviewed.Id, reviewed with
            {
                Rationale = reviewed.Rationale + " (not rediscovered at the current analysis bounds)",
            });
        }

        var findings = allFindings.Values
            .OrderBy(item => CategoryOrder(item.Category))
            .ThenBy(item => item.Target, StringComparer.Ordinal)
            .ToArray();
        WriteImpact(change, findings, truncated);
        _changes.AppendEvent(change, "impact-analysed", new Dictionary<string, string>
        {
            ["findingCount"] = findings.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["roots"] = roots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["truncated"] = truncated.ToString().ToLowerInvariant(),
        });
        var completeness = CalculateCompleteness(findings, truncated);
        return new ImpactResult("analysed", change.Id, change.GraphBuildId, findings, completeness, diagnostics, true);
    }

    public ImpactResult Findings(string repositoryPath, string changeId, string? state = null)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var document = ReadImpact(change);
        var findings = string.IsNullOrWhiteSpace(state)
            ? document.Findings
            : document.Findings.Where(item => string.Equals(item.State, state, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new ImpactResult("listed", change.Id, change.GraphBuildId, findings, CalculateCompleteness(document.Findings, document.Truncated), [], false);
    }

    public ImpactResult Disposition(
        string repositoryPath,
        string changeId,
        string findingId,
        string state,
        string reason)
    {
        if (state is not ("accepted" or "rejected" or "deferred"))
        {
            return Error(changeId, "Disposition must be accepted, rejected, or deferred.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error(changeId, "A review reason is required.");
        }

        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var document = ReadImpact(change);
        var index = document.Findings.ToList().FindIndex(item => item.Id == findingId);
        if (index < 0)
        {
            return Error(changeId, $"Impact finding was not found: {findingId}");
        }

        var findings = document.Findings.ToArray();
        findings[index] = findings[index] with { State = state, ReviewReason = Clean(reason) };
        WriteImpact(change, findings, document.Truncated);
        _changes.AppendEvent(change, $"impact-{state}", new Dictionary<string, string>
        {
            ["findingId"] = findingId,
            ["reason"] = Clean(reason),
        });
        return new ImpactResult(state, change.Id, change.GraphBuildId, findings, CalculateCompleteness(findings, document.Truncated), [], true);
    }

    public ImpactResult CarryForwardApprovedFeature(
        string repositoryPath,
        string changeId,
        string featureItemId,
        string featurePath,
        string approvedContentHash,
        string reviewer)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
            return Error(changeId, $"Change dossier was not found: {changeId}");

        var document = ReadImpact(change);
        if (document.Findings.Count == 0)
            return Error(change.Id, "Impact analysis must run before approved feature authority can carry forward.");
        if (document.Truncated)
            return Error(change.Id, "Truncated impact analysis requires explicit human review and cannot use authority carry-forward.");
        if (document.Findings.Any(item => item.State == "deferred"))
            return Error(change.Id, "Deferred impact findings require explicit human review and cannot use authority carry-forward.");
        var uncertain = document.Findings
            .Where(item => item.State == "proposed" && item.Confidence.Equals("low", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id)
            .ToArray();
        if (uncertain.Length > 0)
            return Error(change.Id, "Low-confidence impact findings require explicit human review: " + string.Join(", ", uncertain));

        var proposed = document.Findings.Where(item => item.State == "proposed").ToArray();
        if (proposed.Length == 0)
            return new ImpactResult("unchanged", change.Id, change.GraphBuildId, document.Findings,
                CalculateCompleteness(document.Findings, document.Truncated), [], false);

        var reason = Clean($"Carried forward from the current human approval of {featureItemId} ({featurePath}, {approvedContentHash}).");
        var findings = document.Findings.Select(item => item.State == "proposed"
            ? item with { State = "accepted", ReviewReason = reason }
            : item).ToArray();
        WriteImpact(change, findings, document.Truncated);
        _changes.AppendEvent(change, "impact-authority-carried-forward", new Dictionary<string, string>
        {
            ["featureItemId"] = featureItemId,
            ["featurePath"] = featurePath,
            ["approvedContentHash"] = approvedContentHash,
            ["approvedBy"] = reviewer,
            ["acceptedFindings"] = proposed.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        return new ImpactResult("authority-carried-forward", change.Id, change.GraphBuildId, findings,
            CalculateCompleteness(findings, document.Truncated), [], true);
    }

    public ImpactResult Completeness(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var document = ReadImpact(change);
        return new ImpactResult(
            "assessed",
            change.Id,
            change.GraphBuildId,
            document.Findings,
            CalculateCompleteness(document.Findings, document.Truncated),
            [],
            false);
    }

    public ImpactDocument ReadImpact(ChangeDossier change)
    {
        var path = _changes.DossierFile(change, "impact.md");
        if (!File.Exists(path))
        {
            return new ImpactDocument([], false);
        }

        var lines = File.ReadAllLines(path);
        var truncated = lines.Any(line => line.Equals("analysis_truncated: true", StringComparison.Ordinal));
        var findings = lines
            .Where(line => line.StartsWith("| IMPACT-", StringComparison.Ordinal))
            .Select(ParseFinding)
            .Where(item => item is not null)
            .Cast<ImpactFinding>()
            .ToArray();
        return new ImpactDocument(findings, truncated);
    }

    public static ImpactCompleteness CalculateCompleteness(IReadOnlyList<ImpactFinding> findings, bool truncated)
    {
        var proposed = findings.Count(item => item.State == "proposed");
        var accepted = findings.Count(item => item.State == "accepted");
        var rejected = findings.Count(item => item.State == "rejected");
        var deferred = findings.Count(item => item.State == "deferred");
        var gaps = new List<string>();
        if (findings.Count == 0)
        {
            gaps.Add("No impact findings exist.");
        }

        if (proposed > 0)
        {
            gaps.Add($"{proposed} finding(s) still require human disposition.");
        }

        if (deferred > 0)
        {
            gaps.Add($"{deferred} finding(s) are deferred and remain outside approved scope.");
        }

        if (truncated)
        {
            gaps.Add("At least one graph traversal was truncated; broaden bounds or record the residual risk.");
        }

        if (accepted == 0)
        {
            gaps.Add("No accepted impact exists from which to build bounded work.");
        }

        return new ImpactCompleteness(
            findings.Count,
            proposed,
            accepted,
            rejected,
            deferred,
            findings.Select(item => item.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            truncated,
            findings.Count > 0 && proposed == 0,
            findings.Count > 0 && proposed == 0 && deferred == 0 && !truncated && accepted > 0,
            gaps);
    }

    private static ImpactFinding CreateFinding(CisGraphNode node, string rootKey, CisGraphTraversal? traversal)
    {
        var category = Category(node);
        var id = FindingId(node.Key);
        var evidence = string.Join(
            "; ",
            node.Locations.Take(3).Select(location => $"{location.Path}:{location.LocatorKind}={location.LocatorValue}"));
        if (string.IsNullOrWhiteSpace(evidence))
        {
            evidence = node.Provenance.FirstOrDefault()?.Path ?? node.Key;
        }

        var isRoot = node.Key == rootKey;
        return new ImpactFinding(
            id,
            category,
            node.Key,
            Clean(node.Label),
            "proposed",
            isRoot ? "high" : traversal?.Edge.Confidence ?? "medium",
            Clean(evidence),
            isRoot
                ? "Explicit analysis root."
                : $"Related through {traversal?.Edge.Type ?? "graph evidence"} at depth {traversal?.Depth ?? 0}.",
            string.Empty);
    }

    private static IReadOnlyList<ImpactFinding> CreateDeclaredTargetFindings(
        string repositoryPath,
        CisGraphNode root)
    {
        if (!string.Equals(root.Kind, "document", StringComparison.Ordinal)
            || !string.Equals(root.Subtype, "feature-specification", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var location = root.Locations.FirstOrDefault(item =>
            string.Equals(item.LocatorKind, "document", StringComparison.OrdinalIgnoreCase));
        if (location is null || string.IsNullOrWhiteSpace(location.Path))
        {
            return [];
        }

        var repository = Path.GetFullPath(repositoryPath);
        var absolute = Path.GetFullPath(Path.Combine(
            repository,
            location.Path.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(repository + Path.DirectorySeparatorChar, comparison)
            || !File.Exists(absolute))
        {
            return [];
        }

        return ReadFrontMatterList(File.ReadAllText(absolute), "targets")
            .Where(IsRepositoryId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(target =>
            {
                var key = $"{target}::repository::{target}";
                return new ImpactFinding(
                    FindingId(key),
                    "repository",
                    key,
                    target,
                    "proposed",
                    "high",
                    Clean($"{location.Path}:frontmatter=targets/{target}"),
                    "Declared target repository of the explicit feature-specification root.",
                    string.Empty);
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ReadFrontMatterList(string content, string key)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || !string.Equals(lines[0].TrimStart('\uFEFF'), "---", StringComparison.Ordinal))
        {
            return [];
        }

        var values = new List<string>();
        var reading = false;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                break;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.Contains(':', StringComparison.Ordinal))
            {
                var separator = line.IndexOf(':');
                var candidate = line[..separator].Trim();
                reading = string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase);
                if (reading)
                {
                    var inline = line[(separator + 1)..].Trim();
                    if (inline.StartsWith("[", StringComparison.Ordinal) && inline.EndsWith("]", StringComparison.Ordinal))
                    {
                        values.AddRange(inline[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(Unquote).Where(value => value.Length > 0));
                        reading = false;
                    }
                }

                continue;
            }

            if (reading && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                var value = Unquote(line.TrimStart()[2..]);
                if (value.Length > 0) values.Add(value);
            }
        }

        return values;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2
               && (trimmed[0] == '"' && trimmed[^1] == '"'
                   || trimmed[0] == '\'' && trimmed[^1] == '\'')
            ? trimmed[1..^1].Trim()
            : trimmed;
    }

    private static bool IsRepositoryId(string value)
        => value.Length is >= 1 and <= 128
           && char.IsLetterOrDigit(value[0])
           && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

    private static ImpactFinding PreserveDisposition(
        ImpactFinding finding,
        IReadOnlyList<ImpactFinding> prior)
    {
        return prior.FirstOrDefault(item => item.Id == finding.Id) is { } existing
            ? finding with
            {
                State = existing.State,
                ReviewReason = existing.ReviewReason,
            }
            : finding;
    }

    private static string FindingId(string key)
        => "IMPACT-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10];

    private void WriteImpact(ChangeDossier change, IReadOnlyList<ImpactFinding> findings, bool truncated)
    {
        var status = findings.All(item => item.State != "proposed") && findings.Count > 0 ? "Reviewed" : "Draft";
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(change.Id + " impact analysis")}");
        builder.AppendLine("type: impact-analysis");
        builder.AppendLine($"status: {status}");
        builder.AppendLine($"change_id: {change.Id}");
        builder.AppendLine($"graph_build_id: {JsonSerializer.Serialize(change.GraphBuildId)}");
        builder.AppendLine($"analysis_truncated: {truncated.ToString().ToLowerInvariant()}");
        builder.AppendLine("authority: human-reviewed");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Impact analysis");
        builder.AppendLine();
        builder.AppendLine("Deterministic findings are proposals until explicitly accepted, rejected, or deferred by a human.");
        builder.AppendLine();
        builder.AppendLine("## Findings");
        builder.AppendLine();
        builder.AppendLine("| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in findings)
        {
            builder.AppendLine($"| {item.Id} | {item.Category} | {Clean(item.Target)} | {Clean(item.Label)} | {item.State} | {item.Confidence} | {Clean(item.Evidence)} | {Clean(item.Rationale)} | {Clean(item.ReviewReason)} |");
        }

        var completeness = CalculateCompleteness(findings, truncated);
        builder.AppendLine();
        builder.AppendLine("## Coverage");
        builder.AppendLine();
        builder.AppendLine($"- Total findings: {completeness.Total}");
        builder.AppendLine($"- Accepted: {completeness.Accepted}");
        builder.AppendLine($"- Proposed: {completeness.Proposed}");
        builder.AppendLine($"- Rejected: {completeness.Rejected}");
        builder.AppendLine($"- Deferred: {completeness.Deferred}");
        builder.AppendLine($"- Traversal truncated: {completeness.Truncated.ToString().ToLowerInvariant()}");
        File.WriteAllText(_changes.DossierFile(change, "impact.md"), builder.ToString());
    }

    private static ImpactFinding? ParseFinding(string line)
    {
        var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
        return cells.Length >= 11
            ? new ImpactFinding(cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7], cells[8], cells[9])
            : null;
    }

    private static string Category(CisGraphNode node) => node.Kind switch
    {
        "reference-item" => "contract",
        "document" => "documentation",
        "test" => "verification",
        "workflow" => "delivery",
        "dependency" => "dependency",
        "repository" => "repository",
        "source-file" or "symbol" or "component" => "implementation",
        _ => node.Kind,
    };

    private static int CategoryOrder(string category) => category switch
    {
        "documentation" => 0,
        "contract" => 1,
        "dependency" => 2,
        "implementation" => 3,
        "delivery" => 4,
        "verification" => 5,
        _ => 6,
    };

    private static string Clean(string value)
        => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static ImpactResult Error(string? changeId, string message)
        => new("invalid", changeId, null, [], null, [$"ERROR: {message}"], false);
}

public sealed record ImpactDocument(IReadOnlyList<ImpactFinding> Findings, bool Truncated);
