using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

internal sealed class RepositoryStatusCache
{
    private const int SchemaVersion = 1;
    private const string RelativePath = ".cis/local/status/repository-initialization.json";
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis/local", ".artifacts", "artifacts", "bin", "obj", "node_modules", ".next",
        "dist", "coverage", "TestResults", ".gradle", ".godot", ".vs", ".idea",
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public bool TryRead(
        string repositoryPath,
        string documentationRoot,
        out IReadOnlyList<CisRepositoryDoctorFinding> findings)
    {
        findings = [];
        try
        {
            var path = AbsolutePath(repositoryPath);
            if (!File.Exists(path)) return false;
            var cached = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(path), JsonOptions);
            if (cached is null || cached.SchemaVersion != SchemaVersion
                || !string.Equals(cached.DocumentationRoot, documentationRoot, StringComparison.Ordinal)
                || !string.Equals(cached.Fingerprint, Fingerprint(repositoryPath), StringComparison.Ordinal))
            {
                return false;
            }

            findings = cached.Findings ?? [];
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or CryptographicException)
        {
            return false;
        }
    }

    public void Write(
        string repositoryPath,
        string documentationRoot,
        IReadOnlyList<CisRepositoryDoctorFinding> findings)
    {
        try
        {
            var path = AbsolutePath(repositoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var envelope = new CacheEnvelope(
                SchemaVersion,
                documentationRoot,
                Fingerprint(repositoryPath),
                DateTimeOffset.UtcNow,
                findings);
            var content = JsonSerializer.Serialize(envelope, JsonOptions) + Environment.NewLine;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or CryptographicException)
        {
            // Status caching is an optimization. Doctor remains authoritative without it.
        }
    }

    private static string Fingerprint(string repositoryPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "cis-repository-status-cache-v1");
        var assembly = typeof(RepositoryStatusCache).Assembly.Location;
        if (File.Exists(assembly)) AppendFileMetadata(hash, assembly, "<cis-repository-module>");

        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var relative = Normalize(Path.GetRelativePath(repositoryPath, child));
                if (!IsExcluded(relative)
                    && !new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var relative = Normalize(Path.GetRelativePath(repositoryPath, file));
                if (!IsExcluded(relative)) AppendFileMetadata(hash, file, relative);
            }
        }

        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendFileMetadata(IncrementalHash hash, string path, string identity)
    {
        var info = new FileInfo(path);
        Append(hash, identity);
        Append(hash, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static bool IsExcluded(string relativePath)
    {
        if (string.Equals(relativePath, RelativePath, StringComparison.OrdinalIgnoreCase)) return true;
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var prefix = string.Join('/', segments.Take(index + 1));
            if (ExcludedDirectories.Contains(segments[index]) || ExcludedDirectories.Contains(prefix)) return true;
        }

        return false;
    }

    private static string AbsolutePath(string repositoryPath) => Path.Combine(
        repositoryPath, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Normalize(string value) => value.Replace('\\', '/');

    private sealed record CacheEnvelope(
        int SchemaVersion,
        string DocumentationRoot,
        string Fingerprint,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<CisRepositoryDoctorFinding>? Findings);
}
