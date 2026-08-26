namespace Cis.Abstractions;

public enum TestFailureKind
{
    None,
    Product,
    Infrastructure,
    MissingPrerequisite,
    Timeout,
    Cancelled,
    Unknown,
    InvalidEvidence,
}

public sealed record TestSuiteProfile(
    string Id,
    string Component,
    string Layer,
    string Framework,
    string Command,
    string WorkingDirectory,
    string ResultFormat,
    string ResultPath,
    string CoveragePath,
    string MutationPath,
    string Prerequisites,
    string AppliesWhen,
    string CiTier,
    string ArtifactPolicy);

public sealed record TestCaseExecution(
    string Id,
    string Name,
    string Status,
    double DurationMilliseconds,
    string? Source,
    string? Failure);

public sealed record TestCoverageSummary(
    double Lines,
    double Statements,
    double Functions,
    double Branches,
    string Scope,
    string SourcePath);

public sealed record TestMutationSummary(
    double Score,
    int Killed,
    int Survived,
    int TimedOut,
    int NoCoverage,
    double? HighThreshold,
    double? LowThreshold,
    double? BreakThreshold,
    string SourcePath);

public sealed record TestArtifact(
    string Kind,
    string Path,
    string Digest,
    long Bytes);

public sealed record TestSuiteExecution(
    string SuiteId,
    string Layer,
    string Framework,
    string Status,
    TestFailureKind FailureKind,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    double DurationMilliseconds,
    IReadOnlyList<TestCaseExecution> Cases,
    TestCoverageSummary? Coverage,
    TestMutationSummary? Mutation,
    IReadOnlyList<TestArtifact> Artifacts,
    IReadOnlyList<string> Diagnostics);

public sealed record TestRunManifest(
    int SchemaVersion,
    string RunId,
    string WorkflowId,
    string WorkflowDigest,
    string RepositoryId,
    string RepositoryRevision,
    string SuiteProfileDigest,
    string Environment,
    string StartedAtUtc,
    string CompletedAtUtc,
    string Status,
    IReadOnlyList<TestSuiteExecution> Suites,
    IReadOnlyList<TestArtifact> Artifacts,
    string? Implementer = null,
    string? Assurer = null,
    string? AssuranceTechnique = null);

public sealed record TestResultAdapterContext(
    string RepositoryPath,
    TestSuiteProfile Suite,
    string ResultPath,
    string? CoveragePath,
    string? MutationPath);

public interface ICisTestResultAdapter
{
    string Format { get; }

    TestSuiteExecution Read(TestResultAdapterContext context);
}
