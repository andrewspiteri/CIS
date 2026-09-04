using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed class GraphSnapshotReader : ICisGraphSnapshotReader
{
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly SqliteGraphStore _store;
    private readonly IReadOnlyList<string> _expectedExtractors;

    public GraphSnapshotReader(ICisRepositoryContextResolver resolver, SqliteGraphStore? store = null,
        IEnumerable<ICisGraphAugmenter>? augmenters = null)
    {
        _resolver = resolver;
        _store = store ?? new SqliteGraphStore();
        _expectedExtractors = GraphBuilder.ComposeExtractors(augmenters);
    }

    public CisGraphSnapshotReadResult Read(string repositoryPath)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
            return Result("invalid-repository", 2, null, "unknown", null, null, [], [], resolution.Errors);
        var context = resolution.Context!;
        var graphPath = _store.DatabasePath(context.RepositoryPath);
        if (!File.Exists(graphPath))
            return Result("missing", 4, context.RepositoryPath, "missing", null, null, [], [], ["Graph generation is missing. Run `cis graph build` first."]);
        var stored = _store.Read(context.RepositoryPath);
        if (!stored.Success || stored.Graph is null || stored.Manifest is null)
            return Result("invalid", 4, context.RepositoryPath, "unknown", null, null, [], [], [$"Unable to read SQLite graph generation: {stored.Error}"]);
        var graph = stored.Graph;
        var manifest = stored.Manifest;
        try
        {
            if (graph.SchemaVersion != GraphBuilder.GraphSchemaVersion
                || manifest.SchemaVersion != GraphBuilder.GraphSchemaVersion)
                return Result("invalid", 4, context.RepositoryPath, "stale", graph, manifest, [], [],
                    [$"Graph schema is incompatible (graph={graph.SchemaVersion}, manifest={manifest.SchemaVersion}, expected={GraphBuilder.GraphSchemaVersion}). Run `cis graph build`."]);
            if (graph.Build.Id != manifest.BuildId)
                return Result("invalid", 4, context.RepositoryPath, "unknown", graph, manifest, [], [], ["Graph and manifest build identities do not match. Run `cis graph build`."]);
            var stale = FindStaleInputs(context.RepositoryPath, manifest);
            var warnings = new List<string>();
            if (graph.Build.Status == "partial") warnings.Add("The graph was built with extraction warnings; inference evidence may be incomplete.");
            if (stale.Count > 0) warnings.Add($"The graph is stale for {stale.Count} input(s); run `cis graph build` before inference.");
            return Result(
                stale.Count == 0 ? "available" : "stale",
                stale.Count == 0 ? 0 : 5,
                context.RepositoryPath,
                stale.Count == 0 ? "fresh" : "stale",
                graph,
                manifest,
                Capabilities(graph, manifest),
                warnings.Concat(stale.Take(20).Select(path => "Stale graph input: " + path)).ToArray(),
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result("invalid", 4, context.RepositoryPath, "unknown", null, null, [], [], [$"Unable to read graph generation: {exception.Message}"]);
        }
    }

    private static IReadOnlyList<string> Capabilities(CisGraphDocument graph, CisGraphManifest manifest)
    {
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var nodeKinds = graph.Nodes.ToDictionary(node => node.Key, node => node.Kind, StringComparer.Ordinal);
        if (graph.Nodes.Any(node => node.Kind == "symbol" && node.Facets.Contains("compiler-bound", StringComparer.OrdinalIgnoreCase)))
            capabilities.Add("compiler.symbols");
        if (graph.Edges.Any(edge => edge.Type == "calls" && edge.Observations.Any(item => item.Method == "compiler-binding")))
            capabilities.Add("compiler.calls");
        if (graph.Nodes.Any(node => node.Kind == "test")) capabilities.Add("tests.symbols");
        if (graph.Edges.Any(edge => edge.Type == "calls" && nodeKinds.TryGetValue(edge.From, out var kind) && kind == "test"))
            capabilities.Add("tests.calls");
        if (manifest.Extractors.Any(extractor => extractor is "cis.csharp.semantic-facts/1" or "cis.csharp.semantic-facts/2" or "cis.csharp.semantic-facts/3"))
        {
            capabilities.Add("compiler.symbols");
            capabilities.Add("compiler.calls");
            capabilities.Add("compiler.attributes");
            capabilities.Add("compiler.interfaces");
            capabilities.Add("compiler.inheritance");
            capabilities.Add("compiler.external-calls");
            capabilities.Add("compiler.generic-arguments");
            capabilities.Add("compiler.invocation-arguments");
            capabilities.Add("compiler.generated-markers");
            capabilities.Add("compiler.dataflow");
        }
        foreach (var extractor in manifest.Extractors) capabilities.Add("extractor:" + extractor);
        return capabilities.ToArray();
    }

    private IReadOnlyList<string> FindStaleInputs(string repositoryPath, CisGraphManifest manifest)
    {
        var stale = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!manifest.Extractors.SequenceEqual(_expectedExtractors, StringComparer.Ordinal)) stale.Add("extractor-set-changed");
        var known = manifest.Inputs.Select(input => input.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var input in manifest.Inputs)
        {
            var path = Path.Combine(repositoryPath, input.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { stale.Add(input.Path + " (missing)"); continue; }
            try
            {
                var hash = GraphBuilder.HashInput(input.Path, path);
                if (!hash.Equals(input.Hash, StringComparison.Ordinal)) stale.Add(input.Path + " (changed)");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stale.Add(input.Path + " (unreadable)");
            }
        }
        try
        {
            foreach (var current in ImplementationGraphExtractor.EnumerateInputPaths(repositoryPath))
                if (!known.Contains(current)) stale.Add(current + " (new)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stale.Add(repositoryPath + " (unable to enumerate current inputs)");
        }
        return stale.ToArray();
    }

    private static CisGraphSnapshotReadResult Result(
        string status, int exitCode, string? repositoryPath, string freshness,
        CisGraphDocument? graph, CisGraphManifest? manifest, IReadOnlyList<string> capabilities,
        IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        => new(status, exitCode, repositoryPath, freshness, graph, manifest, capabilities, warnings, errors);
}
