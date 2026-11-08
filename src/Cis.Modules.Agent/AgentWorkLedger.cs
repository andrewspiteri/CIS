using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

[assembly: InternalsVisibleTo("Cis.Modules.Agent.Tests")]

namespace Cis.Modules.Agent;

internal sealed record AgentWorkAdmission(CisAgentWorkResult Result, CisAgentWorkReference? Reference, int TimeoutSeconds)
{
    public bool Admitted => Result.Status == "admitted" && Reference is not null;
}

internal sealed record AgentWorkCorrectionAdmission(CisAgentWorkResult Result, CisAgentWorkCorrectionRound? Round);

/// <summary>Direct-task accounting. All mutations are short authority-wide transactions.</summary>
internal sealed partial class AgentWorkLedger(Func<DateTimeOffset>? clock = null)
{
    internal const string RelativePath = CisAgentWorkPaths.Ledger;
    internal const string AdoptionPath = CisAgentWorkPaths.Adoption;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions LedgerJson = new(Json) { WriteIndented = false };
    private const int MaximumLedgerBytes = 8 * 1024 * 1024;
    private const int NativeAdmissionLedgerBytes = 6 * 1024 * 1024;
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    private sealed class Ledger
    {
        public int SchemaVersion { get; set; } = 1;
        public string AuthorityId { get; set; } = "";
        public string AdoptionId { get; set; } = "";
        public long Revision { get; set; }
        public long BaselineStarted { get; set; }
        public bool CorrectionOnly { get; set; }
        public Dictionary<string, Change> Changes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, CorrectionRelationship> Corrections { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed class CorrectionRelationship
    {
        public string Id { get; set; } = "";
        public CisAgentWorkIdentity? Identity { get; set; }
        public string? OwnerTaskId { get; set; }
        public string Actor { get; set; } = "";
        public string? Hypothesis { get; set; }
        public string? ActiveRootExecutionId { get; set; }
        public string? ObservedContractDigest { get; set; }
        public CisAgentWorkCorrectionContext? RequiredContext { get; set; }
        public List<CisAgentWorkCorrectionRound> Rounds { get; set; } = [];
        public List<string> RequiredFindings { get; set; } = [];
        public CorrectionGrant? Grant { get; set; }
    }
    private sealed record CorrectionGrant(string Id, IReadOnlyList<string> Findings, int MaximumSeconds,
        string NextAction, string Actor, string RecordedAtUtc, CisAgentWorkCorrectionContext? Context = null)
    {
        public CisAgentWorkCorrectionDirection? AuthorizedDirection { get; init; }
    }
    private sealed record Adoption(int SchemaVersion, string AuthorityId, string AdoptionId, string Actor, string Reason, string CreatedAtUtc);
    private sealed class Change
    {
        public string Id { get; set; } = "";
        public long Milliseconds { get; set; }
        public int Attempts { get; set; }
        public long Restoration { get; set; }
        public long RestorationSpent { get; set; }
        public long Consumed { get; set; }
        public int UsedAttempts { get; set; }
        public string DecisionReference { get; set; } = "";
        public string StorageReference { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Reason { get; set; } = "";
        public List<string> Controllers { get; set; } = [];
        public Dictionary<string, Owner> Owners { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Execution> Executions { get; set; } = new(StringComparer.Ordinal);
        public List<AllocationAudit> AllocationHistory { get; set; } = [];
        public List<CisAgentWorkTaskTimeAmendment> TaskTimeAmendments { get; set; } = [];
        public Dictionary<string, HistoricalCharge> HistoricalRuns { get; set; } = new(StringComparer.Ordinal);
        public StorageAllocation? Storage { get; set; }
    }
    private sealed record HistoricalCharge(string RelationshipId, string OwnerTaskId, long Milliseconds,
        string Actor, string Reason, string RecordedAtUtc);
    private sealed record AllocationAudit(string Actor, string Reason, string RecordedAtUtc,
        IReadOnlyList<CisAgentWorkTaskAllocation> Tasks);
    private sealed class Owner
    {
        public string Id { get; set; } = "";
        public long Milliseconds { get; set; }
        public int Attempts { get; set; }
        public long Restoration { get; set; }
        public long RestorationSpent { get; set; }
        public bool Fixed { get; set; }
        public bool AllowRedistribution { get; set; }
        public long Consumed { get; set; }
        public int UsedAttempts { get; set; }
        public long? ReflectionWindowMilliseconds { get; set; }
        public string? OpenAttempt { get; set; }
        public string? Hypothesis { get; set; }
        public string? ExpectedResult { get; set; }
        public string? LastBlocker { get; set; }
        public int RepeatedBlockers { get; set; }
        public string? Hold { get; set; }
        public string? HoldKind { get; set; }
        public string NextAction { get; set; } = "Run the next bounded action within the allocation.";
        public List<string> Outcomes { get; set; } = [];
        public List<CisAgentWorkCheckpointRecord> Checkpoints { get; set; } = [];
    }
    private sealed class Execution
    {
        public CisAgentWorkReference Reference { get; set; } = new("", "", "", "", "", "", "", null);
        public string RootId { get; set; } = "";
        public long Started { get; set; }
        public long? Completed { get; set; }
        public long Bound { get; set; }
        public string Status { get; set; } = "reserved";
        public string? TerminalStatus { get; set; }
        public bool LaunchIntent { get; set; }
        public bool Stopped { get; set; }
        public bool Finalized { get; set; }
        public bool ConservativeRecovery { get; set; }
        public string? RestorationOfExecutionId { get; set; }
        public bool NativeRestorationRequired { get; set; }
        public bool NativeCancellationRequested { get; set; }
        public CisAgentWorkPreparationOwner? PreparationOwner { get; set; }
        public string? Controller { get; set; }
        public CisNativeWorkContainment? NativeContainment { get; set; }
        public CisAgentWorkStage? Stage { get; set; }
        public int? ProcessId { get; set; }
        public string? ProcessStartedAtUtc { get; set; }
        public string? RecoveryReason { get; set; }
        public string? Summary { get; set; }
        public List<string> Evidence { get; set; } = [];
        public List<Interval> Intervals { get; set; } = [];
    }
    private sealed record Interval(long Start, long? End);

    internal CisAgentWorkResult Read(CisRepositoryContext authority, string changeId, string? taskId = null, int? reflectionWindowSeconds = null,
        CisAgentWorkStage? stage = null)
    {
        try
        {
            // The immutable marker lets a missing-state status remain entirely
            // observational. Existing snapshots share the short transaction lock
            // so Windows replacement cannot race path/attribute inspection.
            if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, RelativePath, out var path)
                || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, AdoptionPath, out var marker))
                throw new InvalidDataException("Work accounting paths must remain inside the authority.");
            if (!File.Exists(path) && !File.Exists(marker)) return Missing();
            using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
            var state = Load(authority);
            if (state is null) return Missing();
            if (state.CorrectionOnly) return CorrectionResult(state, null, "status", false);
            if (reflectionWindowSeconds is { } window)
            {
                if (window < 30) throw new InvalidDataException("The adopted reflection window must be at least 30 seconds.");
                if (state.Changes.TryGetValue(changeId, out var projectedChange))
                    foreach (var owner in projectedChange.Owners.Values) owner.ReflectionWindowMilliseconds = checked((long)window * 1000);
            }
            return state.Changes.TryGetValue(changeId, out var change) ? Result(state, change, taskId, "status", false, stage: stage)
                : Result(state, new() { Id = changeId }, null, "missing-allocation", false) with
                { Errors = ["This change has no owning allocation. Supply its allocation using the current accounting revision."] };
        }
        catch (Exception error) when (Recoverable(error)) { return Failure(error); }
    }

    internal CisAgentWorkResult Allocate(CisRepositoryContext authority, string changeId, CisAgentWorkAllocationRequest request,
        IReadOnlyList<CisAgentWorkIdentity> tasks, string actor, string reason, bool hasHistoricalWorkReferences = false)
        => Mutate(authority, state =>
        {
            Text(actor, "Allocation actor"); Text(reason, "Allocation rationale"); Text(request.DecisionReference, "Allocation decision reference");
            Text(request.StorageCeilingReference, "Storage ceiling reference");
            if (request.Tasks is null || request.Tasks.Count is < 1 or > 1000 || request.Tasks.Select(item => item.TaskId).Distinct(StringComparer.Ordinal).Count() != request.Tasks.Count)
                throw new InvalidDataException("Supply one bounded allocation for each distinct canonical task owner.");
            if (state is null)
            {
                if (hasHistoricalWorkReferences) throw new InvalidDataException("Historical runs reference adopted work accounting. Restore its missing state; a fresh baseline cannot erase consumption.");
                if (!request.AcknowledgeHistoricalUnknown) throw new InvalidDataException("Initial allocation must explicitly acknowledge unmeasured historical effort.");
                if (request.ExpectedRevision is not null) throw new InvalidDataException("The first accounting baseline has no prior revision.");
                state = new() { AuthorityId = authority.RepositoryId, AdoptionId = Guid.NewGuid().ToString("N"), BaselineStarted = Tick() };
            }
            else CheckRevision(state, request.ExpectedRevision);
            if (state.CorrectionOnly)
            {
                if (!request.AcknowledgeHistoricalUnknown)
                    throw new InvalidDataException("Adopting a correction-only history requires acknowledgment of earlier unmeasured work; its correction rounds remain retained.");
                state.CorrectionOnly = false;
            }
            var isNew = !state.Changes.TryGetValue(changeId, out var change);
            if (isNew)
            {
                change = new()
                {
                    Id = changeId,
                    Milliseconds = Milliseconds(request.ChangeSeconds),
                    Attempts = Attempts(request.ChangeAttempts),
                    Restoration = Milliseconds(request.RestorationSeconds),
                    DecisionReference = request.DecisionReference,
                    StorageReference = request.StorageCeilingReference,
                    Actor = actor.Trim(),
                    Reason = reason.Trim(),
                    Controllers = (request.Controllers ?? []).Append(actor.Trim()).Distinct(StringComparer.Ordinal).ToList()
                };
                if (change.Restoration > change.Milliseconds) throw new InvalidDataException("Change restoration capacity must fit its fixed allocation.");
                state.Changes[changeId] = change;
            }
            else
            {
                Authorize(change!, actor);
                if (Milliseconds(request.ChangeSeconds) != change!.Milliseconds || request.ChangeAttempts != change.Attempts
                    || Milliseconds(request.RestorationSeconds) != change.Restoration)
                    throw new InvalidDataException("Ordinary allocation cannot increase or reset the fixed containing-change allowance or its restoration reserve.");
                if (request.DecisionReference != change.DecisionReference || request.StorageCeilingReference != change.StorageReference)
                    throw new InvalidDataException("Retain the owning allocation and storage authority references; ordinary redistribution does not replace them.");
            }
            ApplyStorageAllocation(authority, state, change!, request.Storage, actor, isNew);
            var canonical = tasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
            var oldTotal = change!.Owners.Values.Sum(item => item.Milliseconds);
            var oldAttempts = change.Owners.Values.Sum(item => (long)item.Attempts);
            long addedMilliseconds = 0, addedAttempts = 0;
            foreach (var allocation in request.Tasks.OrderBy(item => canonical.GetValueOrDefault(item.TaskId)?.AncestorTaskIds.Count ?? int.MaxValue))
            {
                if (!canonical.TryGetValue(allocation.TaskId, out var identity)) throw new InvalidDataException("Allocation requires a canonical approved task identity: " + allocation.TaskId);
                ValidateIdentity(authority, changeId, identity);
                var inherited = FindOwner(change, identity);
                if (inherited is not null && inherited.Id != allocation.TaskId)
                    throw new InvalidDataException($"Task {allocation.TaskId} inherits accounting owner {inherited.Id}; decomposition cannot create fresh capacity.");
                var milliseconds = Milliseconds(allocation.Seconds); var attempts = Attempts(allocation.Attempts); var restoration = Milliseconds(allocation.RestorationSeconds);
                if (restoration > milliseconds) throw new InvalidDataException("Task restoration capacity must fit its allocation.");
                if (change.Owners.TryGetValue(allocation.TaskId, out var owner))
                {
                    if (restoration < owner.Restoration)
                        throw new InvalidDataException("Ordinary redistribution cannot reduce protected task restoration capacity. Retain the existing RestorationSeconds value, including when updating other task limits.");
                    if (owner.Fixed && (owner.Milliseconds != milliseconds || owner.Attempts != attempts || owner.Restoration != restoration))
                        throw new InvalidDataException("A separately fixed task limit cannot be overridden by redistribution.");
                    if ((owner.Milliseconds != milliseconds || owner.Attempts != attempts || owner.Restoration != restoration)
                        && !owner.AllowRedistribution) throw new InvalidDataException("This task does not delegate redistribution of its remaining allocation.");
                    if (owner.Fixed != allocation.Fixed || owner.AllowRedistribution != allocation.AllowRedistribution)
                        throw new InvalidDataException("Ordinary allocation cannot broaden task allocation authority.");
                    if (milliseconds < owner.Consumed + Reserved(change, owner.Id) + restoration || attempts < owner.UsedAttempts)
                        throw new InvalidDataException("Redistribution must preserve consumption, reservations and restoration capacity.");
                    owner.Milliseconds = milliseconds; owner.Attempts = attempts; owner.Restoration = restoration;
                }
                else
                {
                    if (!isNew)
                    {
                        if (actor.Trim() != change.Actor)
                            throw new InvalidDataException("Only the original allocation authority may assign previously unallocated outer capacity to a new canonical root task.");
                        addedMilliseconds = checked(addedMilliseconds + milliseconds); addedAttempts += attempts;
                        if (addedMilliseconds > Math.Max(0, change.Milliseconds - change.Restoration - oldTotal)
                            || addedAttempts > Math.Max(0, change.Attempts - oldAttempts))
                            throw new InvalidDataException("The new root allocation exceeds previously unallocated containing-change time or attempts; fixed limits and restoration capacity remain unchanged.");
                    }
                    owner = new()
                    {
                        Id = allocation.TaskId,
                        Milliseconds = milliseconds,
                        Attempts = attempts,
                        Restoration = restoration,
                        Fixed = allocation.Fixed,
                        AllowRedistribution = allocation.AllowRedistribution,
                        Outcomes = identity.AcceptedOutcomeReferences.ToList()
                    };
                    change.Owners[owner.Id] = owner;
                }
                change.Bindings[allocation.TaskId] = owner.Id;
            }
            if (!isNew && (change.Owners.Values.Sum(item => item.Milliseconds) > oldTotal + addedMilliseconds
                || change.Owners.Values.Sum(item => (long)item.Attempts) > oldAttempts + addedAttempts))
                throw new InvalidDataException("Delegated redistribution cannot increase the original task allocation pool.");
            change.AllocationHistory.Add(new(actor.Trim(), reason.Trim(), Time(Tick()), request.Tasks));
            return (state, Result(state, change, null, "allocated", true));
        });

    internal AgentWorkAdmission Admit(CisRepositoryContext authority, CisAgentWorkIdentity identity, string executionId,
        int requestedSeconds, int maximumRunSeconds, string? parentExecutionId = null, string? logicalAttemptId = null,
        CisAgentWorkPreparationOwner? preparationOwner = null, bool clampToRemaining = false, CisAgentWorkStage? stage = null,
        string? controller = null, string? restorationOfExecutionId = null, bool nativeChildContinuation = false)
    {
        CisAgentWorkReference? reference = null; var timeout = 0;
        var result = Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("Direct task execution requires an authorized owning work allocation; no unlimited baseline was substituted.");
            ValidateIdentity(authority, identity.ChangeId, identity); Text(executionId, "Execution identity");
            var change = ChangeFor(state, identity.ChangeId); var owner = FindOwner(change, identity) ?? throw new InvalidDataException("No task or allocated canonical ancestor owns this task's work allowance.");
            if (controller is not null) Authorize(change, controller);
            if (stage?.NativeOperation is not null && !NativeLedgerMaintenance(stage, restorationOfExecutionId)
                && JsonSerializer.SerializeToUtf8Bytes(state, LedgerJson).Length >= NativeAdmissionLedgerBytes)
                return (state, Result(state, change, identity.TaskId, "held", false,
                    "Native accounting history is near its finite ledger capacity. Existing settlement, cancellation, delegated deletion and pinned restoration remain available; preserve retained execution proof before admitting more work."));
            if (change.Executions.ContainsKey(executionId)) throw new InvalidDataException("This execution identity already has accounting state; inspect or reconcile it instead of reserving again.");
            var minimumSeconds = parentExecutionId is null ? 30 : 1;
            if (requestedSeconds < minimumSeconds || requestedSeconds > maximumRunSeconds || maximumRunSeconds < 30)
                throw new InvalidDataException($"The requested execution bound must be between {minimumSeconds} and the adopted {maximumRunSeconds}-second ceiling.");
            if (preparationOwner is not null) ValidatePreparationOwner(preparationOwner);
            owner.ReflectionWindowMilliseconds = checked((long)maximumRunSeconds * 1000);
            var correctionCapacity = StoryCorrectionCapacity(state, change, owner.Id, stage);
            var hold = Hold(change, owner, stage, correctionCapacity);
            Execution? parent = null;
            if (parentExecutionId is not null)
            {
                if (!change.Executions.TryGetValue(parentExecutionId, out parent) || parent.Stopped || parent.Reference.OwnerTaskId != owner.Id
                    || parent.Reference.TaskId != identity.TaskId && !identity.AncestorTaskIds.Contains(parent.Reference.TaskId, StringComparer.Ordinal))
                    throw new InvalidDataException("A child execution must inherit a live canonical parent and its unchanged accounting owner.");
                hold = change.Executions.Values.FirstOrDefault(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id && item.RecoveryReason is not null)?.RecoveryReason
                    ?? owner.Hold ?? NativeCancellationHold(change, owner)
                    ?? (nativeChildContinuation && parent.Stage?.NativeOperation is not null && NativeContinuationOwnsRestoration(change, parent)
                        ? null : NativeRestorationHold(change, owner));
            }
            var restorationOf = restorationOfExecutionId ?? parent?.RestorationOfExecutionId;
            Execution? original = null;
            if (restorationOf is not null)
            {
                if (!change.Executions.TryGetValue(restorationOf, out original) || original.Reference.OwnerTaskId != owner.Id
                    || original.Reference.TaskId != identity.TaskId
                    || original.Stage?.NativeOperation is null || !original.NativeRestorationRequired || !original.Stopped
                    || original.RecoveryReason is not null || original.Stage.NativeRecoveryBasis is null
                    || original.Stage.NativeRecoveryBasis != stage?.NativeRecoveryBasis
                    || NativeSubtree(change, original.Reference.ExecutionId).Any(item => !item.Stopped || item.RecoveryReason is not null))
                    throw new InvalidDataException("Restoration requires the exact stopped native execution, unchanged recovery recipe and accounting owner; a new run or caller flag grants no reserve.");
                hold = change.Executions.Values.FirstOrDefault(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id
                    && item.RecoveryReason is not null)?.RecoveryReason
                    ?? (owner.HoldKind == "repeated-blocker" ? null : owner.Hold) ?? NativeCancellationHold(change, owner);
                if (parent is null && change.Executions.Values.Any(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id))
                    hold ??= "An owned execution remains active; preserve its reservation before starting separate restoration.";
            }
            if (hold is not null) return (state, Result(state, change, identity.TaskId, "held", false, hold));
            if (restorationOf is null && correctionCapacity is { } exhaustedCapacity && exhaustedCapacity.Remaining < minimumSeconds * 1000L)
                return (state, Result(state, change, identity.TaskId, "held", false, exhaustedCapacity.Action));
            var milliseconds = checked((long)requestedSeconds * 1000);
            // The inherited deadline and the new reservation share one origin. Time spent
            // persisting admission must not move a child beyond its parent's bound.
            var started = Tick();
            var available = parent is null ? Math.Min(owner.Milliseconds - owner.Consumed - Reserved(change, owner.Id) - ProtectedRestoration(owner),
                change.Milliseconds - change.Consumed - Reserved(change, null) - ProtectedRestoration(change))
                : Math.Max(0, parent.Started + parent.Bound - started);
            if (restorationOf is not null && parent is null)
                available = Math.Min(ProtectedRestoration(owner) - ReservedRestoration(change, owner.Id),
                    Math.Min(owner.Milliseconds - owner.Consumed - Reserved(change, owner.Id),
                        change.Milliseconds - change.Consumed - Reserved(change, null)));
            if (restorationOf is null && correctionCapacity is { } boundedCapacity) available = Math.Min(available, boundedCapacity.Remaining);
            if (restorationOf is null && stage?.CorrectionRoundId is { } correctionId)
            {
                var relationship = CorrectionFor(state, stage.RelationshipId);
                var correction = relationship.Rounds.SingleOrDefault(round => round.Id == correctionId)
                    ?? throw new InvalidDataException("The correction stage has no reserved durable round.");
                if (correction.Status != "started" || relationship.OwnerTaskId != owner.Id)
                    throw new InvalidDataException("The correction stage must retain its active round and canonical work owner.");
                if (correction.MaximumSeconds is { } correctionSeconds)
                    available = Math.Min(available, Math.Max(0, checked((long)correctionSeconds * 1000) - CorrectionMeasured(change, correction)));
            }
            if (clampToRemaining && milliseconds > available && available >= minimumSeconds * 1000L)
                milliseconds = available / 1000 * 1000;
            if (milliseconds > available)
            {
                var shorter = Math.Max(0, available / 1000);
                return (state, Result(state, change, identity.TaskId, "held", false,
                    shorter >= minimumSeconds && (clampToRemaining || shorter >= 30) ? $"Remaining capacity permits a shorter execution bound of at most {shorter} seconds."
                    : parent is not null && shorter >= 1 ? "The admitted parent has less than the 30-second minimum needed for a direct child run. Retain its measured work; after this parent safely settles, use an eligible continuation under the same owner and allowance."
                    : parent is not null ? "The admitted parent has no usable whole-second execution remainder. Retain its measured work before another bounded continuation."
                    : "Remaining hard allowance cannot support the minimum operation while preserving restoration capacity. Seek the applicable allocation authority."));
            }
            if (restorationOf is null && owner.OpenAttempt is null)
            {
                if (owner.UsedAttempts >= owner.Attempts || change.UsedAttempts >= change.Attempts)
                    return (state, Result(state, change, identity.TaskId, "held", false, "The logical-attempt allowance is exhausted. Seek the applicable allocation authority."));
                owner.OpenAttempt = owner.Id + ":" + ++owner.UsedAttempts; change.UsedAttempts++;
            }
            var attempt = restorationOf is not null ? original!.Reference.LogicalAttemptId : owner.OpenAttempt!;
            if (logicalAttemptId is not null && logicalAttemptId != attempt) throw new InvalidDataException("Run identifiers cannot open or replace the current logical attempt. Close it through controller progress first.");
            reference = new(state.AdoptionId, authority.RepositoryId, change.Id, identity.TaskId, owner.Id, executionId, attempt, parentExecutionId);
            change.Executions[executionId] = new()
            {
                Reference = reference,
                RootId = parent?.RootId ?? executionId,
                Started = started,
                Bound = milliseconds,
                PreparationOwner = preparationOwner,
                Controller = controller?.Trim(),
                Stage = stage,
                RestorationOfExecutionId = restorationOf,
                NativeRestorationRequired = stage?.NativeRecoveryBasis is not null,
                Intervals = [new(started, null)]
            };
            change.Bindings[identity.TaskId] = owner.Id;
            owner.Outcomes = owner.Outcomes.Concat(identity.AcceptedOutcomeReferences).Distinct(StringComparer.Ordinal).ToList();
            timeout = checked((int)(milliseconds / 1000));
            if (stage?.CorrectionRoundId is { } roundId)
            {
                var relation = CorrectionFor(state, stage.RelationshipId);
                var at = relation.Rounds.FindIndex(round => round.Id == roundId);
                relation.Rounds[at] = relation.Rounds[at] with { ExecutionIds = relation.Rounds[at].ExecutionIds.Append(executionId).ToArray() };
            }
            return (state, Result(state, change, identity.TaskId, "admitted", true));
        });
        return new(result, result.Status == "admitted" ? reference : null, timeout);
    }

    internal CisAgentWorkResult MarkLaunchIntent(CisRepositoryContext authority, CisAgentWorkReference reference)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            if (execution.Stopped) throw new InvalidDataException("A stopped execution cannot launch again.");
            if (NativeCancellationHold(change, change.Owners[execution.Reference.OwnerTaskId]) is { } cancellation)
                throw new InvalidDataException(cancellation);
            execution.LaunchIntent = true; execution.Status = "launch-intent";
            return Result(state, change, reference.TaskId, "launch-intent", true);
        });

    internal CisAgentWorkResult AttachProcess(CisRepositoryContext authority, CisAgentWorkReference reference, int processId, string startedAtUtc,
        CisNativeWorkContainment? containment = null)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            if (execution.Stopped || !execution.LaunchIntent || processId <= 0 || !DateTimeOffset.TryParse(startedAtUtc, out _))
                throw new InvalidDataException("Attach only a verified owned process to a live launch intent.");
            if (NativeCancellationHold(change, change.Owners[execution.Reference.OwnerTaskId]) is { } cancellation)
                throw new InvalidDataException(cancellation);
            if (execution.ProcessId is not null && (execution.ProcessId != processId || execution.ProcessStartedAtUtc != startedAtUtc))
                throw new InvalidDataException("An execution's owned process identity cannot be replaced.");
            if (containment is not null && (execution.Stage?.NativeOperation is null
                || execution.NativeContainment is not null && execution.NativeContainment != containment))
                throw new InvalidDataException("Native containment must retain its original actual native-driver binding.");
            execution.ProcessId = processId; execution.ProcessStartedAtUtc = startedAtUtc; execution.Status = "running";
            if (containment is not null) execution.NativeContainment = containment;
            return Result(state, change, reference.TaskId, "running", true);
        });

    internal CisAgentWorkResult MarkRecoveryUncertain(CisRepositoryContext authority, CisAgentWorkReference reference, string reason)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            Text(reason, "Recovery reason");
            if (execution.Finalized) return Result(state, change, reference.TaskId, "unchanged", false);
            execution.RecoveryReason = reason; execution.Status = "uncertain";
            return Result(state, change, reference.TaskId, "held", true, reason);
        });

    internal CisAgentWorkResult Settle(CisRepositoryContext authority, CisAgentWorkReference reference, string status, bool ownedWorkStopped,
        string? summary = null, IReadOnlyList<string>? evidence = null, bool conservativeRecovery = false)
        => ExecutionChange(authority, reference, (state, change, execution) =>
            SettleCore(state, change, execution, status, ownedWorkStopped, summary, evidence, conservativeRecovery));

    private CisAgentWorkResult SettleCore(Ledger state, Change change, Execution execution, string status, bool ownedWorkStopped,
        string? summary, IReadOnlyList<string>? evidence, bool conservativeRecovery)
    {
        var reference = execution.Reference;
        if (execution.Finalized || execution.Stopped) return Result(state, change, reference.TaskId, "unchanged", false);
        if (!ownedWorkStopped)
        {
            execution.RecoveryReason = "Owned execution has not been proven stopped; its reservation remains held.";
            execution.Status = "uncertain";
            return Result(state, change, reference.TaskId, "held", true, execution.RecoveryReason);
        }
        var stopped = Tick(); if (stopped < execution.Started) throw new InvalidDataException("The accounting clock moved backwards; reconcile usage before releasing capacity.");
        execution.Stopped = true; execution.Completed = stopped; execution.Status = status; execution.TerminalStatus = status; execution.Summary = summary;
        execution.NativeCancellationRequested = false;
        execution.ConservativeRecovery = conservativeRecovery;
        execution.Evidence = evidence?.ToList() ?? []; execution.RecoveryReason = null;
        execution.Intervals[^1] = execution.Intervals[^1] with { End = stopped };
        if (execution.Stage?.NativeOperation is not null && NativeSubtree(change, reference.ExecutionId).All(item => !item.LaunchIntent))
            foreach (var native in NativeSubtree(change, reference.ExecutionId)) native.NativeRestorationRequired = false;
        if (execution.Stage?.NativeOperation is not null && (status is "native-finished" or "Restored")
            && NativeSubtree(change, reference.ExecutionId).All(item => item.Stopped && item.RecoveryReason is null))
        {
            foreach (var native in NativeSubtree(change, reference.ExecutionId)) native.NativeRestorationRequired = false;
            if (status == "Restored" && execution.RestorationOfExecutionId is { } restored)
                foreach (var native in NativeSubtree(change, restored)) native.NativeRestorationRequired = false;
        }
        var root = change.Executions[execution.RootId];
        var tree = change.Executions.Values.Where(item => item.RootId == root.RootId).ToArray();
        if (root.Stopped && tree.All(item => item.Stopped))
        {
            var charge = UnionMilliseconds(tree.SelectMany(item => item.Intervals).Select(interval => (interval.Start, interval.End!.Value)));
            if (tree.Any(item => item.ConservativeRecovery)) charge = Math.Max(charge, root.Bound);
            change.Consumed = checked(change.Consumed + charge);
            var owner = change.Owners[root.Reference.OwnerTaskId]; owner.Consumed = checked(owner.Consumed + charge);
            if (root.RestorationOfExecutionId is not null)
            {
                owner.RestorationSpent = checked(owner.RestorationSpent + Math.Min(charge, ProtectedRestoration(owner)));
                change.RestorationSpent = checked(change.RestorationSpent + Math.Min(charge, ProtectedRestoration(change)));
            }
            foreach (var item in tree) { item.Finalized = true; item.Status = item.TerminalStatus ?? item.Status; }
            SettleReconciledNativeStorage(change, tree);
            var checkpoint = new CisAgentWorkCheckpoint(root.Status + ": " + (root.Summary ?? "Inspect retained result."), root.Evidence,
                owner.Hypothesis ?? "Execute the current canonical task within its allocation.",
                tree.Any(item => item.ConservativeRecovery) ? "Usage conservatively reconciled after stopped-process verification; the full reserved bound is charged."
                : "Inspect retained result and required evidence before the next bounded action.", owner.NextAction);
            owner.Checkpoints.Add(new(root.Reference.LogicalAttemptId, "CIS measured execution", Time(stopped), checkpoint, owner.Consumed, Reserved(change, owner.Id))
            { Origin = "execution-summary" });
        }
        else
        {
            if (root.Stopped) root.Status = "waiting-for-owned-children";
            if (execution.Stage?.NativeOperation is null)
            {
                var owner = change.Owners[root.Reference.OwnerTaskId];
                owner.Checkpoints.Add(new(reference.LogicalAttemptId, "CIS measured execution", Time(stopped),
                    new(status + ": " + (summary ?? "Inspect retained result."), evidence ?? [], owner.Hypothesis ?? "Execute the current bounded task.",
                        "Owned execution time remains included in the parent reservation until every owned execution safely settles.", owner.NextAction),
                    owner.Consumed, Reserved(change, owner.Id))
                { Origin = "execution-summary" });
            }
        }
        return Result(state, change, reference.TaskId, "settled", true);
    }

    internal CisAgentWorkResult Progress(CisRepositoryContext authority, CisAgentWorkIdentity identity, CisAgentWorkProgressRequest request, string actor,
        string? storyRelationshipId = null, string? storyContractDigest = null)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("An owning work allocation is required before progress can be recorded.");
            CheckRevision(state, request.ExpectedRevision); ValidateIdentity(authority, identity.ChangeId, identity);
            var change = ChangeFor(state, identity.ChangeId); Authorize(change, actor);
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("This task has no accounting owner.");
            var checkpoint = request.Checkpoint ?? throw new InvalidDataException("Supply the bounded progress checkpoint.");
            var story = storyRelationshipId is not null || storyContractDigest is not null;
            if (story)
            {
                Text(storyRelationshipId, "Story reflection relationship"); Text(storyContractDigest, "Story reflection contract");
                if (checkpoint.CloseAttempt || checkpoint.MaterialNewInformation || checkpoint.BlockerKey is not null
                    || checkpoint.Hypothesis is not null || checkpoint.ExpectedResult is not null)
                    throw new InvalidDataException("Automatic story execution summaries cannot claim new material information, change approach authority or close a logical attempt.");
            }
            Text(checkpoint.Outcome, "Outcome"); Text(checkpoint.Approach, "Approach"); Text(checkpoint.Decision, "Decision"); Text(checkpoint.NextAction, "Next action");
            if (checkpoint.Evidence is null || checkpoint.Evidence.Count > 40 || checkpoint.MaterialNewInformation && checkpoint.Evidence.Count == 0)
                throw new InvalidDataException("Material new information requires bounded evidence references.");
            if (checkpoint.CloseAttempt && (owner.OpenAttempt is null || change.Executions.Values.Any(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id)))
                throw new InvalidDataException("Close only an existing logical attempt whose owned executions have safely settled.");
            owner.Checkpoints.Add(new(owner.OpenAttempt ?? owner.Id + ":pending", actor.Trim(), Time(Tick()), checkpoint, MeasuredOwnerMilliseconds(change, owner), Reserved(change, owner.Id))
            {
                Origin = story ? "story-execution-summary" : "controller-progress",
                StoryRelationshipId = storyRelationshipId,
                StoryContractDigest = storyContractDigest
            });
            if (!story)
            {
                owner.Hypothesis = checkpoint.Hypothesis ?? owner.Hypothesis; owner.ExpectedResult = checkpoint.ExpectedResult ?? owner.ExpectedResult;
                owner.NextAction = checkpoint.NextAction;
            }
            if (checkpoint.CloseAttempt)
            {
                owner.RepeatedBlockers = !checkpoint.MaterialNewInformation && checkpoint.BlockerKey is not null
                    ? owner.LastBlocker == checkpoint.BlockerKey ? owner.RepeatedBlockers + 1 : 1 : 0;
                owner.LastBlocker = checkpoint.BlockerKey; owner.OpenAttempt = null;
                if (owner.RepeatedBlockers >= 2 && (owner.Hold is null || owner.HoldKind == "repeated-blocker"))
                {
                    owner.Hold = "Two consecutive logical attempts reported the same blocker without material new information. Reconsider this approach before another unchanged expensive repetition.";
                    owner.HoldKind = "repeated-blocker";
                }
            }
            return (state, Result(state, change, identity.TaskId, "recorded", true));
        });

    internal CisAgentWorkResult Disposition(CisRepositoryContext authority, CisAgentWorkIdentity identity, CisAgentWorkDispositionRequest request, string actor, string reason,
        CisAgentWorkPreparationOwner? verifiedPreparationOwner = null, bool preparationStopped = false,
        IReadOnlyList<CisAgentWorkExecution>? verifiedNativeTree = null)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("An owning work allocation is required before disposition.");
            CheckRevision(state, request.ExpectedRevision); ValidateIdentity(authority, identity.ChangeId, identity);
            var change = ChangeFor(state, identity.ChangeId); Authorize(change, actor); Text(reason, "Disposition rationale");
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("This task has no accounting owner.");
            string? correctionDecision = null;
            if (request.CorrectionRelationshipId is { } correctionRelationship)
            {
                var relation = CorrectionFor(state, correctionRelationship);
                if (relation.OwnerTaskId != owner.Id || relation.Identity?.ChangeId != identity.ChangeId)
                    throw new InvalidDataException("The correction continuation must retain this exact canonical owner and containing change.");
                correctionDecision = GrantCorrection(change, relation, request, actor, reason);
            }
            switch (request.Kind)
            {
                case "simplify":
                    if (owner.Hold is not null && owner.HoldKind != "repeated-blocker")
                        throw new InvalidDataException("A simpler approach does not clear this separate controller hold. Resolve its specific disposition first.");
                    Text(request.Hypothesis, "Justified replacement hypothesis");
                    if (request.Hypothesis == owner.Hypothesis || request.Evidence is null || request.Evidence.Count == 0)
                        throw new InvalidDataException("A simpler in-scope approach requires a changed hypothesis, supporting evidence and controller rationale; rewording alone is insufficient.");
                    owner.Hypothesis = request.Hypothesis; owner.Hold = null; owner.HoldKind = null; owner.RepeatedBlockers = 0;
                    break;
                case "hold": owner.Hold = reason; owner.HoldKind = "controller"; break;
                case "continue":
                    if (owner.HoldKind == "controller" && request.Evidence.Count > 0) { owner.Hold = null; owner.HoldKind = null; }
                    break;
                case "reconcile":
                    if (request.ExecutionId is null || !change.Executions.TryGetValue(request.ExecutionId, out var execution)
                        || execution.Reference.AuthorityRepositoryId != authority.RepositoryId || execution.Reference.ChangeId != identity.ChangeId
                        || execution.Reference.TaskId != identity.TaskId || execution.Reference.OwnerTaskId != owner.Id)
                        throw new InvalidDataException("Reconciliation requires the exact execution ID belonging to this retained task, owner and authority.");
                    if (execution.Finalized && (execution.Stage?.NativeOperation is null
                        || !PendingNativeStorage(change, execution.Reference.ExecutionId)))
                        return (state, Result(state, change, identity.TaskId, "unchanged", false));
                    if (execution.Stage?.NativeOperation is not null)
                        return (state, ReconcileNativeCore(state, change, owner, execution, request, actor, reason, verifiedNativeTree));
                    if (execution.LaunchIntent || execution.ProcessId is not null)
                        return (state, Result(state, change, identity.TaskId, "held", false,
                            "A launch intent or owned process exists. Use cis agent recover with verified stopped-process evidence; no-PID launched work remains uncertain."));
                    if (Tick() - execution.Started < 30_000)
                        return (state, Result(state, change, identity.TaskId, "held", false, "The never-launched execution is inside its 30-second recovery grace period."));
                    if (execution.PreparationOwner is null)
                        return (state, Result(state, change, identity.TaskId, "held", false,
                            "Preparation ownership is unknown in this historical reservation. Preserve its capacity until stopped preparation can be established; an absent owner is not proof of abandonment."));
                    if (!preparationStopped || verifiedPreparationOwner != execution.PreparationOwner)
                        return (state, Result(state, change, identity.TaskId, "held", false,
                            "Preparation is live or its recorded owner has not been proven stopped. Retain the reservation and let the owning invocation finish, or reconcile after its exact owner is verified stopped."));
                    var reconciled = SettleCore(state, change, execution, "Interrupted", true, reason, request.Evidence, true);
                    owner.Checkpoints.Add(new(execution.Reference.LogicalAttemptId, actor.Trim(), Time(Tick()),
                        new("Never-launched reservation reconciled", request.Evidence, owner.Hypothesis ?? "Retain current scope.",
                            "reconcile: " + reason, "Inspect charged usage before another bounded action."), owner.Consumed, Reserved(change, owner.Id))
                    { Origin = "reconciliation" });
                    return (state, reconciled);
                default: throw new InvalidDataException("Disposition must be continue, simplify, hold or reconcile.");
            }
            owner.NextAction = request.NextAction ?? owner.NextAction;
            owner.Checkpoints.Add(new(owner.OpenAttempt ?? owner.Id + ":pending", actor.Trim(), Time(Tick()),
                new("Controller disposition", request.Evidence, owner.Hypothesis ?? "Retain current scope.",
                    request.Kind + (correctionDecision is null ? "" : " [continuation:" + correctionDecision + "]") + ": " + reason,
                    owner.NextAction), owner.Consumed, Reserved(change, owner.Id))
            { Origin = "controller-disposition" });
            return (state, Result(state, change, identity.TaskId, "disposed", true));
        });

    internal bool HasGovernedState(CisRepositoryContext authority)
    {
        var ledger = SafePath(authority, RelativePath); var marker = SafePath(authority, AdoptionPath);
        if (!File.Exists(ledger) && !File.Exists(marker)) return false;
        try
        {
            using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
            return Load(authority)?.CorrectionOnly != true;
        }
        catch (Exception error) when (Recoverable(error)) { return true; }
    }

    internal bool HasCorrectionHistory(CisRepositoryContext authority)
    {
        if (!File.Exists(SafePath(authority, RelativePath)) && !File.Exists(SafePath(authority, AdoptionPath))) return false;
        using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
        return Load(authority)?.Corrections.Values.Any(relation => relation.Rounds.Count > 0) == true;
    }

    internal CisAgentWorkResult ReadCorrections(CisRepositoryContext authority, string relationshipId, string? contractDigest = null,
        string? actor = null, CisAgentWorkIdentity? expectedIdentity = null, bool inspectIdentity = false)
    {
        try
        {
            if (!File.Exists(SafePath(authority, RelativePath)) && !File.Exists(SafePath(authority, AdoptionPath)))
                return new("status", null, [], false);
            using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
            var state = Load(authority);
            if (state is null) return new("status", null, [], false);
            var relation = state.Corrections.GetValueOrDefault(relationshipId);
            var identityHold = inspectIdentity && state.CorrectionOnly && relation is { OwnerTaskId: null, Identity: not null }
                && !SameCorrectionIdentity(relation.Identity, expectedIdentity) ? CorrectionIdentityAction(relation, expectedIdentity) : null;
            return CorrectionResult(state, relation, "status", false, identityHold, contractDigest, actor);
        }
        catch (Exception error) when (Recoverable(error)) { return Failure(error); }
    }

    internal AgentWorkCorrectionAdmission BeginCorrection(CisRepositoryContext authority, CisAgentWorkIdentity? identity,
        CisAgentWorkReference? rootReference, string relationshipId, string actor, IReadOnlyList<string> findingReferences,
        string? contractDigest = null, string? materialBasis = null, IReadOnlyList<CisStoryReviewFinding>? findingDetails = null,
        IReadOnlyList<CisAgentWorkCandidateTree>? candidateTrees = null)
    {
        CisAgentWorkCorrectionRound? selected = null;
        var result = Mutate(authority, state =>
        {
            Text(relationshipId, "Stable story relationship"); Text(actor, "Story controller");
            var findings = FindingReferences(findingReferences);
            var context = CorrectionContext(contractDigest, materialBasis, findingDetails, candidateTrees);
            if (findings.Count == 0) throw new InvalidDataException("A correction must address identified required findings; optional advice does not reserve a round.");
            state ??= new() { AuthorityId = authority.RepositoryId, AdoptionId = Guid.NewGuid().ToString("N"), BaselineStarted = Tick(), CorrectionOnly = true };
            var owner = CorrectionOwner(state, authority, identity, rootReference, actor);
            if (!state.Corrections.TryGetValue(relationshipId, out var relation))
            {
                relation = new() { Id = relationshipId, Identity = identity, OwnerTaskId = owner, Actor = actor.Trim() };
                state.Corrections.Add(relationshipId, relation);
            }
            var active = relation.Rounds.LastOrDefault(round => round.Status == "started");
            var attachIdentity = state.CorrectionOnly && owner is null && relation.OwnerTaskId is null
                && relation.Identity is null && identity is not null;
            if (attachIdentity) ValidateIdentity(authority, identity!.ChangeId, identity);
            var correctionOnlyBinding = state.CorrectionOnly && owner is null && relation.OwnerTaskId is null
                && (attachIdentity || SameCorrectionIdentity(relation.Identity, identity));
            if (state.CorrectionOnly && owner is null && relation.OwnerTaskId is null && !correctionOnlyBinding)
                return (state, CorrectionResult(state, relation, "held", false, CorrectionIdentityAction(relation, identity)));
            if (active is not null && correctionOnlyBinding && relation.Actor != actor.Trim())
                return (state, CorrectionResult(state, relation, "held", false, CorrectionActorAction(relation, null)));
            if (attachIdentity) relation.Identity = identity;
            if (!correctionOnlyBinding || active is not null) CheckCorrectionOwner(relation, identity, owner, actor);
            if (active is not null)
            {
                var directions = CorrectionDirections(state, relation, active, out var directionHold);
                if (directionHold is not null)
                    return (state, CorrectionResult(state, relation, "held", false, directionHold));
                var recoveredDirection = directions is not null
                    && (active.AuthorizedDirections is null || !active.AuthorizedDirections.SequenceEqual(directions));
                if (recoveredDirection)
                {
                    active = active with { AuthorizedDirections = directions };
                    relation.Rounds[relation.Rounds.FindIndex(round => round.Id == active.Id)] = active;
                }
                if (context is not null && active.Context is not null && context.ContractDigest != active.Context.ContractDigest)
                    throw new InvalidDataException("Observe the newly approved task contract before superseding an older correction; its round number and cost remain retained.");
                var changedRoot = rootReference is not null && relation.ActiveRootExecutionId != rootReference.ExecutionId;
                if (changedRoot && relation.ActiveRootExecutionId is { } previousRoot
                    && identity is not null && ChangeFor(state, identity.ChangeId).Executions.TryGetValue(previousRoot, out var prior)
                    && !prior.Finalized)
                    return (state, CorrectionResult(state, relation, "held", false,
                        "The correction round is still claimed by an unresolved owned root. Reconcile its exact execution before continuing the same round."));
                if (changedRoot) relation.ActiveRootExecutionId = rootReference!.ExecutionId;
                selected = active;
                return (state, CorrectionResult(state, relation, "correction-resumed", changedRoot || attachIdentity || recoveredDirection));
            }
            relation.RequiredFindings = findings.ToList();
            if (context is not null)
            {
                relation.RequiredContext = context;
                relation.ObservedContractDigest = context.ContractDigest;
            }
            var grant = relation.Grant;
            var grantedDirection = grant is null ? null : CorrectionDirection(state, relation, grant.Id, grant.AuthorizedDirection);
            if (grant is not null && grantedDirection is null)
                return (state, CorrectionResult(state, relation, "held", false, MissingCorrectionDirection(relation, grant.Id)));
            if (grant?.Context is { } grantedContext && context is not null
                && (grantedContext.ContractDigest != context.ContractDigest || grantedContext.MaterialBasis != context.MaterialBasis
                    || !grantedContext.Candidates.SequenceEqual(context.Candidates)))
                throw new InvalidDataException("The bounded continuation belongs to another retained candidate or task contract. Reconcile its exact current material context before work.");
            if (relation.Rounds.Count >= 2 && (grant is null || !grant.Findings.SequenceEqual(findings, StringComparer.Ordinal)))
                return (state, CorrectionResult(state, relation, "held", true, CorrectionLimit));
            selected = new(Guid.NewGuid().ToString("N"), relationshipId, relation.Rounds.Count + 1, "started", grant?.RecordedAtUtc ?? Time(Tick()), findings, [])
            {
                DispositionReference = grant?.Id,
                MaximumSeconds = grant?.MaximumSeconds,
                Context = grant?.Context ?? context,
                AuthorizedDirections = grantedDirection is null ? null : [grantedDirection]
            };
            relation.Rounds.Add(selected); relation.Grant = null;
            if (correctionOnlyBinding) relation.Actor = actor.Trim();
            relation.ActiveRootExecutionId = rootReference?.ExecutionId;
            return (state, CorrectionResult(state, relation, "correction-started", true));
        });
        return new(result, result.ExitCode == 0 ? selected : null);
    }

    internal CisAgentWorkResult ObserveContract(CisRepositoryContext authority, CisAgentWorkIdentity? identity,
        CisAgentWorkReference? rootReference, string relationshipId, string actor, string contractDigest)
    {
        Text(contractDigest, "Approved story contract digest");
        if (!File.Exists(SafePath(authority, RelativePath)) && !File.Exists(SafePath(authority, AdoptionPath)))
            return new("unchanged", null, [], false);
        return Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("Restore the retained story history before observing a new contract.");
            if (!state.Corrections.TryGetValue(relationshipId, out var relation))
                return (state, CorrectionResult(state, null, "unchanged", false));
            var correctionOnlyObservation = state.CorrectionOnly && rootReference is null && relation.OwnerTaskId is null
                && (relation.Identity is null || SameCorrectionIdentity(relation.Identity, identity));
            if (state.CorrectionOnly && rootReference is null && relation.OwnerTaskId is null && !correctionOnlyObservation)
                return (state, CorrectionResult(state, relation, "held", false, CorrectionIdentityAction(relation, identity)));
            if (!correctionOnlyObservation)
            {
                var owner = CorrectionOwner(state, authority, identity, rootReference, actor);
                CheckCorrectionOwner(relation, identity, owner, actor);
            }
            var attachIdentity = correctionOnlyObservation && relation.Identity is null && identity is not null;
            if (attachIdentity) { ValidateIdentity(authority, identity!.ChangeId, identity); relation.Identity = identity; }
            var applied = relation.ObservedContractDigest != contractDigest || attachIdentity;
            for (var index = 0; index < relation.Rounds.Count; index++)
            {
                var round = relation.Rounds[index];
                if (round.Status == "started" && round.Context is { } prior && prior.ContractDigest != contractDigest)
                {
                    relation.Rounds[index] = round with { Status = "superseded", CompletedAtUtc = Time(Tick()) };
                    relation.ActiveRootExecutionId = null; applied = true;
                }
            }
            if (relation.RequiredContext is { } required && required.ContractDigest != contractDigest)
            { relation.RequiredContext = null; relation.RequiredFindings = []; applied = true; }
            if (relation.Grant?.Context is { } granted && granted.ContractDigest != contractDigest)
            { relation.Grant = null; applied = true; }
            relation.ObservedContractDigest = contractDigest;
            return (state, CorrectionResult(state, relation, applied ? "contract-observed" : "unchanged", applied, contractDigest: contractDigest));
        });
    }

    private static CisAgentWorkCorrectionContext? CorrectionContext(string? contractDigest, string? materialBasis,
        IReadOnlyList<CisStoryReviewFinding>? findings, IReadOnlyList<CisAgentWorkCandidateTree>? candidates)
    {
        if (contractDigest is null && materialBasis is null && findings is null && candidates is null) return null;
        Text(contractDigest, "Required-finding task contract"); Text(materialBasis, "Required-finding material basis");
        if (findings is null || candidates is null || findings.Count > 100 || candidates.Count > 100
            || findings.Any(finding => finding is null || finding.Disposition != "required")
            || candidates.Any(candidate => candidate is null || !BoundedText(candidate.RepositoryId)
                || !BoundedText(candidate.BaselineTree) || !BoundedText(candidate.CandidateTree)))
            throw new InvalidDataException("Retain a bounded structured required-finding and exact candidate-tree context.");
        var context = new CisAgentWorkCorrectionContext(contractDigest!, materialBasis!, findings,
            candidates.OrderBy(candidate => candidate.RepositoryId, StringComparer.Ordinal).ToArray());
        if (JsonSerializer.Serialize(context, Json).Length > 256 * 1024)
            throw new InvalidDataException("The current correction context exceeds its bounded retained projection.");
        return context;
    }

    internal CisAgentWorkResult RecordCorrectionStage(CisRepositoryContext authority, CisAgentWorkIdentity? identity,
        CisAgentWorkReference? rootReference, string relationshipId, string roundId, string actor,
        string repositoryId, string runId, string candidate, IReadOnlyList<string> evidence)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("The reserved correction round is missing; restore its retained history.");
            var owner = CorrectionOwner(state, authority, identity, rootReference, actor);
            var relation = CorrectionFor(state, relationshipId); CheckCorrectionOwner(relation, identity, owner, actor);
            Text(repositoryId, "Correction repository"); Text(runId, "Correction run"); Text(candidate, "Correction candidate");
            if (evidence is null || evidence.Count > 40 || evidence.Any(item => !BoundedText(item)))
                throw new InvalidDataException("Retain bounded actual correction evidence references.");
            var at = relation.Rounds.FindIndex(round => round.Id == roundId && round.Status == "started");
            if (at < 0) throw new InvalidDataException("Record a repository stage only in its active reserved correction round.");
            var round = relation.Rounds[at];
            if (round.Stages.SingleOrDefault(stage => stage.RepositoryId == repositoryId) is { } existing)
            {
                if (existing.RunId != runId || existing.Candidate != candidate)
                    throw new InvalidDataException("The completed correction stage is already retained; reuse its candidate rather than replacing or charging it again.");
                return (state, CorrectionResult(state, relation, "unchanged", false));
            }
            relation.Rounds[at] = round with { Stages = round.Stages.Append(new(repositoryId, runId, candidate, Time(Tick()), evidence)).ToArray() };
            return (state, CorrectionResult(state, relation, "correction-stage-recorded", true));
        });

    internal CisAgentWorkResult CompleteCorrection(CisRepositoryContext authority, CisAgentWorkIdentity? identity,
        CisAgentWorkReference? rootReference, string relationshipId, string roundId, string actor)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("The retained correction round is missing.");
            var owner = CorrectionOwner(state, authority, identity, rootReference, actor);
            var relation = CorrectionFor(state, relationshipId); CheckCorrectionOwner(relation, identity, owner, actor);
            var at = relation.Rounds.FindIndex(round => round.Id == roundId);
            if (at < 0) throw new InvalidDataException("Complete only the exact retained correction round after current review.");
            if (relation.Rounds[at].Status == "completed") return (state, CorrectionResult(state, relation, "unchanged", false));
            relation.Rounds[at] = relation.Rounds[at] with { Status = "completed", CompletedAtUtc = Time(Tick()) };
            relation.RequiredFindings = []; relation.RequiredContext = null; relation.ActiveRootExecutionId = null;
            return (state, CorrectionResult(state, relation, "correction-reviewed", true));
        });

    internal CisAgentWorkResult DispositionCorrection(CisRepositoryContext authority, string relationshipId,
        CisAgentWorkDispositionRequest request, string actor, string reason)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("Inspect retained story correction history before recording its continuation.");
            CheckRevision(state, request.ExpectedRevision); Text(reason, "Continuation rationale");
            var relation = CorrectionFor(state, relationshipId);
            throw new InvalidDataException("A post-limit correction needs an enforceable bounded allowance. Bind this story to its approved canonical change/task, establish the authorized allocation with its historical-effort baseline, then record the specific simplify disposition; correction-only history is not an unlimited grant.");
        });

    internal bool HistoricalReconciled(CisRepositoryContext authority, CisAgentWorkIdentity identity,
        string relationshipId, IReadOnlyList<string> runIds)
    {
        if (runIds.Count == 0) return true;
        try
        {
            using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
            var state = Load(authority);
            if (state is null || !state.Changes.TryGetValue(identity.ChangeId, out var change)) return false;
            var owner = FindOwner(change, identity);
            return owner is not null && runIds.All(id => change.HistoricalRuns.TryGetValue(id, out var charge)
                && charge.RelationshipId == relationshipId && charge.OwnerTaskId == owner.Id);
        }
        catch (Exception error) when (Recoverable(error)) { return false; }
    }

    internal CisAgentWorkResult ReconcileHistory(CisRepositoryContext authority, CisAgentWorkIdentity identity,
        string relationshipId, IReadOnlyDictionary<string, long> runBounds, string expectedRevision, string actor, string reason)
        => Mutate(authority, state =>
        {
            if (state is null || state.CorrectionOnly) throw new InvalidDataException("Establish the authorized owning allocation and historical-effort acknowledgment before reconciling story usage.");
            CheckRevision(state, expectedRevision); ValidateIdentity(authority, identity.ChangeId, identity);
            var change = ChangeFor(state, identity.ChangeId); Authorize(change, actor); Text(reason, "Historical reconciliation rationale");
            Text(relationshipId, "Stable story relationship");
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("The selected canonical task has no authorized accounting owner.");
            if (runBounds.Count is < 1 or > 100 || runBounds.Any(pair => !BoundedText(pair.Key) || pair.Value is <= 0 or > 31_536_000_000))
                throw new InvalidDataException("Reconcile only bounded service-verified retained story run references.");
            var applied = false;
            foreach (var (runId, milliseconds) in runBounds)
            {
                var existing = state.Changes.Values.SelectMany(item => item.HistoricalRuns.Where(pair => pair.Key == runId)).ToArray();
                if (existing.Length > 0)
                {
                    if (existing.Length != 1 || !change.HistoricalRuns.TryGetValue(runId, out var charged)
                        || charged.OwnerTaskId != owner.Id || charged.RelationshipId != relationshipId)
                        throw new InvalidDataException("A retained historical run already belongs to another canonical work relationship; its charge cannot be transferred or reset.");
                    continue;
                }
                change.HistoricalRuns.Add(runId, new(relationshipId, owner.Id, milliseconds, actor.Trim(), reason.Trim(), Time(Tick())));
                owner.Consumed = checked(owner.Consumed + milliseconds); change.Consumed = checked(change.Consumed + milliseconds); applied = true;
            }
            if (state.Corrections.TryGetValue(relationshipId, out var relation))
            {
                if (relation.OwnerTaskId is not null && (relation.OwnerTaskId != owner.Id || relation.Identity?.TaskId != identity.TaskId))
                    throw new InvalidDataException("The correction relationship already has a different retained work owner.");
                applied |= relation.OwnerTaskId is null;
                relation.Identity = identity; relation.OwnerTaskId = owner.Id;
            }
            return (state, Result(state, change, identity.TaskId, applied ? "history-reconciled" : "unchanged", applied));
        });

    private string GrantCorrection(Change change, CorrectionRelationship relation, CisAgentWorkDispositionRequest request, string actor, string reason)
    {
        if (request.Kind != "simplify" || request.CorrectionRelationshipId != relation.Id)
            throw new InvalidDataException("A post-limit correction needs a specific simplify disposition for its retained required context.");
        Text(request.Hypothesis, "Justified simpler approach"); Text(request.NextAction, "Specific bounded next correction action");
        if (request.Hypothesis == relation.Hypothesis || request.Evidence is not { Count: > 0 and <= 40 }
            || request.Evidence.Any(item => !BoundedText(item)) || request.CorrectionMaximumSeconds is not (>= 30 and <= 3600))
            throw new InvalidDataException("Supply a changed in-scope hypothesis, actual evidence and one correction bound between 30 and 3600 seconds; a generic continuation does not refill rounds.");
        var findings = FindingReferences(request.CorrectionFindingReferences);
        if (findings.Count == 0 || !findings.SequenceEqual(relation.RequiredFindings, StringComparer.Ordinal))
            throw new InvalidDataException("Bind the continuation to the exact current required-finding references shown by work status.");
        if (relation.Rounds.LastOrDefault(round => round.Status == "started") is { } active)
        {
            if (active.DispositionReference is null || active.MaximumSeconds is not { } currentMaximum
                || !findings.SequenceEqual(active.FindingReferences, StringComparer.Ordinal)
                || JsonSerializer.Serialize(active.Context, Json) != JsonSerializer.Serialize(relation.RequiredContext, Json))
                throw new InvalidDataException("Extend only an already granted active round with its same exact required findings and material context.");
            var roots = change.Executions.Values.Where(execution => execution.Stage?.CorrectionRoundId == active.Id
                    || active.ExecutionIds.Contains(execution.Reference.ExecutionId, StringComparer.Ordinal)
                    || execution.Stage is { Route: "story-task", Name: "orchestration" } stage && stage.RelationshipId == relation.Id)
                .Select(execution => execution.RootId).ToHashSet(StringComparer.Ordinal);
            if (relation.ActiveRootExecutionId is not { } activeRoot || !change.Executions.ContainsKey(activeRoot)
                || active.ExecutionIds.Any(id => !change.Executions.ContainsKey(id))
                || change.Executions.Values.Any(execution => (roots.Contains(execution.RootId) || execution.RootId == activeRoot)
                    && (!execution.Finalized || execution.RecoveryReason is not null)))
                throw new InvalidDataException("All owned root and child executions for this granted round must be safely finalized before another bounded step; live or uncertain ownership retains its hold.");
            var measured = CorrectionMeasured(change, active);
            var remaining = checked((long)currentMaximum * 1000) - measured;
            var priorDirections = CorrectionDirections(change, relation, active, out var directionHold);
            if (remaining >= 30_000 && directionHold is null)
                throw new InvalidDataException("The current granted round still has a usable bounded step; use its remaining capacity before extending it.");
            if (remaining >= 30_000 && checked((long)request.CorrectionMaximumSeconds.Value * 1000) > remaining)
                throw new InvalidDataException("Reconcile missing correction direction with a bounded simplify action that fits the existing remaining seconds; reconciliation does not replenish its cap.");
            // Match extension rounding for the cumulative cap. The next root floors
            // its remainder, so fractional spent work cannot erase a valid 30-second step.
            var nextStepMaximum = checked((measured + 999) / 1000 + request.CorrectionMaximumSeconds.Value);
            var totalSeconds = remaining >= 30_000
                ? Math.Min(currentMaximum, nextStepMaximum)
                : Math.Max(currentMaximum, nextStepMaximum);
            if (totalSeconds > 31_536_000)
                throw new InvalidDataException("The cumulative correction bound must remain finite and within the existing one-year accounting limit.");
            var decision = Guid.NewGuid().ToString("N");
            var direction = new CisAgentWorkCorrectionDirection(decision, request.Kind, request.Hypothesis!, request.NextAction!,
                actor.Trim(), Time(Tick()), reason, request.Evidence);
            var unresolved = new[] { active.DispositionReference! }.Concat(active.ContinuationReferences ?? [])
                .Where(reference => priorDirections?.Any(prior => prior.DispositionReference == reference) != true).ToArray();
            var at = relation.Rounds.FindIndex(round => round.Id == active.Id);
            relation.Rounds[at] = active with
            {
                MaximumSeconds = checked((int)totalSeconds),
                ContinuationReferences = (active.ContinuationReferences ?? []).Append(decision).ToArray(),
                AuthorizedDirections = (priorDirections ?? []).Append(direction).ToArray(),
                UnresolvedDirectionReferences = unresolved.Length == 0 ? null : unresolved
            };
            relation.Hypothesis = request.Hypothesis;
            return decision;
        }
        relation.Hypothesis = request.Hypothesis;
        var grantId = Guid.NewGuid().ToString("N");
        var recordedAt = Time(Tick());
        relation.Grant = new(grantId, findings, request.CorrectionMaximumSeconds.Value,
            request.NextAction!, actor.Trim(), recordedAt, relation.RequiredContext)
        {
            AuthorizedDirection = new(grantId, request.Kind, request.Hypothesis!, request.NextAction!,
                actor.Trim(), recordedAt, reason, request.Evidence)
        };
        return relation.Grant.Id;
    }

    private static CisAgentWorkCorrectionDirection? CorrectionDirection(Ledger state, CorrectionRelationship relation,
        string reference, CisAgentWorkCorrectionDirection? retained)
        => CorrectionDirection(relation.Identity is { } identity ? state.Changes.GetValueOrDefault(identity.ChangeId) : null,
            relation, reference, retained);

    private static CisAgentWorkCorrectionDirection? CorrectionDirection(Change? change, CorrectionRelationship relation,
        string reference, CisAgentWorkCorrectionDirection? retained)
    {
        if (retained is not null && retained.DispositionReference == reference && ValidCorrectionDirection(retained)) return retained;
        // A pre-fix grant copied neither the hypothesis nor the reason. Recover only
        // its exact authorized disposition, never the owner's newer hypothesis or a review.
        var prefix = "simplify [continuation:" + reference + "]: ";
        var matches = relation.OwnerTaskId is { } ownerId && change?.Owners.GetValueOrDefault(ownerId) is { } owner
            ? owner.Checkpoints.Where(checkpoint => checkpoint.Origin == "controller-disposition"
                && checkpoint.Checkpoint.Decision.StartsWith(prefix, StringComparison.Ordinal)).ToArray() : [];
        if (matches.Length != 1) return null;
        var record = matches[0];
        var recovered = new CisAgentWorkCorrectionDirection(reference, "simplify", record.Checkpoint.Approach,
            record.Checkpoint.NextAction, record.Actor, record.RecordedAtUtc, record.Checkpoint.Decision[prefix.Length..],
            record.Checkpoint.Evidence);
        return ValidCorrectionDirection(recovered) ? recovered : null;
    }

    private static bool ValidCorrectionDirection(CisAgentWorkCorrectionDirection direction)
        => BoundedText(direction.DispositionReference) && direction.Kind == "simplify" && BoundedText(direction.Hypothesis)
            && BoundedText(direction.NextAction) && BoundedText(direction.Actor) && BoundedText(direction.Reason)
            && DateTimeOffset.TryParse(direction.RecordedAtUtc, out _)
            && direction.Evidence is { Count: > 0 and <= 40 } && direction.Evidence.All(BoundedText);

    private static IReadOnlyList<CisAgentWorkCorrectionDirection>? CorrectionDirections(Ledger state,
        CorrectionRelationship relation, CisAgentWorkCorrectionRound round, out string? hold)
        => CorrectionDirections(relation.Identity is { } identity ? state.Changes.GetValueOrDefault(identity.ChangeId) : null,
            relation, round, out hold);

    private static IReadOnlyList<CisAgentWorkCorrectionDirection>? CorrectionDirections(Change? change,
        CorrectionRelationship relation, CisAgentWorkCorrectionRound round, out string? hold)
    {
        hold = null;
        if (round.DispositionReference is null) return null; // Ordinary automatic rounds require no disposition.
        var directions = new List<CisAgentWorkCorrectionDirection>();
        var references = new[] { round.DispositionReference! }.Concat(round.ContinuationReferences ?? []).ToArray();
        foreach (var reference in references)
        {
            var retained = round.AuthorizedDirections?.Where(direction => direction.DispositionReference == reference).ToArray();
            if (retained is { Length: > 1 }) { hold ??= MissingCorrectionDirection(relation, reference); continue; }
            var direction = CorrectionDirection(change, relation, reference, retained?.SingleOrDefault());
            if (direction is null)
            {
                // A later explicit simplify decision can authorize future work while
                // preserving an irrecoverable earlier disposition as unknown history.
                if (reference == references[^1] || round.UnresolvedDirectionReferences?.Contains(reference, StringComparer.Ordinal) != true)
                    hold ??= MissingCorrectionDirection(relation, reference);
                continue;
            }
            directions.Add(direction);
        }
        return directions;
    }

    private static string MissingCorrectionDirection(CorrectionRelationship relation, string reference)
        => "The authorized hypothesis, next action and disposition provenance are missing for correction disposition '"
            + reference + "' in correctionRelationshipId '" + relation.Id
            + "'. Restore verified retained controller-disposition evidence if available. Otherwise use cis agent work disposition "
            + "with kind=simplify, the current revision, exact correctionRelationshipId and required findings, changed hypothesis, "
            + "specific nextAction, evidence and correctionMaximumSeconds. A safely finalized started round may reconcile future direction "
            + "within its existing usable remaining cap, or receive an eligible bounded extension when exhausted; its missing prior direction "
            + "stays explicitly unknown. Preserve prior rounds, stages and spent work. "
            + "A review, provider response or generic continuation cannot establish this authority.";

    private static IReadOnlyList<string> FindingReferences(IReadOnlyList<string> references)
    {
        if (references is null || references.Count > 100 || references.Any(item => !BoundedText(item)))
            throw new InvalidDataException("Required findings need a bounded material reference set.");
        return references.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static CorrectionRelationship CorrectionFor(Ledger state, string id)
        => state.Corrections.GetValueOrDefault(id) ?? throw new InvalidDataException("The stable story correction relationship is not retained.");

    private static string? CorrectionOwner(Ledger state, CisRepositoryContext authority, CisAgentWorkIdentity? identity,
        CisAgentWorkReference? rootReference, string actor)
    {
        if (rootReference is null)
        {
            if (!state.CorrectionOnly) throw new InvalidDataException("Adopted story work needs its reserved canonical root before correction work.");
            return null;
        }
        if (identity is null) throw new InvalidDataException("The story correction lost its canonical accounting identity.");
        ValidateIdentity(authority, identity.ChangeId, identity);
        var change = ChangeFor(state, identity.ChangeId); Authorize(change, actor);
        if (!change.Executions.TryGetValue(rootReference.ExecutionId, out var root) || root.Reference != rootReference
            || root.Stopped || root.Reference.ParentExecutionId is not null || rootReference.TaskId != identity.TaskId)
            throw new InvalidDataException("Story correction requires its exact live inclusive root reservation.");
        return rootReference.OwnerTaskId;
    }

    private static void CheckCorrectionOwner(CorrectionRelationship relation, CisAgentWorkIdentity? identity, string? owner, string actor)
    {
        if (relation.OwnerTaskId != owner || relation.Identity?.ChangeId != identity?.ChangeId
            || relation.Identity?.TaskId != identity?.TaskId || owner is null && relation.Actor != actor.Trim())
            throw new InvalidDataException("A story's correction history cannot acquire another owner, task or controller through a new invocation.");
    }

    private const string CorrectionLimit = "Two automatic correction rounds are retained. Inspect the required findings and record a relevant simplify disposition with this relationship, evidence and one bounded next action through cis agent work disposition; prior rounds and spent work remain binding.";

    private static bool SameCorrectionIdentity(CisAgentWorkIdentity? left, CisAgentWorkIdentity? right)
        => left?.AuthorityRepositoryId == right?.AuthorityRepositoryId && left?.ChangeId == right?.ChangeId && left?.TaskId == right?.TaskId;

    private static string CorrectionIdentityAction(CorrectionRelationship relation, CisAgentWorkIdentity? requested)
        => "The correction history for correctionRelationshipId '" + relation.Id + "' retains canonical work '"
            + relation.Identity?.ChangeId + "/" + relation.Identity?.TaskId + "', while this request identifies '"
            + (requested is null ? "unbound work" : requested.ChangeId + "/" + requested.TaskId)
            + "'. Restore the matching approved story binding or resolve its ownership through existing approved change/task preparation. Rebinding cannot replace retained ownership or reset correction rounds; spent or unmeasured historical effort remains retained.";

    private static string? CorrectionActorAction(CorrectionRelationship relation, string? contractDigest)
        => relation.Rounds.LastOrDefault(round => round.Status == "started"
            && (contractDigest is null || round.Context is null || round.Context.ContractDigest == contractDigest)) is { } active
            ? "The non-adopted correction round " + active.Number + " for correctionRelationshipId '" + relation.Id
                + "' is retained by original controller '" + relation.Actor + "'. Have that original controller resume through the governed story route, or bind the approved canonical work and explicitly allocate authority-wide finite accounting, then use the reconcile-story-history disposition for retained native work. All prior rounds and spent work remain retained."
            : null;

    private (long Remaining, string Action)? StoryCorrectionCapacity(Ledger state, Change change, string ownerId, CisAgentWorkStage? stage)
    {
        if (stage is not { Route: "story-task", ContractDigest: { } digest }
            || !state.Corrections.TryGetValue(stage.RelationshipId, out var relation)
            || relation.OwnerTaskId != ownerId || relation.Identity?.ChangeId != change.Id) return null;
        if (relation.Grant is { Context: { } context } grant && context.ContractDigest == digest)
            return (Math.Max(0, checked((long)grant.MaximumSeconds * 1000)
                    - StoryPreparationMeasured(change, relation.Id, digest, grant.RecordedAtUtc)),
                "The bounded correction grant for correctionRelationshipId '" + relation.Id
                    + "' has fewer than 30 usable seconds. Record an explicit replacement simplify disposition through cis agent work disposition with this relationship, current required finding references, a changed justified hypothesis, evidence and one bounded next action within remaining task/change allowance. Retry and reallocation do not replenish its cap or spent work.");
        if (relation.Rounds.LastOrDefault(round => round.Status == "started"
            && (round.Context is null || round.Context.ContractDigest == digest)
            && (stage.CorrectionRoundId is null || stage.CorrectionRoundId == round.Id)) is { MaximumSeconds: { } seconds } active)
            return (Math.Max(0, checked((long)seconds * 1000) - CorrectionMeasured(change, active)),
                "The started correction round " + active.Number + " for correctionRelationshipId '" + relation.Id
                    + "' has fewer than 30 usable seconds. Use the existing same-round simplify extension through cis agent work disposition after all associated owned work is safely finalized, with current required finding references, a changed justified hypothesis, evidence and a 30–3600-second additional step. Retain this round, consumed work and hard task/change allowances.");
        return null;
    }

    private long StoryPreparationMeasured(Change change, string relationshipId, string contractDigest, string since)
    {
        if (!DateTimeOffset.TryParse(since, out var recorded))
            throw new InvalidDataException("The bounded continuation has unresolved timing evidence; retain its allowance until reconciliation.");
        var start = recorded.ToUnixTimeMilliseconds();
        var end = Tick();
        return UnionMilliseconds(change.Executions.Values.Where(execution => execution.Stage is
        { Route: "story-task", Name: "orchestration" } stage && stage.RelationshipId == relationshipId
                && stage.ContractDigest == contractDigest)
            .SelectMany(execution => execution.Intervals)
            .Select(interval => (Start: Math.Max(start, interval.Start), End: Math.Min(end, interval.End ?? end)))
            .Where(interval => interval.End > interval.Start));
    }

    private long CorrectionMeasured(Change change, CisAgentWorkCorrectionRound round)
    {
        if (!DateTimeOffset.TryParse(round.StartedAtUtc, out var started)
            || round.CompletedAtUtc is { } completed && !DateTimeOffset.TryParse(completed, out _))
            throw new InvalidDataException("The correction round has unresolved timing evidence; retain its bound until reconciliation.");
        var start = started.ToUnixTimeMilliseconds();
        var end = round.CompletedAtUtc is { } completedAt ? DateTimeOffset.Parse(completedAt).ToUnixTimeMilliseconds() : Tick();
        return UnionMilliseconds(change.Executions.Values.Where(execution => execution.Stage is { } stage
                && (stage.CorrectionRoundId == round.Id || stage.Route == "story-task"
                    && stage.RelationshipId == round.RelationshipId && stage.Name == "orchestration"))
            .SelectMany(execution => execution.Intervals)
            .Select(interval => (Start: Math.Max(start, interval.Start), End: Math.Min(end, interval.End ?? end)))
            .Where(interval => interval.End > interval.Start));
    }

    private CisAgentWorkResult CorrectionResult(Ledger state, CorrectionRelationship? relation, string status, bool applied,
        string? hold = null, string? contractDigest = null, string? actor = null)
    {
        var currentContext = relation?.RequiredContext;
        var contextCurrent = contractDigest is null || currentContext is null || currentContext.ContractDigest == contractDigest;
        var grant = relation?.Grant;
        if (contractDigest is not null && grant?.Context is { } granted && granted.ContractDigest != contractDigest) grant = null;
        var grantedDirection = grant is null || relation is null ? null : CorrectionDirection(state, relation, grant.Id, grant.AuthorizedDirection);
        var rounds = relation?.Rounds.ToArray() ?? [];
        if (relation is not null)
        {
            if (grant is not null && grantedDirection is null) hold ??= MissingCorrectionDirection(relation, grant.Id);
            for (var index = 0; index < rounds.Length; index++)
            {
                if (rounds[index].Status != "started"
                    || contractDigest is not null && rounds[index].Context is { } roundContext && roundContext.ContractDigest != contractDigest) continue;
                var directions = CorrectionDirections(state, relation, rounds[index], out var directionHold);
                hold ??= directionHold;
                if (directions is not null) rounds[index] = rounds[index] with { AuthorizedDirections = directions };
            }
        }
        var actorAction = state.CorrectionOnly && relation is not null ? CorrectionActorAction(relation, contractDigest) : null;
        if (actorAction is not null && !string.IsNullOrWhiteSpace(actor) && relation!.Actor != actor.Trim()) hold ??= actorAction;
        hold ??= contextCurrent && relation is { RequiredFindings.Count: > 0, Grant: null } && relation.Rounds.Count >= 2
            && !relation.Rounds.Any(round => round.Status == "started") ? CorrectionLimit : null;
        if (hold is not null && status == "status") status = "held";
        if (relation?.Identity is { } identity && state.Changes.TryGetValue(identity.ChangeId, out var change))
        {
            var digest = contractDigest ?? grant?.Context?.ContractDigest ?? currentContext?.ContractDigest
                ?? relation.Rounds.LastOrDefault(round => round.Status == "started")?.Context?.ContractDigest;
            var stage = new CisAgentWorkStage("story-task", relation.Id, "orchestration", state.AuthorityId) { ContractDigest = digest };
            var capacity = relation.OwnerTaskId is { } ownerId ? StoryCorrectionCapacity(state, change, ownerId, stage) : null;
            var result = Result(state, change, identity.TaskId, status, applied, hold, stage);
            if (status == "status" && capacity is { Remaining: < 30_000 } exhausted && result.Work?.HoldReason == exhausted.Action)
                result = result with { Status = "held" };
            return result with
            {
                Work = result.Work! with
                {
                    Corrections = rounds,
                    CorrectionRelationshipId = relation.Id,
                    RequiredCorrectionFindingReferences = contextCurrent ? relation.RequiredFindings : [],
                    RequiredCorrectionContext = contextCurrent ? currentContext : null,
                    CorrectionContinuationAvailable = grant is not null && grantedDirection is not null && capacity is not { Remaining: < 30_000 },
                    CorrectionContinuation = grant is null ? null : new(grant.Id, relation.Id, grant.MaximumSeconds, grant.NextAction,
                        grant.Actor, grant.RecordedAtUtc, grant.Context)
                    { AuthorizedDirection = grantedDirection }
                }
            };
        }
        return new(status, new(state.AuthorityId, relation?.Identity?.ChangeId ?? "STORY", relation?.Identity?.TaskId,
            null, Revision(state), state.AdoptionId, "Correction history only; active effort is unmeasured and no work allocation is asserted.", "known", hold,
            hold ?? actorAction ?? "Continue the current bounded story route; correction history does not grant an allocation or approval.",
            Math.Max(0, Tick() - state.BaselineStarted), new(0, 0, 0, 0, 0, 0), null, null, 0, "", state.AuthorityId, "", [], [])
        {
            CorrectionOnly = true,
            Corrections = rounds,
            CorrectionRelationshipId = relation?.Id,
            RequiredCorrectionFindingReferences = contextCurrent ? relation?.RequiredFindings ?? [] : [],
            RequiredCorrectionContext = contextCurrent ? currentContext : null,
            CorrectionContinuationAvailable = grant is not null && grantedDirection is not null,
            CorrectionContinuation = grant is null ? null : new(grant.Id, relation!.Id, grant.MaximumSeconds, grant.NextAction,
                grant.Actor, grant.RecordedAtUtc, grant.Context)
            { AuthorizedDirection = grantedDirection }
        }, [], applied);
    }

    private CisAgentWorkResult ExecutionChange(CisRepositoryContext authority, CisAgentWorkReference reference,
        Func<Ledger, Change, Execution, CisAgentWorkResult> action)
        => Mutate(authority, state =>
        {
            if (state is null || state.AdoptionId != reference.AdoptionId || reference.AuthorityRepositoryId != authority.RepositoryId)
                throw new InvalidDataException("The authoritative accounting reference is missing or changed; restore it before continuation.");
            var change = ChangeFor(state, reference.ChangeId);
            if (!change.Executions.TryGetValue(reference.ExecutionId, out var execution) || execution.Reference != reference)
                throw new InvalidDataException("This execution does not match its immutable work reference.");
            return (state, action(state, change, execution));
        });

    private CisAgentWorkResult Mutate(CisRepositoryContext authority, Func<Ledger?, (Ledger State, CisAgentWorkResult Result)> action)
    {
        try
        {
            var path = SafePath(authority, RelativePath); var marker = SafePath(authority, AdoptionPath);
            var lockPath = SafePath(authority, CisAgentWorkPaths.Lock);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var held = AcquireTransactionLock(lockPath);
            var current = Load(authority); var next = action(current);
            if (!next.Result.Applied) return next.Result;
            foreach (var change in next.State.Changes.Values) CompactStorageState(next.State, change);
            foreach (var change in next.State.Changes.Values) ValidateStorageRecordBounds(change);
            next.State.Revision++;
            var serialized = JsonSerializer.Serialize(next.State, LedgerJson);
            if (Encoding.UTF8.GetByteCount(serialized) > MaximumLedgerBytes) throw new InvalidDataException("The durable work ledger reached its 8 MiB bound; preserve accounting and resolve storage capacity before more work.");
            Atomic(path, serialized);
            if (!next.State.CorrectionOnly && !File.Exists(marker)) Atomic(marker, JsonSerializer.Serialize(new Adoption(1, authority.RepositoryId, next.State.AdoptionId,
                next.State.Changes.Values.First().Actor, next.State.Changes.Values.First().Reason, Time(Tick())), Json));
            return next.Result with { Work = next.Result.Work is null ? null : next.Result.Work with { Revision = Revision(next.State) } };
        }
        catch (IOException error) { return new("busy", null, ["Accounting is busy or unavailable; retry without starting a provider. " + error.Message], false); }
        catch (Exception error) when (Recoverable(error)) { return Failure(error); }
    }

    private static Ledger? Load(CisRepositoryContext authority)
    {
        var path = SafePath(authority, RelativePath); var marker = SafePath(authority, AdoptionPath);
        if (!File.Exists(path) && !File.Exists(marker)) return null;
        if (!File.Exists(path)) throw new InvalidDataException("Adopted work accounting is incomplete. Restore its ledger and adoption reference; missing state cannot become a fresh allowance.");
        var adoption = File.Exists(marker) ? JsonSerializer.Deserialize<Adoption>(ReadSnapshot(marker, 65536), Json) : null;
        var state = JsonSerializer.Deserialize<Ledger>(ReadSnapshot(path, MaximumLedgerBytes), Json);
        if (state is not { SchemaVersion: 1, Changes: not null, Corrections: not null }
            || state.AuthorityId != authority.RepositoryId || string.IsNullOrWhiteSpace(state.AdoptionId) || state.Revision < 1 || state.BaselineStarted <= 0
            || state.CorrectionOnly && (adoption is not null || state.Changes.Count != 0 || state.Corrections.Count == 0)
            || !state.CorrectionOnly && (adoption is not { SchemaVersion: 1 } || adoption.AuthorityId != authority.RepositoryId
                || state.AdoptionId != adoption.AdoptionId || state.Changes.Count == 0))
            throw new InvalidDataException("Work accounting identity or adoption evidence is invalid; restore it rather than resetting consumption.");
        foreach (var (relationshipId, relationship) in state.Corrections)
        {
            if (relationship is null || relationship.Id != relationshipId || relationship.Rounds is null || relationship.RequiredFindings is null
                || !BoundedText(relationship.Actor) || relationship.Rounds.Select(round => round.Id).Distinct(StringComparer.Ordinal).Count() != relationship.Rounds.Count
                || relationship.Rounds.Select((round, at) => round.Number != at + 1 || round.RelationshipId != relationshipId
                    || round.Status is not ("started" or "completed" or "superseded") || round.Stages is null || round.FindingReferences is null
                    || round.ExecutionIds is null || round.MaximumSeconds is < 30 or > 31_536_000
                    || round.MaximumSeconds > 3600 && (round.DispositionReference is null || round.ContinuationReferences is not { Count: > 0 })
                    || round.ContinuationReferences is { } references && (references.Count > 10000 || references.Any(reference => !BoundedText(reference)))
                    || round.AuthorizedDirections is { Count: > 10001 }
                    || round.UnresolvedDirectionReferences is { } unresolved && (unresolved.Count > 10000 || unresolved.Any(reference => !BoundedText(reference)))).Any(invalid => invalid)
                || relationship.Rounds.Count(round => round.Status == "started") > 1)
                throw new InvalidDataException("Story correction history is incomplete or invalid; restore it without replenishing rounds.");
            if (relationship.RequiredContext is { } required)
                CorrectionContext(required.ContractDigest, required.MaterialBasis, required.Findings, required.Candidates);
            foreach (var round in relationship.Rounds.Where(round => round.Context is not null))
            {
                var context = round.Context!;
                CorrectionContext(context.ContractDigest, context.MaterialBasis, context.Findings, context.Candidates);
            }
            if (relationship.Grant is { } grant)
            {
                Text(grant.Id, "Retained correction continuation"); Text(grant.NextAction, "Retained bounded action");
                if (grant.MaximumSeconds is < 30 or > 3600 || FindingReferences(grant.Findings).Count == 0)
                    throw new InvalidDataException("The retained correction continuation has lost its bounded authority.");
                if (grant.Context is { } context)
                    CorrectionContext(context.ContractDigest, context.MaterialBasis, context.Findings, context.Candidates);
            }
        }
        foreach (var (id, change) in state.Changes)
        {
            if (change is null || change.Id != id || change.Milliseconds < 0 || change.Attempts < 1 || change.Consumed < 0 || change.UsedAttempts < 0
                || change.Restoration < 0 || change.Restoration > change.Milliseconds || change.RestorationSpent < 0 || change.RestorationSpent > change.Restoration
                || change.Owners is null || change.Bindings is null || change.Executions is null
                || change.Controllers is null || change.Controllers.Count == 0 || change.AllocationHistory is null || change.HistoricalRuns is null
                || change.HistoricalRuns.Any(pair => !BoundedText(pair.Key) || pair.Value is null || pair.Value.Milliseconds <= 0
                    || !change.Owners.ContainsKey(pair.Value.OwnerTaskId)))
                throw new InvalidDataException("A containing-change accounting record is corrupt; restore it without replenishing capacity.");
            ValidateStorageState(authority, state, change);
            foreach (var (ownerId, owner) in change.Owners)
                if (owner is null || owner.Id != ownerId || owner.Milliseconds < 0 || owner.Attempts < 1 || owner.Consumed < 0 || owner.UsedAttempts < 0
                    || owner.Restoration < 0 || owner.Restoration > owner.Milliseconds || owner.RestorationSpent < 0 || owner.RestorationSpent > owner.Restoration
                    || owner.Checkpoints is null || owner.Outcomes is null
                    || owner.ReflectionWindowMilliseconds is < 30_000)
                    throw new InvalidDataException("A task accounting owner is corrupt; restore its recorded consumption and allocation.");
            if (change.Bindings.Values.Any(owner => !change.Owners.ContainsKey(owner)))
                throw new InvalidDataException("A task accounting binding has lost its immutable owner.");
            ValidateTaskTimeAmendments(state, change);
            foreach (var (executionId, execution) in change.Executions)
            {
                if (execution is null || execution.Reference is null || execution.Reference.ExecutionId != executionId || execution.Reference.AdoptionId != state.AdoptionId
                    || execution.Reference.AuthorityRepositoryId != state.AuthorityId || execution.Reference.ChangeId != change.Id
                    || !change.Owners.ContainsKey(execution.Reference.OwnerTaskId) || change.Bindings.GetValueOrDefault(execution.Reference.TaskId) != execution.Reference.OwnerTaskId
                    || execution.Started <= 0 || execution.Bound <= 0 || execution.Intervals is null || execution.Intervals.Count == 0
                    || execution.Controller is not null && !BoundedText(execution.Controller)
                    || !change.Executions.ContainsKey(execution.RootId) || execution.Reference.ParentExecutionId is { } parent && !change.Executions.ContainsKey(parent))
                    throw new InvalidDataException("An execution reservation or inherited accounting reference is corrupt; reconcile it before execution.");
                if (execution.PreparationOwner is not null) ValidatePreparationOwner(execution.PreparationOwner);
                if (execution.NativeRestorationRequired && (execution.Stage?.NativeOperation is null || execution.Stage.NativeRecoveryBasis is null)
                    || execution.RestorationOfExecutionId is { } originalId && (!change.Executions.TryGetValue(originalId, out var original)
                        || original.Reference.OwnerTaskId != execution.Reference.OwnerTaskId
                        || original.Stage?.NativeRecoveryBasis != execution.Stage?.NativeRecoveryBasis))
                    throw new InvalidDataException("Native restoration ownership or its retained recipe is corrupt; reconcile it without replenishing capacity.");
            }
        }
        return state;
    }

    private CisAgentWorkResult Result(Ledger state, Change change, string? taskId, string status, bool applied, string? explicitHold = null,
        CisAgentWorkStage? stage = null)
    {
        var owner = taskId is not null && change.Bindings.TryGetValue(taskId, out var id) ? change.Owners.GetValueOrDefault(id) : null;
        var correctionCapacity = owner is null ? null : StoryCorrectionCapacity(state, change, owner.Id, stage);
        var hold = explicitHold ?? (owner is null ? null : Hold(change, owner, stage, correctionCapacity));
        var executions = change.Executions.Values.Where(item => owner is null || item.Reference.OwnerTaskId == owner.Id)
            .Select(item => new CisAgentWorkExecution(item.Reference.ExecutionId, item.Reference.TaskId, item.Reference.OwnerTaskId,
                item.Reference.LogicalAttemptId, item.Reference.ParentExecutionId, item.RootId, item.Status, Time(item.Started),
                item.Completed is { } end ? Time(end) : null, UnionMilliseconds(item.Intervals.Select(interval => (interval.Start, interval.End ?? Tick()))),
                Math.Max(0, (item.Completed ?? Tick()) - item.Started), item.RootId == item.Reference.ExecutionId && !item.Finalized ? item.Bound : 0,
                item.ProcessId, item.ProcessStartedAtUtc, item.RecoveryReason ?? UnownedPreparation(item))
            {
                Finalized = item.Finalized,
                LaunchIntent = item.LaunchIntent,
                PreparationOwner = item.PreparationOwner,
                NativeContainment = item.NativeContainment,
                Stage = item.Stage,
                NativeRestorationRequired = item.NativeRestorationRequired,
                RestorationOfExecutionId = item.RestorationOfExecutionId
            }).ToArray();
        return new(status, new(state.AuthorityId, change.Id, taskId, owner?.Id, Revision(state), state.AdoptionId,
            change.Executions.Values.Any(item => item.ConservativeRecovery) || change.HistoricalRuns.Count > 0
                ? "Measured from the authorized baseline with conservative full-bound recovery charges; earlier historical effort is unmeasured."
                : "Measured from the authorized baseline; earlier historical effort is unmeasured.", executions.Any(item => item.RecoveryReason is not null) ? "uncertain" : "known",
            hold, explicitHold ?? (hold == ReflectionAction || hold is not null && owner is not null && hold == NativeRestorationHold(change, owner)
                || correctionCapacity is { } capacity && hold == capacity.Action ? hold : owner?.NextAction) ?? "Select a canonical task and inspect its remaining allowance.",
            Math.Max(0, Tick() - state.BaselineStarted),
            new(change.Milliseconds, change.Consumed, Reserved(change, null), ProtectedRestoration(change), change.Attempts, change.UsedAttempts),
            owner is null ? null : new(owner.Milliseconds, owner.Consumed, Reserved(change, owner.Id), ProtectedRestoration(owner), owner.Attempts, owner.UsedAttempts),
            owner?.OpenAttempt, owner?.RepeatedBlockers ?? 0, change.DecisionReference, state.AuthorityId, change.StorageReference,
            executions, owner?.Checkpoints ?? change.Owners.Values.SelectMany(item => item.Checkpoints).ToList())
        {
            TaskTimeAmendments = change.TaskTimeAmendments.Where(item => owner is null || item.OwnerTaskId == owner.Id).ToArray(),
            AcceptedOutcomeReferences = owner?.Outcomes ?? [],
            Hypothesis = owner?.Hypothesis,
            ExpectedResult = owner?.ExpectedResult,
            ReflectionWindowMilliseconds = owner?.ReflectionWindowMilliseconds,
            LastControllerReflectionMilliseconds = owner is null ? 0 : LastControllerReflection(change, owner),
            ReflectionDue = owner is not null && ReflectionDue(change, owner),
            Corrections = state.Corrections.Values.Where(relation => relation.Identity?.ChangeId == change.Id
                && (owner is null || relation.OwnerTaskId == owner.Id)).SelectMany(relation => relation.Rounds).ToArray()
        }, [], applied);
    }

    private static Owner? FindOwner(Change change, CisAgentWorkIdentity identity)
    {
        foreach (var task in new[] { identity.TaskId }.Concat(identity.AncestorTaskIds))
            if (change.Bindings.TryGetValue(task, out var owner) && change.Owners.TryGetValue(owner, out var existing)) return existing;
            else if (change.Owners.TryGetValue(task, out existing)) return existing;
        return null;
    }
    private string? Hold(Change change, Owner owner, CisAgentWorkStage? stage = null,
        (long Remaining, string Action)? correctionCapacity = null)
        => change.Executions.Values.FirstOrDefault(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id && item.RecoveryReason is not null)?.RecoveryReason
            ?? owner.Hold ?? NativeCancellationHold(change, owner) ?? NativeRestorationHold(change, owner)
            ?? (change.Executions.Values.Any(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id)
                ? change.Executions.Values.Where(item => item.Reference.OwnerTaskId == owner.Id).Select(UnownedPreparation).FirstOrDefault(reason => reason is not null)
                    ?? "An owned execution or child reservation is still active. Inspect or reconcile it before conflicting work."
                : owner.Milliseconds - owner.Consumed - ProtectedRestoration(owner) < 30_000 || change.Milliseconds - change.Consumed - Reserved(change, null) - ProtectedRestoration(change) < 30_000
                    ? "Remaining hard allowance cannot support the minimum operation while preserving restoration capacity. Use cis agent work allocate through the original allocation authority for unused outer capacity or a valid delegated transfer; fixed limits remain binding."
                    : owner.OpenAttempt is null && (owner.UsedAttempts >= owner.Attempts || change.UsedAttempts >= change.Attempts)
                        ? "The logical-attempt allowance is exhausted. Seek the applicable allocation authority."
                        : correctionCapacity is { Remaining: < 30_000 } exhausted ? exhausted.Action
                        : ReflectionDue(change, owner, stage) ? ReflectionAction : null);

    internal const string ReflectionAction = "Controller reflection is due for cumulative active work. Record Outcome, Evidence, measured Cost context, Approach, Decision and the next bounded action through cis agent work progress; the current logical attempt can remain open.";

    private bool ReflectionDue(Change change, Owner owner, CisAgentWorkStage? stage = null)
        => owner.ReflectionWindowMilliseconds is { } window
            && MeasuredOwnerMilliseconds(change, owner) - LastReflectionForRoute(change, owner, stage) >= window;

    private long LastReflectionForRoute(Change change, Owner owner, CisAgentWorkStage? stage)
        // Both current and pre-fix automatic story snapshots lack controller judgment.
        // Explicit authorized progress remains the existing owner-wide assessment route.
        => LastControllerReflection(change, owner);

    private long LastControllerReflection(Change change, Owner owner)
    {
        var measured = MeasuredOwnerMilliseconds(change, owner);
        return owner.Checkpoints.Where(record => QualifyingReflection(change, record, measured))
            .Select(record => record.ActiveMilliseconds).DefaultIfEmpty(0).Max();
    }

    private static bool QualifyingReflection(Change change, CisAgentWorkCheckpointRecord? record, long measured)
    {
        if (!ValidReflectionFields(change, record, measured)) return false;
        if (record!.Origin is not null) return record.Origin == "controller-progress";
        if (record.StoryRelationshipId is not null || record.StoryContractDigest is not null) return false;
        // These are exact historical CIS producer identities, not a classifier
        // over the controller's engineering judgment or narrative.
        return record.Actor != "CIS measured execution"
            && record.Checkpoint.Outcome is not ("Controller disposition" or "Never-launched reservation reconciled");
    }

    private static bool ValidReflectionFields(Change change, CisAgentWorkCheckpointRecord? record, long measured)
    {
        if (record is null || record.Checkpoint is not { } checkpoint || record.ActiveMilliseconds < 0 || record.ActiveMilliseconds > measured
            || !change.Controllers.Contains(record.Actor, StringComparer.Ordinal)
            || !BoundedText(checkpoint.Outcome) || !BoundedText(checkpoint.Approach) || !BoundedText(checkpoint.Decision) || !BoundedText(checkpoint.NextAction)
            || checkpoint.Evidence is not { Count: > 0 and <= 40 } || checkpoint.Evidence.Any(evidence => !BoundedText(evidence))) return false;
        return true;
    }

    private long MeasuredOwnerMilliseconds(Change change, Owner owner)
    {
        var tick = Tick();
        return checked(owner.Consumed + change.Executions.Values.Where(execution => !execution.Finalized && execution.Reference.OwnerTaskId == owner.Id)
            .GroupBy(execution => execution.RootId, StringComparer.Ordinal)
            .Sum(tree => UnionMilliseconds(tree.SelectMany(execution => execution.Intervals).Select(interval => (interval.Start, interval.End ?? tick)))));
    }

    private static string? UnownedPreparation(Execution execution)
        => !execution.Stopped && !execution.Finalized && !execution.LaunchIntent && execution.ProcessId is null && execution.PreparationOwner is null
            ? "Preparation ownership is unknown for an unresolved historical reservation. Preserve its capacity; elapsed grace and absent owner metadata do not prove stopped preparation." : null;

    private static void ValidatePreparationOwner(CisAgentWorkPreparationOwner owner)
    {
        Text(owner.HostId, "Preparation host identity");
        Text(owner.ProcessStartedAtUtc, "Preparation process start time");
        if (owner.ProcessId <= 0 || !DateTimeOffset.TryParse(owner.ProcessStartedAtUtc, out _))
            throw new InvalidDataException("Preparation ownership requires its verified process ID and start time.");
    }

    private static bool BoundedText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000 && !value.Contains('\0');
    private static long Reserved(Change change, string? owner)
        => change.Executions.Values.Where(item => !item.Finalized && item.Reference.ParentExecutionId is null && (owner is null || item.Reference.OwnerTaskId == owner)).Sum(item => item.Bound);
    internal static long UnionMilliseconds(IEnumerable<(long Start, long End)> intervals)
    {
        long total = 0, start = 0, end = 0; var first = true;
        foreach (var interval in intervals.OrderBy(item => item.Start))
        {
            if (interval.End < interval.Start) throw new InvalidDataException("An accounting interval ends before it starts.");
            if (first) { start = interval.Start; end = interval.End; first = false; }
            else if (interval.Start <= end) end = Math.Max(end, interval.End);
            else { total = checked(total + end - start); start = interval.Start; end = interval.End; }
        }
        return first ? 0 : checked(total + end - start);
    }
    private static Change ChangeFor(Ledger state, string id) => state.Changes.GetValueOrDefault(id) ?? throw new InvalidDataException("The containing change has no authorized allocation. Supply that owning decision once before governed execution.");
    private static void Authorize(Change change, string actor)
    { Text(actor, "Controller actor"); if (!change.Controllers.Contains(actor.Trim(), StringComparer.Ordinal)) throw new InvalidDataException("This actor is not delegated as a controller by the owning allocation."); }
    private static void CheckRevision(Ledger state, string? expected)
    { if (expected != Revision(state)) throw new InvalidDataException("Work accounting changed. Read its current revision before recording this controller decision."); }
    private static void ValidateIdentity(CisRepositoryContext authority, string change, CisAgentWorkIdentity identity)
    { if (identity.AuthorityRepositoryId != authority.RepositoryId || identity.ChangeId != change || identity.AncestorTaskIds is null || identity.AcceptedOutcomeReferences is null) throw new InvalidDataException("Work ownership must resolve through the same CIS authority and containing change."); Text(identity.TaskId, "Canonical task identity"); }
    private static long Milliseconds(long seconds) => seconds is >= 0 and <= 31_536_000 ? checked(seconds * 1000) : throw new InvalidDataException("Allocation seconds must be finite and bounded to one year.");
    private static int Attempts(int value) => value is >= 1 and <= 10000 ? value : throw new InvalidDataException("Supply between one and 10,000 logical attempts.");
    private static void Text(string? value, string label)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > 4000 || value.Contains('\0')) throw new InvalidDataException(label + " is required and must be bounded."); }
    private static string Revision(Ledger state) => state.AdoptionId + ":" + state.Revision;
    private long Tick() => now().ToUniversalTime().ToUnixTimeMilliseconds();
    private static string Time(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).ToString("O");
    private static string SafePath(CisRepositoryContext authority, string relative)
    {
        if (CisPathSafety.IsReparsePoint(authority.RepositoryPath) || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, relative, out var path)
            || CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path))
            throw new InvalidDataException("Work accounting requires a regular owned authority path without links.");
        return path;
    }
    private static void Atomic(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    // ReplaceFile preserves existing delete-sharing snapshots on
                    // Windows; MoveFile overwrite can deny the same open handle.
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(10); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string ReadSnapshot(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Work accounting exceeds its bounded state format.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() >= 0) throw new InvalidDataException("Work accounting changed outside atomic replacement; restore a coherent snapshot.");
        return Encoding.UTF8.GetString(bytes);
    }
    private static FileStream AcquireTransactionLock(string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(10); }
        }
    }
    private static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or OverflowException;
    private static CisAgentWorkResult Failure(Exception error) => new("held", null, ["Work accounting must be restored or reconciled before execution: " + error.Message], false);
    private static CisAgentWorkResult Missing() => new("missing-allocation", null, ["No authorized work allocation exists. Establish the owning change/task allocation and explicit historical-effort baseline before governed execution."], false);
}
