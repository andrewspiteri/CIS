using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Graph;

internal static class MarkdownGraphExtractor
{
    private static readonly IReadOnlyDictionary<string, ReferenceFamilyDefinition> ReferenceFamilies =
        CreateReferenceFamilies();

    private static readonly Regex GovernedByPattern = new(
        "\\bGoverned by\\s+`(?<id>[^`]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ExtractedComponent> ExtractComponents(string content)
    {
        var components = new List<ExtractedComponent>();
        ComponentBuilder? current = null;
        var readingEvidence = false;
        foreach (var rawLine in SplitLines(content))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                AddCurrent(components, current);
                current = new ComponentBuilder(line[4..].Trim());
                readingEvidence = false;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.Equals("- Evidence:", StringComparison.OrdinalIgnoreCase))
            {
                readingEvidence = true;
                continue;
            }

            if (readingEvidence && line.StartsWith("  - ", StringComparison.Ordinal))
            {
                current.Evidence.Add(line[4..].Trim());
                continue;
            }

            readingEvidence = false;
            if (TryReadBullet(line, "Root", out var root))
            {
                current.Root = TrimCode(root);
            }
            else if (TryReadBullet(line, "Languages", out var languages))
            {
                current.Languages = SplitValues(languages);
            }
            else if (TryReadBullet(line, "Frameworks", out var frameworks))
            {
                current.Frameworks = SplitValues(frameworks);
            }
            else if (TryReadBullet(line, "Roles", out var roles))
            {
                current.Roles = SplitValues(roles);
            }
            else if (TryReadBullet(line, "Capabilities", out var capabilities))
            {
                current.Capabilities = SplitValues(capabilities);
            }
            else if (TryReadBullet(line, "Confidence", out var confidence))
            {
                current.Confidence = confidence.Trim().ToLowerInvariant();
            }
        }

        AddCurrent(components, current);
        return components
            .Where(component => !string.IsNullOrWhiteSpace(component.Id))
            .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ExtractedReferenceTable? ExtractReferenceTable(string path, string content)
    {
        var slug = Path.GetFileNameWithoutExtension(path);
        if (!ReferenceFamilies.TryGetValue(slug, out var family))
        {
            return null;
        }

        var lines = SplitLines(content);
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!IsTableLine(lines[index]) || !IsSeparatorRow(ParseTableRow(lines[index + 1])))
            {
                continue;
            }

            var headers = ParseTableRow(lines[index]);
            var rows = new List<IReadOnlyDictionary<string, string>>();
            for (var rowIndex = index + 2; rowIndex < lines.Length && IsTableLine(lines[rowIndex]); rowIndex++)
            {
                var values = ParseTableRow(lines[rowIndex]);
                if (values.Count == 0)
                {
                    continue;
                }

                var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (var cell = 0; cell < headers.Count; cell++)
                {
                    row[headers[cell]] = cell < values.Count ? values[cell] : string.Empty;
                }

                rows.Add(row);
            }

            return new ExtractedReferenceTable(family, headers, rows);
        }

        return new ExtractedReferenceTable(family, [], []);
    }

    public static string? ExtractGoverningDocumentId(string content)
    {
        var match = GovernedByPattern.Match(content);
        return match.Success ? match.Groups["id"].Value.Trim() : null;
    }

    public static IReadOnlyList<string> CreateIdentityParts(
        ReferenceFamilyDefinition family,
        IReadOnlyDictionary<string, string> row)
        => (string.IsNullOrWhiteSpace(GetValue(row, "Repository")) ? [] : new[] { GetValue(row, "Repository") })
            .Concat(family.IdentityFields
            .Select(field => GetValue(row, field))
            .Select(value => value.Trim()))
            .ToArray();

    public static string CreateReferenceLocalId(
        ReferenceFamilyDefinition family,
        IReadOnlyList<string> identityParts)
        => family.Subtype + "/" + string.Join('/', identityParts.Select(Uri.EscapeDataString));

    public static string CreateReferenceLabel(
        ReferenceFamilyDefinition family,
        IReadOnlyDictionary<string, string> row,
        IReadOnlyList<string> identityParts)
    {
        var values = family.LabelFields
            .Select(field => GetValue(row, field))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (family.Subtype == "api-operation" && values.Length == 2)
        {
            return values[0] + " " + values[1];
        }

        return values.Length > 0 ? string.Join(" / ", values) : string.Join(" / ", identityParts);
    }

    public static IReadOnlyList<string> FindOwners(
        ReferenceFamilyDefinition family,
        IReadOnlyDictionary<string, string> row)
    {
        foreach (var field in family.OwnerFields)
        {
            var value = GetValue(row, field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return [];
    }

    public static string GetValue(IReadOnlyDictionary<string, string> row, string field)
        => row.FirstOrDefault(pair => string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase)).Value
            ?? string.Empty;

    private static IReadOnlyDictionary<string, ReferenceFamilyDefinition> CreateReferenceFamilies()
    {
        var families = new[]
        {
            new ReferenceFamilyDefinition("api-dictionary", "api-operation", ["API ID"], ["contract"], ["Host"], ["Method", "Path"]),
            new ReferenceFamilyDefinition("command-dictionary", "command", ["Command ID"], ["contract", "domain-behaviour"], ["Module"], ["Command name"]),
            new ReferenceFamilyDefinition("event-dictionary", "event", ["Event ID"], ["contract", "domain-behaviour"], ["Producer"], ["Event name"]),
            new ReferenceFamilyDefinition("workflow-state-dictionary", "workflow-state", ["Workflow ID", "State"], ["domain-behaviour"], [], ["Workflow", "State"]),
            new ReferenceFamilyDefinition("business-invariant-catalogue", "business-invariant", ["Invariant ID"], ["domain-behaviour", "rule"], [], ["Rule"]),
            new ReferenceFamilyDefinition("projection-dictionary", "projection", ["Projection ID"], ["domain-behaviour", "data"], ["Producer / refresh"], ["Projection / read model"]),
            new ReferenceFamilyDefinition("permissions-dictionary", "permission", ["Permission code"], ["contract", "security"], [], ["Permission name"]),
            new ReferenceFamilyDefinition("configuration-dictionary", "configuration-key", ["Owner", "Path"], ["contract", "configuration"], ["Owner"], ["Name", "Path"]),
            new ReferenceFamilyDefinition("package-catalogue", "package", ["Component", "Package"], ["dependency"], ["Component"], ["Package"]),
            new ReferenceFamilyDefinition("screen-route-map", "screen-route", ["Component", "Platform", "Screen or route"], ["navigation", "contract"], ["Component"], ["Screen or route"]),
            new ReferenceFamilyDefinition("problem-details-catalogue", "problem", ["Problem ID"], ["contract"], ["Raised by"], ["Problem type / reason"]),
            new ReferenceFamilyDefinition("module-ownership-map", "module-boundary", ["Module ID"], ["ownership"], ["Module ID"], ["Module / context"]),
            new ReferenceFamilyDefinition("data-dictionary", "entity-field", ["Entity", "Field"], ["contract", "data"], ["Owner"], ["Entity", "Field"]),
            new ReferenceFamilyDefinition("erd", "entity-relationship", ["Entity", "Relationship", "Target"], ["data"], ["Owner"], ["Entity", "Relationship", "Target"]),
            new ReferenceFamilyDefinition("traceability-matrix", "trace-link", ["Trace ID"], ["traceability"], ["Component"], ["Requirement / source"]),
        };
        return families.ToDictionary(family => family.Slug, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTableLine(string line)
        => line.TrimStart().StartsWith('|') && line.TrimEnd().EndsWith('|');

    private static bool IsSeparatorRow(IReadOnlyList<string> cells)
        => cells.Count > 0 && cells.All(cell => Regex.IsMatch(cell, "^:?-{3,}:?$"));

    private static IReadOnlyList<string> ParseTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|')
        {
            return [];
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        var body = trimmed[1..^1];
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character == '\\' && index + 1 < body.Length && body[index + 1] == '|')
            {
                current.Append('|');
                index++;
                continue;
            }

            if (character == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static string[] SplitLines(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static bool TryReadBullet(string line, string name, out string value)
    {
        var prefix = $"- {name}:";
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string TrimCode(string value)
        => value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;

    private static IReadOnlyList<string> SplitValues(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void AddCurrent(ICollection<ExtractedComponent> components, ComponentBuilder? current)
    {
        if (current is not null)
        {
            components.Add(current.Build());
        }
    }

    private sealed class ComponentBuilder(string id)
    {
        public string Id { get; } = id;
        public string Root { get; set; } = string.Empty;
        public IReadOnlyList<string> Languages { get; set; } = [];
        public IReadOnlyList<string> Frameworks { get; set; } = [];
        public IReadOnlyList<string> Roles { get; set; } = [];
        public IReadOnlyList<string> Capabilities { get; set; } = [];
        public string Confidence { get; set; } = "unknown";
        public List<string> Evidence { get; } = [];

        public ExtractedComponent Build()
            => new(Id, Root, Languages, Frameworks, Roles, Capabilities, Confidence, Evidence.ToArray());
    }
}

internal sealed record ExtractedComponent(
    string Id,
    string Root,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities,
    string Confidence,
    IReadOnlyList<string> Evidence);

internal sealed record ReferenceFamilyDefinition(
    string Slug,
    string Subtype,
    IReadOnlyList<string> IdentityFields,
    IReadOnlyList<string> Facets,
    IReadOnlyList<string> OwnerFields,
    IReadOnlyList<string> LabelFields);

internal sealed record ExtractedReferenceTable(
    ReferenceFamilyDefinition Family,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);
