namespace Example.Core;

public interface IReadingStore
{
    Task<bool> AppendAsync(Reading reading, CancellationToken cancellationToken);
    Task<IReadOnlyList<Reading>> ReadAllAsync(CancellationToken cancellationToken);
}
