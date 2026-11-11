using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentQuestionnaireService
{
    private const string PrimaryStoreValues = "PostgreSQL|SQLite|SQL Server|MySQL|MariaDB";
    private const string ModelExclusionDirection = @"\b(?:AI|learned models?)\b[^.!?]*\b(?:excluded|not applicable|not required|deferred|do(?:es)? not become selected architecture)\b|\bno\s+(?:AI|learned models?)\b";

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
            var brdBases = SupportingBases(authority);
            // Broken extraction affects the document; a category-specific issue
            // below stays focused on its question instead of adding optional scope.
            var documentImportProblem = errors.Any(error => error.StartsWith("Input import problem:", StringComparison.Ordinal));
            var result = questions.Select(question =>
            {
                // Manual answers retain their existing authority. A source-derived
                // answer is always re-read; it must never become a fictitious human answer.
                if (question.ResolutionSource == "human" && !conflicts.Contains(question.Id)
                    || !DocumentTopics.TryGetValue(question.Id, out var topic)) return question;
                var chosen = documents.SelectMany(document => document.Passages
                    .Where(passage => TopicMatches(question.Id, passage, topic, document.Technical))
                    .Select(passage => (Document: document, Passage: passage))).ToArray();
                var categories = chosen.Where(item => IsSupportingServiceCategory(question.Id, item.Passage, item.Document.Technical))
                    .GroupBy(item => (item.Document, item.Passage.HeadingLine)).ToArray();
                var recognizedCategories = categories.Where(group => group.Any(item => AnswersFactualQuestion(question.Id, item.Passage)))
                    .Select(group => group.Key).ToHashSet();
                var explicitCategories = categories.Where(group => group.Any(item => AnswersFactualQuestion(question.Id, item.Passage) && IsExplicitDirection(item.Passage)))
                    .Select(group => group.Key).ToHashSet();
                var accepted = chosen.Where(item => (item.Document.Accepted || item.Document.Working)
                    && !IsUnresolvedDirection(item.Passage)
                    && (IsExplicitDirection(item.Passage) || explicitCategories.Contains((item.Document, item.Passage.HeadingLine)))).ToArray();
                var sourceDerived = question.ResolutionSource is "accepted-document" or "selected-document" or "scope-not-requested" or "selected-scope-not-requested";
                var retainedFact = RetainedAcceptedSourceFact(question, documents);
                var settledDirection = accepted.Any(item => AnswersFactualQuestion(question.Id, item.Passage))
                    && !chosen.Any(item => (item.Document.Accepted || item.Document.Working) && IsUnresolvedDirection(item.Passage));
                // Selection and copied approval metadata cannot replace historical
                // authority. The existing adoption must also bind this product's
                // canonical technical role to B, and B must actually answer the fact.
                var adoptedReplacement = !documentImportProblem && settledDirection
                    && accepted.Any(item => item.Document.Adopted && AnswersFactualQuestion(question.Id, item.Passage));
                if (retainedFact is not null && !adoptedReplacement && documents.Any(document => document.Adopted))
                    retainedFact = RetainSourceFact(retainedFact, retainedFact.SourceFactAuthority?.Basis, "NeedsReview",
                        $"The adopted replacement does not provide settled direction for {question.Id}. The predecessor's answer and provenance remain historical; resolve the replacement's missing or unresolved direction before renewing this fact.");
                if (chosen.Length == 0)
                {
                    if (retainedFact is not null) return retainedFact;
                    // Absence of optional scope is not an exclusion or a technical
                    // choice. Retain the field without asking the user to add scope.
                    var acceptedScope = documents.FirstOrDefault(document => document.Technical && document.Selected && (document.Accepted || document.Working));
                    if (acceptedScope is not null && !documentImportProblem && question.Id is "TI-Q-007" or "TI-Q-011" or "TI-Q-015")
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
                // A recognised category can contain ownership or rebuild details
                // alongside its service choice. Assess that heading occurrence as
                // one context, without hiding an unreadable separate occurrence.
                var uninterpreted = categories.Where(group => !recognizedCategories.Contains(group.Key)
                    && !group.Any(item => IsUnresolvedDirection(item.Passage))
                    && group.Any(item => item.Document.Selected || item.Document.Accepted)).SelectMany(group => group).ToArray();
                if (uninterpreted.Length > 0)
                {
                    errors.Add($"Input import problem: {question.Id} contains relevant supporting-service direction that could not be safely interpreted: "
                        + string.Join("; ", uninterpreted.Select(item => $"{item.Document.Path}:{item.Passage.Line} — {item.Passage.Heading} — {item.Passage.Text}")));
                    if (retainedFact is not null)
                        return RetainSourceFact(retainedFact, retainedFact.SourceFactAuthority?.Basis, "Unknown",
                            "The accepted source fact is retained, but relevant supporting-service direction could not be safely interpreted. Resolve the reported input import problem before renewing this assessment.")
                            with
                        { Status = "Import problem", SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)) };
                    return UnresolveSourceQuestion(question) with
                    {
                        Status = "Import problem",
                        ResolutionSource = "import-problem",
                        SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)),
                        Evidence = evidence,
                    };
                }
                if (conflicts.Contains(question.Id))
                {
                    if (question.ResolutionSource == "accepted-document" && !string.IsNullOrWhiteSpace(question.Answer))
                        return RetainSourceFact(question, question.SourceFactAuthority?.Basis, "NeedsReview",
                            "The accepted source fact is retained, but current authoritative inputs conflict. Reconcile the cited source statements before renewing its assessment.")
                            with
                        { Status = "Conflict", SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)) };
                    return (question.ResolutionSource == "human" ? question : UnresolveSourceQuestion(question)) with
                    {
                        Status = "Conflict",
                        ResolutionSource = "source-conflict",
                        SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)),
                        Evidence = evidence,
                    };
                }
                if (retainedFact is not null && !adoptedReplacement)
                    return retainedFact with { SuggestedAnswer = string.Join("\n\n", chosen.Select(item => item.Passage.Text)) };
                if (settledDirection)
                    return question with
                    {
                        Status = "Derived",
                        Answer = string.Join("\n\n", accepted.Select(item => item.Passage.Text).Distinct(StringComparer.Ordinal)),
                        AnsweredBy = null,
                        AnsweredAtUtc = null,
                        ResolutionSource = accepted.All(item => item.Document.Accepted) ? "accepted-document" : "selected-document",
                        Confidence = accepted.All(item => item.Document.Accepted) ? "source-explicit" : "source-explicit-proposal",
                        Evidence = accepted.Select(item => $"{item.Document.Path}:{item.Passage.Line} — {item.Passage.Heading} ({item.Document.Digest})").ToArray(),
                        SourceFactAuthority = accepted.All(item => item.Document.Accepted)
                            ? new(SourceFactBasis(accepted.Select(item => item.Document).Distinct(), question.Id)) : null,
                        SupportingBasis = brdBases[question.Id],
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
            return documentImportProblem
                ? result.Select(question => IsResolved(question) || question.SourceFactAuthority is not null ? question : question with { Status = "Import problem", ResolutionSource = "import-problem" }).ToArray()
                : result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or RegexMatchTimeoutException)
        {
            errors.Add("Input import problem: the selected direction could not be interpreted: " + exception.Message);
            return questions.Select(question => question.ResolutionSource == "accepted-document" && !string.IsNullOrWhiteSpace(question.Answer)
                    ? RetainSourceFact(question, question.SourceFactAuthority?.Basis, "Unknown", "The accepted source fact is retained, but its current input could not be interpreted. Resolve the reported import problem before renewing its assessment.") with { Status = "Import problem" }
                    : question.ResolutionSource is "selected-document" or "scope-not-requested" or "selected-scope-not-requested" ? UnresolveSourceQuestion(question) : question)
                .Select(question => IsResolved(question) || question.SourceFactAuthority is not null ? question : question with { Status = "Import problem", ResolutionSource = "import-problem" }).ToArray();
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
            SourceFactAuthority = null,
            Evidence = [],
            SuggestedAnswer = Definitions.FirstOrDefault(definition => definition.Id == question.Id)?.SuggestedAnswer ?? question.SuggestedAnswer
        };

    private static TechnicalIntentQuestion RetainSourceFact(TechnicalIntentQuestion question,
        IReadOnlyDictionary<string, string>? basis, string status, string? reason)
        => question with { SourceFactAuthority = new(basis, status, reason), AssessmentStatus = status, AssessmentReason = reason };

    private static IReadOnlyDictionary<string, string> SourceFactBasis(IEnumerable<InputDocument> documents, string id)
        => documents.ToDictionary(document => document.Path, document => Hash(string.Join("\n\n", document.Passages
            .Where(passage => TopicMatches(id, passage, DocumentTopics[id], document.Technical)).Select(passage => passage.Text))
            + "\n" + string.Join("\n\n", ReadAssessmentRawSections(TechnicalIntentService.WithoutDecisionAssessmentMetadata(document.Content)).Where(passage =>
                AssessmentTopics.Value[id].Content.IsMatch(passage.Text) || AssessmentTopics.Value[id].Heading.IsMatch(passage.Heading))
                .Select(passage => passage.Text))), StringComparer.Ordinal);

    private static TechnicalIntentQuestion? RetainedAcceptedSourceFact(TechnicalIntentQuestion question, IReadOnlyList<InputDocument> documents)
    {
        if (question.ResolutionSource != "accepted-document" || string.IsNullOrWhiteSpace(question.Answer)) return null;
        var basis = question.SourceFactAuthority?.Basis;
        var originals = basis is null ? documents.Where(document => (question.Evidence ?? []).Any(evidence =>
            evidence.StartsWith(document.Path + ":", StringComparison.Ordinal))).ToArray()
            : documents.Where(document => basis.ContainsKey(document.Path)).ToArray();
        if (originals.Length > 0 && originals.All(document => document.Accepted)
            && (basis is null || originals.Length == basis.Count)) return null;
        if (basis is null)
        {
            var evidence = question.Evidence ?? [];
            if (originals.Length == 0 || evidence.Any(item => !originals.Any(document =>
                item.StartsWith(document.Path + ":", StringComparison.Ordinal) && item.EndsWith("(" + document.Digest + ")", StringComparison.Ordinal)))
                || originals.Any(document => !document.ApprovedContent && !document.Accepted))
                return RetainSourceFact(question, null, "Unknown", "The accepted source fact is retained, but its original scoped source compatibility cannot be established. Review the changed source; its historical provenance is preserved.");
            basis = SourceFactBasis(originals, question.Id);
        }
        var current = SourceFactBasis(originals, question.Id);
        var changed = ChangedBasis(basis, current);
        return RetainSourceFact(question, basis, changed.Length == 0 ? "Current" : "NeedsReview", changed.Length == 0 ? null
            : "The accepted source fact is retained, but relevant source direction changed: " + string.Join(", ", changed)
                + ". Review and adopt the changed source before replacing this recorded fact.");
    }

    internal static string ImportedDirectionsFor(string content, IReadOnlyList<string> ids)
        => string.Join("\n\n", ReadDocumentPassages(content).Where(passage => ids.Any(id =>
            DocumentTopics.TryGetValue(id, out var topic) && TopicMatches(id, passage, topic, technical: true)))
            .Select(passage => passage.Text).Distinct(StringComparer.Ordinal));

    internal static string ImportedDecisionClosure(string content)
        => string.Join("\n\n", ReadDocumentPassages(content).Where(passage => Regex.IsMatch(passage.Text,
            @"\b(?:technical\s+)?(?:choices|decisions)\s+(?:are\s+)?(?:now\s+)?closed\b|\bno\s+(?:open|unresolved)\s+technical\s+decisions\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Select(passage => passage.Text));

    private static bool TopicMatches(string id, DocumentPassage passage, (string Content, string Heading) topic, bool technical)
        => Regex.IsMatch(passage.Text, topic.Content, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
           || IsSupportingServiceCategory(id, passage, technical)
           || Regex.IsMatch(passage.Heading, topic.Heading, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
               && Regex.IsMatch(passage.Text, @"\b(?:Rust|Go|Java|Python|TypeScript|JavaScript|Ruby|PHP|Kotlin|Swift|React|Vue|Angular|Svelte|None)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // An unambiguous question-category heading supplies context for its body;
    // recognizing relevant content must not depend on an unrelated framework name.
    // Broad headings such as exclusions still require a topical body match.
    private static bool IsSupportingServiceCategory(string id, DocumentPassage passage, bool technical)
        => technical && id == "TI-Q-007" && Regex.IsMatch(passage.Heading,
            @"\b(?:supporting|additional)\s+(?:data\s+services?|(?:data\s+)?stores?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool IsUnresolvedDirection(DocumentPassage passage)
    {
        var text = Regex.Replace(passage.Text, @"\b(?:no|without)\s+(?:open|unresolved)\b|\bmay not\b", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // A permission about payload content is compatible with a settled transport;
        // the object, rather than an "optional" adjective, identifies that permission.
        // A transport choice in that same clause is still tentative.
        const string transports = "HTTP(?:/JSON)?|gRPC|GraphQL|WebSockets?";
        text = Regex.Replace(text, @"\bmay\s+(?:send|include|provide)\s+(?:(?:an?|the)\s+)?(?:optional\s+)?(?:(?!or\b)[\w-]+\s+){0,3}(?:IDs?|identifiers?|headers?|fields?|metadata|payloads?)\b(?=(?<tail>[^.;!?\r\n]*))",
            match => Regex.IsMatch(match.Groups["tail"].Value,
                @"\b(?:" + transports + @")\b\s+(?:or|versus|vs\.?)\s+(?:" + transports + @")\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? match.Value : string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.IsMatch(text,
            @"\b(?:TODO|TBD|unresolved|undecided|to be decided|under consideration|proposed|recommend(?:ed|ation)?|consider using|may|could)\b|\?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || HasPrimaryStoreAlternatives(ReadPrimaryStoreRow(passage.Text));
    }

    private static bool AnswersFactualQuestion(string id, DocumentPassage passage)
    {
        if (id == "TI-Q-006")
            return ReadPrimaryStoreClaims(passage).Any() || Regex.IsMatch(passage.Text,
                @"\b(?:no durable application data|no database (?:is )?required|database (?:is )?not applicable|no (?:new )?transactional (?:application )?system of record (?:is )?(?:introduced|required|selected))\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var pattern = id switch
        {
            "TI-Q-001" => @"\b(?:CLI|command.line|browser UI|desktop|mobile|APIs?)\b",
            "TI-Q-002" => @"\b(?:React|Angular|Vue|Svelte|SwiftUI|MAUI)\b|\b(?:no|excluded|not applicable|deferred)\b.*\b(?:frontend|browser UI|web UI)\b|\b(?:frontend|browser UI|web UI)\b.*\b(?:not required|excluded|not applicable|deferred)\b",
            "TI-Q-003" => @"C#|\.NET|ASP\.NET|Node\.js|Spring Boot|FastAPI|\b(?:Java|Python|Rust|Go|PHP|Ruby|Kotlin)\b|\bno backend\b|\bbackend\s+(?:is )?not applicable\b",
            "TI-Q-004" => @"\b(?:monolith|microservices?|serverless|event.driven services|client.only|existing architecture)\b",
            "TI-Q-007" => @"\b(?:Redis|cache|search(?: index)?|object storage|blob storage|graph (?:database|store)|analytics warehouse|vector store|message broker|disk spool|supporting store|none)\b|\bno\s+(?:additional|supporting)\s+(?:data\s+)?(?:services?|stores?)\b",
            "TI-Q-008" => @"\b(?:HTTP|JSON|gRPC|GraphQL|WebSocket|files|batches|in.process|events|messages|SDKs?)\b",
            "TI-Q-015" => ModelExclusionDirection + @"|\b(?:local model|managed model API|self.hosted model|model hosting|model provider|model governance|human approval required)\b",
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
            @"\b(?:use[sd]?|must|shall|will|is|are|required|excluded|deferred|not applicable|no|none|only|preserve|retain|default|selected|chosen|may|can|permitted|allowed)\b|\||^\s*(?:Rust|Go|Java|Python|TypeScript|JavaScript|Ruby|PHP|Kotlin|Swift|React|Vue|Angular|Svelte)\.?\s*$",
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
                warnings.Add($"The aggregate baseline for {relative} needs review or adoption. Retained accepted facts keep their original provenance; new or changed direction is not accepted until adoption.");
            result.Add(new(relative, Hash(content), accepted, selected, working, item.Technical, passages,
                Content: content, ApprovedContent: item.Technical && TechnicalIntentService.HasCurrentApproval(content),
                Adopted: item.Technical && selected && accepted && HasAdoptedTechnicalRole(authority, item.Path, content)));
        }
        return result;
    }

    private static bool HasAdoptedTechnicalRole(CisWorkspaceRepository authority, string path, string content)
        => CisFrontMatter.Read(content, "stable_id", nested: true)?.Trim('"', '\'') == $"{authority.Id}:spec:technical-intent"
            && TechnicalIntentService.HasActiveTechnicalCatalogBinding(authority, path);

    private static HashSet<string> FindSourceConflicts(IReadOnlyList<InputDocument> documents, List<string> errors, bool includeSelectedDraft = false)
    {
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        var primaryStores = new List<(string Value, string Scope, string Citation, string Text)>();
        foreach (var document in documents.Where(item => item.Accepted || includeSelectedDraft && item.Selected))
            foreach (var passage in document.Passages)
                foreach (var claim in ReadPrimaryStoreClaims(passage, forConflict: true))
                    primaryStores.Add((claim.Value, claim.Scope, $"{document.Path}:{passage.Line} — {passage.Heading}", claim.Text));
        var conflicting = primaryStores.GroupBy(item => item.Scope, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).SelectMany(group => group).ToArray();
        if (conflicting.Length > 0)
        {
            conflicts.Add("TI-Q-006");
            errors.Add("Authoritative input conflict: primary data store. Retain and reconcile both statements: "
                + string.Join("; ", conflicting.Select(item => $"{item.Citation} — {item.Text}")));
        }
        var modelScope = documents.Where(item => item.Accepted || includeSelectedDraft && item.Selected)
            .SelectMany(document => document.Passages.SelectMany(passage => ReadModelScopeClaims(passage)
                .Select(claim => (claim.Selected, claim.Text, Citation: $"{document.Path}:{passage.Line} — {passage.Heading}")))).ToArray();
        if (modelScope.Select(claim => claim.Selected).Distinct().Count() > 1)
        {
            conflicts.Add("TI-Q-015");
            errors.Add("Authoritative input conflict: AI/model scope. Retain and reconcile both statements: "
                + string.Join("; ", modelScope.Select(item => $"{item.Citation} — {item.Text}")));
        }
        return conflicts;
    }

    private static IEnumerable<(bool Selected, string Text)> ReadModelScopeClaims(DocumentPassage passage)
    {
        foreach (var direction in Regex.Split(passage.Text, @"(?<=[.!?;])\s+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            // A future option is neither a current model selection nor a
            // contradiction of the accepted current exclusion.
            if (Regex.IsMatch(direction, @"\b(?:future|later phase|phase\s*[2-9]|proposed|consider|optional|may|could)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
            if (Regex.IsMatch(direction, ModelExclusionDirection,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                yield return (false, direction);
            if (Regex.Matches(direction,
                @"\b(?:use[sd]?|require[sd]?|must use|shall use|will use)\s+(?:(?:a|an|the)\s+)?(?:AI|learned models?|local model|managed model API|self.hosted model)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Any(selection => !Regex.IsMatch(direction[..selection.Index], @"\b(?:not|never)\s+\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
                yield return (true, direction);
        }
    }

    // One bounded scalar interpretation is used for source derivation and for
    // comparison with recorded answers. Scope qualifications still control
    // whether an otherwise recognized claim can contradict another claim.
    private static Match ReadPrimaryStoreRow(string text)
        => Regex.Match(text,
            @"^\s*\|\s*(?:primary (?:database|data store|transactional store)|system of record|database|datastore|persistence)\s*\|\s*(?<direction>[^|]+)(?:\|(?<notes>.*)|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool HasPrimaryStoreAlternatives(Match row)
        => row.Success && Regex.IsMatch(row.Groups["direction"].Value,
            @"\b(?:" + PrimaryStoreValues + @")\b`?(?:\s+\d[\w.+-]*`?)?\s+(?:or|versus|vs\.?)\s+`?(?:" + PrimaryStoreValues + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IReadOnlyList<string> ReadPrimaryStoreNotes(Match row)
        => row.Success ? Regex.Split(row.Groups["notes"].Value, @"(?<!\\)\|")
            .Select(cell => cell.Trim().Trim('`').Trim()).Where(cell => cell.Length > 0).ToArray() : [];

    private static bool IsDeferredStoreNote(string note)
        => Regex.IsMatch(note,
            @"\A(?:deferred\s+(?:to|until)\s+(?:phase\s*[2-9]\b|(?:a\s+)?later\s+phase\b)|(?:only\s+(?:for|in)\s+)?phase\s*[2-9]\b|(?:for\s+)?(?:migration|adapter)\s+only\b|future\s+(?:option|phase|work)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static bool IsSupportingStoreNote(string note)
        => Regex.IsMatch(note,
            @"\A(?:(?:local\s+)?(?:cache|search index|analytics|supporting store)\s+only|(?:used\s+)?(?:as|for)\s+(?:(?:a|an|the|local)\s+)*(?:cache|search index|analytics|supporting store)(?:\s+only)?)(?:[.;:(]|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string PrimaryStoreNoteScope(string note)
    {
        var scope = Regex.Match(note,
            @"\A(?:(?:for|in)\s+)?(?<scope>production|testing|tests?|development)(?:\s+only)?(?:[.;:(]|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["scope"].Value.ToLowerInvariant();
        return scope is "test" or "tests" ? "testing" : scope;
    }

    private static bool HasDeferredStoreQualification(string direction)
    {
        var stores = Regex.Matches(direction, @"\b(?:" + PrimaryStoreValues + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (stores.Count != 1) return false;
        // A strong suffix qualifies this scalar claim. An incidental migration
        // or exception elsewhere in a multi-store statement does not discard it.
        var suffix = direction[(stores[0].Index + stores[0].Length)..].TrimStart().TrimStart('`').TrimStart();
        suffix = Regex.Replace(suffix, @"\A\d[\w.+-]*`?\s+", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return IsDeferredStoreNote(suffix);
    }

    private static IEnumerable<(string Value, string Scope, string Text)> ReadPrimaryStoreClaims(DocumentPassage passage, bool forConflict = false)
    {
        const string values = PrimaryStoreValues;
        // Parse the row before splitting its direction into statements. Notes
        // qualify every statement, while each sentence retains its own scope.
        var row = ReadPrimaryStoreRow(passage.Text);
        if (HasPrimaryStoreAlternatives(row)) yield break;
        var sourceDirection = row.Success ? row.Groups["direction"].Value : passage.Text;
        // Extra columns can be ownership metadata or actual qualifications.
        // Only complete qualifying phrases affect the current-store claim.
        var notes = ReadPrimaryStoreNotes(row);
        if (notes.Any(IsDeferredStoreNote) || notes.Any(IsSupportingStoreNote)) yield break;
        var noteScope = notes.Select(PrimaryStoreNoteScope).FirstOrDefault(value => value.Length > 0) ?? string.Empty;
        foreach (var sentence in Regex.Split(sourceDirection, @"(?<=[.!?])\s+|\r?\n(?=\s*(?:[-*+]|\d+[.)])\s)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            // A second store after a clause separator owns its qualifications;
            // alternatives joined by "or" remain one unresolved statement.
            foreach (var direction in Regex.Split(sentence, @"(?:;\s*|,\s*with\s+)(?=(?:use\s+)?`?(?:" + values + @")\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                var statement = row.Success ? passage.Text : sentence;
                if (forConflict && Regex.IsMatch(direction, @"\b(?:deferred|phase\s*[2-9]|later phase|future|migration|adapter|except|proposed|consider)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
                var scope = Regex.Match(direction, @"\b(?:production|testing|tests|development)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Value.ToLowerInvariant();
                if (scope.Length == 0) scope = noteScope;
                if (scope == "tests") scope = "testing";
                if (!forConflict && (scope is "testing" or "development" || noteScope is "testing" or "development"
                        || HasDeferredStoreQualification(direction))) continue;
                if (Regex.IsMatch(direction,
                        @"(?:\buse\s+|\A\s*)`?(?:" + values + @")`?(?:\s+\d[\w.+-]*)?`?\s+(?:as|for)\s+(?:(?:a|an|the|local)\s+)*(?:cache|search index|analytics|supporting store)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
                if ((row.Success || scope.Length > 0) && Regex.Match(direction, @"^\s*`?(?<value>" + values + @")\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } cell)
                {
                    yield return (cell.Groups["value"].Value, scope, statement);
                    continue;
                }
                var pattern = @"\b(?:primary (?:database|data store|transactional store)|system of record)\s*(?:is|will be|must be|:)\s*`?(?<value>" + values
                    + @")\b|\buse\s+`?(?<value>" + values + @")\b|^\s*`?(?<value>" + values
                    + @")`?(?:\s+in\s+(?:production|testing|tests|development))?\.?\s*$";
                foreach (Match match in Regex.Matches(direction, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    yield return (match.Groups["value"].Value, scope, statement);
            }
    }

    private static IReadOnlyList<DocumentPassage> ReadDocumentPassages(string content, List<string>? issues = null)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var passages = new List<DocumentPassage>();
        var paragraph = new List<string>();
        var heading = "Technical intent"; var headingLine = 0; var start = 1;
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
                    passages.Add(new(text, heading, start, headingLine));
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
                Flush(); heading = line.TrimStart('#', ' '); headingLine = i + 1;
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

    private sealed record DocumentPassage(string Text, string Heading, int Line, int HeadingLine = 0);
    private sealed record InputDocument(string Path, string Digest, bool Accepted, bool Selected, bool Working, bool Technical,
        IReadOnlyList<DocumentPassage> Passages, bool Manual = false, string Content = "", bool ApprovedContent = false, bool Adopted = false);
}
