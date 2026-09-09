using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

internal static class ErdDiagramRenderer
{
    internal const string Start = "<!-- cis:erd-diagrams:start -->";
    internal const string End = "<!-- cis:erd-diagrams:end -->";
    internal sealed record Asset(string RelativePath, string Content);
    internal sealed record Result(string Markdown, IReadOnlyList<Asset> Assets);
    private sealed record Relationship(string Repository, string Owner, string Entity, string Property, string Target, string Cardinality, string Status);

    public static Result Render(string markdown, string repositoryId)
    {
        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var content = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = content.IndexOf(Start, StringComparison.Ordinal);
        var end = content.IndexOf(End, StringComparison.Ordinal);
        if ((start < 0) != (end < 0) || end >= 0 && end < start)
            throw new InvalidDataException("The generated ERD block is incomplete; restore its start/end markers before preparation.");
        if (start >= 0) content = content.Remove(start, end + End.Length - start).TrimEnd() + "\n";
        var lines = content.Split('\n');
        var header = Array.FindIndex(lines, line => line.StartsWith('|') && Cells(line).Contains("Relationship", StringComparer.OrdinalIgnoreCase)
            && Cells(line).Contains("Entity", StringComparer.OrdinalIgnoreCase));
        if (header < 0) return new(markdown, []);
        var headers = Cells(lines[header]);
        var relationships = new List<Relationship>();
        for (var index = header + 2; index < lines.Length && lines[index].StartsWith('|'); index++)
        {
            var cells = Cells(lines[index]);
            if (cells.Length != headers.Length) continue;
            var row = headers.Select((name, ordinal) => (name, value: cells[ordinal])).ToDictionary(item => item.name, item => item.value, StringComparer.OrdinalIgnoreCase);
            if (!row.TryGetValue("Entity", out var entity) || entity is "" or "TODO") continue;
            relationships.Add(new(row.GetValueOrDefault("Repository", repositoryId), row.GetValueOrDefault("Owner", ""), entity,
                row.GetValueOrDefault("Relationship", "unknown"), Target(row.GetValueOrDefault("Target", "unknown")),
                row.GetValueOrDefault("Cardinality", "unknown"), row.GetValueOrDefault("Status", "Draft")));
        }
        var assets = new List<Asset>();
        var section = new StringBuilder(Start + "\n## Relationship diagrams\n\n");
        if (relationships.Count == 0) section.AppendLine("No entity relationships have been discovered yet. Prepare the contracts page after implementation evidence is available.");
        else
        {
            var groups = relationships.GroupBy(row => (row.Repository, row.Owner, row.Entity))
                .OrderBy(group => group.Key.Repository, StringComparer.Ordinal).ThenBy(group => group.Key.Owner, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Entity, StringComparer.Ordinal).ToArray();
            section.AppendLine($"{relationships.Count} relationships across {groups.Length} entity views. Expand an entity to see its diagram; the first view is open below. SVG images work in ordinary Markdown preview.\n");
            section.AppendLine("Each view shows the declaring entity and its related entities. `N:1`, `1:N`, and `N:N` show declared maximum multiplicity; required participation and database keys are not inferred. Unresolved targets remain explicit. The relationship table remains the source of truth.\n");
            for (var index = 0; index < groups.Length; index++)
            {
                var group = groups[index];
                var rows = group.OrderBy(row => row.Target, StringComparer.Ordinal).ThenBy(row => row.Property, StringComparer.Ordinal).ToArray();
                var svg = Svg(rows);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(svg))).ToLowerInvariant();
                var slug = Regex.Replace(group.Key.Entity, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
                if (slug.Length > 40) slug = slug[..40];
                var asset = new Asset($"erd-diagrams/{slug}-{hash}.svg", svg);
                assets.Add(asset);
                section.AppendLine($"<details{(index == 0 ? " open" : "")}><summary>{Escape(group.Key.Entity)} — {group.Count()} relationship(s) · {Escape(group.Key.Repository)} · {Escape(group.Key.Owner)}</summary>\n");
                section.AppendLine($"![Entity relationship diagram]({asset.RelativePath})\n");
                section.AppendLine($"[Open full-size diagram]({asset.RelativePath})\n\n</details>\n");
            }
        }
        section.AppendLine(End);
        // Append after the canonical table so existing table readers and row locators remain stable.
        var result = content.TrimEnd() + "\n\n" + section.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        return new(newline == "\n" ? result : result.Replace("\n", newline, StringComparison.Ordinal), assets);
    }

    private static string Svg(Relationship[] rows)
    {
        var first = rows[0];
        var targetGroups = rows.GroupBy(row => row.Target == "Unresolved target" ? row.Target + "/" + row.Property : row.Target, StringComparer.Ordinal).ToArray();
        var height = 210 + rows.Length * 76 + targetGroups.Length * 18;
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1180\" height=\"{height}\" viewBox=\"0 0 1180 {height}\" role=\"img\" aria-labelledby=\"title description\">\n");
        svg.AppendLine($"<title id=\"title\">{Escape(first.Entity)} entity relationships</title><desc id=\"description\">{Escape(first.Repository)}. {rows.Length} declared relationships. Required participation is not inferred.</desc>");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" rx=\"12\" fill=\"#f8fafc\"/><g font-family=\"Segoe UI, Arial, sans-serif\" fill=\"#0f172a\">");
        Text(svg, 32, 37, first.Entity + " relationships", 25, "600");
        Text(svg, 32, 66, first.Repository + " · " + first.Owner, 15);
        svg.AppendLine($"<rect x=\"32\" y=\"96\" width=\"355\" height=\"{height - 150}\" rx=\"8\" fill=\"white\" stroke=\"#94a3b8\"/>");
        svg.AppendLine("<path d=\"M40 96 H379 Q387 96 387 104 V154 H32 V104 Q32 96 40 96\" fill=\"#dbeafe\"/>");
        Wrapped(svg, 50, 121, first.Entity, 31, 18);
        var y = 178;
        foreach (var target in targetGroups)
        {
            var targetRows = target.ToArray();
            var blockHeight = targetRows.Length * 76;
            svg.AppendLine($"<rect x=\"820\" y=\"{y - 14}\" width=\"328\" height=\"{blockHeight - 8}\" rx=\"8\" fill=\"#ecfdf5\" stroke=\"#6b9d89\"/>");
            Wrapped(svg, 842, y + blockHeight / 2 - 12, targetRows[0].Target, 29, 18);
            foreach (var row in targetRows)
            {
                Wrapped(svg, 50, y + 8, row.Property, 31, 17);
                Text(svg, 50, y + 52, row.Status, 12);
                svg.AppendLine($"<path d=\"M387 {y + 22} H820 M811 {y + 16} L820 {y + 22} L811 {y + 28}\" fill=\"none\" stroke=\"#64748b\" stroke-width=\"2\"/>");
                var multiplicity = row.Cardinality switch { "ManyToOne" => "N:1", "OneToMany" => "1:N", "ManyToMany" => "N:N", "OneToOne" => "1:1", _ => row.Cardinality };
                Text(svg, 420, y + 12, multiplicity, 16);
                y += 76;
            }
            y += 18;
        }
        Text(svg, 32, height - 20, "Generated from the ERD relationship table. Lifecycle and unresolved details remain subject to review.", 13);
        return svg.AppendLine("</g></svg>").ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void Wrapped(StringBuilder svg, int x, int y, string value, int width, int size)
    {
        for (var offset = 0; offset < value.Length; offset += width)
            Text(svg, x, y + offset / width * 21, value.Substring(offset, Math.Min(width, value.Length - offset)), size);
    }
    private static void Text(StringBuilder svg, int x, int y, string value, int size, string weight = "400")
        => svg.AppendLine($"<text x=\"{x}\" y=\"{y}\" font-size=\"{size}\" font-weight=\"{weight}\">{Escape(value)}</text>");
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string[] Cells(string line) => Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(cell => cell.Trim().Trim('`').Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
    private static string Target(string value)
    {
        value = Regex.Replace(value, "import\\(['\"][^'\"]+['\"]\\)\\.", "");
        value = Regex.Replace(value, @"\s+(?:/|\|)\s+(?:null|undefined)\b", "").Trim();
        value = Regex.Replace(value, @"\[\]$", "");
        if (Regex.Match(value, @"^(?:Array|ReadonlyArray|Relation)<(?<target>.+)>$") is { Success: true } wrapper) value = wrapper.Groups["target"].Value;
        return value is "" or "any" or "unknown" or "object" or "TODO" ? "Unresolved target" : value;
    }
}
