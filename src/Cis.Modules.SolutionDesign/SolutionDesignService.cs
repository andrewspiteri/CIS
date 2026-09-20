using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.SolutionDesign;

public sealed partial class SolutionDesignService : IChangeReadinessCheck, ICisSolutionDesignDrafts
{
    private const string ManagedStart = "<!-- cis:solution-design-managed:start -->";
    private const string ManagedEnd = "<!-- cis:solution-design-managed:end -->";
    private const string ComponentsStart = "<!-- cis:component-sheet-managed:start -->";
    private const string ComponentsEnd = "<!-- cis:component-sheet-managed:end -->";

    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly ISolutionDesignTechnicalIntentSource _technicalIntent;
    private readonly ICisWorkspaceRegistry _workspaceRegistry;

    public SolutionDesignService(
        ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver,
        DocumentationCatalogMerger catalogMerger,
        ISolutionDesignTechnicalIntentSource technicalIntent,
        Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _catalogMerger = catalogMerger;
        _technicalIntent = technicalIntent;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public SolutionDesignResult Initialize(string workspacePath)
        => InitializeCore(workspacePath, false);

    private SolutionDesignResult InitializeCore(string workspacePath, bool existingDraft)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        var blocking = existingDraft ? state.ReadinessErrors.Where(error => !error.StartsWith("An Active, current technical intent", StringComparison.Ordinal)).ToArray() : state.ReadinessErrors;
        if (blocking.Count > 0) return Error("blocked", state, blocking);

        var designStableId = $"{state.Authority!.Id}:architecture:overall-solution-design";
        var componentsStableId = $"{state.Authority.Id}:reference:component-sheet";
        var designRelative = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.DesignPath!));
        var componentsRelative = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.ComponentSheetPath!));
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(state.Authority.Id, catalog,
        [
            new CatalogArtifactEntry(designStableId, designRelative, "overall-solution-design", "review-required", "canonical"),
            new CatalogArtifactEntry(componentsStableId, componentsRelative, "component-sheet", "review-required", "canonical"),
        ]);
        if (merge.Collisions.Count > 0) return Error("collision", state, merge.Collisions);

        var generatedDesign = RenderDesign(state, designStableId);
        var generatedComponents = RenderComponentSheet(state, componentsStableId);
        var nextDesign = ReconcileExisting(state.DesignPath!, generatedDesign, designStableId, ManagedStart, ManagedEnd, existingDraft);
        var nextComponents = ReconcileExisting(state.ComponentSheetPath!, generatedComponents, componentsStableId, ComponentsStart, ComponentsEnd, existingDraft);
        if (nextDesign is null || nextComponents is null)
            return Error("collision", state, ["A canonical solution-design path contains a document with a different stable identity."]);

        var approvedDigest = BundleDigest(nextDesign, nextComponents);
        var activeBundle = ReadFrontMatter(nextDesign, "status") == "Active"
            || ReadFrontMatter(nextComponents, "status") == "Active";
        var intactApproval = ReadFrontMatter(nextDesign, "status") == "Active"
            && ReadFrontMatter(nextComponents, "status") == "Active"
            && ReadNested(nextDesign, "approved_bundle_hash") == approvedDigest
            && ReadNested(nextComponents, "approved_bundle_hash") == approvedDigest;
        if (activeBundle && !intactApproval)
        {
            nextDesign = ResetApproval(nextDesign);
            nextComponents = ResetApproval(nextComponents);
        }

        var designChanged = !File.Exists(state.DesignPath!) || !Equivalent(File.ReadAllText(state.DesignPath!), nextDesign);
        var componentsChanged = !File.Exists(state.ComponentSheetPath!) || !Equivalent(File.ReadAllText(state.ComponentSheetPath!), nextComponents);
        var lifecycle = ReadFrontMatter(nextDesign, "status") == "Active"
            && ReadFrontMatter(nextComponents, "status") == "Active"
            ? "active"
            : "review-required";
        var nextCatalog = UpdateCatalogStatus(UpdateCatalogStatus(merge.Content, designStableId, lifecycle), componentsStableId, lifecycle);
        var catalogChanged = !Equivalent(catalog, nextCatalog);
        if (!designChanged && !componentsChanged && !catalogChanged)
            return ValidateInternal(workspacePath, "unchanged", false);

        Directory.CreateDirectory(Path.GetDirectoryName(state.DesignPath!)!);
        Directory.CreateDirectory(Path.GetDirectoryName(state.ComponentSheetPath!)!);
        if (designChanged) Write(state.DesignPath!, nextDesign);
        if (componentsChanged) Write(state.ComponentSheetPath!, nextComponents);
        if (catalogChanged) Write(state.Context.CatalogPath, nextCatalog);
        return ValidateInternal(workspacePath, "initialized", true);
    }

    public SolutionDesignResult Validate(string workspacePath) => ValidateInternal(workspacePath, "validated", false);

    public SolutionDesignResult Status(string workspacePath) => ValidateInternal(workspacePath, "status", false);

    public SolutionDesignResult Approve(string workspacePath, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return new SolutionDesignResult("invalid", workspacePath, null, null, null, null, [], null,
                ["Reviewer and approval reason are required."], false);
        var assessed = Validate(workspacePath);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true, Current: true })
            return assessed with { Status = "blocked" };

        var state = Resolve(workspacePath);
        var design = File.ReadAllText(state.DesignPath!);
        var components = File.ReadAllText(state.ComponentSheetPath!);
        if (ReadFrontMatter(design, "status") == "Active"
            && ReadFrontMatter(components, "status") == "Active"
            && ReadNested(design, "approved_by") == reviewer.Trim()
            && ReadNested(design, "approval_reason") == reason.Trim()
            && assessed.Validation.EffectiveStatus == "Active")
            return assessed with { Status = "unchanged" };

        var now = _clock().ToUniversalTime();
        design = ApplyApproval(design, reviewer, reason, now, null);
        components = ApplyApproval(components, reviewer, reason, now, null);
        var bundleHash = BundleDigest(design, components);
        design = ReplaceNested(design, "approved_bundle_hash", JsonSerializer.Serialize(bundleHash));
        components = ReplaceNested(components, "approved_bundle_hash", JsonSerializer.Serialize(bundleHash));
        Write(state.DesignPath!, design);
        Write(state.ComponentSheetPath!, components);

        var designStableId = $"{state.Authority!.Id}:architecture:overall-solution-design";
        var componentsStableId = $"{state.Authority.Id}:reference:component-sheet";
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        Write(state.Context.CatalogPath,
            UpdateCatalogStatus(UpdateCatalogStatus(catalog, designStableId, "active"), componentsStableId, "active"));
        return ValidateInternal(workspacePath, "approved", true);
    }

    public ChangeReadinessResult Evaluate(string repositoryPath)
    {
        if (!File.Exists(Path.Combine(Path.GetFullPath(repositoryPath), ".cis", "workspace.yml")))
            return new ChangeReadinessResult("solution-design", false, true, []);
        var result = Status(repositoryPath);
        var ready = result.Validation is { } validation
            && CisDefinitionDraftScope.Accepts(validation.Valid, validation.Current, validation.EffectiveStatus);
        var errors = ready ? [] : result.Errors.Concat(result.Validation?.Errors ?? [])
            .Append("An Active, current overall solution design and component sheet are required. Run `cis solution-design status`.")
            .Distinct(StringComparer.Ordinal).ToArray();
        return new ChangeReadinessResult("solution-design", true, ready, errors);
    }

    private SolutionDesignResult ValidateInternal(string workspacePath, string operation, bool applied, bool validateDiagramDisplay = true)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        var designRelative = Normalize(Path.GetRelativePath(state.Authority!.RepositoryPath, state.DesignPath!));
        var componentsRelative = Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.ComponentSheetPath!));
        if (!File.Exists(state.DesignPath!) || !File.Exists(state.ComponentSheetPath!))
            return new SolutionDesignResult("missing", state.Workspace!.WorkspacePath, state.Authority.Id,
                designRelative, componentsRelative, state.TechnicalIntentVersion, state.Components,
                new SolutionDesignValidation(false, false, "Missing",
                    File.Exists(state.DesignPath) ? ReadFrontMatter(File.ReadAllText(state.DesignPath), "status") ?? "Unknown" : "Missing",
                    File.Exists(state.ComponentSheetPath) ? ReadFrontMatter(File.ReadAllText(state.ComponentSheetPath), "status") ?? "Unknown" : "Missing",
                    ["Both canonical artifacts are required. Run `cis solution-design init`."], state.ReadinessErrors), [], applied);

        var design = File.ReadAllText(state.DesignPath);
        var components = File.ReadAllText(state.ComponentSheetPath);
        var errors = new List<string>();
        var warnings = new List<string>(state.ReadinessErrors);
        ValidateIdentity(design, $"{state.Authority.Id}:architecture:overall-solution-design", "overall-solution-design", "Overall solution design", errors);
        ValidateIdentity(components, $"{state.Authority.Id}:reference:component-sheet", "component-sheet", "Component sheet", errors);
        if (!design.Contains(ManagedStart, StringComparison.Ordinal) || !design.Contains(ManagedEnd, StringComparison.Ordinal))
            errors.Add("Overall solution design managed markers are missing.");
        if (!components.Contains(ComponentsStart, StringComparison.Ordinal) || !components.Contains(ComponentsEnd, StringComparison.Ordinal))
            errors.Add("Component-sheet managed markers are missing.");

        foreach (var heading in new[] { "Architecture drivers", "System context and boundaries", "Logical component topology", "Data ownership and consistency", "Integration architecture", "Security and trust boundaries", "Deployment, operations, and recovery", "Quality and verification architecture", "UI design handoff", "Traceability" })
            if (string.IsNullOrWhiteSpace(ExtractSection(design, heading))) errors.Add($"Overall solution design section is missing or empty: {heading}");
        foreach (var heading in new[] { "Component catalogue", "Component responsibility profiles", "Component interaction catalogue", "Ownership rules", "Component-specific notes and accepted exceptions" })
            if (string.IsNullOrWhiteSpace(ExtractSection(components, heading))) errors.Add($"Component sheet section is missing or empty: {heading}");
        if (Placeholder(design) || Placeholder(components)) errors.Add("Solution-design artifacts contain TODO, TBD, or incomplete placeholders.");
        if (design.Contains(InferredMarker, StringComparison.Ordinal) || design.Contains(ArchitectureDiagramModel.Marker, StringComparison.Ordinal))
        {
            try
            {
                var model = ArchitectureDiagramModel.ReadRequired(design);
                if (validateDiagramDisplay && model.SchemaVersion == 2 && !Equivalent(ArchitectureDiagramPresentation.Embed(design, model.Render()), design))
                    errors.Add("C4 diagram display is missing or stale. Run solution-design diagrams on the review-only draft.");
            }
            catch (InvalidDataException exception) { errors.Add(exception.Message); }
        }

        var parsedComponents = ParseComponents(components);
        if (parsedComponents.Count == 0) errors.Add("Component sheet contains no structured `TI-MOD-*` component rows.");
        foreach (var duplicate in parsedComponents.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            errors.Add($"Component identity occurs more than once: {duplicate.Key}");
        foreach (var component in parsedComponents.Where(component => !design.Contains($"`{component.Id}`", StringComparison.Ordinal)))
            errors.Add($"Overall solution design does not reference component `{component.Id}`.");

        var sourceCurrent = ReadNested(design, "technical_intent_hash") == state.TechnicalIntentVersion
            && ReadNested(components, "technical_intent_hash") == state.TechnicalIntentVersion;
        var reconciliationRequired = design.Contains(ReconciliationPendingMarker, StringComparison.Ordinal)
            || components.Contains(ReconciliationPendingMarker, StringComparison.Ordinal)
            || !sourceCurrent && (design.Contains(InferredMarker, StringComparison.Ordinal) || components.Contains(InferredMarker, StringComparison.Ordinal));
        var current = state.ReadinessErrors.Count == 0 && sourceCurrent && !reconciliationRequired;
        if (!current) warnings.Add("The solution-design bundle differs from the current Active technical intent.");
        if (reconciliationRequired) warnings.Add("The inferred architecture needs reconciliation with the updated technical direction. Use Reconcile architecture with technical direction in the wizard, or run cis agent author solution-design. Prepare only refreshes diagrams after reconciliation.");
        var designStatus = ReadFrontMatter(design, "status") ?? "Unknown";
        var componentStatus = ReadFrontMatter(components, "status") ?? "Unknown";
        if (!designStatus.Equals(componentStatus, StringComparison.OrdinalIgnoreCase))
            errors.Add("Overall solution design and component sheet must share one lifecycle status.");
        if (designStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            var expected = BundleDigest(design, components);
            var designHash = ReadNested(design, "approved_bundle_hash");
            var componentHash = ReadNested(components, "approved_bundle_hash");
            if (string.IsNullOrWhiteSpace(designHash) || designHash == "null" || designHash != expected || componentHash != expected)
            {
                current = false;
                warnings.Add("The approved solution-design bundle changed after approval.");
            }
            if (string.IsNullOrWhiteSpace(ReadNested(design, "approved_by"))
                || ReadNested(design, "approved_by") == "null"
                || ReadNested(design, "approved_by") != ReadNested(components, "approved_by")
                || ReadNested(design, "approval_reason") != ReadNested(components, "approval_reason"))
                errors.Add("Active solution-design artifacts require matching approval authority and rationale.");
        }

        var valid = errors.Count == 0;
        var effective = designStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? valid && current ? "Active" : "Stale"
            : valid && current ? "Ready for Approval" : "Review Required";
        return new SolutionDesignResult(operation, state.Workspace!.WorkspacePath, state.Authority.Id,
            designRelative, componentsRelative, state.TechnicalIntentVersion, parsedComponents,
            new SolutionDesignValidation(valid, current, effective, designStatus, componentStatus,
                errors.Distinct(StringComparer.Ordinal).Order().ToArray(), warnings.Distinct(StringComparer.Ordinal).Order().ToArray())
            { InferenceReconciliationRequired = reconciliationRequired }, [], applied);
    }

    private State Resolve(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
            return new State(null, null, null, null, null, null, [], [], resolution.Errors);
        var authority = resolution.Workspace.AuthorityRepository;
        if (authority is null) return new State(resolution.Workspace, null, null, null, null, null, [], [], ["Workspace has no authority repository."]);
        var contextResolution = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!contextResolution.IsSuccess || contextResolution.Context is null)
            return new State(resolution.Workspace, authority, null, null, null, null, [], [], contextResolution.Errors);
        var docs = authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar);
        var technicalPath = Path.Combine(authority.RepositoryPath, docs, "specs", "technical-intent-spec.md");
        var designPath = Path.Combine(authority.RepositoryPath, docs, "architecture", "overall-solution-design.md");
        var componentPath = Path.Combine(authority.RepositoryPath, docs, "references", "component-sheet.md");
        var technicalStatus = _technicalIntent.Status(resolution.Workspace.WorkspacePath);
        var readiness = new List<string>();
        if (technicalStatus.Validation is not { } technicalValidation
            || !CisDefinitionDraftScope.Accepts(technicalValidation.Valid, technicalValidation.Current, technicalValidation.EffectiveStatus))
            readiness.Add("An Active, current technical intent is required. Run `cis technical-intent status`.");
        if (!File.Exists(technicalPath))
            readiness.Add("Canonical technical intent is missing.");
        var technical = File.Exists(technicalPath) ? File.ReadAllText(technicalPath) : string.Empty;
        var version = technical.Length == 0 ? null : TechnicalIntentDigest(technical);
        var components = technical.Length == 0 ? [] : ParseComponents(ExtractSection(technical, "Product module architecture"));
        if (technical.Length > 0 && components.Count == 0)
            readiness.Add("Technical intent contains no structured product-module ownership rows.");
        return new State(resolution.Workspace, authority, contextResolution.Context, technicalPath,
            designPath, componentPath, components, readiness.Distinct(StringComparer.Ordinal).ToArray(), [], technical, version);
    }

    private static string RenderDesign(State state, string stableId)
    {
        var componentRows = string.Join('\n', state.Components.Select(component =>
            $"| `{Cell(component.Id)}` — {Cell(component.Name)} | {Cell(component.Classification)} | {Cell(component.Responsibility)} | {Cell(component.Owns)} | {Codes(component.RequirementIds)} |"));
        var managed = $"""
## Architecture drivers

{SectionOrFallback(state.TechnicalIntentContent, "Design goals and principles", "The approved technical intent defines the architectural drivers for this solution.")}

## System context and boundaries

{SectionOrFallback(state.TechnicalIntentContent, "Runtime architecture and trust boundaries", "The solution boundary follows the approved technical intent.")}

## Logical component topology

The following components are the concrete architectural projection of the approved `TI-MOD-*` ownership boundaries. A component is not automatically an independently deployed service.

{SectionPreamble(ExtractSection(state.TechnicalIntentContent, "Product module architecture"))}

| Component | Classification | Responsibility | Owns | BRD authority |
| --- | --- | --- | --- | --- |
{componentRows}

Dependency direction is inward toward the owning application or domain contract. Cross-component state access is prohibited; interactions use the integration architecture below.

## Data ownership and consistency

{SectionOrFallback(state.TechnicalIntentContent, "Application model and invariants", "Each component owns its authoritative state and consistency boundary.")}

## Integration architecture

{SectionOrFallback(state.TechnicalIntentContent, "Integration point catalog", "Every cross-boundary handoff requires an explicit owned contract.")}

## Security and trust boundaries

{SectionOrFallback(state.TechnicalIntentContent, "Security and privacy intent", "Authentication and authorization are enforced at each trusted application boundary.")}

## Deployment, operations, and recovery

{SectionOrFallback(state.TechnicalIntentContent, "Operations, migration, and recovery intent", "Deployment and recovery follow the approved technical intent.")}

## Quality and verification architecture

{SectionOrFallback(state.TechnicalIntentContent, "Quality attributes and verification direction", "Architecture, contract, security, integration, and recovery rules require deterministic verification.")}

## UI design handoff

High-level UI direction is the next distinct stage. It consumes the BRD actors and journeys, the approved experience-surface choices, component capabilities, permissions, contracts, and failure states to establish shared product character, shell, visual language, reusable components, responsive behavior, and accessibility. Detailed screens remain feature-wireframe work. Neither stage may change component ownership, business scope, or trust boundaries silently.

{SectionOrFallback(state.TechnicalIntentContent, "Component and interaction map", "No additional experience-surface projection was recorded.")}

## Traceability

- Business authority: canonical Active BRD through the approved technical-intent baseline.
- Technical authority: `{Normalize(Path.GetRelativePath(state.Authority!.RepositoryPath, state.TechnicalIntentPath!))}` at `{state.TechnicalIntentVersion}`.
- Component authority: `{Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.ComponentSheetPath!))}`.
- Durable alternatives or exceptions require an ADR before implementation changes the approved direction.
""";
        return Header($"{state.Authority!.Id} Overall Solution Design", "overall-solution-design", stableId,
            state.TechnicalIntentVersion!, ManagedStart, managed, ManagedEnd,
            "## Design decisions and accepted exceptions\n\nNo additional exceptions are accepted. Durable changes require an ADR and renewed bundle validation.");
    }

    private static string RenderComponentSheet(State state, string stableId)
    {
        var rows = string.Join('\n', state.Components.Select(component =>
            $"| `{Cell(component.Id)}` | {Cell(component.Name)} | {Cell(component.Classification)} | {Cell(component.Responsibility)} | {Cell(component.Owns)} | {Cell(component.Excludes)} | {Codes(component.RequirementIds)} |"));
        var managed = $"""
## Component catalogue

| Component ID | Name | Classification | Responsibility | Owns | Must not own | BRD authority |
| --- | --- | --- | --- | --- | --- | --- |
{rows}

## Component responsibility profiles

{SubsectionOrFallback(state.TechnicalIntentContent, "Product module architecture", "Module responsibility profiles", "The approved technical intent does not provide additional component profiles.")}

## Component interaction catalogue

{SectionOrFallback(state.TechnicalIntentContent, "Integration point catalog", "No cross-component integration points are currently declared.")}

## Ownership rules

- Each business rule, durable record, public contract, and state transition has one owning component.
- A component consumes another component only through an explicit `TI-INT-*` integration; direct access to another component's store or internal types is prohibited.
- Deployment may combine components, but it does not merge their ownership or permit cyclic dependencies.
- Feature work may split or merge components only with requirement traceability, ownership migration, affected integration updates, and an accepted architecture decision.
- Shared technical capabilities remain independent of product-component internals.
""";
        return Header($"{state.Authority!.Id} Component Sheet", "component-sheet", stableId,
            state.TechnicalIntentVersion!, ComponentsStart, managed, ComponentsEnd,
            "## Component-specific notes and accepted exceptions\n\nNo component-specific exceptions are accepted. Record future exceptions through an ADR and link them here.");
    }

    private static string Header(string title, string type, string stableId, string technicalHash,
        string markerStart, string managed, string markerEnd, string tail) => $"""
---
title: "{title}"
type: {type}
status: Review Required
scope: Workspace
owner: Product owner and architecture maintainers
last_reviewed: null
review_cadence: on approved business, technical, component, or integration change
cis:
  stable_id: {stableId}
  solution_design_schema: 1
  technical_intent_hash: {technicalHash}
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_bundle_hash: null
---

# {title}

This document is one half of the governed overall solution-design bundle. The overall design and component sheet share one source baseline, lifecycle, and approval.

{markerStart}
{managed.Trim()}
{markerEnd}

{tail}
""";

    private static string? ReconcileExisting(string path, string generated, string stableId, string start, string end, bool existingDraft = false)
    {
        if (!File.Exists(path)) return generated;
        var existing = File.ReadAllText(path);
        if (!existing.Contains($"stable_id: {stableId}", StringComparison.Ordinal)) return null;
        // Inference owns the narrative. Ordinary preparation must preserve it and expose source drift.
        if (existing.Contains(InferredMarker, StringComparison.Ordinal))
            return existingDraft && ReadNested(existing, "technical_intent_hash") != ReadNested(generated, "technical_intent_hash")
                ? ResetApproval(ReplaceNested(existing, "technical_intent_hash", ReadNested(generated, "technical_intent_hash")!))
                    + (existing.Contains(ReconciliationPendingMarker, StringComparison.Ordinal) ? "" : "\n" + ReconciliationPendingMarker + "\n")
                : existing;
        var next = ReplaceBlock(existing, start, end, ReadBlock(generated, start, end));
        next = ReplaceNested(next, "technical_intent_hash", ReadNested(generated, "technical_intent_hash")!);
        if (!Equivalent(existing, next)) next = ResetApproval(next);
        return next;
    }

    private static void ValidateIdentity(string content, string stableId, string type, string label, ICollection<string> errors)
    {
        if (ReadFrontMatter(content, "type") != type) errors.Add($"{label} must declare `type: {type}`.");
        if (ReadNested(content, "stable_id") != stableId) errors.Add($"{label} stable identity must be `{stableId}`.");
        if (ReadNested(content, "solution_design_schema") != "1") errors.Add($"{label} schema metadata is missing or unsupported.");
    }

    private static IReadOnlyList<SolutionComponent> ParseComponents(string content)
    {
        var components = new List<SolutionComponent>();
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
            if (cells.Length < 6 || cells[0].Contains("Component", StringComparison.OrdinalIgnoreCase) || cells.All(cell => cell.All(character => character is '-' or ':' or ' '))) continue;
            var idMatch = Regex.Match(cells[0], @"\bTI-MOD-[A-Z0-9-]+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!idMatch.Success) continue;
            var id = idMatch.Value;
            var name = cells[0].Replace($"`{id}`", string.Empty, StringComparison.Ordinal).Trim(' ', '—', '-', '`');
            var offset = cells.Length >= 7 ? 1 : 0;
            if (offset == 1) name = cells[1].Trim(' ', '`');
            var classification = cells[1 + offset].Trim(' ', '`');
            var responsibility = cells[2 + offset].Trim(' ', '`');
            var owns = cells[3 + offset].Trim(' ', '`');
            var excludes = cells[4 + offset].Trim(' ', '`');
            var requirements = Regex.Matches(cells[5 + offset], @"\b(?:BRD|BR)-[A-Z]+-[0-9]+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            components.Add(new SolutionComponent(id, name.Length == 0 ? id : name, classification, responsibility, owns, excludes, requirements));
        }
        // Existing-system technical narratives retain stable IDs in responsibility profiles rather
        // than forcing their implementation observations into the original six-column starter.
        foreach (Match profile in Regex.Matches(content, @"(?ms)^####[ \t]+(?<id>TI-MOD-[A-Z0-9-]+)[ \t]*(?:—|-|:)?[ \t]*(?<name>[^\r\n]*)\r?\n(?<body>.*?)(?=^#{1,4} |\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var id = profile.Groups["id"].Value;
            if (components.Any(item => item.Id == id)) continue;
            var body = profile.Groups["body"].Value;
            string Field(string label, string fallback)
            {
                var field = Regex.Match(body, $@"(?s)\*\*{Regex.Escape(label)}:\*\*\s*(?<value>.*?)(?=\r?\n\s*\r?\n|\z)",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                return field.Success ? field.Groups["value"].Value.Trim() : fallback;
            }
            var name = profile.Groups["name"].Value.Trim();
            var requirements = Regex.Matches(body, @"\b(?:BRD|BR)-[A-Z]+-[0-9]+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
            components.Add(new(id, name.Length == 0 ? id : name, "Logical module",
                Field("Purpose", "Responsibility requires review against implementation."),
                Field("Owns", "Ownership requires review. Observed data/state: " + Field("Data and state", "Not established by the current technical profile.")),
                Field("Excludes", "Exclusions require review against the other component boundaries."), requirements));
        }
        return components;
    }

    private static string SectionOrFallback(string content, string heading, string fallback)
    {
        var value = ExtractSection(content, heading).Trim();
        return value.Length == 0 ? fallback : value;
    }

    private static string SubsectionOrFallback(string content, string section, string heading, string fallback)
    {
        var value = ExtractSubsection(ExtractSection(content, section), heading).Trim();
        return value.Length == 0 ? fallback : value;
    }

    private static string ExtractSubsection(string content, string heading)
    {
        var match = Regex.Match(content, $@"(?ms)^### {Regex.Escape(heading)}\s*$\n(?<body>.*?)(?=^## |^### |\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
    }

    private static string SectionPreamble(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "The approved technical intent defines the component topology.";
        var index = content.IndexOf("\n### ", StringComparison.Ordinal);
        return (index < 0 ? content : content[..index]).Trim();
    }

    private static string ExtractSection(string content, string heading)
    {
        var match = Regex.Match(content, $@"(?ms)^## {Regex.Escape(heading)}\s*$\n(?<body>.*?)(?=^## |\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
    }

    private static string ReadBlock(string content, string start, string end)
    {
        var from = content.IndexOf(start, StringComparison.Ordinal);
        var to = content.IndexOf(end, StringComparison.Ordinal);
        return from < 0 || to <= from ? string.Empty : content[(from + start.Length)..to].Trim();
    }

    private static string ReplaceBlock(string content, string start, string end, string body)
    {
        var from = content.IndexOf(start, StringComparison.Ordinal);
        var to = content.IndexOf(end, StringComparison.Ordinal);
        if (from < 0 || to <= from) return content;
        return content[..(from + start.Length)] + "\n" + body.Trim() + "\n" + content[to..];
    }

    private static string ApplyApproval(string content, string reviewer, string reason, DateTimeOffset now, string? bundleHash)
    {
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNested(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNested(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNested(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        return ReplaceNested(content, "approved_bundle_hash", bundleHash is null ? "null" : JsonSerializer.Serialize(bundleHash));
    }

    private static string ResetApproval(string content)
    {
        content = ReplaceFrontMatter(content, "status", "Review Required");
        content = ReplaceFrontMatter(content, "last_reviewed", "null");
        foreach (var field in new[] { "approved_by", "approved_at", "approval_reason", "approved_bundle_hash" })
            content = ReplaceNested(content, field, "null");
        return content;
    }

    private static string BundleDigest(string design, string components)
        => Digest(NormalizeApproval(design) + "\n--- component-sheet ---\n" + NormalizeApproval(components));

    private static string NormalizeApproval(string content)
    {
        content = ReplaceNested(content, "approved_bundle_hash", "null");
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string Digest(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();

    private static string TechnicalIntentDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $@"(?m)^{key}:.*$", $"{key}: <approval-metadata>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $@"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        normalized = Regex.Replace(normalized,
            Regex.Escape("<!-- cis:technical-intent-baseline:start -->") + ".*?" + Regex.Escape("<!-- cis:technical-intent-baseline:end -->"),
            "<managed-technical-intent-baseline>", RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return "semantic-v1:" + Digest(normalized.TrimEnd())["sha256:".Length..];
    }

    private static string? ReadFrontMatter(string content, string key)
    {
        var match = Regex.Match(content, $@"(?m)^{Regex.Escape(key)}:\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? Unquote(match.Groups["value"].Value) : null;
    }

    private static string? ReadNested(string content, string key)
    {
        var match = Regex.Match(content, $@"(?m)^  {Regex.Escape(key)}:\s*(?<value>.*?)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? Unquote(match.Groups["value"].Value) : null;
    }

    private static string ReplaceFrontMatter(string content, string key, string value)
        => Regex.Replace(content, $@"(?m)^{Regex.Escape(key)}:\s*.*$", $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string ReplaceNested(string content, string key, string value)
        => Regex.Replace(content, $@"(?m)^  {Regex.Escape(key)}:\s*.*$", $"  {key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string UpdateCatalogStatus(string catalog, string stableId, string status)
    {
        var match = Regex.Match(catalog,
            $@"(?ms)(?<entry>^  - id:\s*{Regex.Escape(stableId)}\s*$.*?)(?=^  - id:|\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return catalog;
        var updated = Regex.Replace(match.Groups["entry"].Value, @"(?m)^    status:\s*.*$", $"    status: {status}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return catalog[..match.Index] + updated + catalog[(match.Index + match.Length)..];
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\''))
            ? trimmed[1..^1] : trimmed;
    }

    private static bool Placeholder(string content)
    {
        var withoutComments = Regex.Replace(content, @"<!--.*?-->", string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.IsMatch(withoutComments, @"(?im)\b(TODO|TBD|TO BE COMPLETED)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string Codes(IEnumerable<string> values)
        => string.Join(", ", values.Select(value => $"`{Cell(value)}`")) is { Length: > 0 } result ? result : "None recorded";

    private static string Cell(string? value) => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static void Write(string path, string content)
        => File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n", new UTF8Encoding(false));

    private static SolutionDesignResult Error(string status, State state, IReadOnlyList<string> errors)
        => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id,
            state.Authority is null || state.DesignPath is null ? null : Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.DesignPath)),
            state.Authority is null || state.ComponentSheetPath is null ? null : Normalize(Path.GetRelativePath(state.Authority.RepositoryPath, state.ComponentSheetPath)),
            state.TechnicalIntentVersion, state.Components, null, errors, false);

    private sealed record State(
        CisWorkspace? Workspace,
        CisWorkspaceRepository? Authority,
        CisRepositoryContext? Context,
        string? TechnicalIntentPath,
        string? DesignPath,
        string? ComponentSheetPath,
        IReadOnlyList<SolutionComponent> Components,
        IReadOnlyList<string> ReadinessErrors,
        IReadOnlyList<string> Errors,
        string TechnicalIntentContent = "",
        string? TechnicalIntentVersion = null);
}
