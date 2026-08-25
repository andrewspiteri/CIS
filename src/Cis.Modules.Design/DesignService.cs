using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Design;

public sealed class DesignService
{
    private readonly ChangeDossierStore _changes;
    private readonly DesignTemplateCatalog _templates;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IDesignProcessRunner _runner;
    private readonly IReadOnlyList<ICisFeatureApprovalAuthority> _featureAuthorities;

    public DesignService(
        ChangeDossierStore changes,
        DesignTemplateCatalog templates,
        IEnumerable<ICisFeatureApprovalAuthority> featureAuthorities,
        Func<DateTimeOffset>? clock = null,
        IDesignProcessRunner? runner = null)
    {
        _changes = changes;
        _templates = templates;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _runner = runner ?? new NodeDesignProcessRunner();
        _featureAuthorities = featureAuthorities.ToArray();
    }

    public DesignResult Templates()
        => new("listed", null, null, null, null, _templates.List(), [], [], false);

    public DesignResult Reuse(DesignReuseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceChangeId)
            || string.IsNullOrWhiteSpace(request.SourceScreenId)
            || string.IsNullOrWhiteSpace(request.TargetScreenId)
            || string.IsNullOrWhiteSpace(request.Rationale))
            return Error(request.ChangeId, "Source change, source screen, target screen, and reason are required.");
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        if (change.Id.Equals(request.SourceChangeId, StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "A design cannot reuse itself.");
        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        if (!FrontMatter(design, "gate_status").Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "Design reuse can only change an Inactive pack before rendering and review.");

        var targetScreens = ParseScreens(File.ReadAllText(_changes.DossierFile(change, "wireframes.md")));
        var targetSlug = Slug(request.TargetScreenId);
        var target = targetScreens.SingleOrDefault(screen => Slug(screen.Id).Equals(targetSlug, StringComparison.OrdinalIgnoreCase));
        if (target is null) return Error(change.Id, $"Target screen is not present in wireframes.md: {request.TargetScreenId}");

        var source = _changes.Read(request.RepositoryPath, request.SourceChangeId);
        if (source is null) return Error(change.Id, $"Source change dossier was not found: {request.SourceChangeId}");
        var sourceDesign = File.ReadAllText(_changes.DossierFile(source, "design.md"));
        if (!FrontMatter(sourceDesign, "approval_status").Equals("Approved", StringComparison.OrdinalIgnoreCase)
            || !FrontMatter(sourceDesign, "gate_status").Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, $"Source design {source.Id} must have current Approved design authority.");
        var sourceDigests = ApprovedDesignDigests(source, sourceDesign);
        if (sourceDigests is null) return Error(change.Id, $"Source design {source.Id} has no complete approved provenance row.");
        var sourceRenderer = ResolveRenderer(source, sourceDesign);
        if (sourceRenderer is null || !File.Exists(sourceRenderer)
            || !Sha(File.ReadAllBytes(sourceRenderer)).Equals(sourceDigests.Value.Renderer, StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, $"Source design {source.Id} renderer no longer matches its approval digest.");
        var sourceWireframes = File.ReadAllText(_changes.DossierFile(source, "wireframes.md"));
        var sourceWireframeContentSha = WireframeBodySha(sourceWireframes);

        var sourceSlug = Slug(request.SourceScreenId);
        var sourceArtifacts = ReadArtifacts(source)
            .Where(item => item.ScreenId.Equals(sourceSlug, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceArtifacts.Length == 0)
            return Error(change.Id, $"Source screen has no PNG manifest artifacts in {source.Id}: {request.SourceScreenId}");
        if (!ManifestSha(ReadArtifacts(source)).Equals(sourceDigests.Value.Manifest, StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, $"Source design {source.Id} PNG manifest no longer matches its approval digest.");
        foreach (var artifact in sourceArtifacts)
        {
            if (!SourceManifestContains(sourceDesign, artifact))
                return Error(change.Id, $"Source artifact is not present in the approved PNG manifest: {artifact.Path}");
        }

        var records = ReadReuseRecords(design)
            .Where(item => !(item.TargetScreenId.Equals(targetSlug, StringComparison.OrdinalIgnoreCase)
                && item.SourceChangeId.Equals(source.Id, StringComparison.OrdinalIgnoreCase)
                && item.SourceScreenId.Equals(sourceSlug, StringComparison.OrdinalIgnoreCase)))
            .Concat(sourceArtifacts.Select(artifact => new DesignReuseRecord(
                targetSlug, target.FrontendType, source.Id, sourceSlug, artifact.State, artifact.Viewport,
                artifact.Path, artifact.Width, artifact.Height, artifact.Sha256,
                sourceDigests.Value.Wireframe, sourceWireframeContentSha, sourceDigests.Value.Renderer, sourceDigests.Value.Manifest,
                request.Rationale.Trim())))
            .OrderBy(item => item.TargetScreenId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceChangeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceScreenId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.State, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Viewport, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        design = ReplaceSection(design, "## Reused approved PNGs", ReuseSection(records));
        design = FrontMatter(design, "status", "InProgress");
        design = FrontMatter(design, "approval_status", "NotReviewed");
        File.WriteAllText(designPath, design);
        _changes.AppendEvent(change, "design-artifacts-reused", new Dictionary<string, string>
        {
            ["sourceChange"] = source.Id,
            ["sourceScreen"] = sourceSlug,
            ["targetScreen"] = targetSlug,
            ["artifacts"] = sourceArtifacts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sourceManifestSha256"] = sourceDigests.Value.Manifest,
            ["sourceWireframeContentSha256"] = sourceWireframeContentSha,
            ["reason"] = request.Rationale.Trim(),
        });
        var mapped = sourceArtifacts.Select(item => item with { ScreenId = targetSlug }).ToArray();
        return new("reused", change.Id, "Inactive", "NotReviewed", null, [], mapped,
            [$"INFO: {sourceArtifacts.Length} approved artifact(s) carried from {source.Id}/{sourceSlug} to {targetSlug}."], true);
    }

    public DesignResult Scaffold(DesignScaffoldRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        var templateErrors = _templates.Validate(request.Shell, request.Components);
        if (templateErrors.Count > 0) return Error(change.Id, templateErrors.ToArray());

        var wireframePath = _changes.DossierFile(change, "wireframes.md");
        var wireframe = File.ReadAllText(wireframePath);
        var wireframeValidation = ValidateWireframes(request.RepositoryPath, request.ChangeId);
        if (wireframeValidation.ExitCode != 0) return wireframeValidation;
        var allScreens = ParseScreens(wireframe);
        if (allScreens.Count == 0)
        {
            return Error(change.Id, "wireframes.md requires at least one non-placeholder Screen inventory row.");
        }

        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        var reusedTargets = ReadReuseRecords(design).Select(item => item.TargetScreenId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var screens = allScreens.Where(screen => !reusedTargets.Contains(Slug(screen.Id))).ToArray();
        var guidelinePath = FindGuideline(change);
        if (guidelinePath is null) return Error(change.Id, "No governed design-guidelines document was found.");
        var relativeGuideline = Relative(change.RepositoryPath, guidelinePath);
        var guidelineSha = Sha(File.ReadAllBytes(guidelinePath));
        var wireframeSha = CurrentApprovedWireframeSha(wireframe) ?? WireframeBodySha(wireframe);
        var slug = Slug(request.Feature);
        if (slug.Length == 0) return Error(change.Id, "Feature name must produce a non-empty renderer filename.");
        var rendererPath = Path.Combine(Path.GetDirectoryName(wireframePath)!, "assets", $"render-{slug}-screens.mjs");
        if (File.Exists(rendererPath) && !request.Force)
        {
            return Error(change.Id, $"Renderer already exists; use --force only after reviewing the replacement: {Relative(change.RepositoryPath, rendererPath)}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(rendererPath)!);
        var requestedComponents = request.Components.Count == 0
            ? new[] { "component.page-header", "component.button", "component.filter-bar", "component.table", "component.status-badge", "component.card", "component.empty-state" }
            : request.Components.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = _templates.ResolveComponents(request.Shell, requestedComponents);
        File.WriteAllText(rendererPath, DesignRendererScaffolder.Render(
            request.Feature, screens, request.Shell, selected, relativeGuideline, guidelineSha, wireframeSha));

        var rendererRelative = Relative(change.RepositoryPath, rendererPath);
        design = ReplaceSection(design, "## Inputs and renderer", $"""
## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `{Relative(change.RepositoryPath, wireframePath)}` | `{wireframeSha}` | `{relativeGuideline}` | `{guidelineSha}` | `{rendererRelative}` | `{Sha(File.ReadAllBytes(rendererPath))}` | Pending render | Pending render | Pending render |

""");
        var usedTemplates = new[] { request.Shell }.Concat(selected).Select(id => _templates.Find(id)!).ToArray();
        var templateRows = string.Join(Environment.NewLine, usedTemplates.Select(template =>
            $"| `{template.Id}` | {template.Version} | {template.Kind} | CIS built-in | {template.Description} | Selected |"));
        design = ReplaceSection(design, "## Application shell and component templates", $"""
## Application shell and component templates

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |
{templateRows}

""");
        var surfaceRows = string.Join(Environment.NewLine, allScreens
            .GroupBy(screen => screen.FrontendType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => FrontendTypeOrder(group.Key))
            .Select(group => $"| `{group.Key}` | {string.Join(", ", group.Select(screen => $"`{screen.Id}`"))} | `{request.Shell}` | {string.Join(", ", group.Select(screen => reusedTargets.Contains(Slug(screen.Id)) ? $"{screen.Id}:Reused" : $"{screen.Id}:Render required"))} |"));
        design = ReplaceSection(design, "## Frontend surface coverage", $"""
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
{surfaceRows}

""");
        design = FrontMatter(design, "status", "InProgress");
        File.WriteAllText(designPath, design);
        _changes.AppendEvent(change, "design-renderer-scaffolded", new Dictionary<string, string>
        {
            ["renderer"] = rendererRelative,
            ["wireframeSha256"] = wireframeSha,
            ["wireframeApproval"] = FrontMatter(wireframe, "approval_status"),
            ["guidelineSha256"] = guidelineSha,
            ["templates"] = string.Join(',', usedTemplates.Select(template => template.Id)),
            ["renderScreens"] = string.Join(',', screens.Select(screen => Slug(screen.Id))),
            ["reusedScreens"] = string.Join(',', reusedTargets),
        });
        var estimated = usedTemplates.Sum(template => template.EstimatedSavedTokens);
        return new("scaffolded", change.Id, "Inactive", "NotReviewed", rendererRelative,
            usedTemplates, [], [$"INFO: estimated template token savings={estimated}; render screens={screens.Length}; reused screens={reusedTargets.Count}"], true);
    }

    public DesignResult Validate(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null) return Error(changeId, $"Change dossier was not found: {changeId}");
        var diagnostics = new List<string>();
        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        var rendererPath = ResolveRenderer(change, design);
        if (rendererPath is null || !File.Exists(rendererPath)) diagnostics.Add("ERROR: A canonical .mjs renderer is required.");
        else
        {
            var source = File.ReadAllText(rendererPath);
            if (!source.Contains("import sharp from \"sharp\"", StringComparison.Ordinal)
                && !source.Contains("(\"sharp\")", StringComparison.Ordinal))
                diagnostics.Add("ERROR: Renderer must use pinned Sharp.");
            Required(source, "function applicationShell", "Every renderer must contain the application-shell template.", diagnostics);
            Required(source, "applicationShell(screen, content)", "Every screen must render inside the application shell.", diagnostics);
            var selectedComponents = Regex.Matches(design,
                    @"(?m)^\| `(?<id>component\.[^`]+)` \| [^\r\n]+ \| Selected(?: after review)? \|\s*$",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => match.Groups["id"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var component in selectedComponents)
            {
                Required(source, $"\"{component}\"",
                    $"Renderer provenance must contain selected template {component}.", diagnostics);
            }
            Required(source, "guidelineSha256", "Renderer must preserve design-guideline provenance.", diagnostics);
            var sourceWithoutSvgNamespace = source.Replace("http://www.w3.org/2000/svg", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (sourceWithoutSvgNamespace.Contains("fetch(", StringComparison.Ordinal)
                || sourceWithoutSvgNamespace.Contains("http://", StringComparison.OrdinalIgnoreCase)
                || sourceWithoutSvgNamespace.Contains("https://", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add("ERROR: Renderer must not fetch or reference network resources.");
            }
        }

        var wireframes = File.ReadAllText(_changes.DossierFile(change, "wireframes.md"));
        var recordedWireframeSha = ApprovedWireframeSha(wireframes);
        var approvedWireframeSha = CurrentApprovedWireframeSha(wireframes);
        var governedWireframeSha = approvedWireframeSha ?? WireframeBodySha(wireframes);
        if (rendererPath is not null && File.Exists(rendererPath))
        {
            var renderer = File.ReadAllText(rendererPath);
            Required(renderer, $"wireframeSha256: \"{governedWireframeSha}\"",
                "Renderer wireframe provenance must match the exact validated wireframe digest.", diagnostics);
            if (!design.Contains($"`{governedWireframeSha}`", StringComparison.Ordinal))
                diagnostics.Add("ERROR: design.md wireframe provenance must match the exact validated wireframe digest.");
        }
        if (recordedWireframeSha is not null && approvedWireframeSha is null)
            diagnostics.Add("INFO: The earlier wireframe approval is stale; design approval or eligible unchanged-manifest reconciliation will renew authority for the current digest.");
        else if (approvedWireframeSha is null)
            diagnostics.Add("INFO: Wireframe authority will be recorded atomically with design approval.");
        var classifiedScreens = ParseScreens(wireframes);
        if (classifiedScreens.Count == 0 || classifiedScreens.Any(screen => !ValidFrontendType(screen.FrontendType)))
        {
            diagnostics.Add("ERROR: Every wireframe screen must classify Frontend type as public, customer, or backoffice.");
        }
        if (!design.Contains("Guideline SHA-256", StringComparison.Ordinal)
            || design.Contains("| TODO | TODO | TODO | TODO | TODO | TODO | TODO | TODO | TODO |", StringComparison.Ordinal))
        {
            diagnostics.Add("ERROR: design.md does not record resolved guideline and renderer provenance.");
        }
        if (!design.Contains("## Frontend surface coverage", StringComparison.Ordinal))
        {
            diagnostics.Add("ERROR: design.md does not record frontend surface coverage.");
        }

        var artifacts = ReadArtifacts(change);
        var reused = ValidateReuseRecords(change, design, diagnostics);
        var combinedArtifacts = artifacts.Concat(reused).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var gateStatus = FrontMatter(design, "gate_status");
        if (gateStatus is "PausedForReview" or "Approved" && combinedArtifacts.Length == 0)
        {
            diagnostics.Add("ERROR: Review or approval requires generated PNG artifacts.");
        }
        if (gateStatus is "PausedForReview" or "Approved")
        {
            var covered = combinedArtifacts.Select(item => item.ScreenId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = classifiedScreens.Select(screen => Slug(screen.Id)).Where(screen => !covered.Contains(screen)).ToArray();
            if (missing.Length > 0)
                diagnostics.Add($"ERROR: Review requires rendered or reused PNG coverage for every wireframe screen; missing: {string.Join(", ", missing)}.");
        }
        return new(diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? "invalid" : "valid",
            change.Id, FrontMatter(design, "gate_status"), FrontMatter(design, "approval_status"),
            rendererPath is null ? null : Relative(change.RepositoryPath, rendererPath), [], combinedArtifacts, diagnostics, false);
    }

    public DesignResult ValidateWireframes(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null) return Error(changeId, $"Change dossier was not found: {changeId}");
        var content = File.ReadAllText(_changes.DossierFile(change, "wireframes.md"));
        var diagnostics = new List<string>();
        if (!FrontMatter(content, "type").Equals("textual-wireframes", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add("ERROR: wireframes.md must declare type: textual-wireframes.");
        if (!FrontMatter(content, "change_id").Equals(change.Id, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add($"ERROR: wireframes.md change_id must be {change.Id}.");
        if (HasAuthoringPlaceholder(content))
            diagnostics.Add("ERROR: Wireframes contain TODO, TBD, or to-be-completed placeholders.");

        var screens = ParseScreens(content);
        if (screens.Count == 0)
            diagnostics.Add("ERROR: At least one non-placeholder Screen inventory row is required.");
        if (screens.Any(screen => !ValidFrontendType(screen.FrontendType)))
            diagnostics.Add("ERROR: Every screen must classify Frontend type as public, customer, or backoffice.");
        if (screens.Any(screen => string.IsNullOrWhiteSpace(screen.Route)
            || !screen.Route.StartsWith("/", StringComparison.Ordinal)
            || screen.Route.Equals("not-applicable", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add("ERROR: Every application screen requires a concrete repository-relative route/path beginning with '/'.");

        var duplicateScreens = RawInventoryIds(content)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateScreens.Length > 0)
            diagnostics.Add($"ERROR: Duplicate Screen IDs: {string.Join(", ", duplicateScreens)}.");
        foreach (var screen in screens)
        {
            var definition = ScreenDefinitionBody(content, screen.Id);
            if (definition.Length == 0)
            {
                diagnostics.Add($"ERROR: Screen {screen.Id} requires a matching definition heading.");
                continue;
            }
            Required(definition, "#### Description", $"Screen {screen.Id} requires a Description section.", diagnostics);
            Required(definition, "#### Actions and paths", $"Screen {screen.Id} requires an Actions and paths section.", diagnostics);
            Required(definition, "#### States", $"Screen {screen.Id} requires a States section.", diagnostics);
        }

        var actions = ParseActionRows(content, diagnostics);
        foreach (var duplicate in actions.GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            diagnostics.Add($"ERROR: Duplicate Action ID: {duplicate.Key}.");
        if (actions.Count == 0) diagnostics.Add("ERROR: At least one action or automatic transition row is required.");
        if (actions.Any(action => string.IsNullOrWhiteSpace(action.DestinationPath)
            || string.IsNullOrWhiteSpace(action.DestinationScreen)))
            diagnostics.Add("ERROR: Every action requires a destination path and destination screen.");

        var coverage = SectionBody(content, "## Journey and requirement coverage");
        if (coverage.Length == 0 || !coverage.Contains('|'))
            diagnostics.Add("ERROR: Journey and requirement coverage requires at least one table row.");
        if (!content.Contains("## Approval decision", StringComparison.Ordinal))
            diagnostics.Add("ERROR: wireframes.md requires an Approval decision section.");

        if (!diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)))
            diagnostics.Add($"INFO: screens={screens.Count};actions={actions.Count};wireframeSha256={WireframeBodySha(content)}");
        return new(diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? "invalid" : "wireframes-valid",
            change.Id, "Inactive", FrontMatter(content, "approval_status"), null, [], [], diagnostics, false);
    }

    public DesignResult ApproveWireframes(DesignReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            return Error(request.ChangeId, "Reviewer and rationale are required.");
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        var path = _changes.DossierFile(change, "wireframes.md");
        var content = File.ReadAllText(path);
        var validation = ValidateWireframes(request.RepositoryPath, request.ChangeId);
        if (validation.ExitCode != 0) return validation;
        var digest = WireframeBodySha(content);
        content = ReplaceSection(content, "## Approval decision", $"""
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Approved | {Cell(request.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | `{digest}` | {Cell(request.Rationale)} |
""");
        content = FrontMatter(content, "status", "Approved");
        content = FrontMatter(content, "approval_status", "Approved");
        File.WriteAllText(path, content);
        _changes.AppendEvent(change, "wireframes-approved", new Dictionary<string, string>
        {
            ["reviewer"] = request.Reviewer.Trim(), ["rationale"] = request.Rationale.Trim(), ["sha256"] = digest,
        });
        return new("wireframes-approved", change.Id, "Inactive", "Approved", null, [], [], [], true);
    }

    public DesignResult RejectWireframes(DesignReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            return Error(request.ChangeId, "Reviewer and rationale are required.");
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        var path = _changes.DossierFile(change, "wireframes.md");
        var content = File.ReadAllText(path);
        var digest = WireframeBodySha(content);
        content = ReplaceSection(content, "## Approval decision", $"""
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Rejected | {Cell(request.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | `{digest}` | {Cell(request.Rationale)} |
""");
        content = FrontMatter(content, "status", "InProgress");
        content = FrontMatter(content, "approval_status", "Rejected");
        File.WriteAllText(path, content);
        _changes.AppendEvent(change, "wireframes-rejected", new Dictionary<string, string>
        {
            ["reviewer"] = request.Reviewer.Trim(), ["rationale"] = request.Rationale.Trim(), ["sha256"] = digest,
        });
        return new("wireframes-rejected", change.Id, "Inactive", "Rejected", null, [], [], [], true);
    }

    public DesignResult Render(string repositoryPath, string changeId)
    {
        var validation = Validate(repositoryPath, changeId);
        var preErrors = validation.Diagnostics.Where(item => item.StartsWith("ERROR:", StringComparison.Ordinal)
            && !item.Contains("PNG artifacts", StringComparison.Ordinal)
            && !item.Contains("PNG coverage", StringComparison.Ordinal)).ToArray();
        if (preErrors.Length > 0) return validation with { Diagnostics = preErrors };
        var change = _changes.Read(repositoryPath, changeId)!;
        var rendererPath = Path.Combine(change.RepositoryPath, validation.RendererPath!.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var process = _runner.Run(change.RepositoryPath, rendererPath);
            if (process.ExitCode != 0)
            {
                return Error(change.Id, $"Renderer failed with exit code {process.ExitCode}: {process.StandardError.Trim()}");
            }
            var artifacts = ReadArtifacts(change);
            var designPath = _changes.DossierFile(change, "design.md");
            var design = File.ReadAllText(designPath);
            var reuseDiagnostics = new List<string>();
            var reused = ValidateReuseRecords(change, design, reuseDiagnostics);
            if (reuseDiagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)))
                return new("invalid", change.Id, null, null, null, [], [], reuseDiagnostics, false);
            var combinedArtifacts = artifacts.Concat(reused).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (combinedArtifacts.Length == 0) return Error(change.Id, "Renderer completed without producing PNG artifacts or resolving approved reuse evidence.");
            var rendererSha = Sha(File.ReadAllBytes(rendererPath));
            design = Regex.Replace(design,
                @"(?m)^(\| `[^`]+` \| `sha256:[^`]+` \| `[^`]+` \| `sha256:[^`]+` \| `[^`]+` \| )`sha256:[^`]+`( \|)",
                $"$1`{rendererSha}`$2", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var runtime = Regex.Match(process.StandardOutput, @"Runtime node=(?<node>\S+) sharp=(?<sharp>\S+) libvips=(?<vips>\S+)");
            if (runtime.Success)
            {
                design = Regex.Replace(design,
                    @"(?m)^(\| `[^`]+` \| `sha256:[^`]+` \| `[^`]+` \| `sha256:[^`]+` \| `[^`]+` \| `sha256:[^`]+` \| )[^\r\n|]+ \| [^\r\n|]+ \| [^\r\n|]+ \|(?=\r?$)",
                    $"$1{runtime.Groups["node"].Value} | {runtime.Groups["sharp"].Value} | {runtime.Groups["vips"].Value} |",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            var screenTypes = ParseScreens(File.ReadAllText(_changes.DossierFile(change, "wireframes.md")))
                .ToDictionary(screen => Slug(screen.Id), screen => screen.FrontendType, StringComparer.OrdinalIgnoreCase);
            var rows = string.Join(Environment.NewLine, artifacts.Select(item =>
                $"| `{item.ScreenId}` | `{screenTypes.GetValueOrDefault(item.ScreenId) ?? "unclassified"}` | {item.State} | {item.Viewport} | `{item.Path}` | {item.Width}x{item.Height} | `{item.Sha256}` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |"));
            design = ReplaceSection(design, "## PNG manifest", $"""
## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
{rows}

""");
            design = FrontMatter(design, "status", "ReadyForReview");
            design = FrontMatter(design, "approval_status", "ReadyForReview");
            design = FrontMatter(design, "gate_status", "PausedForReview");
            File.WriteAllText(designPath, design);
            _changes.AppendEvent(change, "design-review-paused", new Dictionary<string, string>
            {
                ["renderer"] = validation.RendererPath!,
                ["renderedArtifacts"] = artifacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reusedArtifacts"] = reused.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["artifacts"] = combinedArtifacts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["manifestSha256"] = ManifestSha(combinedArtifacts),
            });
            return new("ready-for-review", change.Id, "PausedForReview", "ReadyForReview",
                validation.RendererPath, [], combinedArtifacts, [$"INFO: {process.StandardOutput.Trim()}; rendered={artifacts.Count}; reused={reused.Count}"], true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Error(change.Id, $"Node/Sharp rendering is unavailable: {ex.Message}");
        }
    }

    public DesignResult Approve(DesignReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            return Error(request.ChangeId, "Reviewer and rationale are required.");
        var validation = Validate(request.RepositoryPath, request.ChangeId);
        if (validation.ExitCode != 0) return validation;
        var change = _changes.Read(request.RepositoryPath, request.ChangeId)!;
        if (!validation.GateStatus!.Equals("PausedForReview", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "Design approval requires a PausedForReview design pack.");
        var wireframes = File.ReadAllText(_changes.DossierFile(change, "wireframes.md"));
        if (CurrentApprovedWireframeSha(wireframes) is null)
        {
            var wireframeApproval = ApproveWireframes(request);
            if (wireframeApproval.ExitCode != 0) return wireframeApproval;
            _changes.AppendEvent(change, "wireframe-authority-carried-with-design", new Dictionary<string, string>
            {
                ["reviewer"] = request.Reviewer.Trim(),
                ["rationale"] = request.Rationale.Trim(),
                ["sha256"] = WireframeBodySha(wireframes),
            });
        }
        var wireframeSha = WireframeBodySha(wireframes);
        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        var rendererSha = Sha(File.ReadAllBytes(Path.Combine(change.RepositoryPath, validation.RendererPath!.Replace('/', Path.DirectorySeparatorChar))));
        design = ReplaceSection(design, "## Approval decision", $"""
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Approved | {Cell(request.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | `{wireframeSha}` | `{rendererSha}` | `{ManifestSha(validation.Artifacts)}` | {Cell(request.Rationale)} |
""");
        design = FrontMatter(design, "status", "Approved");
        design = FrontMatter(design, "approval_status", "Approved");
        design = FrontMatter(design, "gate_status", "Approved");
        File.WriteAllText(designPath, design);
        _changes.AppendEvent(change, "design-approved", new Dictionary<string, string>
        {
            ["reviewer"] = request.Reviewer.Trim(), ["rationale"] = request.Rationale.Trim(),
            ["wireframeSha256"] = wireframeSha, ["rendererSha256"] = rendererSha,
            ["manifestSha256"] = ManifestSha(validation.Artifacts),
        });
        return validation with { Status = "approved", GateStatus = "Approved", ApprovalStatus = "Approved", Applied = true };
    }

    public DesignResult Reconcile(string repositoryPath, string changeId)
    {
        var validation = Validate(repositoryPath, changeId);
        if (validation.ExitCode != 0) return validation;
        var change = _changes.Read(repositoryPath, changeId)!;
        if (!string.Equals(validation.GateStatus, "PausedForReview", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "Design reconciliation requires a PausedForReview design pack.");

        var planPath = _changes.DossierFile(change, "plan.md");
        var plan = File.ReadAllText(planPath);
        if (!FrontMatter(plan, "status").Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "Design reconciliation requires an Approved delivery plan.");
        var featurePath = FrontMatter(plan, "feature_spec_path");
        var plannedFeatureSha = FrontMatter(plan, "feature_spec_sha256").ToLowerInvariant();
        if (featurePath.Length == 0 || !Regex.IsMatch(plannedFeatureSha, "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            return Error(change.Id, "The Approved plan must pin feature_spec_path and feature_spec_sha256.");

        var featureAbsolute = Path.GetFullPath(Path.Combine(change.RepositoryPath,
            featurePath.Replace('/', Path.DirectorySeparatorChar)));
        var repositoryRoot = Path.GetFullPath(change.RepositoryPath).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!featureAbsolute.StartsWith(repositoryRoot, comparison) || !File.Exists(featureAbsolute))
            return Error(change.Id, $"The plan's feature specification is missing or outside the repository: {featurePath}");
        var currentFeatureSha = Sha(File.ReadAllBytes(featureAbsolute));
        if (!currentFeatureSha.Equals(plannedFeatureSha, StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, $"The Approved plan is stale: feature digest is {currentFeatureSha}, expected {plannedFeatureSha}.");

        var applicable = _featureAuthorities.Select(authority => authority.Evaluate(change.RepositoryPath, featurePath))
            .Where(result => result.Applicable).ToArray();
        if (applicable.Length != 1)
            return Error(change.Id, applicable.Length == 0
                ? "No feature-approval authority applies; explicit design approval is required."
                : "Multiple feature-approval authorities apply; resolve the authority conflict before reconciliation.");
        var authority = applicable[0];
        if (!authority.Ready)
            return Error(change.Id, authority.Errors.Count == 0
                ? "The feature approval is not current; explicit design approval is required."
                : string.Join(" ", authority.Errors));
        if (!string.Equals(authority.FeaturePath, featurePath, comparison)
            || string.IsNullOrWhiteSpace(authority.Reviewer)
            || string.IsNullOrWhiteSpace(authority.Rationale))
            return Error(change.Id, "Feature authority is missing the exact path, reviewer, or rationale required for carry-forward.");

        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        var priorManifest = PriorApprovedManifestSha(design);
        if (priorManifest is null)
            return Error(change.Id, "No earlier approved design manifest exists; explicit design approval is required.");
        var currentManifest = ManifestSha(validation.Artifacts);
        if (!priorManifest.Equals(currentManifest, StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, $"Rendered pixels changed ({priorManifest} -> {currentManifest}); explicit design review is required.");

        var wireframePath = _changes.DossierFile(change, "wireframes.md");
        var wireframes = File.ReadAllText(wireframePath);
        var wireframeSha = WireframeBodySha(wireframes);
        var rationale = $"Authority carried forward from currently approved feature {authority.ItemId ?? featurePath}: {authority.Rationale} Rendered PNG manifest is unchanged from the earlier approved revision.";
        wireframes = ReplaceSection(wireframes, "## Approval decision", $"""
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Authority carried forward | {Cell(authority.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | `{wireframeSha}` | {Cell(rationale)} |
""");
        wireframes = FrontMatter(wireframes, "status", "Approved");
        wireframes = FrontMatter(wireframes, "approval_status", "Approved");
        File.WriteAllText(wireframePath, wireframes);

        var rendererSha = Sha(File.ReadAllBytes(Path.Combine(change.RepositoryPath,
            validation.RendererPath!.Replace('/', Path.DirectorySeparatorChar))));
        design = ReplaceSection(design, "## Approval decision", $"""
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Authority carried forward | {Cell(authority.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | `{wireframeSha}` | `{rendererSha}` | `{currentManifest}` | {Cell(rationale)} |
""");
        design = FrontMatter(design, "status", "Approved");
        design = FrontMatter(design, "approval_status", "Approved");
        design = FrontMatter(design, "gate_status", "Approved");
        File.WriteAllText(designPath, design);
        _changes.AppendEvent(change, "design-authority-carried-forward", new Dictionary<string, string>
        {
            ["feature"] = authority.ItemId ?? featurePath,
            ["featurePath"] = featurePath,
            ["featureSha256"] = currentFeatureSha,
            ["reviewer"] = authority.Reviewer.Trim(),
            ["wireframeSha256"] = wireframeSha,
            ["rendererSha256"] = rendererSha,
            ["manifestSha256"] = currentManifest,
            ["priorManifestSha256"] = priorManifest,
        });
        return validation with
        {
            Status = "authority-carried-forward",
            GateStatus = "Approved",
            ApprovalStatus = "Approved",
            Diagnostics = validation.Diagnostics.Append(
                $"INFO: Authority carried forward from {authority.ItemId ?? featurePath}; PNG manifest is unchanged.").ToArray(),
            Applied = true,
        };
    }

    public DesignResult Reject(DesignReviewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            return Error(request.ChangeId, "Reviewer and rationale are required.");
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        if (!FrontMatter(design, "gate_status").Equals("PausedForReview", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "Design rejection requires a PausedForReview design pack.");
        var rendererPath = ResolveRenderer(change, design);
        var localArtifacts = ReadArtifacts(change);
        var validation = Validate(request.RepositoryPath, request.ChangeId);
        var hashes = string.Join(", ", validation.Artifacts.Select(item => item.Sha256));
        var existing = SectionBody(design, "## Rejected revisions");
        var row = $"| `{(rendererPath is null ? "missing" : Sha(File.ReadAllBytes(rendererPath)))}` | {Cell(hashes)} | {Cell(request.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | Rejected during human design review | {Cell(request.Rationale)} |";
        var replacement = existing.Contains("| Renderer revision/digest |", StringComparison.Ordinal)
            ? existing.TrimEnd() + Environment.NewLine + row + Environment.NewLine
            : $"""
Rejected PNG files are removed. Preserve their manifest hashes and review evidence.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |
{row}
""";
        design = ReplaceSection(design, "## Rejected revisions", "## Rejected revisions\n\n" + replacement + "\n");
        design = FrontMatter(design, "status", "InProgress");
        design = FrontMatter(design, "approval_status", "Rejected");
        design = FrontMatter(design, "gate_status", "PausedForReview");
        File.WriteAllText(designPath, design);
        foreach (var artifact in localArtifacts)
        {
            var path = Path.GetFullPath(Path.Combine(change.RepositoryPath, artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
            var assetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(designPath)!, "assets")) + Path.DirectorySeparatorChar;
            if (path.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) File.Delete(path);
        }
        _changes.AppendEvent(change, "design-rejected", new Dictionary<string, string>
        {
            ["reviewer"] = request.Reviewer.Trim(), ["rationale"] = request.Rationale.Trim(),
            ["rejectedPngHashes"] = hashes,
        });
        return new("rejected", change.Id, "PausedForReview", "Rejected",
            rendererPath is null ? null : Relative(change.RepositoryPath, rendererPath), [], [], [], true);
    }

    public DesignResult Status(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null) return Error(changeId, $"Change dossier was not found: {changeId}");
        var design = File.ReadAllText(_changes.DossierFile(change, "design.md"));
        var renderer = ResolveRenderer(change, design);
        var diagnostics = new List<string>();
        var artifacts = ReadArtifacts(change).Concat(ValidateReuseRecords(change, design, diagnostics))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        return new("shown", change.Id, FrontMatter(design, "gate_status"), FrontMatter(design, "approval_status"),
            renderer is null ? null : Relative(change.RepositoryPath, renderer), [], artifacts, diagnostics, false);
    }

    private static IReadOnlyList<WireframeScreen> ParseScreens(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var header = Array.FindIndex(lines, line => line.Trim().StartsWith("| Screen ID |", StringComparison.OrdinalIgnoreCase));
        if (header < 0) return [];
        var headers = lines[header].Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
        var idAt = Array.FindIndex(headers, value => value.Equals("Screen ID", StringComparison.OrdinalIgnoreCase));
        var typeAt = Array.FindIndex(headers, value => value.Equals("Frontend type", StringComparison.OrdinalIgnoreCase));
        var nameAt = Array.FindIndex(headers, value => value.Equals("Name", StringComparison.OrdinalIgnoreCase));
        var routeAt = Array.FindIndex(headers, value => value.Equals("Route/path", StringComparison.OrdinalIgnoreCase));
        var platformAt = Array.FindIndex(headers, value => value.Equals("Platform", StringComparison.OrdinalIgnoreCase));
        var purposeAt = Array.FindIndex(headers, value => value.Equals("Purpose", StringComparison.OrdinalIgnoreCase));
        if (new[] { idAt, typeAt, nameAt, routeAt, platformAt, purposeAt }.Any(index => index < 0)) return [];
        var result = new List<WireframeScreen>();
        for (var index = header + 2; index < lines.Length && lines[index].TrimStart().StartsWith('|'); index++)
        {
            var cells = lines[index].Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray();
            if (cells.Length < headers.Length || HasAuthoringPlaceholder(cells[idAt])) continue;
            result.Add(new(cells[idAt], cells[typeAt].ToLowerInvariant(), cells[nameAt], cells[routeAt], cells[platformAt], cells[purposeAt]));
        }
        return result.DistinctBy(screen => screen.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ValidFrontendType(string value) => value is "public" or "customer" or "backoffice";

    private static IReadOnlyList<string> RawInventoryIds(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var header = Array.FindIndex(lines, line => line.Trim().StartsWith("| Screen ID |", StringComparison.OrdinalIgnoreCase));
        if (header < 0) return [];
        var result = new List<string>();
        for (var index = header + 2; index < lines.Length && lines[index].TrimStart().StartsWith('|'); index++)
        {
            var cells = lines[index].Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray();
            if (cells.Length > 0 && !HasAuthoringPlaceholder(cells[0])) result.Add(cells[0]);
        }
        return result;
    }

    private static IReadOnlyList<WireframeAction> ParseActionRows(string content, ICollection<string> diagnostics)
    {
        var result = new List<WireframeAction>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Trim().StartsWith("| Action ID |", StringComparison.OrdinalIgnoreCase)) continue;
            var headers = lines[index].Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            var idAt = Array.FindIndex(headers, value => value.Equals("Action ID", StringComparison.OrdinalIgnoreCase));
            var pathAt = Array.FindIndex(headers, value => value.Equals("Destination path", StringComparison.OrdinalIgnoreCase));
            var screenAt = Array.FindIndex(headers, value => value.Equals("Destination screen", StringComparison.OrdinalIgnoreCase));
            if (idAt < 0 || pathAt < 0 || screenAt < 0)
            {
                diagnostics.Add("ERROR: Actions and paths tables require Action ID, Destination path, and Destination screen columns.");
                continue;
            }
            for (var row = index + 2; row < lines.Length && lines[row].TrimStart().StartsWith('|'); row++)
            {
                var cells = lines[row].Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray();
                if (cells.Length < headers.Length)
                {
                    diagnostics.Add($"ERROR: Malformed action row near line {row + 1}.");
                    continue;
                }
                if (!HasAuthoringPlaceholder(cells[idAt])) result.Add(new(cells[idAt], cells[pathAt], cells[screenAt]));
            }
        }
        return result;
    }

    private static bool HasAuthoringPlaceholder(string value)
        => Regex.IsMatch(value, @"(?<![A-Za-z0-9_-])(?:TODO|TBD)(?![A-Za-z0-9_-])|(?i:\bto be completed\b)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static int FrontendTypeOrder(string value) => value switch { "public" => 0, "customer" => 1, "backoffice" => 2, _ => 3 };

    private IReadOnlyList<DesignArtifact> ValidateReuseRecords(
        ChangeDossier change,
        string design,
        ICollection<string> diagnostics)
    {
        var records = ReadReuseRecords(design);
        var rawRows = MarkdownTable(SectionBody(design, "## Reused approved PNGs"));
        var declaredRows = Math.Max(0, rawRows.Count - 2);
        if (declaredRows != records.Count)
            diagnostics.Add($"ERROR: Reused approved PNGs contains {declaredRows - records.Count} malformed row(s); repair or remove them before continuing.");
        if (records.Count == 0) return [];
        var targetScreens = ParseScreens(File.ReadAllText(_changes.DossierFile(change, "wireframes.md")))
            .ToDictionary(screen => Slug(screen.Id), StringComparer.OrdinalIgnoreCase);
        var duplicateRows = records.GroupBy(item => $"{item.TargetScreenId}|{item.Path}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateRows.Length > 0)
            diagnostics.Add($"ERROR: Duplicate reused design artifact mappings: {string.Join(", ", duplicateRows)}.");
        var result = new List<DesignArtifact>();
        foreach (var record in records)
        {
            if (!targetScreens.TryGetValue(record.TargetScreenId, out var target))
            {
                diagnostics.Add($"ERROR: Reused artifact target is not a wireframe screen: {record.TargetScreenId}.");
                continue;
            }
            if (!target.FrontendType.Equals(record.FrontendType, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Reused artifact frontend type for {record.TargetScreenId} is stale ({record.FrontendType} != {target.FrontendType}).");
            var source = _changes.Read(change.RepositoryPath, record.SourceChangeId);
            if (source is null)
            {
                diagnostics.Add($"ERROR: Reused artifact source change is missing: {record.SourceChangeId}.");
                continue;
            }
            var sourceDesign = File.ReadAllText(_changes.DossierFile(source, "design.md"));
            var digests = ApprovedDesignDigests(source, sourceDesign);
            if (!FrontMatter(sourceDesign, "approval_status").Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || !FrontMatter(sourceDesign, "gate_status").Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || digests is null)
            {
                diagnostics.Add($"ERROR: Reused artifact source {source.Id} no longer has complete Approved authority.");
                continue;
            }
            if (!record.SourceWireframeSha256.Equals(digests.Value.Wireframe, StringComparison.OrdinalIgnoreCase)
                || !record.SourceRendererSha256.Equals(digests.Value.Renderer, StringComparison.OrdinalIgnoreCase)
                || !record.SourceManifestSha256.Equals(digests.Value.Manifest, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Reused artifact provenance for {source.Id}/{record.SourceScreenId} is stale.");
            var renderer = ResolveRenderer(source, sourceDesign);
            if (renderer is null || !File.Exists(renderer)
                || !Sha(File.ReadAllBytes(renderer)).Equals(digests.Value.Renderer, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Reused artifact source renderer is stale: {source.Id}.");
            var sourceWireframes = File.ReadAllText(_changes.DossierFile(source, "wireframes.md"));
            if (!WireframeBodySha(sourceWireframes).Equals(record.SourceWireframeContentSha256, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Reused artifact source wireframe content is stale: {source.Id}.");
            var sourceArtifacts = ReadArtifacts(source);
            if (!ManifestSha(sourceArtifacts).Equals(digests.Value.Manifest, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Reused artifact source PNG manifest is stale: {source.Id}.");

            var path = Path.GetFullPath(Path.Combine(change.RepositoryPath, record.Path.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(change.RepositoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root, comparison) || !File.Exists(path))
            {
                diagnostics.Add($"ERROR: Reused PNG is missing or outside the repository: {record.Path}.");
                continue;
            }
            var dimensions = PngDimensions(path);
            var sha = Sha(File.ReadAllBytes(path));
            if (dimensions.Width != record.Width || dimensions.Height != record.Height
                || !sha.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Reused PNG bytes or dimensions changed: {record.Path}.");
            var sourceArtifact = new DesignArtifact(record.SourceScreenId, record.State, record.Viewport,
                record.Path, record.Width, record.Height, record.Sha256, new FileInfo(path).Length);
            if (!SourceManifestContains(sourceDesign, sourceArtifact))
                diagnostics.Add($"ERROR: Reused PNG is no longer declared by source {source.Id}: {record.Path}.");
            result.Add(sourceArtifact with { ScreenId = record.TargetScreenId });
        }
        if (!diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)))
            diagnostics.Add($"INFO: reused artifacts={result.Count}; reused target screens={result.Select(item => item.ScreenId).Distinct(StringComparer.OrdinalIgnoreCase).Count()}.");
        return result;
    }

    private (string Wireframe, string Renderer, string Manifest)? ApprovedDesignDigests(ChangeDossier source, string design)
    {
        var section = SectionBody(design, "## Approval decision");
        var rows = MarkdownTable(section);
        if (rows.Count < 2) return null;
        var headers = rows[0];
        var approved = rows.Skip(2).FirstOrDefault(row => row.Count == headers.Count
            && (row[0].Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || row[0].Equals("Authority carried forward", StringComparison.OrdinalIgnoreCase)));
        if (approved is null) return null;
        string Value(string name)
        {
            var index = headers.FindIndex(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? approved[index].Trim('`').ToLowerInvariant() : string.Empty;
        }
        var wireframe = Value("Wireframe SHA-256");
        if (wireframe.Length == 0)
        {
            var content = File.ReadAllText(_changes.DossierFile(source, "wireframes.md"));
            wireframe = ApprovedWireframeSha(content) ?? string.Empty;
        }
        var renderer = Value("Renderer SHA-256");
        var manifest = Value("PNG manifest SHA-256");
        return IsSha(wireframe) && IsSha(renderer) && IsSha(manifest)
            ? (wireframe, renderer, manifest)
            : null;
    }

    private static IReadOnlyList<DesignReuseRecord> ReadReuseRecords(string design)
    {
        var rows = MarkdownTable(SectionBody(design, "## Reused approved PNGs"));
        if (rows.Count < 2) return [];
        var result = new List<DesignReuseRecord>();
        foreach (var cells in rows.Skip(2))
        {
            if (cells.Count != 15 || cells[0].Equals("Target screen ID", StringComparison.OrdinalIgnoreCase)) continue;
            var dimensions = cells[8].Split('x', 2);
            if (dimensions.Length != 2
                || !int.TryParse(dimensions[0], out var width)
                || !int.TryParse(dimensions[1], out var height)) continue;
            result.Add(new DesignReuseRecord(
                cells[0].Trim('`'), cells[1].Trim('`'), cells[2].Trim('`'), cells[3].Trim('`'),
                cells[4].Trim('`'), cells[5].Trim('`'), cells[6].Trim('`'), width, height,
                cells[9].Trim('`').ToLowerInvariant(), cells[10].Trim('`').ToLowerInvariant(),
                cells[11].Trim('`').ToLowerInvariant(), cells[12].Trim('`').ToLowerInvariant(),
                cells[13].Trim('`').ToLowerInvariant(), cells[14]));
        }
        return result;
    }

    private static string ReuseSection(IReadOnlyList<DesignReuseRecord> records)
    {
        var rows = string.Join(Environment.NewLine, records.Select(item =>
            $"| `{item.TargetScreenId}` | `{item.FrontendType}` | `{item.SourceChangeId}` | `{item.SourceScreenId}` | `{item.State}` | `{item.Viewport}` | `{item.Path}` | Approved unchanged | {item.Width}x{item.Height} | `{item.Sha256}` | `{item.SourceWireframeSha256}` | `{item.SourceWireframeContentSha256}` | `{item.SourceRendererSha256}` | `{item.SourceManifestSha256}` | {Cell(item.Rationale)} |"));
        return $"""
## Reused approved PNGs

These exact PNGs remain owned by their approved source change. CIS verifies the source
approval digests and current file hashes before rendering or approving this pack.

| Target screen ID | Frontend type | Source change | Source screen ID | State | Viewport | Path | Compatibility | Dimensions | SHA-256 | Source wireframe approval SHA-256 | Source wireframe content SHA-256 | Source renderer SHA-256 | Source manifest SHA-256 | Reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
{rows}

""";
    }

    private static List<List<string>> MarkdownTable(string section)
        => section.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => line.TrimStart().StartsWith('|'))
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList())
            .ToList();

    private static bool SourceManifestContains(string design, DesignArtifact artifact)
        => SectionBody(design, "## PNG manifest").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Any(line => line.StartsWith($"| `{artifact.ScreenId}` |", StringComparison.Ordinal)
                && line.Contains($"| {artifact.State} | {artifact.Viewport} |", StringComparison.Ordinal)
                && line.Contains($"`{artifact.Path}`", StringComparison.Ordinal)
                && line.Contains($"`{artifact.Sha256}`", StringComparison.Ordinal));

    private static bool IsSha(string value)
        => Regex.IsMatch(value, "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? FindGuideline(ChangeDossier change)
    {
        var documentation = Path.Combine(change.RepositoryPath, change.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(documentation)) return null;
        return Directory.EnumerateFiles(documentation, "*.md", SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Take(30).Any(line => line.Trim().Equals("type: design-guidelines", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path.Contains($"{Path.DirectorySeparatorChar}templates{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? ResolveRenderer(ChangeDossier change, string design)
    {
        var match = Regex.Match(design, @"\| `[^`]+` \| `sha256:[^`]+` \| `[^`]+` \| `sha256:[^`]+` \| `(?<path>[^`]+\.mjs)` \|");
        if (match.Success) return Path.GetFullPath(Path.Combine(change.RepositoryPath, match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar)));
        var assets = Path.Combine(change.RepositoryPath, change.RelativePath.Replace('/', Path.DirectorySeparatorChar), "assets");
        return Directory.Exists(assets) ? Directory.EnumerateFiles(assets, "render-*-screens.mjs").SingleOrDefault() : null;
    }

    private static IReadOnlyList<DesignArtifact> ReadArtifacts(ChangeDossier change)
    {
        var dossier = Path.Combine(change.RepositoryPath, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var assets = Path.Combine(dossier, "assets");
        if (!Directory.Exists(assets)) return [];
        return Directory.EnumerateFiles(assets, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path).Split("--", StringSplitOptions.None);
                var (width, height) = PngDimensions(path);
                return new DesignArtifact(name.ElementAtOrDefault(0) ?? "screen", name.ElementAtOrDefault(1) ?? "primary",
                    name.ElementAtOrDefault(2) ?? "desktop", Relative(change.RepositoryPath, path), width, height,
                    Sha(File.ReadAllBytes(path)), new FileInfo(path).Length);
            }).ToArray();
    }

    private static (int Width, int Height) PngDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        if (stream.Read(header) != header.Length || !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return (0, 0);
        return (ReadBigEndian(header[16..20]), ReadBigEndian(header[20..24]));
    }

    private static int ReadBigEndian(ReadOnlySpan<byte> bytes)
        => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static string ReplaceSection(string content, string heading, string replacement)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return content.TrimEnd() + Environment.NewLine + Environment.NewLine + replacement.TrimEnd() + Environment.NewLine;
        var next = content.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        if (next < 0) next = content.Length;
        return content[..start] + replacement.TrimEnd() + Environment.NewLine + content[next..].TrimStart('\r', '\n');
    }

    private static string SectionBody(string content, string heading)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += heading.Length;
        var next = content.IndexOf("\n## ", start, StringComparison.Ordinal);
        if (next < 0) next = content.Length;
        return content[start..next].Trim();
    }

    private static string ScreenDefinitionBody(string content, string screenId)
    {
        var heading = $"### {screenId}:";
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += heading.Length;
        var nextScreen = content.IndexOf("\n### ", start, StringComparison.Ordinal);
        var nextSection = content.IndexOf("\n## ", start, StringComparison.Ordinal);
        var candidates = new[] { nextScreen, nextSection }.Where(index => index >= 0).ToArray();
        var end = candidates.Length == 0 ? content.Length : candidates.Min();
        return content[start..end].Trim();
    }

    private static string FrontMatter(string content, string key)
    {
        var match = Regex.Match(content, $@"(?m)^{Regex.Escape(key)}:\s*(?<value>.*)$");
        return match.Success ? match.Groups["value"].Value.Trim().Trim('"', '\'') : string.Empty;
    }

    private static string FrontMatter(string content, string key, string value)
        => Regex.Replace(content, $@"(?m)^{Regex.Escape(key)}:.*$", $"{key}: {value}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void Required(string source, string value, string message, ICollection<string> diagnostics)
    {
        if (!source.Contains(value, StringComparison.Ordinal)) diagnostics.Add("ERROR: " + message);
    }

    private static string ManifestSha(IReadOnlyList<DesignArtifact> artifacts)
        => Sha(Encoding.UTF8.GetBytes(string.Join('\n', artifacts.Select(item => $"{item.Path}|{item.Width}|{item.Height}|{item.Sha256}"))));
    private static string Sha(byte[] value) => "sha256:" + Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string WireframeBodySha(string content)
    {
        var index = content.IndexOf("## Approval decision", StringComparison.Ordinal);
        var body = index < 0 ? content : content[..index];
        // Workflow transitions must not invalidate the content they approve. The
        // approval command changes these two fields after computing the digest,
        // so normalize them to their review-state values before hashing.
        body = FrontMatter(body, "status", "ReadyForReview");
        body = FrontMatter(body, "approval_status", "NotReviewed");
        return Sha(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n"));
    }
    private static string? ApprovedWireframeSha(string content)
    {
        var match = Regex.Match(content,
            @"(?m)^\|\s*(?:Approved|Authority carried forward)\s*\|[^\r\n]*?`(?<sha>sha256:[0-9a-fA-F]{64})`[^\r\n]*\r?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["sha"].Value.ToLowerInvariant() : null;
    }
    private static string? PriorApprovedManifestSha(string content)
    {
        var match = Regex.Match(SectionBody(content, "## Approval decision"),
            @"(?m)^\|\s*(?:Approved|Authority carried forward)\s*\|[^\r\n]*?`sha256:[0-9a-fA-F]{64}`\s*\|\s*`sha256:[0-9a-fA-F]{64}`\s*\|\s*`(?<sha>sha256:[0-9a-fA-F]{64})`\s*\|",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["sha"].Value.ToLowerInvariant() : null;
    }
    private static string? CurrentApprovedWireframeSha(string content)
    {
        var approved = ApprovedWireframeSha(content);
        return approved is not null && approved.Equals(WireframeBodySha(content), StringComparison.OrdinalIgnoreCase)
            ? approved
            : null;
    }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Cell(string value) => value.Replace('|', '/').Replace("\r", " ").Replace("\n", " ").Trim();
    private static string Slug(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    private DesignResult Error(string? changeId, params string[] messages)
        => new("invalid", changeId, null, null, null, [], [], messages.Select(message => "ERROR: " + message).ToArray(), false);
}
