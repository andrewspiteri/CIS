using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    // These are feature-definition dependencies, not write tokens. Raw inputs and
    // the complete request revision remain checked separately by every writer.
    private static IReadOnlyDictionary<string, string> PageBasis(WizardState state, string pageId)
    {
        var basis = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Add(string name, object value) => basis[name] = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)));
        var plan = state.Record.Plan;
        Add("feature authority and repository boundary", new
        {
            ProductId = state.Workspace.Product?.Id,
            EcosystemId = state.Workspace.Ecosystem?.Id,
            Authority = DeliveryRepositoryIdentity(state.Authority),
            plan.RepositoryPath,
            plan.DocumentationRoot,
            plan.BacklogItemId,
            repositories = state.Workspace.Repositories.Where(repo => SamePath(repo.RepositoryPath, plan.RepositoryPath)
                || plan.IntegrationRepositories.Contains(repo.Id, StringComparer.Ordinal)).OrderBy(repo => repo.Id, StringComparer.Ordinal)
                .Select(DeliveryRepositoryIdentity)
        });
        Add("authored feature request scope", FeatureRequestMaterial(state));
        if (pageId == "review")
        {
            foreach (var page in ReviewPages)
            {
                foreach (var entry in PageBasis(state, page.Id)) basis[page.Title + ": " + entry.Key] = entry.Value;
                Add(page.Title + " answers", PageAnswerScope(state, page.Id));
            }
            Add("repository work and prerequisites", state.Review.RepositoryWork ?? []);
            Add("screen choices", (state.Review.ScreenReviews ?? []).GroupBy(item => item.Key, StringComparer.Ordinal)
                .Select(group => group.Last()).OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new { item.Key, item.Decision, item.Feedback, item.ScreenRevision }));
            Add("delivery choices", (state.Review.DeliveryReviews ?? []).GroupBy(item => item.StoryId, StringComparer.Ordinal)
                .Select(group => group.Last()).OrderBy(item => item.StoryId, StringComparer.Ordinal)
                .Select(item => new { item.StoryId, item.Treatment, item.Owners, item.Plan }));
            return basis;
        }
        var definition = ReviewPages.Single(page => page.Id == pageId);
        var terms = PageSourceTerms(pageId);
        Add("feature BRD " + definition.Title.ToLowerInvariant(), ReviewSourceMaterial(state.Source, terms));
        Add("shared feature constraints and authority", ReviewSourceMaterial(state.Source,
            "constraint|assumption|authoritative|permission boundar|scope exclusion|out.of.scope"));
        if (pageId == "business")
        {
            Add("open business decisions", plan.OpenDecisions.Select(DecisionSubject).Order(StringComparer.Ordinal));
            foreach (var context in DecisionContexts(state.Source).GroupBy(item => item.Subject, StringComparer.Ordinal))
                Add("business decision context " + context.Key, context.Select(item => item.Context).Order(StringComparer.Ordinal));
        }
        if (pageId == "delivery")
            Add("business decision scope and prerequisites", DecisionContexts(state.Source).OrderBy(item => item.Subject, StringComparer.Ordinal).ThenBy(item => item.Context, StringComparer.Ordinal)
                .Select(item => new { item.Subject, item.Context }));
        foreach (var source in SourceBrds(plan).Where(source => source.Path != plan.SourcePath).OrderBy(source => source.Path, StringComparer.Ordinal))
        {
            var absolute = Path.Combine(state.Authority.RepositoryPath, source.Path);
            Add("supporting BRD " + source.Title, ReviewSourceMaterial(File.ReadAllText(absolute), terms));
        }
        foreach (var relative in definition.Documents)
        {
            var path = Cis.Abstractions.CisProductDocumentPaths.Resolve(Path.Combine(state.Authority.RepositoryPath, state.Authority.DocumentationRoot), relative);
            Add("product baseline " + relative, File.Exists(path) ? relative == "specs/business-requirements.md"
                ? BrdDocumentDigest.Compute(File.ReadAllText(path)) : ReviewDocumentMaterial(File.ReadAllText(path)) : "missing");
        }
        // A saved upstream conclusion is part of the downstream contract; its
        // actor, timestamp and serialization history are not that conclusion.
        var upstream = pageId switch
        {
            "architecture" => new[] { "technical" },
            "contracts" => ["technical", "architecture"],
            "experience" => ["architecture"],
            "delivery" => ["business", "technical", "architecture", "contracts", "experience"],
            _ => []
        };
        foreach (var page in upstream) Add(page + " reviewed direction", PageAnswerScope(state, page));
        return basis;
    }

    private static string PageSourceTerms(string pageId)
    {
        var definition = ReviewPages.Single(page => page.Id == pageId);
        var terms = string.Join('|', QuestionsFor(pageId).Select(question => question.SourceTerms).Where(value => value.Length > 0).Prepend(definition.SourceTerms));
        return pageId == "business" ? terms + "|requirement|functional|outcome|user|objective" : terms;
    }

    private static IReadOnlyDictionary<string, string> PageAnswerScope(WizardState state, string pageId)
    {
        var answers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var answer in state.Review.Pages.GetValueOrDefault(pageId)?.Answers ?? [])
        {
            var subject = answer.Key;
            if (pageId == "business" && answer.Key.StartsWith("decision-", StringComparison.Ordinal)
                && int.TryParse(answer.Key[9..], out var index) && index > 0 && index <= state.Record.Plan.OpenDecisions.Count)
                subject = "decision:" + DecisionSubject(state.Record.Plan.OpenDecisions[index - 1]);
            answers[subject] = answer.Value;
        }
        return answers;
    }

    private static string? PageBasisReason(PageReview review, WizardState state, string pageId)
    {
        if (review.InvalidatedReason is not null) return review.InvalidatedReason;
        if (review.Basis is null)
            return review.Binding == state.Binding || review.Binding == state.LegacyBinding ? null
                : "This saved page has a legacy whole-feature basis whose compatibility cannot be established. Its answers and original reviewer are retained; review and save this page to establish its current basis.";
        var current = PageBasis(state, pageId);
        var changed = review.Basis.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)
            .Where(key => review.Basis.GetValueOrDefault(key) != current.GetValueOrDefault(key)).Order(StringComparer.Ordinal).ToArray();
        return changed.Length == 0 ? null : "Changed " + string.Join("; ", changed)
            + ". Retained answers and their review history remain available; review and save this page against the changed basis.";
    }

    private static string DecisionSubject(string question)
        => DecisionIdentity(question) is { } identity ? identity + ":" + DecisionMeaning(question) : question;

    private static string? DecisionIdentity(string question)
    {
        // Explicit IDs are supported only in a leading Markdown ID label. The
        // question's list ordinal is deliberately never an identity.
        var match = Regex.Match(question, @"^\s*(?:\*\*|`)?\[?(?<id>(?:DEC|DECISION|BD|BRD|Q)[-_]\d{1,6})\]?(?:\*\*|`)?\s*[:.—-]?\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["id"].Value.ToUpperInvariant() : null;
    }

    private static string DecisionMeaning(string question)
        => Regex.Replace(question, @"^\s*(?:\*\*|`)?\[?(?:DEC|DECISION|BD|BRD|Q)[-_]\d{1,6}\]?(?:\*\*|`)?\s*[:.—-]?\s+", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool BusinessDecisionHeading(string title)
        => Regex.IsMatch(title, @"^(?:\d+[.)]?\s+)?(?:Open (?:business )?(?:decisions|questions)|Unresolved (?:decisions|questions))\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IReadOnlyList<(string Subject, string Context)> DecisionContexts(string source)
    {
        // Match the existing OpenDecisions model exactly, including its supported
        // level-four through level-six context headings. List order is not scope.
        var section = Regex.Match(source, @"(?im)^#{1,3}\s+(?:\d+[.)]?\s+)?(?:Open (?:business )?(?:decisions|questions)|Unresolved (?:decisions|questions))\s*\r?$\n(?<body>.*?)(?=^#{1,3}\s|\z)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!section.Success) return [];
        var contexts = new List<(string Subject, string Context)>();
        var headings = new List<(int Level, string Title)>();
        var prose = new List<string>();
        foreach (var raw in section.Groups["body"].Value.Split('\n'))
        {
            var line = raw.Trim();
            var heading = Regex.Match(line, @"^(?<level>#{4,6})\s+(?<title>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (heading.Success)
            {
                var level = heading.Groups["level"].Length;
                headings.RemoveAll(item => item.Level >= level);
                headings.Add((level, heading.Groups["title"].Value));
            }
            else if (Regex.IsMatch(line, @"^(?:\d+[.)]|[-*])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                contexts.Add((DecisionSubject(Regex.Replace(line, @"^(?:\d+[.)]|[-*])\s+", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))),
                    string.Join(" / ", headings.Select(item => item.Title))));
            else if (!string.IsNullOrWhiteSpace(raw)) prose.Add(raw);
        }
        var shared = string.Join('\n', prose);
        if (contexts.Count == 0 && shared.Length > 0)
            return [("unassigned source context (impact is conservative)", shared)];
        return contexts.Take(200).Select(item => (item.Subject, item.Context + "\n" + shared)).ToArray();
    }

    private static string ReviewSourceMaterial(string source, string terms)
    {
        // Use the existing document heading model to select complete source
        // sections. Read original ranges so code, comments and full paragraphs
        // inside a selected contract are never truncated or normalized away.
        source = ReviewDocumentMaterial(source);
        var visible = BrdDocumentLayout.Visible(source);
        var decisionHeadings = Regex.Matches(visible, @"(?m)^(?<level>#{1,6})\s+(?<title>[^\r\n]+)\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var decisionRanges = new List<(int Start, int End)>();
        for (var index = 0; index < decisionHeadings.Count; index++)
        {
            var heading = decisionHeadings[index];
            if (!BusinessDecisionHeading(heading.Groups["title"].Value)) continue;
            var next = index + 1;
            while (next < decisionHeadings.Count && decisionHeadings[next].Groups["level"].Length > heading.Groups["level"].Length) next++;
            var end = next < decisionHeadings.Count ? decisionHeadings[next].Index : source.Length;
            decisionRanges.Add((heading.Index, end));
            index = next - 1;
        }
        foreach (var range in decisionRanges.AsEnumerable().Reverse()) source = source[..range.Start] + source[range.End..];
        visible = BrdDocumentLayout.Visible(source);
        var headings = Regex.Matches(visible, @"(?m)^(?<level>#{1,6})\s+(?<title>[^\r\n]+)\r?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var selected = new List<(int Start, int End)>();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            if (!Regex.IsMatch(heading.Groups["title"].Value, terms, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                || BusinessDecisionHeading(heading.Groups["title"].Value)) continue;
            var next = index + 1;
            while (next < headings.Count && headings[next].Groups["level"].Length > heading.Groups["level"].Length) next++;
            var end = next < headings.Count ? headings[next].Index : source.Length;
            if (selected.Count == 0 || heading.Index >= selected[^1].End) selected.Add((heading.Index, end));
        }
        // Keep uncovered, nonempty authored sections conservative at feature
        // scope instead of claiming that an unknown heading proves no impact.
        var unclassified = new List<string>();
        var supportingParagraphs = new List<string>();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            if (selected.Any(range => heading.Index >= range.Start && heading.Index < range.End)) continue;
            var title = heading.Groups["title"].Value;
            var end = index + 1 < headings.Count ? headings[index + 1].Index : source.Length;
            var body = source[(heading.Index + heading.Length)..end];
            foreach (var paragraph in Regex.Split(body, "\n[ \\t]*\n").Where(value => !string.IsNullOrWhiteSpace(value)))
                if (Regex.IsMatch(paragraph, terms, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    supportingParagraphs.Add(title + "\n" + paragraph);
            if (heading.Groups["level"].Length != 1 && Regex.IsMatch(title, string.Join('|', ReviewPages.Select(page => PageSourceTerms(page.Id))),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
            if (!string.IsNullOrWhiteSpace(visible[(heading.Index + heading.Length)..end]))
                unclassified.Add(source[(heading.Index + (heading.Groups["level"].Length == 1 ? heading.Length : 0))..end].Trim());
        }
        var preambleEnd = headings.Count > 0 ? headings[0].Index : source.Length;
        var preamble = source[..preambleEnd].Trim();
        return string.Join("\n\n", selected.Select(range => source[range.Start..range.End].Trim())
            .Concat(supportingParagraphs).Concat(unclassified).Prepend(preamble).Where(value => value.Length > 0));
    }

    private static string ReviewDocumentMaterial(string content)
    {
        // Only explicitly generated provenance/review bookkeeping is excluded.
        // Authored comments, code, ordering and whitespace remain material.
        content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        content = CisFrontMatter.NormalizeApproval(CisBrdPresentation.RestoreManagedEvidence(CisTechnicalIntentPresentation.RestoreManagedEvidence(content)));
        content = Regex.Replace(content, @"<!-- cis:(baseline|sources|feature-traceability|technical-intent-baseline):start -->.*?<!-- cis:\1:end -->", "", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        content = Regex.Replace(content, @"(?<start><!-- cis:technical-intent-questionnaire-evidence:start -->)(?<body>.*?)(?<end><!-- cis:technical-intent-questionnaire-evidence:end -->)", match =>
            match.Groups["start"].Value + Regex.Replace(match.Groups["body"].Value,
                @"(?m)^(?<direction>\|\s*`TI-Q-\d{3}`\s*\|(?:[^|\\]|\\.)*\|(?:[^|\\]|\\.)*\|)(?:[^|\\]|\\.)*\|\s*$",
                "${direction} <recorded direction provenance> |", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) + match.Groups["end"].Value,
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var lines = content.Split('\n');
        for (var index = 1; index < lines.Length; index++)
        {
            const string auditPrefix = "<!-- cis:technical-decision-review ";
            const string basisPrefix = "<!-- cis:technical-decision-basis ";
            var prefix = lines[index].StartsWith(auditPrefix, StringComparison.Ordinal) ? auditPrefix
                : lines[index].StartsWith(basisPrefix, StringComparison.Ordinal) ? basisPrefix : null;
            if (prefix is null || !lines[index].EndsWith(" -->", StringComparison.Ordinal)) continue;
            try
            {
                using var audit = JsonDocument.Parse(lines[index][prefix.Length..^4]);
                var root = audit.RootElement;
                if (!root.TryGetProperty("RowHash", out var rowHash) || rowHash.GetString() != Hash(Encoding.UTF8.GetBytes(lines[index - 1].TrimEnd('\r')))) continue;
                if (prefix == auditPrefix && root.TryGetProperty("Actor", out var actor) && root.TryGetProperty("RecordedAtUtc", out var at))
                {
                    var suffix = " Recorded by: " + (actor.GetString() ?? "").Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ') + " at " + at.GetString() + ". |";
                    if (!lines[index - 1].EndsWith(suffix, StringComparison.Ordinal)) continue;
                    lines[index - 1] = lines[index - 1][..^suffix.Length] + " |";
                    lines[index] = "<!-- cis:technical-decision-meaning " + JsonSerializer.Serialize(new
                    { Id = root.GetProperty("Id").GetString(), Resolution = root.GetProperty("Resolution").GetString(), Reason = root.GetProperty("Reason").GetString() }, Json) + " -->";
                }
                else if (prefix == basisPrefix) lines[index] = "";
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
            { /* An unrecognized authored record remains material. */ }
        }
        content = string.Join('\n', lines);
        var header = Regex.Match(content, @"\A(?:\uFEFF)?---\n.*?\n---(?:\n|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (header.Success)
        {
            var materialHeader = Regex.Replace(header.Value, @"(?m)^(?:last_reviewed|reviewed_at|updated_at|generated_at|last_updated|source_hash|product_definition_hash):[^\n]*\n", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            content = materialHeader + content[header.Length..];
        }
        return content;
    }

    private static string FeatureRequestMaterial(WizardState state)
    {
        static string WithoutRecords(string content)
        {
            content = Regex.Replace(content, Regex.Escape(ReviewStart) + ".*?" + Regex.Escape(ReviewEnd), "", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return Regex.Replace(content, Regex.Escape(RecordMarker) + ".*?\n-->", "", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        var actual = WithoutRecords(state.Content);
        var generated = WithoutRecords(Render(state.Authority.Id, state.RequestPath,
            Path.Combine(state.Authority.RepositoryPath, state.Record.Plan.SourcePath), state.Record, state.Workspace).Replace("\r\n", "\n", StringComparison.Ordinal));
        foreach (var paragraph in Regex.Split(generated, "\n[ \\t]*\n").Where(value => !string.IsNullOrWhiteSpace(value)))
            actual = actual.Replace(paragraph, "", StringComparison.Ordinal);
        return actual.Trim();
    }
}
