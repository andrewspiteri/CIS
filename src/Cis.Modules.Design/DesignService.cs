using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Modules.Change;

namespace Cis.Modules.Design;

public sealed class DesignService
{
    private readonly ChangeDossierStore _changes;
    private readonly DesignTemplateCatalog _templates;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IDesignProcessRunner _runner;

    public DesignService(
        ChangeDossierStore changes,
        DesignTemplateCatalog templates,
        Func<DateTimeOffset>? clock = null,
        IDesignProcessRunner? runner = null)
    {
        _changes = changes;
        _templates = templates;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _runner = runner ?? new NodeDesignProcessRunner();
    }

    public DesignResult Templates()
        => new("listed", null, null, null, null, _templates.List(), [], [], false);

    public DesignResult Scaffold(DesignScaffoldRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        var templateErrors = _templates.Validate(request.Shell, request.Components);
        if (templateErrors.Count > 0) return Error(change.Id, templateErrors.ToArray());

        var wireframePath = _changes.DossierFile(change, "wireframes.md");
        var wireframe = File.ReadAllText(wireframePath);
        if (!FrontMatter(wireframe, "approval_status").Equals("Approved", StringComparison.OrdinalIgnoreCase))
        {
            return Error(change.Id, "Textual wireframes require explicit approval before renderer scaffolding.");
        }
        var screens = ParseScreens(wireframe);
        if (screens.Count == 0)
        {
            return Error(change.Id, "wireframes.md requires at least one non-placeholder Screen inventory row.");
        }

        var guidelinePath = FindGuideline(change);
        if (guidelinePath is null) return Error(change.Id, "No governed design-guidelines document was found.");
        var relativeGuideline = Relative(change.RepositoryPath, guidelinePath);
        var guidelineSha = Sha(File.ReadAllBytes(guidelinePath));
        var wireframeSha = ApprovedWireframeSha(wireframe) ?? WireframeBodySha(wireframe);
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
        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
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
        var surfaceRows = string.Join(Environment.NewLine, screens
            .GroupBy(screen => screen.FrontendType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => FrontendTypeOrder(group.Key))
            .Select(group => $"| `{group.Key}` | {string.Join(", ", group.Select(screen => $"`{screen.Id}`"))} | `{request.Shell}` | Classified |"));
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
            ["guidelineSha256"] = guidelineSha,
            ["templates"] = string.Join(',', usedTemplates.Select(template => template.Id)),
        });
        var estimated = usedTemplates.Sum(template => template.EstimatedSavedTokens);
        return new("scaffolded", change.Id, "Inactive", "NotReviewed", rendererRelative,
            usedTemplates, [], [$"INFO: estimated template token savings={estimated}"], true);
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
        if (!FrontMatter(wireframes, "approval_status").Equals("Approved", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add("ERROR: Textual wireframes are not approved.");
        }
        var approvedWireframeSha = ApprovedWireframeSha(wireframes);
        if (approvedWireframeSha is null)
        {
            diagnostics.Add("ERROR: Approved textual wireframes must record their exact approval digest.");
        }
        else if (rendererPath is not null && File.Exists(rendererPath))
        {
            var renderer = File.ReadAllText(rendererPath);
            Required(renderer, $"wireframeSha256: \"{approvedWireframeSha}\"",
                "Renderer wireframe provenance must match the exact human-approved digest.", diagnostics);
            if (!design.Contains($"`{approvedWireframeSha}`", StringComparison.Ordinal))
                diagnostics.Add("ERROR: design.md wireframe provenance must match the exact human-approved digest.");
        }
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
        if (FrontMatter(design, "gate_status") is "PausedForReview" or "Approved" && artifacts.Count == 0)
        {
            diagnostics.Add("ERROR: Review or approval requires generated PNG artifacts.");
        }
        return new(diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? "invalid" : "valid",
            change.Id, FrontMatter(design, "gate_status"), FrontMatter(design, "approval_status"),
            rendererPath is null ? null : Relative(change.RepositoryPath, rendererPath), [], artifacts, diagnostics, false);
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
            && !item.Contains("PNG artifacts", StringComparison.Ordinal)).ToArray();
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
            if (artifacts.Count == 0) return Error(change.Id, "Renderer completed without producing PNG artifacts.");
            var designPath = _changes.DossierFile(change, "design.md");
            var design = File.ReadAllText(designPath);
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
                ["artifacts"] = artifacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["manifestSha256"] = ManifestSha(artifacts),
            });
            return new("ready-for-review", change.Id, "PausedForReview", "ReadyForReview",
                validation.RendererPath, [], artifacts, [$"INFO: {process.StandardOutput.Trim()}"], true);
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
        var designPath = _changes.DossierFile(change, "design.md");
        var design = File.ReadAllText(designPath);
        var rendererSha = Sha(File.ReadAllBytes(Path.Combine(change.RepositoryPath, validation.RendererPath!.Replace('/', Path.DirectorySeparatorChar))));
        design = ReplaceSection(design, "## Approval decision", $"""
## Approval decision

| Decision | Reviewer | Date | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- |
| Approved | {Cell(request.Reviewer)} | {_clock():yyyy-MM-ddTHH:mm:ssZ} | `{rendererSha}` | `{ManifestSha(validation.Artifacts)}` | {Cell(request.Rationale)} |
""");
        design = FrontMatter(design, "status", "Approved");
        design = FrontMatter(design, "approval_status", "Approved");
        design = FrontMatter(design, "gate_status", "Approved");
        File.WriteAllText(designPath, design);
        _changes.AppendEvent(change, "design-approved", new Dictionary<string, string>
        {
            ["reviewer"] = request.Reviewer.Trim(), ["rationale"] = request.Rationale.Trim(),
            ["rendererSha256"] = rendererSha, ["manifestSha256"] = ManifestSha(validation.Artifacts),
        });
        return validation with { Status = "approved", GateStatus = "Approved", ApprovalStatus = "Approved", Applied = true };
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
        var artifacts = ReadArtifacts(change);
        var hashes = string.Join(", ", artifacts.Select(item => item.Sha256));
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
        foreach (var artifact in artifacts)
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
        return new("shown", change.Id, FrontMatter(design, "gate_status"), FrontMatter(design, "approval_status"),
            renderer is null ? null : Relative(change.RepositoryPath, renderer), [], ReadArtifacts(change), [], false);
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
        => Regex.IsMatch(value, @"\b(?:TODO|TBD)\b|(?i:\bto be completed\b)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static int FrontendTypeOrder(string value) => value switch { "public" => 0, "customer" => 1, "backoffice" => 2, _ => 3 };

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
        return Sha(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n"));
    }
    private static string? ApprovedWireframeSha(string content)
    {
        var match = Regex.Match(content,
            @"(?m)^\|\s*Approved\s*\|[^\r\n]*?`(?<sha>sha256:[0-9a-fA-F]{64})`[^\r\n]*\r?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["sha"].Value.ToLowerInvariant() : null;
    }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Cell(string value) => value.Replace('|', '/').Replace("\r", " ").Replace("\n", " ").Trim();
    private static string Slug(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    private DesignResult Error(string? changeId, params string[] messages)
        => new("invalid", changeId, null, null, null, [], [], messages.Select(message => "ERROR: " + message).ToArray(), false);
}
