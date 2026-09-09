using System.Net;
using System.Text.RegularExpressions;

namespace Cis.Abstractions;

/// <summary>Hides controller-owned provenance without changing its recorded values.</summary>
public static class CisBrdPresentation
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static string HideManagedEvidence(string content)
    {
        foreach (var name in new[] { "baseline", "sources", "feature-traceability" })
        {
            var start = $"<!-- cis:{name}:start -->";
            var end = $"<!-- cis:{name}:end -->";
            var startIndex = content.IndexOf(start, StringComparison.Ordinal);
            var endIndex = content.IndexOf(end, startIndex < 0 ? 0 : startIndex + start.Length,
                StringComparison.Ordinal);
            if (startIndex < 0 || endIndex < 0) continue;
            var bodyStart = startIndex + start.Length;
            var body = content[bodyStart..endIndex];
            if (body.TrimStart().StartsWith("<!-- cis:brd-evidence\n", StringComparison.Ordinal)
                || body.TrimStart().StartsWith("<!-- cis:brd-evidence\r\n", StringComparison.Ordinal)) continue;
            var leading = body.Length - body.TrimStart().Length;
            var trailing = body.TrimEnd().Length;
            if (trailing <= leading) continue;
            content = content[..bodyStart] + body[..leading] + Comment(body[leading..trailing])
                + body[trailing..] + content[endIndex..];
        }

        // Only the standard metadata headings immediately preceding their managed blocks.
        return Regex.Replace(content,
            @"(?m)^## (?:CIS participant baseline|Source assessment)\r?\n(?=\s*<!-- cis:(?:baseline|sources):start -->)",
            match => Comment(match.Value.TrimEnd('\r', '\n'))
                + (match.Value.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"),
            RegexOptions.CultureInvariant, Timeout);
    }

    public static string RestoreManagedEvidence(string content)
        => Regex.Replace(content, @"<!-- cis:brd-evidence\r?\n(?<body>.*?)\r?\n-->",
            match => WebUtility.HtmlDecode(match.Groups["body"].Value),
            RegexOptions.Singleline | RegexOptions.CultureInvariant, Timeout);

    public static bool HasVisibleLinksOrSourceIds(string content)
    {
        var visible = Regex.Replace(content, @"\A---\r?\n.*?\r?\n---(?:\r?\n|\z)",
            string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant, Timeout);
        visible = Regex.Replace(visible, @"<!--.*?-->", string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant, Timeout);
        return Regex.IsMatch(visible,
            @"\[[^\]\r\n]+\]\s*(?:\(|\[[^\]\r\n]*\])|^\s{0,3}\[[^\]\r\n]+\]:|<a\b|\b[a-z][a-z0-9+.-]*://|mailto:|www\.|[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}|\bBRD-SRC-[A-Za-z0-9_-]+",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, Timeout);
    }

    private static string Comment(string content)
        // Encode delimiters as well as existing entities: arbitrary source rationale must
        // never terminate the comment, and restoring it must recover the exact evidence.
        => "<!-- cis:brd-evidence\n" + WebUtility.HtmlEncode(content) + "\n-->";
}
