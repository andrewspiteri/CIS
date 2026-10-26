using System.Runtime.CompilerServices;
using Cis.MSTestAdapterCompat.Runtime;
using Xunit;

namespace Cis.MSTestAdapterCompat.Tests;

public sealed class CompatibilityRuntimeTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void NeitherCancellationSourceMasksTheOther(int originalKind, bool cancelOwned)
    {
        using var owned = new CancellationTokenSource();
        using var original = new CancellationTokenSource();
        if (originalKind == 3) original.Cancel();
        if (cancelOwned) owned.Cancel();
        CancellationToken? token = originalKind switch
        {
            0 => null,
            1 => CancellationToken.None,
            _ => original.Token,
        };
        var error = Record.Exception(() => CompatibilityRuntime.ThrowIfCancellationRequested(owned, token));
        if (cancelOwned || originalKind == 3)
        {
            var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Equal(originalKind == 3 ? original.Token : owned.Token, cancellation.CancellationToken);
        }
        else Assert.Null(error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExternalCancellationIsPreservedAfterJoiningActiveWork(bool cancelBefore, bool throwOwned)
    {
        using var owned = new CancellationTokenSource();
        using var original = new CancellationTokenSource();
        owned.Cancel();
        if (cancelBefore) original.Cancel();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ActiveWork()
        {
            await release.Task;
            if (throwOwned) CompatibilityRuntime.ThrowIfCancellationRequested(owned, null);
        }
        var observed = CompatibilityRuntime.ObserveCancellationAsync(ActiveWork(), owned, original.Token);
        if (!cancelBefore) original.Cancel();
        Assert.False(observed.IsCompleted);
        release.SetResult();
        var error = await Record.ExceptionAsync(() => observed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(original.Token, cancellation.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupContextLinksExternalCancellationBeforeAndDuringCleanup(bool cancelBefore)
    {
        using var context = new CancellationTokenSource();
        using var original = new CancellationTokenSource();
        if (cancelBefore) original.Cancel();
        using var registration = CompatibilityRuntime.RegisterCleanupCancellation(context, original.Token);
        Assert.Equal(cancelBefore, context.IsCancellationRequested);
        original.Cancel();
        Assert.True(context.IsCancellationRequested);
    }

    [Fact]
    public void CleanupCancellationRegistrationReleasesItsContext()
    {
        using var context = new CancellationTokenSource();
        using var original = new CancellationTokenSource();
        CompatibilityRuntime.RegisterCleanupCancellation(context, original.Token)?.Dispose();
        original.Cancel();
        Assert.False(context.IsCancellationRequested);
        Assert.Null(CompatibilityRuntime.RegisterCleanupCancellation(context, null));
        Assert.Null(CompatibilityRuntime.RegisterCleanupCancellation(context, CancellationToken.None));
    }

    [Fact]
    public async Task ObserverJoinsActiveWorkBeforeAcceptingOwnedCancellation()
    {
        using var owned = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task QueueOperation()
        {
            await release.Task;
            CompatibilityRuntime.ThrowIfCancellationRequested(owned, null);
        }
        owned.Cancel();
        var observed = CompatibilityRuntime.ObserveCancellationAsync(QueueOperation(), owned);
        Assert.False(observed.IsCompleted);
        release.SetResult();
        await observed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("untagged")]
    [InlineData("unrelated-token")]
    [InlineData("unsignaled-owner")]
    [InlineData("ordinary-error")]
    [InlineData("aggregate")]
    [InlineData("missing-owner")]
    public async Task ObserverPreservesFailuresOutsideExactOwnedCancellation(string kind)
    {
        using var owned = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        if (kind != "unsignaled-owner") owned.Cancel();
        unrelated.Cancel();
        Exception expected = kind switch
        {
            "unrelated-token" => new OperationCanceledException(unrelated.Token),
            "unsignaled-owner" => new OperationCanceledException(owned.Token),
            "ordinary-error" => new InvalidOperationException("product failure"),
            "aggregate" => new AggregateException(new OperationCanceledException(owned.Token)),
            "missing-owner" => new OperationCanceledException(owned.Token),
            _ => new OperationCanceledException(),
        };
        var actual = await Record.ExceptionAsync(() => CompatibilityRuntime.ObserveCancellationAsync(
            Task.FromException(expected), kind == "missing-owner" ? null : owned));
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SuccessfulDispatchIsPreservedEvenAfterCancellationWasRequested()
    {
        using var owned = new CancellationTokenSource();
        owned.Cancel();
        await CompatibilityRuntime.ObserveCancellationAsync(Task.CompletedTask, owned);
        Assert.True(CompatibilityRuntime.IsOwnedCancellationRequested(owned));
        Assert.False(CompatibilityRuntime.IsOwnedCancellationRequested(null));
    }

    [Fact]
    public async Task RawSameTokenCancellationCannotStandInForAQueueCancellation()
    {
        using var owned = new CancellationTokenSource();
        owned.Cancel();
        var failure = new OperationCanceledException("framework cancellation", owned.Token);
        async Task FrameworkOperation() { await Task.Yield(); throw failure; }
        Assert.Same(failure, await Record.ExceptionAsync(() => CompatibilityRuntime.ObserveCancellationAsync(FrameworkOperation(), owned)));
    }

    [Fact]
    public async Task CleanupHasOneOutcomeOwnerAndAllConcurrentCallersJoin()
    {
        var owner = new object();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("normalized cleanup failure");
        int invocations = 0;
        async Task<Exception?> Cleanup()
        {
            Interlocked.Increment(ref invocations);
            await release.Task;
            return failure;
        }
        var first = CompatibilityRuntime.CleanupOnceAsync(owner, true, Cleanup);
        var entered = Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var waiters = entered.Select(signal => Task.Run(async () =>
        {
            signal.SetResult();
            return await CompatibilityRuntime.CleanupOnceAsync(owner, true, Cleanup);
        })).ToArray();
        await Task.WhenAll(entered.Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref invocations));
        Assert.False(first.IsCompleted);
        Assert.All(waiters, waiter => Assert.False(waiter.IsCompleted));
        Assert.False(CompatibilityRuntime.CleanupCompleted(owner));
        release.SetResult();
        Assert.Same(failure, await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.All(await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), Assert.Null);
        Assert.Null(await CompatibilityRuntime.CleanupOnceAsync(owner, true, Cleanup));
        Assert.Equal(1, invocations);
        Assert.True(CompatibilityRuntime.CleanupCompleted(owner));
    }

    [Fact]
    public async Task UninitializedScopeDoesNotConsumeLaterEligibleCleanup()
    {
        var owner = new object();
        int invocations = 0;
        Task<Exception?> Cleanup() { invocations++; return Task.FromResult<Exception?>(null); }
        Assert.Null(await CompatibilityRuntime.CleanupOnceAsync(owner, false, Cleanup));
        Assert.Equal(0, invocations);
        Assert.False(CompatibilityRuntime.CleanupCompleted(owner));
        await CompatibilityRuntime.CleanupOnceAsync(owner, true, Cleanup);
        Assert.Equal(1, invocations);
        Assert.True(CompatibilityRuntime.CleanupCompleted(owner));
    }

    [Fact]
    public async Task SeparateRequestOwnersEachReceiveTheirOwnCleanup()
    {
        int invocations = 0;
        Task<int> Cleanup() => Task.FromResult(++invocations);
        Assert.Equal(1, await CompatibilityRuntime.CleanupOnceAsync(new object(), true, Cleanup));
        Assert.Equal(2, await CompatibilityRuntime.CleanupOnceAsync(new object(), true, Cleanup));
    }

    [Fact]
    public async Task CleanupInfrastructureFaultIsNotSwallowedForAnyCaller()
    {
        var owner = new object();
        var failure = new IOException("cleanup infrastructure failure");
        int invocations = 0;
        Task<object?> Cleanup() { invocations++; return Task.FromException<object?>(failure); }
        Assert.Same(failure, await Record.ExceptionAsync(() => CompatibilityRuntime.CleanupOnceAsync(owner, true, Cleanup)));
        Assert.Same(failure, await Record.ExceptionAsync(() => CompatibilityRuntime.CleanupOnceAsync(owner, true, Cleanup)));
        Assert.Equal(1, invocations);
        Assert.True(CompatibilityRuntime.CleanupCompleted(owner));
    }

    [Fact]
    public void CompletedScopeDoesNotStayRootedByTheRuntime()
    {
        var owner = CreateCompletedOwner();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(owner.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateCompletedOwner()
    {
        var owner = new object();
        CompatibilityRuntime.CleanupOnceAsync(owner, true, () => Task.FromResult<object?>(null)).GetAwaiter().GetResult();
        return new WeakReference(owner);
    }
}
