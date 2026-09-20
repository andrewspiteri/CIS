using System.Net;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.SolutionDesign;

public sealed partial class FeatureArchitectureGenerator
{
    private sealed record Requirement(int Id, string Heading, string Text, bool Constraint, bool Direction);
    private sealed record FeatureEvidence(IReadOnlyList<Requirement> Requirements, IReadOnlyList<string> Roles)
    {
        public IReadOnlyList<Requirement> Contracts { get; } = Requirements.Where(r => !r.Constraint &&
            Regex.IsMatch(r.Text, @"\b(?:uses?|calls?|sends?|receives?|reads?|queries|writes?|requests?|returns?|submits?|captures?|selects?|enters?|opens?|visits?|initiates?|acknowledg\w*|redirect\w*|pull\w*|push\w*|publish\w*|imports?|exports?|exposes?|consumes?|provides?|webhooks?|APIs?|endpoints?|contracts?|events?|integrat\w*)\b", RegexOptions.IgnoreCase)).ToArray();

        // Every constraint participates in evidence selection, even when the bounded
        // model prompt cannot repeat all of them. Negative mentions never admit peers.
        public string Constraints(ArchitectureNode[] known) => string.Join('\n', Requirements.Where(r => r.Constraint)
            .DistinctBy(r => r.Text).OrderByDescending(r => known.Any(n => NamesPeer(r, n) || Names(r.Text, n.Id)))
            .Select(r => "[" + r.Id + "] " + r.Text));
    }

    private static FeatureEvidence ReadEvidence(CisFeatureArchitectureInput input)
    {
        var result = new List<Requirement>();
        var roles = new List<string>();
        Read(input.Source, false);
        Read(input.Direction, true);
        return new(result, roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        void Read(string markdown, bool direction)
        {
            var lines = Regex.Replace(markdown, @"<!--.*?-->", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Split('\n');
            var headings = new List<(int Level, string Title, bool Excluded)>();
            var excludedList = false;
            var fence = "";
            var textFlow = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                var marker = Regex.Match(line, @"^(`{3,}|~{3,})(.*)$");
                if (marker.Success)
                {
                    if (fence.Length == 0)
                    {
                        fence = marker.Groups[1].Value;
                        textFlow = marker.Groups[2].Value.Trim() is "text" or "plaintext";
                    }
                    else if (marker.Groups[1].Value.StartsWith(fence, StringComparison.Ordinal)) fence = "";
                    continue;
                }
                // Plain-text business flows are evidence; executable examples are not.
                if ((fence.Length > 0 && !textFlow) || line.Length == 0) continue;
                var heading = Regex.Match(fence.Length > 0 ? "" : line, @"^(#{1,6})\s+(.+)$");
                if (heading.Success)
                {
                    var level = heading.Groups[1].Length;
                    headings.RemoveAll(h => h.Level >= level);
                    var title = Clean(heading.Groups[2].Value);
                    headings.Add((level, title, IsExcludedHeading(title)));
                    excludedList = false;
                    continue;
                }
                if (Regex.IsMatch(line, @"^[|\s:-]+$") || (i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^\s*\|?\s*:?-{3,}"))) continue;
                var listItem = Regex.IsMatch(line, @"^(?:[-*+]\s+|\d+[.)]\s+|\|)");
                if (!listItem) excludedList = false;
                var text = Clean(line);
                if (text.Length < 12) continue;
                var negative = IsConstraint(text);
                if (!direction && !negative && !headings.Any(h => h.Excluded) && line.StartsWith('|')
                    && headings.Any(h => Regex.IsMatch(h.Title, @"\b(?:stakeholders?|actors?|external systems?|participants?)\b", RegexOptions.IgnoreCase)))
                {
                    var name = line.Trim('|').Split('|')[0].Trim();
                    roles.AddRange(name.Split('/').Select(Clean).Where(role => role.Length is >= 3 and <= 60));
                }
                result.Add(new(result.Count, headings.LastOrDefault().Title ?? "", text,
                    headings.Any(h => h.Excluded) || excludedList || negative, direction));
                if (negative && text.EndsWith(':')) excludedList = true;
            }
        }
    }

    private static bool IsExcludedHeading(string title) => Regex.IsMatch(title,
        @"\b(?:exclud\w*|separate products?|separate.*bounded contexts?|out.of.scope|non.goals?|future)\b", RegexOptions.IgnoreCase);

    private static bool IsConstraint(string text) => Regex.IsMatch(text,
        @"\b(?:no|not|never|without|outside|exclud\w*|out.of.scope|non.goals?)\b|\b(?:remain|keep|kept|delivered|managed|implemented)\w*\b.{0,120}\bseparate\w*\b",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static string Clean(string text)
    {
        text = WebUtility.HtmlDecode(Regex.Replace(text, @"<[^>]+>", " "));
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        text = text.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal).Replace('|', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '*');
    }

    private static bool Names(string text, string name)
    {
        var words = Regex.Matches(Clean(name), @"[\p{L}\p{N}]+").Select(m => Regex.Escape(m.Value)).ToArray();
        return words.Length > 0 && Regex.IsMatch(Clean(text), @"(?<![\p{L}\p{N}])" + string.Join(@"[\s_-]+", words) + @"s?(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    private static bool NamesPeer(Requirement requirement, ArchitectureNode node) => Names(requirement.Text, node.Label)
        // A vendor-neutral capability ID does not prove which external vendor fulfils it.
        || (node.Kind == "container" && node.Id.Length >= 4 && Names(requirement.Text, node.Id));

    private static bool SupportsRole(Requirement requirement, string name)
    {
        var words = Regex.Matches(Clean(name), @"[\p{L}\p{N}]+").Select(m => Regex.Escape(m.Value)).ToArray();
        if (words.Length == 0) return false;
        var role = @"(?<![\p{L}\p{N}])" + string.Join(@"[\s_-]+", words) + @"s?(?![\p{L}\p{N}])";
        return Regex.IsMatch(requirement.Text,
            @"\b(?:to|from|with|by|for)\s+(?:(?:the|a|an)\s+)?" + role + @"(?!\s+(?:IDs?|identifiers?|fields?|records?)\b)|"
            + role + @"(?:-level|'s)?\s+(?:(?:shall|must|may|can|will|optionally)\s+)?(?:send\w*|receiv\w*|submit\w*|request\w*|call\w*|publish\w*|consum\w*|access\w*|read\w*|provid\w*|select\w*|enter\w*|open\w*|visit\w*|initiat\w*|acknowledg\w*|webhooks?|endpoints?|APIs?)\b",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    private static bool SupportsPeer(Requirement requirement, ArchitectureNode node) => SupportsRole(requirement, node.Label)
        || (node.Kind == "container" && node.Id.Length >= 4 && SupportsRole(requirement, node.Id));

    private static bool ExcludedPeer(FeatureEvidence evidence, ArchitectureNode node) => evidence.Requirements.Any(r => !r.Direction
        && r.Constraint && (NamesPeer(r, node) || Names(r.Text, node.Id)))
        && !evidence.Contracts.Any(r => !r.Direction && SupportsPeer(r, node));

    private static bool ExcludedRole(FeatureEvidence evidence, string label) => evidence.Requirements.Any(r => !r.Direction
        && r.Constraint && Names(r.Text, label)) && !evidence.Contracts.Any(r => !r.Direction && SupportsRole(r, label));

    private static IReadOnlyList<Requirement> ContractsFor(FeatureEvidence evidence, Passage passage)
    {
        static string Heading(string value) => Regex.Replace(Clean(value), @"^\d+(?:\.\d+)*[.)]?\s+", "");
        var title = Heading(passage.Title);
        var ids = Regex.Matches(passage.Text, @"\b[A-Z][A-Z0-9-]*-\d{2,}\b").Select(m => m.Value).ToArray();
        return evidence.Contracts.Where(r => r.Direction
            ? Names(r.Text, title) || ids.Any(id => Names(r.Text, id))
            : Heading(r.Heading).Equals(title, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
