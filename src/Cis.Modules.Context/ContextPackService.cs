using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Context;

public sealed class ContextPackService
{
    private const string ContextRoot = ".cis/local/context";
    private const int MaximumSourceFileBytes = 2_000_000;
    private const int MaximumReportedEvidencePerSource = 25;

    private static readonly (string Name, Regex Pattern)[] SensitiveContentPatterns =
    [
        ("private-key", new Regex("-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("aws-access-key", new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("github-token", new Regex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("jwt", new Regex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("literal-credential", new Regex(@"(?im)\b(?:password|passwd|client[_-]?secret|api[_-]?key|access[_-]?token)\b\s*[:=]\s*[""']?[A-Za-z0-9+/=_-]{16,}", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
    ];

    private static readonly string[] PackEdgeTypes =
    [
        "declares",
        "references",
        "governed-by",
        "describes",
        "owns",
        "belongs-to",
        "implemented-by",
        "consumed-by",
        "produced-by",
        "depends-on",
        "calls",
        "publishes",
        "subscribes-to",
        "configured-by",
        "deployed-by",
        "verified-by",
        "generated-from",
        "supersedes",
    ];

    private readonly ICisGraphQueryService _graphQueries;

    public ContextPackService(ICisGraphQueryService graphQueries)
    {
        _graphQueries = graphQueries;
    }

    public ContextPackResult Create(ContextPackRequest request)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return validation;
        }

        var requestedRoots = new[]
            {
                new ContextPackRoot(request.RepositoryPath, request.Id, request.Kind),
            }
            .Concat(request.AdditionalRoots ?? [])
            .ToArray();
        if (requestedRoots.Length > 20)
        {
            return Invalid(request, "A context pack supports at most 20 roots.");
        }

        var rootQueries = requestedRoots
            .Select((root, index) => new RootQuery(index, root, QueryContext(root, request)))
            .ToArray();
        var failedQuery = rootQueries.FirstOrDefault(item => item.Query.ExitCode != 0);
        if (failedQuery is not null)
        {
            return FromQueries(
                rootQueries,
                failedQuery.Query.Status,
                request,
                [],
                [],
                applied: false);
        }

        var missingRoots = rootQueries.Where(item => item.Query.Nodes.Count == 0).ToArray();
        if (missingRoots.Length > 0)
        {
            if (rootQueries.Length == 1)
            {
                return FromQueries(rootQueries, "no-match", request, [], [], applied: false);
            }

            return Invalid(
                request,
                "Every multi-root entry must resolve exactly one node. Missing: " +
                string.Join(", ", missingRoots.Select(item => $"{item.Root.RepositoryPath}#{item.Root.Id}")));
        }

        var resolvedRoots = rootQueries.Select(item => new ContextPackResolvedRoot(
            Path.GetFullPath(item.Query.RepositoryPath!),
            item.Query.Build!.RepositoryId,
            item.Root.Id,
            item.Query.Nodes[0],
            item.Query.Build,
            item.Query.Freshness)).ToArray();
        var repositoryPath = resolvedRoots[0].RepositoryPath;
        var startNode = resolvedRoots[0].StartNode;
        var candidates = rootQueries
            .SelectMany(item => BuildCandidates(
                Path.GetFullPath(item.Query.RepositoryPath!),
                item.Query.Build!.RepositoryId,
                item.Index,
                item.Query.Nodes[0],
                item.Query.Nodes,
                item.Query.Traversals))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.RootOrder)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        var sources = new List<ContextPackSource>();
        var omissions = new List<ContextPackOmission>();
        var remaining = request.MaxCharacters;
        foreach (var candidate in candidates)
        {
            if (remaining == 0)
            {
                omissions.Add(new ContextPackOmission(
                    candidate.RepositoryId,
                    candidate.RepositoryPath,
                    candidate.Path,
                    "character-budget-exhausted",
                    TryGetLength(candidate.AbsolutePath)));
                continue;
            }

            if (TryFindSensitivePath(candidate.Path, out var sensitivePathRule))
            {
                omissions.Add(new ContextPackOmission(
                    candidate.RepositoryId,
                    candidate.RepositoryPath,
                    candidate.Path,
                    $"sensitive-path:{sensitivePathRule}",
                    TryGetLength(candidate.AbsolutePath)));
                continue;
            }

            var fileLength = TryGetLength(candidate.AbsolutePath);
            if (fileLength is > MaximumSourceFileBytes)
            {
                omissions.Add(new ContextPackOmission(
                    candidate.RepositoryId,
                    candidate.RepositoryPath,
                    candidate.Path,
                    $"source-too-large:{MaximumSourceFileBytes}",
                    fileLength));
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(candidate.AbsolutePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                omissions.Add(new ContextPackOmission(
                    candidate.RepositoryId,
                    candidate.RepositoryPath,
                    candidate.Path,
                    $"unreadable: {exception.Message}",
                    TryGetLength(candidate.AbsolutePath)));
                continue;
            }

            content = NormalizeLineEndings(content);
            if (content.Contains('\0'))
            {
                omissions.Add(new ContextPackOmission(
                    candidate.RepositoryId,
                    candidate.RepositoryPath,
                    candidate.Path,
                    "binary-content",
                    content.Length));
                continue;
            }

            if (TryFindSensitiveContent(content, out var sensitiveContentRule))
            {
                omissions.Add(new ContextPackOmission(
                    candidate.RepositoryId,
                    candidate.RepositoryPath,
                    candidate.Path,
                    $"sensitive-content:{sensitiveContentRule}",
                    content.Length));
                continue;
            }

            var selected = CreateExcerpts(content, candidate.Evidence);
            var selectedCharacters = selected.Sum(excerpt => excerpt.Content.Length);
            var excerpts = new List<ContextPackExcerpt>();
            var perSourceBudget = Math.Max(1_000, request.MaxCharacters / 5);
            var sourceRemaining = Math.Min(remaining, perSourceBudget);
            foreach (var excerpt in selected)
            {
                if (remaining == 0 || sourceRemaining == 0)
                {
                    break;
                }

                var includedCharacters = Math.Min(excerpt.Content.Length, Math.Min(remaining, sourceRemaining));
                excerpts.Add(new ContextPackExcerpt(
                    excerpt.StartLine,
                    excerpt.EndLine,
                    excerpt.Locators,
                    excerpt.Content.Length,
                    includedCharacters,
                    includedCharacters < excerpt.Content.Length,
                    excerpt.Content[..includedCharacters]));
                remaining -= includedCharacters;
                sourceRemaining -= includedCharacters;
            }

            var totalIncludedCharacters = excerpts.Sum(excerpt => excerpt.IncludedCharacters);
            var truncated = totalIncludedCharacters < selectedCharacters;
            var orderedEvidence = candidate.Evidence
                .OrderBy(evidence => evidence.LocatorKind, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.LocatorValue, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.EdgeType, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.Method, StringComparer.Ordinal)
                .ToArray();
            var reportedEvidence = orderedEvidence.Take(MaximumReportedEvidencePerSource).ToArray();
            sources.Add(new ContextPackSource(
                candidate.RepositoryId,
                candidate.RepositoryPath,
                candidate.Path,
                candidate.NodeKeys.Order(StringComparer.Ordinal).ToArray(),
                candidate.Reasons.Order(StringComparer.Ordinal).ToArray(),
                candidate.FactClass,
                content.Length,
                selectedCharacters,
                totalIncludedCharacters,
                EstimateTokens(totalIncludedCharacters),
                truncated,
                orderedEvidence.Length,
                orderedEvidence.Length - reportedEvidence.Length,
                reportedEvidence,
                excerpts));
        }

        var outputResolution = ResolveOutputPath(repositoryPath, request, resolvedRoots);
        if (!outputResolution.IsValid)
        {
            return Invalid(request, outputResolution.Error!);
        }

        var absoluteOutputPath = outputResolution.AbsolutePath!;
        var relativeOutputPath = ToRepositoryPath(repositoryPath, absoluteOutputPath);
        var preliminary = FromQueries(
            rootQueries,
            "created",
            request,
            sources,
            omissions,
            applied: true,
            outputPath: relativeOutputPath);
        var markdown = RenderMarkdown(preliminary);
        if (File.Exists(absoluteOutputPath))
        {
            var existing = File.ReadAllText(absoluteOutputPath);
            if (string.Equals(existing, markdown, StringComparison.Ordinal))
            {
                return preliminary with { Status = "unchanged", Applied = false };
            }

            if (!request.Force)
            {
                return preliminary with { Status = "collision", Applied = false };
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath)!);
        WriteAtomic(absoluteOutputPath, markdown);
        return preliminary;
    }

    private CisGraphQueryResult QueryContext(ContextPackRoot root, ContextPackRequest request)
    {
        var first = _graphQueries.Related(
            root.RepositoryPath,
            new CisGraphRelatedRequest(
                root.Id,
                root.Kind,
                PackEdgeTypes,
                "both",
                Depth: 1,
                request.Limit,
                request.IncludeProposed));
        if (first.ExitCode != 0 || first.Nodes.Count == 0 || request.Depth == 1)
        {
            return first;
        }

        var nodes = first.Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
        var nodeDepths = first.Nodes.ToDictionary(
            node => node.Key,
            node => node.Key == first.Nodes[0].Key ? 0 : 1,
            StringComparer.Ordinal);
        var traversals = first.Traversals.ToDictionary(
            traversal => traversal.Edge.Key,
            StringComparer.Ordinal);
        var diagnostics = first.Diagnostics.ToDictionary(DiagnosticKey, StringComparer.Ordinal);
        var frontier = first.Nodes.Skip(1)
            .Where(IsExpandable)
            .OrderBy(node => node.Key, StringComparer.Ordinal)
            .ToArray();
        var expanded = new HashSet<string>(StringComparer.Ordinal)
        {
            first.Nodes[0].Key,
        };
        var truncated = first.Truncated;
        const int maximumExpansions = 25;
        var expansionCount = 0;
        for (var depth = 2; depth <= request.Depth && frontier.Length > 0; depth++)
        {
            var nextFrontier = new List<CisGraphNode>();
            foreach (var current in frontier)
            {
                if (!expanded.Add(current.Key))
                {
                    continue;
                }

                if (expansionCount == maximumExpansions)
                {
                    truncated = true;
                    break;
                }

                expansionCount++;
                var related = _graphQueries.Related(
                    root.RepositoryPath,
                    new CisGraphRelatedRequest(
                        current.Key,
                        current.Kind,
                        PackEdgeTypes,
                        "both",
                        Depth: 1,
                        request.Limit,
                        request.IncludeProposed));
                truncated |= related.Truncated;
                foreach (var diagnostic in related.Diagnostics)
                {
                    diagnostics.TryAdd(DiagnosticKey(diagnostic), diagnostic);
                }

                if (related.ExitCode != 0)
                {
                    continue;
                }

                foreach (var node in related.Nodes.Skip(1))
                {
                    if (nodes.ContainsKey(node.Key))
                    {
                        continue;
                    }

                    if (nodes.Count == request.Limit)
                    {
                        truncated = true;
                        continue;
                    }

                    nodes.Add(node.Key, node);
                    nodeDepths.Add(node.Key, depth);
                    if (IsExpandable(node))
                    {
                        nextFrontier.Add(node);
                    }
                }

                foreach (var traversal in related.Traversals)
                {
                    if (nodes.ContainsKey(traversal.Edge.From)
                        && nodes.ContainsKey(traversal.Edge.To))
                    {
                        traversals.TryAdd(
                            traversal.Edge.Key,
                            traversal with { Depth = depth });
                    }
                }
            }

            frontier = nextFrontier
                .OrderBy(node => node.Key, StringComparer.Ordinal)
                .ToArray();
        }

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in first.Query)
        {
            query[pair.Key] = pair.Value;
        }

        query["depth"] = request.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        query["edges"] = string.Join(',', PackEdgeTypes);
        query["operation"] = "context-pack";
        return first with
        {
            Query = query,
            Nodes = nodes.Values
                .OrderBy(node => nodeDepths[node.Key])
                .ThenBy(node => node.Key, StringComparer.Ordinal)
                .ToArray(),
            Traversals = traversals.Values
                .OrderBy(traversal => traversal.Depth)
                .ThenBy(traversal => traversal.Edge.Key, StringComparer.Ordinal)
                .ToArray(),
            Truncated = truncated,
            Diagnostics = diagnostics.Values
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    internal static string RenderMarkdown(ContextPackResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CIS context pack");
        builder.AppendLine();
        builder.AppendLine($"- Purpose: {result.Purpose}");
        builder.AppendLine($"- Verification requested: {result.Verification}");
        builder.AppendLine($"- Graph build: {result.Build?.Id ?? "unknown"}");
        builder.AppendLine($"- Repository baseline: {result.Build?.Head ?? "uncommitted"}; dirty={result.Build?.Dirty.ToString().ToLowerInvariant() ?? "unknown"}");
        builder.AppendLine($"- Freshness: {result.Freshness}");
        builder.AppendLine($"- Start node: {result.StartNode?.Key ?? "unresolved"}");
        builder.AppendLine($"- Roots: {result.Roots.Count}");
        builder.AppendLine($"- Bounds: depth={result.Depth}; nodes={result.Limit}; characters={result.MaxCharacters}");
        builder.AppendLine($"- Estimated included tokens: {result.EstimatedTokens}");
        builder.AppendLine($"- Truncation: graph={result.GraphTruncated.ToString().ToLowerInvariant()}; content={result.ContentTruncated.ToString().ToLowerInvariant()}");
        builder.AppendLine();
        builder.AppendLine("## Roots and repository baselines");
        builder.AppendLine();
        builder.AppendLine("| Repository | Root | Graph build | Freshness | Path |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var root in result.Roots)
        {
            builder.AppendLine($"| `{EscapeTable(root.RepositoryId)}` | `{EscapeTable(root.StartNode.Key)}` | `{EscapeTable(root.Build.Id)}` | {root.Freshness} | `{EscapeTable(root.RepositoryPath)}` |");
        }

        builder.AppendLine();
        builder.AppendLine("## Selection manifest");
        builder.AppendLine();
        if (result.Sources.Count == 0)
        {
            builder.AppendLine("No readable source locations were selected.");
        }
        else
        {
            builder.AppendLine("| Source | Fact class | Selected | Included | Est. tokens | Reason |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");
            foreach (var source in result.Sources)
            {
                builder.AppendLine($"| `{EscapeTable(source.RepositoryId)}:{EscapeTable(source.Path)}` | {source.FactClass} | {source.SelectedCharacters}/{source.TotalCharacters} | {source.IncludedCharacters}/{source.SelectedCharacters} | {source.EstimatedTokens} | {EscapeTable(string.Join(", ", source.Reasons))} |");
            }
        }

        if (result.Omissions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Omissions");
            builder.AppendLine();
            foreach (var omission in result.Omissions)
            {
                builder.AppendLine($"- `{omission.RepositoryId ?? "unknown"}:{omission.Path}`: {omission.Reason}");
            }
        }

        if (result.Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Diagnostics and uncertainty");
            builder.AppendLine();
            foreach (var diagnostic in result.Diagnostics)
            {
                builder.AppendLine($"- `{diagnostic.Code}` ({diagnostic.Severity}): {diagnostic.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Selected sources");
        foreach (var source in result.Sources)
        {
            builder.AppendLine();
            builder.AppendLine($"### `{source.RepositoryId}:{source.Path}`");
            builder.AppendLine();
            builder.AppendLine($"Repository: `{source.RepositoryPath}`.");
            builder.AppendLine($"Fact class: {source.FactClass}. Selection: {string.Join(", ", source.Reasons)}.");
            if (source.Truncated)
            {
                builder.AppendLine($"Selected excerpts are truncated at {source.IncludedCharacters} of {source.SelectedCharacters} characters; source size is {source.TotalCharacters} characters.");
            }

            builder.AppendLine($"Estimated included tokens: {source.EstimatedTokens}.");
            builder.AppendLine();
            builder.AppendLine($"Evidence: {source.TotalEvidence} fact(s); {source.Evidence.Count} shown; {source.OmittedEvidence} compacted.");
            foreach (var evidence in source.Evidence)
            {
                builder.Append("- ");
                if (evidence.NodeKey is not null)
                {
                    builder.Append($"node=`{evidence.NodeKey}`; ");
                }

                if (evidence.EdgeType is not null)
                {
                    builder.Append($"edge={evidence.EdgeType}; state={evidence.RelationshipState}; ");
                }

                if (evidence.Confidence is not null)
                {
                    builder.Append($"confidence={evidence.Confidence}; ");
                }

                builder.Append($"locator=`{evidence.LocatorKind}:{evidence.LocatorValue}`");
                if (evidence.Method is not null)
                {
                    builder.Append($"; method={evidence.Method}");
                }

                builder.AppendLine();
            }

            foreach (var excerpt in source.Excerpts)
            {
                builder.AppendLine();
                builder.AppendLine($"#### Lines {excerpt.StartLine}–{excerpt.EndLine}");
                builder.AppendLine();
                builder.AppendLine($"Locators: {string.Join(", ", excerpt.Locators.Select(locator => $"`{locator}`"))}.");
                if (excerpt.Truncated)
                {
                    builder.AppendLine($"Excerpt truncated at {excerpt.IncludedCharacters} of {excerpt.TotalCharacters} characters.");
                }

                builder.AppendLine();
                builder.AppendLine("``````text");
                builder.Append(excerpt.Content);
                if (!excerpt.Content.EndsWith('\n'))
                {
                    builder.AppendLine();
                }

                builder.AppendLine("``````");
            }
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static ContextPackResult? Validate(ContextPackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return Invalid(request, "A start node ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Purpose))
        {
            return Invalid(request, "A non-empty pack purpose is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Verification))
        {
            return Invalid(request, "A non-empty verification request is required.");
        }

        if (request.Depth is < 1 or > 10)
        {
            return Invalid(request, "Depth must be between 1 and 10.");
        }

        if (request.Limit is < 1 or > 1000)
        {
            return Invalid(request, "Limit must be between 1 and 1000.");
        }

        if (request.MaxCharacters is < 1000 or > 1_000_000)
        {
            return Invalid(request, "Character budget must be between 1000 and 1000000.");
        }

        if ((request.AdditionalRoots ?? []).Any(root =>
            string.IsNullOrWhiteSpace(root.RepositoryPath) || string.IsNullOrWhiteSpace(root.Id)))
        {
            return Invalid(request, "Every additional root requires a repository path and node ID.");
        }

        var duplicateRoot = new[] { new ContextPackRoot(request.RepositoryPath, request.Id, request.Kind) }
            .Concat(request.AdditionalRoots ?? [])
            .GroupBy(root => $"{root.RepositoryPath.Replace('\\', '/').TrimEnd('/')}\u001f{root.Id}\u001f{root.Kind}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRoot is not null)
        {
            return Invalid(request, "Context roots must be unique.");
        }

        return null;
    }

    private static IReadOnlyList<SourceCandidate> BuildCandidates(
        string repositoryPath,
        string repositoryId,
        int rootOrder,
        CisGraphNode startNode,
        IReadOnlyList<CisGraphNode> nodes,
        IReadOnlyList<CisGraphTraversal> traversals)
    {
        var candidates = new Dictionary<string, MutableCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var nodeTraversals = traversals
                .Where(traversal => traversal.Edge.From == node.Key || traversal.Edge.To == node.Key)
                .ToArray();
            var nodeEdges = nodeTraversals.Select(traversal => traversal.Edge).ToArray();
            var observedPaths = nodeEdges
                .SelectMany(edge => edge.Observations)
                .Select(observation => observation.Path)
                .ToArray();
            var useNodeLocations = node.Key == startNode.Key
                || node.Kind is "repository" or "component" or "document" or "workflow"
                || observedPaths.Length == 0;
            var paths = (useNodeLocations
                    ? node.Locations.Select(location => location.Path)
                        .Concat(node.Provenance.Select(evidence => evidence.Path))
                    : observedPaths)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (!TryResolveSource(repositoryPath, path, out var relativePath, out var absolutePath))
                {
                    continue;
                }

                if (!candidates.TryGetValue(relativePath, out var candidate))
                {
                    candidate = new MutableCandidate(relativePath, absolutePath);
                    candidates.Add(relativePath, candidate);
                }

                candidate.NodeKeys.Add(node.Key);
                foreach (var location in node.Locations.Where(location => PathEquals(location.Path, relativePath)))
                {
                    candidate.AddEvidence(new ContextPackEvidence(
                        node.Key,
                        null,
                        null,
                        null,
                        relativePath,
                        location.LocatorKind,
                        location.LocatorValue,
                        null));
                }

                foreach (var provenance in node.Provenance.Where(evidence => PathEquals(evidence.Path, relativePath)))
                {
                    candidate.AddEvidence(new ContextPackEvidence(
                        node.Key,
                        null,
                        null,
                        provenance.Confidence,
                        relativePath,
                        provenance.LocatorKind,
                        provenance.LocatorValue,
                        provenance.Method));
                }

                foreach (var edge in nodeEdges)
                {
                    foreach (var observation in edge.Observations.Where(evidence => PathEquals(evidence.Path, relativePath)))
                    {
                        candidate.AddEvidence(new ContextPackEvidence(
                            node.Key,
                            edge.Type,
                            edge.State,
                            observation.Confidence,
                            relativePath,
                            observation.LocatorKind,
                            observation.LocatorValue,
                            observation.Method));
                    }
                }

                candidate.Reasons.Add(node.Key == startNode.Key
                    ? "start-node evidence"
                    : $"related {node.Kind}/{node.Subtype}");
                foreach (var edge in nodeEdges)
                {
                    candidate.Reasons.Add($"{edge.State} {edge.Type}");
                }

                if (node.Authority == "proposal"
                    || nodeEdges.Any(edge => edge.State is "proposed" or "possibly-stale"))
                {
                    candidate.Interpreted = true;
                }

                var depth = node.Key == startNode.Key
                    ? 0
                    : nodeTraversals.Select(traversal => traversal.Depth).DefaultIfEmpty(1).Min();
                candidate.Score = Math.Min(candidate.Score, Score(node, startNode, depth));
            }
        }

        return candidates.Values
            .Where(candidate => File.Exists(candidate.AbsolutePath))
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select((candidate, rank) => new SourceCandidate(
                repositoryId,
                repositoryPath,
                rootOrder,
                rank,
                candidate.Path,
                candidate.AbsolutePath,
                candidate.NodeKeys,
                candidate.Reasons,
                candidate.Interpreted ? "interpreted" : "strict",
                candidate.Evidence.Values.ToArray()))
            .ToArray();
    }

    private static int Score(CisGraphNode node, CisGraphNode startNode, int depth)
    {
        if (node.Key == startNode.Key)
        {
            return 0;
        }

        var kindScore = node.Kind switch
        {
            "test" or "source-file" or "symbol" => 1,
            "document" => 2,
            "reference-item" or "requirement" or "decision" => 2,
            "component" or "workflow" => 3,
            "dependency" => 4,
            _ => 5,
        };
        return checked((depth * 10) + kindScore);
    }

    private static bool IsExpandable(CisGraphNode node)
        => node.Kind is "component" or "workflow";

    private static string DiagnosticKey(CisGraphDiagnostic diagnostic)
        => $"{diagnostic.Code}\u001f{diagnostic.Severity}\u001f{diagnostic.Message}";

    private static bool PathEquals(string first, string second)
        => string.Equals(
            first.Replace('\\', '/').TrimStart('/'),
            second.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveSource(
        string repositoryPath,
        string path,
        out string relativePath,
        out string absolutePath)
    {
        relativePath = string.Empty;
        absolutePath = string.Empty;
        if (Path.IsPathRooted(path))
        {
            return false;
        }

        absolutePath = Path.GetFullPath(Path.Combine(
            repositoryPath,
            path.Replace('/', Path.DirectorySeparatorChar)));
        var repositoryPrefix = repositoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        relativePath = ToRepositoryPath(repositoryPath, absolutePath);
        return !relativePath.StartsWith(".cis/local/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RawExcerpt> CreateExcerpts(
        string content,
        IReadOnlyList<ContextPackEvidence> evidence)
    {
        var lines = content.Split('\n');
        var ranges = new List<ExcerptRange>();
        foreach (var locator in evidence
            .Select(item => (Kind: item.LocatorKind.ToLowerInvariant(), item.LocatorValue))
            .Distinct())
        {
            switch (locator.Kind)
            {
                case "line" when int.TryParse(
                    locator.LocatorValue,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var line):
                    ranges.Add(new ExcerptRange(
                        Math.Max(1, line - 12),
                        Math.Min(lines.Length, line + 20),
                        [$"line:{locator.LocatorValue}"]));
                    break;
                case "heading":
                    if (TryFindHeadingRange(lines, locator.LocatorValue, out var headingStart, out var headingEnd))
                    {
                        ranges.Add(new ExcerptRange(
                            headingStart,
                            headingEnd,
                            [$"heading:{locator.LocatorValue}"]));
                    }

                    break;
                case "table-row":
                    AddTableRowRanges(lines, locator.LocatorValue, ranges);
                    break;
                case "declaration":
                    AddMatchingLineRanges(lines, locator.LocatorValue, 3, ranges, "declaration");
                    break;
            }
        }

        if (ranges.Count == 0)
        {
            return [new RawExcerpt(
                1,
                Math.Max(1, lines.Length),
                ["file:whole"],
                content)];
        }

        var merged = new List<ExcerptRange>();
        foreach (var range in ranges
            .OrderBy(range => range.StartLine)
            .ThenBy(range => range.EndLine))
        {
            if (merged.Count == 0 || range.StartLine > merged[^1].EndLine + 2)
            {
                merged.Add(range);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = previous with
            {
                EndLine = Math.Max(previous.EndLine, range.EndLine),
                Locators = previous.Locators
                    .Concat(range.Locators)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
            };
        }

        return merged.Select(range => new RawExcerpt(
                range.StartLine,
                range.EndLine,
                range.Locators,
                string.Join('\n', lines[(range.StartLine - 1)..range.EndLine])))
            .ToArray();
    }

    private static bool TryFindHeadingRange(
        IReadOnlyList<string> lines,
        string locator,
        out int startLine,
        out int endLine)
    {
        startLine = 0;
        endLine = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            if (!TryReadHeading(lines[index], out var level, out var text)
                || !HeadingMatches(text, locator))
            {
                continue;
            }

            startLine = index + 1;
            endLine = lines.Count;
            for (var following = index + 1; following < lines.Count; following++)
            {
                if (TryReadHeading(lines[following], out var followingLevel, out _)
                    && followingLevel <= level)
                {
                    endLine = following;
                    break;
                }
            }

            return true;
        }

        return false;
    }

    private static bool TryReadHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;
        var trimmed = line.TrimStart();
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
        {
            level++;
        }

        if (level == 0 || level >= trimmed.Length || !char.IsWhiteSpace(trimmed[level]))
        {
            return false;
        }

        text = trimmed[(level + 1)..].Trim().TrimEnd('#').Trim();
        return text.Length > 0;
    }

    private static bool HeadingMatches(string heading, string locator)
    {
        static string NormalizeHeading(string value)
            => new(value.ToLowerInvariant()
                .Where(character => char.IsLetterOrDigit(character))
                .ToArray());

        var normalizedHeading = NormalizeHeading(heading);
        var normalizedLocator = NormalizeHeading(locator);
        return normalizedLocator.Length > 0
            && (normalizedHeading.Contains(normalizedLocator, StringComparison.Ordinal)
                || normalizedLocator.Contains(normalizedHeading, StringComparison.Ordinal));
    }

    private static void AddTableRowRanges(
        IReadOnlyList<string> lines,
        string locator,
        ICollection<ExcerptRange> ranges)
    {
        var matches = Enumerable.Range(0, lines.Count)
            .Where(index => lines[index].Contains(locator, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();
        foreach (var index in matches)
        {
            var tableStart = index;
            while (tableStart > 0 && lines[tableStart - 1].TrimStart().StartsWith('|'))
            {
                tableStart--;
            }

            if (tableStart < index)
            {
                ranges.Add(new ExcerptRange(
                    tableStart + 1,
                    Math.Min(index + 1, tableStart + 2),
                    [$"table-header:{locator}"]));
            }

            ranges.Add(new ExcerptRange(
                index + 1,
                Math.Min(lines.Count, index + 2),
                [$"table-row:{locator}"]));
        }
    }

    private static void AddMatchingLineRanges(
        IReadOnlyList<string> lines,
        string value,
        int contextLines,
        ICollection<ExcerptRange> ranges,
        string locatorKind)
    {
        foreach (var index in Enumerable.Range(0, lines.Count)
            .Where(index => lines[index].Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(10))
        {
            ranges.Add(new ExcerptRange(
                Math.Max(1, index + 1 - contextLines),
                Math.Min(lines.Count, index + 1 + contextLines),
                [$"{locatorKind}:{value}"]));
        }
    }

    private static bool TryFindSensitivePath(string path, out string rule)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized).ToLowerInvariant();
        if (fileName.StartsWith(".env", StringComparison.Ordinal)
            && fileName is not ".env.example" and not ".env.sample" and not ".env.template")
        {
            rule = "environment-file";
            return true;
        }

        if (fileName is "id_rsa" or "id_ed25519" or "secrets.json" or ".npmrc" or ".pypirc"
            || normalized.Contains("/.aws/credentials", StringComparison.OrdinalIgnoreCase))
        {
            rule = "credential-file";
            return true;
        }

        if (Path.GetExtension(fileName) is ".pfx" or ".p12" or ".key" or ".pem" or ".jks" or ".keystore" or ".kdbx")
        {
            rule = "key-material";
            return true;
        }

        rule = string.Empty;
        return false;
    }

    private static bool TryFindSensitiveContent(string content, out string rule)
    {
        foreach (var pattern in SensitiveContentPatterns)
        {
            if (pattern.Pattern.IsMatch(content))
            {
                rule = pattern.Name;
                return true;
            }
        }

        rule = string.Empty;
        return false;
    }

    private static string NormalizeLineEndings(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static int EstimateTokens(int characters)
        => (characters + 3) / 4;

    private static OutputResolution ResolveOutputPath(
        string repositoryPath,
        ContextPackRequest request,
        IReadOnlyList<ContextPackResolvedRoot> roots)
    {
        var startNode = roots[0].StartNode;
        string relativePath;
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            var identity = string.Join('\n', roots.Select(root =>
                    $"root:{root.RepositoryId}:{root.Build.Id}:{root.StartNode.Key}")) +
                $"\npurpose:{request.Purpose}" +
                $"\nverification:{request.Verification}" +
                $"\ndepth:{request.Depth}" +
                $"\nlimit:{request.Limit}" +
                $"\ncharacters:{request.MaxCharacters}" +
                $"\nproposed:{request.IncludeProposed}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
                .ToLowerInvariant()[..12];
            var rootSuffix = roots.Count == 1 ? string.Empty : $"-plus-{roots.Count - 1}";
            relativePath = $"{ContextRoot}/{Slug(startNode.Label)}{rootSuffix}-{hash}.md";
        }
        else
        {
            if (Path.IsPathRooted(request.OutputPath))
            {
                return new OutputResolution(false, null, "Output must be a repository-relative path beneath .cis/local/context/.");
            }

            relativePath = request.OutputPath.Replace('\\', '/').TrimStart('/');
            if (!relativePath.Contains('/'))
            {
                relativePath = $"{ContextRoot}/{relativePath}";
            }
        }

        if (!relativePath.StartsWith(ContextRoot + "/", StringComparison.OrdinalIgnoreCase)
            || !relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return new OutputResolution(false, null, "Output must be a Markdown file beneath .cis/local/context/.");
        }

        var absolutePath = Path.GetFullPath(Path.Combine(
            repositoryPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var contextPrefix = Path.GetFullPath(Path.Combine(repositoryPath, ContextRoot.Replace('/', Path.DirectorySeparatorChar)))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return absolutePath.StartsWith(contextPrefix, StringComparison.OrdinalIgnoreCase)
            ? new OutputResolution(true, absolutePath, null)
            : new OutputResolution(false, null, "Output escapes .cis/local/context/.");
    }

    private static ContextPackResult FromQueries(
        IReadOnlyList<RootQuery> queries,
        string status,
        ContextPackRequest request,
        IReadOnlyList<ContextPackSource> sources,
        IReadOnlyList<ContextPackOmission> omissions,
        bool applied,
        string? outputPath = null)
    {
        var primary = queries[0].Query;
        var roots = queries
            .Where(item => item.Query.Nodes.Count > 0 && item.Query.Build is not null && item.Query.RepositoryPath is not null)
            .Select(item => new ContextPackResolvedRoot(
                Path.GetFullPath(item.Query.RepositoryPath!),
                item.Query.Build!.RepositoryId,
                item.Root.Id,
                item.Query.Nodes[0],
                item.Query.Build,
                item.Query.Freshness))
            .ToArray();
        var diagnostics = queries
            .SelectMany(item => item.Query.Diagnostics.Select(diagnostic =>
            {
                if (queries.Count == 1)
                {
                    return diagnostic;
                }

                var repositoryId = item.Query.Build?.RepositoryId ?? item.Root.RepositoryPath;
                return diagnostic with
                {
                    Message = $"[{repositoryId}] {diagnostic.Message}",
                    Evidence = diagnostic.Evidence
                        .Select(evidence => $"{repositoryId}:{evidence}")
                        .ToArray(),
                };
            }))
            .DistinctBy(DiagnosticKey)
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
        var freshness = queries.Any(item => item.Query.Freshness == "stale")
            ? "stale"
            : queries.All(item => item.Query.Freshness == "fresh")
                ? "fresh"
                : primary.Freshness;
        return new ContextPackResult(
            status,
            primary.RepositoryPath,
            outputPath,
            primary.Build,
            freshness,
            request.Purpose,
            request.Verification,
            roots.FirstOrDefault()?.StartNode,
            roots,
            request.Depth,
            request.Limit,
            request.MaxCharacters,
            EstimateTokens(sources.Sum(source => source.IncludedCharacters)),
            sources,
            omissions,
            queries.Any(item => item.Query.Truncated),
            sources.Any(source => source.Truncated) || omissions.Count > 0,
            diagnostics,
            applied,
            queries.All(item => item.Query.RepositoryConfigurationValid),
            queries.All(item => item.Query.GraphAvailable),
            queries.All(item => item.Query.QueryValid));
    }

    private static ContextPackResult Invalid(ContextPackRequest request, string message)
        => new(
            "invalid-request",
            null,
            null,
            null,
            "unknown",
            request.Purpose,
            request.Verification,
            null,
            [],
            request.Depth,
            request.Limit,
            request.MaxCharacters,
            0,
            [],
            [],
            false,
            false,
            [new CisGraphDiagnostic("CIS-CONTEXT-PACK-001", "error", message, [])],
            false,
            RepositoryConfigurationValid: true,
            GraphAvailable: true,
            RequestValid: false);

    private static int? TryGetLength(string path)
    {
        try
        {
            return checked((int)Math.Min(new FileInfo(path).Length, int.MaxValue));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Slug(string value)
    {
        var slug = new string(value.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length > 48)
        {
            slug = slug[..48].TrimEnd('-');
        }

        return slug.Length == 0 ? "context" : slug;
    }

    private static string EscapeTable(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string ToRepositoryPath(string repositoryPath, string path)
        => Path.GetRelativePath(repositoryPath, path).Replace('\\', '/');

    private static void WriteAtomic(string path, string content)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class MutableCandidate(string path, string absolutePath)
    {
        public string Path { get; } = path;

        public string AbsolutePath { get; } = absolutePath;

        public HashSet<string> NodeKeys { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Reasons { get; } = new(StringComparer.Ordinal);

        public bool Interpreted { get; set; }

        public int Score { get; set; } = int.MaxValue;

        public Dictionary<string, ContextPackEvidence> Evidence { get; } = new(StringComparer.Ordinal);

        public void AddEvidence(ContextPackEvidence evidence)
        {
            // Traversal exposes the same observation from both edge endpoints. Keep one
            // representative fact in the context pack while the graph retains both nodes.
            var key = string.Join(
                '\u001f',
                evidence.EdgeType,
                evidence.RelationshipState,
                evidence.Confidence,
                evidence.Path,
                evidence.LocatorKind,
                evidence.LocatorValue,
                evidence.Method);
            Evidence.TryAdd(key, evidence);
        }
    }

    private sealed record SourceCandidate(
        string RepositoryId,
        string RepositoryPath,
        int RootOrder,
        int Rank,
        string Path,
        string AbsolutePath,
        IReadOnlySet<string> NodeKeys,
        IReadOnlySet<string> Reasons,
        string FactClass,
        IReadOnlyList<ContextPackEvidence> Evidence);

    private sealed record RawExcerpt(
        int StartLine,
        int EndLine,
        IReadOnlyList<string> Locators,
        string Content);

    private sealed record ExcerptRange(
        int StartLine,
        int EndLine,
        IReadOnlyList<string> Locators);

    private sealed record OutputResolution(bool IsValid, string? AbsolutePath, string? Error);

    private sealed record RootQuery(
        int Index,
        ContextPackRoot Root,
        CisGraphQueryResult Query);
}
