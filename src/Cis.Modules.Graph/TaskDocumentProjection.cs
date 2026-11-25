using System.Text.RegularExpressions;

namespace Cis.Modules.Graph;

/// <summary>Context currency only; canonical task and native material digests retain their own rules.</summary>
internal static class TaskDocumentProjection
{
    internal static string? Read(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != "---") return null;
        var end = Array.FindIndex(lines, 1, line => line == "---");
        if (end < 0 || !lines.Take(end).Any(line => Regex.IsMatch(line,
            "^type:\\s*['\"]?agent-task['\"]?\\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))) return null;
        var retained = new List<string>();
        var completion = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (index < end && Regex.IsMatch(line, "^(?:status|task_status):", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)) continue;
            if (index > end && line.TrimStart().StartsWith("## ", StringComparison.Ordinal))
                completion = line.Trim().Equals("## Completion evidence", StringComparison.OrdinalIgnoreCase);
            if (completion) continue;
            retained.Add(index > end ? Regex.Replace(line, @"^(\s*[-*] )\[[ xX]\]", "$1[]", RegexOptions.CultureInvariant) : line);
        }
        return string.Join('\n', retained).TrimEnd();
    }
}
