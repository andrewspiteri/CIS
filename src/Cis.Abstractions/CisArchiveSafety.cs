using System.IO.Compression;

namespace Cis.Abstractions;

public static class CisArchiveSafety
{
    public static void ExtractZip(
        string archivePath,
        string destination,
        int maximumEntries,
        long maximumExpandedBytes)
    {
        if (maximumEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumExpandedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumExpandedBytes));

        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > maximumEntries)
            throw new InvalidDataException($"Archive exceeds the {maximumEntries} entry limit.");

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(relative)) continue;
            if (!identities.Add(relative))
                throw new InvalidDataException($"Archive contains a duplicate path: {entry.FullName}");
            if (IsSymbolicLink(entry))
                throw new InvalidDataException($"Archive contains a symbolic-link entry: {entry.FullName}");
            if (!CisPathSafety.TryResolveUnderRoot(destination, relative, out var target))
                throw new InvalidDataException($"Archive contains an escaping or invalid path: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            if (entry.Length < 0 || entry.Length > maximumExpandedBytes - expanded)
                throw new InvalidDataException($"Archive exceeds the {maximumExpandedBytes} byte expanded-size limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            expanded += CopyWithLimit(input, output, maximumExpandedBytes - expanded);
        }
    }

    private static long CopyWithLimit(Stream input, Stream output, long remaining)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > remaining) throw new InvalidDataException("Archive expanded-size limit was exceeded while extracting an entry.");
            output.Write(buffer, 0, read);
        }
        return written;
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == UnixSymbolicLink || windowsAttributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
