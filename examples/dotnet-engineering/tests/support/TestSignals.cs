using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Example.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Example.TestSupport;

/// <summary>Test composition enables native listeners and Debug logging; application behavior stays unchanged.</summary>
internal sealed partial class TestSignals : ILogger<ReadingBatchService>, IDisposable
{
    private readonly MeterListener _metrics = new();
    private readonly ActivityListener _traces;
    private long _accepted;
    private int _logCount;
    private int _traceCount;
    public string Name { get; } = "Example.Tests." + Guid.NewGuid().ToString("N");
    public ConcurrentQueue<string> Logs { get; } = new();
    public ConcurrentQueue<string> TraceRuns { get; } = new();
    public long Accepted => Interlocked.Read(ref _accepted);

    private readonly ITestOutputHelper _output;
    private readonly TestSignalJournal _journal;
    public string JournalPath => _journal.Path;

    public TestSignals(ITestOutputHelper output, bool enabled = true)
    {
        _output = output;
        _journal = new TestSignalJournal(Name, enabled);
        _output.WriteLine("Partial diagnostic journal: " + JournalPath);
        // A missing collector stays observable as missing; it never produces a zero-valued successful capture.
        CollectorsEnabled = enabled;
        _metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (enabled && instrument.Meter.Name == Name) listener.EnableMeasurementEvents(instrument);
        };
        _metrics.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            Interlocked.Add(ref _accepted, value);
            _journal.Append("accepted", value);
        });
        _metrics.Start();
        _traces = new ActivityListener
        {
            ShouldListenTo = source => enabled && source.Name == Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Interlocked.Increment(ref _traceCount) <= 256)
                {
                    var run = Sanitize(activity.GetTagItem("run.id")?.ToString() ?? "");
                    TraceRuns.Enqueue(run);
                    _journal.Append("trace-run", run);
                }
            },
        };
        ActivitySource.AddActivityListener(_traces);
    }
    public bool CollectorsEnabled { get; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and < LogLevel.None;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (Interlocked.Increment(ref _logCount) <= 256)
        {
            var message = Sanitize(formatter(state, exception));
            Logs.Enqueue(message);
            _journal.Append("log", message);
        }
    }
    public void Dispose()
    {
        _traces.Dispose();
        _metrics.Dispose();
        _journal.Dispose();
        // Native xUnit output is retained by TRX, including when an assertion fails or cancellation is observed.
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            source = Name,
            collectorsEnabled = CollectorsEnabled,
            accepted = Accepted,
            logs = Logs.ToArray(),
            traceRuns = TraceRuns.ToArray(),
            truncated = _logCount > 256 || _traceCount > 256
        }));
    }

    private static string Sanitize(string value)
    {
        var safe = SecretPattern().Replace(value, "$1=[REDACTED]");
        return safe.Length <= 1024 ? safe : safe[..1024];
    }
    [GeneratedRegex(@"(?i)(password|token|secret|authorization|api[-_]?key)\s*[:=]\s*[^;\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
}
