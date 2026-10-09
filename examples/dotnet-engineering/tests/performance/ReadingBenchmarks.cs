using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Example.Core;

namespace Example.Performance;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[ShortRunJob]
public class ReadingBenchmarks : IDisposable
{
    private Reading[] _readings = [];
    private ActivitySource _activities = null!;
    private ActivityListener? _listener;

    [Params(500)]
    public int Records { get; set; }

    [Params(false, true)]
    public bool Instrumented { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _readings = Enumerable.Range(0, Records).Select(index =>
            new Reading(new Guid(index + 1, 0, 0, new byte[8]), DateTimeOffset.UnixEpoch.AddSeconds(index), 0.00000001m)).ToArray();
        _activities = new ActivitySource("Example.Performance");
        if (!Instrumented) return;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Example.Performance",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [Benchmark]
    public decimal ValidateReadings()
    {
        using var activity = _activities.StartActivity("validate");
        var total = 0m;
        foreach (var reading in _readings)
        {
            reading.Validate();
            total += reading.Quantity;
        }
        return total;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        _listener?.Dispose();
        _activities.Dispose();
    }
}
