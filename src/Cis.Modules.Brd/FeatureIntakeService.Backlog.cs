using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private IReadOnlyList<CisBacklogFeature> BacklogFeatures(string workspacePath, IReadOnlyList<CisFeatureIntakePlan> plans)
    {
        if (backlog is null) return [];
        var status = backlog.Status(workspacePath);
        var ready = status.Validation is { Valid: true, Current: true, EffectiveStatus: "Active" };
        var authority = workspaces.Resolve(workspacePath).Workspace?.AuthorityRepository;
        var baseline = authority is null ? null : definitions.Select(check => check.Evaluate(authority.RepositoryPath)).FirstOrDefault(check => check.Applicable);
        var started = plans.Where(plan => plan.BacklogItemId is not null).Select(plan => plan.BacklogItemId!).ToHashSet(StringComparer.Ordinal);
        foreach (var item in status.Items.Where(item => item.FeatureSpecification != "not-created")) started.Add(item.Id);
        return status.Items.Where(item => !plans.Any(plan => plan.BacklogItemId == item.Id)).Select(item =>
        {
            var issues = new List<string>();
            if (!ready) issues.AddRange(status.Errors.Concat(status.Validation?.Errors ?? []).Concat(["Approve a current backlog before starting feature definition."]));
            if (baseline is { Active: false }) issues.AddRange(baseline.Errors.DefaultIfEmpty("Activate the current product definition before starting feature definition."));
            var pending = item.DependsOn.Where(id => !started.Contains(id)).ToArray();
            if (pending.Length > 0) issues.Add("Start the prerequisite feature definitions first: " + string.Join(", ", pending));
            var canStart = ready && baseline is not { Active: false } && pending.Length == 0;
            return new CisBacklogFeature(item.Id, item.RequirementId, item.Outcome, canStart ? "Ready for feature definition" : "Blocked",
                item.DependsOn, issues.Distinct().ToArray(), canStart, Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item, Json) + "|" + baseline?.BaselineHash)));
        }).ToArray();
    }

    public CisFeatureIntakeResult StartBacklogFeature(string workspacePath, string itemId, string actor, string expectedInputHash)
    {
        try
        {
            var workspace = workspaces.Resolve(workspacePath).Workspace;
            var authority = workspace?.AuthorityRepository;
            if (backlog is null || authority is null || !SingleLine(actor, 200)) return Invalid(["A product authority, backlog service and human actor are required."]);
            var lockPath = Path.Combine(authority.RepositoryPath, ".cis/local/feature-intake.lock");
            if (!SafeAbsolutePath(lockPath)) return Invalid(["Unsafe feature intake lock path."]);
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var intakeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var plans = Requests(workspacePath);
            var existing = plans.SingleOrDefault(plan => plan.BacklogItemId == itemId);
            if (existing is not null) return new("unchanged", existing, [], [], false);
            var selected = BacklogFeatures(workspacePath, plans).SingleOrDefault(item => item.Id == itemId);
            if (selected is null) return Invalid(["The backlog feature no longer exists. Refresh the feature list."]);
            if (!selected.CanStart) return new("blocked", null, selected.Issues, [], false);
            if (selected.InputHash != expectedInputHash) return Invalid(["The backlog changed. Refresh the feature list before starting its definition."]);
            var docs = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot);
            var brdPath = CisProductDocumentPaths.Resolve(docs, "specs", "business-requirements.md");
            if (!SafeAbsolutePath(brdPath) || new FileInfo(brdPath).Length > 2_097_152) return Invalid(["The source BRD is unavailable or exceeds the retained source limit."]);
            var brdBytes = File.ReadAllBytes(brdPath);
            var brdHash = Hash(brdBytes);
            var brdRelative = $".cis/inputs/brds/{brdHash[7..]}/source.md";
            var retainedBrd = Path.Combine(authority.RepositoryPath, brdRelative);
            if (!SafeAbsolutePath(retainedBrd)) return Invalid(["Unsafe source BRD snapshot path."]);
            var requirements = BrdRequirementReader.Read(Encoding.UTF8.GetString(brdBytes), "Functional requirements");
            var requirement = requirements.Requirements.Single(item => item.Id == selected.RequirementId);
            var baseline = definitions.Select(check => check.Evaluate(authority.RepositoryPath)).FirstOrDefault(check => check.Applicable);
            var slug = selected.Id.ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$") || slug.Length > 80)
                return Invalid(["The backlog item needs a safe, stable feature identifier."]);
            var requestRelative = $"{authority.DocumentationRoot}/specs/feature-requests/{slug}/request.md";
            var sourceRelative = $".cis/inputs/features/{slug}/source.md";
            if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, requestRelative, out var requestPath)
                || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, sourceRelative, out var sourcePath)
                || !SafeAbsolutePath(requestPath) || !SafeAbsolutePath(sourcePath)) return Invalid(["Unsafe backlog feature paths."]);
            if (File.Exists(requestPath)) return Invalid(["The feature slug already belongs to another request. Existing work was preserved."]);
            var source = $"# {selected.Title}\n\n## Backlog authority\n\n- Backlog item: {selected.Id}\n- BRD requirement: {requirement.Id}\n- Product baseline: {baseline?.BaselineHash}\n- Prerequisites: {string.Join(", ", selected.DependsOn)}\n\n## Functional requirements\n\n- **{requirement.Id} — {requirement.Outcome}.** {requirement.Text}\n- Acceptance: {requirement.Acceptance}\n\n## Acceptance intent\n\n{requirement.Acceptance}\n\n## Scope\n\nThis feature implements the identified approved requirement. The product BRD, technical direction and architecture remain the authority for shared constraints. Repository links will be decided in story planning.\n";
            var sourceBytes = Encoding.UTF8.GetBytes(source);
            var plan = new CisFeatureIntakePlan(selected.Title, slug, authority.RepositoryPath, "product", authority.DocumentationRoot,
                requestRelative, sourceRelative, Hash(sourceBytes), [], [], baseline?.BaselineHash, selected.InputHash)
            {
                BacklogItemId = selected.Id,
                SourceBrds = [BrdSource(Path.GetRelativePath(authority.RepositoryPath, brdPath).Replace('\\', '/'), brdRelative, brdHash, [requirement.Id])]
            };
            var record = new IntakeRecord(selected.InputHash, actor.Trim(), DateTimeOffset.UtcNow.ToString("O"), plan);
            var catalogPath = Path.Combine(docs, "catalog.yml");
            if (!SafeAbsolutePath(catalogPath)) return Invalid(["Unsafe catalog path."]);
            var currentCatalog = File.ReadAllText(catalogPath);
            var merged = catalog.Merge(authority.Id, currentCatalog, [new($"{authority.Id}:feature-intake:{slug}", requestRelative, "feature-intake", "draft", "canonical")]);
            if (merged.Collisions.Count > 0) return Invalid(merged.Collisions);
            if (File.Exists(sourcePath) && Hash(File.ReadAllBytes(sourcePath)) != plan.SourceHash) return Invalid(["A different retained feature source exists. Existing work was preserved."]);
            if (FileHash(brdPath) != brdHash) return Invalid(["The source BRD changed during feature setup. Refresh before starting this feature."]);
            WriteRevision(retainedBrd, brdBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
            if (!File.Exists(sourcePath)) File.WriteAllBytes(sourcePath, sourceBytes);
            File.WriteAllText(requestPath, Render(authority.Id, requestPath, sourcePath, record, workspace!), new UTF8Encoding(false));
            if (File.ReadAllText(catalogPath) != currentCatalog) return new("setup-incomplete", plan, ["The catalog changed during feature setup. The draft was preserved; refresh before continuing."], [], true);
            File.WriteAllText(catalogPath, merged.Content, new UTF8Encoding(false));
            return new("created", plan, [], [], true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return Invalid([exception.Message]); }
    }
}
