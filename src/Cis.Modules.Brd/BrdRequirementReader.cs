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
            var priorityText = text;
            var identityText = Normalize(Regex.Replace(text, @"(?m)^[ \t]*(?:[-*+]|\d+[.)])[ \t]+", string.Empty,
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            text = Normalize(text);
            if (text.Length == 0)
            {
                if (identity is not null) errors.Add($"BRD requirement {identity} has no requirement text.");
                return;
            }
            identity ??= "BRD-IMPORT-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityText)))[..16].ToUpperInvariant();
            // A condition constrains an optional capability without requiring its
            // delivery. Only the delivery clause can supply an obligation priority;
            // inferred optional scope must not replace a reviewed backlog priority.
            var titleClause = PriorityClause(title);
            var assertionClause = PriorityClause(priorityText);
            var titleModality = PriorityModality(titleClause);
            var assertionModality = PriorityModality(assertionClause);
            var titleOptional = HasOptionalDeliveryScope(titleClause);
            var assertionOptional = HasOptionalDeliveryScope(assertionClause);
            var mandatoryTitle = !titleOptional && titleModality.Value.ToLowerInvariant() is "must" or "shall";
            var mandatoryTitleScope = mandatoryTitle || !titleOptional
                && Regex.IsMatch(titleClause, @"\A(?:mandatory|required)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (string.IsNullOrWhiteSpace(priority) && mandatoryTitleScope && assertionOptional)
                errors.Add($"BRD requirement {identity} has conflicting delivery scope in its title and assertion. Retain both passages and review their scope.");
            var deferredTitle = Regex.IsMatch(titleClause, @"\bdeferred\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            // A provision condition constrains the deferred capability;
            // a condition governing an actual delivery obligation retains its modal.
            var provisionCondition = deferredTitle
                ? Regex.Match(assertionClause, @"\A(?:when|if)\b[^,;.]*?\bprovided\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                : Match.Empty;
            var conditionalConstraint = provisionCondition.Success && !PriorityModality(provisionCondition.Value).Success;
            var modality = titleModality.Success ? titleModality : conditionalConstraint ? Match.Empty : assertionModality;
            var optional = titleOptional || !mandatoryTitle && !titleModality.Success && assertionOptional;
            var explicitPriority = !string.IsNullOrWhiteSpace(priority) || mandatoryTitle || modality.Success && !optional;
            priority = !string.IsNullOrWhiteSpace(priority) ? priority : optional ? "Could" : mandatoryTitle ? "Must" : modality.Value.ToLowerInvariant() switch
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
                if (anonymous && listIndent < 0)
                {
                    var next = lineIndex + 1;
                    while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
                    if (next == lines.Length || !ContinuesPreviousDirection(ReadParagraph(lines, next).Text)) Flush();
                    else body.AppendLine();
                    lineIndex = next - 1;
                }
                else if (id is not null || anonymous) body.AppendLine();
                continue;
            }
            if (id is not null || anonymous) body.AppendLine(line);
            else if (!identifiedBlocks[lineIndex])
            {
                var paragraph = ReadParagraph(lines, lineIndex);
                if (Regex.IsMatch(paragraph.Text, @"\b(?:must|shall|should|may|required to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                { anonymous = true; body.AppendLine(paragraph.Text); }
                lineIndex = paragraph.End - 1;
            }
        }
        Flush();
        foreach (var duplicate in requirements.GroupBy(requirement => requirement.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            errors.Add($"BRD requirement identity occurs more than once: {duplicate.Key}. Resolve the duplicate before preparing delivery.");
        return new(requirements, errors);
    }

    // These explicit back-references qualify the immediately preceding passage.
    // Independent obligations, identified requirements and headings remain boundaries.
    internal static bool ContinuesPreviousDirection(string paragraph)
    {
        var text = paragraph.TrimStart();
        if (StartsRequirementBoundary(text)) return false;
        if (Regex.IsMatch(text, @"\b(?:is|are)\s+exempt\s+from\s+(?:this|that)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return true;
        if (!Regex.IsMatch(text, @"\A(?:except\b|unless\b|provided that\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return false;
        // A conditional opening can introduce an independent obligation. Keep it
        // separate unless a back-reference or relative exception ties it to the prior rule.
        return !Regex.IsMatch(text, @"\b(?:must|shall|should|may|could|will|required to)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || Regex.IsMatch(text, @"\b(?:this|that)\s+(?:\w+\s+){0,2}(?:rule|requirement|policy|default|direction|constraint)\b|\Aexcept\b[^,]+,\s*which\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string PriorityClause(string text)
    {
        // Preserve sentence and list boundaries only for priority inference; persisted
        // requirement text and identity retain their existing normalization.
        // A leading condition governs its own obligation, including in later items.
        // Keep trailing conditional constraints separate from the delivery priority.
        // Strip list markers before sentence whitespace can consume their line boundary.
        var items = Regex.Split(text.Trim(), @"^[ \t]*(?:[-*+]|\d+[.)])[ \t]+",
            RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var segment in items.SelectMany(item => Regex.Split(item.Trim(), @"(?<=[.!?])\s+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
        {
            foreach (var part in segment.Split(';'))
            {
                var clause = part.Trim();
                var governing = Regex.IsMatch(clause, @"\A(?:when|if|provided that)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                    ? clause
                    : Regex.Split(clause, @"\b(?:when|if|provided that)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))[0];
                // A declared delivery scope governs following conditional constraints.
                // A neutral preamble, however, must not hide a later actual obligation.
                if (PriorityModality(governing).Success || HasOptionalDeliveryScope(governing)
                    || Regex.IsMatch(governing, @"\bdeferred\b|\A(?:mandatory|required)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return governing;
            }
        }
        return string.Empty;
    }

    private static Match PriorityModality(string text)
        => Regex.Match(text, @"\b(must|shall|should|may|could)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool HasOptionalDeliveryScope(string text)
    {
        var optional = Regex.Match(text, @"\Aoptional\b|\b(?:is|are)\s+(?:explicitly\s+)?optional\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var modality = PriorityModality(text);
        return optional.Success && (!modality.Success || optional.Index < modality.Index);
    }

    private static bool StartsRequirementBoundary(string text)
        => Regex.IsMatch(text.TrimStart(), $@"\A(?:#|[-*+]\s|\d+[.)]\s|\||(?:\*\*)?{Identity}(?=\s|[:.]))",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static (string Text, int End) ReadParagraph(string[] lines, int start)
    {
        var end = start + 1;
        while (end < lines.Length && !string.IsNullOrWhiteSpace(lines[end]) && !StartsRequirementBoundary(lines[end])) end++;
        return (string.Join('\n', lines[start..end]), end);
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
