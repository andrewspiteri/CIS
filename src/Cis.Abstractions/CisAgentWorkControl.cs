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
    public CisOwnedStoragePolicy? Storage { get; init; }
}

/// <summary>An explicit original-authority decision replacing an existing task time ceiling.</summary>
public sealed record CisAgentWorkTaskTimeAmendmentRequest(long Seconds, string DecisionReference, string ExpectedRevision);

public sealed record CisAgentWorkTaskTimeAmendment(string Reference, string AuthorityRepositoryId,
    string ChangeId, string TaskId, string OwnerTaskId, long PreviousAllocatedMilliseconds, long AllocatedMilliseconds,
    string Actor, string Reason, string DecisionReference, string RecordedAtUtc, string PreviousRevision, string Revision);

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
    public string? CorrectionRelationshipId { get; init; }
    public IReadOnlyList<string> CorrectionFindingReferences { get; init; } = [];
    public int? CorrectionMaximumSeconds { get; init; }
    public IReadOnlyList<string> HistoricalRunIds { get; init; } = [];
}

public sealed record CisAgentWorkStage(string Route, string RelationshipId, string Name, string RepositoryId,
    string? CorrectionRoundId = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeOperation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeRecoveryBasis { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RepositoryPath { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ContractDigest { get; init; }
}

public sealed record CisAgentWorkCorrectionStage(string RepositoryId, string RunId, string Candidate,
    string RecordedAtUtc, IReadOnlyList<string> Evidence);

public sealed record CisAgentWorkCandidateTree(string RepositoryId, string BaselineTree, string CandidateTree);

public sealed record CisAgentWorkCorrectionContext(string ContractDigest, string MaterialBasis,
    IReadOnlyList<CisStoryReviewFinding> Findings, IReadOnlyList<CisAgentWorkCandidateTree> Candidates);

/// <summary>The exact controller disposition governing one bounded correction step.</summary>
public sealed record CisAgentWorkCorrectionDirection(string DispositionReference, string Kind, string Hypothesis,
    string NextAction, string Actor, string RecordedAtUtc, string Reason, IReadOnlyList<string> Evidence);

public sealed record CisAgentWorkCorrectionContinuation(string Id, string RelationshipId, int MaximumSeconds,
    string NextAction, string Actor, string RecordedAtUtc, CisAgentWorkCorrectionContext? Context)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisAgentWorkCorrectionDirection? AuthorizedDirection { get; init; }
}

/// <summary>A retained correction round and its accounting basis.</summary>
/// <param name="Id">The retained round identity.</param>
/// <param name="RelationshipId">The owning story relationship.</param>
/// <param name="Number">The durable round number.</param>
/// <param name="Status">The recorded round state.</param>
/// <param name="StartedAtUtc">The measurement origin. For a granted round this is the grant's recorded time,
/// including earlier active preparation, rather than the later candidate verification or round creation time.
/// Measured cost unions relevant active intervals and excludes settled waiting.</param>
/// <param name="FindingReferences">The required findings bound to this round.</param>
/// <param name="Stages">The retained completed repository stages.</param>
public sealed record CisAgentWorkCorrectionRound(string Id, string RelationshipId, int Number, string Status,
    string StartedAtUtc, IReadOnlyList<string> FindingReferences, IReadOnlyList<CisAgentWorkCorrectionStage> Stages)
{
    public string? CompletedAtUtc { get; init; }
    public string? DispositionReference { get; init; }
    public int? MaximumSeconds { get; init; }
    public IReadOnlyList<string> ExecutionIds { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisAgentWorkCorrectionContext? Context { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ContinuationReferences { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CisAgentWorkCorrectionDirection>? AuthorizedDirections { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UnresolvedDirectionReferences { get; init; }
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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NativeRestorationRequired { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RestorationOfExecutionId { get; init; }
    public bool Finalized { get; init; }
    public bool LaunchIntent { get; init; }
    public CisAgentWorkPreparationOwner? PreparationOwner { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisNativeWorkContainment? NativeContainment { get; init; }
    public CisAgentWorkStage? Stage { get; init; }
}

public sealed record CisAgentWorkCheckpointRecord(string LogicalAttemptId, string Actor, string RecordedAtUtc,
    CisAgentWorkCheckpoint Checkpoint, long ActiveMilliseconds, long ReservedMilliseconds)
{
    public string? Origin { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? StoryRelationshipId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? StoryContractDigest { get; init; }
}

public sealed record CisAgentWorkView(string AuthorityRepositoryId, string ChangeId, string? TaskId,
    string? OwnerTaskId, string Revision, string AdoptionId, string Coverage, string RecoveryState,
    string? HoldReason, string NextAction, long ElapsedMilliseconds, CisAgentWorkAllowance Change, CisAgentWorkAllowance? Task,
    string? LogicalAttemptId, int RepeatedBlockerCount, string DecisionReference, string StorageOwner,
    string StorageCeilingReference, IReadOnlyList<CisAgentWorkExecution> Executions,
    IReadOnlyList<CisAgentWorkCheckpointRecord> Checkpoints)
{
    public IReadOnlyList<CisAgentWorkTaskTimeAmendment> TaskTimeAmendments { get; init; } = [];
    public IReadOnlyList<string> AcceptedOutcomeReferences { get; init; } = [];
    public string? Hypothesis { get; init; }
    public string? ExpectedResult { get; init; }
    public long? ReflectionWindowMilliseconds { get; init; }
    public long LastControllerReflectionMilliseconds { get; init; }
    public bool ReflectionDue { get; init; }
    public IReadOnlyList<CisAgentWorkCorrectionRound> Corrections { get; init; } = [];
    public bool CorrectionOnly { get; init; }
    public string? CorrectionRelationshipId { get; init; }
    public IReadOnlyList<string> RequiredCorrectionFindingReferences { get; init; } = [];
    public bool CorrectionContinuationAvailable { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisAgentWorkCorrectionContext? RequiredCorrectionContext { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CisAgentWorkCorrectionContinuation? CorrectionContinuation { get; init; }
}

public sealed record CisAgentWorkResult(string Status, CisAgentWorkView? Work, IReadOnlyList<string> Errors, bool Applied)
{
    public int ExitCode => Errors.Count > 0 || Status is "held" or "invalid" or "busy" or "missing-allocation" ? 4 : 0;
}
