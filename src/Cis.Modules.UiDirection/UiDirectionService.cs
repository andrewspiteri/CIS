using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.UiDirection;

public sealed class UiDirectionService : IChangeReadinessCheck
{
    private const string ManagedStart = "<!-- cis:ui-direction-managed:start -->";
    private const string ManagedEnd = "<!-- cis:ui-direction-managed:end -->";
    private readonly ICisWorkspaceRegistry _workspaceRegistry;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly IUiDirectionSolutionDesignSource _solutionDesign;
    private readonly UiDirectionQuestionnaireService _questionnaire;
    private readonly Func<DateTimeOffset> _clock;

    public UiDirectionService(ICisWorkspaceRegistry workspaceRegistry, ICisRepositoryContextResolver repositoryResolver,
        DocumentationCatalogMerger catalogMerger, IUiDirectionSolutionDesignSource solutionDesign,
        UiDirectionQuestionnaireService questionnaire, Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _catalogMerger = catalogMerger;
        _solutionDesign = solutionDesign;
        _questionnaire = questionnaire;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public UiDirectionResult Initialize(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        if (state.ReadinessErrors.Count > 0) return Error("blocked", state, state.ReadinessErrors);
        var stableId = $"{state.Authority!.Id}:design:ui-direction";
        var relative = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path!));
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(state.Authority.Id, catalog,
            [new CatalogArtifactEntry(stableId, relative, "ui-direction", "review-required", "canonical")]);
        if (merge.Collisions.Count > 0) return Error("collision", state, merge.Collisions);
        var generated = Render(state, stableId);
        var next = Reconcile(state.Path!, generated, stableId);
        if (next is null) return Error("collision", state, ["The canonical UI-direction path contains a different stable identity."]);
        if (ReadFrontMatter(next, "status") == "Active" && ReadNested(next, "approved_content_hash") != ApprovalDigest(next))
            next = ResetApproval(next);
        var changed = !File.Exists(state.Path!) || !Equivalent(File.ReadAllText(state.Path), next);
        var lifecycle = ReadFrontMatter(next, "status") == "Active" ? "active" : "review-required";
        var nextCatalog = UpdateCatalogStatus(merge.Content, stableId, lifecycle);
        var catalogChanged = !Equivalent(catalog, nextCatalog);
        if (!changed && !catalogChanged) return ValidateInternal(workspacePath, "unchanged", false);
        Directory.CreateDirectory(Path.GetDirectoryName(state.Path!)!);
        if (changed) Write(state.Path!, next);
        if (catalogChanged) Write(state.Context!.CatalogPath, nextCatalog);
        return ValidateInternal(workspacePath, "initialized", true);
    }

    public UiDirectionResult Validate(string workspacePath) => ValidateInternal(workspacePath, "validated", false);
    public UiDirectionResult Status(string workspacePath) => ValidateInternal(workspacePath, "status", false);

    public UiDirectionResult Approve(string workspacePath, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return new UiDirectionResult("invalid", workspacePath, null, null, null, null, null, ["Reviewer and approval reason are required."], false);
        var assessed = Validate(workspacePath);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true, Current: true }) return assessed with { Status = "blocked" };
        var state = Resolve(workspacePath); var content = File.ReadAllText(state.Path!);
        if (assessed.Validation.EffectiveStatus == "Active" && ReadNested(content, "approved_by") == reviewer.Trim()
            && ReadNested(content, "approval_reason") == reason.Trim()) return assessed with { Status = "unchanged" };
        var now = _clock().ToUniversalTime();
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNested(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNested(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNested(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        content = ReplaceNested(content, "approved_content_hash", "null");
        content = ReplaceNested(content, "approved_content_hash", JsonSerializer.Serialize(ApprovalDigest(content)));
        Write(state.Path!, content);
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        Write(state.Context.CatalogPath, UpdateCatalogStatus(catalog, $"{state.Authority!.Id}:design:ui-direction", "active"));
        return ValidateInternal(workspacePath, "approved", true);
    }

    public ChangeReadinessResult Evaluate(string repositoryPath)
    {
        if (!File.Exists(Path.Combine(Path.GetFullPath(repositoryPath), ".cis", "workspace.yml")))
            return new ChangeReadinessResult("ui-direction", false, true, []);
        var result = Status(repositoryPath);
        var ready = result.Validation is { } validation
            && CisDefinitionDraftScope.Accepts(validation.Valid, validation.Current, validation.EffectiveStatus);
        var errors = ready ? [] : result.Errors.Concat(result.Validation?.Errors ?? [])
            .Append("An Active, current high-level UI direction is required. Run `cis ui-direction status`.")
            .Distinct(StringComparer.Ordinal).ToArray();
        return new ChangeReadinessResult("ui-direction", true, ready, errors);
    }

    private UiDirectionResult ValidateInternal(string workspacePath, string operation, bool applied)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        var relative = Normalize(Path.GetRelativePath(state.Authority!.RepositoryPath, state.Path!));
        if (!File.Exists(state.Path!))
            return new UiDirectionResult("missing", state.Workspace!.WorkspacePath, state.Authority.Id, relative,
                state.SolutionVersion, state.Questionnaire?.Digest, new(false, false, "Missing", "Missing",
                    ["Canonical high-level UI direction is missing. Run `cis ui-direction init`."], state.ReadinessErrors), [], applied);
        var content = File.ReadAllText(state.Path); var errors = new List<string>(); var warnings = new List<string>(state.ReadinessErrors);
        if (ReadFrontMatter(content, "type") != "ui-direction") errors.Add("UI direction must declare `type: ui-direction`.");
        if (ReadNested(content, "stable_id") != $"{state.Authority.Id}:design:ui-direction") errors.Add("UI direction has an invalid stable identity.");
        if (ReadNested(content, "ui_direction_schema") != "1") errors.Add("UI-direction schema metadata is missing or unsupported.");
        if (!content.Contains(ManagedStart, StringComparison.Ordinal) || !content.Contains(ManagedEnd, StringComparison.Ordinal)) errors.Add("UI-direction managed markers are missing.");
        foreach (var heading in RequiredHeadings)
            if (string.IsNullOrWhiteSpace(ExtractSection(content, heading))) errors.Add($"UI-direction section is missing or empty: {heading}");
        if (Regex.IsMatch(content, @"\b(?:TODO|TBD|Not answered)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            errors.Add("UI direction contains an incomplete placeholder.");
        foreach (var question in state.Questionnaire?.Questions ?? [])
            if (!content.Contains($"`{question.Id}`", StringComparison.Ordinal)) errors.Add($"UI direction does not trace `{question.Id}`.");
        var current = state.ReadinessErrors.Count == 0
            && ReadNested(content, "solution_design_hash") == state.SolutionVersion
            && ReadNested(content, "questionnaire_hash") == state.Questionnaire?.Digest
            && ReadNested(content, "design_guidelines_hash") == state.DesignGuidelinesDigest
            && ReadNested(content, "ui_framework_profiles_hash") == state.FrameworkProfilesDigest;
        if (!current) warnings.Add("High-level UI direction differs from its current architecture, questionnaire, design-guideline, or UI-framework baseline.");
        var status = ReadFrontMatter(content, "status") ?? "Unknown";
        if (status == "Active")
        {
            if (ReadNested(content, "approved_content_hash") != ApprovalDigest(content)) { current = false; warnings.Add("Approved UI-direction content changed after approval."); }
            if (string.IsNullOrWhiteSpace(ReadNested(content, "approved_by")) || ReadNested(content, "approved_by") == "null") errors.Add("Active UI direction requires approval authority.");
        }
        var valid = errors.Count == 0;
        var effective = status == "Active" ? valid && current ? "Active" : "Stale" : valid && current ? "Ready for Approval" : "Review Required";
        return new UiDirectionResult(operation, state.Workspace!.WorkspacePath, state.Authority.Id, relative,
            state.SolutionVersion, state.Questionnaire?.Digest,
            new(valid, current, effective, status, errors.Distinct().Order().ToArray(), warnings.Distinct().Order().ToArray()), [], applied);
    }

    private State Resolve(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null) return new(null, null, null, null, null, null, null, null, [], resolution.Errors);
        var authority = resolution.Workspace.AuthorityRepository;
        if (authority is null) return new(resolution.Workspace, null, null, null, null, null, null, null, [], ["Workspace has no authority repository."]);
        var context = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!context.IsSuccess || context.Context is null) return new(resolution.Workspace, authority, null, null, null, null, null, null, [], context.Errors);
        var docs = authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(authority.RepositoryPath, docs, "design", "ui-direction.md");
        var solutionStatus = _solutionDesign.Status(resolution.Workspace.WorkspacePath);
        var solutionReady = solutionStatus.Validation is { } solutionValidation
            && CisDefinitionDraftScope.Accepts(solutionValidation.Valid, solutionValidation.Current, solutionValidation.EffectiveStatus);
        var sourceVersion = solutionReady ? UiDirectionQuestionnaireService.SourceDigest(authority.RepositoryPath, docs) : null;
        var snapshot = _questionnaire.Snapshot(resolution.Workspace.WorkspacePath);
        var readiness = new List<string>();
        if (!solutionReady || string.IsNullOrWhiteSpace(sourceVersion)) readiness.Add("An Active, current overall solution-design bundle is required.");
        if (snapshot is not { Current: true, Complete: true }) readiness.Add("A complete, current UI-direction questionnaire is required. Run `cis ui-direction questions status`.");
        var guideline = Path.Combine(authority.RepositoryPath, docs, "specs", "design-guidelines.md");
        if (!File.Exists(guideline)) readiness.Add("Active design guidelines are required at `specs/design-guidelines.md`.");
        var guidelineDigest = File.Exists(guideline) ? Digest(File.ReadAllText(guideline)) : null;
        var profiles = resolution.Workspace.Repositories.Select(repository => Path.Combine(repository.RepositoryPath,
                repository.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "references", "ui-framework-profile.md"))
            .Where(File.Exists).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var profileDigest = Digest(string.Join("\n--- ui-framework-profile ---\n", profiles.Select(File.ReadAllText)));
        return new(resolution.Workspace, authority, context.Context, path, sourceVersion, snapshot, guidelineDigest,
            profileDigest, readiness.Distinct().ToArray(), []);
    }

    private static string Render(State state, string stableId)
    {
        string Answer(string id) => state.Questionnaire!.Questions.First(question => question.Id == id).Answer!;
        string Trace(params string[] ids) => string.Join(", ", ids.Select(id => $"`{id}`"));
        var managed = $"""
## Experience intent and principles

{Answer("UI-Q-002")}

Authority: {Trace("UI-Q-002")}.

## Product surfaces and audience classes

{Answer("UI-Q-001")}

Public, customer, backoffice, and native surfaces keep distinct trust and disclosure behavior while sharing the approved visual language. Authority: {Trace("UI-Q-001")}.

## Shell and navigation model

{Answer("UI-Q-003")}

The application shell is reusable product infrastructure. Feature designs may add destinations within it but may not silently replace its navigation model. Authority: {Trace("UI-Q-003")}.

## Visual language and tokens

{Answer("UI-Q-005")}

The canonical design guidelines supply the starting color, spacing, elevation, radius, status, and contrast tokens. Product-specific replacements must be named, reusable, and reviewed here. Authority: {Trace("UI-Q-005")}.

## Layout, spacing, and density

{Answer("UI-Q-004")}

Feature designs must preserve information hierarchy and task order at every supported viewport rather than merely scaling a desktop image. Authority: {Trace("UI-Q-004")}.

## Typography, content, iconography, and imagery

{Answer("UI-Q-006")}

Text remains understandable without decorative imagery; icon-only actions require accessible names and an unambiguous familiar symbol. Authority: {Trace("UI-Q-006")}.

## Reusable component system

{Answer("UI-Q-007")}

{Answer("UI-Q-011")}

Shared controls include their focus, validation, loading, disabled, denied, empty, error, and recovery behavior. Feature renderers consume governed templates instead of redrawing common controls. Authority: {Trace("UI-Q-007", "UI-Q-011")}.

## State, feedback, and motion

{Answer("UI-Q-010")}

No transition may conceal completion or failure. Destructive operations remain explicit, and animation never becomes the only carrier of meaning. Authority: {Trace("UI-Q-010")}.

## Responsive and platform adaptation

{Answer("UI-Q-008")}

Platform conventions may adapt presentation, but capabilities, terminology, and safety boundaries remain coherent. Authority: {Trace("UI-Q-008")}.

## Accessibility and inclusive interaction

{Answer("UI-Q-009")}

Accessibility is an acceptance boundary for textual wireframes, rendered designs, implementation, and verification. Authority: {Trace("UI-Q-009")}.

## Constraints and exclusions

{Answer("UI-Q-012")}

The high-level direction does not authorize new business behavior, architecture, data access, or product surfaces. Authority: {Trace("UI-Q-012")}.

## Feature-design handoff

Each UI-bearing feature consumes this direction together with its applicable component and contract boundaries. Its textual wireframe defines screen structure, actions, states, and paths; its deterministic Sharp/SVG design pack proves the visual interpretation. A feature-level design may specialize this direction but cannot contradict it without first renewing this artifact.

## Traceability

- Solution architecture baseline: `{state.SolutionVersion}`.
- UI questionnaire baseline: `{state.Questionnaire!.Digest}`.
- Design-guideline baseline: `{state.DesignGuidelinesDigest}`.
- UI-framework-profile baseline: `{state.FrameworkProfilesDigest}`.
- The questionnaire records the exact authority for {Trace(DefinitionsIds)}.
""";
        return $"""
---
title: "{state.Authority!.Id} High-Level UI Direction"
type: ui-direction
status: Review Required
scope: Workspace
owner: Product owner and design authority
last_reviewed: null
review_cadence: on approved surface, shell, brand, design-system, accessibility, or source-baseline change
cis:
  stable_id: {stableId}
  ui_direction_schema: 1
  solution_design_hash: {state.SolutionVersion}
  questionnaire_hash: {state.Questionnaire.Digest}
  design_guidelines_hash: {state.DesignGuidelinesDigest}
  ui_framework_profiles_hash: {state.FrameworkProfilesDigest}
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# {state.Authority.Id} High-Level UI Direction

This workspace-level authority defines the intended look, feel, application shell, interaction language, reusable component basis, responsive behavior, and accessibility baseline. It governs later feature wireframes and visual design packs without prescribing their detailed screens.

{ManagedStart}
{managed.Trim()}
{ManagedEnd}

## UI-specific decisions and accepted exceptions

No additional exception is accepted. A durable departure requires an explicit design-system or architecture decision and renewed approval of this document.
""";
    }

    private static readonly string[] RequiredHeadings = ["Experience intent and principles", "Product surfaces and audience classes", "Shell and navigation model", "Visual language and tokens", "Layout, spacing, and density", "Typography, content, iconography, and imagery", "Reusable component system", "State, feedback, and motion", "Responsive and platform adaptation", "Accessibility and inclusive interaction", "Constraints and exclusions", "Feature-design handoff", "Traceability", "UI-specific decisions and accepted exceptions"];
    private static readonly string[] DefinitionsIds = ["UI-Q-001", "UI-Q-002", "UI-Q-003", "UI-Q-004", "UI-Q-005", "UI-Q-006", "UI-Q-007", "UI-Q-008", "UI-Q-009", "UI-Q-010", "UI-Q-011", "UI-Q-012"];

    private static string? Reconcile(string path, string generated, string stableId)
    {
        if (!File.Exists(path)) return generated;
        var existing = File.ReadAllText(path); if (!existing.Contains($"stable_id: {stableId}", StringComparison.Ordinal)) return null;
        var next = ReplaceBlock(existing, ManagedStart, ManagedEnd, ReadBlock(generated, ManagedStart, ManagedEnd));
        foreach (var key in new[] { "solution_design_hash", "questionnaire_hash", "design_guidelines_hash", "ui_framework_profiles_hash" }) next = ReplaceNested(next, key, ReadNested(generated, key)!);
        return Equivalent(existing, next) ? next : ResetApproval(next);
    }
    private static string ResetApproval(string content)
    {
        content = ReplaceFrontMatter(content, "status", "Review Required"); content = ReplaceFrontMatter(content, "last_reviewed", "null");
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" }) content = ReplaceNested(content, key, "null");
        return content;
    }
    private static string ApprovalDigest(string content) => Digest(ReplaceNested(content, "approved_content_hash", "null"));
    private static string Digest(string content) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()))).ToLowerInvariant();
    private static string ExtractSection(string content, string heading) => Regex.Match(content, $@"(?ms)^## {Regex.Escape(heading)}\s*$\n(?<body>.*?)(?=^## |\z)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match ? match.Groups["body"].Value.Trim() : string.Empty;
    private static string ReadBlock(string content, string start, string end) { var from = content.IndexOf(start, StringComparison.Ordinal); var to = content.IndexOf(end, StringComparison.Ordinal); return from < 0 || to <= from ? string.Empty : content[(from + start.Length)..to].Trim(); }
    private static string ReplaceBlock(string content, string start, string end, string body) { var from = content.IndexOf(start, StringComparison.Ordinal); var to = content.IndexOf(end, StringComparison.Ordinal); return from < 0 || to <= from ? content : content[..(from + start.Length)] + "\n" + body.Trim() + "\n" + content[to..]; }
    private static string? ReadFrontMatter(string content, string key) => Regex.Match(content, $@"(?m)^{Regex.Escape(key)}:\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match ? Unquote(match.Groups["value"].Value) : null;
    private static string? ReadNested(string content, string key) => Regex.Match(content, $@"(?m)^  {Regex.Escape(key)}:\s*(?<value>.*?)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) is { Success: true } match ? Unquote(match.Groups["value"].Value) : null;
    private static string ReplaceFrontMatter(string content, string key, string value) => Regex.Replace(content, $@"(?m)^{Regex.Escape(key)}:\s*.*$", $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string ReplaceNested(string content, string key, string value) => Regex.Replace(content, $@"(?m)^  {Regex.Escape(key)}:\s*.*$", $"  {key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string Unquote(string value) => value.Trim().Trim('"');
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static void Write(string path, string content) => File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    private static string UpdateCatalogStatus(string catalog, string stableId, string status)
    {
        var match = Regex.Match(catalog, $@"(?ms)(?<entry>^  - id:\s*{Regex.Escape(stableId)}\s*$.*?)(?=^  - id:|\z)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); if (!match.Success) return catalog;
        var entry = Regex.Replace(match.Groups["entry"].Value, @"(?m)^    status:\s*.*$", $"    status: {status}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return catalog[..match.Index] + entry + catalog[(match.Index + match.Length)..];
    }
    private static UiDirectionResult Error(string status, State state, IReadOnlyList<string> errors) => new(status, state.Workspace?.WorkspacePath,
        state.Authority?.Id, state.Path is null || state.Authority is null ? null : Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.Path)), state.SolutionVersion, state.Questionnaire?.Digest, null, errors, false);

    private sealed record State(CisWorkspace? Workspace, CisWorkspaceRepository? Authority, CisRepositoryContext? Context,
        string? Path, string? SolutionVersion, UiDirectionQuestionnaireService.QuestionnaireSnapshot? Questionnaire,
        string? DesignGuidelinesDigest, string? FrameworkProfilesDigest, IReadOnlyList<string> ReadinessErrors, IReadOnlyList<string> Errors);
}
