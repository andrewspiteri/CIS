namespace Cis.Abstractions;

/// <summary>Verify current saved native evidence without executing tests or changing the cache.</summary>
public sealed record IncrementalEvidenceOptions(string RepositoryPath, string ProfilePath,
    string[]? RefinementProfilePaths = null);

public sealed record IncrementalEvidenceInput(string Path, string Sha256, string[] Roles);

/// <summary>Indices address the receipt's deduplicated input inventory.</summary>
public sealed record IncrementalEvidenceClosure(string Id, int[] TestInputIndices, int[] DependencyInputIndices);

/// <summary>Execution facts and digests; command and environment values are never exported.</summary>
public sealed record IncrementalEvidenceExecution(string Role, string Status, int? ExitCode,
    long DurationMilliseconds, string CommandSha256, string HelperProducerSha256,
    bool OutputTruncated, bool HasError);

public sealed record IncrementalEvidenceBatch(string SourceFile, string ClosureId,
    string UnitFingerprint, string MutationFingerprint, string TestFilterSha256, bool FullFile,
    string UnitReportPath, string UnitReportSha256, long UnitReportBytes, string UnitStageSha256,
    DateTimeOffset UnitFinishedUtc, int UnitPassed,
    string? MutationReportPath, string? MutationReportSha256, long? MutationReportBytes,
    string? MutationStageSha256, IncrementalMutationCounts? Raw,
    IncrementalEvidenceExecution[] Executions)
{
    public bool FullPostflightReceiptVerified { get; init; }
    public bool RawPostflightRetained => false;
}

public sealed record IncrementalEvidenceRefinement(string RerunProfilePath, string RerunProfileSha256,
    IReadOnlyList<IncrementalRefinementBinding> Bindings,
    IReadOnlyList<IncrementalRefinementBaselineBinding> BaselineBindings)
{
    public IReadOnlyList<IncrementalEvidenceReportPin> RerunReports { get; init; } = [];
}

public sealed record IncrementalEvidenceReportPin(string SourceFile, string Role, string Path,
    string Sha256, long Bytes, string Fingerprint, string StageSha256, DateTimeOffset FinishedUtc,
    IncrementalEvidenceExecution[] Executions);

/// <summary>A current declared-scope receipt. Release scope and acceptance remain the receiver's decision.</summary>
public sealed record IncrementalEvidenceResult(string Status, int ExitCode)
{
    public int SchemaVersion => 1;
    public string Policy => "cis-incremental-evidence-v1";
    public bool Qualified => false;
    public string? ProfileId { get; init; }
    public string? ProfilePath { get; init; }
    public string? ProfileSha256 { get; init; }
    public string? GraphBuildId { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public string? OsDescription { get; init; }
    public string? EnvironmentSha256 { get; init; }
    public string? TestingProducerSha256 { get; init; }
    public string? HelperProducerSha256 { get; init; }
    public DateTimeOffset VerifiedUtc { get; init; }
    public bool ScopeComplete { get; init; }
    public bool ThresholdMet { get; init; }
    public IncrementalMutationCounts? Raw { get; init; }
    public IncrementalMutationCounts? Effective { get; init; }
    public IReadOnlyList<IncrementalEvidenceInput> Inputs { get; init; } = [];
    public IReadOnlyList<IncrementalEvidenceClosure> Closures { get; init; } = [];
    public IReadOnlyList<IncrementalEvidenceBatch> Batches { get; init; } = [];
    public IReadOnlyList<IncrementalEvidenceRefinement> Refinements { get; init; } = [];
    public IReadOnlyList<IncrementalRefinementCredit> Credits { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
