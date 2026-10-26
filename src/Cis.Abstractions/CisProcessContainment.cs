using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Cis.Abstractions;

/// <summary>
/// Owns a Windows job whose children cannot outlive its handle. Assign a trusted
/// gated launcher before releasing it; attaching an already-running arbitrary
/// command cannot close the child-creation race.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CisProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;

    public CisProcessJob()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        handle = Native.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            handle.Dispose();
            throw error;
        }
        var limits = new ExtendedLimits
        {
            Basic = new BasicLimits { LimitFlags = 0x00002000 } // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };
        if (!Native.SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            handle.Dispose();
            throw error;
        }
    }

    public void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        if (!Native.AssignProcessToJobObject(handle, process.SafeHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public int ActiveProcessCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            if (!Native.QueryInformationJobObject(handle, 1, out var accounting,
                    (uint)Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return checked((int)accounting.ActiveProcesses);
        }
    }

    public void Terminate()
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        if (!Native.TerminateJobObject(handle, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
            ref ExtendedLimits information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass,
            out BasicAccounting information, uint length, IntPtr returnedLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    }
}

/// <summary>
/// Observes a trusted Linux launcher started with setsid and terminates live
/// process groups in that session. This is not a sandbox for self-detaching code.
/// Zombies are already terminal and are excluded; the caller still reaps its root.
/// </summary>
[SupportedOSPlatform("linux")]
public static class CisProcessSession
{
    public static bool IsSessionLeader(int processId)
    {
        RequireLinux(processId);
        int session = Native.GetSessionId(processId);
        if (session < 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 3) return false; // ESRCH: process has exited.
            throw new Win32Exception(error);
        }
        return session == processId;
    }

    public static int[] GetLiveProcessIds(int sessionId)
        => ReadMembers(sessionId).Select(member => member.Id).ToArray();

    public static void Terminate(int sessionId)
    {
        RequireLinux(sessionId);
        int ownSession = Native.GetSessionId(0);
        if (ownSession < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (ownSession == sessionId)
            throw new InvalidOperationException("Refusing to terminate the caller's process session.");

        // Include groups created inside the owned session, not only its leader's
        // original group. Recheck membership before signaling each group.
        foreach (int group in ReadMembers(sessionId).Select(member => member.Group).Distinct())
        {
            if (!ReadMembers(sessionId).Any(member => member.Group == group)) continue;
            if (Native.Kill(-group, 9) != 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 3) throw new Win32Exception(error);
            }
        }
    }

    private static List<Member> ReadMembers(int sessionId)
    {
        RequireLinux(sessionId);
        var members = new List<Member>();
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int id)) continue;
            string stat;
            try { stat = File.ReadAllText(Path.Combine(directory, "stat")); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException)
            {
                // procfs can fail a read after the process disappears. Skip only
                // when the kernel confirms that PID is gone, not other I/O errors.
                if (Native.GetSessionId(id) < 0 && Marshal.GetLastWin32Error() == 3) continue;
                throw;
            }
            // comm may contain spaces and parentheses; fields after its last ')'
            // begin with state, ppid, pgrp, session (proc_pid_stat(5)).
            int end = stat.LastIndexOf(')');
            if (end < 0) throw new InvalidDataException("Malformed process stat record.");
            string[] fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int group) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int session))
                throw new InvalidDataException("Malformed process session record.");
            if (session == sessionId && fields[0] is not "Z" and not "X")
            {
                if (group <= 1) throw new InvalidDataException("Invalid owned process group.");
                members.Add(new Member(id, group));
            }
        }
        return members;
    }

    private static void RequireLinux(int id)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (id <= 1) throw new ArgumentOutOfRangeException(nameof(id));
    }

    private sealed record Member(int Id, int Group);

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getsid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int GetSessionId(int processId);

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Kill(int processGroup, int signal);
    }
}
