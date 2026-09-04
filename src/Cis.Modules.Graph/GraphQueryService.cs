using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed class GraphQueryService : ICisGraphQueryService
{
    private const string GraphRelativePath = SqliteGraphStore.DatabaseRelativePath;

    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly SqliteGraphStore _store;
    private readonly IReadOnlyList<string> _expectedExtractors;

    public GraphQueryService(ICisRepositoryContextResolver repositoryContextResolver, SqliteGraphStore? store = null,
        IEnumerable<ICisGraphAugmenter>? augmenters = null)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _store = store ?? new SqliteGraphStore();
        _expectedExtractors = GraphBuilder.ComposeExtractors(augmenters);
    }

    public CisGraphQueryResult Find(string repositoryPath, CisGraphFindRequest request)
    {
        var query = QueryProperties(
            ("operation", "find"),
            ("id", request.Id),
            ("kind", request.Kind),
            ("subtype", request.Subtype),
            ("facet", request.Facet),
            ("text", request.Text),
            ("limit", request.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (request.Limit is < 1 or > 1000)
        {
            return InvalidQuery(repositoryPath, query, "Limit must be between 1 and 1000.");
        }

        if (new[] { request.Id, request.Kind, request.Subtype, request.Facet, request.Text }
            .All(string.IsNullOrWhiteSpace))
        {
            return InvalidQuery(repositoryPath, query, "At least one find filter is required.");
        }

        var load = Load(repositoryPath, query);
        if (load.Result is not null)
        {
            return load.Result;
        }

        var matches = _store.FindNodes(load.RepositoryPath!, request.Id, request.Kind, request.Subtype,
            request.Facet, request.Text, request.Limit + 1).ToArray();
        var truncated = matches.Length > request.Limit;
        var selected = matches.Take(request.Limit).ToArray();
        return Success(
            load,
            selected.Length == 0 ? "no-match" : "matched",
            query,
            selected,
            [],
            truncated);
    }

    public CisGraphQueryResult Related(string repositoryPath, CisGraphRelatedRequest request)
    {
        var query = QueryProperties(
            ("operation", "related"),
            ("id", request.Id),
            ("kind", request.Kind),
            ("edges", string.Join(',', request.EdgeTypes)),
            ("direction", request.Direction),
            ("depth", request.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("limit", request.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("includeProposed", request.IncludeProposed.ToString().ToLowerInvariant()));
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return InvalidQuery(repositoryPath, query, "A start node ID is required.");
        }

        if (request.Depth is < 1 or > 10)
        {
            return InvalidQuery(repositoryPath, query, "Depth must be between 1 and 10.");
        }

        if (request.Limit is < 1 or > 1000)
        {
            return InvalidQuery(repositoryPath, query, "Limit must be between 1 and 1000.");
        }

        if (request.Direction is not ("in" or "out" or "both"))
        {
            return InvalidQuery(repositoryPath, query, "Direction must be in, out, or both.");
        }

        var load = Load(repositoryPath, query);
        if (load.Result is not null)
        {
            return load.Result;
        }

        var startNodes = _store.ResolveNodes(load.RepositoryPath!, request.Id, request.Kind).ToArray();
        if (startNodes.Length == 0)
        {
            return Success(load, "no-match", query, [], [], Truncated: false);
        }

        if (startNodes.Length > 1)
        {
            return InvalidQuery(
                load.RepositoryPath!,
                query,
                $"Start node ID is ambiguous; use a full graph key or add --kind: {request.Id}",
                startNodes.Select(node => node.Key).ToArray());
        }

        var start = startNodes[0];
        var nodesByKey = new Dictionary<string, CisGraphNode>(StringComparer.Ordinal) { [start.Key] = start };
        var nodesAtDepth = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [start.Key] = 0,
        };
        var pending = new Queue<CisGraphNode>();
        var traversals = new List<CisGraphTraversal>();
        var traversedEdges = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;
        pending.Enqueue(start);
        while (pending.TryDequeue(out var current))
        {
            var currentDepth = nodesAtDepth[current.Key];
            if (currentDepth >= request.Depth)
            {
                continue;
            }

            var adjacentEdges = _store.ReadAdjacentEdges(load.RepositoryPath!, current.Key, request.Direction,
                request.EdgeTypes, request.IncludeProposed);
            var adjacentKeys = AdjacentEdges(adjacentEdges, current.Key, request.Direction)
                .Select(candidate => candidate.Direction == "out" ? candidate.Edge.To : candidate.Edge.From)
                .Where(key => !nodesByKey.ContainsKey(key)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var node in _store.ReadNodes(load.RepositoryPath!, adjacentKeys)) nodesByKey[node.Key] = node;
            foreach (var candidate in AdjacentEdges(adjacentEdges, current.Key, request.Direction))
            {
                var adjacentKey = candidate.Direction == "out" ? candidate.Edge.To : candidate.Edge.From;
                if (!nodesByKey.TryGetValue(adjacentKey, out var adjacent))
                {
                    continue;
                }

                if (!nodesAtDepth.ContainsKey(adjacentKey))
                {
                    if (nodesAtDepth.Count >= request.Limit)
                    {
                        truncated = true;
                        continue;
                    }

                    nodesAtDepth[adjacentKey] = currentDepth + 1;
                    pending.Enqueue(adjacent);
                }

                if (nodesAtDepth[adjacentKey] <= request.Depth
                    && traversedEdges.Add(candidate.Edge.Key))
                {
                    traversals.Add(new CisGraphTraversal(
                        currentDepth + 1,
                        candidate.Direction,
                        candidate.Edge));
                }
            }
        }

        var selectedNodes = nodesAtDepth
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => nodesByKey[pair.Key])
            .ToArray();
        return Success(
            load,
            "matched",
            query,
            selectedNodes,
            traversals.OrderBy(item => item.Depth).ThenBy(item => item.Edge.Key, StringComparer.Ordinal).ToArray(),
            truncated);
    }

    public CisGraphTraceResult Trace(string repositoryPath, CisGraphTraceRequest request)
    {
        var query = QueryProperties(
            ("operation", "trace"),
            ("from", request.From),
            ("to", request.To),
            ("fromKind", request.FromKind),
            ("toKind", request.ToKind),
            ("edges", string.Join(',', request.EdgeTypes)),
            ("direction", request.Direction),
            ("maxDepth", request.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("maxPaths", request.MaxPaths.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("includeProposed", request.IncludeProposed.ToString().ToLowerInvariant()));
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.To))
        {
            return InvalidTraceQuery(repositoryPath, query, "Both source and target node IDs are required.");
        }

        if (request.MaxDepth is < 1 or > 10)
        {
            return InvalidTraceQuery(repositoryPath, query, "Maximum depth must be between 1 and 10.");
        }

        if (request.MaxPaths is < 1 or > 100)
        {
            return InvalidTraceQuery(repositoryPath, query, "Maximum paths must be between 1 and 100.");
        }

        if (request.Direction is not ("in" or "out" or "both"))
        {
            return InvalidTraceQuery(repositoryPath, query, "Direction must be in, out, or both.");
        }

        var load = Load(repositoryPath, query);
        if (load.Result is not null)
        {
            return TraceFromQueryResult(load.Result);
        }

        var fromMatches = _store.ResolveNodes(load.RepositoryPath!, request.From, request.FromKind).ToArray();
        var toMatches = _store.ResolveNodes(load.RepositoryPath!, request.To, request.ToKind).ToArray();
        if (fromMatches.Length == 0 || toMatches.Length == 0)
        {
            return TraceSuccess(load, "no-match", query, [], Truncated: false);
        }

        if (fromMatches.Length > 1 || toMatches.Length > 1)
        {
            var evidence = fromMatches.Length > 1 ? fromMatches : toMatches;
            return InvalidTraceQuery(
                load,
                query,
                "Trace endpoint is ambiguous; use a full graph key or provide its kind.",
                evidence.Select(node => node.Key).ToArray());
        }

        var from = fromMatches[0];
        var to = toMatches[0];
        if (string.Equals(from.Key, to.Key, StringComparison.Ordinal))
        {
            return TraceSuccess(
                load,
                "matched",
                query,
                [new CisGraphPath([from], [])],
                Truncated: false);
        }

        var nodesByKey = new Dictionary<string, CisGraphNode>(StringComparer.Ordinal)
        {
            [from.Key] = from,
            [to.Key] = to,
        };
        var pending = new Queue<PathState>();
        var bestDepth = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [from.Key] = 0,
        };
        var paths = new List<CisGraphPath>();
        int? shortestDepth = null;
        var truncated = false;
        pending.Enqueue(new PathState(from, [from], []));
        while (pending.TryDequeue(out var state))
        {
            var depth = state.Traversals.Count;
            if (depth >= request.MaxDepth || shortestDepth is not null && depth >= shortestDepth)
            {
                continue;
            }

            var adjacentEdges = _store.ReadAdjacentEdges(load.RepositoryPath!, state.Current.Key, request.Direction,
                request.EdgeTypes, request.IncludeProposed);
            var adjacentKeys = AdjacentEdges(adjacentEdges, state.Current.Key, request.Direction)
                .Select(candidate => candidate.Direction == "out" ? candidate.Edge.To : candidate.Edge.From)
                .Where(key => !nodesByKey.ContainsKey(key)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var node in _store.ReadNodes(load.RepositoryPath!, adjacentKeys)) nodesByKey[node.Key] = node;
            foreach (var candidate in AdjacentEdges(adjacentEdges, state.Current.Key, request.Direction))
            {
                var adjacentKey = candidate.Direction == "out" ? candidate.Edge.To : candidate.Edge.From;
                if (!nodesByKey.TryGetValue(adjacentKey, out var adjacent)
                    || state.Nodes.Any(node => string.Equals(node.Key, adjacentKey, StringComparison.Ordinal)))
                {
                    continue;
                }

                var nextDepth = depth + 1;
                var nextNodes = state.Nodes.Append(adjacent).ToArray();
                var nextTraversals = state.Traversals.Append(new CisGraphTraversal(
                    nextDepth,
                    candidate.Direction,
                    candidate.Edge)).ToArray();
                if (string.Equals(adjacentKey, to.Key, StringComparison.Ordinal))
                {
                    shortestDepth ??= nextDepth;
                    if (nextDepth == shortestDepth)
                    {
                        if (paths.Count < request.MaxPaths)
                        {
                            paths.Add(new CisGraphPath(nextNodes, nextTraversals));
                        }
                        else
                        {
                            truncated = true;
                        }
                    }

                    continue;
                }

                if (shortestDepth is not null || nextDepth >= request.MaxDepth)
                {
                    continue;
                }

                if (!bestDepth.TryGetValue(adjacentKey, out var knownDepth) || nextDepth <= knownDepth)
                {
                    bestDepth[adjacentKey] = nextDepth;
                    pending.Enqueue(new PathState(adjacent, nextNodes, nextTraversals));
                }
            }
        }

        return TraceSuccess(
            load,
            paths.Count == 0 ? "no-path" : "matched",
            query,
            paths
                .OrderBy(path => PathIdentity(path), StringComparer.Ordinal)
                .ToArray(),
            truncated);
    }

    private LoadState Load(
        string repositoryPath,
        IReadOnlyDictionary<string, string> query)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return new LoadState(
                null,
                null,
                null,
                null,
                "unknown",
                [],
                new CisGraphQueryResult(
                    "invalid-repository",
                    null,
                    null,
                    null,
                    "unknown",
                    query,
                    [],
                    [],
                    false,
                    resolution.Errors.Select(error => Diagnostic(
                        "CIS-GRAPH-QUERY-REPO-001",
                        "error",
                        error)).ToArray(),
                    RepositoryConfigurationValid: false,
                    GraphAvailable: false,
                    QueryValid: true));
        }

        var context = resolution.Context!;
        var graphPath = Path.Combine(context.RepositoryPath, GraphRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(graphPath))
        {
            return Unavailable(
                context.RepositoryPath,
                query,
                "Graph generation is missing. Run `cis graph build` first.",
                graphPath);
        }

        var header = _store.ReadHeader(context.RepositoryPath);
        if (!header.Success || header.Build is null || header.Manifest is null)
        {
            return Unavailable(context.RepositoryPath, query,
                $"Unable to read the SQLite graph generation: {header.Error}", graphPath);
        }
        var manifest = header.Manifest;
        if (header.Build.Id != manifest.BuildId || manifest.SchemaVersion != GraphBuilder.GraphSchemaVersion)
        {
            return Unavailable(context.RepositoryPath, query,
                $"Graph schema or build identity is unsupported: schema={manifest.SchemaVersion}.", graphPath);
        }
        var staleEvidence = FindStaleEvidence(context.RepositoryPath, manifest);
        var diagnostics = new List<CisGraphDiagnostic>();
        if (staleEvidence.Count > 0)
            diagnostics.Add(Diagnostic("CIS-GRAPH-QUERY-STALE-001", "warning",
                "The derived graph does not match the current repository inputs. Run `cis graph build`.",
                staleEvidence.Take(20).ToArray()));
        if (header.Build.Status == "partial")
            diagnostics.Add(Diagnostic("CIS-GRAPH-QUERY-PARTIAL-001", "warning",
                "The graph was built with extraction warnings; query results may be incomplete.", header.Build.Id));
        return new LoadState(context.RepositoryPath, GraphRelativePath, header.Build, manifest,
            staleEvidence.Count == 0 ? "fresh" : "stale", diagnostics, null);
    }

    private IReadOnlyList<string> FindStaleEvidence(
        string repositoryPath,
        CisGraphManifest manifest)
    {
        var stale = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!manifest.Extractors.SequenceEqual(_expectedExtractors, StringComparer.Ordinal))
        {
            stale.Add("extractor-set-changed");
        }

        var manifestPaths = manifest.Inputs
            .Select(input => input.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var input in manifest.Inputs)
        {
            var path = Path.Combine(repositoryPath, input.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                stale.Add(input.Path + " (missing)");
                continue;
            }

            try
            {
                if (!string.Equals(GraphBuilder.HashInput(input.Path, path), input.Hash, StringComparison.Ordinal))
                {
                    stale.Add(input.Path + " (changed)");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stale.Add(input.Path + " (unreadable)");
            }
        }

        try
        {
            foreach (var currentPath in ImplementationGraphExtractor.EnumerateInputPaths(repositoryPath))
            {
                if (!manifestPaths.Contains(currentPath))
                {
                    stale.Add(currentPath + " (new)");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stale.Add(repositoryPath + " (unable to enumerate current inputs)");
        }

        return stale.ToArray();
    }

    private static LoadState Unavailable(
        string repositoryPath,
        IReadOnlyDictionary<string, string> query,
        string message,
        string evidence)
        => new(
            repositoryPath,
            GraphRelativePath,
            null,
            null,
            "unknown",
            [],
            new CisGraphQueryResult(
                "graph-unavailable",
                repositoryPath,
                GraphRelativePath,
                null,
                "unknown",
                query,
                [],
                [],
                false,
                [Diagnostic("CIS-GRAPH-QUERY-LOAD-001", "error", message, evidence)],
                RepositoryConfigurationValid: true,
                GraphAvailable: false,
                QueryValid: true));

    private static CisGraphQueryResult Success(
        LoadState load,
        string status,
        IReadOnlyDictionary<string, string> query,
        IReadOnlyList<CisGraphNode> nodes,
        IReadOnlyList<CisGraphTraversal> traversals,
        bool Truncated)
        => new(
            status,
            load.RepositoryPath,
            load.GraphPath,
            load.Build,
            load.Freshness,
            query,
            nodes,
            traversals,
            Truncated,
            load.Diagnostics,
            RepositoryConfigurationValid: true,
            GraphAvailable: true,
            QueryValid: true);

    private static CisGraphQueryResult InvalidQuery(
        string repositoryPath,
        IReadOnlyDictionary<string, string> query,
        string message,
        params string[] evidence)
        => new(
            "invalid-query",
            repositoryPath,
            null,
            null,
            "unknown",
            query,
            [],
            [],
            false,
            [Diagnostic("CIS-GRAPH-QUERY-INPUT-001", "error", message, evidence)],
            RepositoryConfigurationValid: true,
            GraphAvailable: false,
            QueryValid: false);

    private static CisGraphTraceResult TraceSuccess(
        LoadState load,
        string status,
        IReadOnlyDictionary<string, string> query,
        IReadOnlyList<CisGraphPath> paths,
        bool Truncated)
        => new(
            status,
            load.RepositoryPath,
            load.GraphPath,
            load.Build,
            load.Freshness,
            query,
            paths,
            Truncated,
            load.Diagnostics,
            RepositoryConfigurationValid: true,
            GraphAvailable: true,
            QueryValid: true);

    private static CisGraphTraceResult InvalidTraceQuery(
        string repositoryPath,
        IReadOnlyDictionary<string, string> query,
        string message,
        params string[] evidence)
        => new(
            "invalid-query",
            repositoryPath,
            null,
            null,
            "unknown",
            query,
            [],
            false,
            [Diagnostic("CIS-GRAPH-TRACE-INPUT-001", "error", message, evidence)],
            RepositoryConfigurationValid: true,
            GraphAvailable: false,
            QueryValid: false);

    private static CisGraphTraceResult InvalidTraceQuery(
        LoadState load,
        IReadOnlyDictionary<string, string> query,
        string message,
        params string[] evidence)
        => new(
            "invalid-query",
            load.RepositoryPath,
            load.GraphPath,
            load.Build,
            load.Freshness,
            query,
            [],
            false,
            load.Diagnostics.Append(Diagnostic(
                "CIS-GRAPH-TRACE-INPUT-001",
                "error",
                message,
                evidence)).ToArray(),
            RepositoryConfigurationValid: true,
            GraphAvailable: true,
            QueryValid: false);

    private static CisGraphTraceResult TraceFromQueryResult(CisGraphQueryResult result)
        => new(
            result.Status,
            result.RepositoryPath,
            result.GraphPath,
            result.Build,
            result.Freshness,
            result.Query,
            [],
            result.Truncated,
            result.Diagnostics,
            result.RepositoryConfigurationValid,
            result.GraphAvailable,
            result.QueryValid);

    private static string PathIdentity(CisGraphPath path)
        => string.Join('\n', path.Traversals.Select(traversal =>
            $"{traversal.Direction}:{traversal.Edge.Key}"));

    private static IEnumerable<AdjacentEdge> AdjacentEdges(
        IEnumerable<CisGraphEdge> edges,
        string key,
        string direction)
    {
        foreach (var edge in edges)
        {
            if (direction is "out" or "both" && string.Equals(edge.From, key, StringComparison.Ordinal))
            {
                yield return new AdjacentEdge(edge, "out");
            }

            if (direction is "in" or "both" && string.Equals(edge.To, key, StringComparison.Ordinal))
            {
                yield return new AdjacentEdge(edge, "in");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> QueryProperties(
        params (string Name, string? Value)[] values)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static CisGraphDiagnostic Diagnostic(
        string code,
        string severity,
        string message,
        params string[] evidence)
        => new(code, severity, message, evidence);

    private sealed record LoadState(
        string? RepositoryPath,
        string? GraphPath,
        CisGraphBuildMetadata? Build,
        CisGraphManifest? Manifest,
        string Freshness,
        IReadOnlyList<CisGraphDiagnostic> Diagnostics,
        CisGraphQueryResult? Result);

    private sealed record AdjacentEdge(CisGraphEdge Edge, string Direction);

    private sealed record PathState(
        CisGraphNode Current,
        IReadOnlyList<CisGraphNode> Nodes,
        IReadOnlyList<CisGraphTraversal> Traversals);
}
