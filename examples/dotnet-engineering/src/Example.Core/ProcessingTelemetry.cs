using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Example.Core;

public sealed class ProcessingTelemetry : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _accepted;
    public ActivitySource Activities { get; }

    public ProcessingTelemetry(string sourceName)
    {
        _meter = new Meter(sourceName, "1.0");
        Activities = new ActivitySource(sourceName, "1.0");
        _accepted = _meter.CreateCounter<long>("readings.accepted");
    }

    public void Accepted(string runId) => _accepted.Add(1, new KeyValuePair<string, object?>("run.id", runId));
    public void Dispose()
    {
        Activities.Dispose();
        _meter.Dispose();
    }
}
