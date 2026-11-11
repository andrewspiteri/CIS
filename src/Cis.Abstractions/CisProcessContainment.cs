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
    private readonly string? name;
    private readonly string? objectPath;
    private readonly int sessionId;

    public CisProcessJob() : this(null) { }

    public CisProcessJob(string? name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (name is not null) ValidateNativeName(name);
        handle = Native.CreateJobObject(IntPtr.Zero, name);
        var createdError = Marshal.GetLastWin32Error();
        if (handle.IsInvalid)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            handle.Dispose();
            throw error;
        }
        if (name is not null && createdError == 183) // ERROR_ALREADY_EXISTS: never adopt another job.
        {
            handle.Dispose();
            throw new InvalidDataException("The fresh native job identity already exists.");
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
        if (name is not null)
        {
            try
            {
                this.name = name;
                objectPath = ReadObjectPath(handle);
                ObjectNamespace(name, objectPath);
                using var current = Process.GetCurrentProcess();
                sessionId = current.SessionId;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    /// <summary>Capture the original named-object binding from this actual job handle.</summary>
    public CisNativeWorkContainment CaptureNativeContainment()
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        if (name is null || objectPath is null)
            throw new InvalidDataException("Only a named native job can supply its original namespace binding.");
        return new(CisNativeWorkContainment.WindowsOuterJob, name, sessionId) { WindowsObjectPath = objectPath };
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

    public static bool IsNamedNativeJobMember(CisNativeWorkContainment containment, Process process)
    {
        ValidateNativeBinding(containment);
        using var job = Native.OpenJobObject(0x0004, false, containment.JobName); // JOB_OBJECT_QUERY
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        VerifyOpenedBinding(containment, job);
        if (!Native.QueryExtendedJobInformation(job, 9, out var limits, (uint)Marshal.SizeOf<ExtendedLimits>(), IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var flags = limits.Basic.LimitFlags;
        if ((flags & 0x00002000) == 0 || (flags & (0x00000800 | 0x00001000)) != 0)
            return false; // Require kill-on-close and deny both forms of breakaway.
        if (!Native.IsProcessInJob(process.SafeHandle, job, out var member))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return member;
    }

    public static bool IsNamedNativeJobEmptyOrDestroyed(CisNativeWorkContainment containment)
    {
        var originalNamespace = ValidateNativeBinding(containment);
        using var job = Native.OpenJobObject(0x0004, false, containment.JobName);
        if (job.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 2)
            {
                // A missing Win32 name proves nothing about a different namespace.
                // Use the same CreateJobObject API to observe this caller's actual
                // namespace; do not infer it from the session, token flags or GUID.
                using var witness = new CisProcessJob(@"Local\cis-native-" + Guid.NewGuid().ToString("N"));
                if (ObjectNamespace(witness.name!, witness.objectPath!) != originalNamespace)
                    throw new InvalidDataException("The inspecting Windows named-object namespace differs from the recorded original namespace. A missing local job name does not prove its destruction; preserve the execution and reservations.");
                return true;
            }
            throw new Win32Exception(error);
        }
        VerifyOpenedBinding(containment, job);
        if (!Native.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return accounting.ActiveProcesses == 0;
    }

    private static string ValidateNativeBinding(CisNativeWorkContainment containment)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ValidateNativeName(containment.JobName);
        using var current = Process.GetCurrentProcess();
        if (containment.Protocol != CisNativeWorkContainment.WindowsOuterJob || containment.WindowsSessionId != current.SessionId)
            throw new InvalidDataException("Native job inspection requires its exact Windows protocol and original local session.");
        if (containment.WindowsObjectPath is null)
            throw new InvalidDataException("The original Windows named-object namespace was not recorded. Preserve the historical execution and reservations until genuine original namespace proof is available; the current namespace, matching session or PID absence cannot supply that proof.");
        return ObjectNamespace(containment.JobName, containment.WindowsObjectPath);
    }

    private static void VerifyOpenedBinding(CisNativeWorkContainment containment, SafeFileHandle job)
    {
        if (ReadObjectPath(job) != containment.WindowsObjectPath)
            throw new InvalidDataException("The opened Windows job does not match its recorded original named-object path. Preserve the exact execution and reservations.");
    }

    private static string ObjectNamespace(string name, string path)
    {
        var leaf = name[@"Local\".Length..];
        var separator = path.LastIndexOf('\\');
        if (path.Length is < 2 or > 32760 || path[0] != '\\' || path.Contains('\0')
            || separator <= 0 || path[(separator + 1)..] != leaf)
            throw new InvalidDataException("The retained Windows job requires its exact bounded original named-object path.");
        return path[..separator];
    }

    private static string ReadObjectPath(SafeFileHandle job)
    {
        // ObjectNameInformation returns the actual object-manager path. Unsupported
        // native queries hold the operation rather than guessing a Win32 namespace.
        try
        {
            var status = Native.NtQueryObject(job, 1, IntPtr.Zero, 0, out var required);
            if (status is not (unchecked((int)0xC0000004) or unchecked((int)0xC0000023) or unchecked((int)0x80000005))
                || required < Marshal.SizeOf<UnicodeString>() || required > 65536)
                throw new InvalidDataException($"The original Windows job object path could not be queried within its bound (NTSTATUS 0x{status:X8}).");
            var buffer = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                status = Native.NtQueryObject(job, 1, buffer, required, out _);
                if (status != 0)
                    throw new InvalidDataException($"The original Windows job object path is unavailable (NTSTATUS 0x{status:X8}).");
                var value = Marshal.PtrToStructure<UnicodeString>(buffer);
                var offset = value.Buffer.ToInt64() - buffer.ToInt64();
                if (value.Length == 0 || value.Length % 2 != 0 || value.Length > value.MaximumLength
                    || value.Length > required || offset < Marshal.SizeOf<UnicodeString>() || offset > required - value.Length)
                    throw new InvalidDataException("The Windows job object path query returned invalid bounded name information.");
                return Marshal.PtrToStringUni(value.Buffer, value.Length / 2)
                    ?? throw new InvalidDataException("The Windows job object path query returned no name.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException)
        { throw new NotSupportedException("This Windows installation cannot verify the original named-job namespace.", error); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    private static void ValidateNativeName(string name)
    {
        const string prefix = @"Local\cis-native-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidDataException("Native job identity must name its original unique local CIS job.");
    }

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
        [DllImport("ntdll.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int NtQueryObject(SafeFileHandle handle, int informationClass,
            IntPtr information, uint length, out uint returnedLength);

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle OpenJobObject(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);

        [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryExtendedJobInformation(SafeFileHandle job, int informationClass,
            out ExtendedLimits information, uint length, IntPtr returnedLength);

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
    public static long GetStartTicks(int processId)
    {
        RequireLinux(processId);
        var stat = File.ReadAllText("/proc/" + processId.ToString(CultureInfo.InvariantCulture) + "/stat");
        var end = stat.LastIndexOf(')');
        if (end < 0) throw new InvalidDataException("Malformed process stat record.");
        var fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // After comm, state is field 3; starttime is the exact kernel field 22.
        if (fields.Length < 20 || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < 0)
            throw new InvalidDataException("Malformed process start-tick record.");
        return ticks;
    }

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
