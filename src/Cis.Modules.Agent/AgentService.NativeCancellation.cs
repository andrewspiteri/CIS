using System.Diagnostics;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private CisAgentWorkResult CancelNativeWork(CisRepositoryContext authority, string changeId, string taskId,
        CisAgentWorkDispositionRequest request, string actor, string reason)
    {
        if (request.ExecutionId is null) return WorkFailure(["Native cancellation needs the exact executionId shown by work status."]);
        var retained = _workLedger.NativeExecution(authority, request.ExecutionId);
        if (retained is null || !retained.Value.Reference.ChangeId.Equals(changeId, StringComparison.OrdinalIgnoreCase)
            || !retained.Value.Reference.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase))
            return WorkFailure(["The native execution does not match this exact canonical change/task."]);
        var reference = retained.Value.Reference;
        var requested = _workLedger.RequestNativeCancellation(authority, reference, request.ExpectedRevision, actor, reason);
        if (requested.ExitCode != 0 || requested.Status == "unchanged") return requested;
        var tree = _workLedger.NativeExecutionTree(authority, reference);
        var identities = tree.Where(item => !item.Finalized).SelectMany(item =>
        {
            var values = new List<CisAgentWorkPreparationOwner>();
            if (item.ProcessId is { } id && item.ProcessStartedAtUtc is { } started && item.PreparationOwner is { } controller)
                values.Add(new(controller.HostId, id, started));
            if (item.PreparationOwner is { } preparation) values.Add(preparation);
            return values;
        }).Distinct().ToArray();
        var errors = new List<string>();
        foreach (var identity in identities)
        {
            if (!identity.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            { errors.Add("A recorded native controller belongs to another host and was not targeted."); continue; }
            if (OwnedProcessStopped(identity.ProcessId, identity.ProcessStartedAtUtc)) continue;
            if (!ProcessIdentityMatches(identity.ProcessId, identity.ProcessStartedAtUtc)
                || CisProcessSafety.IsSameOrDescendant(Environment.ProcessId, identity.ProcessId))
            { errors.Add("A changed/unreadable identity or this cancellation caller's own ancestor was not targeted; use an external authorized controller."); continue; }
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (!ProcessIdentityMatches(identity.ProcessId, identity.ProcessStartedAtUtc))
                    throw new InvalidOperationException("The owned identity changed before cancellation.");
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000) || !OwnedProcessStopped(identity.ProcessId, identity.ProcessStartedAtUtc))
                    errors.Add("A recorded native process did not stop within the bounded cancellation wait.");
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            { if (!OwnedProcessStopped(identity.ProcessId, identity.ProcessStartedAtUtc)) errors.Add(error.Message); }
        }
        foreach (var execution in tree.Where(item => !item.Finalized))
        {
            var owned = _workLedger.NativeExecution(authority, execution.ExecutionId);
            if (owned is not null) _workLedger.MarkRecoveryUncertain(authority, owned.Value.Reference,
                "Cancellation stopped only verified recorded identities. Native child cleanup and restoration remain held until their retained evidence is reconciled.");
        }
        var result = _workLedger.Read(authority, reference.ChangeId, reference.TaskId);
        return result with { Status = "held", Errors = errors.Append("Inspect native output/owned-child recovery evidence, then reconcile the exact stopped executions through the existing work disposition route; cancellation alone does not release uncertain reservations.").ToArray() };
    }
}
