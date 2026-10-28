namespace Cis.Abstractions;

/// <summary>Verify a completed native rerun without executing tests or changing raw reports.</summary>
public sealed record IncrementalRefinementOptions(string RepositoryPath, string ProfilePath,
    string RerunProfilePath, string Reason);

public sealed record IncrementalMutationCounts(int Killed, int Survived, int TimedOut, int NoCoverage,
    int Denominator, double? Score);

public sealed record IncrementalRefinementCredit(string SourceFile, string MutantId,
    string DefinitionSha256, string[] KillingTestIds, string[] KillingTestDefinitionSha256);

public sealed record IncrementalRefinementBinding(string SourceFile, string OriginalReportPath,
    string OriginalReportSha256, string OriginalFingerprint, string RerunReportPath,
    string RerunReportSha256, string RerunFingerprint, string OriginalStageSha256, string RerunStageSha256);

public sealed record IncrementalRefinementBaselineBinding(string SourceFile, string ReportPath,
    string ReportSha256, string Fingerprint, DateTimeOffset FinishedUtc);

/// <summary>A derived attestation; its raw report and current input bindings remain authoritative.</summary>
public sealed record IncrementalRefinementResult(string Status, int ExitCode)
{
    public int SchemaVersion => 1;
    public string Policy => "cis-native-timeout-refinement-v1";
    public bool Qualified => false;
    public string? ProfileId { get; init; }
    public string? RerunProfileId { get; init; }
    public string? OriginalProfileSha256 { get; init; }
    public string? RerunProfileSha256 { get; init; }
    public string? OriginalProfilePath { get; init; }
    public string? RerunProfilePath { get; init; }
    public string? EnvironmentSha256 { get; init; }
    public string? TestingProducerSha256 { get; init; }
    public string? HelperProducerSha256 { get; init; }
    public string? SidecarPath { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset VerifiedUtc { get; init; }
    public bool RequiresBaseline { get; init; }
    public bool ThresholdMet { get; init; }
    public IncrementalMutationCounts? Raw { get; init; }
    public IncrementalMutationCounts? Effective { get; init; }
    public IReadOnlyList<IncrementalRefinementBinding> Bindings { get; init; } = [];
    public IReadOnlyList<IncrementalRefinementBaselineBinding> BaselineBindings { get; init; } = [];
    public IReadOnlyList<MutationInputIdentity> Inputs { get; init; } = [];
    public IReadOnlyList<IncrementalRefinementCredit> Credits { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
