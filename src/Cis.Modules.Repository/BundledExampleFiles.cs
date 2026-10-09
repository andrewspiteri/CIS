using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

internal sealed record BundledExampleFile(string Path, string Content, string Hash);
internal sealed record BundledExample(string Digest, IReadOnlyList<BundledExampleFile> Files);

/// <summary>Reads bounded UTF-8 reference sources from the CIS distribution, never target assemblies.</summary>
internal static class BundledExampleFiles
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static BundledExample Read(string distribution)
    {
        var root = Path.Combine(distribution, "examples", "dotnet-engineering");
        if (!Directory.Exists(root) || CisPathSafety.ContainsReparsePoint(distribution, root))
            throw new InvalidDataException("The bundled .NET example is missing or linked. Repair the CIS distribution or explicitly select --examples off.");
        var files = new List<BundledExampleFile>();
        long bytes = 0;
        foreach (var path in Enumerate(root).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!ValidRelativePath(relative) || CisPathSafety.ContainsReparsePoint(root, path))
                throw new InvalidDataException("Unsafe bundled example path.");
            var content = ReadText(path);
            bytes += Utf8.GetByteCount(content);
            if (files.Count >= 200 || bytes > 32 * 1024 * 1024)
                throw new InvalidDataException("Bundled example exceeds its file or byte limit.");
            files.Add(new(relative, content, RepositoryExampleInstallation.Hash(content)));
        }
        if (files.Count == 0 || !files.Any(file => file.Path == "README.md"))
            throw new InvalidDataException("The bundled .NET example is empty or has no README.");
        var identity = string.Join('\n', files.Select(file => file.Path + "=" + file.Hash));
        return new(RepositoryExampleInstallation.Hash(identity), files);
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var count = 0;
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 400) throw new InvalidDataException("Bundled example exceeds its entry limit.");
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked bundled example entries are not supported.");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
                else yield return entry;
            }
    }

    internal static bool ValidRelativePath(string? path) => !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path) && !path.Contains(':') && !path.Contains('\\')
        && path.Split('/').All(segment => segment is not ("" or "." or "..") && !segment.Equals(".git", StringComparison.OrdinalIgnoreCase));

    internal static string ReadText(string path)
    {
        ExampleFileSafety.EnsureRegular(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Example file exceeds 1 MiB.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return Utf8.GetString(bytes);
    }
}
