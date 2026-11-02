using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Brd;

// Shared source-linked interpretation; parsing never edits or approves the source.
internal static class BrdRequirementReader
{
    private const string Identity = @"(?:BR(?:D)?-[A-Z0-9]+(?:-[A-Z0-9]+)+|[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-\d+)";
    internal sealed record Requirement(string Id, string Outcome, string Text, string Priority, string Acceptance,
        bool ExplicitPriority = true, string SourceReference = "");
    internal sealed record Result(IReadOnlyList<Requirement> Requirements, IReadOnlyList<string> Errors);

    internal static Result Read(string content, string heading)
    {
        var layoutErrors = BrdDocumentLayout.Validate(content);
        if (layoutErrors.Count > 0) return new([], layoutErrors);
        var section = BrdDocumentLayout.Visible(BrdService.ExtractRecognizedSection(content, heading));
        var lines = section.Split('\n');
        var identifiedBlocks = IdentifiedBlocks(lines);
        var requirements = new List<Requirement>(); var errors = new List<string>();
        string? id = null; string? outcome = null; var body = new StringBuilder();
        var anonymous = false; var listIndent = -1;
        var tableRequirement = -1; var tableId = -1; var tablePriority = -1; var tableAcceptance = -1;
        void Add(string? identity, string title, string text, string? priority = null, string? acceptance = null)
        {
            var identityText = Normalize(Regex.Replace(text, @"(?m)^[ \t]*(?:[-*+]|\d+[.)])[ \t]+", string.Empty,
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            text = Normalize(text);
            if (text.Length == 0)
            {
                if (identity is not null) errors.Add($"BRD requirement {identity} has no requirement text.");
                return;
            }
            identity ??= "BRD-IMPORT-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityText)))[..16].ToUpperInvariant();
            var modality = Regex.Match(title + " " + text, @"\b(must|shall|should|may|could)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var optional = !modality.Success && Regex.IsMatch(text, @"\A(?:optional|deferred)\b|\b(?:is|are)\s+(?:explicitly\s+)?(?:optional|deferred)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var explicitPriority = !string.IsNullOrWhiteSpace(priority) || modality.Success || optional;
            priority = !string.IsNullOrWhiteSpace(priority) ? priority : optional ? "Could" : modality.Value.ToLowerInvariant() switch
            {
                "should" => "Should",
                "may" or "could" => "Could",
                _ => "Must",
            };
            requirements.Add(new(identity, string.IsNullOrWhiteSpace(title) ? text : title, text, priority,
                acceptance ?? text, explicitPriority, heading + "#" + identity));
        }
        void Flush()
        {
            if (id is not null || anonymous) Add(id, outcome ?? "", body.ToString());
            id = null; outcome = null; body.Clear(); anonymous = false; listIndent = -1;
        }
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var headingMatch = Regex.Match(line, @"^\s*#{1,6}\s+(?<text>.+?)\s*#*\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (headingMatch.Success)
            {
                Flush(); tableRequirement = -1;
                var identified = Regex.Match(headingMatch.Groups["text"].Value, $@"^(?<id>{Identity})(?:\s*[:.—–-]\s*|\s+)?(?<title>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (identified.Success) { id = identified.Groups["id"].Value; outcome = identified.Groups["title"].Value.Trim().TrimEnd('.', ':'); }
                continue;
            }
            var bold = Regex.Match(line, $@"^\s*(?:[-*+]\s+|\d+[.)]\s+)?\*\*(?<id>{Identity})(?:\s+(?:(?:—|–|-)\s+)?(?<title>[^*]+?))?[:.]?\*\*\s*:?[ \t]*(?<body>.*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var plain = Regex.Match(line, $@"^\s*(?:[-*+]\s+|\d+[.)]\s+)?(?<id>{Identity})(?:\s*[:.]\s+|\s+(?:—|–|-)\s+|\s+)(?<body>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (bold.Success || plain.Success)
            {
                Flush(); tableRequirement = -1;
                var identified = bold.Success ? bold : plain;
                id = identified.Groups["id"].Value; outcome = bold.Success ? bold.Groups["title"].Value.Trim().TrimEnd('.', ':') : "";
                body.AppendLine(identified.Groups["body"].Value); continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                var cells = Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(cell => cell.Trim().Trim('`').Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
                var requirementColumn = Array.FindIndex(cells, cell => cell.Equals("Requirement", StringComparison.OrdinalIgnoreCase));
                if (requirementColumn >= 0)
                {
                    Flush(); tableRequirement = requirementColumn;
                    tableId = Array.FindIndex(cells, cell => cell.Equals("ID", StringComparison.OrdinalIgnoreCase) || cell.Equals("Requirement ID", StringComparison.OrdinalIgnoreCase));
                    tablePriority = Array.FindIndex(cells, cell => cell.Equals("Priority", StringComparison.OrdinalIgnoreCase));
                    tableAcceptance = Array.FindIndex(cells, cell => cell.Equals("Acceptance", StringComparison.OrdinalIgnoreCase)
                        || cell.Equals("Acceptance criteria", StringComparison.OrdinalIgnoreCase) || cell.Equals("Acceptance intent", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                if (cells.All(cell => Regex.IsMatch(cell, @"^[-: ]+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))) continue;
                if (tableRequirement >= 0 && cells.Length > tableRequirement)
                {
                    var identity = tableId >= 0 && cells.Length > tableId ? cells[tableId] : null;
                    if (!string.IsNullOrWhiteSpace(identity) && !Regex.IsMatch(identity, $@"\A{Identity}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                        errors.Add($"BRD import could not read requirement identity: {identity}. The source row was retained.");
                    else Add(string.IsNullOrWhiteSpace(identity) ? null : identity, cells[tableRequirement], cells[tableRequirement],
                        tablePriority >= 0 && cells.Length > tablePriority ? cells[tablePriority] : null,
                        tableAcceptance >= 0 && cells.Length > tableAcceptance ? cells[tableAcceptance] : "");
                    continue;
                }
                if (id is not null || anonymous) body.AppendLine(line);
                continue;
            }
            tableRequirement = -1;
            var bullet = Regex.Match(line, @"^(?<indent>\s*)(?:[-*+]\s+|\d+[.)]\s+)(?<text>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (bullet.Success && id is null)
            {
                var indent = bullet.Groups["indent"].Length;
                if (!anonymous || listIndent < 0 || indent <= listIndent)
                {
                    Flush(); anonymous = true; listIndent = indent; body.AppendLine(bullet.Groups["text"].Value);
                }
                else body.AppendLine(line);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                if (anonymous && listIndent < 0) Flush();
                else if (id is not null || anonymous) body.AppendLine();
                continue;
            }
            if (id is not null || anonymous) body.AppendLine(line);
            else if (!identifiedBlocks[lineIndex] && Regex.IsMatch(line, @"\b(?:must|shall|should|may|required to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            { anonymous = true; body.AppendLine(line); }
        }
        Flush();
        foreach (var duplicate in requirements.GroupBy(requirement => requirement.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"BRD requirement identity occurs more than once: {duplicate.Key}. Resolve the duplicate before preparing delivery.");
        return new(requirements, errors);
    }

    private static bool[] IdentifiedBlocks(string[] lines)
    {
        // Introductory prose in an identified requirement group is context, not
        // an additional requirement merely because it contains an obligation verb.
        // Prose-only groups and independent plain requirement bullets still import.
        var identified = new bool[lines.Length];
        var definition = new Regex($@"^\s*(?:(?:#{{1,6}}|[-*+]|\d+[.)])\s+|\|\s*)?(?:\*\*)?{Identity}(?=\s|[:.*|—–-]|$)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var heading = new Regex(@"^\s*#{1,6}\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var start = 0; var hasIdentity = false;
        for (var index = 0; index <= lines.Length; index++)
        {
            if (index == lines.Length || heading.IsMatch(lines[index]))
            {
                Array.Fill(identified, hasIdentity, start, index - start);
                start = index; hasIdentity = false;
            }
            if (index < lines.Length && definition.IsMatch(lines[index])) hasIdentity = true;
        }
        return identified;
    }

    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
