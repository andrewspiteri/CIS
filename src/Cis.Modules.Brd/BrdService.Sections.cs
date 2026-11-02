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
        try
        {
            if (BrdDocumentLayout.Extract(content, heading) is { } mapped) return mapped;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException)
        { return string.Empty; } // Validation reports the invalid map; never fall back to a different section.
        var sections = FindRecognizedSectionBounds(content, heading)
            .Select(section => content[section.Start..section.End].Trim())
            .Where(section => !string.IsNullOrWhiteSpace(BrdDocumentLayout.Visible(section))).ToArray();
        if (heading != "Open questions" && sections.Any(section => !IsEmptyScaffoldSection(section)))
            sections = sections.Where(section => !IsEmptyScaffoldSection(section)).ToArray();
        // Answer editing has one question-table location. Ambiguous independent
        // locations are reported by validation, never merged into a competing copy.
        if (heading == "Open questions" && sections.Length > 1) return string.Empty;
        return string.Join("\n\n###### CIS section boundary\n\n", sections);
    }

    private static bool IsEmptyScaffoldSection(string content)
        => Regex.IsMatch(content.Trim(), @"\ATODO: Complete [^\r\n]+ through human review of workspace evidence\.\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static (int Start, int End)? FindSectionBounds(string content, string heading)
    {
        var sections = FindRecognizedSectionBounds(content, heading);
        var populated = sections.FirstOrDefault(section => !string.IsNullOrWhiteSpace(BrdDocumentLayout.Visible(content[section.Start..section.End])));
        if (populated != default) return populated;
        return sections.FirstOrDefault() is var bounds && bounds != default ? bounds : null;
    }

    internal static bool HasAmbiguousQuestionSections(string content)
        => FindRecognizedSectionBounds(content, "Open questions")
            .Count(section => !string.IsNullOrWhiteSpace(BrdDocumentLayout.Visible(content[section.Start..section.End]))) > 1;

    private static IReadOnlyList<(int Start, int End)> FindRecognizedSectionBounds(string content, string heading)
    {
        var aliases = SectionAliases(heading);
        var roleTitles = RequiredSections.SelectMany(role => new[] { role }.Concat(SectionAliases(role)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = BrdDocumentLayout.Visible(content).Split('\n');
        var headings = new List<(int Offset, int BodyOffset, int Level, string Title)>();
        var offset = 0;
        foreach (var line in lines)
        {
            var lineOffset = offset;
            offset = Math.Min(content.Length, offset + line.Length + 1);
            var match = Regex.Match(line.TrimStart(), @"^(#{1,6})\s+(.+?)\s*#*\s*$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success) continue;
            var title = Regex.Replace(match.Groups[2].Value, @"^\d+(?:\.\d+)*[.)]?\s+", string.Empty,
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            headings.Add((lineOffset, offset, match.Groups[1].Length, title.Trim()));
        }
        var ranges = new List<(int Start, int End)>();
        // Keep canonical-first ordering for answer editing. A recognized role
        // starts a new section even when the author uses a deeper heading level.
        foreach (var name in new[] { heading }.Concat(aliases))
        {
            foreach (var found in headings.Select((item, index) => (item, index))
                .Where(pair => pair.item.Title.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                var start = found.item;
                var end = headings.Skip(found.index + 1).FirstOrDefault(item => item.Level <= start.Level || roleTitles.Contains(item.Title));
                var range = (Start: start.BodyOffset, End: end == default ? content.Length : end.Offset);
                if (!ranges.Any(existing => range.Start < existing.End && existing.Start < range.End)) ranges.Add(range);
            }
        }
        return ranges;
    }

    private static string[] SectionAliases(string heading) => heading switch
    {
        "Executive summary" => ["Overview", "Document intent"],
        "Business outcomes" => ["Business objectives", "Product objectives", "Platform objectives"],
        "Scope" => ["Product scope", "Project scope", "Business scope"],
        "Stakeholders and actors" => ["Stakeholders", "Actors and roles"],
        "Business capabilities and processes" => ["Business capabilities", "Business processes",
                "Shared platform capabilities and service product capabilities"],
        "Functional requirements" => ["Business functional requirements", "Requirements", "Product requirements",
                "Business requirements", "User requirements", "Required behavior", "Required behaviour", "Product behavior", "Product behaviour"],
        "Quality, regulatory, and operational requirements" => ["Non-functional requirements",
                "Quality requirements"],
        "Constraints and assumptions" => ["Assumptions and constraints"],
        "Success measures" => ["Success criteria", "Success metrics"],
        "Traceability" => ["Requirements traceability"],
        "Open questions" => ["Outstanding questions"],
        _ => [],
    };
}
