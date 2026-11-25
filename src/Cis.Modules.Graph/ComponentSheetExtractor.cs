using System.Text.RegularExpressions;

namespace Cis.Modules.Graph;

/// <summary>Reads explicit catalogue and path declarations, never ownership inferred from prose.</summary>
internal static class ComponentSheetExtractor
{
    public static ComponentSheet Read(string content)
    {
        var components = new List<ComponentSheetRow>();
        var locations = new List<ComponentPathRow>();
        var issues = new List<string>();
        var section = string.Empty;
        string? fence = null;
        string[]? headers = null;
        var lines = content.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                var marker = line[..3];
                if (fence is null) fence = marker;
                else if (fence == marker) fence = null;
                continue;
            }
            if (fence is not null) continue;
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                section = line.TrimStart('#').Trim();
                headers = null;
                continue;
            }
            if (!line.StartsWith('|')) continue;
            var cells = Cells(line);
            if (cells.All(cell => Regex.IsMatch(cell, "^:?-+:?$", RegexOptions.CultureInvariant))) continue;
            if (headers is null)
            {
                headers = cells;
                if ((section.Equals("Component catalogue", StringComparison.OrdinalIgnoreCase)
                        || section.Equals("Implementation and verification locations", StringComparison.OrdinalIgnoreCase))
                    && !headers.Contains("Component ID", StringComparer.OrdinalIgnoreCase))
                    issues.Add($"line {index + 1}: table requires a Component ID header");
                continue;
            }
            string Cell(string name) => Array.FindIndex(headers, header => header.Equals(name, StringComparison.OrdinalIgnoreCase)) is var column
                && column >= 0 && column < cells.Length ? cells[column] : string.Empty;
            if (section.Equals("Component catalogue", StringComparison.OrdinalIgnoreCase))
            {
                var id = Cell("Component ID");
                if (id.Length == 0) { issues.Add($"line {index + 1}: component ID is missing"); continue; }
                components.Add(new(id, Cell("Name"), Cell("Classification"), Cell("Responsibility"),
                    Cell("Owns"), Cell("Must not own"), Cell("BRD authority"), index + 1));
            }
            else if (section.Equals("Implementation and verification locations", StringComparison.OrdinalIgnoreCase))
            {
                var id = Cell("Component ID");
                var relation = Cell("Relationship").ToLowerInvariant();
                var path = Cell("Path");
                if (id.Length == 0 || relation is not ("owns" or "verified-by") || !TryNormalizePath(path, out var normalized))
                {
                    issues.Add($"line {index + 1}: expected a component ID, owns/verified-by and one repository-relative path");
                    continue;
                }
                locations.Add(new(id, relation, normalized, index + 1));
            }
        }
        return new(components, locations, issues);
    }

    internal static bool TryNormalizePath(string value, out string normalized)
    {
        normalized = value.Trim().Trim('`').Replace('\\', '/').TrimEnd('/');
        if (normalized == ".") { normalized = string.Empty; return true; }
        return normalized.Length > 0 && !normalized.StartsWith('/') && !normalized.Contains(':')
            && !normalized.Contains('*') && !normalized.Contains('?')
            && normalized.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));
    }

    private static string[] Cells(string line)
        => Regex.Split(line.Trim().Trim('|'), "(?<!\\\\)\\|", RegexOptions.CultureInvariant)
            .Select(cell => cell.Trim().Trim('`').Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
}

internal sealed record ComponentSheet(IReadOnlyList<ComponentSheetRow> Components,
    IReadOnlyList<ComponentPathRow> Locations, IReadOnlyList<string> Issues);

internal sealed record ComponentSheetRow(string Id, string Name, string Classification,
    string Responsibility, string Owns, string Excludes, string RequirementIds, int Line);

internal sealed record ComponentPathRow(string ComponentId, string Relationship, string Path, int Line);
