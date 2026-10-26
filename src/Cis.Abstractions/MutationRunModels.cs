namespace Cis.Abstractions;

public sealed record MutationRunOptions(string RepositoryPath, string ProfilePath, string Kind = "diagnostic");

public sealed record MutationRunProfile
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string WorkingDirectory { get; init; } = ".";
    public string[] Command { get; init; } = [];
    public string ReportPath { get; init; } = "";
    public double BreakThreshold { get; init; } = double.NaN;
    public string[]? CanaryCommand { get; init; }
    public string[]? HealthCheckCommand { get; init; }
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);
    public int MinimumCanaryKills { get; init; } = 1;
    public int TimeoutSeconds { get; init; } = 1200;
    public long MaxRunBytes { get; init; } = 536870912;
    public long MaxRetainedBytes { get; init; } = 1073741824;
    public long MinFreeBytes { get; init; } = 1073741824;
    public string[] SourceFiles { get; init; } = [];
    public string[] ConfigFiles { get; init; } = [];
}

public sealed record MutationRunLimits(int TimeoutSeconds, long MaxRunBytes, long MaxRetainedBytes, long MinFreeBytes);
public sealed record MutationInputIdentity(string Path, string Sha256);
public sealed record MutationSourceIdentity(string Digest, IReadOnlyList<MutationInputIdentity> Inputs, bool Unchanged);
public sealed record MutationNativeExecution(string Status, int? ExitCode, long DurationMilliseconds,
    long PeakRunBytes, long PeakRetainedBytes, bool OutputTruncated, string? Error)
{
    public string? ExecutablePath { get; init; }
    public string[]? Command { get; init; }
    public string? CisToolAssemblySha256 { get; init; }
}

public sealed record MutationRunResult(string Status, int ExitCode, string Kind)
{
    public string? ProfileId { get; init; }
    public string? RunId { get; init; }
    public bool Partial { get; init; } = true;
    public bool Qualified { get; init; }
    public string? ProfileSha256 { get; init; }
    public MutationSourceIdentity? SourceIdentity { get; init; }
    public MutationNativeExecution? NativeExecution { get; init; }
    public MutationNativeExecution? HealthCheckExecution { get; init; }
    public TestSuiteExecution? Assessment { get; init; }
    public MutationRunResult? Canary { get; init; }
    public string? ReportPath { get; init; }
    public string ReportAvailability { get; init; } = "unavailable";
    public string? SummaryPath { get; init; }
    public MutationRunLimits? Limits { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset FinishedUtc { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
