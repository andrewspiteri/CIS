using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed record RepositoryExampleResult(string Name, string Status, string? Destination, IReadOnlyList<string> Files, IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 2;
}

/// <summary>Copies a bundled native-tool example into a new directory, without altering existing project choices.</summary>
public sealed class RepositoryExample(string distributionPath)
{
    public RepositoryExampleResult Export(string repository, string? destination, bool dryRun)
    {
        const string name = "dotnet-engineering";
        var source = Path.Combine(distributionPath, "examples", name);
        if (!Directory.Exists(source)) return new(name, "unavailable", null, [], ["The native .NET example is not bundled in this distribution."]);
        try
        {
            var files = CisPathSafety.EnumerateFiles(source).Order(StringComparer.Ordinal).ToArray();
            var names = files.Select(path => Path.GetRelativePath(source, path).Replace('\\', '/')).ToArray();
            if (files.Length == 0 || files.Length > 200 || files.Any(path => new FileInfo(path).Length > 1024 * 1024))
                throw new InvalidDataException("Bundled example is empty or exceeds its file limits.");
            if (destination is null) return new(name, "available", null, names, []);
            if (!CisPathSafety.TryResolveUnderRoot(repository, destination, out var target)
                || CisPathSafety.ContainsReparsePoint(repository, target) || Directory.Exists(target) || File.Exists(target))
                throw new InvalidDataException("Choose a new directory inside the repository without linked parents; existing paths are never overwritten.");
            if (!dryRun)
                foreach (var path in files)
                {
                    var output = Path.Combine(target, Path.GetRelativePath(source, path));
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    File.Copy(path, output, overwrite: false);
                }
            return new(name, dryRun ? "planned" : "exported", target, names, []);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return new(name, "invalid", destination, [], [exception.Message]); }
    }
}
