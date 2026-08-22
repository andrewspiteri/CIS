namespace Cis.Abstractions;

public interface ICisGraphSnapshotReader
{
    CisGraphSnapshotReadResult Read(string repositoryPath);
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
    IReadOnlyList<string> Errors);
