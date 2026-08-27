namespace Cis.Abstractions;

public enum SecurityFailureKind
{
    None,
    Finding,
    Scanner,
    Infrastructure,
    MissingPrerequisite,
    Timeout,
    Cancelled,
    InvalidEvidence,
    Unknown,
}

public sealed record SecuritySuiteProfile(
    string Id,
    string Component,
    string Category,
    string Tool,
    string Command,
    string WorkingDirectory,
    string ResultFormat,
    string ResultPath,
    string Target,
    string AppliesWhen,
    string CiTier,
    string FailSeverities,
    string ArtifactPolicy);

public sealed record SecurityFinding(
    string Id,
    string RuleId,
    string Scanner,
    string Category,
    string Severity,
    string Message,
    string Path,
    int? StartLine,
    string Fingerprint,
    string Status = "new",
    string? AcceptanceId = null);

public sealed record SecurityArtifact(
    string Kind,
    string Path,
    string Digest,
    long Bytes,
    string? RunId = null,
    int? Attempt = null,
    string? SuiteId = null,
    string? Component = null,
    string? RepositoryRevision = null);

public sealed record SecuritySuiteExecution(
    string SuiteId,
    string Category,
    string Tool,
    string Status,
    SecurityFailureKind FailureKind,
    int Critical,
    int High,
    int Medium,
    int Low,
    int Accepted,
    IReadOnlyList<SecurityFinding> Findings,
    IReadOnlyList<SecurityArtifact> Artifacts,
    IReadOnlyList<string> Diagnostics);

public sealed record SecurityRunManifest(
    int SchemaVersion,
    string RunId,
    string WorkflowId,
    string WorkflowDigest,
    string RepositoryId,
    string RepositoryRevision,
    string ProfileDigest,
    string Environment,
    string StartedAtUtc,
    string CompletedAtUtc,
    string Status,
    IReadOnlyList<SecuritySuiteExecution> Suites,
    IReadOnlyList<SecurityArtifact> Artifacts);

public sealed record SecurityResultAdapterContext(
    string RepositoryPath,
    SecuritySuiteProfile Suite,
    string ResultPath);

public interface ICisSecurityResultAdapter
{
    string Format { get; }

    SecuritySuiteExecution Read(SecurityResultAdapterContext context);
}

public sealed record AcceptedSecurityFinding(
    string Id,
    string Scanner,
    string Fingerprint,
    string Classification,
    string Reason,
    DateOnly AcceptedUntil,
    string Owner,
    string ApprovedBy,
    string ApprovalReference);
