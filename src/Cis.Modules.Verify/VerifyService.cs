using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;
using Cis.Modules.Testing;
using Cis.Modules.Security;

namespace Cis.Modules.Verify;

public sealed record VerifyFileChange(string Status, string Path, string RepositoryId = "", string ContentDigest = "");
public sealed record VerifyRepositoryBaseline(string RepositoryId, string BaselineKind, string Baseline, bool Exact);
public sealed record VerifyFinding(string Severity, string Code, string Message, string? Path);
public sealed record VerifySnapshot(int SchemaVersion, string ChangeId, string Baseline, string CapturedAtUtc,
    IReadOnlyList<VerifyFileChange> Files, string Digest, IReadOnlyList<VerifyRepositoryBaseline>? Repositories = null);
public sealed record VerifyResult(string Status, string? RepositoryPath, string? ChangeId, VerifySnapshot? Snapshot,
    IReadOnlyList<VerifyFinding> Findings, IReadOnlyList<string> Evidence, bool Applied)
{
    public int ExitCode => Findings.Any(x => x.Severity == "error") ? 4 : 0;
}

public sealed class VerifyService
{
    public const string RootPath = ".cis/local/verify";
    private const int GitCommandTimeoutMilliseconds = 30_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly ChangeDossierStore _changes;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;
    private readonly PlanningService? _planning;
    private readonly TestingService? _testing;
    private readonly SecurityService? _security;
    private readonly IReadOnlyList<ICisFeatureApprovalAuthority> _featureAuthorities;
    private readonly Func<DateTimeOffset> _clock;

    public VerifyService(ICisRepositoryContextResolver resolver, ChangeDossierStore changes,
        IEnumerable<ICisFeatureApprovalAuthority> featureAuthorities,
        Func<DateTimeOffset>? clock = null, ICisWorkspaceRegistry? workspaceRegistry = null,
        PlanningService? planning = null, TestingService? testing = null, SecurityService? security = null)
    {
        _resolver = resolver;
        _changes = changes;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _workspaceRegistry = workspaceRegistry;
        _planning = planning;
        _testing = testing;
        _security = security;
        _featureAuthorities = featureAuthorities.ToArray();
    }

    public VerifyResult Diff(string repo, string change)
    {
        if (!Setup(repo, change, out var context, out var dossier, out var findings))
            return New(context, change, null, findings, [], false, "invalid");
        change = Path.GetFileName(dossier!.Id);
        var snapshot = Capture(context!, dossier!, findings);
        if (snapshot is null) return New(context, change, null, findings, [], false, "invalid");
        Write(SnapshotPath(context!, change), JsonSerializer.Serialize(snapshot, JsonOptions));
        return New(context, change, snapshot, findings, [], true, "captured");
    }

    public VerifyResult Compare(string repo, string change)
    {
        if (!Setup(repo, change, out var context, out var dossier, out var findings))
            return New(context, change, null, findings, [], false, "invalid");
        change = Path.GetFileName(dossier!.Id);
        var snapshot = ReadSnapshot(context!, change, findings);
        if (snapshot is null) return New(context, change, null, findings, [], false, "missing-baseline");

        var scope = PlannedScope(context!, change);
        var authority = context!.RepositoryId;
        var actualRepos = snapshot.Files.Select(x => RepoId(x, authority)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownRepos = Repositories(context, findings).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in scope.Repositories.Where(x => !knownRepos.Contains(x)))
            findings.Add(new("error", "CIS-VERIFY-REPOSITORY-MISSING", "Task target is not a registered workspace repository.", id));
        foreach (var id in scope.Repositories.Where(x => !actualRepos.Contains(x)))
            findings.Add(new("warning", "CIS-VERIFY-PLANNED-MISSING", "Planned repository has no observed change.", id));
        foreach (var id in actualRepos.Where(x => !x.Equals(authority, StringComparison.OrdinalIgnoreCase) && !scope.Repositories.Contains(x)))
            findings.Add(new("warning", "CIS-VERIFY-UNPLANNED", "Observed repository is not named by a task target.", id));
        foreach (var target in scope.Paths.Where(x => !snapshot.Files.Any(y => PathMatches(x, y, authority))))
            findings.Add(new("warning", "CIS-VERIFY-PLANNED-MISSING", "Planned target path has no observed change.", target.Display));
        return New(context, change, snapshot, findings, [], false, "compared");
    }

    public VerifyResult Validate(string repo, string change)
    {
        var compared = Compare(repo, change);
        if (compared.RepositoryPath is null) return compared;
        var context = _resolver.Resolve(compared.RepositoryPath).Context!;
        change = Path.GetFileName(compared.ChangeId!);
        var dossier = _changes.Read(context.RepositoryPath, change)!;
        var findings = compared.Findings.ToList();
        var snapshot = compared.Snapshot;
        if (snapshot is not null)
        {
            if (snapshot.SchemaVersion < 3)
                findings.Add(new("error", "CIS-VERIFY-SNAPSHOT-VERSION", "Run `cis verify diff` again to create a content-aware workspace snapshot.", null));
            if (snapshot.Files.Count == 0)
                findings.Add(new("error", "CIS-VERIFY-EMPTY", "An empty snapshot cannot satisfy final verification.", null));
            if (snapshot.Digest != Digest(snapshot.Files))
                findings.Add(new("error", "CIS-VERIFY-DIGEST", "Snapshot digest does not match its inventory.", null));
            var liveFindings = new List<VerifyFinding>();
            var live = Capture(context, dossier, liveFindings);
            findings.AddRange(liveFindings.Where(x => x.Severity == "error"));
            if (live is not null && live.Digest != snapshot.Digest)
                findings.Add(new("error", "CIS-VERIFY-SNAPSHOT-STALE", "Repository state changed after capture. Run `cis verify diff` again.", null));
        }

        var dir = Path.Combine(context.DocumentationPath, "changes", change);
        var plan = Path.Combine(dir, "plan.md");
        var design = Path.Combine(dir, "design.md");
        if (!File.Exists(plan) || !Regex.IsMatch(File.ReadAllText(plan), @"(?im)^status:\s*(Approved|Complete|Accepted)\s*$"))
            findings.Add(new("error", "CIS-VERIFY-PLAN", "Plan is not approved.", Relative(context, plan)));
        var planText = File.Exists(plan) ? File.ReadAllText(plan) : string.Empty;
        if (_planning is not null)
        {
            var planValidation = _planning.Validate(context.RepositoryPath, change);
            foreach (var error in planValidation.Errors.Concat(planValidation.Validation?.Errors ?? []))
                findings.Add(new("error", "CIS-VERIFY-PLAN-CURRENCY", error, Relative(context, plan)));
            if (Regex.IsMatch(planText, @"(?im)^feature_spec_path:\s*.+$"))
            {
                foreach (var error in _planning.ValidateManualTestAutomationCoverage(context.RepositoryPath, change))
                    findings.Add(new("error", "CIS-VERIFY-AUTOMATION-COVERAGE", error,
                        Relative(context, Path.Combine(dir, "test-cases.md"))));
            }
        }
        ValidateFeatureAuthority(context, plan, planText, findings);
        var designRequired = !Regex.IsMatch(planText, @"(?im)^feature_spec_frontend:\s*false\s*$");
        if (designRequired && File.Exists(design) && File.ReadAllText(design).Contains("approval_status:", StringComparison.OrdinalIgnoreCase)
            && !Regex.IsMatch(File.ReadAllText(design), @"(?im)^approval_status:\s*Approved\s*$"))
            findings.Add(new("error", "CIS-VERIFY-DESIGN", "Design approval is not complete.", Relative(context, design)));
        var taskDir = Path.Combine(dir, "agent-tasks");
        var tasks = Directory.Exists(taskDir) ? Directory.EnumerateFiles(taskDir, "*.md") : [];
        foreach (var task in tasks.Where(x => !HasVerificationDisposition(File.ReadAllText(x))))
            findings.Add(new("error", "CIS-VERIFY-TASK", "Task lacks a verification-ready lifecycle disposition.", Relative(context, task)));
        var verification = Path.Combine(dir, "verification.md");
        if (!File.Exists(verification) || !File.ReadLines(verification).Any(x => x.TrimStart().StartsWith('|') && x.Contains("Passed", StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("error", "CIS-VERIFY-EVIDENCE", "No passing verification evidence is recorded.", Relative(context, verification)));
        if (_testing is not null && Regex.IsMatch(planText, @"(?im)^feature_spec_path:\s*.+$"))
            ValidateReconciledTesting(context, change, verification, findings);
        if (_security is not null && File.Exists(Path.Combine(context.DocumentationPath, "references", "security-suite-profile.md")))
            ValidateReconciledSecurity(context, findings);
        return New(context, change, snapshot, findings, ReadEvidence(verification), false,
            findings.Any(x => x.Severity == "error") ? "invalid" : "valid");
    }

    private void ValidateReconciledSecurity(CisRepositoryContext context, List<VerifyFinding> findings)
    {
        var result = _security!.Status(context.RepositoryPath, null);
        if (result.Manifest is null)
        {
            findings.Add(new("error", "CIS-VERIFY-SECURITY-EVIDENCE", "No reconciled security run is available for the repository.", ".cis/local/security"));
            return;
        }
        if (result.Manifest.Status is not ("passed" or "passed-with-findings"))
            findings.Add(new("error", "CIS-VERIFY-SECURITY-STATUS", $"Security run '{result.Manifest.RunId}' is {result.Manifest.Status}.", $".cis/local/security/runs/{result.Manifest.RunId}/manifest.json"));
        var revision = GitRevision(context.RepositoryPath);
        if (revision != "unavailable" && !result.Manifest.RepositoryRevision.Equals(revision, StringComparison.OrdinalIgnoreCase))
            findings.Add(new("error", "CIS-VERIFY-SECURITY-STALE", "Security evidence was reconciled against a different repository revision.", $".cis/local/security/runs/{result.Manifest.RunId}/manifest.json"));
    }

    private static string GitRevision(string repository)
    {
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repository };
            start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
            var result = CisProcessSafety.Run(start, TimeSpan.FromMilliseconds(GitCommandTimeoutMilliseconds));
            return !result.TimedOut && result.ExitCode == 0 ? result.StandardOutput.Trim() : "unavailable";
        }
        catch (Win32Exception) { return "unavailable"; }
        catch (InvalidOperationException) { return "unavailable"; }
        catch (IOException) { return "unavailable"; }
        catch (UnauthorizedAccessException) { return "unavailable"; }
    }

    public VerifyResult Evidence(string repo, string change, string task, string check, string artifact, string result, string notes)
    {
        if (!Setup(repo, change, out var context, out var dossier, out var findings))
            return New(context, change, null, findings, [], false, "invalid");
        change = Path.GetFileName(dossier!.Id);
        if (new[] { task, check, artifact, result }.Any(string.IsNullOrWhiteSpace))
            findings.Add(new("error", "CIS-VERIFY-EVIDENCE-INPUT", "Task, check, artifact, and result are required.", null));
        if (findings.Any(x => x.Severity == "error")) return New(context, change, null, findings, [], false, "invalid");
        var snapshot = ReadSnapshot(context!, change, findings);
        if (snapshot is null || findings.Any(x => x.Severity == "error"))
            return New(context, change, snapshot, findings, [], false, "invalid");
        var path = Path.Combine(context!.DocumentationPath, "changes", change, "verification.md");
        var separator = File.Exists(path) && new FileInfo(path).Length > 0 && !EndsWithLineBreak(path)
            ? Environment.NewLine
            : string.Empty;
        File.AppendAllText(path, $"{separator}| {Esc(task)} | {Esc(check)} | `{Esc(artifact)}` | {Esc(result)} | {Esc(notes)} |{Environment.NewLine}");
        return New(context, change, snapshot, findings, ReadEvidence(path), true, "recorded");
    }

    private static bool EndsWithLineBreak(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length == 0) return true;
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() is '\n' or '\r';
    }

    public VerifyResult Accept(string repo, string change, string reviewer, string reason)
    {
        var valid = Validate(repo, change);
        if (valid.ExitCode != 0) return valid;
        change = Path.GetFileName(valid.ChangeId!);
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return Invalid(valid, "CIS-VERIFY-AUTHORITY", "Reviewer and reason are required.");
        if (valid.Snapshot is null || valid.Snapshot.Files.Count == 0 || valid.Snapshot.SchemaVersion < 3)
            return Invalid(valid, "CIS-VERIFY-SNAPSHOT-REQUIRED", "Acceptance requires a current non-empty workspace-aware snapshot.");
        var context = _resolver.Resolve(valid.RepositoryPath!).Context!;
        var path = Path.Combine(context.DocumentationPath, "changes", change, "verification.md");
        var text = new Regex(@"(?im)^status:\s*[^\r\n]+$").Replace(File.ReadAllText(path), "status: Accepted", 1);
        text = text.TrimEnd() + $"\n\n## Human acceptance\n\n- Reviewer: {Esc(reviewer)}\n- Accepted UTC: {_clock().ToUniversalTime():O}\n- Rationale: {Esc(reason)}\n- Snapshot digest: `{valid.Snapshot.Digest}`\n";
        Write(path, text);
        return valid with { Status = "accepted", Applied = true, Evidence = ReadEvidence(path) };
    }

    public VerifyResult Finalize(string repo, string change, string reviewer, string reason)
    {
        var valid = Validate(repo, change);
        if (valid.ExitCode != 0) return valid;
        change = Path.GetFileName(valid.ChangeId!);
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return Invalid(valid, "CIS-VERIFY-AUTHORITY", "Reviewer and reason are required.");
        if (_planning is null)
            return Invalid(valid, "CIS-VERIFY-FINALIZE-SERVICE", "The planning lifecycle service is unavailable.");

        var plan = _planning.Status(repo, change);
        if (plan.ExitCode != 0)
            return Invalid(valid, "CIS-VERIFY-FINALIZE-PLAN", string.Join(" ", plan.Errors));
        var finalSweep = plan.WorkItems.SingleOrDefault(x => x.TaskTypeKey == "core.delivery.final-sweep");
        var coordination = plan.WorkItems.SingleOrDefault(x => x.TaskTypeKey == "core.coordination.scope-guard");
        if (finalSweep is null || coordination is null || finalSweep.TaskPath is null || coordination.TaskPath is null)
            return Invalid(valid, "CIS-VERIFY-FINALIZE-TASKS", "Finalization requires one final sweep and one coordination scope guard.");

        var context = _resolver.Resolve(repo).Context!;
        var dossierDirectory = Path.Combine(context.DocumentationPath, "changes", change);
        var protectedPaths = new[]
        {
            Path.Combine(dossierDirectory, "proposal.md"),
            Path.Combine(dossierDirectory, "plan.md"),
            Path.Combine(dossierDirectory, "verification.md"),
            Path.Combine(dossierDirectory, "events.jsonl"),
            Path.Combine(dossierDirectory, finalSweep.TaskPath!.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(dossierDirectory, coordination.TaskPath!.Replace('/', Path.DirectorySeparatorChar)),
            SnapshotPath(context, change),
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var originals = protectedPaths.ToDictionary(path => path,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null,
            StringComparer.OrdinalIgnoreCase);
        VerifyResult Rollback(VerifyResult failure)
        {
            foreach (var item in originals)
            {
                if (item.Value is null) { if (File.Exists(item.Key)) File.Delete(item.Key); }
                else { Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!); File.WriteAllBytes(item.Key, item.Value); }
            }
            return failure;
        }

        var transitionReason = $"Final acceptance by {reviewer}: {reason}";
        if (!finalSweep.Status.Equals("Complete", StringComparison.OrdinalIgnoreCase))
        {
            var transitioned = _planning.TransitionTask(new(repo, change, finalSweep.Id, "Complete", reviewer, transitionReason));
            if (transitioned.ExitCode != 0)
                return Rollback(Invalid(valid, "CIS-VERIFY-FINALIZE-SWEEP", string.Join(" ", transitioned.Errors)));
        }
        if (!coordination.Status.Equals("Complete", StringComparison.OrdinalIgnoreCase))
        {
            var transitioned = _planning.TransitionTask(new(repo, change, coordination.Id, "Complete", reviewer, transitionReason));
            if (transitioned.ExitCode != 0)
                return Rollback(Invalid(valid, "CIS-VERIFY-FINALIZE-COORDINATION", string.Join(" ", transitioned.Errors)));
        }

        var closed = _changes.Close(repo, change);
        if (closed.ExitCode != 0)
            return Rollback(Invalid(valid, "CIS-VERIFY-FINALIZE-CLOSE", string.Join(" ", closed.Errors)));
        var captured = Diff(repo, change);
        if (captured.ExitCode != 0) return Rollback(captured);
        var accepted = Accept(repo, change, reviewer, reason);
        if (accepted.ExitCode != 0) return Rollback(accepted);
        var finalCapture = Diff(repo, change);
        if (finalCapture.ExitCode != 0) return Rollback(finalCapture);
        var finalValidation = Validate(repo, change);
        return finalValidation.ExitCode == 0
            ? finalValidation with { Status = "finalized", Applied = true }
            : Rollback(finalValidation);
    }

    private VerifySnapshot? Capture(CisRepositoryContext context, ChangeDossier dossier, List<VerifyFinding> findings)
    {
        var declared = (dossier.RepositoryBaselines ?? []).ToDictionary(x => x.RepositoryId, StringComparer.OrdinalIgnoreCase);
        var files = new List<VerifyFileChange>();
        var baselines = new List<VerifyRepositoryBaseline>();
        foreach (var repository in Repositories(context, findings).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var exact = declared.TryGetValue(repository.Id, out var stored);
            var kind = exact ? stored!.BaselineKind : "git";
            var baseline = exact ? stored!.Baseline : repository.Id.Equals(context.RepositoryId, StringComparison.OrdinalIgnoreCase)
                ? dossier.Baseline : Git(repository.RepositoryPath, repository.Id, findings, "rev-parse", "HEAD").Trim();
            if (!exact && repository.Id != context.RepositoryId)
                findings.Add(new("warning", "CIS-VERIFY-BASELINE-FALLBACK", "Legacy dossier has no participant creation baseline; current HEAD is used.", repository.Id));
            if (!kind.Equals("git", StringComparison.OrdinalIgnoreCase) || baseline.Length == 0)
            {
                findings.Add(new("error", "CIS-VERIFY-BASELINE", "Verification requires a Git baseline.", repository.Id));
                continue;
            }
            baselines.Add(new(repository.Id, kind, baseline, exact));
            files.AddRange(GitDiff(repository.RepositoryPath, repository.Id, baseline,
                exact ? stored!.WorkingTree : null, findings));
        }
        if (findings.Any(x => x.Severity == "error")) return null;
        var ordered = files.DistinctBy(x => $"{x.RepositoryId}\u001f{x.Status}\u001f{x.Path}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.RepositoryId, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToArray();
        return new(3, dossier.Id, dossier.Baseline, _clock().ToUniversalTime().ToString("O"), ordered, Digest(ordered), baselines);
    }

    private IReadOnlyList<CisWorkspaceRepository> Repositories(CisRepositoryContext context, List<VerifyFinding> findings)
    {
        if (_workspaceRegistry is not null)
        {
            var result = _workspaceRegistry.Resolve(context.RepositoryPath);
            if (result.IsSuccess && result.Workspace is not null) return result.Workspace.Repositories;
            if (File.Exists(Path.Combine(context.RepositoryPath, ".cis", "workspace.yml")))
                findings.AddRange(result.Errors.Select(x => new VerifyFinding("error", "CIS-VERIFY-WORKSPACE", x, null)));
        }
        return [new(context.RepositoryId, context.RepositoryPath, context.DocumentationRoot, "authority")];
    }

    private bool Setup(string repo, string change, out CisRepositoryContext? context, out ChangeDossier? dossier, out List<VerifyFinding> findings)
    {
        var result = _resolver.Resolve(repo);
        context = result.Context;
        findings = result.Errors.Select(x => new VerifyFinding("error", "CIS-VERIFY-REPOSITORY", x, null)).ToList();
        dossier = context is null ? null : _changes.Read(context.RepositoryPath, change);
        if (context is not null && dossier is null) findings.Add(new("error", "CIS-VERIFY-CHANGE", $"Unknown change '{change}'.", null));
        return context is not null && dossier is not null && findings.Count == 0;
    }

    private static VerifyFileChange[] GitDiff(
        string repo,
        string id,
        string baseline,
        IReadOnlyList<ChangeRepositoryWorkingFile>? initialWorkingTree,
        List<VerifyFinding> findings)
    {
        var tracked = Git(repo, id, findings, "diff", "--name-status", "--find-renames", baseline)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\r').Split('\t'))
            .Where(x => x.Length >= 2).Select(x => WorkingState(repo, x[0], x[^1]));
        var untracked = Git(repo, id, findings, "ls-files", "--others", "--exclude-standard")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => WorkingState(repo, "??", x.TrimEnd('\r')));
        var current = tracked.Concat(untracked)
            .Where(x => !x.Path.StartsWith(".cis/local/", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        if (initialWorkingTree is null)
            return current.Values.Select(x => new VerifyFileChange(x.Status, x.Path, id, x.Digest)).ToArray();

        var initial = initialWorkingTree
            .Where(x => !x.Path.StartsWith(".cis/local/", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        return current.Keys.Union(initial.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(path => !current.TryGetValue(path, out var currentFile)
                || !initial.TryGetValue(path, out var initialFile)
                || !currentFile.Status.Equals(initialFile.Status, StringComparison.Ordinal)
                || !currentFile.Digest.Equals(initialFile.Digest, StringComparison.OrdinalIgnoreCase))
            .Select(path => current.TryGetValue(path, out var file)
                ? new VerifyFileChange(file.Status, file.Path, id, file.Digest)
                : new VerifyFileChange("D", path, id, FileDigest(repo, path)))
            .ToArray();
    }

    private static ChangeRepositoryWorkingFile WorkingState(string repositoryPath, string status, string path)
    {
        var normalized = path.Replace('\\', '/');
        var absolute = Path.Combine(repositoryPath, normalized.Replace('/', Path.DirectorySeparatorChar));
        var digest = FileDigest(repositoryPath, normalized);
        return new(status, normalized, digest);
    }

    [SuppressMessage("Globalization", "CA1308", Justification = "Lowercase hexadecimal preserves the existing canonical digest representation.")]
    private static string FileDigest(string repositoryPath, string normalizedPath)
    {
        var absolute = Path.Combine(repositoryPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absolute)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(absolute))).ToLowerInvariant()
            : "missing";
    }

    private static string Git(string repo, string id, List<VerifyFinding> findings, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repo };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            var result = CisProcessSafety.Run(start, TimeSpan.FromMilliseconds(GitCommandTimeoutMilliseconds));
            if (result.TimedOut)
            {
                findings.Add(new("error", "CIS-VERIFY-GIT-TIMEOUT",
                    $"Git command exceeded {GitCommandTimeoutMilliseconds / 1000} seconds: git {string.Join(' ', args)}", id));
                return string.Empty;
            }
            if (result.ExitCode == 0) return result.StandardOutput;
            findings.Add(new("error", "CIS-VERIFY-GIT", result.StandardError.Trim(), id));
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { findings.Add(new("error", "CIS-VERIFY-GIT", e.Message, id)); }
        return string.Empty;
    }

    private static Scope PlannedScope(CisRepositoryContext context, string change)
    {
        var root = Path.Combine(context.DocumentationPath, "changes", change, "agent-tasks");
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var paths = new HashSet<TargetPath>();
        if (!Directory.Exists(root)) return new(repos, paths);
        foreach (var file in Directory.EnumerateFiles(root, "*.md"))
        {
            var lines = File.ReadAllLines(file);
            var targets = lines.FirstOrDefault(x => x.StartsWith("targets:", StringComparison.OrdinalIgnoreCase));
            if (targets is not null) foreach (Match m in Regex.Matches(targets, "[\\\"'](?<id>[^\\\"']+)[\\\"']")) repos.Add(m.Groups["id"].Value.Trim());
            var inOutputs = false;
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, @"^##\s+Required outputs\s*$", RegexOptions.IgnoreCase)) { inOutputs = true; continue; }
                if (inOutputs && line.StartsWith("## ", StringComparison.Ordinal)) inOutputs = false;
                var explicitTarget = Regex.IsMatch(line, @"^\s*(?:[-*]\s*)?Targets?(?:\s+files?|\s+paths?)?\s*[:`]", RegexOptions.IgnoreCase);
                if (!inOutputs && !explicitTarget) continue;
                foreach (Match m in Regex.Matches(line, @"`(?<p>[^`\r\n]+[/\\][^`\r\n]+)`"))
                {
                    var value = m.Groups["p"].Value; if (value.Contains(' ', StringComparison.Ordinal) || Uri.TryCreate(value, UriKind.Absolute, out _)) continue;
                    var split = value.IndexOf("::", StringComparison.Ordinal);
                    paths.Add(split > 0 ? new(value[..split], Normalize(value[(split + 2)..])) : new(null, Normalize(value)));
                }
            }
        }
        return new(repos, paths);
    }

    private static bool PathMatches(TargetPath target, VerifyFileChange actual, string authority)
        => (target.RepositoryId is null || target.RepositoryId.Equals(RepoId(actual, authority), StringComparison.OrdinalIgnoreCase))
           && target.Path.Equals(actual.Path, StringComparison.OrdinalIgnoreCase);
    private static string RepoId(VerifyFileChange item, string authority) => string.IsNullOrWhiteSpace(item.RepositoryId) ? authority : item.RepositoryId;
    private static VerifySnapshot? ReadSnapshot(CisRepositoryContext context, string change, List<VerifyFinding> findings)
    {
        var path = SnapshotPath(context, change); if (!File.Exists(path)) { findings.Add(new("error", "CIS-VERIFY-SNAPSHOT", "Run `cis verify diff` first.", null)); return null; }
        try { return JsonSerializer.Deserialize<VerifySnapshot>(File.ReadAllText(path), JsonOptions); }
        catch (JsonException e) { findings.Add(new("error", "CIS-VERIFY-SNAPSHOT", e.Message, Relative(context, path))); return null; }
    }
    private static bool HasVerificationDisposition(string text)
    {
        var status = Regex.Match(text, @"(?im)^task_status:\s*(?<v>[^\r\n]+)").Groups["v"].Value;
        var category = Regex.Match(text, @"(?im)^category:\s*(?<v>[^\r\n]+)").Groups["v"].Value;
        if (PlanTaskDispositionPolicy.IsTerminal(status, category)) return true;
        return Regex.IsMatch(text, @"(?im)^task_status:\s*InProgress\s*$") && Regex.IsMatch(text, @"(?im)^task_type:\s*core\.delivery\.final-sweep\s*$")
            && !Regex.IsMatch(text, @"(?m)^\s*-\s*\[\s\]\s+") && Regex.IsMatch(text, @"(?im)^\|[^\r\n]*\|\s*Passed\s*\|");
    }
    private void ValidateFeatureAuthority(CisRepositoryContext context, string planPath, string planText,
        List<VerifyFinding> findings)
    {
        var match = Regex.Match(planText, @"(?im)^feature_spec_path:\s*(?<value>[^\r\n]+)$");
        if (!match.Success) return;
        var featurePath = match.Groups["value"].Value.Trim().Trim('"', '\'');
        if (featurePath.Length == 0) return;
        var applicable = _featureAuthorities.Select(authority => authority.Evaluate(context.RepositoryPath, featurePath))
            .Where(result => result.Applicable).ToArray();
        if (applicable.Length == 0) return;
        if (applicable.Length > 1)
        {
            findings.Add(new("error", "CIS-VERIFY-FEATURE-AUTHORITY",
                "Multiple feature-approval authorities apply; resolve the authority conflict before verification.",
                Relative(context, planPath)));
            return;
        }
        if (applicable[0].Ready) return;
        var message = applicable[0].Errors.Count > 0
            ? string.Join(" ", applicable[0].Errors)
            : "The governed feature approval is not current enough for final verification.";
        findings.Add(new("error", "CIS-VERIFY-FEATURE-AUTHORITY", message, Relative(context, planPath)));
    }
    private void ValidateReconciledTesting(CisRepositoryContext context, string change, string verificationPath,
        List<VerifyFinding> findings)
    {
        var text = File.Exists(verificationPath) ? File.ReadAllText(verificationPath) : string.Empty;
        var run = Regex.Match(text, @"(?im)^test_run_id:\s*(?<value>[^\r\n]+)$").Groups["value"].Value.Trim().Trim('"', '\'');
        if (run.Length == 0)
        {
            findings.Add(new("error", "CIS-VERIFY-TEST-RUN", "Feature verification must name a reconciled `test_run_id`.", Relative(context, verificationPath)));
            return;
        }
        var result = _testing!.Trace(context.RepositoryPath, change, run);
        foreach (var diagnostic in result.Diagnostics.Where(item => item.StartsWith("ERROR:", StringComparison.Ordinal)))
            findings.Add(new("error", "CIS-VERIFY-AUTOMATED-EXECUTION", diagnostic[6..].Trim(), Relative(context, verificationPath)));
        if (result.Manifest is null) return;
        var exceptions = Regex.Matches(text, @"(?im)^test_exception:\s*(?<suite>[^|\r\n]+)\|(?<reviewer>[^|\r\n]+)\|(?<reason>[^\r\n]+)$")
            .Cast<Match>().Select(match => match.Groups["suite"].Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var suite in result.Manifest.Suites)
        {
            if (suite.Status is "failed" or "invalid-evidence" or "findings")
                findings.Add(new("error", "CIS-VERIFY-TEST-SUITE", $"Suite '{suite.SuiteId}' is {suite.Status}.", suite.SuiteId));
            else if (suite.Status == "unavailable" && !exceptions.Contains(suite.SuiteId))
                findings.Add(new("error", "CIS-VERIFY-TEST-UNAVAILABLE", $"Suite '{suite.SuiteId}' is unavailable without a bounded human-approved exception.", suite.SuiteId));
        }
        var assuranceTasks = Path.Combine(context.DocumentationPath, "changes", change, "agent-tasks");
        var claimsAssurance = Directory.Exists(assuranceTasks) && Directory.EnumerateFiles(assuranceTasks, "*.md")
            .Any(path => File.ReadAllText(path).Contains("core.assurance.independent", StringComparison.OrdinalIgnoreCase));
        if (claimsAssurance && (string.IsNullOrWhiteSpace(result.Manifest.AssuranceTechnique)
            || result.Manifest.AssuranceTechnique.Equals("repeated-run", StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(result.Manifest.Assurer)
                || result.Manifest.Assurer.Equals(result.Manifest.Implementer, StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("error", "CIS-VERIFY-INDEPENDENCE", "Independent assurance requires a distinct assurer or a genuinely independent mechanical technique.", Relative(context, verificationPath)));
    }
    private static VerifyResult Invalid(VerifyResult result, string code, string message) => result with { Status = "invalid", Findings = result.Findings.Append(new VerifyFinding("error", code, message, null)).ToArray() };
    private static string Digest(IEnumerable<VerifyFileChange> files) => Sha(string.Join("\n", files.Select(x => $"{x.RepositoryId}\t{x.Status}\t{x.Path}\t{x.ContentDigest}")));
    private static string[] ReadEvidence(string path) => File.Exists(path)
        ? File.ReadLines(path).Where(x => x.TrimStart().StartsWith('|') && !x.Contains("---", StringComparison.Ordinal)).ToArray()
        : [];
    private static string SnapshotPath(CisRepositoryContext c, string id) => Path.Combine(c.RepositoryPath, RootPath.Replace('/', Path.DirectorySeparatorChar), id, "snapshot.json");
    private static string Relative(CisRepositoryContext c, string path) => Path.GetRelativePath(c.RepositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');
    private static string Esc(string? value) => (value ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
    [SuppressMessage("Globalization", "CA1308", Justification = "Lowercase hexadecimal preserves the existing canonical digest representation.")]
    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + ".tmp"; File.WriteAllText(temp, text); File.Move(temp, path, true); }
    private static VerifyResult New(CisRepositoryContext? c, string id, VerifySnapshot? s, IReadOnlyList<VerifyFinding> f, IReadOnlyList<string> e, bool a, string status) => new(status, c?.RepositoryPath, id, s, f, e, a);
    private sealed record Scope(HashSet<string> Repositories, HashSet<TargetPath> Paths);
    private sealed record TargetPath(string? RepositoryId, string Path) { public string Display => RepositoryId is null ? Path : $"{RepositoryId}::{Path}"; }
}
