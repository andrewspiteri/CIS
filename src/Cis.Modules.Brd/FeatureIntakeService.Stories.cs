using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record StorySection(string Title, string Context, IReadOnlyList<string> Lines, bool Excluded);
    private sealed record StoryRequirement(string Id, string Text);
    private sealed record StoryDraft(string Title, string Narrative, IReadOnlyList<string> Acceptance, string Source, bool FutureCandidate);
    private const string StoryFieldPrefix = "delivery-stories-";

    // This is a bounded, repeatable projection of the retained feature BRD. Opening
    // the wizard must not call a model, save a decision or create backlog approvals.
    private static IReadOnlyDictionary<string, string> SuggestStories(string source)
        => StoryGroups(source).ToDictionary(pair => StoryFieldPrefix + pair.Key, pair => RenderStories(pair.Key, pair.Value), StringComparer.Ordinal);

    private static Dictionary<string, List<StoryDraft>> StoryGroups(string source)
    {
        var sections = StorySections(source);
        var groups = new Dictionary<string, List<StoryDraft>>(StringComparer.Ordinal)
        { ["foundation"] = [], ["mvp"] = [], ["post-mvp"] = [] };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var detailed = sections.Any(s => !s.Excluded && StoryMatch(s.Context, @"(?:^| / )(?:functional|integration|data) requirements(?: / |$)|user stories")
            && Requirements(s.Lines).Count > 0);
        var futureExtensions = sections.Any(s => !s.Excluded && StoryMatch(s.Title, @"future.*(?:extensions|enhancements)|post.mvp|later releases"));
        foreach (var section in sections.Where(s => !s.Excluded))
        {
            var future = StoryMatch(section.Context, @"post.mvp|future|later releases|deferred|phase [2-9]");
            if (futureExtensions && StoryMatch(section.Title, "future.compatible")) continue;
            var requirements = Requirements(section.Lines);
            var explicitStories = StoryMatch(section.Context, "user stories");
            var requirementSection = StoryMatch(section.Context, @"(?:^| / )(?:(?:functional|non.functional|integration|data|business|system) )?requirements(?: / |$)");
            var phaseSection = StoryMatch(section.Context, @"\bfoundations?\b|\bmvp\b|first release|initial release");
            if (!future && !explicitStories && !requirementSection && !(phaseSection && !detailed)) continue;
            if (requirements.Count == 0) continue;
            var phase = future ? "post-mvp" : StoryMatch(section.Context, @"\bfoundations?\b|non.functional requirements")
                || StoryMatch(section.Title, @"\b(?:security|privacy|tenant|tenancy|organisation|organization|access control|roles|permission|authentication|audit|observability|recovery|idempotency)\b")
                ? "foundation" : "mvp";
            var candidate = future && (StoryMatch(section.Context, "future|potential|candidate|uncommitted|not committed")
                || section.Lines.Any(line => StoryMatch(line, "potential|not committed|may include|could include")));
            // Named requirement sections are capability-sized stories. Scope/future
            // lists already enumerate capabilities and yield one story per item.
            var individual = future || (phaseSection && !requirementSection) || explicitStories;
            var batches = individual ? requirements.Select(r => (IReadOnlyList<StoryRequirement>)new[] { r }) : [requirements];
            foreach (var batch in batches)
            {
                var primary = batch.FirstOrDefault(r => !StoryMatch(r.Text, @"\b(?:not required|shall not|must not|out.of.scope|excluded)\b|^no\b"));
                if (primary is null) continue;
                var title = individual ? StoryText(primary.Text).TrimEnd('.') : StoryTitle(section.Title);
                if (title.Length > 160) title = title[..157].TrimEnd() + "…";
                if (!seen.Add(Regex.Replace(title, @"[^\p{L}\p{N}]", ""))) continue;
                var acceptance = batch.Select(r => r.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var actor = phase == "foundation" ? "platform operator" : "feature user";
                var narrative = StoryNarrative(primary.Text, actor);
                var sourceNote = section.Title + (batch.Any(r => r.Id.Length > 0) ? "; " + string.Join(", ", batch.Where(r => r.Id.Length > 0).Select(r => r.Id)) : "");
                // An interface section often restates a capability already covered by
                // a functional story. Keep its acceptance evidence on that story.
                var existing = !future && StoryMatch(section.Context, @"(?:^| / )integration requirements(?: / |$)")
                    ? groups[phase].Select((story, index) => (Index: index, Score: StoryOverlap(story.Title, title)))
                        .Where(match => match.Score > 0).OrderByDescending(match => match.Score).Select(match => (int?)match.Index).FirstOrDefault()
                    : null;
                if (existing is { } target)
                {
                    var previous = groups[phase][target];
                    groups[phase][target] = previous with { Acceptance = previous.Acceptance.Concat(acceptance).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Source = previous.Source + "; " + sourceNote };
                }
                else groups[phase].Add(new(title, narrative, acceptance, sourceNote, candidate));
            }
        }
        return groups;
    }

    private static IReadOnlyList<StorySection> StorySections(string source)
    {
        var result = new List<StorySection>();
        var headings = new List<(int Level, string Title)>();
        var lines = new List<string>();
        var fence = "";
        void Flush()
        {
            if (headings.Count == 0 || lines.Count == 0) { lines.Clear(); return; }
            var context = string.Join(" / ", headings.Select(h => StoryTitle(h.Title)));
            result.Add(new(headings[^1].Title, context, lines.ToArray(), headings.Any(h =>
                StoryMatch(h.Title, @"excluded|exclusions|out.of.scope|non.goals|separate.*(?:products|systems|contexts)|glossary|appendix"))));
            lines.Clear();
        }
        foreach (var raw in Regex.Replace(source, @"<!--.*?-->", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Split('\n'))
        {
            var line = raw.Trim();
            var marker = Regex.Match(line, @"^(`{3,}|~{3,})");
            if (marker.Success)
            {
                if (fence.Length == 0) fence = marker.Value;
                else if (marker.Value.StartsWith(fence, StringComparison.Ordinal)) fence = "";
                continue;
            }
            if (fence.Length > 0) continue;
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (heading.Success)
            {
                Flush();
                headings.RemoveAll(h => h.Level >= heading.Groups[1].Length);
                headings.Add((heading.Groups[1].Length, StoryText(heading.Groups[2].Value)));
            }
            else lines.Add(line);
        }
        Flush();
        return result;
    }

    private static IReadOnlyList<StoryRequirement> Requirements(IReadOnlyList<string> lines)
    {
        var result = new List<StoryRequirement>();
        var excludedList = false;
        foreach (var line in lines)
        {
            if (line.Length == 0 || Regex.IsMatch(line, @"^[|\s:*-]+$")) continue;
            var list = Regex.IsMatch(line, @"^(?:[-*+]\s+|\d+[.)]\s+|\|)");
            var cleaned = StoryText(line);
            if (!list)
            {
                excludedList = cleaned.EndsWith(':') && StoryMatch(cleaned, @"remain.*separate|outside|out.of.scope|exclud|not.*(?:include|part|required)");
                if (!StoryMatch(cleaned, @"\b(?:shall|must)\b|^As (?:a|an|the)\b")) continue;
            }
            if (excludedList || cleaned.EndsWith(':')) continue;
            var cells = line.Trim('|').Split('|').Select(StoryText).ToArray();
            var id = line.StartsWith('|') ? cells.FirstOrDefault(cell => Regex.IsMatch(cell, @"^[A-Z][A-Z0-9-]*-\d{2,}$")) ?? "" : "";
            if (line.StartsWith('|') && id.Length == 0) continue;
            var body = id.Length > 0 ? cells.ElementAtOrDefault(Array.IndexOf(cells, id) + 1) ?? "" : Regex.Replace(cleaned, @"^(?:[-*+]\s+|\d+[.)]\s+)", "");
            if (body.Length >= 12) result.Add(new(id, body));
        }
        return result;
    }

    private static string StoryNarrative(string requirement, string actor)
    {
        var text = StoryText(requirement);
        if (text.StartsWith("As ", StringComparison.OrdinalIgnoreCase)) return text;
        var specific = Regex.Match(text, @"^(?:An? |The )?(?<actor>authori[sz]ed user|administrator|customer|operator|user|export requester|export approver)\s+(?:shall|must)\s+(?:be able to\s+)?(?<action>.+)$", RegexOptions.IgnoreCase);
        if (specific.Success)
        {
            var role = specific.Groups["actor"].Value.ToLowerInvariant();
            var article = role == "user" || role == "customer" ? "a" : "an";
            return $"As {article} {role}, I want to {specific.Groups["action"].Value.TrimEnd('.')}.";
        }
        var behaviour = Regex.Match(text, @"^(?<subject>.+?)\s+(?:shall|must)\s+(?<action>.+)$", RegexOptions.IgnoreCase);
        if (behaviour.Success)
        {
            var subject = behaviour.Groups["subject"].Value;
            if (StoryMatch(subject, @"^(?:The|Every|Each|All|An?)\b")) subject = char.ToLowerInvariant(subject[0]) + subject[1..];
            return $"As a {actor}, I want {subject} to {behaviour.Groups["action"].Value.TrimEnd('.')}.";
        }
        return $"As a {actor}, I want support for {char.ToLowerInvariant(text[0]) + text[1..].TrimEnd('.')}.";
    }

    private static string RenderStories(string phase, IReadOnlyList<StoryDraft> stories)
    {
        if (stories.Count == 0) return phase == "post-mvp"
            ? "No Post-MVP stories are established by the current BRD. Review and confirm that no later delivery is required."
            : "";
        var text = new StringBuilder();
        foreach (var story in stories)
        {
            var block = new StringBuilder("### " + story.Title + "\n\n" + story.Narrative + "\n\n");
            if (story.FutureCandidate) block.AppendLine("Future candidate in the BRD; not committed for delivery. Confirm before including it in the backlog.\n");
            block.AppendLine("Acceptance outline:");
            foreach (var criterion in story.Acceptance.Take(4)) block.AppendLine("- " + criterion);
            if (story.Acceptance.Count > 4) block.AppendLine($"\nAlso satisfy the remaining {story.Acceptance.Count - 4} requirements in this BRD section.");
            block.AppendLine("\n<!-- Source: " + story.Source.Replace("--", "—", StringComparison.Ordinal) + " -->\n");
            if (text.Length + block.Length > 23_000)
            {
                text.AppendLine("\nAdditional stories do not fit this suggested list. Review the remaining BRD sections and split the delivery scope before accepting it.");
                break;
            }
            text.Append(block);
        }
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static int StoryOverlap(string left, string right)
    {
        static HashSet<string> Words(string title) => Regex.Matches(title.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value).Where(word => !new[] { "and", "the", "optional", "basic", "integration", "requirements" }.Contains(word)).ToHashSet(StringComparer.Ordinal);
        var a = Words(left); var b = Words(right);
        var overlap = a.Intersect(b).Count();
        return a.SetEquals(b) || (overlap >= 2 && (a.IsSubsetOf(b) || b.IsSubsetOf(a))) ? overlap : 0;
    }

    private static string StoryTitle(string text) => Regex.Replace(text, @"^\d+(?:\.\d+)*[.)]?\s+", "").Trim();
    private static bool StoryMatch(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string StoryText(string text)
    {
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        return Regex.Replace(WebUtility.HtmlDecode(text).Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal), @"\s+", " ").Trim();
    }
}
