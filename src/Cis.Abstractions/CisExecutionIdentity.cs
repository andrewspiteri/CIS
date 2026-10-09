using System.Security.Cryptography;
using System.Text;

namespace Cis.Abstractions;

public sealed record CisRepositoryInput(string Path, string Digest);

/// <summary>Content identity for repository execution inputs, including uncommitted and newly added files.</summary>
public static class CisExecutionIdentity
{
    public static string? CaptureIfAdopted(CisRepositoryContext context)
        => CisEngineeringPolicy.Read(context.RepositoryPath)?.RequireTaskCompletion == true ? Capture(context) : null;

    public static string Capture(CisRepositoryContext context)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var input in CaptureInputs(context))
            digest.AppendData(Encoding.UTF8.GetBytes(input.Path + "\0" + input.Digest + "\n"));
        return "sha256:" + Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    /// <summary>Retains bounded input hashes using the same exclusions as execution identity.</summary>
    public static IReadOnlyList<CisRepositoryInput> CaptureInputs(CisRepositoryContext context, bool includeDossiers = false,
        IReadOnlyList<string>? excludedRepositoryRoots = null)
    {
        var root = context.RepositoryPath;
        var excluded = (excludedRepositoryRoots ?? []).Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (excluded.Any(path => !CisPathSafety.IsUnderRoot(root, path)))
            throw new InvalidDataException("An excluded nested repository must remain below the inventory root.");
        var dossierRoot = context.DocumentationRoot.TrimEnd('/', '\\').Replace('\\', '/') + "/changes/";
        if (CisPathSafety.IsReparsePoint(root)) throw new InvalidDataException("Execution repository root must not be a linked path.");
        var files = new List<string>();
        var outputs = new CisExecutionOutputs(root);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (excluded.Contains(entry)) continue;
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                var segments = relative.Split('/');
                var derivedCisState = segments.Length >= 2 && segments[^2] == ".cis" && segments[^1] is "local" or "cache" or "runs";
                if (derivedCisState || !includeDossiers && relative == dossierRoot.TrimEnd('/')) continue;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory) && outputs.IsOutput(entry, relative)) continue;
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException($"Execution input traverses a linked path: {relative}");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
                else if (Path.GetFileName(entry) != ".git") files.Add(relative);
                if (files.Count > 100_000) throw new InvalidDataException("Execution identity exceeds 100,000 inputs; define a bounded repository before running checks.");
            }
        }
        var inputs = new List<CisRepositoryInput>();
        foreach (var relative in files.Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(root, relative);
            if (new FileInfo(path).Length > 128L * 1024 * 1024) throw new InvalidDataException($"Execution input exceeds 128 MiB: {relative}");
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            inputs.Add(new(relative, hash));
        }
        return inputs;
    }
}
