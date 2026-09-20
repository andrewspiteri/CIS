using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.UiDirection;

public sealed class UiDirectionQuestionnaireService
{
    private static readonly IReadOnlyList<QuestionDefinition> Definitions =
    [
        new("UI-Q-001", "Surfaces and audiences", "Which user-facing surfaces and audience classifications need one coherent visual direction?",
            "Public, customer, backoffice, mobile, desktop, and embedded surfaces have different trust, navigation, and density needs.",
            ["Public web", "Customer web", "Backoffice web", "iOS", "Android", "Desktop", "Embedded", "No user-facing UI"],
            "Include only surfaces established by the Active BRD and technical intent. Keep public, customer, and backoffice experiences explicitly classified."),
        new("UI-Q-002", "Product character", "What should the product feel like to its users?",
            "A small set of experience qualities gives later screen designs a stable basis for visual judgment.",
            ["Clear and calm", "Practical and efficient", "Premium and restrained", "Friendly and approachable", "Technical and precise", "Playful", "Institutional"],
            "Use a clear, credible, modern, confident, and practical direction with restrained visual noise unless product evidence requires another character."),
        new("UI-Q-003", "Shell and navigation", "What application shell and primary navigation model should each surface use?",
            "Shell and navigation are shared product structures; deciding them per feature creates inconsistency and unnecessary design work.",
            ["Header only", "Header and side navigation", "Bottom navigation", "Tabs", "Master/detail", "Document flow", "Platform-native shell"],
            "Choose the smallest persistent shell that supports the product's information architecture. Do not add side or bottom navigation to a simple product without a demonstrated need."),
        new("UI-Q-004", "Layout and density", "What layout rhythm, content width, and information density should guide screens?",
            "Density and hierarchy affect scanability, task speed, responsive behavior, and component selection.",
            ["Spacious", "Balanced", "Compact", "Data-dense", "Single-column", "Responsive grid", "Master/detail"],
            "Use an 8 px spacing rhythm, generous outer margins, one dominant action per region, and the lowest density that still supports efficient completion."),
        new("UI-Q-005", "Color and theme", "What color, theme, branding, and contrast direction should apply?",
            "Color is both identity and interaction state; it must remain accessible and reusable across all surfaces.",
            ["Light", "Dark", "System theme", "Brand-led", "Neutral with restrained accent", "High-contrast option"],
            "Start from the Active design guidelines: neutral surfaces, deep anchors, restrained accents, and WCAG-compliant contrast. Record any brand-specific replacement tokens."),
        new("UI-Q-006", "Typography and content", "What typography, iconography, imagery, and writing tone should the interface use?",
            "These choices establish hierarchy and voice while preventing each feature from inventing its own presentation language.",
            ["Sans serif", "System typography", "Product typeface", "Text-first", "Icon-supported", "Illustration-led", "Photography-led"],
            "Use one highly readable sans-serif family, sentence-case outcome headings, direct evidence-oriented copy, and icons only where they improve recognition."),
        new("UI-Q-007", "Reusable component system", "Which UI framework, design tokens, and reusable component families are authoritative?",
            "Buttons, fields, menus, tables, feedback, and navigation should be reused rather than redrawn in every feature.",
            ["Existing component system", "Classification-selected CIS default", "Platform-native components", "Custom governed components"],
            "Preserve an evidenced existing UI system. Otherwise use the platform-specific framework recorded in the UI framework profile and govern shared shell, controls, data display, feedback, overlays, and navigation components."),
        new("UI-Q-008", "Responsive and platform adaptation", "How should layout and interaction adapt across supported viewports and platforms?",
            "Responsive behavior is part of the interaction contract, not an afterthought added during implementation.",
            ["Desktop first", "Mobile first", "Desktop and compact web", "Tablet", "Native adaptive layouts", "Keyboard-heavy desktop"],
            "Define at least desktop and compact behavior for web surfaces; preserve task order and meaning while adapting navigation, density, and touch targets."),
        new("UI-Q-009", "Accessibility and input", "Which accessibility baseline and input modes must every feature support?",
            "Keyboard, screen reader, focus, contrast, touch, motion, and error behavior must be designed before screenshots are approved.",
            ["WCAG 2.2 AA", "Keyboard", "Screen reader", "Touch", "Pointer", "Voice control", "Reduced motion", "High contrast"],
            "Use WCAG 2.2 AA as the baseline with semantic structure, complete keyboard operation, visible focus, non-color state cues, accessible names, and reduced-motion support."),
        new("UI-Q-010", "State, feedback, and motion", "How should loading, empty, success, warning, error, unavailable, destructive, and transition states behave?",
            "A polished happy path is insufficient when users cannot understand pending, failed, denied, or recoverable states.",
            ["Inline feedback", "Status banners", "Toast notifications", "Skeletons", "Progress indicators", "Confirmation dialogs", "Optimistic updates"],
            "Every operation needs truthful pending, success, failure, and recovery feedback. Prefer inline feedback; reserve motion for continuity and honor reduced-motion preferences."),
        new("UI-Q-011", "Data and action patterns", "Which recurring presentation and action patterns should be standardized?",
            "Lists, tables, forms, filters, editors, and destructive actions are common sources of avoidable inconsistency.",
            ["Flat lists", "Tables/grids", "Cards", "Forms", "Inline editing", "Search/filter", "Bulk actions", "Destructive confirmation"],
            "Standardize only patterns supported by the product's journeys. Keep primary actions obvious, secondary actions compact, and destructive actions explicitly confirmed."),
        new("UI-Q-012", "Constraints and exclusions", "Which brand assets, mandated conventions, legacy constraints, or explicit visual exclusions must be preserved?",
            "Explicit constraints prevent feature design from silently rebranding, replacing frameworks, or expanding platform scope.",
            ["Existing brand", "Existing shell", "Existing design system", "White-label", "Localization", "Offline", "No imagery", "No animation"],
            "Record only evidenced constraints. Treat framework replacement, rebranding, new surfaces, and major shell changes as explicit decisions rather than feature-level interpretation."),
    ];

    private readonly ICisWorkspaceRegistry _workspaceRegistry;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly IUiDirectionSolutionDesignSource _solutionDesign;
    private readonly Func<DateTimeOffset> _clock;
    private readonly UiBaselineDiscovery _baseline;

    public UiDirectionQuestionnaireService(ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver, DocumentationCatalogMerger catalogMerger,
        IUiDirectionSolutionDesignSource solutionDesign, Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _catalogMerger = catalogMerger;
        _solutionDesign = solutionDesign;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _baseline = new UiBaselineDiscovery(workspaceRegistry);
    }

    public UiDirectionQuestionnaireResult Initialize(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Result("invalid", state, [], false, state.Errors);
        if (!state.SolutionReady) return Result("blocked", state, [], false,
            ["An Active, current overall solution design and component sheet are required before UI direction."]);

        var existing = File.Exists(state.Path!) ? File.ReadAllText(state.Path) : null;
        var previous = existing is null ? new Dictionary<string, UiDirectionQuestion>(StringComparer.Ordinal) : Parse(existing);
        var sourceChanged = existing is not null && ReadNested(existing, "solution_design_hash") != state.SolutionVersion;
        var derived = Derive(state);
        var baseline = _baseline.Discover(state.Workspace!.WorkspacePath);
        // Code observations are proposals for the common direction, not design authority.
        if (baseline.Suggestions.ContainsKey("UI-Q-007")) derived.Remove("UI-Q-007");
        var questions = Definitions.Select(original =>
        {
            var definition = baseline.Suggestions.TryGetValue(original.Id, out var suggestion)
                ? original with { Suggestion = suggestion } : original;
            if (previous.TryGetValue(definition.Id, out var item) && !string.IsNullOrWhiteSpace(item.Answer))
            {
                if (item.ResolutionSource == "repository-evidence")
                    return derived.TryGetValue(definition.Id, out var refreshed) ? ToDerived(definition, refreshed) : ToQuestion(definition);
                if (!sourceChanged) return item;
                return ToQuestion(definition with { Suggestion = $"Previously recorded direction — recheck it against the renewed solution architecture: {item.Answer}" });
            }
            return derived.TryGetValue(definition.Id, out var value) ? ToDerived(definition, value) : ToQuestion(definition);
        }).ToArray();
        var complete = questions.All(IsResolved);
        var content = Render(state.Authority!.Id, state.SolutionVersion!, complete, questions);
        var stableId = $"{state.Authority.Id}:spec:ui-direction-questionnaire";
        var relative = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path!));
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(state.Authority.Id, catalog,
            [new CatalogArtifactEntry(stableId, relative, "repository-specification", complete ? "active" : "draft", "canonical")]);
        if (merge.Collisions.Count > 0) return Result("collision", state, questions, false, merge.Collisions);
        var changed = existing is null || !Equivalent(existing, content);
        var catalogChanged = !Equivalent(catalog, merge.Content);
        if (changed) { Directory.CreateDirectory(Path.GetDirectoryName(state.Path!)!); Write(state.Path!, content); }
        if (catalogChanged) Write(state.Context.CatalogPath, merge.Content);
        return StatusInternal(state.Workspace!.WorkspacePath, changed || catalogChanged ? "initialized" : "unchanged", changed || catalogChanged);
    }

    public UiDirectionQuestionnaireResult Status(string workspacePath) => StatusInternal(workspacePath, "status", false);

    public CisUiBaselineResult Baseline(string workspacePath) => _baseline.Discover(workspacePath);

    public UiDirectionQuestionnaireResult Answer(string workspacePath, string id, string answer, string actor)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(actor))
            return new UiDirectionQuestionnaireResult("invalid", workspacePath, null, null, null, false, false, 0, 0, [], [], ["Question ID, substantive answer, and human actor are required."], false);
        if (answer.Trim().Length > 16_384)
            return new UiDirectionQuestionnaireResult("invalid", workspacePath, null, null, null, false, false, 0, 0, [], [], ["The answer exceeds the 16 KiB limit."], false);
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Result("invalid", state, [], false, state.Errors);
        if (!state.SolutionReady) return Result("blocked", state, [], false, ["The approved solution-design bundle is not current."]);
        if (!File.Exists(state.Path!))
        {
            var initialized = Initialize(workspacePath); if (initialized.ExitCode != 0) return initialized;
            state = Resolve(workspacePath);
        }
        var questions = Parse(File.ReadAllText(state.Path!));
        if (!questions.ContainsKey(id.Trim())) return Result("invalid", state, questions.Values.ToArray(), false, [$"Unknown UI-direction question '{id.Trim()}'."]);
        var updated = questions.Values.Select(item => item.Id == id.Trim() ? item with
        {
            Status = "Answered", Answer = answer.Trim(), AnsweredBy = actor.Trim(), AnsweredAtUtc = _clock().ToString("O"),
            ResolutionSource = "human", Confidence = "confirmed", Evidence = [],
        } : item).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        Write(state.Path!, Render(state.Authority!.Id, state.SolutionVersion!, updated.All(IsResolved), updated));
        var stableId = $"{state.Authority.Id}:spec:ui-direction-questionnaire";
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        Write(state.Context.CatalogPath, UpdateCatalogStatus(catalog, stableId, updated.All(IsResolved) ? "active" : "draft"));
        return StatusInternal(state.Workspace!.WorkspacePath, "answered", true);
    }

    internal QuestionnaireSnapshot? Snapshot(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0 || !File.Exists(state.Path!)) return null;
        var content = File.ReadAllText(state.Path);
        var questions = Parse(content).Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return new QuestionnaireSnapshot(state.Path, Digest(content), ReadNested(content, "solution_design_hash") ?? string.Empty,
            state.SolutionReady && ReadNested(content, "solution_design_hash") == state.SolutionVersion,
            questions.Length == Definitions.Count && questions.All(IsResolved), questions);
    }

    private UiDirectionQuestionnaireResult StatusInternal(string workspacePath, string status, bool applied)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Result("invalid", state, [], false, state.Errors);
        if (!File.Exists(state.Path!)) return Result("missing", state, [], false, []);
        var content = File.ReadAllText(state.Path);
        var questions = Parse(content).Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var errors = new List<string>(); var warnings = new List<string>();
        if (questions.Length != Definitions.Count) errors.Add($"The questionnaire contains {questions.Length} of {Definitions.Count} required questions. Run `cis ui-direction questions init`.");
        var current = state.SolutionReady && ReadNested(content, "solution_design_hash") == state.SolutionVersion;
        var complete = questions.Length == Definitions.Count && questions.All(IsResolved);
        if (!current) warnings.Add("The UI-direction questionnaire does not match the current Active solution-design bundle.");
        if (!complete) warnings.Add($"{questions.Count(item => !IsResolved(item))} high-level UI decision(s) remain unanswered.");
        return new UiDirectionQuestionnaireResult(status, state.Workspace!.WorkspacePath, state.Authority!.Id,
            Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path)), state.SolutionVersion, current, complete,
            questions.Count(IsResolved), questions.Count(item => !IsResolved(item)), questions, warnings, errors, applied);
    }

    private QuestionnaireState Resolve(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null) return new(null, null, null, null, null, false, resolution.Errors);
        var authority = resolution.Workspace.AuthorityRepository;
        if (authority is null) return new(resolution.Workspace, null, null, null, null, false, ["Workspace has no authority repository."]);
        var context = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!context.IsSuccess || context.Context is null) return new(resolution.Workspace, authority, null, null, null, false, context.Errors);
        var docs = authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(authority.RepositoryPath, docs, "specs", "ui-direction-questionnaire.md");
        var status = _solutionDesign.Status(resolution.Workspace.WorkspacePath);
        var ready = status.Validation is { } validation
            && CisDefinitionDraftScope.Accepts(validation.Valid, validation.Current, validation.EffectiveStatus);
        var version = ready ? SourceDigest(authority.RepositoryPath, docs) : null;
        return new(resolution.Workspace, authority, context.Context, path, version, ready && version is not null, []);
    }

    private static Dictionary<string, DerivedDirection> Derive(QuestionnaireState state)
    {
        var result = new Dictionary<string, DerivedDirection>(StringComparer.Ordinal);
        var root = state.Authority!.RepositoryPath;
        var docs = state.Authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar);
        var technicalPath = Path.Combine(root, docs, "specs", "technical-intent-spec.md");
        if (File.Exists(technicalPath))
        {
            var technical = File.ReadAllText(technicalPath);
            var surface = ExtractSection(technical, "Product surfaces and entry points");
            var meaningful = surface.Replace("None recorded.", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (meaningful.Length > 0)
                result["UI-Q-001"] = new("Preserve the approved product surfaces and public/customer/backoffice classifications recorded in technical intent. Feature design may refine journeys but may not introduce another surface silently.", "high", [Normalize(Path.GetRelativePath(root, technicalPath))]);
        }
        var profiles = state.Workspace!.Repositories.Select(repository =>
        {
            var profile = Path.Combine(repository.RepositoryPath, repository.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "references", "ui-framework-profile.md");
            return (repository.Id, Path: profile);
        }).Where(item => File.Exists(item.Path)).ToArray();
        if (profiles.Length > 0)
        {
            var descriptions = profiles.Select(item => $"{item.Id}: {SummarizeFrameworkProfile(File.ReadAllText(item.Path))}").ToArray();
            result["UI-Q-007"] = new($"Preserve the governed UI-framework resolution for each surface: {string.Join("; ", descriptions)}. Use its component primitives and tokens as the implementation baseline.", "high",
                profiles.Select(item => $"{item.Id}:{Normalize(Path.GetRelativePath(state.Workspace.Repositories.First(repository => repository.Id == item.Id).RepositoryPath, item.Path))}").ToArray());
        }
        return result;
    }

    private static string SummarizeFrameworkProfile(string content)
    {
        var values = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => line.TrimStart().StartsWith('|') && !line.Contains("---") && !line.Contains("Component", StringComparison.OrdinalIgnoreCase))
            .Select(line => Regex.Replace(line.Trim().Trim('|'), @"\s*\|\s*", " / ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .Where(line => line.Length > 0).Take(4).ToArray();
        return values.Length > 0 ? string.Join("; ", values) : "use the canonical profile";
    }

    private static UiDirectionQuestion ToQuestion(QuestionDefinition definition) => new(definition.Id, definition.Area,
        definition.Question, definition.Why, definition.Options, definition.Suggestion, "Unanswered", null, null, null, "unresolved", null, []);
    private static UiDirectionQuestion ToDerived(QuestionDefinition definition, DerivedDirection value) => new(definition.Id, definition.Area,
        definition.Question, definition.Why, definition.Options, definition.Suggestion, "Derived", value.Answer,
        "CIS deterministic repository analysis", null, "repository-evidence", value.Confidence, value.Evidence);
    private static bool IsResolved(UiDirectionQuestion value) => value.Status is "Answered" or "Derived" && !string.IsNullOrWhiteSpace(value.Answer);

    private static string Render(string authorityId, string sourceVersion, bool complete, IReadOnlyList<UiDirectionQuestion> questions)
    {
        var builder = new StringBuilder($$"""
---
title: "{{authorityId}} UI Direction Questionnaire"
type: specification
status: {{(complete ? "Complete" : "Draft")}}
scope: Workspace
owner: Product owner and design authority
last_reviewed: null
review_cadence: on approved solution architecture, surface, brand, or design-system change
cis:
  stable_id: {{authorityId}}:spec:ui-direction-questionnaire
  ui_direction_questionnaire_schema: 1
  solution_design_hash: {{sourceVersion}}
---

# {{authorityId}} UI Direction Questionnaire

This governed pre-design record captures the workspace-level look, feel, shell, interaction, accessibility, and reusable-component direction. Existing implementation facts are derived when evidence is strong; subjective or product-specific choices remain human decisions.

## Decision progress

- Resolved: {{questions.Count(IsResolved)}} of {{questions.Count}}
- State: {{(complete ? "Complete — ready to generate high-level UI direction" : "Draft — answer every question before generation")}}

## Questions

""");
        foreach (var question in questions)
        {
            builder.AppendLine($"### {question.Id} — {question.Area}").AppendLine().AppendLine(question.Question).AppendLine()
                .AppendLine($"- Why this is needed: {question.Why}").AppendLine($"- Common options: {string.Join("; ", question.CommonOptions)}")
                .AppendLine($"- Advisory starting direction: {question.SuggestedAnswer}").AppendLine($"- Status: {question.Status}")
                .AppendLine($"- Answered by: {question.AnsweredBy ?? "Not recorded"}").AppendLine($"- Answered at: {question.AnsweredAtUtc ?? "Not recorded"}")
                .AppendLine($"- Resolution source: {question.ResolutionSource}").AppendLine($"- Confidence: {question.Confidence ?? "Not assessed"}")
                .AppendLine($"- Evidence: {(question.Evidence.Count == 0 ? "Not recorded" : string.Join("; ", question.Evidence))}")
                .AppendLine().AppendLine("#### Recorded answer").AppendLine().AppendLine(question.Answer ?? "Not answered.").AppendLine();
        }
        return builder.ToString().TrimEnd() + "\n";
    }

    private static Dictionary<string, UiDirectionQuestion> Parse(string content)
    {
        var result = new Dictionary<string, UiDirectionQuestion>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(content, @"(?ms)^### (?<id>UI-Q-\d{3}) — (?<area>.+?)\r?$\n(?<body>.*?)(?=^### UI-Q-|\z)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
        {
            var definition = Definitions.FirstOrDefault(item => item.Id == match.Groups["id"].Value); if (definition is null) continue;
            var body = match.Groups["body"].Value; var status = ReadBullet(body, "Status") ?? "Unanswered";
            var answer = Regex.Match(body, @"(?ms)^#### Recorded answer\s*$\r?\n(?<answer>.*)\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["answer"].Value.Trim();
            if (answer == "Not answered.") answer = string.Empty;
            var resolved = status is "Answered" or "Derived" && answer.Length > 0 ? status : "Unanswered";
            result[definition.Id] = new(definition.Id, definition.Area, definition.Question, definition.Why,
                (ReadBullet(body, "Common options") ?? string.Join("; ", definition.Options)).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ReadBullet(body, "Advisory starting direction") ?? definition.Suggestion, resolved, answer.Length == 0 ? null : answer,
                Null(ReadBullet(body, "Answered by")), Null(ReadBullet(body, "Answered at")), ReadBullet(body, "Resolution source") ?? "unresolved",
                Null(ReadBullet(body, "Confidence")), (ReadBullet(body, "Evidence") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(value => value != "Not recorded").ToArray());
        }
        return result;
    }

    internal static string SourceDigest(string root, string docs)
    {
        var design = Path.Combine(root, docs, "architecture", "overall-solution-design.md");
        var components = Path.Combine(root, docs, "references", "component-sheet.md");
        if (!File.Exists(design) || !File.Exists(components)) return string.Empty;
        return "semantic-v1:" + Digest(NormalizeApproval(File.ReadAllText(design)) + "\n--- component-sheet ---\n" + NormalizeApproval(File.ReadAllText(components)))["sha256:".Length..];
    }
    private static string NormalizeApproval(string content)
    {
        var value = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" }) value = Regex.Replace(value, $@"(?m)^{key}:.*$", $"{key}: <approval>");
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_bundle_hash" }) value = Regex.Replace(value, $@"(?m)^  {key}:.*$", $"  {key}: <approval>");
        return value.TrimEnd();
    }
    private static string ExtractSection(string content, string heading)
    {
        var match = Regex.Match(content, $@"(?ms)^## {Regex.Escape(heading)}\s*$\n(?<body>.*?)(?=^## |\z)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
    }
    private static string? ReadBullet(string body, string label) => Regex.Match(body, $@"(?m)^- {Regex.Escape(label)}:\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match ? match.Groups["value"].Value.Trim() : null;
    private static string? ReadNested(string content, string key) => Regex.Match(content, $@"(?m)^  {Regex.Escape(key)}:\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match ? match.Groups["value"].Value.Trim().Trim('"') : null;
    private static string? Null(string? value) => value is null or "Not recorded" or "Not assessed" ? null : value;
    private static string Digest(string content) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static void Write(string path, string content) => File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    private static string UpdateCatalogStatus(string catalog, string stableId, string status)
    {
        var match = Regex.Match(catalog, $@"(?ms)(?<entry>^  - id:\s*{Regex.Escape(stableId)}\s*$.*?)(?=^  - id:|\z)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return catalog;
        var entry = Regex.Replace(match.Groups["entry"].Value, @"(?m)^    status:\s*.*$", $"    status: {status}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return catalog[..match.Index] + entry + catalog[(match.Index + match.Length)..];
    }
    private static UiDirectionQuestionnaireResult Result(string status, QuestionnaireState state, IReadOnlyList<UiDirectionQuestion> questions, bool applied, IReadOnlyList<string> errors)
        => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id, state.Path is null || state.Authority is null ? null : Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path)), state.SolutionVersion,
            false, false, questions.Count(IsResolved), questions.Count(item => !IsResolved(item)), questions, [], errors, applied);

    private sealed record QuestionDefinition(string Id, string Area, string Question, string Why, IReadOnlyList<string> Options, string Suggestion);
    private sealed record DerivedDirection(string Answer, string Confidence, IReadOnlyList<string> Evidence);
    internal sealed record QuestionnaireSnapshot(string Path, string Digest, string SourceVersion, bool Current, bool Complete, IReadOnlyList<UiDirectionQuestion> Questions);
    private sealed record QuestionnaireState(CisWorkspace? Workspace, CisWorkspaceRepository? Authority, CisRepositoryContext? Context,
        string? Path, string? SolutionVersion, bool SolutionReady, IReadOnlyList<string> Errors);
}
