using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentQuestionnaireService
{
    private static readonly Lazy<IReadOnlyDictionary<string, (Regex Content, Regex Heading)>> AssessmentTopics = new(CreateAssessmentTopics);

    private static IReadOnlyDictionary<string, (Regex Content, Regex Heading)> CreateAssessmentTopics()
        => DocumentTopics.ToDictionary(item => item.Key, item =>
            (new Regex(item.Value.Content, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
             new Regex(item.Value.Heading, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))), StringComparer.Ordinal);

    // This is a small projection of the existing question/topic relationship, not
    // another source importer. Retained sources still use their exact digests.
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SupportingBases(CisWorkspaceRepository authority)
    {
        var path = CisProductDocumentPaths.Resolve(Path.Combine(authority.RepositoryPath,
            authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar)), "specs", "business-requirements.md");
        var result = Definitions.ToDictionary(item => item.Id, _ => new SortedDictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            foreach (var basis in result.Values) basis["BRD:missing"] = "missing";
            return result.ToDictionary(item => item.Key, item => (IReadOnlyDictionary<string, string>)item.Value, StringComparer.Ordinal);
        }
        var content = File.ReadAllText(path);
        var issues = new List<string>();
        var passages = ReadDocumentPassages(content, issues);
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (passage, raw) in passages.Select(passage => (passage, false))
            .Concat(ReadAssessmentRawSections(content).Select(passage => (passage, true))))
        {
            var ids = AssessmentTopics.Value.Where(topic => topic.Value.Content.IsMatch(passage.Text)
                || topic.Value.Heading.IsMatch(passage.Heading))
                .Select(topic => topic.Key).ToArray();
            var requirement = Regex.Match(passage.Text, @"\bBRD-(?!Q-)\w+-\d+\b",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var global = Regex.IsMatch(passage.Text, @"\b(?:product.wide|all (?:components|repositories|surfaces)|every (?:component|repository|surface))\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            // A requirement with no established topic mapping cannot safely be
            // declared irrelevant. Its stable ID explains the bounded fallback.
            var unmapped = ids.Length == 0;
            var stem = raw ? "raw-literal:" + passage.Heading : requirement.Success ? requirement.Value : passage.Heading;
            ordinals.TryGetValue(stem, out var ordinal); ordinals[stem] = ordinal + 1;
            var key = $"BRD:{(global ? "global:" : unmapped ? "unmapped:" : "")}{stem}:{ordinal}";
            foreach (var id in global || unmapped ? result.Keys.ToArray() : ids)
                result[id][key] = Hash(passage.Text);
        }
        if (issues.Count > 0)
            foreach (var basis in result.Values) basis["BRD:uninterpretable-input"] = Hash(content);
        return result.ToDictionary(item => item.Key, item => (IReadOnlyDictionary<string, string>)item.Value, StringComparer.Ordinal);
    }

    // Fact import deliberately ignores examples and comments. Assessment identity
    // cannot treat a changed literal contract as equivalent, so retain the exact
    // authored range under its real enclosing headings without interpreting it.
    private static IReadOnlyList<DocumentPassage> ReadAssessmentRawSections(string content)
    {
        var lines = CisBrdPresentation.RestoreManagedEvidence(content).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var result = new List<DocumentPassage>();
        var headings = new List<(int Level, string Title)>();
        var body = new List<string>();
        var heading = "Unclassified BRD content"; var start = 1;
        var frontmatter = lines.Length > 0 && lines[0].TrimStart('\uFEFF').Trim() == "---";
        var comment = false; var fence = '\0'; var fenceLength = 0;
        var literal = false; var excludedLevel = 0; string? managedEnd = null;
        void Flush()
        {
            if (literal && excludedLevel == 0) result.Add(new(string.Join('\n', body), heading, start));
            body.Clear(); literal = false;
        }
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index]; var trimmed = line.Trim();
            if (frontmatter) { if (index > 0 && trimmed == "---") frontmatter = false; continue; }
            if (managedEnd is not null) { if (trimmed == managedEnd) managedEnd = null; continue; }
            if (fence != '\0')
            {
                body.Add(line);
                var close = trimmed.TakeWhile(character => character == fence).Count();
                if (close >= fenceLength && trimmed.AsSpan(close).Trim().Length == 0) fence = '\0';
                continue;
            }
            if (!comment && (trimmed is "<!-- cis:baseline:start -->" or "<!-- cis:sources:start -->" or "<!-- cis:feature-traceability:start -->"))
            { managedEnd = trimmed.Replace(":start -->", ":end -->", StringComparison.Ordinal); continue; }
            if (!comment && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                body.Add(line); literal = true; fence = trimmed[0];
                fenceLength = trimmed.TakeWhile(character => character == fence).Count(); continue;
            }
            if (!comment && Regex.Match(line, @"^(?<level>#{1,6})\s+(?<title>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match)
            {
                Flush(); var level = match.Groups["level"].Length; var title = match.Groups["title"].Value;
                if (excludedLevel > 0 && level <= excludedLevel) excludedLevel = 0;
                if (Regex.IsMatch(title, @"^(?:\d+[. ]*)?(?:source and evidence|source register|references\b|revision history)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) excludedLevel = level;
                headings.RemoveAll(item => item.Level >= level); headings.Add((level, title));
                heading = string.Join(" / ", headings.Select(item => item.Title)); start = index + 1;
                continue;
            }
            body.Add(line);
            var cursor = 0;
            while (cursor < line.Length)
            {
                var marker = line.IndexOf(comment ? "-->" : "<!--", cursor, StringComparison.Ordinal);
                if (marker < 0) break;
                literal = true; cursor = marker + (comment ? 3 : 4); comment = !comment;
            }
        }
        Flush();
        return result;
    }

    private static TechnicalIntentQuestion[] AssessQuestions(CisWorkspaceRepository authority,
        IEnumerable<TechnicalIntentQuestion> questions, string? recordedBrd, string currentBrd)
    {
        var bases = SupportingBases(authority);
        return questions.Select(question =>
        {
            var basis = bases[question.Id];
            if (question.ResolutionSource != "human" || string.IsNullOrWhiteSpace(question.Answer))
                return question with { SupportingBasis = basis, AssessmentStatus = "Current", AssessmentReason = null };
            if (question.SupportingBasis is null)
                return recordedBrd == currentBrd && question.AssessmentStatus != "Unknown"
                    ? question with { SupportingBasis = basis, AssessmentStatus = "Current", AssessmentReason = null }
                    : question with { AssessmentStatus = "Unknown", AssessmentReason = "The retained human answer predates scoped supporting evidence and its recorded BRD baseline differs. Review this answer against the current BRD; its author and time are preserved." };
            var changed = ChangedBasis(question.SupportingBasis, basis);
            var unknown = changed.Any(key => key.StartsWith("BRD:unmapped:", StringComparison.Ordinal)
                || key == "BRD:uninterpretable-input");
            return question with
            {
                AssessmentStatus = changed.Length == 0 ? "Current" : unknown ? "Unknown" : "NeedsReview",
                AssessmentReason = changed.Length == 0 ? null : unknown
                    ? "Changed BRD input has no established question/topic applicability: " + string.Join(", ", changed)
                        + ". Because its relevance cannot be bounded, this question conservatively requires review. Its recorded answer and human provenance are retained."
                    : "Supporting BRD input changed: " + string.Join(", ", changed) + ". The recorded answer is retained; reassess this question.",
            };
        }).ToArray();
    }

    internal static string[] ChangedBasis(IReadOnlyDictionary<string, string> recorded, IReadOnlyDictionary<string, string> current)
        => recorded.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(key => !recorded.TryGetValue(key, out var oldValue) || !current.TryGetValue(key, out var value) || oldValue != value).ToArray();

    internal static IReadOnlyDictionary<string, string> CurrentSupportingBasis(CisWorkspaceRepository authority, IEnumerable<string> ids)
    {
        var bases = SupportingBases(authority);
        return ids.SelectMany(id => bases[id].Select(item => new KeyValuePair<string, string>(id + ":" + item.Key, item.Value)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    internal static bool SupportsQuestions(string text, IEnumerable<string> ids)
        => ids.Any(id => AssessmentTopics.Value.TryGetValue(id, out var topic) && topic.Content.IsMatch(text));

    private static bool NeedsReassessment(TechnicalIntentQuestion question) => question.AssessmentStatus is "NeedsReview" or "Unknown";

    private static IReadOnlyDictionary<string, string> QuestionnaireSourceHashes(CisWorkspaceRepository authority)
    {
        var docs = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar));
        var paths = new[] { CisProductDocumentPaths.Resolve(docs, "specs", "business-requirements.md"),
            CisProductDocumentPaths.Resolve(docs, "specs", "technical-intent-spec.md"),
            Path.Combine(authority.RepositoryPath, ".cis", "product-documents.json") };
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, path => File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing", StringComparer.OrdinalIgnoreCase);
    }

    private static bool QuestionnaireSourcesMatch(CisWorkspaceRepository authority, IReadOnlyDictionary<string, string> sources)
    {
        var current = QuestionnaireSourceHashes(authority);
        return sources.Count == current.Count && sources.All(item => !CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, item.Key)
            && current.TryGetValue(item.Key, out var hash) && item.Value == hash);
    }

    private static string RenderAssessment(TechnicalIntentQuestion question)
        => JsonSerializer.Serialize(new QuestionAssessment(question.SupportingBasis, question.AssessmentStatus, question.AssessmentReason, question.History));

    private static QuestionAssessment? ReadAssessment(string body)
    {
        var value = ReadBullet(body, "Assessment record");
        if (value is null) return null;
        try { return JsonSerializer.Deserialize<QuestionAssessment>(value); }
        catch (JsonException) { return null; }
    }

    private sealed record QuestionAssessment(IReadOnlyDictionary<string, string>? SupportingBasis,
        string? Status, string? Reason, IReadOnlyList<TechnicalIntentAnswerHistory>? History);
}
