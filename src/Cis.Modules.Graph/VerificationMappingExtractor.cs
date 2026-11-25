using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Cis.Modules.Graph;

internal static class VerificationMappingExtractor
{
    private static readonly string[] MappingHeaders = ["Criterion", "Task", "Component", "Artifact", "Check", "Result reference"];

    internal static VerificationDeclarations Read(string content)
    {
        var criteria = new List<DeclaredCriterion>();
        var mappings = new List<VerificationMapping>();
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
            if (line.StartsWith('#')) { section = line.TrimStart('#').Trim(); headers = null; continue; }
            if (!line.StartsWith('|')) { headers = null; continue; }
            var cells = Cells(line);
            if (cells.All(cell => Regex.IsMatch(cell, "^:?-+:?$", RegexOptions.CultureInvariant))) continue;
            if (headers is null)
            {
                headers = cells;
                if (section.Equals("Implementation and verification", StringComparison.OrdinalIgnoreCase)
                    && !MappingHeaders.All(header => headers.Contains(header, StringComparer.OrdinalIgnoreCase)))
                    issues.Add($"line {index + 1}: explicit verification table requires Criterion, Task, Component, Artifact, Check and Result reference columns");
                continue;
            }
            string Cell(string name)
            {
                var column = Array.FindIndex(headers, header => header.Equals(name, StringComparison.OrdinalIgnoreCase));
                return column >= 0 && column < cells.Length ? cells[column] : string.Empty;
            }
            if (section.Equals("Implementation and verification", StringComparison.OrdinalIgnoreCase)
                && MappingHeaders.All(header => headers.Contains(header, StringComparer.OrdinalIgnoreCase)))
                mappings.Add(new(Cell("Criterion"), Cell("Task"), Cell("Component"), Cell("Artifact"),
                    Cell("Check"), Cell("Result reference"), index + 1));
            else if (!section.Equals("Implementation and verification", StringComparison.OrdinalIgnoreCase)
                && headers.Contains("Criterion", StringComparer.OrdinalIgnoreCase))
            {
                var id = headers.Contains("ID", StringComparer.OrdinalIgnoreCase) ? Cell("ID") : Cell("Criterion");
                if (Regex.IsMatch(id, "^(?:AC-[0-9]+|P[0-9]+)$", RegexOptions.CultureInvariant))
                    criteria.Add(new(id, Cell("Criterion"), index + 1));
            }
        }
        return new(criteria, mappings) { Issues = issues };
    }

    internal static string SemanticText(string content)
    {
        var declarations = Read(content);
        var builder = new StringBuilder();
        foreach (var criterion in declarations.Criteria)
            builder.AppendLine("criterion@" + criterion.Line.ToString(CultureInfo.InvariantCulture) + ":" + criterion.Id + "=" + criterion.Text);
        foreach (var mapping in declarations.Mappings)
            builder.AppendLine("mapping@" + mapping.Line.ToString(CultureInfo.InvariantCulture) + ":" + string.Join('|', mapping.Criterion, mapping.Task, mapping.Component,
                mapping.Artifact, mapping.Check, mapping.ResultReference));
        var acceptance = false;
        foreach (var raw in content.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                acceptance = line.Equals("## Acceptance criteria", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (acceptance && line.Length > 0)
                builder.AppendLine("acceptance:" + Regex.Replace(line, @"^[-*] \[[ xX]\]\s*", string.Empty, RegexOptions.CultureInvariant));
        }
        return builder.ToString();
    }

    private static string[] Cells(string line)
        => Regex.Split(line.Trim().Trim('|'), "(?<!\\\\)\\|", RegexOptions.CultureInvariant)
            .Select(cell => cell.Trim().Trim('`').Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
}

internal sealed record VerificationDeclarations(IReadOnlyList<DeclaredCriterion> Criteria, IReadOnlyList<VerificationMapping> Mappings)
{
    internal IReadOnlyList<string> Issues { get; init; } = [];
}
internal sealed record DeclaredCriterion(string Id, string Text, int Line);
internal sealed record VerificationMapping(string Criterion, string Task, string Component, string Artifact,
    string Check, string ResultReference, int Line);
