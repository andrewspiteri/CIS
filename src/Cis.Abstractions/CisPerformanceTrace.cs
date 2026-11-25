using System.Diagnostics;
using System.Text.Json;

namespace Cis.Abstractions;

/// <summary>Opt-in local phase timings on stderr; never includes repository content.</summary>
public sealed class CisPerformanceTrace : IDisposable
{
    private static readonly CisPerformanceTrace Disabled = new();
    private readonly string? _operation;
    private readonly bool _writeTrace;
    private readonly long _started;
    private long _previous;
    private CisPerformanceTrace() { }
    private CisPerformanceTrace(string operation)
    { _operation = operation; _writeTrace = Environment.GetEnvironmentVariable("CIS_PERF_TRACE") == "1"; _started = _previous = Stopwatch.GetTimestamp(); }

    public static CisPerformanceTrace Start(string operation)
        => CisInvocationTelemetry.IsActive || Environment.GetEnvironmentVariable("CIS_PERF_TRACE") == "1" ? new(operation) : Disabled;

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

    private void Write(string operation, double milliseconds)
    {
        // Preserve detailed opt-in stderr traces while aggregating ledger attribution.
        // Reader/operation names may contain paths or many distinct low-level labels.
        CisInvocationTelemetry.RecordPhase(LedgerPhase(operation), milliseconds);
        if (_writeTrace)
            Console.Error.WriteLine("cis.perf " + JsonSerializer.Serialize(new { operation, milliseconds = Math.Round(milliseconds, 3) }));
    }

    private static string LedgerPhase(string operation)
    {
        if (operation.Contains("graph", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("sqlite", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("compiler", StringComparison.OrdinalIgnoreCase)) return "graph";
        if (operation.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("hash", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("digest", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("dossier", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("adoption", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("final-fence", StringComparison.OrdinalIgnoreCase)) return "identity";
        if (operation.Contains("scan", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("inventory", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("inputs", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("classif", StringComparison.OrdinalIgnoreCase)) return "scan";
        if (operation.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("receipt", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("verification", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("assessment", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("alignment", StringComparison.OrdinalIgnoreCase)) return "evidence";
        if (operation.Contains("native", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("process", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("execute", StringComparison.OrdinalIgnoreCase)) return "native";
        if (operation.Contains("render", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("serialize", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("output", StringComparison.OrdinalIgnoreCase)) return "render";
        return "other";
    }
}
