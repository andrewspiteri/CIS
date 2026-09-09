using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Frontend;

public sealed class FrontendContextService : ICisGraphAugmenter
{
    public const string StatePath = ".cis/local/frontend/context.json";
    private static readonly string[] Extensions = [".ts", ".tsx", ".js", ".jsx", ".vue", ".swift", ".kt", ".kts", ".gd", ".tscn"];
    private static readonly string[] Excluded = [".git", ".cis", ".codex-tmp", ".artifacts", "artifacts", "node_modules", "bin", "obj", "dist", "build", ".next", ".nuxt", ".godot", ".gradle", "coverage", "Pods"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly IReadOnlyList<ICisFrontendContextProvider> _providers;
    private readonly Func<DateTimeOffset> _clock;

    public FrontendContextService(ICisRepositoryContextResolver resolver, IEnumerable<ICisFrontendContextProvider> providers,
        Func<DateTimeOffset>? clock = null)
    {
        _resolver = resolver;
        _providers = providers.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Name => "frontend-context/3";

    public FrontendContextResult Discover(string repositoryPath)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, null, null, [], [], resolution.Errors.Select(error => Diagnostic("CIS-FRONTEND-REPO-001", "error", error)).ToArray(), false, false);
        var context = resolution.Context;
        var diagnostics = ProviderDiagnostics().ToList();
        var observations = new List<CisFrontendObservation>();
        if (diagnostics.All(item => item.Severity != "error"))
        {
            var discoveryContext = new CisFrontendDiscoveryContext(context.RepositoryPath, context.RepositoryId, context.DocumentationPath);
            foreach (var source in ReadSources(context, diagnostics))
                foreach (var provider in _providers.Where(item => item.CanInspect(source)))
                    observations.AddRange(provider.Discover(discoveryContext, source));
        }
        var normalized = observations.DistinctBy(item => $"{item.Provider}\u001f{item.Kind}\u001f{item.Id}", StringComparer.Ordinal)
            .OrderBy(item => item.Framework, StringComparer.Ordinal).ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.SourcePath, StringComparer.Ordinal).ThenBy(item => item.Line).ToArray();
        diagnostics.AddRange(ValidateObservations(context, normalized));
        var digest = Digest(normalized, diagnostics);
        var path = Path.Combine(context.RepositoryPath, StatePath.Replace('/', Path.DirectorySeparatorChar));
        var previous = ReadDocument(path);
        var created = previous?.Digest == digest ? previous.CreatedUtc : _clock().ToUniversalTime().ToString("O");
        var document = new FrontendContextDocument(1, context.RepositoryId, created, Revision(context.RepositoryPath), digest,
            _providers.Select(item => item.Name).ToArray(), normalized, diagnostics);
        var rendered = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var applied = !File.Exists(path) || !File.ReadAllText(path).Equals(rendered, StringComparison.Ordinal);
        if (applied) AtomicWrite(path, rendered);
        return new(diagnostics.Any(item => item.Severity == "error") ? "invalid" : "discovered", context.RepositoryPath,
            StatePath, digest, document.Providers, normalized, diagnostics, false, applied);
    }

    public FrontendContextResult Inventory(string repositoryPath, string? framework = null, string? kind = null)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, null, null, [], [], resolution.Errors.Select(error => Diagnostic("CIS-FRONTEND-REPO-001", "error", error)).ToArray(), false, false);
        var path = Path.Combine(resolution.Context.RepositoryPath, StatePath.Replace('/', Path.DirectorySeparatorChar));
        var document = ReadDocument(path);
        if (document is null) return new("missing-state", resolution.Context.RepositoryPath, StatePath, null, [], [],
            [Diagnostic("CIS-FRONTEND-STATE-001", "error", "Frontend context state is missing or unreadable. Run cis frontend discover.", StatePath)], false, false);
        var observations = document.Observations.Where(item => string.IsNullOrWhiteSpace(framework) || item.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(kind) || item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new("available", resolution.Context.RepositoryPath, StatePath, document.Digest, document.Providers, observations, document.Diagnostics, false, false);
    }

    public FrontendContextResult Validate(string repositoryPath, bool strict, bool refresh = true)
    {
        var result = refresh ? Discover(repositoryPath) : Inventory(repositoryPath);
        return result with { Status = result.Diagnostics.Any(item => item.Severity == "error") || strict && result.Diagnostics.Any(item => item.Severity == "warning") ? "invalid" : "valid", Strict = strict };
    }

    public CisGraphAugmentation Augment(CisRepositoryContext context, IReadOnlyDictionary<string, string> inputHashes)
    {
        var result = Discover(context.RepositoryPath);
        var nodes = new List<CisGraphNode>();
        var edges = new List<CisGraphEdge>();
        var repositoryKey = $"{context.RepositoryId}::repository::{context.RepositoryId}";
        foreach (var item in result.Observations)
        {
            var kind = GraphKind(item.Kind); var key = $"{context.RepositoryId}::{kind}::{item.Id}";
            var evidence = new CisGraphEvidence("source", item.SourcePath, "line", item.Line.ToString(),
                inputHashes.GetValueOrDefault(item.SourcePath), "frontend-provider", item.Provider, "medium", "Deterministic framework adapter observation.");
            var properties = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = item.Provider, ["framework"] = item.Framework, ["frontendKind"] = item.Kind,
            };
            if (item.Route is not null) properties["route"] = item.Route;
            if (item.Target is not null) properties["target"] = item.Target;
            foreach (var pair in item.Properties) properties[pair.Key] = pair.Value;
            nodes.Add(new(key, context.RepositoryId, kind, item.Framework, item.Id, item.Name,
                ["frontend", item.Framework, item.Kind], "derived", "active",
                [new(item.SourcePath, "line", item.Line.ToString())], properties, [evidence]));
            edges.Add(Edge(context.RepositoryId, "contains", repositoryKey, key, evidence));
        }
        foreach (var item in result.Observations.Where(item => !string.IsNullOrWhiteSpace(item.Target)))
        {
            var from = nodes.First(node => node.LocalId == item.Id);
            var targetObservation = result.Observations.FirstOrDefault(candidate =>
                candidate.Id.Equals(item.Target, StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Equals(item.Target, StringComparison.OrdinalIgnoreCase)
                || candidate.Route?.Equals(item.Target, StringComparison.OrdinalIgnoreCase) == true);
            if (targetObservation is null) continue;
            var to = nodes.First(node => node.LocalId == targetObservation.Id);
            var evidence = from.Provenance[0];
            edges.Add(Edge(context.RepositoryId, item.Kind == "route" ? "renders" : "navigates-to", from.Key, to.Key, evidence));
        }
        return new(nodes, edges, result.Diagnostics.Select(item => new CisGraphDiagnostic(item.Code, item.Severity, item.Message, item.Evidence)).ToArray());
    }

    private IReadOnlyList<FrontendContextDiagnostic> ProviderDiagnostics()
    {
        return _providers.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .Select(group => Diagnostic("CIS-FRONTEND-PROVIDER-001", "error", $"Frontend provider is registered more than once: {group.Key}", group.Select(item => item.GetType().FullName ?? item.GetType().Name).ToArray())).ToArray();
    }

    private static IReadOnlyList<CisFrontendSourceFile> ReadSources(CisRepositoryContext context, List<FrontendContextDiagnostic> diagnostics)
    {
        var results = new List<CisFrontendSourceFile>();
        try
        {
            foreach (var path in EnumerateFrontendFiles(context.RepositoryPath, diagnostics))
            {
                var info = new FileInfo(path); if (info.Length > 2_000_000) { diagnostics.Add(Diagnostic("CIS-FRONTEND-SOURCE-002", "warning", "Frontend source exceeds the 2 MB adapter limit.", Relative(context, path))); continue; }
                try { results.Add(new(Relative(context, path), File.ReadAllText(path))); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { diagnostics.Add(Diagnostic("CIS-FRONTEND-SOURCE-001", "warning", exception.Message, Relative(context, path))); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { diagnostics.Add(Diagnostic("CIS-FRONTEND-SOURCE-003", "error", "Unable to enumerate frontend sources: " + exception.Message)); }
        return results;
    }

    private static IEnumerable<string> EnumerateFrontendFiles(string root, List<FrontendContextDiagnostic> diagnostics)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = CisPathSafety.EnumerateFiles(current, recursive: false).ToArray();
                directories = CisPathSafety.EnumerateDirectories(current, recursive: false).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var relative = Path.GetRelativePath(root, current).Replace('\\', '/');
                diagnostics.Add(Diagnostic(current.Equals(root, StringComparison.OrdinalIgnoreCase)
                        ? "CIS-FRONTEND-SOURCE-003" : "CIS-FRONTEND-SOURCE-004",
                    current.Equals(root, StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                    "Unable to enumerate frontend source directory: " + exception.Message,
                    relative == "." ? "." : relative));
                continue;
            }

            foreach (var file in files.Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                         .Order(StringComparer.OrdinalIgnoreCase))
                yield return file;

            foreach (var directory in directories.Where(path => !ExcludedPath(root, path))
                         .OrderDescending(StringComparer.OrdinalIgnoreCase))
                pending.Push(directory);
        }
    }

    private static IReadOnlyList<FrontendContextDiagnostic> ValidateObservations(CisRepositoryContext context, IReadOnlyList<CisFrontendObservation> observations)
    {
        var diagnostics = new List<FrontendContextDiagnostic>();
        foreach (var collision in observations.Where(item => item.Kind == "route" && item.Route is not null
                && item.Properties.GetValueOrDefault("routeResolution") != "relative-declaration")
            .GroupBy(item => $"{item.Framework}\u001f{item.Route}", StringComparer.OrdinalIgnoreCase).Where(group => group.Select(item => item.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            diagnostics.Add(Diagnostic("CIS-FRONTEND-ROUTE-001", "error", $"Route resolves to multiple targets: {collision.First().Route}", collision.Select(item => $"{item.SourcePath}:{item.Line}").ToArray()));
        var canonical = CanonicalDestinations(context);
        if (canonical.Count > 0)
            foreach (var route in observations.Where(item => item.Kind == "route" && item.Route is not null && !canonical.Contains(item.Route)
                && item.Properties.GetValueOrDefault("routeResolution") != "relative-declaration"))
                diagnostics.Add(Diagnostic("CIS-FRONTEND-CANONICAL-001", "warning", $"Observed route is not represented in the canonical screen and route map: {route.Route}", $"{route.SourcePath}:{route.Line}"));
        return diagnostics;
    }

    private static IReadOnlySet<string> CanonicalDestinations(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "screen-route-map.md");
        if (!File.Exists(path)) return new HashSet<string>();
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length >= 8 && !cells[0].Equals("Screen or route", StringComparison.OrdinalIgnoreCase) && !cells.All(cell => cell.All(ch => ch is '-' or ':' or ' '))
                && cells[4].StartsWith('/')) values.Add(cells[4]);
        }
        return values;
    }

    private static FrontendContextDocument? ReadDocument(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<FrontendContextDocument>(File.ReadAllText(path), JsonOptions); }
        catch (JsonException) { return null; }
    }
    private static string Digest(IReadOnlyList<CisFrontendObservation> observations, IReadOnlyList<FrontendContextDiagnostic> diagnostics) => Sha256(JsonSerializer.Serialize(new { observations, diagnostics }, JsonOptions));
    private static string Revision(string repository)
    {
        try { var start = new ProcessStartInfo("git") { WorkingDirectory = repository, ArgumentList = { "rev-parse", "HEAD" } }; var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(10)); return !result.TimedOut && result.ExitCode == 0 ? result.StandardOutput.Trim() : "unavailable"; }
        catch { return "unavailable"; }
    }
    private static CisGraphEdge Edge(string repositoryId, string type, string from, string to, CisGraphEvidence evidence) => new($"{repositoryId}::edge::frontend-{Sha256($"{from}\n{type}\n{to}")[..20]}", type, from, to, "discovered", "medium", [evidence], new SortedDictionary<string, string>());
    private static string GraphKind(string kind) => kind switch { "screen" => "screen", "route" => "route", "component" or "screen-component" => "ui-component", "navigation" => "navigation", "api-call" => "api-client", "state" => "state-store", _ => "frontend-observation" };
    private static bool ExcludedPath(string root, string path) => Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => Excluded.Contains(segment, StringComparer.OrdinalIgnoreCase));
    private static string Relative(CisRepositoryContext context, string path) => Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
    private static FrontendContextDiagnostic Diagnostic(string code, string severity, string message, params string[] evidence) => new(code, severity, message, evidence);
    private static void AtomicWrite(string path, string content) { var temp = path + ".tmp"; File.WriteAllText(temp, content); File.Move(temp, path, true); }
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
