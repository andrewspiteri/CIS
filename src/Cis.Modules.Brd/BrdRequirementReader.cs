using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Brd;

// One normalized read model for approval preflight and downstream delivery.
// Preserve source identities and complete wording; never rewrite an approved BRD.
internal static class BrdRequirementReader
{
    private const string Identity = @"(?:BR(?:D)?-[A-Z0-9]+(?:-[A-Z0-9]+)+|[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-\d+)";
    internal sealed record Requirement(string Id, string Outcome, string Text, string Priority, string Acceptance, bool ExplicitPriority = true);
    internal sealed record Result(IReadOnlyList<Requirement> Requirements, IReadOnlyList<string> Errors);

    internal static Result Read(string content, string heading)
    {
        var visible = new StringBuilder();
        var fence = '\0'; var fenceLength = 0;
        foreach (var line in Regex.Replace(content, @"<!--.*?-->", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                var length = trimmed.TakeWhile(character => character == trimmed[0]).Count();
                if (fence == '\0') { fence = trimmed[0]; fenceLength = length; }
                else if (trimmed[0] == fence && length >= fenceLength) fence = '\0';
                continue;
            }
            if (fence == '\0') visible.AppendLine(line.TrimEnd('\r'));
        }
        var section = BrdService.ExtractRecognizedSection(visible.ToString(), heading);
        var requirements = new List<Requirement>(); var errors = new List<string>();
        string? id = null; string? outcome = null; var body = new StringBuilder();
        void Flush()
        {
            if (id is null) return;
            var text = Regex.Replace(body.ToString().Trim(), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (string.IsNullOrWhiteSpace(text)) errors.Add($"BRD requirement {id} has no requirement text.");
            else requirements.Add(new(id, string.IsNullOrWhiteSpace(outcome) ? text : outcome, text, "Must", text, false));
            id = null; outcome = null; body.Clear();
        }
        foreach (var line in section.Split('\n'))
        {
            var narrative = Regex.Match(line, $@"^\s*(?:[-*+]\s+)?\*\*(?<id>{Identity})(?:\s+(?:—|–|-)\s+(?<title>.*?))?[:.]?\*\*\s*:?[ \t]*(?<body>.*)$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (narrative.Success)
            {
                Flush(); id = narrative.Groups["id"].Value; outcome = narrative.Groups["title"].Value.Trim().TrimEnd('.', ':');
                body.AppendLine(narrative.Groups["body"].Value); continue;
            }
            if (Regex.IsMatch(line, @"^\s*#{1,6}\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) { Flush(); continue; }
            if (line.TrimStart().StartsWith('|'))
            {
                var cells = Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(cell => cell.Trim().Trim('`').Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
                if (cells.Length >= 2 && Regex.IsMatch(cells[0], $@"\A{Identity}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    Flush();
                    if (string.IsNullOrWhiteSpace(cells[1])) errors.Add($"BRD requirement {cells[0]} has no requirement text.");
                    else requirements.Add(new(cells[0], cells[1], cells[1], cells.Length > 2 ? cells[2] : "Must", cells.Length > 3 ? cells[3] : "", cells.Length > 2 && !string.IsNullOrWhiteSpace(cells[2])));
                    continue;
                }
            }
            if (id is not null) body.AppendLine(line);
        }
        Flush();
        foreach (var duplicate in requirements.GroupBy(requirement => requirement.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"BRD requirement identity occurs more than once: {duplicate.Key}. Resolve the duplicate before preparing delivery.");
        return new(requirements, errors);
    }
}
