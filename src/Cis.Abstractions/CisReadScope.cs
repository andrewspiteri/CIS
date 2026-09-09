using System.Collections.Concurrent;

namespace Cis.Abstractions;

/// <summary>
/// Shares reads within one synchronous, read-only projection. Nested projections
/// reuse the same read results; disposing the outer scope releases it. Never span writes
/// or separate commands with this scope: their freshness must be evaluated again.
/// </summary>
public sealed class CisReadScope : IDisposable
{
    private static readonly AsyncLocal<ConcurrentDictionary<ReadKey, Lazy<object>>?> Current = new();
    private readonly ConcurrentDictionary<ReadKey, Lazy<object>>? _previous;
    private bool _disposed;

    private CisReadScope()
    {
        _previous = Current.Value;
        Current.Value ??= new();
    }

    public static CisReadScope Enter() => new();

    public static T Read<T>(object reader, string operation, string repositoryPath, Func<T> read) where T : notnull
    {
        T ReadMeasured()
        {
            using var timing = CisPerformanceTrace.Start((reader is Type type ? type.Name : reader.GetType().Name) + "." + operation);
            return read();
        }
        var snapshot = Current.Value;
        if (snapshot is null) return ReadMeasured();
        string path;
        try { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Preserve the reader's structured invalid-path result.
            return ReadMeasured();
        }
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return (T)snapshot.GetOrAdd(new ReadKey(reader, operation, path),
            _ => new Lazy<object>(() => ReadMeasured())).Value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Current.Value = _previous;
    }

    private sealed record ReadKey(object Reader, string Operation, string RepositoryPath);
}
