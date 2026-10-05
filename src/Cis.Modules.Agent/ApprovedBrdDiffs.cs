using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

// Compatibility recovery for saved UTF-8 output decoded through Windows OEM 437.
// This never changes the approved record and never guesses a replacement anchor.
internal static class ApprovedBrdDiffs
{
    internal static bool TryApply(string original, CisBrdReviewDispositionDocument disposition,
        out string candidate, out string error)
    {
        candidate = original;
        error = string.Empty;
        var lines = Regex.Matches(original, @"[^\r\n]*(?:\r\n|\n|\r|$)")
            .Select(match => match.Value).Where(line => line.Length > 0).ToList();
        foreach (var finding in disposition.Findings.Where(item => item.Decision == "accepted").OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var recommendation = RepairPunctuation(CisBrdReviewDispositionCodec.EffectiveRecommendation(finding));
            var blocks = Regex.Matches(recommendation, @"(?ms)^```diff[ \t]*\r?\n(.*?)^```[ \t]*(?:\r?\n|$)",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (blocks.Count == 0) { error = $"{finding.Id} has no exact diff to recover locally."; return false; }
            foreach (Match block in blocks)
            {
                var edits = block.Groups[1].Value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
                if (edits.Any(line => line.Length == 0 || line[0] is not (' ' or '+' or '-'))
                    || !edits.Any(line => line[0] is '+' or '-'))
                { error = $"{finding.Id} contains an unsupported diff; recovery requires literal context, added and removed lines."; return false; }
                var before = edits.Where(line => line[0] != '+').Select(line => line[1..]).ToArray();
                if (before.Length == 0) { error = $"{finding.Id} has no exact original anchor."; return false; }
                var matches = Enumerable.Range(0, Math.Max(0, lines.Count - before.Length + 1))
                    .Where(index => before.Select((line, offset) => line == lines[index + offset].TrimEnd('\r', '\n')).All(equal => equal))
                    .Take(2).ToArray();
                if (matches.Length != 1) { error = $"{finding.Id} does not match exactly one current BRD location."; return false; }
                var start = matches[0];
                var ending = lines[start].EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var replacements = new List<string>();
                var consumed = 0;
                foreach (var edit in edits)
                {
                    if (edit[0] == '+') replacements.Add(edit[1..] + ending);
                    else
                    {
                        if (edit[0] == ' ') replacements.Add(lines[start + consumed]);
                        consumed++;
                    }
                }
                lines.RemoveRange(start, before.Length);
                lines.InsertRange(start, replacements);
            }
        }
        candidate = string.Concat(lines);
        if (candidate == original) { error = "Approved diffs do not change the BRD."; return false; }
        return true;
    }

    private static string RepairPunctuation(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(437);
        // Reverse only known punctuation sequences, including two consecutive faulty decodes.
        foreach (var depth in new[] { 2, 1 })
        foreach (var punctuation in "\u2013\u2014\u2018\u2019\u201c\u201d\u2026")
        {
            var corrupted = punctuation.ToString();
            for (var i = 0; i < depth; i++) corrupted = oem.GetString(Encoding.UTF8.GetBytes(corrupted));
            text = text.Replace(corrupted, punctuation.ToString(), StringComparison.Ordinal);
        }
        return text;
    }
}
