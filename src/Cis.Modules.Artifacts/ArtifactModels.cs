namespace Cis.Modules.Artifacts;

public sealed record ArtifactRetentionRule(
    string Family,
    string Path,
    int RetainDays,
    int KeepLatest,
    string Action,
    long? MaximumBytes = null);

public sealed record ArtifactFamilyStorage(
    string Family,
    long LiveBytes,
    long ProtectedBytes,
    long CandidateBytes,
    long PlannedLiveBytes,
    long? MaximumBytes,
    bool BudgetExceeded,
    bool BudgetBlocked);

public sealed record ArtifactStorageTotals(
    long ConfiguredLiveBytes,
    long ArchiveBytes,
    long ManagementBytes,
    long TotalReportedBytes);

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
    public IReadOnlyList<ArtifactFamilyStorage> FamilyStorage { get; init; } = [];
    public ArtifactStorageTotals? Storage { get; init; }
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
}
