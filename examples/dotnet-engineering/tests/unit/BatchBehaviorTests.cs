using Example.Core;
using Example.TestSupport;

namespace Example.UnitTests;

public sealed class BatchBehaviorTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EmptyBatchCompletesWithoutStorageOrAcceptedMetrics()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var store = new RecordingReadingStore((_, _) => Task.FromResult(true));
        var service = new ReadingBatchService(store, telemetry, signals);

        Assert.Equal(0, await service.ApplyAsync([], "empty", TestContext.Current.CancellationToken));

        Assert.Empty(store.Calls);
        Assert.Equal(0, signals.Accepted);
        Assert.Equal(["empty"], signals.TraceRuns);
        Assert.Contains(signals.Logs, log => log == "Run empty accepted 0 readings");
    }

    [Fact]
    public async Task ApplicationPreservesOrderValuesAndTokenAndCountsOnlyAcceptedWrites()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        using var cancellation = new CancellationTokenSource();
        var first = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMicroseconds(1), 0.12345678m);
        var duplicate = first with { };
        var last = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 2m);
        var store = new RecordingReadingStore((reading, _) => Task.FromResult(!ReferenceEquals(reading, duplicate)));
        var service = new ReadingBatchService(store, telemetry, signals);

        Assert.Equal(2, await service.ApplyAsync([first, duplicate, last], "ordered", cancellation.Token));

        var calls = store.Calls.ToArray();
        Assert.Equal([first, duplicate, last], calls.Select(call => call.Reading));
        Assert.Same(first, calls[0].Reading);
        Assert.Same(last, calls[2].Reading);
        Assert.All(calls, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.Equal(2, signals.Accepted);
        Assert.Contains(signals.Logs, log => log == "Run ordered accepted 2 readings");
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("time")]
    [InlineData("quantity")]
    public async Task InvalidReadingNeverReachesTheStoragePort(string invalidField)
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var valid = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1m);
        var invalid = invalidField switch
        {
            "identity" => valid with { Id = Guid.Empty },
            "time" => valid with { ObservedAt = valid.ObservedAt.ToOffset(TimeSpan.FromHours(1)) },
            _ => valid with { Quantity = -1m },
        };
        var store = new RecordingReadingStore((_, _) => Task.FromResult(true));
        var service = new ReadingBatchService(store, telemetry, signals);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ApplyAsync([invalid], "invalid", TestContext.Current.CancellationToken));

        Assert.Empty(store.Calls);
        Assert.Equal(0, signals.Accepted);
        Assert.Empty(signals.Logs);
        Assert.Equal(["invalid"], signals.TraceRuns);
    }

    [Fact]
    public async Task InvalidLaterReadingStopsTheStreamWithoutUndoingEarlierAcceptedWork()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var first = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1m);
        var invalid = first with { Id = Guid.Empty };
        var store = new RecordingReadingStore((_, _) => Task.FromResult(true));
        var service = new ReadingBatchService(store, telemetry, signals);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync([first, invalid, first], "partial-invalid", TestContext.Current.CancellationToken));

        Assert.Same(first, Assert.Single(store.Calls).Reading);
        Assert.Equal(1, signals.Accepted);
        Assert.Empty(signals.Logs);
        Assert.Equal(["partial-invalid"], signals.TraceRuns);
    }

    [Fact]
    public async Task AlreadyCancelledBatchDoesNotCallStorage()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new RecordingReadingStore((_, _) => Task.FromResult(true));
        var service = new ReadingBatchService(store, telemetry, signals);
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1m);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplyAsync([reading], "pre-cancelled", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(store.Calls);
        Assert.Equal(0, signals.Accepted);
        Assert.Empty(signals.Logs);
        Assert.Equal(["pre-cancelled"], signals.TraceRuns);
    }

    [Fact]
    public async Task StorageFailurePropagatesAndStopsLaterWritesWithoutASuccessLog()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var first = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1m);
        var failed = first with { Id = Guid.NewGuid() };
        var failure = new InvalidOperationException("Synthetic storage refusal.");
        var store = new RecordingReadingStore((reading, _) => reading == failed ? Task.FromException<bool>(failure) : Task.FromResult(true));
        var service = new ReadingBatchService(store, telemetry, signals);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync([first, failed, first], "storage-failure", TestContext.Current.CancellationToken));

        Assert.Same(failure, actual);
        Assert.Equal([first, failed], store.Calls.Select(call => call.Reading));
        Assert.Equal(1, signals.Accepted);
        Assert.Empty(signals.Logs);
        Assert.Equal(["storage-failure"], signals.TraceRuns);
    }
}
