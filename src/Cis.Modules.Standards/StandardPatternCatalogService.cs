using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Docs;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Modules.Standards;

public sealed partial class StandardPatternCatalogService
{
    private static readonly HashSet<string> Lifecycles = new(StringComparer.OrdinalIgnoreCase) { "Active", "Draft", "Deprecated", "Archived" };
    private static readonly HashSet<string> EnforcementStates = new(StringComparer.OrdinalIgnoreCase) { "deterministic", "architecture-test", "manual-review", "advisory-model", "not-mapped" };
    private static readonly HashSet<string> Levels = new(StringComparer.OrdinalIgnoreCase) { "MUST", "MUST NOT", "SHOULD", "MAY" };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly IReadOnlyList<ICisStandardPatternProvider> _providers;
    private readonly ICisGraphSnapshotReader? _graphReader;
    private readonly DocumentationCatalogReader _catalogReader;
    private readonly IDeserializer _deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    public StandardPatternCatalogService(
        ICisRepositoryContextResolver resolver,
        IEnumerable<ICisStandardPatternProvider> providers,
        ICisGraphSnapshotReader? graphReader = null,
        DocumentationCatalogReader? catalogReader = null)
    {
        _resolver = resolver;
        _providers = providers.OrderBy(provider => provider.ProviderKey, StringComparer.Ordinal).ToArray();
        _graphReader = graphReader;
        _catalogReader = catalogReader ?? new DocumentationCatalogReader();
    }

    public StandardPatternCatalogResult Inventory(StandardPatternCatalogRequest request)
    {
        var resolution = _resolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess)
            return new("invalid", 2, null, _providers.Count, [], [], [], resolution.Errors);
        var context = resolution.Context!;
        var diagnostics = new List<StandardPatternDiagnostic>();
        var patterns = new List<CisStandardPatternDefinition>();
        foreach (var provider in _providers)
        foreach (var definition in provider.Patterns)
            patterns.Add(definition with
            {
                Provider = provider.ProviderKey,
                Authority = string.IsNullOrWhiteSpace(definition.Authority) ? "extension" : definition.Authority,
                Source = string.IsNullOrWhiteSpace(definition.Source) ? provider.ProviderKey : definition.Source,
            });
        patterns.AddRange(ReadRepositoryPatterns(context, diagnostics));
        foreach (var pattern in patterns) Validate(pattern, diagnostics);

        var duplicates = patterns.GroupBy(pattern => pattern.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray();
        foreach (var duplicate in duplicates)
        {
            var sources = duplicate.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
            diagnostics.Add(Diagnostic("CIS-STD-PATTERN-020", "error", duplicate.Key, string.Join(", ", sources),
                $"Pattern ID '{duplicate.Key}' is registered more than once.",
                "Use a repository-unique ID; pattern providers cannot replace one another by load order."));
        }
        var conflicted = duplicates.Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var valid = patterns.Where(pattern => !conflicted.Contains(pattern.Id)
            && !diagnostics.Any(item => item.Severity == "error" && item.PatternId.Equals(pattern.Id, StringComparison.OrdinalIgnoreCase))).ToArray();

        var graph = _graphReader?.Read(context.RepositoryPath);
        var graphCapabilities = graph?.GraphCapabilities ?? [];
        var repositoryDimensions = Dimensions(graph?.Graph);
        var languageFilter = request.Languages.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = valid
            .Where(pattern => languageFilter.Count == 0 || pattern.Languages.Any(languageFilter.Contains))
            .Select(pattern =>
            {
                var missing = pattern.RequiredGraphCapabilities.Where(required => !graphCapabilities.Contains(required, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
                var applicable = IsApplicable(pattern, repositoryDimensions);
                return new StandardPatternCatalogItem(pattern, applicable, graph is null || graph.Graph is null ? "graph-unavailable" : missing.Length == 0 ? "ready" : "requires-graph-capability", missing);
            })
            .Where(item => !request.ApplicableOnly || item.Applicable)
            .OrderBy(item => item.Definition.Id, StringComparer.Ordinal)
            .ToArray();
        var warnings = diagnostics.Where(item => item.Severity == "warning").Select(item => item.Message).Distinct().ToArray();
        var errors = diagnostics.Where(item => item.Severity == "error").Select(item => item.Message).Distinct().ToArray();
        var exit = errors.Length > 0 ? 2 : request.Strict && warnings.Length > 0 ? 5 : 0;
        return new(exit == 0 ? "valid" : "invalid", exit, context.RepositoryPath, _providers.Count + (patterns.Any(item => item.Provider == "repository") ? 1 : 0), items, diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.PatternId, StringComparer.Ordinal).ToArray(), warnings, errors);
    }

    private IReadOnlyList<CisStandardPatternDefinition> ReadRepositoryPatterns(CisRepositoryContext context, ICollection<StandardPatternDiagnostic> diagnostics)
    {
        var root = Path.Combine(context.DocumentationPath, "references", "standard-patterns");
        if (!Directory.Exists(root)) return [];
        var catalogRead = _catalogReader.Read(context.CatalogPath);
        if (!catalogRead.IsSuccess)
        {
            foreach (var error in catalogRead.Errors) diagnostics.Add(Diagnostic("CIS-STD-PATTERN-017", "error", string.Empty, context.CatalogPath, error, "Repair the documentation catalog before loading repository patterns."));
            return [];
        }
        var catalog = catalogRead.Catalog!;
        var patterns = new List<CisStandardPatternDefinition>();
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(context.RepositoryPath, file).Replace('\\', '/');
            try
            {
                var yaml = FrontMatter(File.ReadAllText(file));
                var document = _deserializer.Deserialize<PatternDocument>(yaml) ?? new PatternDocument();
                if (!document.Type.Equals("standard-inference-pattern", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Diagnostic("CIS-STD-PATTERN-001", "error", document.Cis.StableId, relative,
                        "Repository pattern documents must declare type: standard-inference-pattern.", "Correct the front matter type or move the file out of the standard-patterns directory."));
                    continue;
                }
                patterns.Add(Map(document, relative));
                var entry = catalog.Documents.FirstOrDefault(item => item.Id.Equals(document.Cis.StableId, StringComparison.OrdinalIgnoreCase));
                if (entry is null || !entry.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)
                    || !entry.Type.Equals("standard-inference-pattern", StringComparison.OrdinalIgnoreCase)
                    || !entry.Authority.Equals("canonical", StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add(Diagnostic("CIS-STD-PATTERN-018", "error", document.Cis.StableId, relative,
                        "Repository pattern is not registered as a canonical standard-inference-pattern at the same path.",
                        "Add or correct its catalog.yml entry before using the pattern."));
            }
            catch (Exception exception) when (exception is YamlException or FormatException or IOException)
            {
                diagnostics.Add(Diagnostic("CIS-STD-PATTERN-002", "error", string.Empty, relative,
                    $"Unable to read repository pattern: {exception.Message}", "Repair the Markdown YAML front matter."));
            }
        }
        return patterns;
    }

    private static CisStandardPatternDefinition Map(PatternDocument document, string source)
    {
        var pattern = document.Pattern ?? new PatternBody();
        return new(
            document.Cis.StableId.Trim(), pattern.Version, document.Title.Trim(), pattern.Description.Trim(), document.Status.Trim(),
            pattern.Languages, pattern.Roles, pattern.Capabilities, pattern.RequiredGraphCapabilities,
            Map(pattern.Subject), pattern.Requires.Select(Map).ToArray(), pattern.Forbids.Select(Map).ToArray(),
            pattern.Thresholds.MinimumOccurrences, pattern.Thresholds.MinimumConsistency,
            new(pattern.Candidate.Target, pattern.Candidate.ProposedLevel, pattern.Candidate.Wording, pattern.Candidate.SuggestedEnforcement, pattern.Candidate.Verification),
            "repository", "canonical", source);
    }

    private static CisStandardPatternSelector Map(PatternSelector? selector)
        => selector is null ? EmptySelector() : new(selector.Kind, selector.Subtype, selector.Facets,
            new Dictionary<string, string>(selector.PropertyEquals, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(selector.PropertyContains, StringComparer.OrdinalIgnoreCase),
            selector.PathPrefixes, selector.ExcludedPathPrefixes);

    private static CisStandardPatternRelation Map(PatternRelation relation)
        => new(relation.Direction, relation.EdgeType, Map(relation.Target));

    private static void Validate(CisStandardPatternDefinition pattern, ICollection<StandardPatternDiagnostic> diagnostics)
    {
        void Error(string code, string message, string remediation) => diagnostics.Add(Diagnostic(code, "error", pattern.Id, pattern.Source, message, remediation));
        if (!PatternIdRegex().IsMatch(pattern.Id)) Error("CIS-STD-PATTERN-003", $"Pattern ID '{pattern.Id}' is invalid.", "Use lowercase dot-separated identity segments.");
        if (pattern.Version < 1) Error("CIS-STD-PATTERN-004", "Pattern version must be at least 1.", "Assign a positive immutable version.");
        if (string.IsNullOrWhiteSpace(pattern.Title) || string.IsNullOrWhiteSpace(pattern.Description)) Error("CIS-STD-PATTERN-005", "Pattern title and description are required.", "Describe the recognized graph convention.");
        if (!Lifecycles.Contains(pattern.Status)) Error("CIS-STD-PATTERN-006", $"Unsupported pattern lifecycle '{pattern.Status}'.", "Use Active, Draft, Deprecated, or Archived.");
        if (pattern.Languages.Count == 0) Error("CIS-STD-PATTERN-007", "At least one language is required.", "Declare the compiler language adapter used by the pattern.");
        if (pattern.RequiredGraphCapabilities.Count == 0) Error("CIS-STD-PATTERN-008", "Required graph capabilities are missing.", "Declare every compiler graph fact required for deterministic matching.");
        if (IsEmpty(pattern.Subject)) Error("CIS-STD-PATTERN-009", "The subject selector is empty.", "Constrain the eligible graph-node population.");
        foreach (var relation in pattern.Requires.Concat(pattern.Forbids))
        {
            if (relation.Direction is not ("incoming" or "outgoing")) Error("CIS-STD-PATTERN-010", $"Unsupported relation direction '{relation.Direction}'.", "Use incoming or outgoing.");
            if (string.IsNullOrWhiteSpace(relation.EdgeType) || IsEmpty(relation.Target)) Error("CIS-STD-PATTERN-011", "Relations require an edge type and constrained target selector.", "Define the graph edge and adjacent-node shape.");
        }
        if (pattern.MinimumOccurrences is < 1 or > 100_000) Error("CIS-STD-PATTERN-012", "Minimum occurrences must be between 1 and 100000.", "Choose a bounded evidence threshold.");
        if (pattern.MinimumConsistency is < 0 or > 1) Error("CIS-STD-PATTERN-013", "Minimum consistency must be between 0 and 1.", "Use a normalized consistency threshold.");
        if (!Levels.Contains(pattern.Candidate.ProposedLevel)) Error("CIS-STD-PATTERN-014", $"Unsupported proposed normative level '{pattern.Candidate.ProposedLevel}'.", "Use MUST, MUST NOT, SHOULD, or MAY.");
        if (string.IsNullOrWhiteSpace(pattern.Candidate.Target) || string.IsNullOrWhiteSpace(pattern.Candidate.Wording)) Error("CIS-STD-PATTERN-015", "Candidate target and wording are required.", "Define the reviewable standard proposal produced by a match.");
        if (!EnforcementStates.Contains(pattern.Candidate.SuggestedEnforcement)) Error("CIS-STD-PATTERN-016", $"Unsupported suggested enforcement '{pattern.Candidate.SuggestedEnforcement}'.", "Use a standards conformance enforcement state.");
    }

    private static bool IsApplicable(CisStandardPatternDefinition pattern, RepositoryDimensions dimensions)
        => pattern.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)
           && (pattern.Languages.Count == 0 || pattern.Languages.Any(dimensions.Languages.Contains))
           && (pattern.Roles.Count == 0 || pattern.Roles.Any(dimensions.Roles.Contains))
           && (pattern.Capabilities.Count == 0 || pattern.Capabilities.Any(dimensions.Capabilities.Contains));

    private static RepositoryDimensions Dimensions(CisGraphDocument? graph)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (graph is not null)
        foreach (var component in graph.Nodes.Where(node => node.Kind == "component"))
        {
            AddCsv(component, "languages", languages); AddCsv(component, "roles", roles); AddCsv(component, "capabilities", capabilities);
        }
        return new(languages, roles, capabilities);
    }

    private static void AddCsv(CisGraphNode node, string property, ISet<string> values)
    {
        if (!node.Properties.TryGetValue(property, out var content)) return;
        foreach (var value in content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) values.Add(value);
    }

    private static string FrontMatter(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal)) throw new FormatException("YAML front matter is required.");
        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0) throw new FormatException("YAML front matter is not terminated.");
        return normalized[4..end];
    }

    private static bool IsEmpty(CisStandardPatternSelector selector)
        => string.IsNullOrWhiteSpace(selector.Kind) && string.IsNullOrWhiteSpace(selector.Subtype) && selector.Facets.Count == 0
           && selector.PropertyEquals.Count == 0 && selector.PropertyContains.Count == 0
           && selector.PathPrefixes.Count == 0 && selector.ExcludedPathPrefixes.Count == 0;

    private static CisStandardPatternSelector EmptySelector() => new(null, null, [], new Dictionary<string, string>(), new Dictionary<string, string>(), [], []);
    private static StandardPatternDiagnostic Diagnostic(string code, string severity, string patternId, string source, string message, string remediation)
        => new(code, severity, patternId, source, message, remediation);

    private sealed record RepositoryDimensions(IReadOnlySet<string> Languages, IReadOnlySet<string> Roles, IReadOnlySet<string> Capabilities);
    private sealed class PatternDocument { public string Title { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public PatternCis Cis { get; set; } = new(); public PatternBody? Pattern { get; set; } }
    private sealed class PatternCis { public string StableId { get; set; } = string.Empty; }
    private sealed class PatternBody { public int Version { get; set; } public string Description { get; set; } = string.Empty; public List<string> Languages { get; set; } = []; public List<string> Roles { get; set; } = []; public List<string> Capabilities { get; set; } = []; public List<string> RequiredGraphCapabilities { get; set; } = []; public PatternSelector Subject { get; set; } = new(); public List<PatternRelation> Requires { get; set; } = []; public List<PatternRelation> Forbids { get; set; } = []; public PatternThresholds Thresholds { get; set; } = new(); public PatternCandidate Candidate { get; set; } = new(); }
    private sealed class PatternSelector { public string? Kind { get; set; } public string? Subtype { get; set; } public List<string> Facets { get; set; } = []; public Dictionary<string, string> PropertyEquals { get; set; } = []; public Dictionary<string, string> PropertyContains { get; set; } = []; public List<string> PathPrefixes { get; set; } = []; public List<string> ExcludedPathPrefixes { get; set; } = []; }
    private sealed class PatternRelation { public string Direction { get; set; } = string.Empty; public string EdgeType { get; set; } = string.Empty; public PatternSelector Target { get; set; } = new(); }
    private sealed class PatternThresholds { public int MinimumOccurrences { get; set; } public double MinimumConsistency { get; set; } }
    private sealed class PatternCandidate { public string Target { get; set; } = string.Empty; public string ProposedLevel { get; set; } = string.Empty; public string Wording { get; set; } = string.Empty; public string SuggestedEnforcement { get; set; } = string.Empty; public string Verification { get; set; } = string.Empty; }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PatternIdRegex();
}
