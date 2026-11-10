using System.Text.RegularExpressions;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private sealed record PlanProjection(int Index, int Length, string Content);

    private static string UpdatePlanTaskStatus(string content, string taskId, string status)
    {
        var executionRows = 0;
        var updated = Regex.Replace(content, @"(?m)^\|[^\r\n]*", match =>
        {
            var cells = match.Value.Split('|');
            if (cells.Length < 10 || cells[1].Trim() != taskId) return match.Value;
            executionRows++;
            // Retain the supported historical manifest layouts as well as the current one.
            var statusColumn = cells.Length >= 14 ? 12 : cells.Length >= 13 ? 11 : 8;
            return ReplacePlanCell(cells, statusColumn, status);
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (executionRows != 1)
            throw new InvalidDataException($"Task {taskId} needs one unambiguous existing plan execution row before its status can change.");

        if (FindPlanTaskTable(updated) is { } table)
        {
            var approvalRows = 0;
            var rows = Regex.Replace(table.Content, @"(?m)^\|[^\r\n]*", match =>
            {
                var cells = match.Value.Split('|');
                if (cells.Length != 9 || !Regex.IsMatch(cells[1].Trim(),
                    @"\A\[?" + Regex.Escape(taskId) + @"(?:\s+[—-]\s+|\]|\z)",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return match.Value;
                approvalRows++;
                return ReplacePlanCell(cells, 7, status);
            }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (approvalRows != 1)
                throw new InvalidDataException($"Task {taskId} needs one unambiguous row in its existing approval table before its status can change.");
            updated = ReplacePlanProjection(updated, table, rows);
        }
        return updated;
    }

    private static string ReplacePlanCell(string[] cells, int index, string value)
    {
        var cell = cells[index];
        var leading = cell.Length - cell.TrimStart().Length;
        var trailing = cell.Length - cell.TrimEnd().Length;
        cells[index] = cell[..leading] + value + (trailing == 0 ? "" : cell[^trailing..]);
        return string.Join('|', cells);
    }

    private static string RefreshAdHocPlanContent(string original, string previousGenerated, string generated)
    {
        var manifest = FindPlanManifest(original);
        // The initial NotBuilt dossier has no generated projections to preserve.
        if (manifest is null)
        {
            if (string.IsNullOrEmpty(original) || Regex.IsMatch(original, @"(?m)^status: NotBuilt\r?$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return generated;
            throw new InvalidDataException("The existing ad hoc plan execution manifest is missing; preserve its authored content and reconcile that projection before rebuilding.");
        }
        if (string.Equals(previousGenerated, generated, StringComparison.Ordinal)) return original;
        var taskTable = FindPlanTaskTable(original)
            ?? throw new InvalidDataException("The existing ad hoc plan task table is missing or changed shape; preserve its authored content and reconcile that projection before rebuilding.");
        var generatedTable = FindPlanTaskTable(generated)
            ?? throw new InvalidDataException("The generated ad hoc plan task table is unavailable.");
        var generatedManifest = FindPlanManifest(generated)
            ?? throw new InvalidDataException("The generated ad hoc plan execution manifest is unavailable.");
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string Newlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);
        // Replace from the end so every earlier offset still refers to the original text.
        var projections = new[] { (Current: taskTable, Next: generatedTable), (Current: manifest, Next: generatedManifest) };
        var updated = original;
        foreach (var projection in projections.OrderByDescending(item => item.Current.Index))
            updated = ReplacePlanProjection(updated, projection.Current, Newlines(projection.Next.Content));
        var summarySections = Regex.Matches(updated, @"(?ms)^## Approval summary[ \t]*\r?\n(?<body>.*?)(?=^## |\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (summarySections.Count > 1) throw new InvalidDataException("The plan has ambiguous Approval summary sections.");
        if (summarySections.Count == 1)
        {
            var summary = summarySections[0].Groups["body"];
            var body = summary.Value;
            foreach (var label in new[] { "Requirements covered", "Work items", "Repositories" })
            {
                var pattern = @"(?m)^- " + Regex.Escape(label) + @":[^\r\n]*";
                if (Regex.Matches(body, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Count > 1)
                    throw new InvalidDataException($"The plan has ambiguous generated {label} summary fields.");
                var replacement = Regex.Match(generated, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (replacement.Success)
                    body = Regex.Replace(body, pattern, _ => replacement.Value, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            updated = ReplacePlanProjection(updated, new(summary.Index, summary.Length, summary.Value), body);
        }
        return updated;
    }

    private static PlanProjection? FindPlanManifest(string content)
    {
        const string start = "<!-- cis:execution-manifest";
        const string end = "cis:execution-manifest -->";
        var first = content.IndexOf(start, StringComparison.Ordinal);
        if (first < 0) return null;
        var last = content.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        if (last < 0 || content.IndexOf(start, first + start.Length, StringComparison.Ordinal) >= 0
            || content.IndexOf(end, last + end.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("The existing plan execution manifest is incomplete or ambiguous; preserve it and reconcile its original content before rebuilding.");
        return new(first, last + end.Length - first, content[first..(last + end.Length)]);
    }

    private static PlanProjection? FindPlanTaskTable(string content)
    {
        var sections = Regex.Matches(content, @"(?ms)^## Tasks for approval[ \t]*\r?\n(?<body>.*?)(?=^## |\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (sections.Count == 0) return null;
        if (sections.Count != 1) throw new InvalidDataException("The plan has ambiguous Tasks for approval sections.");
        var body = sections[0].Groups["body"];
        var tables = Regex.Matches(body.Value, @"(?m)^\|[^\r\n]*(?:\r?\n\|[^\r\n]*)*",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var expected = new[] { "Task", "Area", "Complexity", "Depends on", "Requirements", "Repositories", "Status" };
        var matches = tables.Cast<Match>().Where(table => table.Value.Split('\n')[0].TrimEnd('\r').Split('|')
            .Skip(1).SkipLast(1).Select(cell => cell.Trim()).SequenceEqual(expected, StringComparer.Ordinal)).ToArray();
        if (matches.Length == 0) throw new InvalidDataException("The existing task approval table has changed shape; preserve it and reconcile its status projection before writing the plan.");
        if (matches.Length != 1) throw new InvalidDataException("The plan has ambiguous task approval tables.");
        return new(body.Index + matches[0].Index, matches[0].Length, matches[0].Value);
    }

    private static string ReplacePlanProjection(string content, PlanProjection current, string replacement)
        => content[..current.Index] + replacement + content[(current.Index + current.Length)..];
}
