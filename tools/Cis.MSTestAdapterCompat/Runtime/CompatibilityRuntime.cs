using System.Runtime.CompilerServices;

namespace Cis.MSTestAdapterCompat.Runtime;

/// <summary>Opt-in support for the exact adapter binary accepted by the companion tool.</summary>
public static class CompatibilityRuntime
{
    private static readonly ConditionalWeakTable<object, CleanupState> CleanupStates = new();

    public static void ThrowIfCancellationRequested(CancellationTokenSource owned, CancellationToken? original)
    {
        ArgumentNullException.ThrowIfNull(owned);
        original.GetValueOrDefault().ThrowIfCancellationRequested();
        if (owned.IsCancellationRequested)
        {
            throw new OwnedQueueCancellationException(owned.Token);
        }
    }

    public static bool IsOwnedCancellationRequested(CancellationTokenSource? owned) =>
        owned?.IsCancellationRequested == true;

    public static Task ObserveCancellationAsync(Task operation, CancellationTokenSource? owned) =>
        ObserveCancellationAsync(operation, owned, original: null);

    public static async Task ObserveCancellationAsync(Task operation, CancellationTokenSource? owned, CancellationToken? original)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            exception is OwnedQueueCancellationException && operation.IsCanceled &&
            owned?.IsCancellationRequested == true && exception.CancellationToken == owned.Token)
        {
            // Only the adapter's own canceled token permits the existing cleanup path to continue.
            original.GetValueOrDefault().ThrowIfCancellationRequested();
        }
        // Even preexisting external cancellation must join all dispatched work before propagating.
        original.GetValueOrDefault().ThrowIfCancellationRequested();
    }

    public static CancellationTokenRegistration? RegisterCleanupCancellation(
        CancellationTokenSource cleanup, CancellationToken? original)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        var external = original.GetValueOrDefault();
        return external.CanBeCanceled
            ? external.Register(static state => ((CancellationTokenSource)state!).Cancel(), cleanup)
            : null;
    }

    public static bool CleanupCompleted(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!CleanupStates.TryGetValue(owner, out var state))
        {
            return false;
        }
        lock (state)
        {
            return state.Operation?.IsCompleted == true;
        }
    }

    public static Task<T> CleanupOnceAsync<T>(object owner, bool initialized, Func<Task<T>> original)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(original);
        if (!initialized)
        {
            return Task.FromResult(default(T)!);
        }

        var state = CleanupStates.GetValue(owner, static _ => new CleanupState());
        TaskCompletionSource<T> completion;
        lock (state)
        {
            if (state.Operation is Task<T> existing)
            {
                return AwaitWithoutRepublishingAsync(existing);
            }
            if (state.Operation is not null)
            {
                throw new InvalidOperationException("An assembly cleanup owner cannot change result type.");
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Operation = completion.Task;
        }
        _ = CompleteCleanupAsync(completion, original);
        return completion.Task;
    }

    public static Task<TResult> CleanupOnceAsync<TContext, TResult>(
        object owner, bool initialized, TContext context, Func<TContext, Task<TResult>> original)
    {
        ArgumentNullException.ThrowIfNull(original);
        return CleanupOnceAsync(owner, initialized, () => original(context));
    }

    private static async Task<T> AwaitWithoutRepublishingAsync<T>(Task<T> operation)
    {
        // The first caller owns the normalized result, output and attachments. All callers
        // still join the original task; infrastructure faults/cancellation are never hidden.
        await operation.ConfigureAwait(false);
        return default!;
    }

    private static async Task CompleteCleanupAsync<T>(TaskCompletionSource<T> completion, Func<Task<T>> original)
    {
        try
        {
            completion.TrySetResult(await original().ConfigureAwait(false));
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private sealed class CleanupState
    {
        public Task? Operation { get; set; }
    }
}

internal sealed class OwnedQueueCancellationException : OperationCanceledException
{
    internal OwnedQueueCancellationException(CancellationToken token)
        : base("The adapter's owned queue was canceled.", token)
    {
    }
}
