using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cis.Abstractions;

public static partial class CisProcessSafety
{
    /// <summary>Checks process ancestry only; callers also verify the ancestor's retained start identity.</summary>
    public static bool IsSameOrDescendant(int processId, int ancestorProcessId)
    {
        if (processId <= 1 || ancestorProcessId <= 1) return false;
        if (processId == ancestorProcessId) return true;
        try
        {
            var parents = OperatingSystem.IsWindows() ? WindowsParents() : null;
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return false;
            var visited = new HashSet<int>();
            for (var depth = 0; depth < 128 && processId > 1 && visited.Add(processId); depth++)
            {
                if (processId == ancestorProcessId) return true;
                processId = parents is not null ? parents.GetValueOrDefault(processId) : LinuxParent(processId);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or System.ComponentModel.Win32Exception or ArgumentException or OverflowException)
        { }
        return false;
    }

    private static int LinuxParent(int processId)
    {
        var path = "/proc/" + processId.ToString(CultureInfo.InvariantCulture) + "/stat";
        using var reader = new StreamReader(File.OpenRead(path));
        var buffer = new char[16_384];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        if (length == buffer.Length || reader.Read() != -1) return 0;
        var text = new string(buffer, 0, length);
        var close = text.LastIndexOf(')');
        if (close < 0 || close + 2 >= text.Length) return 0;
        var fields = text[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent) ? parent : 0;
    }

    private static Dictionary<int, int> WindowsParents()
    {
        using var snapshot = NativeProcessSnapshot.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var entry = new NativeProcessSnapshot.Entry { Size = (uint)Marshal.SizeOf<NativeProcessSnapshot.Entry>(), Executable = "" };
        if (!NativeProcessSnapshot.Process32First(snapshot, ref entry))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var parents = new Dictionary<int, int>();
        do
        {
            if (entry.ProcessId <= int.MaxValue && entry.ParentProcessId <= int.MaxValue)
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        } while (NativeProcessSnapshot.Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return parents;
    }

    private static class NativeProcessSnapshot
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Entry
        {
            internal uint Size, Usage, ProcessId;
            internal UIntPtr DefaultHeapId;
            internal uint ModuleId, Threads, ParentProcessId;
            internal int BasePriority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(SafeFileHandle snapshot, ref Entry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(SafeFileHandle snapshot, ref Entry entry);
    }
}
