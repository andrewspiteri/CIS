namespace Cis.Abstractions;

/// <summary>Optional local advisory context over the existing graph/context-pack implementation.</summary>
public interface ICisTaskContextPreparer
{
    CisTaskContextDelivery Prepare(CisTaskContextRequest request);
}

public sealed record CisTaskContextRequest(string RepositoryPath, string ChangeId, string TaskId,
    string CanonicalTaskPath, string? TargetRepositoryPath = null, int MaxCharacters = 16_000,
    int MaxUtf8Bytes = 64_000)
{
    public IReadOnlyList<string> ContextRoots { get; init; } = [];
}

/// <summary>Derived evidence only: this does not replace or amend canonical task instructions or execution authority.</summary>
public sealed record CisTaskContextDelivery(int SchemaVersion, string Status, string Freshness, string? SnapshotId,
    string Content, string? DetailPath, int MaxCharacters, int MaxUtf8Bytes, int DeliveredCharacters,
    int DeliveredUtf8Bytes, int EstimatedTokens, int OmittedItems, IReadOnlyList<string> DiagnosticCodes);
