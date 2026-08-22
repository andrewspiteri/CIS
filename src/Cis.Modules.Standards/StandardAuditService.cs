using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Standards;

public sealed partial class StandardAuditService
{
    private const int MaximumBodyCharacters = 2_000;
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "be", "by", "for", "from", "in", "is", "it", "of", "on", "or", "the", "this", "to", "with",
        "standard", "rule", "must", "should", "may", "repository", "cis", "purpose", "scope", "verification", "exceptions",
    };

    private readonly ICisRepositoryContextResolver _resolver;
    private readonly StandardsGovernanceService _standards;
    private readonly ICisTextGenerationService? _generation;

    public StandardAuditService(ICisRepositoryContextResolver resolver, StandardsGovernanceService standards, ICisTextGenerationService? generation = null)
    {
        _resolver = resolver; _standards = standards; _generation = generation;
    }

    public StandardAuditResult Audit(StandardAuditRequest request)
    {
        var resolution = _resolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess) return Create(null, 0, 0, "unavailable", null, null, [], [], resolution.Errors, request.Strict, request.Fix);
        if (request.MaximumPairs is < 1 or > 1_000) return Create(resolution.Context!.RepositoryPath, 0, 0, "not-run", null, null, [], [], ["--max-pairs must be between 1 and 1000."], request.Strict, request.Fix);
        var context = resolution.Context!; var inventory = _standards.Inventory(context.RepositoryPath);
        if (inventory.ExitCode != 0) return Create(context.RepositoryPath, 0, 0, "not-run", null, null, [], inventory.Warnings, inventory.Errors, request.Strict, request.Fix);

        var documents = inventory.Standards.Select(item => Read(context, item)).ToArray();
        var findings = DeterministicFindings(documents).ToList();
        var candidates = CandidatePairs(documents, request.MaximumPairs).ToArray();
        var warnings = new List<string>(); var llmStatus = "disabled"; string? provider = null; string? model = null;
        if (!request.DisableLlm)
        {
            if (_generation is null) { llmStatus = "unavailable"; warnings.Add("Semantic standards audit is unavailable because no text-generation service is loaded."); }
            else
            {
                var selected = SelectProvider(_generation.GetStatus().Providers, request.Model);
                if (selected is null) { llmStatus = "unavailable"; warnings.Add("No local or configured remote LLM is available; deterministic standards auditing completed without semantic conflict review."); }
                else if (candidates.Length == 0) { llmStatus = "not-needed"; provider = selected.Name; }
                else
                {
                    provider = selected.Name; llmStatus = "completed";
                    if (!selected.IsLocal) warnings.Add($"No local LLM was available; semantic standards audit used configured remote provider '{selected.Name}'.");
                    foreach (var pair in candidates)
                    {
                        var generated = _generation.Generate(new CisTextGenerationRequest(CreatePrompt(pair), Provider: selected.Name, Model: request.Model, AllowRemote: !selected.IsLocal, TimeoutSeconds: selected.IsLocal ? 45 : 120, MaxOutputTokens: 512));
                        model ??= generated.Model;
                        if (!generated.IsSuccess || string.IsNullOrWhiteSpace(generated.Text)) { llmStatus = "partial"; warnings.Add($"Semantic standards audit failed for '{pair.Left.Id}' and '{pair.Right.Id}': {generated.Detail ?? generated.Status}"); continue; }
                        findings.AddRange(ParseModel(generated.Text, pair, warnings, selected.IsLocal ? "local-llm" : "remote-llm"));
                    }
                }
            }
        }

        var distinct = findings.GroupBy(FindingKey, StringComparer.Ordinal).Select(group => group.OrderByDescending(item => item.Method == "deterministic").ThenByDescending(item => item.Confidence).First()).OrderBy(item => Order(item.Kind)).ThenBy(item => string.Join('|', item.Standards), StringComparer.Ordinal).ToArray();
        var result = Create(context.RepositoryPath, documents.Length, candidates.Length, llmStatus, provider, model, distinct, warnings, [], request.Strict, request.Fix);
        if (request.Fix) result = Quarantine(context, documents, result, request.Strict);
        var persisted = result with { Applied = true, JsonPath = ".cis/local/standards/audit.json", MarkdownPath = ".cis/local/standards/audit.md" };
        WriteAudit(context, persisted); return persisted;
    }

    private static IReadOnlyList<StandardAuditFinding> DeterministicFindings(IReadOnlyList<AuditStandard> standards)
    {
        var findings = new List<StandardAuditFinding>();
        foreach (var group in standards.GroupBy(item => item.SemanticHash, StringComparer.Ordinal).Where(group => group.Count() > 1))
            findings.Add(Finding("duplicate", "warning", group.Select(item => item.Id).ToArray(), "Standards contain the same normalized normative content.", group.Select(item => item.Path).ToArray(), "deterministic", 1));
        foreach (var group in standards.SelectMany(item => item.Rules.Select(rule => (item, rule.Id, rule.Text))).GroupBy(item => NormalizeRule(item.Text), StringComparer.Ordinal).Where(group => group.Key.Length > 20 && group.Select(item => item.item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            findings.Add(Finding("overlap", "warning", group.Select(item => item.item.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), "Standards contain materially identical normalized rule text.", group.Select(item => $"{item.Id}: {Truncate(item.Text, 240)}").Take(5).ToArray(), "deterministic", 0.98));
        foreach (var group in standards.SelectMany(item => item.Rules.Select(rule => (StandardId: item.Id, RuleId: rule.Id))).GroupBy(item => item.RuleId, StringComparer.OrdinalIgnoreCase).Where(group => group.Select(item => item.StandardId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            findings.Add(Finding("conflict", "error", group.Select(item => item.StandardId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), $"Stable rule ID '{group.Key}' is declared by several standards.", group.Select(item => item.StandardId).ToArray(), "deterministic", 1));
        return findings;
    }

    private static IEnumerable<AuditPair> CandidatePairs(IReadOnlyList<AuditStandard> standards, int maximum)
    {
        var pairs = new List<AuditPair>();
        for (var left = 0; left < standards.Count; left++) for (var right = left + 1; right < standards.Count; right++)
        {
            var score = Jaccard(standards[left].Terms, standards[right].Terms);
            var sharedTargets = standards[left].Targets
                .Intersect(standards[right].Targets, StringComparer.OrdinalIgnoreCase)
                .Any(target => !string.IsNullOrWhiteSpace(target) && target != "*");
            if (score >= 0.18 || sharedTargets) pairs.Add(new AuditPair(standards[left], standards[right], score));
        }
        return pairs.OrderByDescending(item => item.Similarity).ThenBy(item => item.Left.Id, StringComparer.Ordinal).ThenBy(item => item.Right.Id, StringComparer.Ordinal).Take(maximum);
    }

    private static string CreatePrompt(AuditPair pair) => $$"""
        Review only the supplied repository standard pair. Return JSON only, with at most one finding:
        {"findings":[{"kind":"duplicate|overlap|conflict","confidence":0.0,"summary":"concise","evidence":["left requirement","right requirement"]}]}.
        If there is no material finding, return exactly {"findings":[]}. Do not include analysis, alternatives, markdown, or more than one finding. Keep each evidence item under 160 characters.
        A duplicate has substantially identical normative responsibility. An overlap governs a material shared normative responsibility without contradiction. A conflict exists only when both standards apply to the same situation and require mutually exclusive behavior. Complementary rules, shared target labels, and broad topic similarity are not overlaps or conflicts. For every finding, give two evidence items containing the exact rule ID and a concise rule paraphrase: one grounded in LEFT and one grounded in RIGHT. Model findings are advisory candidates only.
        LEFT ID: {{pair.Left.Id}}
        LEFT TARGETS: {{string.Join(',', pair.Left.Targets)}}
        LEFT CONTENT: {{Truncate(pair.Left.Body, MaximumBodyCharacters)}}
        RIGHT ID: {{pair.Right.Id}}
        RIGHT TARGETS: {{string.Join(',', pair.Right.Targets)}}
        RIGHT CONTENT: {{Truncate(pair.Right.Body, MaximumBodyCharacters)}}
        """;

    private static IReadOnlyList<StandardAuditFinding> ParseModel(string text, AuditPair pair, ICollection<string> warnings, string method)
    {
        try
        {
            using var json = JsonDocument.Parse(ExtractJson(text), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); if (!json.RootElement.TryGetProperty("findings", out var array) || array.ValueKind != JsonValueKind.Array) { warnings.Add("Semantic standards audit returned no findings array."); return []; }
            var results = new List<StandardAuditFinding>(); foreach (var item in array.EnumerateArray())
            {
                var kind = Read(item, "kind").ToLowerInvariant(); var summary = Read(item, "summary"); if (kind is not ("duplicate" or "overlap" or "conflict") || summary.Length == 0) continue;
                var evidence = item.TryGetProperty("evidence", out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? string.Empty).Where(value => value.Length > 0).Take(5).ToArray() : [];
                var confidence = item.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var value) ? Math.Clamp(value, 0, 1) : 0.5;
                if (!MaterialModelFinding(kind, evidence, pair, confidence)) { if (kind == "conflict") warnings.Add($"Semantic standards audit omitted an unsubstantiated conflict for '{pair.Left.Id}' and '{pair.Right.Id}'."); continue; }
                results.Add(Finding(kind, "warning", [pair.Left.Id, pair.Right.Id], summary, evidence, method, confidence));
            }
            return results;
        }
        catch (JsonException exception) { warnings.Add($"Semantic standards audit returned invalid JSON: {exception.Message}"); return []; }
    }

    private static StandardAuditResult Quarantine(CisRepositoryContext context, IReadOnlyList<AuditStandard> standards, StandardAuditResult result, bool strict)
    {
        var ids = result.Findings.Where(item => item.Kind == "conflict").SelectMany(item => item.Standards)
            .Concat(result.Findings.Where(item => item.Kind == "duplicate" && item.Method == "deterministic").SelectMany(item => item.Standards.Order(StringComparer.Ordinal).Skip(1)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return result;
        var selected = standards.Where(item => ids.Contains(item.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var relativeRoot = context.DocumentationRoot + "/standards-quarantine"; var root = Path.Combine(context.DocumentationPath, "standards-quarantine");
        var moves = selected.Select(item => (item.Id, Source: Path.Combine(context.RepositoryPath, item.Path.Replace('/', Path.DirectorySeparatorChar)), Destination: Path.Combine(root, Path.GetFileName(item.Path)), Relative: relativeRoot + "/" + Path.GetFileName(item.Path))).ToArray();
        var errors = moves.Where(move => File.Exists(move.Destination)).Select(move => $"Quarantine destination already exists for '{move.Id}': {move.Relative}").ToList();
        if (errors.Count > 0) return result with { Status = "invalid", ExitCode = 2, Errors = result.Errors.Concat(errors).ToArray() };
        var catalogPath = context.CatalogPath; var matrixPath = Path.Combine(context.DocumentationPath, StandardsGovernanceService.ConformanceMatrixPath.Replace('/', Path.DirectorySeparatorChar));
        var catalog = File.ReadAllText(catalogPath); var matrix = File.ReadAllText(matrixPath); var moved = new List<(string Source, string Destination)>();
        try
        {
            Directory.CreateDirectory(root); foreach (var move in moves) { File.Move(move.Source, move.Destination); moved.Add((move.Source, move.Destination)); }
            Write(catalogPath, RewriteCatalog(catalog, moves.ToDictionary(item => item.Id, item => item.Relative, StringComparer.OrdinalIgnoreCase)));
            Write(matrixPath, RewriteMatrix(matrix, ids));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Write(catalogPath, catalog); Write(matrixPath, matrix); foreach (var move in moved.AsEnumerable().Reverse()) if (File.Exists(move.Destination)) File.Move(move.Destination, move.Source);
            return result with { Status = "invalid", ExitCode = 2, Errors = result.Errors.Concat([$"Standard quarantine failed: {exception.Message}"]).ToArray() };
        }
        var quarantined = selected.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray(); var unresolved = result.Findings.Where(item => !item.Standards.Any(id => quarantined.Contains(id, StringComparer.OrdinalIgnoreCase))).ToArray();
        return result with { Status = "quarantined", ExitCode = strict && unresolved.Length > 0 ? 5 : 0, QuarantinePath = relativeRoot, QuarantinedStandards = quarantined };
    }

    private static string RewriteCatalog(string content, IReadOnlyDictionary<string, string> paths)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'); string? current = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim(); if (trimmed.StartsWith("- id: ", StringComparison.Ordinal)) current = trimmed[6..].Trim();
            if (current is null || !paths.TryGetValue(current, out var path)) continue;
            if (trimmed.StartsWith("path: ", StringComparison.Ordinal)) lines[index] = "    path: " + path;
            else if (trimmed.StartsWith("type: ", StringComparison.Ordinal)) lines[index] = "    type: quarantined-standard";
            else if (trimmed.StartsWith("status: ", StringComparison.Ordinal)) lines[index] = "    status: quarantined";
            else if (trimmed.StartsWith("authority: ", StringComparison.Ordinal)) lines[index] = "    authority: historical";
        }
        return string.Join('\n', lines);
    }

    private static string RewriteMatrix(string content, IReadOnlyList<string> ids)
        => string.Join('\n', content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => ids.Any(id => line.TrimStart().StartsWith("| " + id + " |", StringComparison.OrdinalIgnoreCase)) ? "> " + line : line));

    private static AuditStandard Read(CisRepositoryContext context, StandardDefinition definition)
    {
        var body = StripFrontMatter(File.ReadAllText(Path.Combine(context.RepositoryPath, definition.Path.Replace('/', Path.DirectorySeparatorChar))));
        var rules = StandardRuleParser.Parse(body).Select(rule => new AuditRule(rule.Id, rule.Text)).ToArray();
        var semantic = NormalizeRule(string.Join('\n', rules.Select(rule => "<rule-id> " + rule.Text)));
        return new AuditStandard(definition.Id, definition.Path, definition.Title, definition.Targets, body, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(semantic))), Terms(definition.Title + " " + string.Join(' ', definition.Targets)), rules);
    }

    private static CisAiProviderStatus? SelectProvider(IReadOnlyList<CisAiProviderStatus> providers, string? model)
    {
        var available = providers.Where(item => item.IsAvailable).ToArray(); if (!string.IsNullOrWhiteSpace(model)) { var matching = available.Where(item => item.Models.Any(value => value.Name.Equals(model, StringComparison.OrdinalIgnoreCase))).ToArray(); if (matching.Length > 0) available = matching; }
        return available.OrderByDescending(item => item.IsLocal).ThenBy(item => item.Name, StringComparer.Ordinal).FirstOrDefault();
    }

    private static StandardAuditResult Create(string? repository, int count, int pairs, string llm, string? provider, string? model, IReadOnlyList<StandardAuditFinding> findings, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, bool strict, bool fix)
    {
        var failed = errors.Count > 0 || findings.Any(item => item.Severity == "error") || strict && findings.Count > 0;
        return new StandardAuditResult(errors.Count > 0 ? "invalid" : findings.Count > 0 ? "findings" : "clean", errors.Count > 0 ? 2 : failed ? 5 : 0, repository, count, pairs, findings.Count(item => item.Kind == "duplicate"), findings.Count(item => item.Kind == "overlap"), findings.Count(item => item.Kind == "conflict"), llm, provider, model, fix, false, null, null, null, [], findings, warnings.Distinct().ToArray(), errors.Distinct().ToArray());
    }

    private static StandardAuditFinding Finding(string kind, string severity, IReadOnlyList<string> standards, string summary, IReadOnlyList<string> evidence, string method, double confidence)
    {
        var ordered = standards.Order(StringComparer.Ordinal).ToArray(); var key = kind + '\u001f' + string.Join('\u001f', ordered); var id = "STANDARD-AUDIT-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
        return new StandardAuditFinding(id, kind, severity, ordered, summary, evidence, method, confidence);
    }

    private static void WriteAudit(CisRepositoryContext context, StandardAuditResult result)
    {
        var root = Path.Combine(context.RepositoryPath, ".cis", "local", "standards"); Directory.CreateDirectory(root);
        Write(Path.Combine(root, "audit.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
        var markdown = new StringBuilder().AppendLine("# Standards audit").AppendLine().AppendLine($"- Standards: {result.StandardCount}").AppendLine($"- Candidate pairs: {result.CandidatePairCount}").AppendLine($"- LLM: {result.LlmStatus}{(result.Provider is null ? string.Empty : $" ({result.Provider}/{result.Model})")}").AppendLine($"- Duplicates: {result.DuplicateCount}; overlaps: {result.OverlapCount}; conflicts: {result.ConflictCount}").AppendLine();
        foreach (var finding in result.Findings) { markdown.AppendLine($"## {finding.Kind}: {string.Join(" / ", finding.Standards)}").AppendLine().AppendLine($"- Method: {finding.Method}; confidence: {finding.Confidence:0.00}").AppendLine($"- Summary: {finding.Summary}"); foreach (var evidence in finding.Evidence) markdown.AppendLine($"- Evidence: {evidence.Replace('\r', ' ').Replace('\n', ' ')}"); markdown.AppendLine(); }
        foreach (var id in result.QuarantinedStandards) markdown.AppendLine($"- Quarantined: `{id}`"); foreach (var warning in result.Warnings) markdown.AppendLine($"- Warning: {warning}"); Write(Path.Combine(root, "audit.md"), markdown.ToString());
    }

    private static string StripFrontMatter(string content) { var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF'); if (!normalized.StartsWith("---\n", StringComparison.Ordinal)) return normalized; var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal); return end < 0 ? normalized : normalized[(end + 5)..]; }
    private static string NormalizeRule(string value) => WhitespaceRegex().Replace(value.ToLowerInvariant(), " ").Trim();
    private static HashSet<string> Terms(string value) => WordRegex().Matches(value.ToLowerInvariant()).Select(match => match.Value).Where(word => word.Length > 2 && !StopWords.Contains(word)).ToHashSet(StringComparer.Ordinal);
    private static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right) { if (left.Count == 0 || right.Count == 0) return 0; var intersection = left.Intersect(right, StringComparer.Ordinal).Count(); return intersection / (double)(left.Count + right.Count - intersection); }
    private static bool Contradictory(IReadOnlyList<string> evidence) { if (evidence.Count < 2) return false; var values = evidence.Select(NormalizeRule).ToArray(); return values.Any(value => value.Contains("must ") || value.Contains("requires") || value.Contains("always ")) && values.Any(value => value.Contains("must not") || value.Contains("never ") || value.Contains("prohibit") || value.Contains("forbid")); }
    private static bool MaterialModelFinding(string kind, IReadOnlyList<string> evidence, AuditPair pair, double confidence)
    {
        if (confidence < 0.75 || evidence.Count < 2 || !TryResolveEvidenceRules(evidence, pair, out var leftRule, out var rightRule)) return false;
        if (kind == "conflict") return Contradictory(evidence);
        var ruleSimilarity = Jaccard(Terms(leftRule.Text), Terms(rightRule.Text));
        if (kind == "duplicate") return ruleSimilarity >= 0.6;
        var sharedTarget = pair.Left.Targets.Intersect(pair.Right.Targets, StringComparer.OrdinalIgnoreCase)
            .Any(target => !string.IsNullOrWhiteSpace(target) && target != "*");
        return sharedTarget && ruleSimilarity >= 0.25;
    }
    private static bool TryResolveEvidenceRules(IReadOnlyList<string> evidence, AuditPair pair, out AuditRule leftRule, out AuditRule rightRule)
    {
        leftRule = pair.Left.Rules.FirstOrDefault(rule => evidence.Any(statement => EvidenceGroundsRule(statement, rule)))!;
        rightRule = pair.Right.Rules.FirstOrDefault(rule => evidence.Any(statement => EvidenceGroundsRule(statement, rule)))!;
        return leftRule is not null && rightRule is not null;
    }
    private static bool EvidenceGroundsRule(string evidence, AuditRule rule)
    {
        if (!evidence.Contains(rule.Id, StringComparison.OrdinalIgnoreCase)) return false;
        var ruleTerms = Terms(rule.Text); var evidenceTerms = Terms(evidence);
        var required = Math.Min(2, ruleTerms.Count);
        return required > 0 && ruleTerms.Intersect(evidenceTerms, StringComparer.Ordinal).Take(required).Count() == required;
    }
    private static string Read(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    private static string ExtractJson(string text) { var start = text.IndexOf('{'); var end = text.LastIndexOf('}'); return start >= 0 && end > start ? text[start..(end + 1)] : text; }
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static string FindingKey(StandardAuditFinding finding) => finding.Kind + '\u001f' + string.Join('\u001f', finding.Standards.Order(StringComparer.Ordinal));
    private static int Order(string kind) => kind switch { "conflict" => 0, "duplicate" => 1, _ => 2 };
    private static void Write(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, overwrite: true); }
    private sealed record AuditStandard(string Id, string Path, string Title, IReadOnlyList<string> Targets, string Body, string SemanticHash, IReadOnlySet<string> Terms, IReadOnlyList<AuditRule> Rules);
    private sealed record AuditRule(string Id, string Text);
    private sealed record AuditPair(AuditStandard Left, AuditStandard Right, double Similarity);
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"[a-z0-9][a-z0-9-]+", RegexOptions.CultureInvariant)] private static partial Regex WordRegex();
}
