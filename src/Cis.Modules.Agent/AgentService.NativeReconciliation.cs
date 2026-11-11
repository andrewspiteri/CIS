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
            if (execution.NativeContainment is not { } containment
                || execution.ProcessId is not { } processId || execution.ProcessStartedAtUtc is not { } started
                || !NativeRecordedIdentityStopped(new(owner.HostId, processId, started)))
            {
                diagnostics.Add("Launched native work requires its recorded stopped driver and actual owned job/session binding. Historical unbound drivers and missing process metadata remain held; PID absence alone does not establish stopped children.");
                return null;
            }
            try
            {
                var empty = OperatingSystem.IsWindows() ? CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(containment)
                    : OperatingSystem.IsLinux() ? NativeLinuxSessionEmpty(containment)
                    : throw new PlatformNotSupportedException("Native reconciliation supports Windows and Linux.");
                if (!empty)
                { diagnostics.Add("The recorded native job/session still owns active processes; retain its work and storage reservations."); return null; }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException)
            {
                diagnostics.Add("The exact native job/session cannot be verified empty from its retained binding: " + error.Message);
                return null;
            }
        }
        return tree;
    }

    private static bool NativeLinuxSessionEmpty(CisNativeWorkContainment containment)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        return CisProcessSession.GetLiveProcessIds(NativeLinuxSessionId(containment)).Length == 0;
    }

    private static int NativeLinuxSessionId(CisNativeWorkContainment containment)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (containment.Protocol != CisNativeWorkContainment.LinuxOwnedSession
            || containment.LinuxSessionId is not > 1
            || containment.LinuxSessionStartTicks is null or < 0)
            throw new InvalidDataException("The actual Linux worker session and its leader start identity were not recorded before target launch. Retire the disposable environment rather than infer child closure from a driver PID.");
        var session = containment.LinuxSessionId.Value;
        try
        {
            if (CisProcessSession.GetStartTicks(session) != containment.LinuxSessionStartTicks.Value)
                throw new InvalidDataException("The recorded Linux session leader PID has been reused; no replacement process or session was targeted.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { /* The leader can exit while its retained session still owns children. */ }
        return session;
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
