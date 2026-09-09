namespace Cis.Abstractions;

public interface ICisGraphSnapshotReader
{
    CisGraphSnapshotReadResult Read(string repositoryPath);

    // Implementations can avoid materializing nodes and edges for baseline consumers.
    CisGraphMetadataReadResult ReadMetadata(string repositoryPath)
    {
        var snapshot = Read(repositoryPath);
        return new(snapshot.Status, snapshot.ExitCode, snapshot.RepositoryPath, snapshot.Freshness,
            snapshot.Graph?.Build, snapshot.Manifest, snapshot.Warnings, snapshot.Errors);
    }
}

public sealed record CisGraphMetadataReadResult(
    string Status, int ExitCode, string? RepositoryPath, string Freshness,
    CisGraphBuildMetadata? Build, CisGraphManifest? Manifest,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);

public sealed record CisGraphSnapshotReadResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    string Freshness,
    CisGraphDocument? Graph,
    CisGraphManifest? Manifest,
    IReadOnlyList<string> GraphCapabilities,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
