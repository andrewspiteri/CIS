using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Docs;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Cis.Modules.Standards;

public sealed partial class StandardsGovernanceService
{
    public const string ConformanceMatrixPath = "references/standards-conformance-matrix.md";

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft", "active", "deprecated", "archived",
    };

    private static readonly HashSet<string> AllowedEnforcement = new(StringComparer.OrdinalIgnoreCase)
    {
        "deterministic", "architecture-test", "manual-review", "advisory-model", "not-mapped",
    };

    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly DocumentationCatalogReader _catalogReader;
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public StandardsGovernanceService(
        ICisRepositoryContextResolver repositoryContextResolver,
        DocumentationCatalogReader catalogReader)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _catalogReader = catalogReader;
    }

    public StandardInventoryResult Inventory(
        string repositoryPath,
        IReadOnlyList<string>? targets = null,
        IReadOnlyList<string>? stacks = null,
        string? status = null)
    {
        var loaded = Load(repositoryPath);
        if (loaded.Errors.Count > 0)
        {
            return new StandardInventoryResult(
                "failed", loaded.RepositoryPath, loaded.DocumentationRoot, [], loaded.Warnings, loaded.Errors);
        }

        var selected = loaded.Standards
            .Where(item => string.IsNullOrWhiteSpace(status)
                || string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesAny(item.Targets, targets))
            .Where(item => MatchesStack(item.Stacks, stacks))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StandardInventoryResult(
            "inventoried", loaded.RepositoryPath, loaded.DocumentationRoot, selected, loaded.Warnings, []);
    }

    public StandardInventoryResult Applicable(
        string repositoryPath,
        IReadOnlyList<string> targets,
        IReadOnlyList<string>? stacks = null)
    {
        if (targets.Count == 0)
        {
            return new StandardInventoryResult(
                "invalid-request", null, null, [], [], ["At least one --target value is required."]);
        }

        return Inventory(repositoryPath, targets, stacks, "active");
    }

    public StandardsConformanceResult Conformance(string repositoryPath, bool gapsOnly = false)
    {
        var loaded = Load(repositoryPath);
        if (loaded.Errors.Count > 0)
        {
            return new StandardsConformanceResult(
                "failed", loaded.RepositoryPath, loaded.DocumentationRoot, [], loaded.Warnings, loaded.Errors);
        }

        var entries = gapsOnly
            ? loaded.Conformance.Where(item =>
                item.Enforcement.Equals("not-mapped", StringComparison.OrdinalIgnoreCase)
                || item.Enforcement.Equals("advisory-model", StringComparison.OrdinalIgnoreCase)
                || !item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)).ToArray()
            : loaded.Conformance;
        return new StandardsConformanceResult(
            "inventoried", loaded.RepositoryPath, loaded.DocumentationRoot, entries, loaded.Warnings, []);
    }

    public StandardsValidationResult Validate(string repositoryPath, bool strict = false)
    {
        var loaded = Load(repositoryPath);
        var diagnostics = new List<StandardsValidationDiagnostic>();
        diagnostics.AddRange(loaded.Errors.Select(message => Diagnostic(
            "CIS-STD-LOAD-001", "error", string.Empty, message,
            "Repair the CIS repository configuration, documentation catalog, or unreadable standard.")));
        diagnostics.AddRange(loaded.Warnings.Select(message => Diagnostic(
            "CIS-STD-LOAD-002", "warning", string.Empty, message,
            "Review the standards inventory and correct incomplete metadata.")));

        foreach (var document in loaded.Documents)
        {
            ValidateDocument(document, diagnostics);
        }

        foreach (var duplicate in loaded.Documents
                     .SelectMany(document => document.Definition.RuleIds.Select(rule => (rule, document.Definition.Path)))
                     .GroupBy(item => item.rule, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Diagnostic(
                "CIS-STD-RULE-004", "error", string.Join(", ", duplicate.Select(item => item.Path)),
                $"Rule ID '{duplicate.Key}' is declared more than once.",
                "Assign a repository-unique stable rule ID; never reuse a retired ID."));
        }

        ValidateConformance(loaded, diagnostics);
        return new StandardsValidationResult(
            diagnostics.Any(item => item.Severity == "error") || (strict && diagnostics.Any(item => item.Severity == "warning"))
                ? "invalid"
                : "valid",
            loaded.RepositoryPath,
            loaded.DocumentationRoot,
            loaded.Standards.Count,
            loaded.Standards.Sum(item => item.RuleIds.Count),
            loaded.Conformance.Count,
            strict,
            diagnostics.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Code).ToArray());
    }

    private LoadedStandards Load(string repositoryPath)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return new LoadedStandards(null, null, [], [], [], [], resolution.Errors);
        }

        var context = resolution.Context!;
        var catalog = _catalogReader.Read(context.CatalogPath);
        if (!catalog.IsSuccess)
        {
            return new LoadedStandards(context.RepositoryPath, context.DocumentationRoot, [], [], [], [], catalog.Errors);
        }

        var documents = new List<ParsedStandard>();
        var warnings = new List<string>();
        var errors = new List<string>();
        foreach (var entry in catalog.Catalog!.Documents
                     .Where(item => item.Type.Equals("standard", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsInsideStandards(entry.Path, context.DocumentationRoot))
            {
                errors.Add($"Cataloged standard '{entry.Id}' must be located below {context.DocumentationRoot}/standards/: {entry.Path}");
                continue;
            }

            var absolute = Path.GetFullPath(Path.Combine(context.RepositoryPath, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolute))
            {
                errors.Add($"Cataloged standard '{entry.Id}' does not exist: {entry.Path}");
                continue;
            }

            try
            {
                documents.Add(ParseStandard(entry, absolute));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlException or FormatException)
            {
                errors.Add($"Unable to parse standard '{entry.Id}' at {entry.Path}: {exception.Message}");
            }
        }

        var matrixPath = Path.Combine(context.DocumentationPath, ConformanceMatrixPath.Replace('/', Path.DirectorySeparatorChar));
        var conformance = Array.Empty<StandardConformanceEntry>();
        if (!File.Exists(matrixPath))
        {
            errors.Add($"Standards conformance matrix was not found: {context.DocumentationRoot}/{ConformanceMatrixPath}");
        }
        else
        {
            try
            {
                conformance = ParseConformance(File.ReadAllText(matrixPath)).ToArray();
            }
            catch (IOException exception)
            {
                errors.Add($"Unable to read the standards conformance matrix: {exception.Message}");
            }
        }

        return new LoadedStandards(
            context.RepositoryPath,
            context.DocumentationRoot,
            documents.Select(item => item.Definition).ToArray(),
            documents.ToArray(),
            conformance,
            warnings,
            errors);
    }

    private ParsedStandard ParseStandard(DocumentationCatalogEntry entry, string absolutePath)
    {
        var content = File.ReadAllText(absolutePath);
        if (!TryExtractFrontMatter(content, out var yaml, out var body))
        {
            throw new FormatException("YAML front matter is required.");
        }

        var metadata = _deserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? [];
        var definition = new StandardDefinition(
            entry.Id,
            entry.Path,
            Scalar(metadata, "title"),
            FirstNonEmpty(Scalar(metadata, "status"), entry.Status),
            Sequence(metadata, "targets"),
            MergeStacks(metadata),
            Scalar(metadata, "owner"),
            Scalar(metadata, "last_reviewed"),
            Scalar(metadata, "review_cadence"),
            StandardRuleParser.Parse(body).Select(rule => rule.Id).ToArray());
        return new ParsedStandard(
            definition,
            entry.Authority,
            Scalar(metadata, "type"),
            NestedScalar(metadata, "cis", "stable_id"),
            Scalar(metadata, "source_of_truth"),
            HeadingRegex().Matches(body).Select(match => match.Groups[1].Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static void ValidateDocument(
        ParsedStandard document,
        ICollection<StandardsValidationDiagnostic> diagnostics)
    {
        var item = document.Definition;
        void Required(string value, string field) {
            if (string.IsNullOrWhiteSpace(value)) diagnostics.Add(Diagnostic(
                "CIS-STD-META-001", "error", item.Path, $"Required standard metadata '{field}' is missing.",
                $"Add '{field}' to the YAML front matter."));
        }

        Required(item.Title, "title"); Required(item.Owner, "owner"); Required(item.LastReviewed, "last_reviewed");
        Required(item.ReviewCadence, "review_cadence"); Required(document.SourceOfTruth, "source_of_truth");
        if (!document.Type.Equals("standard", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Diagnostic("CIS-STD-META-002", "error", item.Path, "Front matter type must be 'standard'.", "Set 'type: standard'."));
        if (!document.Authority.Equals("canonical", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Diagnostic("CIS-STD-META-003", "error", item.Path, "A governed standard must be canonical in the documentation catalog.", "Set the catalog authority to 'canonical'."));
        if (!string.Equals(document.StableId, item.Id, StringComparison.Ordinal))
            diagnostics.Add(Diagnostic("CIS-STD-META-004", "error", item.Path, $"cis.stable_id '{document.StableId}' does not match catalog ID '{item.Id}'.", "Align cis.stable_id with the immutable catalog ID."));
        if (!AllowedStatuses.Contains(item.Status))
            diagnostics.Add(Diagnostic("CIS-STD-META-005", "error", item.Path, $"Unsupported lifecycle status '{item.Status}'.", "Use Draft, Active, Deprecated, or Archived."));
        if (item.Targets.Count == 0)
            diagnostics.Add(Diagnostic("CIS-STD-META-006", "error", item.Path, "At least one governed target is required.", "Add a non-empty targets list."));

        foreach (var heading in new[] { "Purpose", "Scope", "Normative language", "Rules", "Verification", "Exceptions" })
        {
            if (!document.Headings.Contains(heading)) diagnostics.Add(Diagnostic(
                "CIS-STD-DOC-001", "error", item.Path, $"Required section '{heading}' is missing.",
                $"Add a '## {heading}' section."));
        }

        if ((item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
             || item.Status.Equals("draft", StringComparison.OrdinalIgnoreCase)) && item.RuleIds.Count == 0)
            diagnostics.Add(Diagnostic("CIS-STD-RULE-001", "error", item.Path, "The standard declares no stable rule IDs.", "Add repository-unique rule IDs such as STD-DOC-001 to normative rules."));
    }

    private static void ValidateConformance(LoadedStandards loaded, ICollection<StandardsValidationDiagnostic> diagnostics)
    {
        var standardsById = loaded.Standards.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var allRules = loaded.Standards.SelectMany(item => item.RuleIds.Select(rule => (item.Id, Rule: rule))).ToArray();
        foreach (var entry in loaded.Conformance)
        {
            if (!standardsById.ContainsKey(entry.StandardId))
                diagnostics.Add(Diagnostic("CIS-STD-CONF-001", "error", ConformanceMatrixPath,
                    $"Conformance row references unknown standard '{entry.StandardId}'.", "Use a cataloged standard ID."));
            else if (!allRules.Any(item => item.Id.Equals(entry.StandardId, StringComparison.OrdinalIgnoreCase)
                                           && item.Rule.Equals(entry.RuleId, StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(Diagnostic("CIS-STD-CONF-002", "error", ConformanceMatrixPath,
                    $"Conformance row references unknown rule '{entry.RuleId}' in '{entry.StandardId}'.", "Use a stable rule ID declared by that standard."));
            if (!AllowedEnforcement.Contains(entry.Enforcement))
                diagnostics.Add(Diagnostic("CIS-STD-CONF-003", "error", ConformanceMatrixPath,
                    $"Unsupported enforcement state '{entry.Enforcement}'.", "Use deterministic, architecture-test, manual-review, advisory-model, or not-mapped."));
            if (entry.Enforcement.Equals("advisory-model", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(Diagnostic("CIS-STD-CONF-004", "warning", ConformanceMatrixPath,
                    $"Rule '{entry.RuleId}' has advisory model enforcement only.", "Add deterministic evidence or retain explicit human review; model output cannot prove a breach."));
            if (entry.Enforcement.Equals("not-mapped", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(Diagnostic("CIS-STD-CONF-005", "warning", ConformanceMatrixPath,
                    $"Rule '{entry.RuleId}' has no enforcement mapping.", "Choose deterministic, architecture-test, manual-review, or advisory-model enforcement."));
        }

        foreach (var rule in allRules.Where(rule => standardsById[rule.Id].Status.Equals("active", StringComparison.OrdinalIgnoreCase)))
        {
            if (!loaded.Conformance.Any(entry => entry.StandardId.Equals(rule.Id, StringComparison.OrdinalIgnoreCase)
                                                 && entry.RuleId.Equals(rule.Rule, StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(Diagnostic("CIS-STD-CONF-006", "error", ConformanceMatrixPath,
                    $"Active rule '{rule.Rule}' in '{rule.Id}' has no conformance row.", "Add a conformance row, using manual-review when no automation exists yet."));
        }
    }

    private static IEnumerable<StandardConformanceEntry> ParseConformance(string content)
    {
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|') || line.Contains("|---", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray();
            if (cells.Length < 7 || cells[0].Equals("Standard ID", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new StandardConformanceEntry(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6]);
        }
    }

    private static bool MatchesAny(IReadOnlyList<string> declared, IReadOnlyList<string>? requested)
        => requested is null || requested.Count == 0 || requested.Any(value => declared.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static bool MatchesStack(IReadOnlyList<string> declared, IReadOnlyList<string>? requested)
        => requested is null || requested.Count == 0 || declared.Count == 0
           || declared.Contains("all", StringComparer.OrdinalIgnoreCase)
           || declared.Contains("generic", StringComparer.OrdinalIgnoreCase)
           || requested.Any(value => declared.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static bool IsInsideStandards(string path, string documentationRoot)
        => path.Replace('\\', '/').StartsWith(documentationRoot.TrimEnd('/') + "/standards/", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractFrontMatter(string content, out string yaml, out string body)
    {
        yaml = string.Empty; body = content;
        using var reader = new StringReader(content);
        if (!string.Equals(reader.ReadLine()?.TrimStart('\uFEFF'), "---", StringComparison.Ordinal)) return false;
        var lines = new List<string>(); string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line == "---") { yaml = string.Join(Environment.NewLine, lines); body = reader.ReadToEnd(); return true; }
            lines.Add(line);
        }
        return false;
    }

    private static string Scalar(IReadOnlyDictionary<object, object?> metadata, string key)
        => metadata.FirstOrDefault(item => item.Key.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true).Value?.ToString()?.Trim() ?? string.Empty;

    private static string NestedScalar(IReadOnlyDictionary<object, object?> metadata, string parent, string child)
    {
        var value = metadata.FirstOrDefault(item => item.Key.ToString()?.Equals(parent, StringComparison.OrdinalIgnoreCase) == true).Value;
        return value is IReadOnlyDictionary<object, object?> nested ? Scalar(nested, child) : string.Empty;
    }

    private static IReadOnlyList<string> Sequence(IReadOnlyDictionary<object, object?> metadata, string key)
    {
        var value = metadata.FirstOrDefault(item => item.Key.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true).Value;
        return value switch
        {
            IEnumerable<object> sequence => sequence.Select(item => item?.ToString()?.Trim() ?? string.Empty).Where(item => item.Length > 0).ToArray(),
            string scalar when !string.IsNullOrWhiteSpace(scalar) => [scalar.Trim()],
            _ => [],
        };
    }

    private static IReadOnlyList<string> MergeStacks(IReadOnlyDictionary<object, object?> metadata)
        => Sequence(metadata, "stacks").Concat(Sequence(metadata, "stack")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static StandardsValidationDiagnostic Diagnostic(string code, string severity, string path, string message, string remediation)
        => new(code, severity, path, message, remediation);

    [GeneratedRegex(@"(?m)^##\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    private sealed record ParsedStandard(
        StandardDefinition Definition,
        string Authority,
        string Type,
        string StableId,
        string SourceOfTruth,
        IReadOnlySet<string> Headings);

    private sealed record LoadedStandards(
        string? RepositoryPath,
        string? DocumentationRoot,
        IReadOnlyList<StandardDefinition> Standards,
        IReadOnlyList<ParsedStandard> Documents,
        IReadOnlyList<StandardConformanceEntry> Conformance,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors);
}
