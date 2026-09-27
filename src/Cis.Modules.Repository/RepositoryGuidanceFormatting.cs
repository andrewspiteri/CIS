using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

internal static class RepositoryGuidanceFormatting
{
    private static readonly Regex Ordered = new(@"^(\s*)(\d+)[.)]\s+(.*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static string Clean(string content)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>();
        var list = new List<(int Index, string Indent, string Text)>();
        char fence = '\0'; var fenceLength = 0; var frontMatter = false;
        void FinishList()
        {
            var counters = new Dictionary<int, int>();
            foreach (var item in list)
            {
                foreach (var deeper in counters.Keys.Where(depth => depth > item.Indent.Length).ToArray()) counters.Remove(deeper);
                var number = counters.GetValueOrDefault(item.Indent.Length) + 1;
                counters[item.Indent.Length] = number;
                output[item.Index] = item.Indent + number + ". " + item.Text;
            }
            list.Clear();
        }
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index]; var trimmed = line.Trim();
            if (index == 0 && trimmed == "---") { frontMatter = true; output.Add(line); continue; }
            if (frontMatter) { output.Add(line); if (trimmed == "---") frontMatter = false; continue; }
            if (fence != '\0')
            {
                output.Add(line);
                if (trimmed.Length >= fenceLength && trimmed.All(character => character == fence)) fence = '\0';
                continue;
            }
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                // A fenced example indented inside a list item belongs to that
                // item. It must not split the procedure or turn steps into bullets.
                if (list.Count == 0 || line.Length - line.TrimStart().Length <= list[0].Indent.Length) FinishList();
                fence = trimmed[0]; fenceLength = trimmed.TakeWhile(character => character == fence).Count();
                output.Add(line); continue;
            }
            var ordered = Ordered.Match(line);
            if (ordered.Success)
                list.Add((output.Count, ordered.Groups[1].Value, ordered.Groups[3].Value));
            else if (trimmed.Length > 0 && (list.Count == 0 || line.Length - line.TrimStart().Length <= list[0].Indent.Length)) FinishList();
            if (trimmed.Length == 0 && (output.Count == 0 || output[^1].Length == 0)) continue;
            output.Add(trimmed.Length == 0 ? "" : line);
        }
        FinishList();
        return string.Join(newline, output).TrimEnd('\r', '\n') + newline;
    }
}
