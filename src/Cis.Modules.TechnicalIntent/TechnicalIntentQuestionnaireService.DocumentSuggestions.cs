using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentQuestionnaireService
{
    // Topic routing selects original passages, never a generated recommendation.
    private static readonly IReadOnlyDictionary<string, (string Content, string Heading)> DocumentTopics =
        new Dictionary<string, (string, string)>
        {
            ["TI-Q-001"] = (@"\bCLI\b|command.line|user.facing|browser UI|desktop|mobile|local APIs?|product surfaces", "surface|interface|purpose|runtime architecture"),
            ["TI-Q-002"] = (@"frontend|front.end|browser UI|web UI|React|Angular|Vue|SwiftUI|MAUI|user interface", "frontend|interface|non.goals|exclusion"),
            ["TI-Q-003"] = (@"C#|\.NET|ASP\.NET|Node\.js|Spring Boot|FastAPI|backend|application framework", "stack|backend|runtime|language"),
            ["TI-Q-004"] = (@"monolith|microservice|serverless|processes|process boundar|deployment boundar|module boundar|dependency direction", "architecture|isolation|purpose"),
            ["TI-Q-005"] = (@"repositor(?:y|ies)|monorepo|deployable|release package|ownership layout", "repositor|ownership|layout|topology"),
            ["TI-Q-006"] = (@"PostgreSQL|SQL Server|MySQL|MariaDB|SQLite|transactional (?:database|store)|system of record|no (?:new )?database|database (?:is )?not applicable", "database|datastore|persistence|data store"),
            ["TI-Q-007"] = (@"Redis|cache|message broker|object storage|blob storage|search index|vector store|disk spool|supporting store", "data service|non.goals|storage|spool|cache"),
            ["TI-Q-008"] = (@"HTTP|JSON|gRPC|GraphQL|WebSocket|contract|idempotenc|compatibility", "API|contract|communicat|integration"),
            ["TI-Q-009"] = (@"authenticat|authoriz|permission|API token|trusted.local|identity|tenan", "identity|access|trusted.local|application roles"),
            ["TI-Q-010"] = (@"Windows Services?|Windows hosts?|Windows laptop|deploy|hosting|container|Kubernetes|cloud platform|supervised", "deploy|host|lifecycle|environment"),
            ["TI-Q-011"] = (@"asynchronous|background (?:job|work)|queue|scheduled|polling|notification|outbox|durable workflow|spool", "handoff|asynchronous|background|polling|outbox|publication"),
            ["TI-Q-012"] = (@"observability|telemetry|metrics|logs|diagnos|runbook|readiness|alert", "observability|diagnos|operations|telemetry"),
            ["TI-Q-013"] = (@"security|privacy|sensitive|secrets|compliance|trusted.local|credential|threat|data classification", "security|privacy|trusted.local|configuration"),
            ["TI-Q-014"] = (@"\btests?\b|acceptance|verification|assurance|performance|recovery objectives", "acceptance|test|verification|quality|assurance"),
            ["TI-Q-015"] = (@"\bAI\b|learned model|model lifecycle|model training|training (?:a |the )?(?:learned |AI )?models?|fine.tun(?:ing|e|ed) (?:a |the )?(?:learned |AI )?models?|model evaluation|model advice", "AI|model|strategy|research"),
            ["TI-Q-016"] = (@"non.goals|excluded|exclusion|mandated|not required|must not|future option|deferred|licens|no cloud", "non.goals|exclusion|constraint|baseline closure"),
        };

    private static TechnicalIntentQuestion[] ApplyDocumentDirections(CisWorkspaceRepository authority,
        TechnicalIntentQuestion[] questions, List<string> warnings, List<string> errors,
        string expectedBrdVersion, bool brdAccepted)
    {
        try
        {
            var documents = IncludeRecordedStoreAnswer(authority, expectedBrdVersion,
                ReadInputDocuments(authority, expectedBrdVersion, brdAccepted, warnings, errors));
            var conflicts = FindSourceConflicts(documents, errors, includeSelectedDraft: true);
            var result = questions.Select(question =>
            {
                // Manual answers retain their existing authority. A source-derived
                // answer is always re-read; it must never become a fictitious human answer.
                if (question.ResolutionSource == "human" && !conflicts.Contains(question.Id)
                    || !DocumentTopics.TryGetValue(question.Id, out var topic)) return question;
                var chosen = documents.SelectMany(document => document.Passages
                    .Where(passage => TopicMatches(passage, topic))
                    .Select(passage => (Document: document, Passage: passage))).ToArray();
                var accepted = chosen.Where(item => (item.Document.Accepted || item.Document.Working) && IsExplicitDirection(item.Passage)).ToArray();
                var sourceDerived = question.ResolutionSource is "accepted-document" or "selected-document" or "scope-not-requested" or "selected-scope-not-requested";
                if (chosen.Length == 0)
                {
                    // Absence of optional scope is not an exclusion or a technical
                    // choice. Retain the field without asking the user to add scope.
                    var acceptedScope = documents.FirstOrDefault(document => document.Technical && document.Selected && (document.Accepted || document.Working));
                    if (acceptedScope is not null && errors.Count == 0 && question.Id is "TI-Q-007" or "TI-Q-011" or "TI-Q-015")
                        return question with
                        {
                            Status = "Not requested",
                            Answer = null,
                            AnsweredBy = null,
                            AnsweredAtUtc = null,
                            ResolutionSource = acceptedScope.Accepted ? "scope-not-requested" : "selected-scope-not-requested",
                            Confidence = acceptedScope.Accepted ? null : "source-explicit-proposal",
                            SuggestedAnswer = acceptedScope.Accepted
                                ? "This optional capability is not requested in the accepted current scope. No exclusion or implementation choice has been recorded."
                                : "This optional capability is not requested in the selected working scope. Final baseline adoption is pending; no exclusion or implementation choice has been recorded.",
                            Evidence = [$"{acceptedScope.Path}:1 — {(acceptedScope.Accepted ? "Accepted current scope" : "Selected working scope; baseline adoption pending")} ({acceptedScope.Digest})"],
                        };
                    return sourceDerived ? UnresolveSourceQuestion(question) : question;
                }
                var evidence = chosen.Select(item => $"{item.Document.Path}:{item.Passage.Line} — {item.Passage.Heading} ({item.Document.Digest})").ToArray();
                if (conflicts.Contains(question.Id))
                    return (question.ResolutionSource == "human" ? question : UnresolveSourceQuestion(question)) with
                    {
                        Status = "Conflict",
                        ResolutionSource = "source-conflict",
                        SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)),
                        Evidence = evidence,
                    };
                if (accepted.Any(item => AnswersFactualQuestion(question.Id, item.Passage))
                    && !chosen.Any(item => (item.Document.Accepted || item.Document.Working) && IsUnresolvedDirection(item.Passage)))
                    return question with
                    {
                        Status = "Derived",
                        Answer = string.Join("\n\n", accepted.Select(item => item.Passage.Text).Distinct(StringComparer.Ordinal)),
                        AnsweredBy = null,
                        AnsweredAtUtc = null,
                        ResolutionSource = accepted.All(item => item.Document.Accepted) ? "accepted-document" : "selected-document",
                        Confidence = accepted.All(item => item.Document.Accepted) ? "source-explicit" : "source-explicit-proposal",
                        Evidence = accepted.Select(item => $"{item.Document.Path}:{item.Passage.Line} — {item.Passage.Heading} ({item.Document.Digest})").ToArray(),
                    };
                if (IsResolved(question) && !sourceDerived) return question;
                return question with
                {
                    Status = "Unanswered",
                    Answer = null,
                    AnsweredBy = null,
                    AnsweredAtUtc = null,
                    SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)),
                    ResolutionSource = "technical-intent-document",
                    Confidence = null,
                    Evidence = evidence,
                };
            }).ToArray();
            return errors.Any(error => error.StartsWith("Input import problem:", StringComparison.Ordinal))
                ? result.Select(question => IsResolved(question) ? question : question with { Status = "Import problem", ResolutionSource = "import-problem" }).ToArray()
                : result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or RegexMatchTimeoutException)
        {
            errors.Add("Input import problem: the selected direction could not be interpreted: " + exception.Message);
            return questions.Select(question => question.ResolutionSource is "accepted-document" or "selected-document" or "scope-not-requested" or "selected-scope-not-requested" ? UnresolveSourceQuestion(question) : question)
                .Select(question => IsResolved(question) ? question : question with { Status = "Import problem", ResolutionSource = "import-problem" }).ToArray();
        }
    }

    private static TechnicalIntentQuestion UnresolveSourceQuestion(TechnicalIntentQuestion question)
        => question with
        {
            Status = "Unanswered",
            Answer = null,
            AnsweredBy = null,
            AnsweredAtUtc = null,
            ResolutionSource = "unresolved",
            Confidence = null,
            Evidence = [],
            SuggestedAnswer = Definitions.FirstOrDefault(definition => definition.Id == question.Id)?.SuggestedAnswer ?? question.SuggestedAnswer
        };

    internal static string ImportedDirectionsFor(string content, IReadOnlyList<string> ids)
        => string.Join("\n\n", ReadDocumentPassages(content).Where(passage => ids.Any(id =>
            DocumentTopics.TryGetValue(id, out var topic) && TopicMatches(passage, topic)))
            .Select(passage => passage.Text).Distinct(StringComparer.Ordinal));

    internal static string ImportedDecisionClosure(string content)
        => string.Join("\n\n", ReadDocumentPassages(content).Where(passage => Regex.IsMatch(passage.Text,
            @"\b(?:technical\s+)?(?:choices|decisions)\s+(?:are\s+)?(?:now\s+)?closed\b|\bno\s+(?:open|unresolved)\s+technical\s+decisions\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Select(passage => passage.Text));

    private static bool TopicMatches(DocumentPassage passage, (string Content, string Heading) topic)
        => Regex.IsMatch(passage.Text, topic.Content, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
           || Regex.IsMatch(passage.Heading, topic.Heading, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
               && Regex.IsMatch(passage.Text, @"\b(?:Rust|Go|Java|Python|TypeScript|JavaScript|Ruby|PHP|Kotlin|Swift|React|Vue|Angular|Svelte|None)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool IsUnresolvedDirection(DocumentPassage passage)
        => Regex.IsMatch(Regex.Replace(passage.Text, @"\b(?:no|without)\s+(?:open|unresolved)\b|\bmay not\b", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            @"\b(?:TODO|TBD|unresolved|to be decided|under consideration|proposed|recommend(?:ed|ation)?|consider using|may|could|optional)\b|\?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool AnswersFactualQuestion(string id, DocumentPassage passage)
    {
        var pattern = id switch
        {
            "TI-Q-001" => @"\b(?:CLI|command.line|browser UI|desktop|mobile|APIs?)\b",
            "TI-Q-002" => @"\b(?:React|Angular|Vue|Svelte|SwiftUI|MAUI)\b|\b(?:no|excluded|not applicable|deferred)\b.*\b(?:frontend|browser UI|web UI)\b|\b(?:frontend|browser UI|web UI)\b.*\b(?:not required|excluded|not applicable|deferred)\b",
            "TI-Q-003" => @"C#|\.NET|ASP\.NET|Node\.js|Spring Boot|FastAPI|\b(?:Java|Python|Rust|Go|PHP|Ruby|Kotlin)\b|\bno backend\b|\bbackend\s+(?:is )?not applicable\b",
            "TI-Q-004" => @"\b(?:monolith|microservices?|serverless|event.driven services|client.only|existing architecture)\b",
            "TI-Q-006" => @"\b(?:primary (?:database|data store|transactional store)|system of record)\s*(?:is|will be|must be|:|\|)\s*(?:PostgreSQL|SQLite|SQL Server|MySQL|MariaDB)\b|\buse\s+(?:PostgreSQL|SQLite|SQL Server|MySQL|MariaDB)\b|\b(?:no durable application data|no database (?:is )?required|database (?:is )?not applicable)\b|^\s*(?:PostgreSQL|SQLite|SQL Server|MySQL|MariaDB)\.?\s*$|^\s*\|\s*(?:primary (?:database|data store|transactional store)|system of record|database|persistence)\s*\|\s*`?(?:PostgreSQL|SQLite|SQL Server|MySQL|MariaDB)\b",
            "TI-Q-008" => @"\b(?:HTTP|JSON|gRPC|GraphQL|WebSocket|files|batches|in.process|events|messages|SDKs?)\b",
            "TI-Q-015" => @"\b(?:AI|learned models?)\b.*\b(?:excluded|not applicable|not required|deferred)\b|\bno\s+(?:AI|learned models?)\b|\b(?:local model|managed model API|self.hosted model|model hosting|model provider|model governance|human approval required)\b",
            _ => null,
        };
        return pattern is null || Regex.IsMatch(passage.Text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    internal static IReadOnlyList<string> ValidateSuppliedDirections(CisWorkspaceRepository authority, string expectedBrdVersion, bool brdAccepted)
    {
        var errors = new List<string>();
        try
        {
            var documents = IncludeRecordedStoreAnswer(authority, expectedBrdVersion,
                ReadInputDocuments(authority, expectedBrdVersion, brdAccepted, [], errors));
            FindSourceConflicts(documents, errors, includeSelectedDraft: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or RegexMatchTimeoutException)
        { errors.Add("Input import problem: the selected direction could not be interpreted: " + exception.Message); }
        return errors;
    }

    private static bool IsExplicitDirection(DocumentPassage passage)
        => !IsUnresolvedDirection(passage) && Regex.IsMatch(passage.Text,
            @"\b(?:use[sd]?|must|shall|will|is|are|required|excluded|deferred|not applicable|no|none|only|preserve|retain|default|selected|chosen)\b|\||^\s*(?:Rust|Go|Java|Python|TypeScript|JavaScript|Ruby|PHP|Kotlin|Swift|React|Vue|Angular|Svelte)\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IReadOnlyList<InputDocument> IncludeRecordedStoreAnswer(CisWorkspaceRepository authority,
        string expectedBrdVersion, IReadOnlyList<InputDocument> documents)
    {
        var path = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar),
            "specs", "technical-intent-questionnaire.md");
        if (!File.Exists(path)) return documents;
        if (CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path))
            throw new InvalidDataException("The recorded technical questionnaire must not be a link.");
        var content = CisDocumentText.Read(path);
        if (ReadNested(content, "brd_version") != expectedBrdVersion) return documents;
        if (!ParseQuestions(content).TryGetValue("TI-Q-006", out var question)
            || question.ResolutionSource != "human" || string.IsNullOrWhiteSpace(question.Answer)) return documents;
        var match = Regex.Match(content, @"(?m)^### TI-Q-006\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var line = match.Success ? content[..match.Index].Count(character => character == '\n') + 1 : 1;
        var relative = Normalize(Path.GetRelativePath(authority.RepositoryPath, path));
        var heading = $"TI-Q-006 — recorded manual answer by {question.AnsweredBy ?? "unrecorded actor"}";
        // Keep the exact recorded answer. Only recognized scalar choices and
        // explicit primary-store claims participate in the bounded conflict rule.
        return documents.Append(new InputDocument(relative, Hash(content), true, false, false, false,
            [new DocumentPassage(question.Answer, heading, line)], Manual: true)).ToArray();
    }

    private static IReadOnlyList<InputDocument> ReadInputDocuments(CisWorkspaceRepository authority,
        string expectedBrdVersion, bool brdAccepted, List<string> warnings, List<string> errors)
    {
        var docs = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar));
        var paths = new[]
        {
            (Path: CisProductDocumentPaths.Resolve(docs, "specs", "technical-intent-spec.md"), Technical: true),
            (Path: CisProductDocumentPaths.Resolve(docs, "specs", "business-requirements.md"), Technical: false),
        };
        var result = new List<InputDocument>();
        foreach (var item in paths)
        {
            if (!File.Exists(item.Path)) continue;
            var relative = Normalize(Path.GetRelativePath(authority.RepositoryPath, item.Path));
            if (CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, item.Path))
            { errors.Add($"Input import problem: {relative} must not be a link."); continue; }
            if (new FileInfo(item.Path).Length > 20 * 1024 * 1024)
            { errors.Add($"Input import problem: {relative} exceeds the supported 20 MB limit; no excerpt was used to assess completeness."); continue; }
            var content = CisDocumentText.Read(item.Path);
            var selected = CisProductDocumentPaths.Read(authority.RepositoryPath).ContainsKey(item.Technical ? "technical" : "business");
            // Installed starters and generated drafts are not supplied product
            // direction. In particular, their TODO text must never replace useful
            // questionnaire guidance or become a saved source-derived answer.
            if (item.Technical && !selected && TechnicalIntentService.IsUnacceptedGeneratedInput(content)) continue;
            var issues = new List<string>();
            var passages = ReadDocumentPassages(content, issues);
            if (issues.Count > 0)
            { errors.AddRange(issues.Select(issue => $"Input import problem: {relative}: {issue}")); continue; }
            if (passages.Count == 0)
            { errors.Add($"Input import problem: {relative} contains no readable direction outside metadata, examples, and reference evidence."); continue; }
            var accepted = item.Technical
                ? TechnicalIntentService.IsAcceptedInput(content, expectedBrdVersion) && brdAccepted
                : brdAccepted && string.Equals(CisFrontMatter.Read(content, "status"), "Active", StringComparison.OrdinalIgnoreCase);
            var working = !accepted && selected && brdAccepted && CisDefinitionDraftScope.IsActive
                && (!item.Technical || TechnicalIntentService.IsPreparedSelectedInput(content, expectedBrdVersion));
            if (item.Technical && !accepted)
                warnings.Add($"Directions from {relative} remain proposals until the document baseline is adopted and current; source-derived answers are not manual approvals.");
            result.Add(new(relative, Hash(content), accepted, selected, working, item.Technical, passages));
        }
        return result;
    }

    private static HashSet<string> FindSourceConflicts(IReadOnlyList<InputDocument> documents, List<string> errors, bool includeSelectedDraft = false)
    {
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        var primaryStores = new List<(string Value, string Scope, string Citation, string Text)>();
        foreach (var document in documents.Where(item => item.Accepted || includeSelectedDraft && item.Selected))
            foreach (var passage in document.Passages)
                foreach (var statement in Regex.Split(passage.Text, @"(?<=[.!?])\s+|\r?\n(?=\s*(?:[-*+]|\d+[.)])\s)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    // Deliberately bounded, explicit scalar claims. Mentioning an adapter,
                    // migration, exception, test store or a later phase is not a contradiction.
                    if (Regex.IsMatch(statement, @"\b(?:deferred|phase\s*[2-9]|later phase|future|migration|adapter|except|proposed|consider)\b",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
                    var scope = Regex.Match(statement, @"\b(?:production|testing|tests|development)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Value.ToLowerInvariant();
                    var citation = $"{document.Path}:{passage.Line} — {passage.Heading}";
                    if (document.Manual && Regex.Match(statement.Trim(), @"\A`?(?<value>PostgreSQL|SQLite|SQL Server|MySQL|MariaDB)`?\.?\z",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } manual)
                        primaryStores.Add((manual.Groups["value"].Value, scope, citation, statement));
                    foreach (Match match in Regex.Matches(statement,
                                 @"\b(?:primary (?:database|data store|transactional store)|system of record)\s*(?:is|will be|must be|:|\|)\s*(?<value>PostgreSQL|SQLite|SQL Server|MySQL|MariaDB)\b",
                                 RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                        primaryStores.Add((match.Groups["value"].Value, scope, citation, statement));
                }
        var conflicting = primaryStores.GroupBy(item => item.Scope, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).SelectMany(group => group).ToArray();
        if (conflicting.Length > 0)
        {
            conflicts.Add("TI-Q-006");
            errors.Add("Authoritative input conflict: primary data store. Retain and reconcile both statements: "
                + string.Join("; ", conflicting.Select(item => $"{item.Citation} — {item.Text}")));
        }
        return conflicts;
    }

    private static IReadOnlyList<DocumentPassage> ReadDocumentPassages(string content, List<string>? issues = null)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var passages = new List<DocumentPassage>();
        var paragraph = new List<string>();
        var heading = "Technical intent"; var start = 1;
        var inFrontmatter = lines.Length > 0 && lines[0].TrimStart('\uFEFF').Trim() == "---";
        var inComment = false; var fence = '\0'; var fenceLength = 0; var excludedSection = false;
        var excludedLevel = 0;
        string? managedEnd = null;
        void Flush()
        {
            if (paragraph.Count > 0)
            {
                var text = string.Join("\n", paragraph).Trim();
                if (!excludedSection && text.Length > 0 && !text.StartsWith("| Document control", StringComparison.OrdinalIgnoreCase))
                    passages.Add(new(text, heading, start));
                paragraph.Clear();
            }
        }
        string VisibleLine(string raw)
        {
            var visible = new StringBuilder();
            var cursor = 0;
            while (cursor < raw.Length)
            {
                if (inComment)
                {
                    var close = raw.IndexOf("-->", cursor, StringComparison.Ordinal);
                    if (close < 0) break;
                    inComment = false; cursor = close + 3; continue;
                }
                var open = raw.IndexOf("<!--", cursor, StringComparison.Ordinal);
                if (open < 0) { visible.Append(raw.AsSpan(cursor)); break; }
                visible.Append(raw.AsSpan(cursor, open - cursor));
                inComment = true; cursor = open + 4;
            }
            return visible.ToString();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i]; var trimmed = line.Trim();
            if (inFrontmatter) { if (i > 0 && trimmed == "---") inFrontmatter = false; continue; }
            if (fence != '\0')
            {
                var closeLength = trimmed.TakeWhile(character => character == fence).Count();
                if (closeLength >= fenceLength && trimmed.AsSpan(closeLength).Trim().Length == 0) fence = '\0';
                continue;
            }
            if (managedEnd is not null) { if (trimmed == managedEnd) managedEnd = null; continue; }
            if (!inComment && trimmed.StartsWith("<!-- cis:", StringComparison.Ordinal) && trimmed.EndsWith(":start -->", StringComparison.Ordinal))
            { Flush(); managedEnd = trimmed.Replace(":start -->", ":end -->", StringComparison.Ordinal); continue; }
            line = VisibleLine(line); trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            { Flush(); fence = trimmed[0]; fenceLength = trimmed.TakeWhile(character => character == fence).Count(); continue; }
            if (Regex.IsMatch(line, @"^#{1,6}\s", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                Flush(); heading = line.TrimStart('#', ' ');
                var level = line.TakeWhile(character => character == '#').Count();
                if (excludedSection && level <= excludedLevel) excludedSection = false;
                if (Regex.IsMatch(heading, @"^(?:\d+[. ]*)?(?:source and evidence|source register|references\b|revision history)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                { excludedSection = true; excludedLevel = level; }
                continue;
            }
            if (trimmed.Length == 0) { Flush(); continue; }
            if (trimmed.StartsWith('|'))
            {
                Flush();
                // Route complete rows individually: one later-phase row must not
                // hide a conflicting current scalar in another row of the table.
                var separator = @"^\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?$";
                var header = i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), separator, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!header && !Regex.IsMatch(trimmed, separator, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                { start = i + 1; paragraph.Add(line); Flush(); }
                continue;
            }
            if (Regex.IsMatch(line, @"^(?:[-*+]|\d+[.)])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) Flush();
            if (paragraph.Count == 0) start = i + 1;
            paragraph.Add(line);
        }
        Flush();
        if (inFrontmatter) issues?.Add("front matter is not closed.");
        if (inComment) issues?.Add("an HTML comment is not closed, so the remaining input cannot be interpreted.");
        if (fence != '\0') issues?.Add("a code fence is not closed, so the remaining input cannot be interpreted.");
        if (managedEnd is not null) issues?.Add("a managed evidence block is not closed.");
        return passages;
    }

    private sealed record DocumentPassage(string Text, string Heading, int Line);
    private sealed record InputDocument(string Path, string Digest, bool Accepted, bool Selected, bool Working, bool Technical, IReadOnlyList<DocumentPassage> Passages, bool Manual = false);
}
