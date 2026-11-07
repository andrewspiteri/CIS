namespace Cis.Abstractions;

/// <summary>Small native correctness controls for the declared runner and fixture path.</summary>
public sealed record NativeCorrectnessPilotDefinition
{
    public string[] BaselineCommand { get; init; } = [];
    public string BaselineReportPath { get; init; } = "unit.trx";
    public string UnitResultFormat { get; init; } = "trx";
    public string ControlSourceFile { get; init; } = "";
    public string[] InfrastructureCommand { get; init; } = [];
    public string MissingInfrastructurePath { get; init; } = "";
    public string[] RestorationCommand { get; init; } = [];
    public int RecoveryTimeoutSeconds { get; init; } = 30;
    public string[] ToolFiles { get; init; } = [];
    public string? ChildTestName { get; init; }
}

public sealed record NativePilotObservation(string Role, MutationNativeExecution Execution,
    string? ReportPath = null, string? ReportSha256 = null)
{
    public IReadOnlyList<TestArtifact> Artifacts { get; init; } = [];
    public string? OutputPath { get; init; }
}

public sealed record NativePilotWorkContext(string RecoveryBasis, string? RecoveryOfExecutionId,
    CisNativeWorkOptions? Association)
{
    public bool RestorationRequired { get; init; }
}

/// <summary>Current native observations, not qualification or controller authority.</summary>
public sealed record NativeCorrectnessPilotResult(string Status, bool Ready, string? BasisSha256,
    string NextAction)
{
    public int SchemaVersion { get; init; } = 1;
    public string Policy { get; init; } = "cis-native-correctness-pilot-v1";
    public string? ProfilePath { get; init; }
    public string? SummaryPath { get; init; }
    public string? EvidenceReference => BasisSha256 is null ? null : "native-pilot:" + BasisSha256;
    public bool ControllerAssessed { get; init; }
    /// <summary>Complete native controls and restored health before required work settlement; never readiness authority by itself.</summary>
    public bool? NativeReady { get; init; }
    public bool RecoveryRequired { get; init; }
    public bool Restored { get; init; }
    public string? RecoveryBasis { get; init; }
    public string? RecoveryOfExecutionId { get; init; }
    public CisAgentWorkReference? WorkReference { get; init; }
    public string? AuthorityPath { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset FinishedUtc { get; init; }
    public long PreparationMilliseconds { get; init; }
    public long NativeMilliseconds { get; init; }
    public long ResultAdmissionMilliseconds { get; init; }
    public IReadOnlyList<NativePilotObservation> Observations { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
