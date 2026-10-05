using System.Text.RegularExpressions;

namespace Cis.Modules.Brd;

public sealed partial class BrdService
{
    private static bool IsSupportingDocument(string path, string content)
    {
        var type = ReadFrontMatter(content, "type")?.Trim('\'', '"');
        return type is "document-card" or "index-card" or "template" or "issue" or "implementation-plan"
            || path.EndsWith(".card.md", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(path, @"(?i)(?:^|/)(?:[^/]*issue[-_]packs?|issues)(?:/|$)",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    // Recognize common imported layouts without rewriting the author's document or
    // treating merely related topics as proof that a required section is complete.
    internal static string ExtractRecognizedSection(string content, string heading)
    {
        var bounds = FindSectionBounds(content, heading);
        return bounds is { } section ? content[section.Start..section.End].Trim() : string.Empty;
    }

    private static (int Start, int End)? FindSectionBounds(string content, string heading)
    {
        string[] aliases = heading switch
        {
            "Executive summary" => ["Overview", "Document intent"],
            "Business outcomes" => ["Business objectives", "Product objectives", "Platform objectives"],
            "Scope" => ["Product scope", "Project scope", "Business scope"],
            "Stakeholders and actors" => ["Stakeholders", "Actors and roles"],
            "Business capabilities and processes" => ["Business capabilities", "Business processes",
                "Shared platform capabilities and service product capabilities"],
            "Functional requirements" => ["Business functional requirements"],
            "Quality, regulatory, and operational requirements" => ["Non-functional requirements",
                "Quality requirements"],
            "Constraints and assumptions" => ["Assumptions and constraints"],
            "Success measures" => ["Success criteria", "Success metrics"],
            "Traceability" => ["Requirements traceability"],
            "Open questions" => ["Outstanding questions"],
            _ => [],
        };
        var lines = content.Split('\n');
        var headings = new List<(int Offset, int BodyOffset, int Level, string Title)>();
        char fence = '\0';
        var fenceLength = 0;
        var offset = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var lineOffset = offset;
            offset = Math.Min(content.Length, offset + lines[index].Length + 1);
            var line = lines[index].TrimStart();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                var length = line.TakeWhile(character => character == line[0]).Count();
                if (fence == '\0') { fence = line[0]; fenceLength = length; }
                else if (line[0] == fence && length >= fenceLength) fence = '\0';
                continue;
            }
            if (fence != '\0') continue;
            var match = Regex.Match(line, @"^(#{1,6})\s+(.+?)\s*#*\s*$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success) continue;
            var title = Regex.Replace(match.Groups[2].Value, @"^\d+(?:\.\d+)*[.)]?\s+", string.Empty,
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            headings.Add((lineOffset, offset, match.Groups[1].Length, title.Trim()));
        }
        // Prefer the canonical heading even if a document also has an overview.
        foreach (var name in new[] { heading }.Concat(aliases))
        {
            var found = headings.FindIndex(item => item.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found < 0) continue;
            var start = headings[found];
            var end = headings.Skip(found + 1).FirstOrDefault(item => item.Level <= start.Level);
            return (start.BodyOffset, end == default ? content.Length : end.Offset);
        }
        return null;
    }
}
