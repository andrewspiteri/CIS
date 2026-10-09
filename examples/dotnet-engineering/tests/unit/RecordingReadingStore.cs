using System.Collections.Concurrent;
using Example.Core;

namespace Example.UnitTests;

// A controllable port for application tests. Native xUnit owns assertions and reporting.
internal sealed class RecordingReadingStore(Func<Reading, CancellationToken, Task<bool>> append) : IReadingStore
{
    internal ConcurrentQueue<(Reading Reading, CancellationToken Token)> Calls { get; } = new();

    public Task<bool> AppendAsync(Reading reading, CancellationToken cancellationToken)
    {
        Calls.Enqueue((reading, cancellationToken));
        return append(reading, cancellationToken);
    }

    public Task<IReadOnlyList<Reading>> ReadAllAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException("Applying a batch must not query all stored readings.");
}
