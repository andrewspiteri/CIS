using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

public sealed class BrdReviewDispositionService : ICisBrdReviewFreshness
{
    private readonly ICisWorkspaceRegistry _workspaceRegistry;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;

    public BrdReviewDispositionService(ICisWorkspaceRegistry workspaceRegistry,
        DocumentationCatalogMerger? catalogMerger = null, Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _catalogMerger = catalogMerger ?? new DocumentationCatalogMerger();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public BrdReviewDispositionService(ICisWorkspaceRegistry workspaceRegistry, Func<DateTimeOffset> clock)
        : this(workspaceRegistry, null, clock)
    {
    }

    public BrdReviewDispositionResult Initialize(string workspacePath, string runId)
    {
        var resolved = Resolve(workspacePath);
        if (resolved.Errors.Count > 0) return Failure(workspacePath, resolved.Errors);
        if (!SafeRunId(runId)) return Failure(resolved.Workspace!.WorkspacePath, ["Review run ID is unsafe."]);
        var paths = Paths(resolved.Workspace!, runId);
        if (File.Exists(paths.Canonical))
        {
            var catalogErrors = EnsureCatalog(resolved.Workspace!, paths);
            if (catalogErrors.Count > 0) return Failure(resolved.Workspace!.WorkspacePath, catalogErrors, paths.Canonical);
            return ReadExisting(resolved.Workspace!, paths, validateSources: true);
        }

        var source = ReadReview(paths, runId);
        if (source.Errors.Count > 0) return Failure(resolved.Workspace!.WorkspacePath, source.Errors, paths.Canonical);
        if (!File.Exists(paths.Brd) || !ShaFile(paths.Brd).Equals(source.BrdSha256, StringComparison.OrdinalIgnoreCase))
            return Failure(resolved.Workspace!.WorkspacePath,
                [ReviewDriftMessage(paths, "The selected review is not bound to the current BRD.")], paths.Canonical);
        var document = new CisBrdReviewDispositionDocument(2, runId, source.ResultSha256!, source.BrdSha256!,
            source.Provider!, UtcNow(), source.Findings!);
        var errors = EnsureCatalog(resolved.Workspace!, paths);
        if (errors.Count > 0) return Failure(resolved.Workspace!.WorkspacePath, errors, paths.Canonical);
        WriteAtomic(paths.Canonical, CisBrdReviewDispositionCodec.Render(document));
        return New("review-required", resolved.Workspace!, paths.Canonical, document, [], true);
    }

    public BrdReviewDispositionResult Status(string workspacePath, string runId)
    {
        var resolved = Resolve(workspacePath);
        if (resolved.Errors.Count > 0) return Failure(workspacePath, resolved.Errors);
        if (!SafeRunId(runId)) return Failure(resolved.Workspace!.WorkspacePath, ["Review run ID is unsafe."]);
        return ReadExisting(resolved.Workspace!, Paths(resolved.Workspace!, runId), validateSources: true);
    }

    public CisBrdReviewFreshness Freshness(string workspacePath, string runId)
    {
        var resolved = Resolve(workspacePath);
        if (resolved.Errors.Count > 0)
            return new("invalid-workspace", workspacePath, runId, null, null, null, false, resolved.Errors);
        var workspace = resolved.Workspace!;
        if (!SafeRunId(runId))
            return new("invalid", workspace.WorkspacePath, runId, null, null, null, false,
                ["Review run ID is unsafe."]);
        var paths = Paths(workspace, runId);
        var relative = Path.GetRelativePath(workspace.AuthorityRepository!.RepositoryPath, paths.Brd)
            .Replace('\\', '/');
        var errors = new List<string>();
        if (!File.Exists(paths.Manifest) || !File.Exists(paths.Result) || !File.Exists(paths.ReviewedBrd))
            errors.Add("The successful BRD review baseline is incomplete.");
        if (!File.Exists(paths.Brd)) errors.Add("The canonical BRD was not found.");
        string? reviewedSha = null; string? currentSha = null;
        if (errors.Count == 0)
        {
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(paths.Manifest));
                using var result = JsonDocument.Parse(File.ReadAllText(paths.Result));
                var manifestRoot = manifest.RootElement; var resultRoot = result.RootElement;
                if (String(manifestRoot, "taskId") != "BRD-REVIEW"
                    || !String(manifestRoot, "status").Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
                    || !String(resultRoot, "status").Equals("Succeeded", StringComparison.OrdinalIgnoreCase))
                    errors.Add("The selected run is not a successful BRD review.");
                if (!resultRoot.TryGetProperty("review", out var review) || review.ValueKind != JsonValueKind.Object)
                    errors.Add("The selected run has no structured BRD review result.");
                reviewedSha = ShaFile(paths.ReviewedBrd); currentSha = ShaFile(paths.Brd);
                if (!String(manifestRoot, "taskDigest").Equals(reviewedSha, StringComparison.OrdinalIgnoreCase))
                    errors.Add("The retained BRD review baseline does not match the run manifest.");
            }
            catch (JsonException exception)
            {
                errors.Add("BRD review evidence is malformed: " + exception.Message);
            }
        }
        if (errors.Count > 0)
            return new("invalid", workspace.WorkspacePath, runId, relative, reviewedSha, currentSha, false, errors);
        if (reviewedSha!.Equals(currentSha, StringComparison.OrdinalIgnoreCase))
            return new("current", workspace.WorkspacePath, runId, relative, reviewedSha, currentSha, true, []);
        var reviewed = File.ReadAllText(paths.ReviewedBrd);
        var current = File.ReadAllText(paths.Brd);
        var compatible = string.Equals(BrdService.IndependentReviewSurface(reviewed),
            BrdService.IndependentReviewSurface(current), StringComparison.Ordinal);
        return new(compatible ? "question-answers-only" : "stale", workspace.WorkspacePath, runId,
            relative, reviewedSha, currentSha, compatible, []);
    }

    public BrdReviewDispositionResult Decide(string workspacePath, string runId, string findingId,
        string decision, string actor, string rationale, string? approvedRecommendation = null)
    {
        var current = Status(workspacePath, runId);
        if (current.Errors.Count > 0 || current.Disposition is null) return current;
        if (CisBrdReviewDispositionCodec.IsApproved(current.Disposition))
            return current with { Status = "blocked", Errors = ["An approved disposition set is immutable; create a new independent review to change scope."] };
        decision = decision.Trim().ToLowerInvariant();
        if (decision is not ("accepted" or "rejected"))
            return current with { Status = "invalid", Errors = ["Decision must be accepted or rejected."] };
        if (string.IsNullOrWhiteSpace(actor))
            return current with { Status = "invalid", Errors = ["A human actor is required."] };
        if (decision == "rejected" && string.IsNullOrWhiteSpace(rationale))
            return current with { Status = "invalid", Errors = ["A rejected legacy disposition requires a rationale."] };
        if (decision == "rejected" && !string.IsNullOrWhiteSpace(approvedRecommendation))
            return current with { Status = "invalid", Errors = ["A rejected disposition cannot define an approved recommendation."] };
        if (approvedRecommendation is { Length: > 16_384 })
            return current with { Status = "invalid", Errors = ["The approved recommendation exceeds the 16 KiB bound."] };
        var sourceDocument = UpgradeForExactRecommendationApproval(current.Disposition);
        var found = false;
        var findings = sourceDocument.Findings.Select(item =>
        {
            if (!item.Id.Equals(findingId, StringComparison.OrdinalIgnoreCase)) return item;
            found = true;
            var selectedRecommendation = decision == "accepted"
                ? string.IsNullOrWhiteSpace(approvedRecommendation) ? item.Recommendation.Trim() : approvedRecommendation.Trim()
                : null;
            return item with
            {
                Decision = decision,
                DecidedBy = actor.Trim(),
                DecidedAtUtc = UtcNow(),
                Rationale = decision == "rejected" ? rationale.Trim() : null,
                ApprovedRecommendation = selectedRecommendation,
            };
        }).ToArray();
        if (!found) return current with { Status = "not-found", Errors = [$"Finding '{findingId}' is not part of review '{runId}'."] };
        var updated = sourceDocument with { Findings = findings };
        if (findings.All(item => item.Decision is "accepted" or "rejected"))
        {
            var completed = updated with
            {
                ApprovedBy = actor.Trim(),
                ApprovedAtUtc = UtcNow(),
                ApprovalReason = null,
            };
            updated = completed with
            {
                ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(completed),
            };
        }
        WriteAtomic(current.CanonicalPath!, CisBrdReviewDispositionCodec.Render(updated));
        return current with
        {
            Status = CisBrdReviewDispositionCodec.IsApproved(updated) ? "approved" : "review-required",
            Disposition = updated,
            Applied = true,
        };
    }

    public BrdReviewDispositionResult AcceptAll(string workspacePath, string runId, string actor)
    {
        var current = Status(workspacePath, runId);
        if (current.Errors.Count > 0 || current.Disposition is null) return current;
        if (CisBrdReviewDispositionCodec.IsApproved(current.Disposition))
            return current with { Status = AppliedStatus(current.Disposition) };
        if (string.IsNullOrWhiteSpace(actor))
            return current with { Status = "invalid", Errors = ["A human actor is required."] };
        var sourceDocument = UpgradeForExactRecommendationApproval(current.Disposition);
        var pending = sourceDocument.Findings.Count(item => item.Decision == "pending");
        if (pending == 0)
            return current with { Status = "invalid", Errors = ["No pending recommendations are available for batch approval."] };
        var decidedAt = UtcNow(); var selectedActor = actor.Trim();
        var findings = sourceDocument.Findings.Select(item => item.Decision != "pending" ? item : item with
        {
            Decision = "accepted",
            DecidedBy = selectedActor,
            DecidedAtUtc = decidedAt,
            Rationale = null,
            ApprovedRecommendation = item.Recommendation.Trim(),
        }).ToArray();
        var completed = sourceDocument with
        {
            Findings = findings,
            ApprovedBy = selectedActor,
            ApprovedAtUtc = decidedAt,
            ApprovalReason = null,
        };
        var approved = completed with
        {
            ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(completed),
        };
        WriteAtomic(current.CanonicalPath!, CisBrdReviewDispositionCodec.Render(approved));
        return current with { Status = "approved", Disposition = approved, Applied = true };
    }

    public BrdReviewDispositionResult Approve(string workspacePath, string runId, string reviewer, string reason)
    {
        var current = Status(workspacePath, runId);
        if (current.Errors.Count > 0 || current.Disposition is null) return current;
        if (CisBrdReviewDispositionCodec.IsApproved(current.Disposition)) return current with { Status = AppliedStatus(current.Disposition) };
        if (string.IsNullOrWhiteSpace(reviewer))
            return current with { Status = "invalid", Errors = ["A human reviewer is required."] };
        var sourceDocument = UpgradeForExactRecommendationApproval(current.Disposition);
        var pending = sourceDocument.Findings.Where(item => item.Decision == "pending").Select(item => item.Id).ToArray();
        if (pending.Length > 0)
            return current with { Status = "blocked", Errors = [$"Every finding must be dispositioned before approval. Pending: {string.Join(", ", pending)}."] };
        var prepared = sourceDocument with
        {
            ApprovedBy = reviewer.Trim(),
            ApprovedAtUtc = UtcNow(),
            ApprovalReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
        var approved = prepared with { ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(prepared) };
        WriteAtomic(current.CanonicalPath!, CisBrdReviewDispositionCodec.Render(approved));
        return current with { Status = "approved", Disposition = approved, Applied = true };
    }

    private BrdReviewDispositionResult ReadExisting(CisWorkspace workspace, ReviewPaths paths, bool validateSources)
    {
        if (!File.Exists(paths.Canonical)) return Failure(workspace.WorkspacePath,
            [$"Canonical review dispositions are missing. Run `cis brd review init {paths.RunId}`."], paths.Canonical);
        if (!CisBrdReviewDispositionCodec.TryParse(File.ReadAllText(paths.Canonical), out var document, out var error) || document is null)
            return Failure(workspace.WorkspacePath, [error ?? "Canonical review dispositions are invalid."], paths.Canonical);
        var errors = new List<string>();
        var superseded = false;
        if (!document.ReviewRunId.Equals(paths.RunId, StringComparison.Ordinal)) errors.Add("Canonical disposition run identity does not match its path.");
        if (document.SchemaVersion is not (1 or 2)) errors.Add("Canonical dispositions use an unsupported schema version.");
        if (document.Findings.Count == 0 || document.Findings.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Findings.Count)
            errors.Add("Canonical dispositions must contain uniquely identified review findings.");
        if (document.Findings.Any(item => item.Decision is not ("pending" or "accepted" or "rejected")))
            errors.Add("Canonical dispositions contain an unsupported decision.");
        if (document.SchemaVersion >= 2 && document.Findings.Any(item => item.Decision == "accepted"
                && string.IsNullOrWhiteSpace(CisBrdReviewDispositionCodec.EffectiveRecommendation(item))))
            errors.Add("An accepted disposition has no approved recommendation text.");
        if (document.Findings.Any(item => item.Decision == "rejected" && string.IsNullOrWhiteSpace(item.Rationale)))
            errors.Add("A rejected legacy disposition has no rationale.");
        if (validateSources)
        {
            var source = ReadReview(paths, paths.RunId);
            errors.AddRange(source.Errors);
            if (source.Errors.Count == 0)
            {
                if (!document.ReviewResultSha256.Equals(source.ResultSha256, StringComparison.OrdinalIgnoreCase)) errors.Add("The source agent review result changed after disposition initialization.");
                var currentBrdSha = File.Exists(paths.Brd) ? ShaFile(paths.Brd) : string.Empty;
                if (document.AppliedByRunId is null && !document.BrdSha256.Equals(currentBrdSha, StringComparison.OrdinalIgnoreCase))
                {
                    var drift = ReviewDriftMessage(paths, "The BRD changed before the approved review recommendations were applied.");
                    superseded = drift.StartsWith("The user-authored BRD content is unchanged", StringComparison.Ordinal);
                    errors.Add(drift);
                }
                if (document.AppliedByRunId is not null && !string.Equals(document.RevisedBrdSha256, currentBrdSha, StringComparison.OrdinalIgnoreCase)) errors.Add("The BRD changed after the approved recommendations were applied.");
                if (!document.ReviewProvider.Equals(source.Provider, StringComparison.OrdinalIgnoreCase)) errors.Add("The source review provider changed after disposition initialization.");
            }
        }
        if (document.ApprovedAtUtc is not null && !CisBrdReviewDispositionCodec.IsApproved(document)) errors.Add("Disposition approval evidence is incomplete or its decision digest is stale.");
        var status = errors.Count > 0 ? superseded && errors.Count == 1 ? "superseded" : "invalid" : AppliedStatus(document);
        return New(status, workspace, paths.Canonical, document, errors, false);
    }

    private static string AppliedStatus(CisBrdReviewDispositionDocument document)
        => document.AppliedByRunId is not null ? "applied"
            : CisBrdReviewDispositionCodec.IsApproved(document) ? "approved"
            : document.Findings.Any(item => item.Decision == "pending") ? "review-required" : "ready-for-approval";

    private static CisBrdReviewDispositionDocument UpgradeForExactRecommendationApproval(
        CisBrdReviewDispositionDocument document)
    {
        if (document.SchemaVersion >= 2) return document;
        var findings = document.Findings.Select(item => item.Decision == "accepted"
            ? item with { ApprovedRecommendation = item.Recommendation.Trim(), Rationale = null }
            : item).ToArray();
        return document with
        {
            SchemaVersion = 2,
            Findings = findings,
            ApprovedBy = null,
            ApprovedAtUtc = null,
            ApprovalReason = null,
            ApprovedDecisionSha256 = null,
        };
    }

    private static ReviewSource ReadReview(ReviewPaths paths, string runId)
    {
        var errors = new List<string>();
        if (!File.Exists(paths.Manifest) || !File.Exists(paths.Result))
            return new(null, null, null, null, [$"Successful agent review evidence for '{runId}' was not found."]);
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(paths.Manifest));
            using var result = JsonDocument.Parse(File.ReadAllText(paths.Result));
            var manifestRoot = manifest.RootElement; var resultRoot = result.RootElement;
            var task = String(manifestRoot, "taskId"); var manifestStatus = String(manifestRoot, "status");
            var resultStatus = String(resultRoot, "status"); var provider = String(manifestRoot, "provider");
            var brdSha = String(manifestRoot, "taskDigest"); var expectedResult = String(manifestRoot, "resultDigest");
            var resultSha = ShaFile(paths.Result);
            if (task != "BRD-REVIEW") errors.Add("The selected run is not a BRD review.");
            if (!manifestStatus.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) || !resultStatus.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)) errors.Add("The selected BRD review did not complete successfully.");
            if (!string.IsNullOrWhiteSpace(expectedResult) && !expectedResult.Equals(resultSha, StringComparison.OrdinalIgnoreCase)) errors.Add("The selected BRD review result digest is stale.");
            var findings = new List<CisBrdReviewFindingDisposition>();
            if (!resultRoot.TryGetProperty("review", out var review) || review.ValueKind != JsonValueKind.Object
                || !review.TryGetProperty("findings", out var array) || array.ValueKind != JsonValueKind.Array)
                errors.Add("The selected run has no structured BRD review findings.");
            else foreach (var item in array.EnumerateArray())
                findings.Add(new(String(item, "id"), String(item, "severity"), String(item, "category"), String(item, "location"),
                    String(item, "observation"), String(item, "recommendation"), "pending"));
            if (findings.Count == 0) errors.Add("The selected review has no recommendations to disposition.");
            return new(resultSha, brdSha, provider, findings, errors);
        }
        catch (JsonException exception) { errors.Add("Agent review evidence is malformed: " + exception.Message); return new(null, null, null, null, errors); }
    }

    private WorkspaceResolution Resolve(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace?.AuthorityRepository is null)
            return new(null, resolution.Errors.Count > 0 ? resolution.Errors : ["Workspace authority repository is unavailable."]);
        return new(resolution.Workspace, []);
    }

    private static ReviewPaths Paths(CisWorkspace workspace, string runId)
    {
        var authority = workspace.AuthorityRepository!;
        var local = Path.Combine(authority.RepositoryPath, ".cis", "local", "agents", "runs", runId);
        var docs = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar));
        var brdRelative = Path.Combine(authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar),
            "specs", "business-requirements.md");
        var reviewedBrd = Path.Combine(authority.RepositoryPath, ".cis", "local", "agents", "workspaces",
            runId, authority.Id, brdRelative);
        return new(runId, Path.Combine(docs, "reviews", "brd", runId + ".md"), Path.Combine(local, "manifest.json"),
            Path.Combine(local, "result.json"), Path.Combine(docs, "specs", "business-requirements.md"), reviewedBrd);
    }

    private static string ReviewDriftMessage(ReviewPaths paths, string fallback)
    {
        if (!File.Exists(paths.Brd) || !File.Exists(paths.ReviewedBrd)) return fallback;
        var reviewed = File.ReadAllText(paths.ReviewedBrd);
        var current = File.ReadAllText(paths.Brd);
        if (!OnlyManagedEvidenceChanged(reviewed, current)) return fallback;
        return "The user-authored BRD content is unchanged, but CIS-managed baseline or source evidence changed after this review. "
            + "The review is superseded because its evidence-related findings may no longer be current; run a fresh independent BRD review.";
    }

    private static bool OnlyManagedEvidenceChanged(string reviewed, string current)
    {
        static string Surface(string value)
        {
            var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (var region in new[] { "baseline", "sources" })
            {
                var startMarker = $"<!-- cis:{region}:start -->";
                var endMarker = $"<!-- cis:{region}:end -->";
                var start = normalized.IndexOf(startMarker, StringComparison.Ordinal);
                var end = normalized.IndexOf(endMarker, start < 0 ? 0 : start + startMarker.Length, StringComparison.Ordinal);
                if (start < 0 || end < 0) continue;
                normalized = normalized[..(start + startMarker.Length)] + "\n<managed-evidence>\n" + normalized[end..];
            }
            return normalized;
        }
        return !string.Equals(reviewed.Replace("\r\n", "\n", StringComparison.Ordinal),
                current.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal)
            && string.Equals(Surface(reviewed), Surface(current), StringComparison.Ordinal);
    }

    private IReadOnlyList<string> EnsureCatalog(CisWorkspace workspace, ReviewPaths paths)
    {
        var authority = workspace.AuthorityRepository!;
        var docs = Path.Combine(authority.RepositoryPath,
            authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar));
        var catalog = Path.Combine(docs, "catalog.yml");
        var relative = Path.GetRelativePath(authority.RepositoryPath, paths.Canonical).Replace('\\', '/');
        var legacyRelative = Path.GetRelativePath(docs, paths.Canonical).Replace('\\', '/');
        var stableId = $"{authority.Id}:review:brd:{paths.RunId.ToLowerInvariant()}";
        var entry = new CatalogArtifactEntry(
            stableId, relative, "brd-review-disposition", "review-required", "canonical");
        var existing = File.Exists(catalog) ? File.ReadAllText(catalog) : null;
        var migrated = false;
        if (existing is not null)
            existing = MigrateLegacyCatalogEntry(existing, stableId, legacyRelative, relative, out migrated);
        var merged = _catalogMerger.Merge(authority.Id, existing, [entry]);
        if (merged.Collisions.Count > 0)
            return merged.Collisions.Select(item => "Documentation catalog collision: " + item).ToArray();
        if (merged.Changed || migrated) WriteAtomic(catalog, merged.Content);
        return [];
    }

    private static string MigrateLegacyCatalogEntry(string content, string stableId,
        string legacyRelative, string correctedRelative, out bool migrated)
    {
        migrated = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Trim().Equals("- id: " + stableId, StringComparison.Ordinal)) continue;
            var end = index + 1;
            while (end < lines.Length && !lines[end].TrimStart().StartsWith("- id: ", StringComparison.Ordinal)) end++;
            var typeMatches = lines.Skip(index + 1).Take(end - index - 1)
                .Any(line => line.Trim().Equals("type: brd-review-disposition", StringComparison.Ordinal));
            if (!typeMatches) return content;
            for (var lineIndex = index + 1; lineIndex < end; lineIndex++)
            {
                if (lines[lineIndex].Trim().Equals("path: " + legacyRelative, StringComparison.Ordinal))
                {
                    lines[lineIndex] = "    path: " + correctedRelative;
                    migrated = true;
                }
                if (lines[lineIndex].Trim().Equals("authority: human-governed", StringComparison.Ordinal))
                {
                    lines[lineIndex] = "    authority: canonical";
                    migrated = true;
                }
            }
            break;
        }
        return migrated ? string.Join('\n', lines) : content;
    }

    private static string String(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    private static bool SafeRunId(string value) => value.Length is > 0 and <= 100 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static string ShaFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private string UtcNow() => _clock().ToUniversalTime().ToString("O");
    private static BrdReviewDispositionResult Failure(string? workspace, IReadOnlyList<string> errors, string? path = null)
        => new("invalid", workspace, path, null, errors, false);
    private static BrdReviewDispositionResult New(string status, CisWorkspace workspace, string path,
        CisBrdReviewDispositionDocument document, IReadOnlyList<string> errors, bool applied)
        => new(status, workspace.WorkspacePath, path, document, errors, applied);
    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content, new UTF8Encoding(false)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record WorkspaceResolution(CisWorkspace? Workspace, IReadOnlyList<string> Errors);
    private sealed record ReviewPaths(string RunId, string Canonical, string Manifest, string Result, string Brd,
        string ReviewedBrd);
    private sealed record ReviewSource(string? ResultSha256, string? BrdSha256, string? Provider,
        IReadOnlyList<CisBrdReviewFindingDisposition>? Findings, IReadOnlyList<string> Errors);
}
