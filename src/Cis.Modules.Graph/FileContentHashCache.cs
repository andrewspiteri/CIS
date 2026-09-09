using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace Cis.Modules.Graph;

/// <summary>Disposable graph database hashes on a local NTFS volume. Source inputs are always hashed.</summary>
internal sealed class FileContentHashCache
{
    private const string RelativePath = ".cis/local/status/graph-database-hash.json";
    private const int Version = 1;
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly AsyncLocal<FileContentHashCache?> Current = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly uint? _volume;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _dirty;

    private FileContentHashCache(string root)
    {
        _root = Path.GetFullPath(root);
        _volume = LocalNtfsVolume(_root);
        if (_volume is null) return;
        try
        {
            var path = Path.Combine(_root, RelativePath);
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return;
            var cached = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), JsonOptions);
            if (cached is null || cached.Version != Version || cached.Root != _root || cached.Entries is null) return;
            foreach (var (key, entry) in cached.Entries)
                if (entry is not null && entry.Hash is { Length: 64 } && entry.Hash.All(char.IsAsciiHexDigit))
                    _entries[key] = entry;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
    }

    internal static IDisposable Enter(string repositoryPath)
    {
        var previous = Current.Value;
        var cache = CisReadScope.Read(typeof(FileContentHashCache), "content-hashes", repositoryPath,
            () => new FileContentHashCache(repositoryPath));
        Current.Value = cache;
        return new Lease(cache, previous);
    }

    internal static string Hash(string path)
        => Current.Value is { } cache ? cache.ReadHash(path) : HashFile(path);

    private string ReadHash(string path)
    {
        if (_volume is null) return HashFile(path);
        // Open with read access, denying writers and replacements while checking metadata.
        // Attribute-only handles do not provide the same sharing/access guarantees.
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var before = Stamp(handle);
        if (before is null || before.Value.Volume != _volume) return HashFile(path);
        var key = Path.GetRelativePath(_root, path);
        lock (_entries)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.Stamp == before.Value) return entry.Hash;
            var hash = HashFile(path);
            if (Stamp(handle) == before)
            {
                _entries[key] = new(before.Value, hash);
                _dirty = true;
            }
            return hash;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            new FileInfo(path).Length >= 1024 * 1024 ? 1024 * 1024 : 4096, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private void Save()
    {
        lock (_entries)
        {
            if (!_dirty) return;
            _dirty = false;
            string? temporary = null;
            try
            {
                var content = JsonSerializer.SerializeToUtf8Bytes(new Envelope(Version, _root, _entries), JsonOptions);
                if (content.Length > MaximumBytes) return;
                var path = Path.Combine(_root, RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    private static uint? LocalNtfsVolume(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var root = Path.GetPathRoot(path)!;
            if (new DriveInfo(root).DriveType != DriveType.Fixed) return null;
            var format = new StringBuilder(32);
            return GetVolumeInformation(root, null, 0, out var volume, out _, out _, format, format.Capacity)
                   && format.ToString() == "NTFS" ? volume : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static FileStamp? Stamp(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var identity)
            || !GetFileInformationByHandleEx(handle, 0, out var basic, (uint)Marshal.SizeOf<BasicInformation>())) return null;
        return new(identity.VolumeSerialNumber, identity.FileIndexHigh, identity.FileIndexLow,
            identity.FileSizeHigh, identity.FileSizeLow, basic.CreationTime, basic.LastWriteTime, basic.ChangeTime);
    }

    private sealed class Lease(FileContentHashCache cache, FileContentHashCache? previous) : IDisposable
    {
        public void Dispose()
        {
            Current.Value = previous;
            cache.Save();
        }
    }

    private sealed record Envelope(int Version, string Root, Dictionary<string, Entry> Entries);
    private sealed record Entry(FileStamp Stamp, string Hash);
    private readonly record struct FileStamp(uint Volume, uint IdHigh, uint IdLow, uint SizeHigh, uint SizeLow,
        long Created, long Written, long Changed);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetVolumeInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, int nameLength,
        out uint serial, out uint maximumComponentLength, out uint flags, StringBuilder format, int formatLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInformation information);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out BasicInformation information, uint size);
}
