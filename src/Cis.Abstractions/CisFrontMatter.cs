using System.Text.RegularExpressions;

namespace Cis.Abstractions;

/// <summary>Edits scalar metadata only in opening front matter, preserving the Markdown body.</summary>
public static class CisFrontMatter
{
    public static string Set(string content, string key, string value, bool nested = false, bool insert = true)
        => Edit(content, key, value, nested, insert);

    public static string Remove(string content, string key, bool nested = false)
        => Edit(content, key, null, nested, insert: false);

    private static string Edit(string content, string key, string? value, bool nested, bool insert)
    {
        var bounds = Bounds(content);
        if (bounds is null) return content;
        var (start, end) = bounds.Value;
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var header = content[start..end];
        var offset = 0;
        var limit = header.Length;
        if (nested)
        {
            var parent = Regex.Match(header, @"(?m)^cis:[ \t]*\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!parent.Success)
                return insert ? content.Insert(end, "cis:" + newline + "  " + key + ": " + value + newline) : content;
            offset = parent.Index + parent.Length;
            if (offset < header.Length && header[offset] == '\n') offset++;
            var next = Regex.Match(header[offset..], @"(?m)^[^\s#][^\r\n]*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            limit = next.Success ? offset + next.Index : header.Length;
        }
        var region = header[offset..limit];
        var prefix = nested ? "  " : "";
        var pattern = $@"(?m)^{prefix}{Regex.Escape(key)}:[^\r\n]*";
        var match = Regex.Match(region, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            if (value is null)
            {
                var from = start + offset + match.Index;
                var to = from + match.Length;
                if (to < end && content[to] == '\r') to++;
                if (to < end && content[to] == '\n') to++;
                return content.Remove(from, to - from);
            }
            return content[..(start + offset + match.Index)] + prefix + key + ": " + value
                + content[(start + offset + match.Index + match.Length)..];
        }
        return insert ? content.Insert(start + limit, prefix + key + ": " + value + newline) : content;
    }

    public static string? Read(string content, string key, bool nested = false)
    {
        var bounds = Bounds(content);
        if (bounds is null) return null;
        var header = content[bounds.Value.Start..bounds.Value.End];
        if (nested)
        {
            var parent = Regex.Match(header, @"(?m)^cis:[ \t]*\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!parent.Success) return null;
            header = header[(parent.Index + parent.Length)..].TrimStart('\r', '\n');
            var next = Regex.Match(header, @"(?m)^[^\s#][^\r\n]*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (next.Success) header = header[..next.Index];
        }
        var match = Regex.Match(header, $@"(?m)^{(nested ? "  " : "")}{Regex.Escape(key)}:[ \t]*(?<value>[^\r\n]*)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    public static string NormalizeApproval(string content)
    {
        foreach (var key in new[] { "status", "last_reviewed" })
            content = Set(content, key, "<approval-metadata>", insert: false);
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            content = Set(content, key, "<approval-metadata>", nested: true, insert: false);
        return content;
    }

    private static (int Start, int End)? Bounds(string content)
    {
        var opening = Regex.Match(content, @"\A\uFEFF?---[ \t]*\r?\n", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!opening.Success) return null;
        var closing = Regex.Match(content[opening.Length..], @"(?m)^---[ \t]*\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return closing.Success ? (opening.Length, opening.Length + closing.Index) : null;
    }
}
