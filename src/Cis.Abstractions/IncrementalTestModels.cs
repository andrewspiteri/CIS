namespace Cis.Abstractions;

/// <summary>Native commands and complete input inventory for bounded development feedback.</summary>
public sealed record IncrementalTestProfile
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string WorkingDirectory { get; init; } = ".";
    public string[] SourceFiles { get; init; } = [];
    public string[] TestFiles { get; init; } = [];
    public string[] ConfigFiles { get; init; } = [];
    public string[] ToolFiles { get; init; } = [];
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);
    public string FullTestFilter { get; init; } = "";
    public string[] UnitCommand { get; init; } = [];
    public string UnitReportPath { get; init; } = "";
    public string UnitResultFormat { get; init; } = "trx";
    public string[]? MutationCommand { get; init; }
    public string? StrykerConfigFile { get; init; }
    public string? CanarySourceFile { get; init; }
    public int MinimumCanaryKills { get; init; } = 1;
    public string MutationReportPath { get; init; } = "reports/mutation-report.json";
    public string[]? RecoveryCommand { get; init; }
    public double BreakThreshold { get; init; } = 90;
    public int TimeoutSeconds { get; init; } = 300;
    public int RecoveryTimeoutSeconds { get; init; } = 120;
    public long MaxRunBytes { get; init; } = 134217728;
    public long MaxRetainedBytes { get; init; } = 268435456;
    public long MinFreeBytes { get; init; } = 1073741824;
}

public sealed record IncrementalTestOptions(string RepositoryPath, string ProfilePath,
    string Layer = "all", int MaximumBatches = 4, int MaximumSeconds = 1200, bool Force = false);

public sealed record IncrementalTestBatch(string Id, string SourceFile, string Selection,
    string[] TestFiles, string TestFilter, string UnitFingerprint, string MutationFingerprint,
    bool UnitCurrent, bool MutationCurrent, string[] DependencyFiles);

public sealed record IncrementalStageResult(string Fingerprint, string Status, bool Completed,
    string ReportPath, string ReportSha256, MutationNativeExecution? Execution,
    int Total, int Passed, int Failed, int Skipped, TestMutationSummary? Mutation,
    DateTimeOffset FinishedUtc, string[] Diagnostics)
{
    public MutationNativeExecution? RecoveryExecution { get; init; }
    public MutationNativeExecution? PostflightExecution { get; init; }
    public int PostflightPassed { get; init; }
    public IncrementalProducerRevalidation[] ProducerRevalidations { get; init; } = [];
}

/// <summary>Reviewed compatibility proof for an unchanged native report and its producer inputs.</summary>
public sealed record IncrementalProducerRevalidation(string PreviousTestingAssemblyPath, string PreviousHelperAssemblyPath,
    string PreviousTestingSha256, string PreviousHelperSha256, string TestingSha256, string HelperSha256,
    string PreviousFingerprint, string Fingerprint, string ReportSha256,
    string PreviousEnvironmentSha256, string EnvironmentSha256, bool LegacySessionEnvironment,
    string Reason, DateTimeOffset VerifiedUtc);

public sealed record IncrementalBatchState(string SourceFile, IncrementalStageResult? Unit,
    IncrementalStageResult? Mutation);

public sealed record IncrementalTestState
{
    public int SchemaVersion { get; init; } = 1;
    public bool RecoveryRequired { get; init; }
    public string? RecoverySourceFile { get; init; }
    public DateTimeOffset? ActiveExecutionUntilUtc { get; init; }
    public Dictionary<string, IncrementalBatchState> Batches { get; init; } = new(StringComparer.Ordinal);
}

public sealed record IncrementalTestResult(string Status, int ExitCode)
{
    public string? ProfileId { get; init; }
    public string? GraphBuildId { get; init; }
    public string? StatePath { get; init; }
    public string Layer { get; init; } = "all";
    public bool ScopeComplete { get; init; }
    public bool Qualified => false;
    public int ExecutedUnitBatches { get; init; }
    public int ExecutedMutationBatches { get; init; }
    public int ReusedUnitBatches { get; init; }
    public int ReusedMutationBatches { get; init; }
    public int PendingUnitBatches { get; init; }
    public int PendingMutationBatches { get; init; }
    public int Killed { get; init; }
    public int Survived { get; init; }
    public int TimedOut { get; init; }
    public int NoCoverage { get; init; }
    public int KnownDenominator { get; init; }
    public double? KnownScore { get; init; }
    public double? CompleteScopeScore { get; init; }
    public bool ThresholdMet { get; init; }
    public long DurationMilliseconds { get; init; }
    public long RetainedBytes { get; init; }
    public IReadOnlyList<IncrementalTestBatch> Batches { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
