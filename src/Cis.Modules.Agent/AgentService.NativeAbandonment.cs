using System.Diagnostics;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private CisAgentWorkResult AbandonNativeUnit(CisRepositoryContext authority, string changeId, string taskId,
        CisAgentWorkDispositionRequest request, string actor, string reason)
    {
        if (!WorkId(changeId) || !WorkId(taskId) || request.ExecutionId is null)
            return WorkFailure(["Identify the interrupted unit invocation and its original change/task."]);
        var retained = _workLedger.NativeExecution(authority, request.ExecutionId);
        if (retained is null || retained.Value.Reference.ChangeId != changeId.ToUpperInvariant()
            || retained.Value.Reference.TaskId != taskId.ToUpperInvariant())
            return WorkFailure(["This invocation does not belong to the selected change/task."]);
        var reference = retained.Value.Reference;
        var repository = retained.Value.Execution.Stage?.RepositoryPath;
        if (repository is null) return WorkFailure(["The original execution workspace is missing; identify its disposable environment before retiring it."]);
        var errors = new List<string>();
        var binding = ResolveNativeBinding(repository,
            new(changeId, taskId, AuthorityPath: authority.RepositoryPath, Actor: actor), errors, retainedInspection: true);
        if (binding is null || errors.Count > 0) return WorkFailure(errors);
        const string leaseRelative = ".cis/local/testing/mutation/run.lock";
        if (!CisPathSafety.TryResolveUnderRoot(repository, leaseRelative, out var leasePath)
            || CisPathSafety.ContainsReparsePoint(repository, leasePath))
            return WorkFailure(["The native invocation lock is not a regular path in its original workspace."]);
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
        using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var tree = _workLedger.NativeExecutionTree(authority, reference);
        if (request.DisposalBasis is not ("process-cleanup" or "retired-environment"))
            return WorkFailure(["First stop the old unit processes or retire their disposable execution environment. Record disposalBasis as process-cleanup or retired-environment and retain factual evidence."]);
        // The controller is responsible for actually retiring the old executor and
        // shared resources before this disposition. This is disposal, not a claim
        // that absent historical namespace/host/stop metadata has been recovered.
        foreach (var execution in tree.Where(item => !item.Finalized))
        {
            var identities = new List<CisAgentWorkPreparationOwner>();
            // A long-lived host may dispose its own returned invocation under the
            // exclusive native lease. The host need not exit; a live native driver
            // still blocks disposal, including when it shares that process ID.
            if (execution.PreparationOwner is { } preparation)
            {
                var currentController = preparation.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    && preparation.ProcessId == Environment.ProcessId
                    && ProcessIdentityMatches(preparation.ProcessId, preparation.ProcessStartedAtUtc);
                if (!currentController || execution.ProcessId == preparation.ProcessId) identities.Add(preparation);
            }
            if (execution.ProcessId is { } id && execution.ProcessStartedAtUtc is { } started
                && execution.PreparationOwner is { } owner) identities.Add(new(owner.HostId, id, started));
            foreach (var identity in identities.Distinct())
            {
                if (!identity.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    if (request.DisposalBasis == "process-cleanup")
                        errors.Add("Use the original host to clean up its processes, or retire that disposable environment first.");
                    continue;
                }
                if (!AbandonedIdentityStopped(identity))
                    errors.Add("An original unit controller/driver is still live or unreadable. Stop it before disposing of its invocation.");
            }
            if (!execution.LaunchIntent) continue;
            if (request.DisposalBasis != "process-cleanup")
            {
                // A retirement statement cannot override a known live local
                // session. Historical missing bindings remain missing.
                if (OperatingSystem.IsLinux() && execution.NativeContainment is { Protocol: CisNativeWorkContainment.LinuxOwnedSession } known
                    && execution.PreparationOwner?.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) == true)
                    try
                    {
                        if (!NativeLinuxSessionEmpty(known)) errors.Add("The original Linux unit session still owns processes. Stop them before retiring its environment.");
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                        or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException)
                    { errors.Add("The recorded Linux unit session could not be inspected: " + error.Message); }
                continue;
            }
            if (execution.NativeContainment is not { } containment)
            { errors.Add("This invocation has no supported complete process-cleanup receipt. Retire its disposable environment before disposal."); continue; }
            try
            {
                var empty = OperatingSystem.IsWindows() ? CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(containment)
                    : OperatingSystem.IsLinux() ? NativeLinuxSessionEmpty(containment)
                    : throw new PlatformNotSupportedException("Native cleanup supports Windows and Linux.");
                if (!empty) errors.Add("The old unit job/session still owns processes. Stop them before disposal.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException)
            { errors.Add("Process cleanup could not be confirmed: " + error.Message); }
        }
        if (errors.Count > 0) return _workLedger.Read(authority, reference.ChangeId, reference.TaskId) with { Status = "held", Errors = errors };
        return _workLedger.AbandonNativeUnit(authority, reference, request, actor, reason, tree);
    }

    private static bool AbandonedIdentityStopped(CisAgentWorkPreparationOwner identity)
    {
        if (identity.ProcessId <= 0 || !DateTimeOffset.TryParse(identity.ProcessStartedAtUtc, out var started)) return false;
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            // PID reuse is a different process; do not target or stop it.
            return process.StartTime.ToUniversalTime() != started.UtcDateTime || process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return false; }
    }
}
