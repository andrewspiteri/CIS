namespace Cis.Abstractions;

public static class CisAgentWorkPaths
{
    public const string Ledger = ".cis/local/agents/work-control.json";
    public const string Adoption = ".cis/local/agents/work-control-adoption.json";
    public const string Lock = ".cis/local/agents/work-control.lock";
}

/// <summary>Canonical plan identity supplied by CIS, never by a provider result.</summary>
public sealed record CisAgentWorkIdentity(string AuthorityRepositoryId, string ChangeId, string TaskId,
    IReadOnlyList<string> AncestorTaskIds, IReadOnlyList<string> AcceptedOutcomeReferences);

public sealed record CisAgentWorkReference(string AdoptionId, string AuthorityRepositoryId, string ChangeId,
    string TaskId, string OwnerTaskId, string ExecutionId, string LogicalAttemptId, string? ParentExecutionId);

public sealed record CisAgentWorkPreparationOwner(string HostId, int ProcessId, string ProcessStartedAtUtc);

public sealed record CisAgentWorkTaskAllocation(string TaskId, long Seconds, int Attempts,
    long RestorationSeconds = 0, bool Fixed = true, bool AllowRedistribution = false);

public sealed record CisAgentWorkAllocationRequest(long ChangeSeconds, int ChangeAttempts,
    long RestorationSeconds, IReadOnlyList<CisAgentWorkTaskAllocation> Tasks, string DecisionReference,
    string StorageCeilingReference, string? ExpectedRevision = null, bool AcknowledgeHistoricalUnknown = false)
{
    public IReadOnlyList<string> Controllers { get; init; } = [];
}

/// <summary>Controller reflection. Numeric consumption is always measured by CIS.</summary>
public sealed record CisAgentWorkCheckpoint(string Outcome, IReadOnlyList<string> Evidence,
    string Approach, string Decision, string NextAction)
{
    public string? Hypothesis { get; init; }
    public string? ExpectedResult { get; init; }
    public string? BlockerKey { get; init; }
    public bool MaterialNewInformation { get; init; }
    public bool CloseAttempt { get; init; }
}

public sealed record CisAgentWorkProgressRequest(CisAgentWorkCheckpoint Checkpoint, string ExpectedRevision);

public sealed record CisAgentWorkDispositionRequest(string Kind, string ExpectedRevision)
{
    public string? ExecutionId { get; init; }
    public string? Hypothesis { get; init; }
    public string? NextAction { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
}

public sealed record CisAgentWorkAllowance(long AllocatedMilliseconds, long ConsumedMilliseconds,
    long ReservedMilliseconds, long RestorationMilliseconds, int AllocatedAttempts, int UsedAttempts)
{
    public long RemainingMilliseconds => Math.Max(0, AllocatedMilliseconds - ConsumedMilliseconds - ReservedMilliseconds - RestorationMilliseconds);
    public int RemainingAttempts => Math.Max(0, AllocatedAttempts - UsedAttempts);
}

public sealed record CisAgentWorkExecution(string ExecutionId, string TaskId, string OwnerTaskId,
    string LogicalAttemptId, string? ParentExecutionId, string RootExecutionId, string Status,
    string StartedAtUtc, string? CompletedAtUtc, long ActiveMilliseconds, long ElapsedMilliseconds,
    long ReservedMilliseconds, int? ProcessId, string? ProcessStartedAtUtc, string? RecoveryReason)
{
    public bool Finalized { get; init; }
    public bool LaunchIntent { get; init; }
    public CisAgentWorkPreparationOwner? PreparationOwner { get; init; }
}

public sealed record CisAgentWorkCheckpointRecord(string LogicalAttemptId, string Actor, string RecordedAtUtc,
    CisAgentWorkCheckpoint Checkpoint, long ActiveMilliseconds, long ReservedMilliseconds)
{
    public string? Origin { get; init; }
}

public sealed record CisAgentWorkView(string AuthorityRepositoryId, string ChangeId, string? TaskId,
    string? OwnerTaskId, string Revision, string AdoptionId, string Coverage, string RecoveryState,
    string? HoldReason, string NextAction, long ElapsedMilliseconds, CisAgentWorkAllowance Change, CisAgentWorkAllowance? Task,
    string? LogicalAttemptId, int RepeatedBlockerCount, string DecisionReference, string StorageOwner,
    string StorageCeilingReference, IReadOnlyList<CisAgentWorkExecution> Executions,
    IReadOnlyList<CisAgentWorkCheckpointRecord> Checkpoints)
{
    public IReadOnlyList<string> AcceptedOutcomeReferences { get; init; } = [];
    public string? Hypothesis { get; init; }
    public string? ExpectedResult { get; init; }
    public long? ReflectionWindowMilliseconds { get; init; }
    public long LastControllerReflectionMilliseconds { get; init; }
    public bool ReflectionDue { get; init; }
}

public sealed record CisAgentWorkResult(string Status, CisAgentWorkView? Work, IReadOnlyList<string> Errors, bool Applied)
{
    public int ExitCode => Errors.Count > 0 || Status is "held" or "invalid" or "busy" or "missing-allocation" ? 4 : 0;
}
