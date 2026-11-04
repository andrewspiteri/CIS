using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    public CisAgentWorkResult AllocateWork(string repositoryPath, string changeId, string inputPath, string actor, string reason)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            if (!WorkRequired(context)) return WorkFailure(["Adopt the existing engineering policy before allocating governed direct-task work."]);
            var request = ReadWorkInput<CisAgentWorkAllocationRequest>(context, inputPath);
            if (request.Tasks is null || request.Tasks.Count is < 1 or > 1000)
                return WorkFailure(["Supply a bounded list of canonical task allocations."]);
            var identities = request.Tasks.Select(task => WorkIdentity(context, changeId, task.TaskId, diagnostics, accountingOnly: true)).ToArray();
            if (diagnostics.Count > 0 || identities.Any(identity => identity is null)) return WorkFailure(diagnostics);
            var canonical = identities.Select(identity => identity!).ToArray();
            request = request with { Tasks = request.Tasks.Select((task, index) => task with { TaskId = canonical[index].TaskId }).ToArray() };
            return _workLedger.Allocate(context, changeId.ToUpperInvariant(), request, canonical, actor, reason,
                ReadRunManifests(context).Any(run => run.Work is not null));
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }

    public CisAgentWorkResult WorkStatus(string repositoryPath, string changeId, string? taskId = null)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        if (!WorkId(changeId) || taskId is not null && !WorkId(taskId)) return WorkFailure(["Work identities must be canonical change/task identifiers."]);
        return _workLedger.Read(context, changeId.ToUpperInvariant(), taskId?.ToUpperInvariant());
    }

    public CisAgentWorkResult RecordWorkProgress(string repositoryPath, string changeId, string taskId, string inputPath, string actor)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            var identity = WorkIdentity(context, changeId, taskId, diagnostics, accountingOnly: true);
            return identity is null || diagnostics.Count > 0 ? WorkFailure(diagnostics)
                : _workLedger.Progress(context, identity, ReadWorkInput<CisAgentWorkProgressRequest>(context, inputPath), actor);
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }

    public CisAgentWorkResult DisposeWork(string repositoryPath, string changeId, string taskId, string inputPath, string actor, string reason)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            var request = ReadWorkInput<CisAgentWorkDispositionRequest>(context, inputPath);
            // Reconciliation concerns retained accounting, even after the canonical task
            // has moved or ceased to be eligible for a new execution.
            var identity = request.Kind == "reconcile" && request.ExecutionId is not null
                ? WorkId(changeId) && WorkId(taskId)
                    ? new CisAgentWorkIdentity(context.RepositoryId, changeId.ToUpperInvariant(), taskId.ToUpperInvariant(), [], [])
                    : throw new InvalidDataException("Reconciliation requires canonical change/task identifiers.")
                : WorkIdentity(context, changeId, taskId, diagnostics, accountingOnly: true);
            return identity is null || diagnostics.Count > 0 ? WorkFailure(diagnostics)
                : _workLedger.Disposition(context, identity, request, actor, reason);
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }

    private static T ReadWorkInput<T>(CisRepositoryContext context, string inputPath)
    {
        var path = Path.GetFullPath(inputPath, context.RepositoryPath);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 128 * 1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Controller input must be a regular UTF-8 JSON file no larger than 128 KiB.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions(JsonOptions)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Controller input must contain the expected JSON object.");
    }

    private static bool WorkException(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or OverflowException;

    private static CisAgentWorkResult WorkFailure(IEnumerable<string> errors)
        => new("held", null, errors.ToArray(), false);

    private static bool WorkId(string value)
        => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,99}\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private bool WorkRequired(CisRepositoryContext context)
        => CisEngineeringPolicy.Read(context.RepositoryPath)?.RequireTaskCompletion == true
            || File.Exists(Path.Combine(context.RepositoryPath, AgentWorkLedger.RelativePath))
            || File.Exists(Path.Combine(context.RepositoryPath, AgentWorkLedger.AdoptionPath))
            || ReadRunManifests(context).Any(run => run.Work is not null);

    private CisAgentWorkIdentity? WorkIdentity(CisRepositoryContext context, string changeId, string taskId,
        List<string> diagnostics, bool accountingOnly = false)
    {
        if (!WorkId(changeId) || !WorkId(taskId))
        { diagnostics.Add("ERROR: Work ownership requires safe canonical change and task identities."); return null; }
        var root = Path.Combine(context.DocumentationPath, "changes", changeId, "agent-tasks");
        if (!CisPathSafety.IsUnderRoot(context.RepositoryPath, root) || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, root))
            throw new InvalidDataException("Work ownership must remain inside its authority task directory.");
        string? Find(string id)
        {
            if (!Directory.Exists(root)) return null;
            var files = Directory.EnumerateFiles(root, "*.md").Take(1001).ToArray();
            if (files.Length > 1000) throw new InvalidDataException("Resolve a bounded task directory before allocating work.");
            var matches = files.Where(path =>
            {
                var total = 0;
                var content = ReadAuthorityArtifact(context, Relative(context.RepositoryPath, path), ref total).Content;
                var canonical = FrontMatter(content, "task_id") ?? Path.GetFileNameWithoutExtension(path);
                return canonical.Equals(id, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(path).Equals(id, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Canonical work task identity is ambiguous: " + id);
            return matches.SingleOrDefault();
        }
        var taskPath = Find(taskId);
        if (taskPath is null) { diagnostics.Add("ERROR: No canonical approved task was found for work ownership: " + taskId); return null; }
        var bytes = 0;
        var task = ReadAuthorityArtifact(context, Relative(context.RepositoryPath, taskPath), ref bytes).Content;
        ValidateTaskEligibility(context, changeId, task, diagnostics, accountingOnly: accountingOnly);
        var canonicalTask = (FrontMatter(task, "task_id") ?? Path.GetFileNameWithoutExtension(taskPath)).ToUpperInvariant();
        var parents = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { canonicalTask };
        var current = task;
        while (FrontMatter(current, "parent_id") is { } parent && parent is not ("none" or "None" or "-" or "null"))
        {
            if (!WorkId(parent) || !seen.Add(parent) || parents.Count >= 32) throw new InvalidDataException("Canonical task ancestry is unsafe, cyclic or over the 32-parent bound.");
            var parentPath = Find(parent) ?? throw new InvalidDataException("Canonical accounting parent task is missing: " + parent);
            bytes = 0; current = ReadAuthorityArtifact(context, Relative(context.RepositoryPath, parentPath), ref bytes).Content;
            parents.Add((FrontMatter(current, "task_id") ?? Path.GetFileNameWithoutExtension(parentPath)).ToUpperInvariant());
        }
        return new(context.RepositoryId, changeId.ToUpperInvariant(), canonicalTask, parents,
            new[] { Relative(context.RepositoryPath, taskPath), Relative(context.RepositoryPath, Path.Combine(context.DocumentationPath, "changes", changeId, "plan.md")),
                Relative(context.RepositoryPath, Path.Combine(context.DocumentationPath, "changes", changeId, "impact.md")) }
                .Concat(FrontMatterArray(task, "requirement_ids")).Distinct(StringComparer.Ordinal).ToArray());
    }

    private WorkExecutionScope? AdmitWork(CisRepositoryContext context, string changeId, string taskId,
        string executionId, int timeoutSeconds, string? parentExecutionId, string? logicalAttemptId, List<string> diagnostics,
        out CisAgentWorkView? admissionView)
    {
        admissionView = null;
        try
        {
            if (!WorkRequired(context)) return null;
            var identity = WorkIdentity(context, changeId, taskId, diagnostics);
            if (identity is null || diagnostics.Count > 0) return null;
            var admission = _workLedger.Admit(context, identity, executionId, timeoutSeconds, RuntimeCeiling(context), parentExecutionId, logicalAttemptId);
            admissionView = admission.Result.Work;
            if (!admission.Admitted) { WorkDiagnostics(admission.Result, diagnostics); return null; }
            return new(this, context, admission.Reference!, admission.TimeoutSeconds, diagnostics);
        }
        catch (Exception error) when (WorkException(error))
        { diagnostics.Add("ERROR: Work admission could not establish authority and remaining capacity: " + error.Message); return null; }
    }

    private static int RuntimeCeiling(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "agent-provider-profile.md");
        if (!File.Exists(path)) return 3600;
        if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, path) || new FileInfo(path).Length > 128 * 1024)
            throw new InvalidDataException("The adopted provider runtime profile must be a bounded regular document.");
        var match = Regex.Match(File.ReadAllText(path), @"(?m)^\|\s*maximum-run-seconds\s*\|\s*(?<value>[^|]+)\|",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return 3600;
        if (!int.TryParse(match.Groups["value"].Value.Trim(), out var value) || value is < 30 or > 3600)
            throw new InvalidDataException("The provider profile maximum-run-seconds must be between 30 and the adopted 3600-second ceiling.");
        return value;
    }

    private static void ValidateRuntimeCeiling(CisRepositoryContext context, int seconds, List<string> diagnostics)
    {
        try { if (seconds > RuntimeCeiling(context)) diagnostics.Add("ERROR: The requested timeout exceeds the current provider-profile runtime ceiling."); }
        catch (Exception error) when (WorkException(error)) { diagnostics.Add("ERROR: Provider runtime policy could not be verified: " + error.Message); }
    }

    private static void WorkDiagnostics(CisAgentWorkResult result, List<string> diagnostics)
    {
        if (result.ExitCode == 0) return;
        diagnostics.AddRange(result.Errors.Select(error => "ERROR: Work control: " + error));
        if (result.Errors.Count == 0) diagnostics.Add("ERROR: Work control: " + (result.Work?.HoldReason ?? result.Work?.NextAction ?? "An authorized work allocation is required before this direct task can launch."));
    }

    private CisAgentWorkView? WorkView(CisRepositoryContext context, AgentRunManifest? manifest)
        => manifest?.Work is { } reference ? _workLedger.Read(context, reference.ChangeId, reference.TaskId).Work : null;

    private AgentResult CompleteWorkResult(CisRepositoryContext context, AgentResult result, WorkExecutionScope? work)
    {
        work?.Dispose();
        return work is null ? result : result with { Work = _workLedger.Read(context, work.Reference.ChangeId, work.Reference.TaskId).Work };
    }

    private static bool IsDirectWorkRun(AgentRunManifest manifest, AgentTaskEnvelope envelope)
        => manifest.ExecutionRoute == "direct-task" || manifest.Work is not null
            || manifest.ExecutionRoute is null && manifest.ChangeId != ProductAuthoringChange
                && envelope.CanonicalTaskPath.Replace('\\', '/').Contains("/changes/" + manifest.ChangeId + "/agent-tasks/", StringComparison.OrdinalIgnoreCase);

    private sealed class WorkExecutionScope : IDisposable
    {
        private readonly AgentService service;
        private readonly CisRepositoryContext context;
        private readonly List<string> diagnostics;
        private readonly DateTimeOffset started;
        private readonly int seconds;
        private bool launched, returned, uncertainIdentity, attached, settled;
        private int? processId;
        private string? processStarted;
        public CisAgentWorkReference Reference { get; }

        internal WorkExecutionScope(AgentService service, CisRepositoryContext context, CisAgentWorkReference reference, int seconds, List<string> diagnostics)
        {
            this.service = service; this.context = context; Reference = reference; this.seconds = seconds; this.diagnostics = diagnostics;
            var execution = service._workLedger.Read(context, reference.ChangeId, reference.TaskId).Work?.Executions.SingleOrDefault(item => item.ExecutionId == reference.ExecutionId);
            started = execution is not null && DateTimeOffset.TryParse(execution.StartedAtUtc, out var at) ? at : service._clock();
        }

        internal TimeSpan RemainingProviderTime()
            => TimeSpan.FromTicks(Math.Max(0, TimeSpan.FromSeconds(seconds).Ticks - Math.Max(0, (service._clock() - started).Ticks)));
        internal void Launch()
        {
            var result = service._workLedger.MarkLaunchIntent(context, Reference);
            WorkDiagnostics(result, diagnostics);
            if (result.ExitCode != 0) throw new InvalidDataException("The durable launch intent could not be recorded.");
            launched = true;
        }
        internal void Process(CisAgentProviderEvent item)
        {
            if (uncertainIdentity) return;
            if (item.ProcessId is null && item.ProcessStartedAtUtc is null) return;
            if (item.ProcessId is not { } id || item.ProcessStartedAtUtc is not { } at)
            {
                uncertainIdentity = true;
                WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference,
                    "The reported process identity is incomplete; bounded reconciliation is required."), diagnostics);
                return;
            }
            if (processId == id && processStarted == at && attached) return;
            if (processId is not null && (processId != id || processStarted != at))
            {
                uncertainIdentity = true;
                WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference,
                    "The owned process identity changed during execution; bounded reconciliation is required."), diagnostics);
                return;
            }
            processId = id; processStarted = at;
            if (!ProcessIdentityMatches(id, at))
            {
                uncertainIdentity = true;
                WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference, "The reported owned process identity could not be verified."), diagnostics);
            }
            else
            {
                var result = service._workLedger.AttachProcess(context, Reference, id, at);
                attached = result.ExitCode == 0;
                // The durable launch intent keeps capacity reserved. A busy attachment
                // can retry on the next event without turning valid output into failure.
                if (result.Status != "busy") WorkDiagnostics(result, diagnostics);
            }
        }
        internal void Returned() => returned = true;
        internal void Finish(string status, string? summary, IReadOnlyList<string>? evidence)
        {
            var stopped = !uncertainIdentity && (processId is { } id
                ? OwnedProcessStopped(id, processStarted)
                : !launched || returned);
            var result = service._workLedger.Settle(context, Reference, status, stopped, summary, evidence);
            WorkDiagnostics(result, diagnostics);
            settled = result.ExitCode == 0 || result.Applied;
        }
        public void Dispose()
        {
            if (settled) return;
            if (!launched || returned || processId is { } id && OwnedProcessStopped(id, processStarted))
                Finish(launched ? "interrupted" : "preparation-ended", "Known preparation/execution cost retained after a stopped controller boundary.", []);
            else WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference,
                "Launch intent exists without controller-observed completion. Inspect the owned process before releasing capacity."), diagnostics);
        }
    }
}
