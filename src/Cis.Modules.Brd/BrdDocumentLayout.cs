using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cis.Modules.Brd;

public sealed record BrdSectionSelection(string Heading, bool IncludeChildren = true);
public sealed record BrdLayoutDefinition(int SchemaVersion, Dictionary<string, BrdSectionSelection[]> Sections);

// A document-owned, digest-covered navigation map. It supplies no business content or decisions.
internal static class BrdDocumentLayout
{
    internal const string Key = "cis_brd_layout";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
    };
    internal sealed record Heading(string Title, int Start, int Body, int Level);
    internal sealed record Range(int Start, int End);

    internal static BrdLayoutDefinition? Read(string content)
    {
        var header = Header(content);
        if (!header.Success && Regex.IsMatch(content, @"(?m)^cis_brd_layout\s*:", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("Layout metadata is present without readable frontmatter.");
        var rows = Regex.Matches(header.Value, @"(?m)^[ \t]*cis_brd_layout[ \t]*:([^\r\n]*)", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (rows.Count == 0) return null;
        if (rows.Count != 1) throw new InvalidDataException("The BRD has duplicate layout metadata.");
        if (!rows[0].Value.StartsWith(Key + ":", StringComparison.Ordinal))
            throw new InvalidDataException("Layout metadata must be a top-level cis_brd_layout field.");
        return Parse(rows[0].Groups[1].Value.Trim());
    }

    internal static BrdLayoutDefinition Parse(string json)
    {
        if (json.Length > 32_768) throw new InvalidDataException("BRD layout metadata exceeds 32 KiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        RejectDuplicateProperties(document.RootElement);
        var layout = document.RootElement.Deserialize<BrdLayoutDefinition>(Json);
        if (layout is null || layout.SchemaVersion != 1 || layout.Sections is not { Count: > 0 and <= 10 })
            throw new InvalidDataException("Use BRD layout schemaVersion 1 with one to ten section roles.");
        foreach (var (role, selections) in layout.Sections)
        {
            // Answer editing requires the canonical question-table location; do not map it to a decision register.
            if (!BrdService.RequiredSections.Contains(role, StringComparer.Ordinal) || role == "Open questions")
                throw new InvalidDataException($"Unsupported layout role: {role}. Open questions retains its canonical heading and answer workflow.");
            if (selections is not { Length: > 0 and <= 32 } || selections.Any(item => item is null
                || string.IsNullOrWhiteSpace(item.Heading) || item.Heading.Length > 300 || item.Heading.Contains('\n') || item.Heading.Contains('\r')))
                throw new InvalidDataException($"Invalid heading selections for {role}.");
        }
        return layout;
    }

    internal static IReadOnlyList<string> Validate(string content)
    {
        try
        {
            if (Read(content) is { } layout)
                foreach (var selections in layout.Sections.Values) Resolve(content, selections);
            return [];
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        { return ["Invalid BRD layout: " + error.Message]; }
    }

    internal static string? Extract(string content, string role)
    {
        var layout = Read(content);
        if (layout is null || !layout.Sections.TryGetValue(role, out var selections)) return null;
        // Separate selections with a heading so a trailing requirement cannot absorb a different section's introduction.
        var visible = Visible(content);
        return string.Join("\n\n###### CIS section boundary\n\n", Resolve(content, selections)
            .Select(range => visible[range.Start..range.End].Trim()));
    }

    internal static string Insert(string content, BrdLayoutDefinition layout)
    {
        var header = Header(content);
        if (!header.Success) throw new InvalidDataException("Reconcile the BRD before mapping its layout.");
        var line = Key + ": " + JsonSerializer.Serialize(layout, Json);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var current = Regex.Matches(header.Value, @"(?m)^cis_brd_layout:[^\r\n]*", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (current.Count > 1) throw new InvalidDataException("The BRD has duplicate layout metadata.");
        if (current.Count == 1)
            return content[..current[0].Index] + line + content[(current[0].Index + current[0].Length)..];
        var closing = header.Value.LastIndexOf("---", StringComparison.Ordinal);
        return content[..closing] + line + newline + content[closing..];
    }

    internal static string Visible(string content)
    {
        // Preserve positions while excluding frontmatter, comments and fenced examples from heading discovery.
        var visible = Regex.Replace(content, @"<!-- cis:(baseline|sources|feature-traceability):start -->.*?<!-- cis:\1:end -->",
            match => Regex.Replace(match.Value, @"[^\r\n]", " "), RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var header = Header(visible);
        if (header.Success) visible = Regex.Replace(header.Value, @"[^\r\n]", " ") + visible[header.Length..];
        visible = Regex.Replace(visible, @"<!--.*?(?:-->|\z)",
            match => Regex.Replace(match.Value, @"[^\r\n]", " "), RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var lines = visible.Split('\n');
        char fence = '\0'; var fenceLength = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].TrimStart();
            var marker = text.StartsWith("```", StringComparison.Ordinal) || text.StartsWith("~~~", StringComparison.Ordinal);
            var masked = fence != '\0' || marker;
            if (marker)
            {
                var length = text.TakeWhile(character => character == text[0]).Count();
                if (fence == '\0') { fence = text[0]; fenceLength = length; }
                else if (text[0] == fence && length >= fenceLength && string.IsNullOrWhiteSpace(text[length..])) fence = '\0';
            }
            if (masked) lines[index] = Regex.Replace(lines[index], @"[^\r]", " ");
        }
        return string.Join('\n', lines);
    }

    private static IReadOnlyList<Range> Resolve(string content, BrdSectionSelection[] selections)
    {
        var headings = new List<Heading>();
        var offset = 0;
        foreach (var line in Visible(content).Split('\n'))
        {
            var start = offset;
            offset = Math.Min(content.Length, offset + line.Length + 1);
            var match = Regex.Match(line, @"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (match.Success) headings.Add(new(match.Groups[2].Value.Trim(), start, offset, match.Groups[1].Length));
        }
        var ranges = new List<Range>();
        foreach (var selection in selections)
        {
            Range range;
            if (selection.Heading == "$introduction")
            {
                var start = Header(content).Length;
                range = new(start, headings.FirstOrDefault()?.Start ?? content.Length);
            }
            else
            {
                var matches = headings.Where(item => item.Title == selection.Heading).ToArray();
                if (matches.Length != 1) throw new InvalidDataException($"Layout heading must occur exactly once: {selection.Heading} (found {matches.Length}).");
                var heading = matches[0];
                var end = headings.FirstOrDefault(item => item.Start > heading.Start && (!selection.IncludeChildren || item.Level <= heading.Level))?.Start ?? content.Length;
                range = new(heading.Body, end);
            }
            if (string.IsNullOrWhiteSpace(Visible(content[range.Start..range.End])))
                throw new InvalidDataException($"Layout heading has no visible content: {selection.Heading}.");
            if (ranges.Any(existing => range.Start < existing.End && existing.Start < range.End))
                throw new InvalidDataException("Layout selections overlap within a section role.");
            ranges.Add(range);
        }
        return ranges.OrderBy(range => range.Start).ToArray();
    }

    private static Match Header(string content) => Regex.Match(content, @"\A\uFEFF?---[ \t]*\r?\n.*?^---[ \t]*\r?$", RegexOptions.Singleline | RegexOptions.Multiline, TimeSpan.FromSeconds(1));

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate layout JSON property: " + property.Name);
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
