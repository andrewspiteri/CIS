using Example.Core;
using Example.TestSupport;

namespace Example.UnitTests;

public sealed class BatchConcurrencyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ConcurrentCallsOnOneServiceKeepCountsAndTraceContextsSeparate()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var first = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1m);
        var second = first with { Id = Guid.NewGuid() };
        var firstReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingReadingStore((reading, _) => reading.Id == first.Id ? firstReply.Task : secondReply.Task);
        var service = new ReadingBatchService(store, telemetry, signals);

        var firstRun = service.ApplyAsync([first], "concurrent-first", TestContext.Current.CancellationToken);
        var secondRun = service.ApplyAsync([second], "concurrent-second", TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(2, store.Calls.Count);
            Assert.False(firstRun.IsCompleted);
            Assert.False(secondRun.IsCompleted);
            secondReply.SetResult(false);
            Assert.Equal(0, await secondRun.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            firstReply.SetResult(true);
            Assert.Equal(1, await firstRun.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            firstReply.TrySetResult(true);
            secondReply.TrySetResult(false);
            await Task.WhenAll(firstRun, secondRun).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, signals.Accepted);
        Assert.Equal(["concurrent-second", "concurrent-first"], signals.TraceRuns);
        Assert.Contains(signals.Logs, log => log == "Run concurrent-first accepted 1 readings");
        Assert.Contains(signals.Logs, log => log == "Run concurrent-second accepted 0 readings");
    }

    [Fact]
    public async Task CancellationReachesAnAwaitedStoreWithoutCountingAnUnfinishedWrite()
    {
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingReadingStore((_, token) => pending.Task.WaitAsync(token));
        var service = new ReadingBatchService(store, telemetry, signals);
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1m);

        var operation = service.ApplyAsync([reading, reading], "during-append", cancellation.Token);
        try
        {
            Assert.Equal(cancellation.Token, Assert.Single(store.Calls).Token);
            Assert.False(operation.IsCompleted);
            await cancellation.CancelAsync();
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
        }
        finally
        {
            pending.TrySetResult(false);
        }

        Assert.Single(store.Calls);
        Assert.Equal(0, signals.Accepted);
        Assert.Empty(signals.Logs);
        Assert.Equal(["during-append"], signals.TraceRuns);
    }
}
