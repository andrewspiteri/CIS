namespace Cis.Abstractions;

/// <summary>
/// Centralizes repository path containment and traversal-safe enumeration.
/// Lexical containment is platform-aware and recursive enumeration never follows
/// symbolic links, junctions, or other reparse points.
/// </summary>
public static class CisPathSafety
{
    public static StringComparison PlatformComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool TryResolveUnderRoot(
        string root,
        string relative,
        out string absolute,
        bool allowRoot = false)
    {
        absolute = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relative)) return false;

        try
        {
            if (Path.IsPathRooted(relative)) return false;
            var normalized = relative.Replace('\\', '/');
            if (normalized == "." && allowRoot)
            {
                absolute = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return true;
            }
            var segments = normalized.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..")) return false;
            if (OperatingSystem.IsWindows() && normalized.Contains(':')) return false;

            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(fullRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnderRoot(fullRoot, candidate, allowRoot)) return false;
            absolute = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsUnderRoot(string root, string candidate, bool allowRoot = false)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (allowRoot && fullCandidate.Equals(fullRoot, PlatformComparison)) return true;
            return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PlatformComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool ContainsReparsePoint(string root, string candidate)
    {
        if (!IsUnderRoot(root, candidate, allowRoot: true)) return true;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (relative == ".") return false;

        var current = fullRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // Missing targets are checked by the caller; one attribute lookup also
                // avoids two additional filesystem probes for every existing segment.
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool ContainsReparsePointInTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                    IgnoreInaccessible = false,
                    AttributesToSkip = 0,
                }).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }

            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return true; }
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            }
        }
        return false;
    }

    public static IEnumerable<string> EnumerateFiles(string root, string pattern = "*", bool recursive = true) =>
        Directory.EnumerateFiles(root, pattern, Options(recursive));

    public static IEnumerable<string> EnumerateDirectories(string root, string pattern = "*", bool recursive = true) =>
        Directory.EnumerateDirectories(root, pattern, Options(recursive));

    public static IEnumerable<string> EnumerateFileSystemEntries(string root, string pattern = "*", bool recursive = true) =>
        Directory.EnumerateFileSystemEntries(root, pattern, Options(recursive));

    private static EnumerationOptions Options(bool recursive) => new()
    {
        RecurseSubdirectories = recursive,
        ReturnSpecialDirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };
}
