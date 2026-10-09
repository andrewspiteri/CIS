using System.Text.RegularExpressions;

namespace Cis.Modules.Brd;

public sealed partial class BrdService
{
    private static string? ReplaceSectionContent(string content, string heading, string body)
    {
        if (FindSectionBounds(content, heading) is not { } section) return content;
        var end = section.End;
        var original = content[section.Start..end];
        // Imported documents can end with hidden CIS evidence rather than another visible heading.
        // Keep that suffix byte-for-byte; never overwrite evidence interleaved with authored content.
        var evidence = Regex.Match(original,
            @"<!-- cis:(?:brd-evidence\s|(?:baseline|sources|feature-traceability):start -->)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (evidence.Success)
        {
            if (!string.IsNullOrWhiteSpace(BrdDocumentLayout.Visible(original[evidence.Index..]))) return null;
            end = section.Start + evidence.Index;
        }
        return content[..section.Start] + "\n" + body.TrimEnd() + "\n\n" + content[end..];
    }
}
