using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Graph;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

public sealed partial class BrdService
{
    private const string BaselineStart = "<!-- cis:baseline:start -->";
    private const string BaselineEnd = "<!-- cis:baseline:end -->";
    private const string SourcesStart = "<!-- cis:sources:start -->";
    private const string SourcesEnd = "<!-- cis:sources:end -->";
    private const string FeatureTraceabilityStart = "<!-- cis:feature-traceability:start -->";
    private const string FeatureTraceabilityEnd = "<!-- cis:feature-traceability:end -->";

    private static readonly string[] RequiredSections =
    [
        "Executive summary",
        "Business outcomes",
        "Scope",
        "Stakeholders and actors",
        "Business capabilities and processes",
        "Functional requirements",
        "Quality, regulatory, and operational requirements",
        "Constraints and assumptions",
        "Success measures",
        "Traceability",
        "Open questions",
    ];

    private static readonly string[] ExcludedSegments =
    [
        "/.git/",
        "/.cis/local/",
        "/.github/",
        "/bin/",
        "/obj/",
        "/node_modules/",
        "/.next/",
        "/dist/",
        "/build/",
    ];

    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly GraphValidator _graphValidator;
    private readonly ICisGraphSnapshotReader _graphReader;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly ICisWorkspaceRegistry _workspaceRegistry;

    public BrdService(
        ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver,
        GraphValidator graphValidator,
        ICisGraphSnapshotReader graphReader,
        DocumentationCatalogMerger catalogMerger,
        Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _graphValidator = graphValidator;
        _graphReader = graphReader;
        _catalogMerger = catalogMerger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public BrdDiscoveryResult Discover(string workspacePath)
    {
        var resolution = ResolveAuthority(workspacePath);
        if (resolution.Errors.Count > 0 || resolution.Workspace is null || resolution.Authority is null)
        {
            return new BrdDiscoveryResult(
                "invalid-workspace",
                null,
                null,
                null,
                [],
                [],
                [],
                resolution.Errors);
        }

        var workspace = resolution.Workspace;
        var authority = resolution.Authority;
        var canonicalPath = CanonicalPath(authority);
        var warnings = new List<string>();
        var errors = new List<string>();
        var baselines = new List<BrdRepositoryBaseline>();
        foreach (var repository in workspace.Repositories)
        {
            var validation = _graphValidator.Validate(repository.RepositoryPath, strict: false);
            if (!validation.RepositoryConfigurationValid || !validation.GraphAvailable)
            {
                errors.Add(
                    $"Repository '{repository.Id}' graph is unavailable. Run `cis graph build --workspace {workspace.WorkspacePath}`.");
                continue;
            }

            var snapshot = _graphReader.Read(repository.RepositoryPath);
            var graph = snapshot.Graph;
            if (graph is null)
            {
                errors.Add($"Repository '{repository.Id}' graph could not be read.");
                continue;
            }

            baselines.Add(new BrdRepositoryBaseline(
                repository.Id,
                repository.RepositoryPath,
                repository.Role,
                graph.Build.Id,
                graph.Build.Head,
                graph.Build.Dirty,
                validation.Freshness,
                graph.Build.Status));
            if (!string.Equals(validation.Freshness, "fresh", StringComparison.Ordinal))
            {
                var message = $"Repository '{repository.Id}' graph is {validation.Freshness}.";
                if (repository.Role == "authority")
                {
                    warnings.Add(message + " Rebuild it before producing downstream context packs.");
                }
                else
                {
                    errors.Add(message + " Rebuild the workspace before BRD approval.");
                }
            }

            warnings.AddRange(validation.Diagnostics
                .Where(diagnostic => diagnostic.Severity == "warning")
                .Select(diagnostic => $"[{repository.Id}] {diagnostic.Message}"));
        }

        var candidates = workspace.Repositories
            .SelectMany(repository => FindCandidates(repository, authority, canonicalPath, warnings))
            .OrderBy(candidate => candidate.RepositoryId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
        var status = candidates.Any(candidate => !candidate.Canonical)
            ? "review-required"
            : File.Exists(canonicalPath)
                ? "canonical-found"
                : "missing";
        return new BrdDiscoveryResult(
            errors.Count > 0 ? "incomplete" : status,
            workspace.WorkspacePath,
            authority.Id,
            NormalizePath(Path.GetRelativePath(authority.RepositoryPath, canonicalPath)),
            candidates,
            baselines,
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
            errors.Distinct(StringComparer.Ordinal).Order().ToArray());
    }

    public BrdResult Initialize(string workspacePath, string title)
    {
        var discovery = Discover(workspacePath);
        if (discovery.ExitCode != 0)
        {
            return Error("invalid", discovery, discovery.Errors);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Error("invalid", discovery, ["BRD title is required."]);
        }

        var workspace = _workspaceRegistry.Resolve(workspacePath).Workspace!;
        var authority = workspace.AuthorityRepository!;
        var contextResolution = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!contextResolution.IsSuccess || contextResolution.Context is null)
        {
            return Error("invalid", discovery, contextResolution.Errors);
        }

        var context = contextResolution.Context;
        var canonicalPath = CanonicalPath(authority);
        var relativePath = NormalizePath(Path.GetRelativePath(context.RepositoryPath, canonicalPath));
        var stableId = $"{authority.Id}:spec:business-requirements";
        var catalogContent = File.ReadAllText(context.CatalogPath);
        var catalogMerge = _catalogMerger.Merge(
            authority.Id,
            catalogContent,
            [new CatalogArtifactEntry(stableId, relativePath, "business-requirements", "review-required", "canonical")]);
        if (catalogMerge.Collisions.Count > 0)
        {
            return new BrdResult(
                "collision",
                discovery.WorkspacePath,
                authority.Id,
                relativePath,
                discovery,
                null,
                discovery.Warnings,
                catalogMerge.Collisions,
                Applied: false);
        }

        var participantBaselines = discovery.Baselines
            .Where(baseline => baseline.Role == "participant")
            .ToArray();
        var sourceCandidates = discovery.Candidates.Where(candidate => !candidate.Canonical).ToArray();
        string nextContent;
        if (!File.Exists(canonicalPath))
        {
            nextContent = RenderNew(
                title.Trim(),
                stableId,
                participantBaselines,
                sourceCandidates);
        }
        else
        {
            var current = File.ReadAllText(canonicalPath);
            if (!current.Contains($"stable_id: {stableId}", StringComparison.Ordinal)
                || !HasManagedBlocks(current))
            {
                return Error(
                    "collision",
                    discovery,
                    ["The canonical BRD path already contains an unmanaged document. Preserve it and migrate it explicitly before CIS reconciliation."]);
            }

            var upgraded = EnsureNestedFrontMatter(current, "approved_content_hash", "null");
            var approvalCanCarryForward = HasCurrentApproval(upgraded);
            nextContent = Reconcile(upgraded, participantBaselines, sourceCandidates);
            if (!Equivalent(current, upgraded))
            {
                nextContent = ClearApproval(nextContent);
            }
            else if (!Equivalent(upgraded, nextContent))
            {
                if (approvalCanCarryForward && SourceAssessmentsResolved(nextContent, sourceCandidates))
                {
                    nextContent = ReplaceNestedFrontMatter(
                        nextContent,
                        "approved_content_hash",
                        JsonSerializer.Serialize(ContentDigest(nextContent)));
                }
                else
                {
                    nextContent = ClearApproval(nextContent);
                }
            }
        }

        var documentChanged = !File.Exists(canonicalPath)
            || !Equivalent(File.ReadAllText(canonicalPath), nextContent);
        var nextCatalog = catalogMerge.Content;
        if (documentChanged)
        {
            var retainedApproval = string.Equals(
                    ReadFrontMatter(nextContent, "status"),
                    "Active",
                    StringComparison.OrdinalIgnoreCase)
                && HasCurrentApproval(nextContent);
            nextCatalog = UpdateCatalogStatus(
                nextCatalog,
                stableId,
                retainedApproval ? "active" : "review-required");
        }

        var catalogChanged = !Equivalent(catalogContent, nextCatalog);
        if (!documentChanged && !catalogChanged)
        {
            var validation = Validate(workspacePath).Validation;
            return new BrdResult(
                "unchanged",
                discovery.WorkspacePath,
                authority.Id,
                relativePath,
                discovery,
                validation,
                discovery.Warnings,
                [],
                Applied: false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
        Write(canonicalPath, nextContent);
        if (catalogChanged)
        {
            Write(context.CatalogPath, nextCatalog);
        }

        var postValidation = Validate(workspacePath).Validation;
        return new BrdResult(
            File.Exists(canonicalPath) && postValidation?.EffectiveStatus == "Active"
                ? "reconciled"
                : File.Exists(canonicalPath) ? "review-required" : "drafted",
            discovery.WorkspacePath,
            authority.Id,
            relativePath,
            discovery,
            postValidation,
            discovery.Warnings,
            [],
            Applied: true);
    }

    public BrdResult Reconcile(string workspacePath)
    {
        var discovery = Discover(workspacePath);
        if (discovery.ExitCode != 0)
        {
            return Error("invalid", discovery, discovery.Errors);
        }

        var workspace = _workspaceRegistry.Resolve(workspacePath).Workspace!;
        var authority = workspace.AuthorityRepository!;
        var path = CanonicalPath(authority);
        if (!File.Exists(path))
        {
            return ValidateInternal(workspacePath, "missing");
        }

        var title = ReadFrontMatter(File.ReadAllText(path), "title")
            ?? "Business Requirements Document";
        return Initialize(workspacePath, title);
    }

    public BrdResult Status(string workspacePath) => ValidateInternal(workspacePath, "status");

    public BrdResult Validate(string workspacePath) => ValidateInternal(workspacePath, "validated");

    public BrdResult Approve(
        string workspacePath,
        string reviewer,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
        {
            return new BrdResult(
                "invalid",
                workspacePath,
                null,
                null,
                null,
                null,
                [],
                ["Reviewer and approval reason are required."],
                Applied: false);
        }

        var assessed = Validate(workspacePath);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true })
        {
            return assessed with { Status = "blocked" };
        }

        var workspace = _workspaceRegistry.Resolve(workspacePath).Workspace!;
        var authority = workspace.AuthorityRepository!;
        var context = _repositoryResolver.Resolve(authority.RepositoryPath).Context!;
        var path = CanonicalPath(authority);
        var content = File.ReadAllText(path);
        if (assessed.Validation.Current
            && string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadNestedFrontMatter(content, "approved_by"), reviewer.Trim(), StringComparison.Ordinal)
            && string.Equals(ReadNestedFrontMatter(content, "approval_reason"), reason.Trim(), StringComparison.Ordinal))
        {
            return assessed with { Status = "unchanged" };
        }

        var now = _clock();
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNestedFrontMatter(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNestedFrontMatter(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        content = EnsureNestedFrontMatter(content, "approved_content_hash", "null");
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
        var participants = assessed.Discovery!.Baselines
            .Where(baseline => baseline.Role == "participant")
            .ToArray();
        content = ReplaceBlock(content, BaselineStart, BaselineEnd, RenderBaselines(participants));
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
        Write(path, content);
        var stableId = $"{authority.Id}:spec:business-requirements";
        var catalog = UpdateCatalogStatus(File.ReadAllText(context.CatalogPath), stableId, "active");
        Write(context.CatalogPath, catalog);
        var validation = Validate(workspacePath).Validation;
        return new BrdResult(
            "approved",
            workspace.WorkspacePath,
            authority.Id,
            NormalizePath(Path.GetRelativePath(authority.RepositoryPath, path)),
            assessed.Discovery,
            validation,
            assessed.Warnings,
            [],
            Applied: true);
    }

    private BrdResult ValidateInternal(string workspacePath, string operation)
    {
        var discovery = Discover(workspacePath);
        if (discovery.ExitCode != 0)
        {
            return Error("invalid", discovery, discovery.Errors);
        }

        var workspace = _workspaceRegistry.Resolve(workspacePath).Workspace!;
        var authority = workspace.AuthorityRepository!;
        var path = CanonicalPath(authority);
        var relativePath = NormalizePath(Path.GetRelativePath(authority.RepositoryPath, path));
        if (!File.Exists(path))
        {
            return new BrdResult(
                "missing",
                workspace.WorkspacePath,
                authority.Id,
                relativePath,
                discovery,
                new BrdValidation(false, false, "Missing", "Missing", ["Canonical BRD was not found. Run `cis brd init`."], []),
                discovery.Warnings,
                [],
                Applied: false);
        }

        var content = File.ReadAllText(path);
        var errors = new List<string>();
        var warnings = new List<string>(discovery.Warnings);
        var stableId = $"{authority.Id}:spec:business-requirements";
        if (!content.Contains($"stable_id: {stableId}", StringComparison.Ordinal))
        {
            errors.Add("Canonical BRD stable identity is missing or incorrect.");
        }

        if (!HasManagedBlocks(content))
        {
            errors.Add("Canonical BRD managed baseline or source-assessment block is missing.");
        }

        foreach (var section in RequiredSections)
        {
            var sectionContent = ExtractSection(content, section);
            if (string.IsNullOrWhiteSpace(sectionContent))
            {
                errors.Add($"Required BRD section is missing or empty: {section}");
            }
            else if (PlaceholderPattern().IsMatch(sectionContent))
            {
                errors.Add($"Required BRD section still contains a placeholder: {section}");
            }
        }

        var sources = ParseSourceRows(content);
        foreach (var candidate in discovery.Candidates.Where(candidate => !candidate.Canonical))
        {
            if (!sources.TryGetValue(candidate.Id, out var row))
            {
                errors.Add($"Discovered BRD candidate is not assessed: {candidate.RepositoryId}/{candidate.Path}");
                continue;
            }

            if (!string.Equals(row.Hash, candidate.ContentHash, StringComparison.Ordinal))
            {
                errors.Add($"BRD candidate changed after assessment: {candidate.RepositoryId}/{candidate.Path}");
            }

            if (row.Assessment is not ("Adopted" or "Reference" or "Rejected"))
            {
                errors.Add($"BRD candidate requires Adopted, Reference, or Rejected assessment: {candidate.Id}");
            }

            if (string.IsNullOrWhiteSpace(row.Rationale) || PlaceholderPattern().IsMatch(row.Rationale))
            {
                errors.Add($"BRD source assessment requires rationale: {candidate.Id}");
            }

            if (candidate.Kind == "feature-specification"
                && row.Assessment == "Adopted"
                && !ExtractSection(content, "Traceability").Contains(candidate.Id, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Adopted feature specification must be referenced by source ID in BRD Traceability: {candidate.Id}");
            }
        }

        var baselines = ParseBaselineRows(content);
        var current = true;
        foreach (var baseline in discovery.Baselines.Where(baseline => baseline.Role == "participant"))
        {
            if (!baselines.TryGetValue(baseline.RepositoryId, out var recorded)
                || !string.Equals(recorded, baseline.BuildId, StringComparison.Ordinal))
            {
                warnings.Add($"BRD baseline differs from repository '{baseline.RepositoryId}'. Human review is required.");
                current = false;
            }
        }

        foreach (var recorded in baselines.Keys.Except(
                     discovery.Baselines.Where(baseline => baseline.Role == "participant")
                         .Select(baseline => baseline.RepositoryId),
                     StringComparer.Ordinal))
        {
            warnings.Add($"BRD baseline references repository no longer registered in the workspace: {recorded}");
            current = false;
        }

        var documentStatus = ReadFrontMatter(content, "status") ?? "Unknown";
        if (string.Equals(documentStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var approvedHash = ReadNestedFrontMatter(content, "approved_content_hash");
            if (string.IsNullOrWhiteSpace(approvedHash)
                || string.Equals(approvedHash, "null", StringComparison.OrdinalIgnoreCase)
                || !ApprovalDigestMatches(content, approvedHash))
            {
                warnings.Add("BRD content changed after approval. Human review is required.");
                current = false;
            }
        }
        var valid = errors.Count == 0;
        var effectiveStatus = string.Equals(documentStatus, "Active", StringComparison.OrdinalIgnoreCase)
            ? valid && current ? "Active" : "Stale"
            : valid ? "Ready for Approval" : "Review Required";
        var validation = new BrdValidation(
            valid,
            current,
            effectiveStatus,
            documentStatus,
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray());
        return new BrdResult(
            operation,
            workspace.WorkspacePath,
            authority.Id,
            relativePath,
            discovery,
            validation,
            validation.Warnings,
            [],
            Applied: false);
    }

    private static string RenderNew(
        string title,
        string stableId,
        IReadOnlyList<BrdRepositoryBaseline> baselines,
        IReadOnlyList<BrdCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(title)}");
        builder.AppendLine("type: business-requirements");
        builder.AppendLine("status: Review Required");
        builder.AppendLine("owner: Business and product stakeholders");
        builder.AppendLine("last_reviewed: null");
        builder.AppendLine("review_cadence: on material business or repository change");
        builder.AppendLine("cis:");
        builder.AppendLine($"  stable_id: {stableId}");
        builder.AppendLine("  brd_schema: 1");
        builder.AppendLine("  approved_by: null");
        builder.AppendLine("  approved_at: null");
        builder.AppendLine("  approval_reason: null");
        builder.AppendLine("  approved_content_hash: null");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine("This canonical BRD belongs to the workspace authority repository. Discovered BRDs and development feature specifications are source evidence, not automatically current authority.");
        builder.AppendLine();
        builder.AppendLine("## CIS participant baseline");
        builder.AppendLine();
        builder.AppendLine(BaselineStart);
        builder.Append(RenderBaselines(baselines));
        builder.AppendLine(BaselineEnd);
        builder.AppendLine();
        builder.AppendLine("## Source assessment");
        builder.AppendLine();
        builder.AppendLine(SourcesStart);
        builder.Append(RenderSources(
            candidates,
            new Dictionary<string, SourceRow>(StringComparer.Ordinal)));
        builder.AppendLine(SourcesEnd);
        builder.AppendLine();
        foreach (var section in RequiredSections)
        {
            builder.AppendLine($"## {section}");
            builder.AppendLine();
            builder.AppendLine($"TODO: Complete {section.ToLowerInvariant()} through human review of workspace evidence.");
            builder.AppendLine();
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Reconcile(
        string content,
        IReadOnlyList<BrdRepositoryBaseline> baselines,
        IReadOnlyList<BrdCandidate> candidates)
    {
        var existingRows = ParseSourceRows(content);
        var next = ReplaceBlock(content, BaselineStart, BaselineEnd, RenderBaselines(baselines));
        next = ReplaceBlock(next, SourcesStart, SourcesEnd, RenderSources(candidates, existingRows));
        next = ReconcileFeatureTraceability(next, candidates);
        return next;
    }

    private static string ClearApproval(string content)
    {
        content = ReplaceFrontMatter(content, "status", "Review Required");
        content = ReplaceFrontMatter(content, "last_reviewed", "null");
        content = ReplaceNestedFrontMatter(content, "approved_by", "null");
        content = ReplaceNestedFrontMatter(content, "approved_at", "null");
        content = ReplaceNestedFrontMatter(content, "approval_reason", "null");
        return ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
    }

    private static bool HasCurrentApproval(string content)
    {
        var approvedHash = ReadNestedFrontMatter(content, "approved_content_hash");
        return string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_by"))
            && ReadNestedFrontMatter(content, "approved_by") != "null"
            && !string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_at"))
            && ReadNestedFrontMatter(content, "approved_at") != "null"
            && !string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approval_reason"))
            && ReadNestedFrontMatter(content, "approval_reason") != "null"
            && !string.IsNullOrWhiteSpace(approvedHash)
            && approvedHash != "null"
            && ApprovalDigestMatches(content, approvedHash);
    }

    private static bool SourceAssessmentsResolved(
        string content,
        IReadOnlyList<BrdCandidate> candidates)
    {
        var rows = ParseSourceRows(content);
        return candidates.All(candidate => rows.TryGetValue(candidate.Id, out var row)
            && string.Equals(row.Hash, candidate.ContentHash, StringComparison.Ordinal)
            && row.Assessment is "Adopted" or "Reference" or "Rejected"
            && !string.IsNullOrWhiteSpace(row.Rationale)
            && !PlaceholderPattern().IsMatch(row.Rationale));
    }

    private static string RenderBaselines(IReadOnlyList<BrdRepositoryBaseline> baselines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Repository | Graph build | Head | Dirty |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var baseline in baselines.OrderBy(item => item.RepositoryId, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"| {Cell(baseline.RepositoryId)} | {Cell(baseline.BuildId ?? "unavailable")} | " +
                $"{Cell(baseline.Head ?? "uncommitted")} | {baseline.Dirty.ToString().ToLowerInvariant()} |");
        }

        if (baselines.Count == 0)
        {
            builder.AppendLine("| none | none | none | false |");
        }

        return builder.ToString();
    }

    private static string RenderSources(
        IReadOnlyList<BrdCandidate> candidates,
        IReadOnlyDictionary<string, SourceRow> existing)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| ID | Type | Repository | Path | Hash | Assessment | Rationale |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var candidate in candidates.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var assessment = "Unreviewed";
            var rationale = "TODO";
            if (existing.TryGetValue(candidate.Id, out var row)
                && string.Equals(row.Hash, candidate.ContentHash, StringComparison.Ordinal))
            {
                assessment = row.Assessment;
                rationale = row.Rationale;
            }

            if (assessment == "Unreviewed"
                && TryGetApprovedFeatureAssessment(candidate, out var approvedFeature))
            {
                assessment = approvedFeature.Assessment;
                rationale = approvedFeature.Rationale;
            }

            builder.AppendLine(
                $"| {candidate.Id} | {Cell(candidate.Kind)} | {Cell(candidate.RepositoryId)} | {Cell(candidate.Path)} | " +
                $"{candidate.ContentHash} | {Cell(assessment)} | {Cell(rationale)} |");
        }

        if (candidates.Count == 0)
        {
            builder.AppendLine("| none | none | none | No BRD or feature-specification evidence was discovered | none | Not Applicable | No source evidence exists |");
        }

        return builder.ToString();
    }

    private static bool TryGetApprovedFeatureAssessment(BrdCandidate candidate, out SourceRow assessment)
    {
        assessment = new SourceRow(candidate.ContentHash, "Unreviewed", "TODO");
        if (candidate.Kind != "feature-specification")
        {
            return false;
        }

        var path = Path.Combine(
            candidate.RepositoryPath,
            candidate.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return false;
        }

        var content = File.ReadAllText(path);
        var reviewer = ReadNestedFrontMatter(content, "approved_by");
        var reason = ReadNestedFrontMatter(content, "approval_reason");
        var digest = ReadNestedFrontMatter(content, "approved_content_hash");
        if (!string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(reviewer) || reviewer == "null"
            || string.IsNullOrWhiteSpace(reason) || reason == "null"
            || string.IsNullOrWhiteSpace(digest) || digest == "null"
            || !string.Equals(digest, ApprovalContentDigest(content), StringComparison.Ordinal))
        {
            return false;
        }

        assessment = new SourceRow(
            candidate.ContentHash,
            "Adopted",
            $"Approved by {reviewer}: {reason}");
        return true;
    }

    private static string ReconcileFeatureTraceability(
        string content,
        IReadOnlyList<BrdCandidate> candidates)
    {
        var sources = ParseSourceRows(content);
        var adopted = candidates
            .Where(candidate => candidate.Kind == "feature-specification"
                && sources.TryGetValue(candidate.Id, out var row)
                && row.Assessment == "Adopted")
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
        var rendered = string.Join('\n', adopted.Select(candidate =>
            $"- {candidate.Id}: adopted feature specification `{candidate.RepositoryId}/{candidate.Path}`; " +
            "detailed scope remains governed by its recorded approval."));

        if (content.Contains(FeatureTraceabilityStart, StringComparison.Ordinal)
            && content.Contains(FeatureTraceabilityEnd, StringComparison.Ordinal))
        {
            return ReplaceBlock(content, FeatureTraceabilityStart, FeatureTraceabilityEnd, rendered);
        }

        if (adopted.Length == 0)
        {
            return content;
        }

        const string heading = "## Traceability";
        var headingIndex = content.IndexOf(heading, StringComparison.Ordinal);
        if (headingIndex < 0)
        {
            return content;
        }

        var lineEnd = content.IndexOf('\n', headingIndex + heading.Length);
        var insertionPoint = lineEnd < 0 ? content.Length : lineEnd + 1;
        var block = $"\n{FeatureTraceabilityStart}\n{rendered}\n{FeatureTraceabilityEnd}\n";
        return content.Insert(insertionPoint, block);
    }

    private static string ApprovalContentDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
        {
            normalized = Regex.Replace(
                normalized,
                $"(?m)^{key}:.*$",
                $"{key}: <approval-metadata>",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
        {
            normalized = Regex.Replace(
                normalized,
                $"(?m)^  {key}:.*$",
                $"  {key}: <approval-metadata>",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        return Hash(normalized.TrimEnd());
    }

    private IEnumerable<BrdCandidate> FindCandidates(
        CisWorkspaceRepository repository,
        CisWorkspaceRepository authority,
        string canonicalPath,
        ICollection<string> warnings)
    {
        var inspected = 0;
        foreach (var path in Directory.EnumerateFiles(repository.RepositoryPath, "*.md", SearchOption.AllDirectories))
        {
            var normalizedAbsolute = "/" + NormalizePath(path).TrimStart('/') + "/";
            if (ExcludedSegments.Any(segment => normalizedAbsolute.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            inspected++;
            if (inspected > 10_000)
            {
                warnings.Add($"BRD discovery stopped after 10000 Markdown files in repository '{repository.Id}'.");
                yield break;
            }

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not inspect BRD candidate '{path}': {exception.Message}");
                continue;
            }

            var relativePath = NormalizePath(Path.GetRelativePath(repository.RepositoryPath, path));
            var evidence = DetectCandidateEvidence(relativePath, content);
            if (evidence.Kind == "feature-specification"
                && !string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "high_level_item"))
                && !IsCurrentApprovedFeature(content))
            {
                continue;
            }
            var isCanonical = repository.Id == authority.Id
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(canonicalPath), PathComparison);
            if (evidence.Signals.Count == 0 && !isCanonical)
            {
                continue;
            }

            yield return new BrdCandidate(
                CandidateId(repository.Id, relativePath),
                isCanonical ? "business-requirements" : evidence.Kind!,
                repository.Id,
                repository.RepositoryPath,
                relativePath,
                Hash(content),
                evidence.Signals.Count == 0 ? ["canonical-path"] : evidence.Signals,
                isCanonical);
        }
    }

    private static CandidateEvidence DetectCandidateEvidence(string path, string content)
    {
        var signals = new List<string>();
        var name = Path.GetFileNameWithoutExtension(path);
        if (BrdNamePattern().IsMatch(name))
        {
            signals.Add("file-name");
        }

        var prefix = content[..Math.Min(content.Length, 128_000)];
        if (BusinessRequirementsHeadingPattern().IsMatch(prefix))
        {
            signals.Add("business-requirements-heading");
        }

        if (BusinessRequirementsTypePattern().IsMatch(prefix))
        {
            signals.Add("business-requirements-type");
        }

        if (signals.Count > 0)
        {
            return new CandidateEvidence("business-requirements", signals);
        }

        if (TemplateTypePattern().IsMatch(prefix))
        {
            return new CandidateEvidence(null, []);
        }

        if (GameDesignDocumentNamePattern().IsMatch(name))
        {
            signals.Add("game-design-document-file-name");
        }

        if (GameDesignDocumentHeadingPattern().IsMatch(prefix))
        {
            signals.Add("game-design-document-heading");
        }

        if (ProductDesignTypePattern().IsMatch(prefix))
        {
            signals.Add("product-design-type");
        }

        if (signals.Count > 0)
        {
            return new CandidateEvidence("product-design", signals);
        }

        if (FeatureSpecificationNamePattern().IsMatch(name))
        {
            signals.Add("feature-specification-file-name");
        }

        if (FeatureSpecificationHeadingPattern().IsMatch(prefix))
        {
            signals.Add("feature-specification-heading");
        }

        if (FeatureSpecificationTypePattern().IsMatch(prefix))
        {
            signals.Add("feature-specification-type");
        }

        return signals.Count == 0
            ? new CandidateEvidence(null, [])
            : new CandidateEvidence("feature-specification", signals);
    }

    private static bool IsCurrentApprovedFeature(string content)
    {
        var digest = ReadNestedFrontMatter(content, "approved_content_hash");
        return string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_by"))
            && ReadNestedFrontMatter(content, "approved_by") != "null"
            && !string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approval_reason"))
            && ReadNestedFrontMatter(content, "approval_reason") != "null"
            && !string.IsNullOrWhiteSpace(digest)
            && digest != "null"
            && string.Equals(digest, ApprovalContentDigest(content), StringComparison.Ordinal);
    }

    private static Dictionary<string, SourceRow> ParseSourceRows(string content)
    {
        var rows = new Dictionary<string, SourceRow>(StringComparer.Ordinal);
        var block = ReadBlock(content, SourcesStart, SourcesEnd);
        foreach (var line in block.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length is not (6 or 7) || !cells[0].StartsWith("BRD-SRC-", StringComparison.Ordinal))
            {
                continue;
            }

            rows[cells[0]] = cells.Length == 7
                ? new SourceRow(cells[4], cells[5], cells[6])
                : new SourceRow(cells[3], cells[4], cells[5]);
        }

        return rows;
    }

    private static Dictionary<string, string> ParseBaselineRows(string content)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var block = ReadBlock(content, BaselineStart, BaselineEnd);
        foreach (var line in block.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length == 4 && cells[0] is not ("Repository" or "---" or "none"))
            {
                rows[cells[0]] = cells[1];
            }
        }

        return rows;
    }

    private static string[] Cells(string line)
        => !line.TrimStart().StartsWith('|')
            ? []
            : line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();

    private static string ExtractSection(string content, string heading)
    {
        var match = Regex.Match(
            content,
            $"(?ms)^## {Regex.Escape(heading)}\\s*$\\n(?<body>.*?)(?=^## |\\z)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
    }

    private static string ReplaceBlock(string content, string start, string end, string replacement)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex)
        {
            return content;
        }

        var replacementStart = startIndex + start.Length;
        return content[..replacementStart] + "\n" + replacement.TrimEnd() + "\n" + content[endIndex..];
    }

    private static string ReadBlock(string content, string start, string end)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        return startIndex < 0 || endIndex < startIndex
            ? string.Empty
            : content[(startIndex + start.Length)..endIndex].Trim();
    }

    private static bool HasManagedBlocks(string content)
        => content.Contains(BaselineStart, StringComparison.Ordinal)
            && content.Contains(BaselineEnd, StringComparison.Ordinal)
            && content.Contains(SourcesStart, StringComparison.Ordinal)
            && content.Contains(SourcesEnd, StringComparison.Ordinal);

    private static string ReplaceFrontMatter(string content, string key, string value)
        => Regex.Replace(
            content,
            $"(?m)^{Regex.Escape(key)}:.*$",
            $"{key}: {value}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static string ReplaceNestedFrontMatter(string content, string key, string value)
        => Regex.Replace(
            content,
            $"(?m)^  {Regex.Escape(key)}:.*$",
            $"  {key}: {value}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static string EnsureNestedFrontMatter(string content, string key, string value)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0)
        {
            return content;
        }
        var prefix = content[..end];
        if (Regex.IsMatch(prefix, $"(?m)^  {Regex.Escape(key)}:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            return content;
        }
        var cisIndex = prefix.IndexOf("\ncis:\n", StringComparison.Ordinal);
        if (cisIndex < 0)
        {
            return content;
        }
        return content.Insert(end, $"\n  {key}: {value}");
    }

    private static string? ReadFrontMatter(string content, string key)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0)
        {
            return null;
        }

        var match = Regex.Match(
            content[..end],
            $"(?m)^{Regex.Escape(key)}:\\s*(?<value>.+)$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["value"].Value.Trim().Trim('"') : null;
    }

    private static string? ReadNestedFrontMatter(string content, string key)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0)
        {
            return null;
        }

        var match = Regex.Match(
            content[..end],
            $"(?m)^  {Regex.Escape(key)}:\\s*(?<value>.+)$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["value"].Value.Trim().Trim('"') : null;
    }

    private static string UpdateCatalogStatus(string catalog, string id, string status)
    {
        var lines = catalog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index].Trim(), $"- id: {id}", StringComparison.Ordinal))
            {
                continue;
            }

            for (var entryIndex = index + 1; entryIndex < lines.Length; entryIndex++)
            {
                if (lines[entryIndex].StartsWith("  - id:", StringComparison.Ordinal))
                {
                    break;
                }

                if (lines[entryIndex].TrimStart().StartsWith("status:", StringComparison.Ordinal))
                {
                    lines[entryIndex] = "    status: " + status;
                    return string.Join('\n', lines);
                }
            }
        }

        return catalog;
    }

    private AuthorityResolution ResolveAuthority(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
        {
            return new AuthorityResolution(null, null, resolution.Errors);
        }

        var authority = resolution.Workspace.AuthorityRepository;
        return authority is null
            ? new AuthorityResolution(
                resolution.Workspace,
                null,
                ["Workspace has no authority repository. Run `cis workspace init` in the documentation repository."])
            : new AuthorityResolution(resolution.Workspace, authority, []);
    }

    private static string CanonicalPath(CisWorkspaceRepository authority)
        => Path.Combine(
            authority.RepositoryPath,
            authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar),
            "specs",
            "business-requirements.md");

    private static string CandidateId(string repositoryId, string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(repositoryId + "\u001f" + path));
        return "BRD-SRC-" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string Hash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string ContentDigest(string content) => BrdDocumentDigest.Compute(content);

    private static bool ApprovalDigestMatches(string content, string approvedHash)
        => string.Equals(approvedHash, ContentDigest(content), StringComparison.Ordinal)
            || string.Equals(approvedHash, LegacyContentDigest(content), StringComparison.Ordinal);

    private static string LegacyContentDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
        {
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
        {
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        return Hash(normalized.TrimEnd());
    }

    private static string RemoveManagedBlock(string content, string start, string end)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex)
        {
            return content;
        }

        var removalEnd = endIndex + end.Length;
        while (removalEnd < content.Length && content[removalEnd] == '\n')
        {
            removalEnd++;
        }
        return content.Remove(startIndex, removalEnd - startIndex);
    }

    private static string Cell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static void Write(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool Equivalent(string left, string right)
        => string.Equals(
            left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            StringComparison.Ordinal);

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static BrdResult Error(
        string status,
        BrdDiscoveryResult discovery,
        IReadOnlyList<string> errors)
        => new(
            status,
            discovery.WorkspacePath,
            discovery.AuthorityRepositoryId,
            discovery.CanonicalPath,
            discovery,
            null,
            discovery.Warnings,
            errors,
            Applied: false);

    private sealed record AuthorityResolution(
        CisWorkspace? Workspace,
        CisWorkspaceRepository? Authority,
        IReadOnlyList<string> Errors);

    private sealed record SourceRow(string Hash, string Assessment, string Rationale);

    private sealed record CandidateEvidence(string? Kind, IReadOnlyList<string> Signals);

    [GeneratedRegex("(?i)(^|[-_. ])brd($|[-_. ])|business[-_. ]?requirements?")]
    private static partial Regex BrdNamePattern();

    [GeneratedRegex("(?im)^#{1,3}\\s+.*business requirements?(?: document)?\\s*$")]
    private static partial Regex BusinessRequirementsHeadingPattern();

    [GeneratedRegex("(?im)^type:\\s*['\"]?business-requirements?['\"]?\\s*$")]
    private static partial Regex BusinessRequirementsTypePattern();

    [GeneratedRegex("(?i)(^|[-_. ])feature[-_. ]?(?:spec|specification)($|[-_. ])")]
    private static partial Regex FeatureSpecificationNamePattern();

    [GeneratedRegex("(?im)^#{1,3}\\s+.*feature specification\\s*$")]
    private static partial Regex FeatureSpecificationHeadingPattern();

    [GeneratedRegex("(?im)^type:\\s*['\"]?feature-(?:spec|specification)['\"]?\\s*$")]
    private static partial Regex FeatureSpecificationTypePattern();

    [GeneratedRegex("(?im)^type:\\s*['\"]?template['\"]?\\s*$")]
    private static partial Regex TemplateTypePattern();

    [GeneratedRegex("(?i)(^|[-_. ()])gdd($|[-_. ()])|game[-_. ]?design[-_. ]?document")]
    private static partial Regex GameDesignDocumentNamePattern();

    [GeneratedRegex("(?im)^#{1,3}\\s+.*(?:game design document|gdd(?:\\s|$)).*$")]
    private static partial Regex GameDesignDocumentHeadingPattern();

    [GeneratedRegex("(?im)^type:\\s*['\"]?product-design['\"]?\\s*$")]
    private static partial Regex ProductDesignTypePattern();

    [GeneratedRegex("\\b(?:TODO|TBD)\\b|(?i:\\bTO BE COMPLETED\\b)")]
    private static partial Regex PlaceholderPattern();
}
