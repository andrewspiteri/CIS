using System.Diagnostics;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private IReadOnlyList<CisAgentWorkExecution>? VerifyNativeReconciliation(CisRepositoryContext authority,
        CisAgentWorkReference reference, List<string> diagnostics)
    {
        var tree = _workLedger.NativeExecutionTree(authority, reference);
        if (tree.Count is < 1 or > 4096)
        { diagnostics.Add("Native reconciliation needs its bounded exact retained execution tree."); return null; }
        foreach (var execution in tree.Where(item => !item.Finalized))
        {
            if (execution.Stage?.NativeOperation is null || execution.PreparationOwner is not { } owner
                || !NativeRecordedIdentityStopped(owner))
            {
                diagnostics.Add("Native preparation is live, belongs to another host, or its exact recorded identity is unavailable. Preserve this execution tree's reservations.");
                return null;
            }
            if (!execution.LaunchIntent && execution.ProcessId is null) continue;
            if (!OperatingSystem.IsWindows() || execution.NativeContainment is not { } containment
                || execution.ProcessId is not { } processId || execution.ProcessStartedAtUtc is not { } started
                || !NativeRecordedIdentityStopped(new(owner.HostId, processId, started)))
            {
                diagnostics.Add("Launched native work requires its recorded stopped driver and actual Windows outer-job binding. Linux session ownership, historical unbound drivers and missing process metadata remain held; PID absence alone does not establish stopped children.");
                return null;
            }
            try
            {
                if (!CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(containment))
                { diagnostics.Add("The recorded native Windows job still owns active processes; retain its work and storage reservations."); return null; }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException)
            {
                diagnostics.Add("The exact native Windows job cannot be verified empty in its original named-object namespace and session: " + error.Message);
                return null;
            }
        }
        return tree;
    }

    private static bool NativeRecordedIdentityStopped(CisAgentWorkPreparationOwner identity)
    {
        if (!identity.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || identity.ProcessId <= 0 || !DateTimeOffset.TryParse(identity.ProcessStartedAtUtc, out var started)) return false;
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            // A reused or unreadable PID is not this recorded identity's stopped receipt.
            return process.StartTime.ToUniversalTime() == started.UtcDateTime && process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return false; }
    }
}
