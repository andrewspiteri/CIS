using System.Diagnostics;
using System.Text.Json;

namespace Cis.Abstractions;

/// <summary>Opt-in local phase timings on stderr; never includes repository content.</summary>
public sealed class CisPerformanceTrace : IDisposable
{
    private static readonly CisPerformanceTrace Disabled = new();
    private readonly string? _operation;
    private readonly long _started;
    private long _previous;
    private CisPerformanceTrace() { }
    private CisPerformanceTrace(string operation)
    { _operation = operation; _started = _previous = Stopwatch.GetTimestamp(); }

    public static CisPerformanceTrace Start(string operation)
        => Environment.GetEnvironmentVariable("CIS_PERF_TRACE") == "1" ? new(operation) : Disabled;

    public void Mark(string phase)
    {
        if (_operation is null) return;
        var now = Stopwatch.GetTimestamp();
        Write(_operation + "." + phase, Stopwatch.GetElapsedTime(_previous, now).TotalMilliseconds);
        _previous = now;
    }

    public void Dispose()
    {
        if (_operation is not null) Write(_operation, Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    }

    private static void Write(string operation, double milliseconds)
        => Console.Error.WriteLine("cis.perf " + JsonSerializer.Serialize(new { operation, milliseconds = Math.Round(milliseconds, 3) }));
}
