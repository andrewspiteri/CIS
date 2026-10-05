using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentQuestionnaireService
{
    // Retrieval only: these rules select whole source passages, never infer decisions
    // from absent technology, turn negation into an affirmative choice, or mark answers saved.
    private static readonly IReadOnlyDictionary<string, (string Content, string Heading)> DocumentTopics =
        new Dictionary<string, (string, string)>
        {
            ["TI-Q-001"] = (@"\bCLI\b|command.line|user.facing|browser UI|desktop|mobile|local APIs?|product surfaces", "surface|interface|purpose|runtime architecture"),
            ["TI-Q-002"] = (@"frontend|front.end|browser UI|web UI|React|Angular|Vue|SwiftUI|MAUI|user interface", "frontend|interface|non.goals|exclusion"),
            ["TI-Q-003"] = (@"C#|\.NET|ASP\.NET|Node\.js|Spring Boot|FastAPI|backend|application framework", "stack|backend|runtime|language"),
            ["TI-Q-004"] = (@"monolith|microservice|serverless|processes|process boundar|deployment boundar|module boundar|dependency direction", "architecture|isolation|purpose"),
            ["TI-Q-005"] = (@"repositor(?:y|ies)|monorepo|deployable|release package|ownership layout", "repositor|ownership|layout|topology"),
            ["TI-Q-006"] = (@"PostgreSQL|SQL Server|MySQL|MariaDB|SQLite|transactional (?:database|store)|system of record", "database|datastore|persistence|data store"),
            ["TI-Q-007"] = (@"Redis|cache|message broker|object storage|blob storage|search index|vector store|disk spool|supporting store", "data service|non.goals|storage|spool|cache"),
            ["TI-Q-008"] = (@"HTTP|JSON|gRPC|GraphQL|WebSocket|contract|idempotenc|compatibility", "API|contract|communicat|integration"),
            ["TI-Q-009"] = (@"authenticat|authoriz|permission|API token|trusted.local|identity|tenan", "identity|access|trusted.local|application roles"),
            ["TI-Q-010"] = (@"Windows Services?|Windows hosts?|Windows laptop|deploy|hosting|container|Kubernetes|cloud platform|supervised", "deploy|host|lifecycle|environment"),
            ["TI-Q-011"] = (@"asynchronous|background (?:job|work)|queue|scheduled|polling|notification|outbox|durable workflow|spool", "handoff|asynchronous|background|polling|outbox|publication"),
            ["TI-Q-012"] = (@"observability|telemetry|metrics|logs|diagnos|runbook|readiness|alert", "observability|diagnos|operations|telemetry"),
            ["TI-Q-013"] = (@"security|privacy|sensitive|secrets|compliance|trusted.local|credential|threat|data classification", "security|privacy|trusted.local|configuration"),
            ["TI-Q-014"] = (@"\btests?\b|acceptance|verification|assurance|performance|recovery objectives", "acceptance|test|verification|quality|assurance"),
            ["TI-Q-015"] = (@"\bAI\b|learned model|model lifecycle|training|fine.tun|research.worker|model evaluation|model advice", "AI|model|strategy|research"),
            ["TI-Q-016"] = (@"non.goals|excluded|exclusion|mandated|not required|must not|future option|deferred|licens|no cloud", "non.goals|exclusion|constraint|baseline closure"),
        };

    private static TechnicalIntentQuestion[] SuggestFromSelectedDocument(CisWorkspaceRepository authority,
        TechnicalIntentQuestion[] questions, List<string> warnings)
    {
        try
        {
            var selected = CisProductDocumentPaths.Read(authority.RepositoryPath).GetValueOrDefault("technical");
            if (selected is null) return questions;
            var path = CisProductDocumentPaths.ValidatePath(authority.RepositoryPath, selected);
            if (!File.Exists(path)) return questions;
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
            { warnings.Add("The selected technical intent exceeds the 2 MB local excerpt limit; review its answers in the document."); return questions; }
            var content = File.ReadAllText(path);
            var passages = ReadDocumentPassages(content);
            var digest = Hash(content);
            return questions.Select(question =>
            {
                if (IsResolved(question) || !DocumentTopics.TryGetValue(question.Id, out var topic)
                    || question.SuggestedAnswer.StartsWith("Previously recorded direction", StringComparison.Ordinal)) return question;
                int Score(DocumentPassage passage)
                {
                    var matches = Regex.Matches(passage.Text, topic.Content, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    return matches.Count == 0 ? 0 : Math.Min(4, matches.Select(match => match.Value.ToLowerInvariant()).Distinct().Count())
                        + (Regex.IsMatch(passage.Heading, topic.Heading, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? 4 : 0);
                }
                var ranked = passages.Select(passage => (Passage: passage, Score: Score(passage)))
                    .Where(item => item.Score >= 2).OrderByDescending(item => item.Score).ThenBy(item => item.Passage.Line).ToArray();
                var chosen = new List<DocumentPassage>();
                var length = 0;
                foreach (var item in ranked)
                {
                    if (item.Score < ranked[0].Score - 2 || length + item.Passage.Text.Length > 4_000) continue;
                    chosen.Add(item.Passage); length += item.Passage.Text.Length;
                    if (chosen.Count == 3) break;
                }
                if (chosen.Count == 0) return question;
                chosen.Sort((left, right) => left.Line.CompareTo(right.Line));
                return question with
                {
                    SuggestedAnswer = string.Join("\n\n", chosen.Select(passage => passage.Text)),
                    ResolutionSource = "technical-intent-document", Confidence = null,
                    Evidence = chosen.Select(passage => $"{selected}:{passage.Line} — {passage.Heading} ({digest})").ToArray(),
                };
            }).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or RegexMatchTimeoutException)
        {
            warnings.Add("Could not read suggested directions from the selected technical intent: " + exception.Message);
            return questions;
        }
    }

    private static IReadOnlyList<DocumentPassage> ReadDocumentPassages(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var passages = new List<DocumentPassage>();
        var paragraph = new List<string>();
        var heading = "Technical intent"; var start = 1;
        var inFrontmatter = lines.Length > 0 && lines[0].TrimStart('\uFEFF').Trim() == "---";
        var inComment = false; var inFence = false; var excludedSection = false;
        void Flush()
        {
            if (paragraph.Count > 0)
            {
                var text = string.Join("\n", paragraph).Trim();
                if (!excludedSection && text.Length is >= 30 and <= 1_800 && !text.StartsWith("| Document control", StringComparison.OrdinalIgnoreCase))
                    passages.Add(new(text, heading, start));
                paragraph.Clear();
            }
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i]; var trimmed = line.Trim();
            if (inFrontmatter) { if (i > 0 && trimmed == "---") inFrontmatter = false; continue; }
            if (inComment) { if (trimmed.Contains("-->", StringComparison.Ordinal)) inComment = false; continue; }
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal)) { Flush(); inComment = !trimmed.Contains("-->", StringComparison.Ordinal); continue; }
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) { Flush(); inFence = !inFence; continue; }
            if (inFence) continue;
            if (Regex.IsMatch(line, @"^#{1,6}\s", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                Flush(); heading = line.TrimStart('#', ' ');
                excludedSection = Regex.IsMatch(heading, @"^(?:\d+[. ]*)?(?:CIS |source and evidence|source register|references\b|revision history)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                continue;
            }
            if (trimmed.Length == 0) { Flush(); continue; }
            if (paragraph.Count == 0) start = i + 1;
            paragraph.Add(line);
        }
        Flush();
        return passages;
    }

    private sealed record DocumentPassage(string Text, string Heading, int Line);
}
