using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Graph;

/// <summary>Disposable structural validation, never a cache of input freshness or approval.</summary>
internal static class GraphValidationCache
{
    internal const string RelativePath = ".cis/local/status/graph-validation.json";
    private const int Version = 1;
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string? Key(CisRepositoryContext context, string buildId, IReadOnlyList<string> extractors)
    {
        try
        {
            var databasePath = Path.Combine(context.RepositoryPath, SqliteGraphStore.DatabaseRelativePath);
            // An external SQLite writer may use WAL even though CIS builds use DELETE.
            // A live journal is not represented by hashing the main database alone.
            if (File.Exists(databasePath + "-wal") || File.Exists(databasePath + "-journal")) return null;
            var graphHash = FileContentHashCache.Hash(databasePath);
            if (File.Exists(databasePath + "-wal") || File.Exists(databasePath + "-journal")) return null;
            var root = OperatingSystem.IsWindows() ? context.RepositoryPath.ToUpperInvariant() : context.RepositoryPath;
            var identity = string.Join('\n', Version, typeof(GraphValidator).Assembly.ManifestModule.ModuleVersionId,
                root, context.RepositoryId, context.DocumentationRoot, buildId,
                string.Join('\n', extractors), graphHash,
                File.ReadAllText(Path.Combine(context.RepositoryPath, ".cis/repository.yml")));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    internal static bool TryRead(string repositoryPath, string? key, Func<string, string?> locatorError,
        out IReadOnlyList<CisGraphDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (key is null) return false;
        try
        {
            var path = Path.Combine(repositoryPath, RelativePath);
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return false;
            var cached = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), JsonOptions);
            if (cached is null || cached.Version != Version || cached.Key != key
                || cached.Diagnostics is null || cached.Locators is null
                || cached.Diagnostics.Any(item => item is null || string.IsNullOrWhiteSpace(item.Code)
                    || item.Severity is not ("error" or "warning" or "info" or "information")
                    || item.Message is null || item.Evidence is null || item.Evidence.Any(path => path is null))
                || cached.Locators.Any(item => locatorError(item.Key) != item.Value)) return false;
            diagnostics = cached.Diagnostics;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    internal static void Write(string repositoryPath, string key, IReadOnlyList<CisGraphDiagnostic> diagnostics,
        IReadOnlyDictionary<string, string?> locators)
    {
        string? temporary = null;
        try
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new Envelope(Version, key, diagnostics, locators), JsonOptions);
            if (content.Length > MaximumBytes) return;
            var path = Path.Combine(repositoryPath, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A read-only or unavailable cache must not change validation results.
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private sealed record Envelope(int Version, string Key, IReadOnlyList<CisGraphDiagnostic> Diagnostics,
        IReadOnlyDictionary<string, string?> Locators);
}
