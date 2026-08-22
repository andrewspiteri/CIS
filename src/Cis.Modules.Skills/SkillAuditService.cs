using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Skills;

public sealed partial class SkillAuditService
{
    private const int MaximumBodyCharactersPerSkill = 1_500;
    // Small local models can leak evidence between pairs when several are reviewed together.
    // One pair per prompt keeps every semantic judgment independently attributable.
    private const int PairsPerPrompt = 1;
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "be", "before", "by", "for", "from", "in", "into",
        "is", "it", "of", "on", "or", "repository", "skill", "the", "this", "to", "use", "when", "with",
        "workflow", "cis", "add", "review", "implement", "implementation", "change", "changes",
    };

    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly SkillValidationService _validationService;
    private readonly ICisTextGenerationService? _generation;

    public SkillAuditService(
        ICisRepositoryContextResolver repositoryContextResolver,
        SkillValidationService validationService,
        ICisTextGenerationService? generation = null)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _validationService = validationService;
        _generation = generation;
    }

    public SkillAuditResult Audit(SkillAuditRequest request)
    {
        var resolution = _repositoryContextResolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess)
        {
            return CreateResult(null, 0, 0, "unavailable", null, null, [], [], resolution.Errors,
                request.Strict, request.Fix);
        }

        if (request.MaximumPairs is < 1 or > 1_000)
        {
            return CreateResult(resolution.Context!.RepositoryPath, 0, 0, "not-run", null, null,
                [], [], ["--max-pairs must be between 1 and 1000."], request.Strict, request.Fix);
        }

        var context = resolution.Context!;
        var validation = _validationService.Validate(context.RepositoryPath, strict: false);
        if (validation.ErrorCount > 0)
        {
            var errors = validation.Diagnostics.Where(item => item.Severity == "error")
                .Select(item => $"{item.Code}: {item.Message} [{item.Path}]").ToArray();
            return CreateResult(context.RepositoryPath, validation.SkillCount, 0, "not-run", null, null,
                [], [], errors, request.Strict, request.Fix);
        }

        var documents = validation.Skills.Select(skill => ReadSkill(context.RepositoryPath, skill)).ToArray();
        var findings = FindExactDuplicates(documents).ToList();
        var candidates = SelectCandidatePairs(documents, request.MaximumPairs).ToArray();
        var warnings = new List<string>();
        var llmStatus = "disabled";
        string? provider = null;
        string? model = null;

        if (!request.DisableLlm)
        {
            if (_generation is null)
            {
                llmStatus = "unavailable";
                warnings.Add("Semantic audit is unavailable because no text-generation service is loaded.");
            }
            else
            {
                var selectedProvider = SelectModelProvider(_generation.GetStatus().Providers, request.Model);
                if (selectedProvider is null)
                {
                    llmStatus = "unavailable";
                    warnings.Add("No local or configured remote LLM is available; deterministic duplicate analysis completed without semantic conflict review.");
                }
                else if (candidates.Length == 0)
                {
                    llmStatus = "not-needed";
                    provider = selectedProvider.Name;
                }
                else
                {
                    provider = selectedProvider.Name;
                    if (!selectedProvider.IsLocal)
                    {
                        warnings.Add($"No local LLM was available; semantic audit used configured remote provider '{selectedProvider.Name}'.");
                    }
                    llmStatus = "completed";
                    foreach (var batch in candidates.Chunk(PairsPerPrompt))
                    {
                        var generated = _generation.Generate(new CisTextGenerationRequest(
                            CreatePrompt(batch),
                            Provider: selectedProvider.Name,
                            Model: request.Model,
                            AllowRemote: !selectedProvider.IsLocal,
                            TimeoutSeconds: selectedProvider.IsLocal ? 45 : 120,
                            MaxOutputTokens: 384));
                        model ??= generated.Model;
                        if (!generated.IsSuccess || string.IsNullOrWhiteSpace(generated.Text))
                        {
                            llmStatus = "partial";
                            warnings.Add($"Semantic audit batch failed: {generated.Detail ?? generated.Status}");
                            continue;
                        }

                        var parsedFindings = ParseModelFindings(
                            generated.Text,
                            batch,
                            warnings,
                            selectedProvider.IsLocal ? "local-llm" : "remote-llm",
                            out var parsed);
                        if (!parsed)
                        {
                            llmStatus = "partial";
                        }
                        findings.AddRange(parsedFindings);
                    }
                }
            }
        }

        var distinctFindings = findings
            .GroupBy(FindingKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Method == "deterministic")
                .ThenByDescending(item => item.Confidence).First())
            .OrderBy(item => KindOrder(item.Kind))
            .ThenBy(item => string.Join('|', item.Skills), StringComparer.Ordinal)
            .ToArray();
        var result = CreateResult(context.RepositoryPath, documents.Length, candidates.Length, llmStatus,
            provider, model, distinctFindings, warnings, [], request.Strict, request.Fix);
        if (request.Fix)
        {
            result = ApplyQuarantine(context, documents, result, request.Strict);
        }

        var persisted = result with
        {
            Applied = true,
            JsonPath = ".cis/local/skills/audit.json",
            MarkdownPath = ".cis/local/skills/audit.md",
        };
        WriteAudit(context, persisted);
        return persisted;
    }

    private static CisAiProviderStatus? SelectModelProvider(
        IReadOnlyList<CisAiProviderStatus> providers,
        string? requestedModel)
    {
        var available = providers.Where(item => item.IsAvailable).ToArray();
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            var matching = available.Where(item => item.Models.Any(model =>
                string.Equals(model.Name, requestedModel.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
            if (matching.Length > 0)
            {
                available = matching;
            }
        }

        return available.OrderByDescending(item => item.IsLocal).ThenBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static SkillAuditResult ApplyQuarantine(
        CisRepositoryContext context,
        IReadOnlyList<AuditSkill> skills,
        SkillAuditResult result,
        bool strict)
    {
        var quarantineNames = result.Findings
            .Where(finding => finding.Kind == "conflict")
            .SelectMany(finding => finding.Skills)
            .Concat(result.Findings
                .Where(finding => finding.Kind == "duplicate" && finding.Method == "deterministic")
                .SelectMany(finding => finding.Skills.Order(StringComparer.Ordinal).Skip(1)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (quarantineNames.Length == 0)
        {
            return result;
        }

        var skillByName = skills.ToDictionary(skill => skill.Name, StringComparer.Ordinal);
        var quarantineRelativePath = ".github/skills-quarantine";
        var quarantineRoot = Path.Combine(context.RepositoryPath, ".github", "skills-quarantine");
        var moves = new List<(string Name, string Source, string Destination)>();
        var errors = new List<string>();
        foreach (var name in quarantineNames)
        {
            if (!skillByName.TryGetValue(name, out var skill))
            {
                errors.Add($"Skill '{name}' is no longer available for quarantine.");
                continue;
            }

            var source = Path.GetDirectoryName(Path.Combine(
                context.RepositoryPath,
                skill.Path.Replace('/', Path.DirectorySeparatorChar)))!;
            var destination = Path.Combine(quarantineRoot, name);
            if (!Directory.Exists(source))
            {
                errors.Add($"Skill source '{skill.Path}' is no longer available for quarantine.");
            }
            else if ((new DirectoryInfo(source).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add($"Skill '{name}' is a reparse point and cannot be quarantined safely.");
            }
            else if (Directory.Exists(destination) || File.Exists(destination))
            {
                errors.Add($"Quarantine destination '.github/skills-quarantine/{name}' already exists; no skills were moved.");
            }
            else
            {
                moves.Add((name, source, destination));
            }
        }

        if (errors.Count > 0)
        {
            return result with
            {
                Status = "invalid",
                ExitCode = 2,
                Errors = result.Errors.Concat(errors).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }

        var completed = new List<(string Name, string Source, string Destination)>();
        try
        {
            Directory.CreateDirectory(quarantineRoot);
            foreach (var move in moves)
            {
                Directory.Move(move.Source, move.Destination);
                completed.Add(move);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var rollbackErrors = new List<string>();
            foreach (var move in completed.AsEnumerable().Reverse())
            {
                try
                {
                    Directory.Move(move.Destination, move.Source);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                {
                    rollbackErrors.Add($"Rollback failed for '{move.Name}': {rollback.Message}");
                }
            }

            return result with
            {
                Status = "invalid",
                ExitCode = 2,
                Errors = result.Errors.Concat([$"Skill quarantine failed: {exception.Message}"])
                    .Concat(rollbackErrors).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }

        var quarantined = completed.Select(move => move.Name).ToHashSet(StringComparer.Ordinal);
        var unresolved = result.Findings.Where(finding => !finding.Skills.Any(quarantined.Contains)).ToArray();
        return result with
        {
            Status = "quarantined",
            ExitCode = strict && unresolved.Length > 0 ? 5 : 0,
            QuarantinePath = quarantineRelativePath,
            QuarantinedSkills = quarantined.Order(StringComparer.Ordinal).ToArray(),
        };
    }

    private static IReadOnlyList<SkillAuditFinding> FindExactDuplicates(IReadOnlyList<AuditSkill> skills)
    {
        var findings = new List<SkillAuditFinding>();
        foreach (var group in skills.GroupBy(skill => skill.SemanticHash, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var names = group.Select(skill => skill.Name).Order(StringComparer.Ordinal).ToArray();
            findings.Add(Finding("duplicate", "warning", names,
                "Skills contain the same normalized instruction body.",
                group.Select(skill => skill.Path).ToArray(), "deterministic", 1));
        }

        foreach (var group in skills.GroupBy(skill => NormalizeText(skill.Description), StringComparer.Ordinal)
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
        {
            var names = group.Select(skill => skill.Name).Order(StringComparer.Ordinal).ToArray();
            findings.Add(Finding("overlap", "warning", names,
                "Skills declare the same normalized trigger description.",
                group.Select(skill => skill.Description).Distinct(StringComparer.Ordinal).ToArray(),
                "deterministic", 0.95));
        }

        return findings;
    }

    private static IEnumerable<AuditPair> SelectCandidatePairs(
        IReadOnlyList<AuditSkill> skills,
        int maximumPairs)
    {
        var pairs = new List<AuditPair>();
        for (var leftIndex = 0; leftIndex < skills.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < skills.Count; rightIndex++)
            {
                var left = skills[leftIndex];
                var right = skills[rightIndex];
                var score = Jaccard(left.Terms, right.Terms);
                var sharedTerms = left.Terms.Intersect(right.Terms, StringComparer.Ordinal).Count();
                var samePrimaryAction = string.Equals(
                    PrimaryAction(left.Description),
                    PrimaryAction(right.Description),
                    StringComparison.Ordinal);
                if (score >= 0.18 || samePrimaryAction && sharedTerms >= 3)
                {
                    pairs.Add(new AuditPair(left, right, score));
                }
            }
        }

        return pairs.OrderByDescending(pair => pair.Similarity)
            .ThenBy(pair => pair.Left.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Right.Name, StringComparer.Ordinal)
            .Take(maximumPairs);
    }

    private static string CreatePrompt(IReadOnlyList<AuditPair> pairs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Review only the supplied repository skill pair. Identify meaningful duplicates, overlapping responsibility, or contradictory instructions.");
        builder.AppendLine("Return JSON only, with at most one finding: {\"findings\":[{\"left\":\"name\",\"right\":\"name\",\"kind\":\"duplicate|overlap|conflict\",\"confidence\":0.0,\"summary\":\"concise\",\"evidence\":[\"left paraphrase\",\"right paraphrase\"]}]}.");
        builder.AppendLine("If there is no material finding, return exactly {\"findings\":[]}. Do not include analysis, alternatives, markdown, or code fences. Keep each evidence item under 160 characters.");
        builder.AppendLine("A duplicate performs substantially the same responsibility for the same trigger. An overlap owns a material part of the same responsibility.");
        builder.AppendLine("A conflict exists only when both skills can apply to the same situation and require mutually exclusive actions or policies. Different scopes, test types, or complementary steps are not conflicts.");
        builder.AppendLine("Do not report an overlap when one skill orchestrates, specializes, verifies, or documents the other; composition is not duplicate ownership.");
        builder.AppendLine("For a conflict, provide two separate evidence items: one stating what the first skill requires and one stating what the second prohibits (or vice versa).");
        builder.AppendLine("For every finding, provide at least two evidence items: one grounded in LEFT and one grounded in RIGHT. Shared generic words are not an overlap. Omit the pair when there is no material issue. Do not invent instructions or quote secrets.");
        foreach (var pair in pairs)
        {
            AppendSkill(builder, "LEFT", pair.Left);
            AppendSkill(builder, "RIGHT", pair.Right);
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    private static void AppendSkill(StringBuilder builder, string side, AuditSkill skill)
    {
        builder.AppendLine($"{side} NAME: {skill.Name}");
        builder.AppendLine($"{side} DESCRIPTION: {skill.Description}");
        builder.AppendLine($"{side} BODY: {Truncate(skill.Body, MaximumBodyCharactersPerSkill)}");
    }

    private static IReadOnlyList<SkillAuditFinding> ParseModelFindings(
        string text,
        IReadOnlyList<AuditPair> allowedPairs,
        ICollection<string> warnings,
        string method,
        out bool parsed)
    {
        parsed = false;
        try
        {
            var json = ExtractJson(text);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            if (!document.RootElement.TryGetProperty("findings", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Semantic audit returned no parseable findings array.");
                return [];
            }

            parsed = true;

            var allowed = allowedPairs.ToDictionary(
                pair => PairKey(pair.Left.Name, pair.Right.Name),
                pair => pair,
                StringComparer.Ordinal);
            var results = new List<SkillAuditFinding>();
            foreach (var item in items.EnumerateArray())
            {
                var left = ReadString(item, "left");
                var right = ReadString(item, "right");
                var kind = ReadString(item, "kind").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)
                    || !allowed.TryGetValue(PairKey(left, right), out var pair)
                    || kind is not ("duplicate" or "overlap" or "conflict"))
                {
                    continue;
                }

                var confidence = item.TryGetProperty("confidence", out var confidenceElement)
                    && confidenceElement.TryGetDouble(out var parsedConfidence)
                        ? Math.Clamp(parsedConfidence, 0, 1)
                        : 0.5;
                var summary = ReadString(item, "summary");
                if (string.IsNullOrWhiteSpace(summary))
                {
                    continue;
                }

                var evidence = item.TryGetProperty("evidence", out var evidenceElement)
                    && evidenceElement.ValueKind == JsonValueKind.Array
                    ? evidenceElement.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString() ?? string.Empty).Where(value => value.Length > 0).Take(5).ToArray()
                    : [];
                if (!IsMaterialModelFinding(kind, summary, evidence, pair, confidence))
                {
                    if (kind == "conflict")
                    {
                        warnings.Add($"Semantic audit omitted an unsubstantiated conflict candidate for '{left}' and '{right}'.");
                    }
                    continue;
                }

                // Model classifications are candidates for human review, never
                // authoritative enough to disable or fail a skill by themselves.
                results.Add(Finding(kind, "warning",
                    [left, right], summary, evidence, method, confidence));
            }

            return results;
        }
        catch (JsonException exception)
        {
            warnings.Add($"Semantic audit returned invalid JSON: {exception.Message}");
            return [];
        }
    }

    private static SkillAuditResult CreateResult(
        string? repositoryPath,
        int skillCount,
        int candidatePairCount,
        string llmStatus,
        string? provider,
        string? model,
        IReadOnlyList<SkillAuditFinding> findings,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        bool strict,
        bool fixRequested)
    {
        var duplicates = findings.Count(item => item.Kind == "duplicate");
        var overlaps = findings.Count(item => item.Kind == "overlap");
        var conflicts = findings.Count(item => item.Kind == "conflict");
        var failed = errors.Count > 0
            || findings.Any(item => item.Severity == "error")
            || strict && findings.Count > 0;
        return new SkillAuditResult(
            errors.Count > 0 ? "invalid" : findings.Count > 0 ? "findings" : "clean",
            errors.Count > 0 ? 2 : failed ? 5 : 0,
            repositoryPath,
            skillCount,
            candidatePairCount,
            duplicates,
            overlaps,
            conflicts,
            llmStatus,
            provider,
            model,
            fixRequested,
            false,
            null,
            null,
            null,
            [],
            findings,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void WriteAudit(CisRepositoryContext context, SkillAuditResult result)
    {
        var root = Path.Combine(context.RepositoryPath, ".cis", "local", "skills");
        Directory.CreateDirectory(root);
        Write(Path.Combine(root, "audit.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }) + "\n");

        var markdown = new StringBuilder();
        markdown.AppendLine("# Skill audit").AppendLine();
        markdown.AppendLine($"- Skills: {result.SkillCount}");
        markdown.AppendLine($"- Candidate pairs: {result.CandidatePairCount}");
        markdown.AppendLine($"- LLM: {result.LlmStatus}{(result.Provider is null ? string.Empty : $" ({result.Provider}/{result.Model})")}");
        markdown.AppendLine($"- Duplicates: {result.DuplicateCount}; overlaps: {result.OverlapCount}; conflicts: {result.ConflictCount}").AppendLine();
        if (result.QuarantinedSkills.Count > 0)
        {
            markdown.AppendLine("## Quarantined skills").AppendLine();
            foreach (var skill in result.QuarantinedSkills)
            {
                markdown.AppendLine($"- `{skill}`");
            }
            markdown.AppendLine();
        }
        foreach (var group in result.Findings.GroupBy(item => item.Kind).OrderBy(group => KindOrder(group.Key)))
        {
            markdown.AppendLine($"## {char.ToUpperInvariant(group.Key[0]) + group.Key[1..]}").AppendLine();
            foreach (var finding in group)
            {
                markdown.AppendLine($"### {string.Join(" / ", finding.Skills)}").AppendLine();
                markdown.AppendLine($"- Severity: {finding.Severity}");
                markdown.AppendLine($"- Method: {finding.Method}; confidence: {finding.Confidence:0.00}");
                markdown.AppendLine($"- Summary: {finding.Summary}");
                foreach (var evidence in finding.Evidence)
                {
                    markdown.AppendLine($"- Evidence: {evidence.Replace('\r', ' ').Replace('\n', ' ')}");
                }
                markdown.AppendLine();
            }
        }

        foreach (var warning in result.Warnings)
        {
            markdown.AppendLine($"- Warning: {warning}");
        }
        Write(Path.Combine(root, "audit.md"), markdown.ToString());
    }

    private static AuditSkill ReadSkill(string repositoryPath, SkillInventoryItem skill)
    {
        var content = File.ReadAllText(Path.Combine(repositoryPath, skill.Path.Replace('/', Path.DirectorySeparatorChar)));
        var body = StripFrontMatter(content);
        var semantic = NormalizeSemanticBody(body);
        return new AuditSkill(skill.Name, skill.Path, skill.Description, body,
            Sha256(semantic), Terms(skill.Name + " " + skill.Description));
    }

    private static string StripFrontMatter(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return normalized;
        }

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0 ? normalized : normalized[(end + 5)..];
    }

    private static string NormalizeSemanticBody(string body)
        => NormalizeText(HeadingRegex().Replace(body, string.Empty));

    private static string NormalizeText(string value)
        => WhitespaceRegex().Replace(value.ToLowerInvariant(), " ").Trim();

    private static HashSet<string> Terms(string value)
        => WordRegex().Matches(value.ToLowerInvariant().Replace('-', ' ')).Select(match => match.Value)
            .Where(word => word.Length > 2 && !StopWords.Contains(word)).ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Intersect(right, StringComparer.Ordinal).Count();
        return intersection / (double)(left.Count + right.Count - intersection);
    }

    private static string PrimaryAction(string description)
        => WordRegex().Matches(description.ToLowerInvariant().Replace('-', ' '))
            .Select(match => match.Value)
            .FirstOrDefault(word => word.Length > 2)
            ?? string.Empty;

    private static SkillAuditFinding Finding(
        string kind,
        string severity,
        IReadOnlyList<string> skills,
        string summary,
        IReadOnlyList<string> evidence,
        string method,
        double confidence)
    {
        var orderedSkills = skills.Order(StringComparer.Ordinal).ToArray();
        var idSource = kind + "\u001f" + string.Join('\u001f', orderedSkills);
        var id = "SKILL-AUDIT-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idSource)))[..12];
        return new SkillAuditFinding(id, kind, severity, orderedSkills, summary, evidence, method, confidence);
    }

    private static string FindingKey(SkillAuditFinding finding)
        => finding.Kind + "\u001f" + string.Join('\u001f', finding.Skills.Order(StringComparer.Ordinal));

    private static string PairKey(string left, string right)
        => string.Join("\u001f", new[] { left, right }.Order(StringComparer.Ordinal));

    private static string ReadString(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static bool HasContradictoryDirectiveEvidence(IReadOnlyList<string> evidence)
    {
        if (evidence.Count < 2)
        {
            return false;
        }

        var normalized = evidence.Select(NormalizeText).ToArray();
        var hasRequirement = normalized.Any(value => ContainsAny(value,
            "require", "requires", "required", "must ", "always ", "mandate", "mandates", "directs"));
        var hasProhibition = normalized.Any(value => ContainsAny(value,
            "must not", "never ", "prohibit", "prohibits", "forbid", "forbids", "cannot ", "disallow", "disallows"));
        return hasRequirement && hasProhibition;
    }

    private static bool IsMaterialModelFinding(
        string kind,
        string summary,
        IReadOnlyList<string> evidence,
        AuditPair pair,
        double confidence)
    {
        if (confidence < 0.75 || evidence.Count < 2 || !EvidenceNamesBothSkills(evidence, pair))
        {
            return false;
        }

        if (kind == "conflict")
        {
            return HasContradictoryDirectiveEvidence(evidence);
        }

        var explanation = NormalizeText(summary + " " + string.Join(' ', evidence));
        if (ContainsAny(explanation,
            "different scope", "different scopes", "differ in", "different focus",
            "different approach", "complementary", "related to", "while the other",
            "while the second", "one focuses on"))
        {
            return false;
        }

        var leftTerms = Terms(pair.Left.Description);
        var rightTerms = Terms(pair.Right.Description);
        var sharedTerms = leftTerms.Intersect(rightTerms, StringComparer.Ordinal).Count();
        return kind == "duplicate"
            ? pair.Similarity >= 0.45 && sharedTerms >= 4
            : pair.Similarity >= 0.08 && sharedTerms >= 3;
    }

    private static bool EvidenceNamesBothSkills(IReadOnlyList<string> evidence, AuditPair pair)
    {
        var leftTerms = Terms(pair.Left.Name + " " + pair.Left.Description + " " + pair.Left.Body);
        var rightTerms = Terms(pair.Right.Name + " " + pair.Right.Description + " " + pair.Right.Body);
        var first = Terms(evidence[0]);
        var second = Terms(evidence[1]);
        static bool Grounded(IReadOnlySet<string> skill, IReadOnlySet<string> statement)
            => skill.Intersect(statement, StringComparer.Ordinal).Take(2).Count() == 2;
        return Grounded(leftTerms, first) && Grounded(rightTerms, second)
            || Grounded(leftTerms, second) && Grounded(rightTerms, first);
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.Ordinal));

    private static string Truncate(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum] + "…";

    private static string Sha256(string value)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static int KindOrder(string kind) => kind switch { "conflict" => 0, "duplicate" => 1, _ => 2 };

    private static void Write(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record AuditSkill(
        string Name,
        string Path,
        string Description,
        string Body,
        string SemanticHash,
        IReadOnlySet<string> Terms);

    private sealed record AuditPair(AuditSkill Left, AuditSkill Right, double Similarity);

    [GeneratedRegex(@"^#\s+.+$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[a-z0-9][a-z0-9-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
