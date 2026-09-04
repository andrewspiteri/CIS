namespace Cis.Modules.Artifacts;

public sealed record ArtifactRetentionRule(
    string Family,
    string Path,
    int RetainDays,
    int KeepLatest,
    string Action);

public sealed record LocalArtifactEntry(
    string Family,
    string Path,
    string Kind,
    long SizeBytes,
    string Digest,
    string LastWriteUtc,
    bool Candidate,
    string Action);

public sealed record ArtifactArchiveManifest(
    int SchemaVersion,
    string ArchiveId,
    string CreatedUtc,
    string Family,
    string ZipPath,
    string ZipDigest,
    IReadOnlyList<LocalArtifactEntry> Entries);

public sealed record ArtifactOperationResult(
    string Status,
    string? RepositoryPath,
    IReadOnlyList<ArtifactRetentionRule> Rules,
    IReadOnlyList<LocalArtifactEntry> Entries,
    IReadOnlyList<ArtifactArchiveManifest> Archives,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
}
