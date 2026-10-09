using Example.Core;
using Example.TestSupport;

namespace Example.UnitTests;

public sealed class BatchTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TestModeCapturesCorrelatedSignalsWithoutChangingReplayBehavior()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var store = new MemoryStore();
        var service = new ReadingBatchService(store, telemetry, signals);
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 0.12345678m);
        Assert.Equal(1, await service.ApplyAsync([reading, reading], "run-1", CancellationToken.None));
        Assert.Equal(0, await service.ApplyAsync([reading], "run-2", CancellationToken.None));
        Assert.Equal(1, signals.Accepted);
        Assert.Contains("run-1", signals.TraceRuns);
        Assert.Contains(signals.Logs, log => log.Contains("run-2", StringComparison.Ordinal));
        Assert.Equal(reading, Assert.Single(await store.ReadAllAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task CancellationPreservesPartialMetricsAndTraceForDiagnosis()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        using var cancellation = new CancellationTokenSource();
        var service = new ReadingBatchService(new MemoryStore(), telemetry, signals);
        IEnumerable<Reading> Interrupted()
        {
            yield return new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1);
            cancellation.Cancel();
            yield return new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 2);
        }
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplyAsync(Interrupted(), "cancelled-run", cancellation.Token));
        Assert.Equal(1, signals.Accepted);
        Assert.Contains("cancelled-run", signals.TraceRuns);
        using var journal = new StreamReader(new FileStream(signals.JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var partial = await journal.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("collector-open", partial, StringComparison.Ordinal);
        Assert.Contains("accepted", partial, StringComparison.Ordinal);
        Assert.Contains("cancelled-run", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("collector-closed", partial, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParallelCollectorsAreIsolatedAndUnavailableCollectionIsExplicit()
    {
        using var first = new TestSignals(output);
        using var second = new TestSignals(output);
        using var unavailable = new TestSignals(output, enabled: false);
        using var firstTelemetry = new ProcessingTelemetry(first.Name);
        using var secondTelemetry = new ProcessingTelemetry(second.Name);
        using var unavailableTelemetry = new ProcessingTelemetry(unavailable.Name);
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Apply(ProcessingTelemetry telemetry, TestSignals signals, string run)
        {
            await start.Task;
            await new ReadingBatchService(new MemoryStore(), telemetry, signals).ApplyAsync([reading], run, CancellationToken.None);
        }
        var firstRun = Apply(firstTelemetry, first, "first-run");
        var secondRun = Apply(secondTelemetry, second, "second-run");
        var unavailableRun = Apply(unavailableTelemetry, unavailable, "unavailable-run");
        start.SetResult();
        await Task.WhenAll(firstRun, secondRun, unavailableRun);
        Assert.Equal(["first-run"], first.TraceRuns);
        Assert.Equal(["second-run"], second.TraceRuns);
        Assert.False(unavailable.CollectorsEnabled);
        Assert.Empty(unavailable.TraceRuns);
        Assert.Equal(0, unavailable.Accepted);
        Assert.Equal(1, first.Accepted);
        Assert.Equal(1, second.Accepted);
    }

    private sealed class MemoryStore : IReadingStore
    {
        private readonly Dictionary<Guid, Reading> _readings = [];
        public Task<bool> AppendAsync(Reading reading, CancellationToken cancellationToken)
            => Task.FromResult(_readings.TryAdd(reading.Id, reading));
        public Task<IReadOnlyList<Reading>> ReadAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Reading>>(_readings.Values.ToArray());
    }
}
