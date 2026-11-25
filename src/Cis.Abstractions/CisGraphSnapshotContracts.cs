namespace Cis.Abstractions;

public interface ICisGraphSnapshotReader
{
    CisGraphSnapshotReadResult Read(string repositoryPath);

    // Explicit current-consumer boundary. Historical navigation keeps using Read.
    CisGraphSnapshotReadResult ReadCurrent(string repositoryPath) => Read(repositoryPath);

    // Implementations can avoid materializing nodes and edges for baseline consumers.
    CisGraphMetadataReadResult ReadMetadata(string repositoryPath)
    {
        var snapshot = Read(repositoryPath);
        return new(snapshot.Status, snapshot.ExitCode, snapshot.RepositoryPath, snapshot.Freshness,
            snapshot.Graph?.Build, snapshot.Manifest, snapshot.Warnings, snapshot.Errors)
        { Diagnostics = snapshot.Diagnostics };
    }
}

public sealed record CisGraphMetadataReadResult(
    string Status, int ExitCode, string? RepositoryPath, string Freshness,
    CisGraphBuildMetadata? Build, CisGraphManifest? Manifest,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors)
{
    public IReadOnlyList<CisGraphDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record CisGraphSnapshotReadResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    string Freshness,
    CisGraphDocument? Graph,
    CisGraphManifest? Manifest,
    IReadOnlyList<string> GraphCapabilities,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public IReadOnlyList<CisGraphDiagnostic> Diagnostics { get; init; } = [];
}
