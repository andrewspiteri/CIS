using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.SolutionDesign;
using Cis.Modules.TechnicalIntent;
using Cis.Modules.UiDirection;

namespace Cis.Modules.Definition;

public sealed class DefinitionWizardService
{
    private const string SessionRelativePath = ".cis/local/definition-wizard/session.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] DictionaryKinds =
    [
        "api-dictionary", "command-dictionary", "event-dictionary", "workflow-state-dictionary",
        "projection-dictionary", "permissions-dictionary", "configuration-dictionary", "data-dictionary",
        "problem-details-catalogue", "screen-route-map", "package-catalogue", "module-ownership-map",
        "business-invariant-catalogue", "traceability-matrix", "erd",
    ];

    private readonly ICisWorkspaceRegistry _workspaces;
    private readonly ICisRepositoryContextResolver _repositories;
    private readonly DocumentationCatalogMerger _catalog;
    private readonly RepositoryInitializer _repositoryInitializer;
    private readonly BrdService _brd;
    private readonly BrdBacklogService _backlog;
    private readonly TechnicalIntentQuestionnaireService _technicalQuestions;
    private readonly TechnicalIntentService _technicalIntent;
    private readonly SolutionDesignService _solutionDesign;
    private readonly UiDirectionQuestionnaireService _uiQuestions;
    private readonly UiDirectionService _uiDirection;
    private readonly GraphBuilder _graph;
    private readonly Func<DateTimeOffset> _clock;

    public DefinitionWizardService(
        ICisWorkspaceRegistry workspaces,
        ICisRepositoryContextResolver repositories,
        DocumentationCatalogMerger catalog,
        RepositoryInitializer repositoryInitializer,
        BrdService brd,
        BrdBacklogService backlog,
        TechnicalIntentQuestionnaireService technicalQuestions,
        TechnicalIntentService technicalIntent,
        SolutionDesignService solutionDesign,
        UiDirectionQuestionnaireService uiQuestions,
        UiDirectionService uiDirection,
        GraphBuilder graph,
        Func<DateTimeOffset>? clock = null)
    {
        _workspaces = workspaces;
        _repositories = repositories;
        _catalog = catalog;
        _repositoryInitializer = repositoryInitializer;
        _brd = brd;
        _backlog = backlog;
        _technicalQuestions = technicalQuestions;
        _technicalIntent = technicalIntent;
        _solutionDesign = solutionDesign;
        _uiQuestions = uiQuestions;
        _uiDirection = uiDirection;
        _graph = graph;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public CisDefinitionWizardResult Initialize(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error(state, state.Errors);
        var foundationErrors = ReconcileFoundation(state);
        if (foundationErrors.Count > 0) return Error(state, foundationErrors);
        var existingSession = ReadSession(state);
        var now = _clock().ToUniversalTime();
        var session = existingSession is { Active: true }
            ? existingSession
            : new WizardSession(1,
                "DEF-" + now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                "foundation", now.ToString("O"), null, true);
        WriteAtomic(state.SessionPath!, JsonSerializer.Serialize(session with { Active = true }, JsonOptions));
        var refreshErrors = TryRefreshDerived(state);
        if (refreshErrors.Count > 0) return Error(state, refreshErrors);
        return StatusInternal(state, "initialized", applied: true);
    }

    public CisDefinitionWizardResult Status(string workspacePath)
    {
        var state = Resolve(workspacePath);
        return state.Errors.Count > 0 ? Error(state, state.Errors) : StatusInternal(state, "status", false);
    }

    public CisDefinitionWizardResult Prepare(string workspacePath, string page)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error(state, state.Errors);
        var normalized = NormalizePage(page);
        if (normalized is null) return Error(state, [$"Unknown definition-wizard page: {page}"]);
        EnsureSession(state, normalized);
        if (normalized is "foundation" or "contracts")
        {
            var foundationErrors = ReconcileFoundation(state);
            if (foundationErrors.Count > 0) return Error(state, foundationErrors);
        }
        using (CisDefinitionDraftScope.Enter())
        {
            switch (normalized)
            {
                case "technical":
                    var technicalQuestions = _technicalQuestions.Initialize(state.WorkspacePath!);
                    if (technicalQuestions.Complete && technicalQuestions.Current)
                        _technicalIntent.Initialize(state.WorkspacePath!);
                    break;
                case "architecture":
                    _solutionDesign.Initialize(state.WorkspacePath!);
                    break;
                case "experience":
                    var uiQuestions = _uiQuestions.Initialize(state.WorkspacePath!);
                    if (uiQuestions.Complete && uiQuestions.Current)
                        _uiDirection.Initialize(state.WorkspacePath!);
                    break;
                case "delivery":
                    _backlog.Build(state.WorkspacePath!);
                    break;
            }
        }
        var refreshErrors = TryRefreshDerived(state);
        if (refreshErrors.Count > 0) return Error(state, refreshErrors);
        return StatusInternal(state, "prepared", true);
    }

    public CisDefinitionWizardResult Answer(string workspacePath, string page, string id, string answer, string actor)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error(state, state.Errors);
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(actor))
            return Error(state, ["Answer and actor are required."]);
        var normalized = NormalizePage(page);
        if (normalized is not ("technical" or "experience"))
            return Error(state, ["Wizard answers are supported for the technical and experience pages."]);
        EnsureSession(state, normalized);
        using (CisDefinitionDraftScope.Enter())
        {
            if (normalized == "technical")
            {
                var result = _technicalQuestions.Answer(state.WorkspacePath!, id, answer, actor);
                if (result.ExitCode != 0) return Error(state, result.Errors);
                if (result.Complete && result.Current) _technicalIntent.Initialize(state.WorkspacePath!);
            }
            else
            {
                var result = _uiQuestions.Answer(state.WorkspacePath!, id, answer, actor);
                if (result.ExitCode != 0) return Error(state, result.Errors);
                if (result.Complete && result.Current) _uiDirection.Initialize(state.WorkspacePath!);
            }
        }
        var refreshErrors = TryRefreshDerived(state);
        if (refreshErrors.Count > 0) return Error(state, refreshErrors);
        return StatusInternal(state, "answered", true);
    }

    public CisDefinitionWizardResult Activate(string workspacePath, string reviewer)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error(state, state.Errors);
        if (string.IsNullOrWhiteSpace(reviewer)) return Error(state, ["Reviewer identity is required."]);
        EnsureSession(state, "review");
        var refreshErrors = TryRefreshDerived(state);
        if (refreshErrors.Count > 0) return Error(state, refreshErrors);
        var before = StatusInternal(state, "activation-check", false);
        if (!before.ReadyToActivate)
            return before with { Status = "blocked", Errors = before.Errors.Concat(["The complete high-level baseline is not ready to activate."]).Distinct().ToArray() };

        var protectedPaths = CanonicalPaths(state).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var snapshots = protectedPaths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var reason = "Approved through the high-level product-definition wizard.";
        try
        {
            using (CisDefinitionDraftScope.Enter())
            {
                RequireApproved(_brd.Approve(state.WorkspacePath!, reviewer, reason).Validation?.EffectiveStatus, "business requirements");
                RequireApproved(_technicalIntent.Approve(state.WorkspacePath!, reviewer, reason).Validation?.EffectiveStatus, "technical intent");
                RequireApproved(_solutionDesign.Approve(state.WorkspacePath!, reviewer, reason).Validation?.EffectiveStatus, "solution design");
                RequireApproved(_uiDirection.Approve(state.WorkspacePath!, reviewer, reason).Validation?.EffectiveStatus, "UI direction");
                RequireApproved(_backlog.Approve(state.WorkspacePath!, reviewer, reason).Validation?.EffectiveStatus, "high-level backlog");
            }
            foreach (var path in new[] { state.DiagramPath!, state.DictionaryIndexPath!, state.PreviewPath! })
                ActivateDerived(path, reviewer, reason);
            UpdateDerivedCatalogStatus(state, "active");
            var session = ReadSession(state)! with { Active = false, CurrentPage = "review", ActivatedAtUtc = _clock().ToUniversalTime().ToString("O") };
            WriteAtomic(state.SessionPath!, JsonSerializer.Serialize(session, JsonOptions));
            var graph = _graph.Build(state.AuthorityRepositoryPath!);
            if (graph.ExitCode != 0)
                throw new InvalidOperationException("Context graph rebuild failed: " + string.Join("; ",
                    graph.Diagnostics.Where(item => item.Severity == "error").Select(item => item.Message)));
            return StatusInternal(state, "activated", true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            foreach (var snapshot in snapshots) WriteBytesAtomic(snapshot.Key, snapshot.Value);
            return StatusInternal(state, "rolled-back", true) with
            {
                Errors = [$"Activation failed and canonical files were restored: {exception.Message}"],
                ReadyToActivate = false,
            };
        }
    }

    private CisDefinitionWizardResult StatusInternal(State state, string operation, bool applied)
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var brd = _brd.Status(state.WorkspacePath!);
        var technicalQuestions = _technicalQuestions.Status(state.WorkspacePath!);
        var technical = _technicalIntent.Status(state.WorkspacePath!);
        var solution = _solutionDesign.Status(state.WorkspacePath!);
        var uiQuestions = _uiQuestions.Status(state.WorkspacePath!);
        var ui = _uiDirection.Status(state.WorkspacePath!);
        var backlog = _backlog.Status(state.WorkspacePath!);
        var dictionaries = ReadDictionaries(state);
        var diagrams = ReadDiagrams(state, solution);
        var preview = ReadPreview(state, uiQuestions);
        var session = ReadSession(state);

        var pages = new List<CisDefinitionPage>
        {
            Page("foundation", 1, "Project foundation", dictionaries.Any(item => item.Applicable) ? "Ready" : "Incomplete",
                dictionaries.Any(item => item.Applicable), true, null,
                dictionaries.Select(item => item.RelativePath), dictionaries.Any(item => item.Applicable)
                    ? [] : ["No governed starter dictionaries are present. Prepare the foundation page to reconcile repository initialization."]),
            Page("business", 2, "Business definition", Effective(brd.Validation?.EffectiveStatus),
                Accepted(brd.Validation?.Valid, brd.Validation?.Current, brd.Validation?.EffectiveStatus), brd.Validation?.Current == true,
                brd.CanonicalPath, brd.CanonicalPath is null ? [] : [brd.CanonicalPath], brd.Errors.Concat(brd.Validation?.Errors ?? [])),
            Page("technical", 3, "Technical direction", Effective(technical.Validation?.EffectiveStatus),
                technicalQuestions.Complete && technicalQuestions.Current && Accepted(technical.Validation?.Valid, technical.Validation?.Current, technical.Validation?.EffectiveStatus),
                technicalQuestions.Current && technical.Validation?.Current == true, technical.CanonicalPath,
                Existing(technicalQuestions.CanonicalPath, technical.CanonicalPath), technicalQuestions.Errors.Concat(technical.Errors).Concat(technical.Validation?.Errors ?? [])),
            Page("architecture", 4, "Solution architecture and diagrams", Effective(solution.Validation?.EffectiveStatus),
                Accepted(solution.Validation?.Valid, solution.Validation?.Current, solution.Validation?.EffectiveStatus) && diagrams.Count > 0 && diagrams.All(item => item.Status != "Stale"),
                solution.Validation?.Current == true && diagrams.All(item => item.Status != "Stale"), solution.DesignPath,
                Existing(solution.DesignPath, solution.ComponentSheetPath, state.DiagramRelativePath), solution.Errors.Concat(solution.Validation?.Errors ?? [])),
            Page("contracts", 5, "Contracts and dictionaries", dictionaries.Any(item => item.Applicable) ? "Ready" : "Incomplete",
                dictionaries.Any(item => item.Applicable) && File.Exists(state.DictionaryIndexPath)
                    && DerivedStatus(state.DictionaryIndexPath!, DictionarySourceHash(dictionaries)) != "Stale",
                File.Exists(state.DictionaryIndexPath) && DerivedStatus(state.DictionaryIndexPath!, DictionarySourceHash(dictionaries)) != "Stale", state.DictionaryIndexRelativePath,
                Existing(state.DictionaryIndexRelativePath).Concat(dictionaries.Where(item => item.Applicable).Select(item => item.RelativePath)),
                dictionaries.Any(item => item.Applicable) ? [] : ["No classification-selected dictionaries were found. Re-run repository initialization."]),
            Page("experience", 6, "Experience direction and UI preview", Effective(ui.Validation?.EffectiveStatus),
                uiQuestions.Complete && uiQuestions.Current && Accepted(ui.Validation?.Valid, ui.Validation?.Current, ui.Validation?.EffectiveStatus) && preview is not null && preview.Status != "Stale",
                uiQuestions.Current && ui.Validation?.Current == true && preview?.Status != "Stale", ui.RelativePath,
                Existing(uiQuestions.RelativePath, ui.RelativePath, state.PreviewRelativePath, state.PreviewSvgRelativePath), uiQuestions.Errors.Concat(ui.Errors).Concat(ui.Validation?.Errors ?? [])),
            Page("delivery", 7, "Delivery map", Effective(backlog.Validation?.EffectiveStatus),
                Accepted(backlog.Validation?.Valid, backlog.Validation?.Current, backlog.Validation?.EffectiveStatus), backlog.Validation?.Current == true,
                backlog.RelativePath, Existing(backlog.RelativePath), backlog.Errors.Concat(backlog.Validation?.Errors ?? [])),
        };
        var ready = pages.All(page => page.Complete && page.Current);
        pages.Add(Page("review", 8, "Review and activate", ready ? "Ready to activate" : "Needs attention",
            ready, true, null, pages.SelectMany(page => page.ArtifactPaths), pages.SelectMany(page => page.Issues)));
        var warnings = pages.SelectMany(page => page.Issues).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToArray();
        return new CisDefinitionWizardResult(operation, state.WorkspacePath, state.AuthorityRepositoryId,
            session?.SessionId, session?.CurrentPage ?? "foundation", session?.Active ?? false, ready,
            pages, dictionaries, diagrams, preview, warnings, [], applied);
    }

    private void RefreshDerived(State state)
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var solution = _solutionDesign.Status(state.WorkspacePath!);
        var uiQuestions = _uiQuestions.Status(state.WorkspacePath!);
        var ui = _uiDirection.Status(state.WorkspacePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(state.DiagramPath!)!);
        Directory.CreateDirectory(Path.GetDirectoryName(state.DictionaryIndexPath!)!);
        Directory.CreateDirectory(Path.GetDirectoryName(state.PreviewPath!)!);
        if (Accepted(solution.Validation?.Valid, solution.Validation?.Current, solution.Validation?.EffectiveStatus))
            WriteDerived(state.DiagramPath!, RenderDiagrams(state, solution));
        var dictionaries = ReadDictionaries(state);
        WriteDerived(state.DictionaryIndexPath!, RenderDictionaryIndex(state, dictionaries));
        if (uiQuestions.Complete && uiQuestions.Current && Accepted(ui.Validation?.Valid, ui.Validation?.Current, ui.Validation?.EffectiveStatus))
        {
            var preview = CreatePreview(state, uiQuestions);
            WriteAtomic(state.PreviewSvgPath!, RenderPreviewSvg(preview));
            WriteDerived(state.PreviewPath!, RenderPreviewDocument(state, preview));
        }
        MergeDerivedCatalog(state);
    }

    private IReadOnlyList<string> TryRefreshDerived(State state)
    {
        try
        {
            RefreshDerived(state);
            return [];
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return [$"Definition artifacts could not be refreshed: {exception.Message}"];
        }
    }

    private IReadOnlyList<string> ReconcileFoundation(State state)
    {
        var documentationRoot = Normalize(Path.GetRelativePath(state.AuthorityRepositoryPath!, state.DocumentationPath!));
        var result = _repositoryInitializer.SeedAuthorityReferences(state.AuthorityRepositoryPath!, documentationRoot);
        return result.Errors.Concat(result.Collisions).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string RenderDiagrams(State state, SolutionDesignResult solution)
    {
        var interactions = ReadInteractions(state, solution);
        var componentNodes = solution.Components.Count == 0
            ? "  system[Product system]"
            : string.Join('\n', solution.Components.Select(item => $"  {Node(item.Id)}[{Mermaid(item.Name)}]"));
        var componentLinks = string.Join('\n', interactions
            .Select(item => (Source: ComponentId(solution.Components, item.Source), Target: ComponentId(solution.Components, item.Target), item.Trigger))
            .Where(item => item.Source is not null && item.Target is not null)
            .Select(item => $"  {item.Source} -->|{Mermaid(Short(item.Trigger, 42))}| {item.Target}")
            .Distinct(StringComparer.Ordinal).Take(30));
        var endpoints = interactions.SelectMany(item => new[] { item.Source, item.Target }).Concat(solution.Components.Select(item => item.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var externalEndpoints = endpoints.Where(endpoint => ComponentId(solution.Components, endpoint) is null).ToArray();
        var externalNodes = string.Join('\n', externalEndpoints.Select(endpoint => $"  {ExternalNode(endpoint)}([{Mermaid(endpoint)}])"));
        var contextLinks = string.Join('\n', interactions.SelectMany(item => new[]
            {
                ComponentId(solution.Components, item.Source) is null ? $"  {ExternalNode(item.Source)} --> system" : string.Empty,
                ComponentId(solution.Components, item.Target) is null ? $"  system --> {ExternalNode(item.Target)}" : string.Empty,
            }).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).Take(20));
        var integrationNodes = string.Join('\n', endpoints.Select(endpoint => ComponentId(solution.Components, endpoint) is { } id
            ? $"  {id}[{Mermaid(endpoint)}]" : $"  {ExternalNode(endpoint)}([{Mermaid(endpoint)}])"));
        var integrationLinks = string.Join('\n', interactions.Select(item =>
            $"  {ComponentId(solution.Components, item.Source) ?? ExternalNode(item.Source)} -->|{Mermaid(Short(item.Contract, 48))}| {ComponentId(solution.Components, item.Target) ?? ExternalNode(item.Target)}")
            .Distinct(StringComparer.Ordinal).Take(30));
        var externalClass = externalEndpoints.Length == 0 ? string.Empty
            : $"  class {string.Join(',', externalEndpoints.Select(ExternalNode))} external";
        var source = DiagramSourceHash(state, solution);
        return $"""
---
title: "{state.AuthorityRepositoryId} High-Level Architecture Diagrams"
type: architecture-diagram-set
status: Review Required
scope: Workspace
owner: Product owner and architecture maintainers
last_reviewed: null
cis:
  stable_id: {state.AuthorityRepositoryId}:architecture:high-level-diagrams
  diagram_schema: 1
  source_hash: {source}
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# {state.AuthorityRepositoryId} High-Level Architecture Diagrams

The Mermaid sources are canonical and are regenerated from the current solution-design and component-sheet bundle.

## System context

```mermaid
flowchart LR
  user[Product users and operators] --> system[{Mermaid(state.AuthorityRepositoryId!)}]
{externalNodes}
{contextLinks}
```

## Component topology

```mermaid
flowchart LR
{componentNodes}
{componentLinks}
```

## Integration and trust boundaries

```mermaid
flowchart LR
{integrationNodes}
{integrationLinks}
  classDef external stroke-dasharray: 5 5
{externalClass}
```

## Deployment and operations

```mermaid
flowchart TB
  delivery[Delivery pipeline] --> runtime[Provider-neutral runtime]
  runtime --> telemetry[Logs, metrics, traces and alerts]
  runtime --> recovery[Backup, migration and recovery controls]
```
""";
    }

    private static string RenderDictionaryIndex(State state, IReadOnlyList<CisDefinitionDictionary> dictionaries)
    {
        var rows = string.Join('\n', dictionaries.Select(item =>
            $"| `{item.Kind}` | [{Cell(item.Title)}]({RelativeFrom(state.DictionaryIndexPath!, Path.Combine(state.AuthorityRepositoryPath!, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)))}) | {(item.Applicable ? "Applicable" : "Not applicable")} | {item.EntryCount} | {item.Status} |"));
        return $"""
---
title: "{state.AuthorityRepositoryId} Contract and Dictionary Index"
type: reference-index
status: Review Required
scope: Workspace
owner: Product owner and architecture maintainers
last_reviewed: null
cis:
  stable_id: {state.AuthorityRepositoryId}:reference:dictionary-index
  dictionary_index_schema: 1
  source_hash: {DictionarySourceHash(dictionaries)}
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# Contract and Dictionary Index

This classification-selected index makes shared contracts discoverable before the feature loop. Features extend the owning dictionary rather than creating disconnected copies.

| Kind | Canonical reference | Applicability | Entries | Lifecycle |
| --- | --- | --- | ---: | --- |
{rows}
""";
    }

    private static PreviewModel CreatePreview(State state, UiDirectionQuestionnaireResult questions)
    {
        string Answer(string id) => questions.Questions.FirstOrDefault(item => item.Id == id)?.Answer
            ?? questions.Questions.FirstOrDefault(item => item.Id == id)?.SuggestedAnswer ?? string.Empty;
        var colors = Regex.Matches(Answer("UI-Q-005"), "#[0-9a-fA-F]{6}").Select(match => match.Value).Distinct().Take(4).ToArray();
        var palette = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["background"] = colors.ElementAtOrDefault(0) ?? "#f6f8fb",
            ["surface"] = colors.ElementAtOrDefault(1) ?? "#ffffff",
            ["foreground"] = colors.ElementAtOrDefault(2) ?? "#172033",
            ["accent"] = colors.ElementAtOrDefault(3) ?? "#2563eb",
            ["success"] = "#15803d", ["warning"] = "#b45309", ["error"] = "#b91c1c",
        };
        var densityAnswer = Answer("UI-Q-004");
        var density = densityAnswer.Contains("compact", StringComparison.OrdinalIgnoreCase) ? "Compact"
            : densityAnswer.Contains("spacious", StringComparison.OrdinalIgnoreCase) ? "Spacious" : "Balanced";
        var radius = Answer("UI-Q-005").Contains("sharp", StringComparison.OrdinalIgnoreCase) ? "2px" : "6px";
        var font = Answer("UI-Q-006").Contains("system", StringComparison.OrdinalIgnoreCase)
            ? "system-ui, sans-serif" : "Inter, Segoe UI, sans-serif";
        var surfaces = SplitChoices(Answer("UI-Q-001"), ["Public", "Customer", "Backoffice", "Mobile", "Desktop", "Embedded"]);
        var components = SplitChoices(Answer("UI-Q-011"), ["Buttons", "Text inputs", "Dropdowns", "Checkboxes", "Tables", "Flat lists", "Cards", "Dialogs", "Status feedback"]);
        return new PreviewModel(font, density, radius, palette, components, surfaces,
            Digest(string.Join('|', questions.SolutionDesignVersion, questions.Questions.Select(item => item.Answer))));
    }

    private static string RenderPreviewDocument(State state, PreviewModel preview)
        => $"""
---
title: "{state.AuthorityRepositoryId} UI System Preview"
type: ui-system-preview
status: Review Required
scope: Workspace
owner: Product owner and design authority
last_reviewed: null
cis:
  stable_id: {state.AuthorityRepositoryId}:design:ui-system-preview
  preview_schema: 1
  source_hash: {preview.SourceHash}
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# UI System Preview

![One-page UI system preview](ui-system-preview.svg)

## Selected visual system

| Property | Decision |
| --- | --- |
| Typography | `{Cell(preview.FontFamily)}` |
| Density | {preview.Density} |
| Radius | `{preview.Radius}` |
| Surfaces | {Cell(string.Join(", ", preview.Surfaces))} |
| Components | {Cell(string.Join(", ", preview.Components))} |

The preview is derived from the current high-level UI questionnaire and direction. Feature wireframes and visual designs inherit it without treating this representative page as a feature screen.
""";

    private static string RenderPreviewSvg(PreviewModel preview)
    {
        var c = preview.Colors;
        return $"""
<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="900" viewBox="0 0 1440 900" role="img" aria-labelledby="title description">
  <title id="title">High-level UI system preview</title><desc id="description">Application shell, typography, colors, form controls, table, status messages and dialog examples.</desc>
  <rect width="1440" height="900" fill="{Xml(c["background"])}"/>
  <rect x="48" y="36" width="1344" height="828" rx="{Radius(preview.Radius)}" fill="{Xml(c["surface"])}" stroke="#cbd5e1"/>
  <rect x="48" y="36" width="1344" height="76" rx="{Radius(preview.Radius)}" fill="{Xml(c["foreground"])}"/>
  <text x="82" y="83" fill="#ffffff" font-family="{Xml(preview.FontFamily)}" font-size="24" font-weight="700">Product workspace</text>
  <text x="1358" y="82" text-anchor="end" fill="#e2e8f0" font-family="{Xml(preview.FontFamily)}" font-size="16">Andrew ▾</text>
  <text x="88" y="176" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="34" font-weight="700">Clear decisions, visible outcomes</text>
  <text x="88" y="211" fill="#64748b" font-family="{Xml(preview.FontFamily)}" font-size="17">Representative shell, controls, data display and operational states</text>
  <rect x="88" y="250" width="396" height="52" rx="{Radius(preview.Radius)}" fill="#ffffff" stroke="#94a3b8"/><text x="108" y="283" fill="#64748b" font-family="{Xml(preview.FontFamily)}" font-size="16">Search or enter a value</text>
  <rect x="500" y="250" width="148" height="52" rx="{Radius(preview.Radius)}" fill="{Xml(c["accent"])}"/><text x="574" y="283" text-anchor="middle" fill="#ffffff" font-family="{Xml(preview.FontFamily)}" font-size="16" font-weight="600">Primary action</text>
  <rect x="664" y="250" width="148" height="52" rx="{Radius(preview.Radius)}" fill="#ffffff" stroke="{Xml(c["accent"])}"/><text x="738" y="283" text-anchor="middle" fill="{Xml(c["accent"])}" font-family="{Xml(preview.FontFamily)}" font-size="16" font-weight="600">Secondary</text>
  <rect x="88" y="338" width="816" height="294" rx="{Radius(preview.Radius)}" fill="#ffffff" stroke="#cbd5e1"/>
  <rect x="88" y="338" width="816" height="52" fill="#eef2f7"/><text x="112" y="370" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="15" font-weight="700">Name</text><text x="512" y="370" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="15" font-weight="700">Status</text><text x="770" y="370" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="15" font-weight="700">Updated</text>
  <g font-family="{Xml(preview.FontFamily)}" font-size="15" fill="{Xml(c["foreground"])}"><text x="112" y="435">Customer assessment</text><text x="512" y="435" fill="{Xml(c["success"])}">Ready</text><text x="770" y="435">Today</text><text x="112" y="501">Connected review</text><text x="512" y="501" fill="{Xml(c["warning"])}">Needs attention</text><text x="770" y="501">Yesterday</text><text x="112" y="567">Unavailable example</text><text x="512" y="567" fill="{Xml(c["error"])}">Unavailable</text><text x="770" y="567">Retry</text></g>
  <rect x="944" y="250" width="400" height="382" rx="{Radius(preview.Radius)}" fill="#ffffff" stroke="#cbd5e1"/><text x="976" y="298" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="21" font-weight="700">Review change</text><text x="976" y="334" fill="#64748b" font-family="{Xml(preview.FontFamily)}" font-size="15">Confirm a bounded, understandable action.</text><rect x="976" y="374" width="336" height="104" rx="{Radius(preview.Radius)}" fill="#eff6ff" stroke="#bfdbfe"/><text x="998" y="411" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="15" font-weight="700">Information</text><text x="998" y="444" fill="#475569" font-family="{Xml(preview.FontFamily)}" font-size="14">Status and recovery remain explicit.</text><rect x="976" y="528" width="146" height="48" rx="{Radius(preview.Radius)}" fill="{Xml(c["accent"])}"/><text x="1049" y="558" text-anchor="middle" fill="#fff" font-family="{Xml(preview.FontFamily)}" font-size="15" font-weight="600">Confirm</text><rect x="1138" y="528" width="146" height="48" rx="{Radius(preview.Radius)}" fill="#fff" stroke="#94a3b8"/><text x="1211" y="558" text-anchor="middle" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="15">Cancel</text>
  <text x="88" y="700" fill="{Xml(c["foreground"])}" font-family="{Xml(preview.FontFamily)}" font-size="20" font-weight="700">Typography and color tokens</text><g font-family="{Xml(preview.FontFamily)}"><text x="88" y="742" fill="{Xml(c["foreground"])}" font-size="24" font-weight="700">Heading</text><text x="238" y="742" fill="{Xml(c["foreground"])}" font-size="16">Body text remains readable and direct.</text></g><circle cx="92" cy="795" r="18" fill="{Xml(c["accent"])}"/><circle cx="142" cy="795" r="18" fill="{Xml(c["success"])}"/><circle cx="192" cy="795" r="18" fill="{Xml(c["warning"])}"/><circle cx="242" cy="795" r="18" fill="{Xml(c["error"])}"/><text x="286" y="801" fill="#64748b" font-family="{Xml(preview.FontFamily)}" font-size="14">{Xml(preview.Density)} density · {Xml(preview.Radius)} radius</text>
</svg>
""";
    }

    private IReadOnlyList<CisDefinitionDictionary> ReadDictionaries(State state)
    {
        return DictionaryKinds.Select(kind =>
        {
            var path = Path.Combine(state.DocumentationPath!, "references", kind + ".md");
            if (!File.Exists(path)) return new CisDefinitionDictionary(kind, Title(kind), Normalize(Path.GetRelativePath(state.AuthorityRepositoryPath!, path)), "Missing", false, 0);
            var content = File.ReadAllText(path);
            return new CisDefinitionDictionary(kind, ReadFrontMatter(content, "title") ?? Title(kind),
                Normalize(Path.GetRelativePath(state.AuthorityRepositoryPath!, path)), ReadFrontMatter(content, "status") ?? "Present",
                true, MarkdownEntryCount(content));
        }).ToArray();
    }

    private IReadOnlyList<CisDefinitionDiagram> ReadDiagrams(State state, SolutionDesignResult solution)
        => File.Exists(state.DiagramPath)
            ?
            [
                new("system-context", "System context", state.DiagramRelativePath!, "mermaid", DerivedStatus(state.DiagramPath!, DiagramSourceHash(state, solution))),
                new("component-topology", "Component topology", state.DiagramRelativePath!, "mermaid", DerivedStatus(state.DiagramPath!, DiagramSourceHash(state, solution))),
                new("integration-trust", "Integration and trust boundaries", state.DiagramRelativePath!, "mermaid", DerivedStatus(state.DiagramPath!, DiagramSourceHash(state, solution))),
                new("deployment-operations", "Deployment and operations", state.DiagramRelativePath!, "mermaid", DerivedStatus(state.DiagramPath!, DiagramSourceHash(state, solution))),
            ]
            : [];

    private static CisDefinitionPreview? ReadPreview(State state, UiDirectionQuestionnaireResult questions)
    {
        if (!File.Exists(state.PreviewPath) || !File.Exists(state.PreviewSvgPath)) return null;
        var preview = CreatePreview(state, questions);
        return new CisDefinitionPreview(state.PreviewRelativePath!, state.PreviewSvgRelativePath!,
            DerivedStatus(state.PreviewPath!, preview.SourceHash),
            preview.FontFamily, preview.Density, preview.Radius, preview.Colors, preview.Components, preview.Surfaces);
    }

    private void MergeDerivedCatalog(State state)
    {
        var entries = new List<CatalogArtifactEntry>();
        if (File.Exists(state.DiagramPath)) entries.Add(new($"{state.AuthorityRepositoryId}:architecture:high-level-diagrams", state.DiagramRelativePath!, "architecture-diagram-set", "review-required", "canonical"));
        if (File.Exists(state.DictionaryIndexPath)) entries.Add(new($"{state.AuthorityRepositoryId}:reference:dictionary-index", state.DictionaryIndexRelativePath!, "reference-index", "review-required", "canonical"));
        if (File.Exists(state.PreviewPath)) entries.Add(new($"{state.AuthorityRepositoryId}:design:ui-system-preview", state.PreviewRelativePath!, "ui-system-preview", "review-required", "canonical"));
        if (entries.Count == 0) return;
        var content = File.ReadAllText(state.CatalogPath!);
        var merged = _catalog.Merge(state.AuthorityRepositoryId!, content, entries);
        if (merged.Collisions.Count > 0) throw new InvalidOperationException(string.Join("; ", merged.Collisions));
        if (merged.Changed) WriteAtomic(state.CatalogPath!, merged.Content);
    }

    private static void UpdateDerivedCatalogStatus(State state, string status)
    {
        if (!File.Exists(state.CatalogPath)) return;
        var content = File.ReadAllText(state.CatalogPath);
        foreach (var id in new[]
                 {
                     $"{state.AuthorityRepositoryId}:architecture:high-level-diagrams",
                     $"{state.AuthorityRepositoryId}:reference:dictionary-index",
                     $"{state.AuthorityRepositoryId}:design:ui-system-preview",
                 })
            content = Regex.Replace(content, $@"(?ms)(^\s*-\s+id:\s*{Regex.Escape(id)}\s*$.*?^\s+status:\s*)[^\r\n]+", $"${{1}}{status}");
        WriteAtomic(state.CatalogPath, content);
    }

    private void ActivateDerived(string path, string reviewer, string reason)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"Derived definition artifact is missing: {path}");
        var now = _clock().ToUniversalTime();
        var content = File.ReadAllText(path);
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNested(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNested(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNested(content, "approval_reason", JsonSerializer.Serialize(reason));
        content = ReplaceNested(content, "approved_content_hash", "null");
        content = ReplaceNested(content, "approved_content_hash", JsonSerializer.Serialize(Digest(content)));
        WriteAtomic(path, content);
    }

    private void EnsureSession(State state, string page)
    {
        var current = ReadSession(state) ?? new WizardSession(1,
            "DEF-" + _clock().ToUniversalTime().ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            page, _clock().ToUniversalTime().ToString("O"), null, true);
        WriteAtomic(state.SessionPath!, JsonSerializer.Serialize(current with { CurrentPage = page, Active = true, ActivatedAtUtc = null }, JsonOptions));
    }

    private State Resolve(string workspacePath)
    {
        var resolution = _workspaces.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
            return new(null, null, null, null, null, null, null, null, null, null, null, null, null, resolution.Errors);
        var authority = resolution.Workspace.AuthorityRepository;
        if (authority is null)
            return new(resolution.Workspace.WorkspacePath, null, null, null, null, null, null, null, null, null, null, null, null, ["Workspace has no authority repository."]);
        var context = _repositories.Resolve(authority.RepositoryPath);
        if (!context.IsSuccess || context.Context is null)
            return new(resolution.Workspace.WorkspacePath, authority.Id, authority.RepositoryPath, null, null, null, null, null, null, null, null, null, null, context.Errors);
        var docs = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar));
        return new(resolution.Workspace.WorkspacePath, authority.Id, authority.RepositoryPath, docs, context.Context.CatalogPath,
            Path.Combine(authority.RepositoryPath, SessionRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(docs, "architecture", "high-level-architecture-diagrams.md"),
            Normalize(Path.Combine(authority.DocumentationRoot, "architecture", "high-level-architecture-diagrams.md")),
            Path.Combine(docs, "references", "dictionary-index.md"),
            Normalize(Path.Combine(authority.DocumentationRoot, "references", "dictionary-index.md")),
            Path.Combine(docs, "design", "ui-system-preview.md"), Normalize(Path.Combine(authority.DocumentationRoot, "design", "ui-system-preview.md")),
            Path.Combine(docs, "design", "ui-system-preview.svg"), []);
    }

    private static WizardSession? ReadSession(State state)
    {
        if (state.SessionPath is null || !File.Exists(state.SessionPath)) return null;
        try { return JsonSerializer.Deserialize<WizardSession>(File.ReadAllText(state.SessionPath), JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<string> CanonicalPaths(State state)
    {
        var docs = state.DocumentationPath!;
        return
        [
            Path.Combine(docs, "specs", "business-requirements.md"),
            Path.Combine(docs, "specs", "technical-intent-spec.md"),
            Path.Combine(docs, "architecture", "overall-solution-design.md"),
            Path.Combine(docs, "references", "component-sheet.md"),
            Path.Combine(docs, "design", "ui-direction.md"),
            Path.Combine(docs, "plans", "high-level-backlog.md"),
            state.DiagramPath!, state.DictionaryIndexPath!, state.PreviewPath!, state.CatalogPath!, state.SessionPath!,
        ];
    }

    private static void RequireApproved(string? effectiveStatus, string label)
    {
        if (!string.Equals(effectiveStatus, "Active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} did not become Active.");
    }

    private static bool Accepted(bool? valid, bool? current, string? status)
        => CisDefinitionDraftScope.Accepts(valid == true, current == true, status);

    private static string Effective(string? value) => string.IsNullOrWhiteSpace(value) ? "Not started" : value;
    private static string? NormalizePage(string page)
    {
        var value = Regex.Replace(page.Trim().ToLowerInvariant(), "[^a-z]", string.Empty);
        return value switch
        {
            "foundation" or "projectfoundation" => "foundation",
            "business" or "businessdefinition" => "business",
            "technical" or "technicaldirection" => "technical",
            "architecture" or "solutionarchitecture" => "architecture",
            "contracts" or "dictionaries" or "contractsanddictionaries" => "contracts",
            "experience" or "uidirection" or "preview" => "experience",
            "delivery" or "deliverymap" or "backlog" => "delivery",
            "review" or "activate" => "review",
            _ => null,
        };
    }

    private static CisDefinitionPage Page(string id, int ordinal, string title, string status, bool complete, bool current,
        string? primary, IEnumerable<string?> paths, IEnumerable<string> issues)
        => new(id, ordinal, title, status, complete, current, primary,
            paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).Distinct().ToArray(),
            issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).Distinct().ToArray());

    private static IEnumerable<string> Existing(params string?[] values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);

    private static int MarkdownEntryCount(string content)
    {
        var rows = content.Split('\n').Count(line => line.TrimStart().StartsWith('|') && !Regex.IsMatch(line, @"^\s*\|?\s*:?-{3,}"));
        return Math.Max(0, rows - 1);
    }

    private static IReadOnlyList<string> SplitChoices(string answer, IReadOnlyList<string> defaults)
    {
        var found = defaults.Where(item => answer.Contains(item, StringComparison.OrdinalIgnoreCase)).ToArray();
        return found.Length > 0 ? found : defaults;
    }

    private static string Title(string value) => string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private static string Node(string value) => "n" + Regex.Replace(value, "[^A-Za-z0-9]", string.Empty);
    private static string ExternalNode(string value) => "x" + Digest(value)[7..15];
    private static string Short(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
    private static string Mermaid(string value) => value.Replace("[", "(", StringComparison.Ordinal).Replace("]", ")", StringComparison.Ordinal)
        .Replace("\"", "'", StringComparison.Ordinal).Replace("|", "/", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static int Radius(string value) => int.TryParse(Regex.Match(value, @"\d+").Value, out var parsed) ? parsed : 6;
    private static string Normalize(string value) => value.Replace('\\', '/');
    private static string RelativeFrom(string source, string target) => Normalize(Path.GetRelativePath(Path.GetDirectoryName(source)!, target));
    private static string Digest(string content) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
    private static string DictionarySourceHash(IEnumerable<CisDefinitionDictionary> dictionaries)
        => Digest(string.Join('|', dictionaries.Select(item => $"{item.Kind}:{item.Status}:{item.EntryCount}")));
    private static string DiagramSourceHash(State state, SolutionDesignResult solution)
    {
        var design = ResolveDocument(state, solution.DesignPath);
        var sheet = ResolveDocument(state, solution.ComponentSheetPath);
        return Digest(string.Join('|', "diagram-renderer-v2", solution.TechnicalIntentVersion,
            File.Exists(design) ? File.ReadAllText(design) : string.Empty,
            File.Exists(sheet) ? File.ReadAllText(sheet) : string.Empty));
    }
    private static string DerivedStatus(string path, string expectedSourceHash)
    {
        var content = File.ReadAllText(path);
        return string.Equals(ReadNestedValue(content, "source_hash"), expectedSourceHash, StringComparison.Ordinal)
            ? ReadFrontMatter(content, "status") ?? "Ready" : "Stale";
    }
    private static string ResolveDocument(State state, string? relativePath)
        => Path.Combine(state.AuthorityRepositoryPath!, (relativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
    private static string? ComponentId(IReadOnlyList<SolutionComponent> components, string name)
        => components.FirstOrDefault(component => string.Equals(component.Name, name, StringComparison.OrdinalIgnoreCase)) is { } component
            ? Node(component.Id) : null;
    private static IReadOnlyList<DiagramInteraction> ReadInteractions(State state, SolutionDesignResult solution)
    {
        var path = ResolveDocument(state, solution.ComponentSheetPath);
        if (!File.Exists(path)) return [];
        var rows = new List<DiagramInteraction>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith("| `TI-INT-", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray();
            if (cells.Length >= 5) rows.Add(new(cells[1], cells[2], cells[3], cells[4]));
        }
        return rows;
    }
    private static string? ReadFrontMatter(string content, string key) => Regex.Match(content, $@"(?m)^{Regex.Escape(key)}:\s*[""']?(?<v>[^""'\r\n]+)").Groups["v"].Value.Trim() is { Length: > 0 } value ? value : null;
    private static string ReplaceFrontMatter(string content, string key, string value) => Regex.Replace(content, $@"(?m)^{Regex.Escape(key)}:\s*[^\r\n]*", $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string ReplaceNested(string content, string key, string value) => Regex.Replace(content, $@"(?m)^(\s+{Regex.Escape(key)}:)\s*[^\r\n]*", $"${{1}} {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static void WriteDerived(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (string.Equals(ReadFrontMatter(existing, "status"), "Active", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ReadNestedValue(existing, "source_hash"), ReadNestedValue(content, "source_hash"), StringComparison.Ordinal))
                return;
            if (string.Equals(existing.Replace("\r\n", "\n", StringComparison.Ordinal), content.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal))
                return;
        }
        WriteAtomic(path, content);
    }

    private static void WriteBytesAtomic(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, true);
    }

    private static string? ReadNestedValue(string content, string key)
        => Regex.Match(content, $@"(?m)^\s+{Regex.Escape(key)}:\s*[""']?(?<v>[^""'\r\n]+)").Groups["v"].Value.Trim() is { Length: > 0 } value ? value : null;

    private static CisDefinitionWizardResult Error(State state, IReadOnlyList<string> errors)
        => new("invalid", state.WorkspacePath, state.AuthorityRepositoryId, null, null, false, false,
            [], [], [], null, [], errors, false);

    private sealed record WizardSession(int SchemaVersion, string SessionId, string CurrentPage,
        string StartedAtUtc, string? ActivatedAtUtc, bool Active);
    private sealed record PreviewModel(string FontFamily, string Density, string Radius,
        IReadOnlyDictionary<string, string> Colors, IReadOnlyList<string> Components,
        IReadOnlyList<string> Surfaces, string SourceHash);
    private sealed record DiagramInteraction(string Source, string Trigger, string Target, string Contract);
    private sealed record State(string? WorkspacePath, string? AuthorityRepositoryId, string? AuthorityRepositoryPath,
        string? DocumentationPath, string? CatalogPath, string? SessionPath,
        string? DiagramPath, string? DiagramRelativePath, string? DictionaryIndexPath, string? DictionaryIndexRelativePath,
        string? PreviewPath, string? PreviewRelativePath, string? PreviewSvgPath, IReadOnlyList<string> Errors)
    {
        public string? PreviewSvgRelativePath => AuthorityRepositoryPath is null || PreviewSvgPath is null
            ? null : Normalize(Path.GetRelativePath(AuthorityRepositoryPath, PreviewSvgPath));
    }
}
