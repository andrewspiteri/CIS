using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    private static readonly StringComparer StoragePathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const long StorageRecordHeadroom = 64 * 1024;
    private const int MaximumStorageRecords = 10000;
    private const long MaximumDeletionRecordGrowth = 256 * 1024;

    private sealed class StorageAllocation
    {
        public CisOwnedStoragePolicy Policy { get; set; } = new(0, []);
        public Dictionary<string, StorageClaim> Claims { get; set; } = new(StoragePathComparer);
        public Dictionary<string, StorageReservation> Reservations { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record StorageClaim(string Path, string OwnerTaskId, bool Scratch, bool Required,
        string? RequiredByExecutionId = null, string? SupersededByExecutionId = null)
    {
        public NativeScratchAdmission? NativeScratchAdmission { get; init; }
        public IReadOnlyList<CisAgentWorkReference> RetainedWorkReferences { get; init; } = [];
    }
    private sealed record NativeScratchAdmission(string Path, string TemporaryRoot, CisAgentWorkReference Reference);
    private sealed record StorageReservation(CisAgentWorkReference Reference, string OperationId, long MaximumGrowthBytes,
        long RecordHeadroomBytes, IReadOnlyList<string> Paths, bool Cleanup, bool Finalized = false)
    {
        public IReadOnlyList<string> OperationPaths { get; init; } = [];
        public IReadOnlyList<string> AccountingPaths { get; init; } = [];
        public long? InitialOperationBytes { get; init; }
        public Dictionary<string, IReadOnlyList<CisAgentWorkReference>> RetainedWorkReferences { get; init; } = new(StoragePathComparer);
    }
    private sealed record StorageSnapshot(Ledger State, Change Change, string OwnerTaskId, long OwnedBytes,
        long ProtectedBytes, IReadOnlyList<string> Paths, IReadOnlyList<string> ProtectedPaths);

    private static void ApplyStorageAllocation(CisRepositoryContext authority, Ledger state, Change change,
        CisOwnedStoragePolicy? requested, string actor, bool isNew)
    {
        if (requested is null) return;
        ValidateStoragePolicy(authority, requested);
        if (change.Storage is { } existing)
        {
            if (JsonSerializer.Serialize(existing.Policy, Json) != JsonSerializer.Serialize(requested, Json))
                throw new InvalidDataException("Ordinary allocation cannot replace the fixed owned-storage ceiling, roots, restoration reserve or cleanup delegation.");
            return;
        }
        if (!isNew && actor.Trim() != change.Actor)
            throw new InvalidDataException("Only the original allocation authority may supply the missing finite storage policy; a test profile cannot allocate it.");
        foreach (var other in state.Changes.Values.Where(item => item != change && item.Storage is not null))
        {
            var sameCeiling = other.StorageReference == change.StorageReference;
            if (sameCeiling && (other.Storage!.Policy.MaximumBytes != requested.MaximumBytes
                || other.Storage.Policy.RestorationBytes != requested.RestorationBytes))
                throw new InvalidDataException("The same owned-storage ceiling reference must retain its original finite byte limit and restoration reserve.");
            if (!sameCeiling && PolicyPaths(requested).Any(path => PolicyPaths(other.Storage!.Policy).Any(otherPath => StorageOverlaps(path, otherPath))))
                throw new InvalidDataException("Overlapping owned storage must share its existing ceiling reference; a new change cannot spend the same physical capacity twice.");
        }
        change.Storage = new() { Policy = requested };
    }

    private static void ValidateStoragePolicy(CisRepositoryContext authority, CisOwnedStoragePolicy policy)
    {
        if (policy.MaximumBytes <= 0 || policy.RestorationBytes < 0 || policy.RestorationBytes > policy.MaximumBytes
            || policy.Roots is not { Count: > 0 and <= 100 } || policy.CleanupControllers is not { Count: <= 100 }
            || policy.CleanupControllers.Any(actor => !BoundedText(actor)))
            throw new InvalidDataException("Supply a finite owned-storage MaximumBytes, explicit CIS roots, and a restoration reserve inside that ceiling.");
        foreach (var root in policy.Roots)
        {
            if (root is null || !Path.IsPathFullyQualified(root.RepositoryPath) || !BoundedText(root.Path)
                || root.Path.Replace('\\', '/').TrimEnd('/') is not { } relative
                || !root.Scratch && relative != ".cis/local" && !relative.StartsWith(".cis/local/", StringComparison.Ordinal)
                || relative.Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
                || !CisPathSafety.TryResolveUnderRoot(root.RepositoryPath, relative, out var path)
                || CisPathSafety.ContainsReparsePoint(root.RepositoryPath, path))
                throw new InvalidDataException("Owned roots must be explicit unlinked CIS local paths or count-only build/scratch paths in the authority or a registered owned repository; .git is excluded.");
        }
    }

    private static void ValidateStorageState(CisRepositoryContext authority, Ledger state, Change change)
    {
        if (change.Storage is not { } storage) return;
        ValidateStoragePolicy(authority, storage.Policy);
        if (storage.Claims is null || storage.Reservations is null || storage.Claims.Count > MaximumStorageRecords || storage.Reservations.Count > MaximumStorageRecords
            || storage.Claims.Any(pair => pair.Value is null || !StoragePathComparer.Equals(pair.Key, pair.Value.Path)
                || !Path.IsPathFullyQualified(pair.Key) || !change.Owners.ContainsKey(pair.Value.OwnerTaskId)
                || pair.Value.RequiredByExecutionId is { } origin && (!change.Executions.TryGetValue(origin, out var source)
                    || source.Reference.OwnerTaskId != pair.Value.OwnerTaskId)
                || pair.Value.SupersededByExecutionId is { } replacement && !change.Executions.ContainsKey(replacement))
            || storage.Reservations.Any(pair => !BoundedText(pair.Key) || pair.Value is null
                || pair.Value.MaximumGrowthBytes < 0 || pair.Value.RecordHeadroomBytes < 0 || pair.Value.Paths is null
                || pair.Value.OperationPaths is null || pair.Value.AccountingPaths is null || pair.Value.InitialOperationBytes < 0
                || pair.Value.OperationPaths.Any(path => !Path.IsPathFullyQualified(path))
                || pair.Value.AccountingPaths.Any(path => !Path.IsPathFullyQualified(path))
                || !change.Executions.TryGetValue(pair.Value.Reference.ExecutionId, out var execution) || execution.Reference != pair.Value.Reference))
            throw new InvalidDataException("Owned-storage accounting is incomplete; restore its original claims and reservations rather than assuming zero usage.");
        foreach (var grouped in StorageGroup(state, change)) ValidateStoragePolicy(authority, grouped.Storage!.Policy);
        foreach (var claim in storage.Claims.Values)
        {
            ValidateStorageClaim(state, change, new(claim.Path, claim.Scratch, claim.Required));
            ValidateRetainedWorkReferences(state, change, claim.OwnerTaskId, claim.RetainedWorkReferences);
            if (!claim.Required && claim.RetainedWorkReferences.Count != 0)
                throw new InvalidDataException("Only required published evidence may retain native work dependencies.");
        }
        foreach (var reservation in storage.Reservations.Values)
        {
            if (reservation.RetainedWorkReferences is null || reservation.RetainedWorkReferences.Count > 100
                || reservation.RetainedWorkReferences.Keys.Any(path => !reservation.Paths.Contains(path, StoragePathComparer)))
                throw new InvalidDataException("Pending evidence dependencies must belong to the original exact storage paths.");
            foreach (var references in reservation.RetainedWorkReferences.Values)
                ValidateRetainedWorkReferences(state, change, reservation.Reference.OwnerTaskId, references);
            if (reservation.RetainedWorkReferences.Values.Sum(references => references.Count) > MaximumStorageRecords)
                throw new InvalidDataException("Pending evidence dependencies exceed the bounded storage inventory.");
        }
    }

    private static void ValidateRetainedWorkReferences(Ledger state, Change change, string ownerTaskId,
        IReadOnlyList<CisAgentWorkReference>? references)
    {
        if (references is null || references.Count > MaximumStorageRecords)
            throw new InvalidDataException("Supply a bounded exact set of retained evidence work references.");
        foreach (var reference in references)
        {
            if (reference is null || reference.ChangeId != change.Id || reference.OwnerTaskId != ownerTaskId)
                throw new InvalidDataException("Retained evidence work must belong to the same canonical change and owner.");
            MatchStorageExecution(state, change, reference, requireLive: false);
        }
    }

    private static Dictionary<string, IReadOnlyList<CisAgentWorkReference>> StorageRetainedWorkReferences(
        Ledger state, Change change, CisAgentWorkReference reference, IReadOnlyList<CisOwnedStoragePath>? claims,
        StorageReservation? previous)
    {
        var retained = previous is null ? new Dictionary<string, IReadOnlyList<CisAgentWorkReference>>(StoragePathComparer)
            : new Dictionary<string, IReadOnlyList<CisAgentWorkReference>>(previous.RetainedWorkReferences, StoragePathComparer);
        foreach (var claim in claims ?? [])
        {
            if (claim.RetainedWorkReferences is not { } references) continue;
            ValidateRetainedWorkReferences(state, change, reference.OwnerTaskId, references);
            var path = Path.GetFullPath(claim.Path);
            var exact = references.Distinct().ToArray();
            if (retained.TryGetValue(path, out var original) && !original.ToHashSet().SetEquals(exact))
                throw new InvalidDataException("Published work dependencies cannot change within the same storage operation.");
            retained[path] = exact;
        }
        if (retained.Values.Sum(references => references.Count) > MaximumStorageRecords)
            throw new InvalidDataException("Pending evidence dependencies exceed the bounded storage inventory.");
        return retained;
    }

    internal CisOwnedStorageResult InspectStorage(CisRepositoryContext authority, string changeId, string taskId,
        IReadOnlyList<string>? ancestors = null)
    {
        try { return StorageResult(SnapshotStorage(authority, changeId, taskId, ancestors: ancestors), "current"); }
        catch (Exception error) when (Recoverable(error)) { return StorageFailure(error); }
    }

    internal IReadOnlyList<CisOwnedStorageRoot> StorageRoots(CisRepositoryContext authority, string changeId)
    {
        using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
        var state = Load(authority) ?? throw new InvalidDataException("The owning storage state is missing.");
        var change = ChangeFor(state, changeId);
        return StorageGroup(state, change).SelectMany(item => item.Storage!.Policy.Roots).Distinct().ToArray();
    }

    internal CisOwnedStorageAdmission ReserveStorage(CisRepositoryContext authority, CisAgentWorkReference reference,
        string operationId, long maximumGrowthBytes, IReadOnlyList<CisOwnedStoragePath>? claims,
        bool cleanup, bool clampToRemaining, string actor)
    {
        try
        {
            Text(operationId, "Owned-storage operation");
            if (maximumGrowthBytes < 0 || claims is { Count: > 100 }) throw new InvalidDataException("Supply one bounded storage growth request and at most 100 exact paths.");
            var snapshot = SnapshotStorage(authority, reference.ChangeId, reference.TaskId, claims);
            var change = snapshot.Change;
            MatchStorageExecution(snapshot.State, change, reference, requireLive: true);
            var scratchAdmissions = (claims ?? []).Select(claim => StorageClaimAdmission(snapshot.State, change, reference, claim)).ToArray();
            var allocation = change.Storage!;
            if (cleanup)
            {
                Authorize(change, actor);
                if (actor.Trim() != change.Actor && !allocation.Policy.CleanupControllers.Contains(actor.Trim(), StringComparer.Ordinal))
                    throw new InvalidDataException("This controller has no delegated cleanup authority in the owning storage policy.");
            }
            // Only the time ledger can establish an exact original native
            // restoration. A caller-selected cleanup flag is not needed for it.
            var execution = change.Executions[reference.ExecutionId];
            // Delegation permits cleanup; it does not turn archive/retrieval growth
            // into native restoration. Only bounded deletion-record metadata may
            // temporarily use that headroom to free an otherwise full ordinary pool.
            var existing = allocation.Reservations.FirstOrDefault(pair => pair.Value.Reference == reference
                && pair.Value.OperationId == operationId);
            var retainedWork = StorageRetainedWorkReferences(snapshot.State, change, reference, claims, existing.Value);
            var operation = StorageOperation(authority, snapshot.State, change, reference, claims, existing.Value);
            var retainedProtection = 0;
            var deletionRepository = execution.Stage?.RepositoryPath ?? authority.RepositoryPath;
            var deletionPaths = new[] { ".cis/local/artifacts", ".cis/local/artifacts/operation.lock", ".cis/local/testing/mutation/run.lock" }
                .Select(path => Path.GetFullPath(Path.Combine(deletionRepository, path))).ToHashSet(StoragePathComparer);
            var cleanupRecords = Path.GetFullPath(Path.Combine(deletionRepository, ".cis/local/artifacts/cleanup"));
            var deletionRecord = cleanup && operationId == "clean" && execution.Stage?.NativeOperation == "artifacts-clean"
                && (existing.Value?.MaximumGrowthBytes ?? maximumGrowthBytes) <= MaximumDeletionRecordGrowth && claims is { Count: > 0 }
                && claims.All(claim => !claim.Scratch && !claim.Required)
                && (existing.Value?.Paths ?? []).Concat(claims.Select(claim => Path.GetFullPath(claim.Path))).All(path =>
                    (deletionPaths.Contains(path) || StoragePathComparer.Equals(Path.GetDirectoryName(path), cleanupRecords)
                        && Path.GetFileName(path) is { Length: <= 160 } name && name.StartsWith("CLEAN-", StringComparison.Ordinal)
                        && name.EndsWith(".json", StringComparison.Ordinal)
                        && name[..^5].All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                    && allocation.Claims.GetValueOrDefault(path) is not { Scratch: true } and not { Required: true });
            var protectedRestoration = deletionRecord || execution.RestorationOfExecutionId is not null
                ? 0 : allocation.Policy.RestorationBytes;
            if (existing.Value is { } previous)
            {
                if (previous.Finalized) throw new InvalidDataException("A finalized storage operation cannot acquire a fresh reservation through the same run ID.");
                var merged = previous.Paths.Concat((claims ?? []).Select(claim => Path.GetFullPath(claim.Path)))
                    .Distinct(StoragePathComparer).ToArray();
                if (merged.Length > 100) throw new InvalidDataException("One native storage operation may retain at most 100 exact owned path claims.");
                foreach (var claim in (claims ?? []).Where(item => item.MutatesExisting))
                    ValidateStorageMutation(snapshot.State, change, reference, Path.GetFullPath(claim.Path), existing.Key);
                ValidateOperationOverlap(snapshot.State, change, reference, operation.Paths, operation.Accounting, existing.Key);
                if (merged.Length == previous.Paths.Count && operation.Paths.SequenceEqual(previous.OperationPaths, StoragePathComparer)
                    && retainedWork.Count == previous.RetainedWorkReferences.Count
                    && (claims ?? []).All(claim => allocation.Claims.TryGetValue(Path.GetFullPath(claim.Path), out var current)
                    && current.Scratch == claim.Scratch && (!claim.Required || current.Required
                        && current.OwnerTaskId == reference.OwnerTaskId && current.SupersededByExecutionId is null)))
                    return OperationAdmission(StorageProtectionResult(StorageResult(snapshot, "reserved"), (claims ?? []).Count(claim =>
                        allocation.Claims.GetValueOrDefault(Path.GetFullPath(claim.Path)) is { Required: true } prior
                        && prior.OwnerTaskId != reference.OwnerTaskId)), existing.Key, previous.MaximumGrowthBytes, operation);
                var headroom = checked(new FileInfo(SafePath(authority, RelativePath)).Length + StorageRecordHeadroom
                    + JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        claims = claims ?? [],
                        scratchAdmissions,
                        operation.Paths,
                        operation.Accounting,
                        operation.Initial
                    }, Json).LongLength * 4);
                var extraHeadroom = Math.Max(0, headroom - previous.RecordHeadroomBytes);
                var allPending = StorageGroup(snapshot.State, change).Sum(item => item.Storage!.Reservations.Values.Where(item => !item.Finalized)
                    .Sum(item => checked(item.MaximumGrowthBytes + item.RecordHeadroomBytes)));
                var available = RemainingStorage(allocation.Policy.MaximumBytes, snapshot.OwnedBytes, allPending, protectedRestoration);
                if (extraHeadroom > available)
                    return new(StorageResult(snapshot, "held", ["Additional owned path metadata cannot fit the existing storage ceiling; no new scratch was created."]), null, 0);
                var registered = Mutate(authority, current =>
                {
                    if (current is null) throw new InvalidDataException("The owning accounting state is missing.");
                    CheckRevision(current, Revision(snapshot.State));
                    var currentChange = ChangeFor(current, reference.ChangeId);
                    MatchStorageExecution(current, currentChange, reference, requireLive: true);
                    foreach (var references in retainedWork.Values)
                        ValidateRetainedWorkReferences(current, currentChange, reference.OwnerTaskId, references);
                    ValidateOperationOverlap(current, currentChange, reference, operation.Paths, operation.Accounting, existing.Key);
                    foreach (var claim in (claims ?? []).Where(item => item.MutatesExisting))
                        ValidateStorageMutation(current, currentChange, reference, Path.GetFullPath(claim.Path), existing.Key);
                    foreach (var claim in claims ?? [])
                        if (RegisterStorageClaim(current, currentChange, reference, claim, existing.Key)) retainedProtection++;
                    currentChange.Storage!.Reservations[existing.Key] = previous with
                    {
                        Paths = merged,
                        OperationPaths = operation.Paths,
                        AccountingPaths = operation.Accounting,
                        InitialOperationBytes = operation.Initial,
                        RetainedWorkReferences = retainedWork,
                        RecordHeadroomBytes = checked(previous.RecordHeadroomBytes + extraHeadroom)
                    };
                    return (current, Result(current, currentChange, reference.TaskId, "storage-claimed", true));
                }, retainedWork.Values.SelectMany(references => references).ToArray());
                return registered.ExitCode == 0
                    ? OperationAdmission(StorageProtectionResult(InspectStorage(authority, reference.ChangeId, reference.TaskId) with { Status = "reserved" }, retainedProtection), existing.Key, previous.MaximumGrowthBytes, operation)
                    : new(new(registered.Status, true, null, registered.Errors), null, 0);
            }
            var group = StorageGroup(snapshot.State, change);
            var pending = group.Sum(item => item.Storage!.Reservations.Values.Where(item => !item.Finalized)
                .Sum(item => checked(item.MaximumGrowthBytes + item.RecordHeadroomBytes)));
            var recordHeadroom = checked(new FileInfo(SafePath(authority, RelativePath)).Length + StorageRecordHeadroom
                + JsonSerializer.SerializeToUtf8Bytes(new
                {
                    claims = claims ?? [],
                    scratchAdmissions,
                    operation.Paths,
                    operation.Accounting,
                    operation.Initial
                }, Json).LongLength * 4);
            var restoration = protectedRestoration;
            var availableBytes = RemainingStorage(allocation.Policy.MaximumBytes, snapshot.OwnedBytes, pending, restoration);
            var usable = recordHeadroom >= availableBytes ? 0 : availableBytes - recordHeadroom;
            var growth = clampToRemaining ? Math.Min(maximumGrowthBytes, usable) : maximumGrowthBytes;
            if (recordHeadroom > availableBytes || growth > usable || maximumGrowthBytes > 0 && growth == 0)
                return new(StorageResult(snapshot, "held", [StorageHold(snapshot, pending, restoration, maximumGrowthBytes)]), null, 0);
            var id = Guid.NewGuid().ToString("N");
            CisOwnedStorageResult? result = null;
            var mutation = Mutate(authority, current =>
            {
                if (current is null) throw new InvalidDataException("The owning accounting state is missing.");
                CheckRevision(current, Revision(snapshot.State));
                var currentChange = ChangeFor(current, reference.ChangeId);
                MatchStorageExecution(current, currentChange, reference, requireLive: true);
                foreach (var references in retainedWork.Values)
                    ValidateRetainedWorkReferences(current, currentChange, reference.OwnerTaskId, references);
                ValidateOperationOverlap(current, currentChange, reference, operation.Paths, operation.Accounting);
                foreach (var claim in (claims ?? []).Where(item => item.MutatesExisting))
                    ValidateStorageMutation(current, currentChange, reference, Path.GetFullPath(claim.Path));
                foreach (var claim in claims ?? [])
                    if (RegisterStorageClaim(current, currentChange, reference, claim)) retainedProtection++;
                currentChange.Storage!.Reservations.Add(id, new(reference, operationId, growth, recordHeadroom,
                    (claims ?? []).Select(claim => Path.GetFullPath(claim.Path)).ToArray(), cleanup)
                {
                    OperationPaths = operation.Paths,
                    AccountingPaths = operation.Accounting,
                    InitialOperationBytes = operation.Initial,
                    RetainedWorkReferences = retainedWork
                });
                result = StorageResult(snapshot, "reserved") with
                { Storage = ViewStorage(snapshot, checked(pending + growth + recordHeadroom)) };
                return (current, Result(current, currentChange, reference.TaskId, "storage-reserved", true));
            }, retainedWork.Values.SelectMany(references => references).ToArray());
            if (mutation.ExitCode != 0 || result is null) return new(new(mutation.Status, true, null, mutation.Errors), null, 0);
            return OperationAdmission(StorageProtectionResult(InspectStorage(authority, reference.ChangeId, reference.TaskId) with { Status = "reserved" }, retainedProtection), id, growth, operation);
        }
        catch (Exception error) when (Recoverable(error)) { return new(StorageFailure(error), null, 0); }
    }

    internal CisOwnedStorageResult SettleStorage(CisRepositoryContext authority, CisAgentWorkReference reference,
        string reservationId, bool safelyStopped, IReadOnlyList<string>? requiredPaths,
        IReadOnlyList<string>? supersededRequiredPaths = null)
    {
        try
        {
            Text(reservationId, "Owned-storage reservation identity");
            if (requiredPaths is { Count: > MaximumStorageRecords } || supersededRequiredPaths is { Count: > MaximumStorageRecords })
                throw new InvalidDataException("Storage evidence replacement exceeds the existing bounded claim inventory.");
            var required = (requiredPaths ?? []).Select(Path.GetFullPath).ToHashSet(StoragePathComparer);
            var superseded = (supersededRequiredPaths ?? []).Select(Path.GetFullPath).Where(path => !required.Contains(path)).ToHashSet(StoragePathComparer);
            var retainedProtection = 0;
            var mutation = Mutate(authority, current =>
            {
                if (current is null) throw new InvalidDataException("The owning accounting state is missing.");
                var change = ChangeFor(current, reference.ChangeId);
                MatchStorageExecution(current, change, reference, requireLive: false);
                var storage = change.Storage ?? throw new InvalidDataException("The original storage allocation is missing.");
                if (!storage.Reservations.TryGetValue(reservationId, out var reservation))
                {
                    if (change.Executions[reference.ExecutionId].Finalized) return (current, Result(current, change, reference.TaskId, "storage-settled", false));
                    throw new InvalidDataException("The storage reservation does not match this immutable work execution.");
                }
                if (reservation.Reference != reference) throw new InvalidDataException("The storage reservation belongs to a different immutable execution.");
                if (reservation.Finalized) return (current, Result(current, change, reference.TaskId, "storage-settled", false));
                if (!safelyStopped) return (current, Result(current, change, reference.TaskId, "held", false,
                    "Owned native work is live or uncertain; its full storage reservation remains held until safely stopped."));
                var retainedRequired = new HashSet<string>(StoragePathComparer);
                foreach (var path in required)
                {
                    ValidateStorageClaim(current, change, new(path, storage.Claims.GetValueOrDefault(path)?.Scratch == true, Required: true));
                    if (storage.Claims.GetValueOrDefault(path)?.Required != true)
                    {
                        try { ValidateRequiredPromotion(current, change, reference, path, reservation, reservationId); }
                        catch (InvalidDataException error)
                        { return (current, Result(current, change, reference.TaskId, "held", false, error.Message)); }
                    }
                    if (storage.Claims.GetValueOrDefault(path) is { Required: true } prior && prior.OwnerTaskId != reference.OwnerTaskId)
                    {
                        try
                        {
                            ValidateSupersededClaim(current, change, reference, path, reservationId);
                            if (prior.RetainedWorkReferences.Count != 0 && !reservation.RetainedWorkReferences.ContainsKey(path))
                                throw new InvalidDataException("Another owner's published dependencies remain protected until explicitly replaced.");
                        }
                        catch (InvalidDataException)
                        {
                            retainedProtection++; retainedRequired.Add(path);
                            storage.Claims[path] = prior with { SupersededByExecutionId = null };
                        }
                    }
                }
                var released = new List<string>();
                foreach (var path in superseded)
                {
                    ValidateStorageClaim(current, change, new(path, storage.Claims.GetValueOrDefault(path)?.Scratch == true));
                    try { ValidateSupersededClaim(current, change, reference, path, reservationId); released.Add(path); }
                    catch (InvalidDataException)
                    {
                        retainedProtection++;
                        if (storage.Claims.GetValueOrDefault(path) is { RequiredByExecutionId: not null } prior
                            && MaySupersedeClaim(change, reference, prior))
                            storage.Claims[path] = prior with { SupersededByExecutionId = reference.ExecutionId };
                    }
                }
                storage.Reservations[reservationId] = reservation with { Finalized = true };
                foreach (var path in required)
                    if (!retainedRequired.Contains(path))
                    {
                        var prior = storage.Claims.GetValueOrDefault(path);
                        var admission = ValidateStorageClaim(current, change, new(path, prior?.Scratch == true, Required: true));
                        storage.Claims[path] = new(path, reference.OwnerTaskId, prior?.Scratch == true, true, reference.ExecutionId)
                        {
                            NativeScratchAdmission = admission,
                            RetainedWorkReferences = reservation.RetainedWorkReferences.GetValueOrDefault(path)
                                ?? prior?.RetainedWorkReferences ?? []
                        };
                    }
                foreach (var path in released)
                    if (storage.Claims.TryGetValue(path, out var claim) && claim.Required)
                        storage.Claims[path] = claim with
                        {
                            Required = false,
                            RequiredByExecutionId = null,
                            SupersededByExecutionId = null,
                            RetainedWorkReferences = []
                        };
                PruneMissingStorageClaims(change, reservation.Paths.Concat(superseded));
                return (current, Result(current, change, reference.TaskId, "storage-settled", true));
            });
            if (mutation.ExitCode != 0) return new(mutation.Status, true, null,
                mutation.Errors.Count > 0 ? mutation.Errors : [mutation.Work?.HoldReason ?? "Storage settlement remains held."]);
            var settled = InspectStorage(authority, reference.ChangeId, reference.TaskId);
            return StorageProtectionResult(settled with { Status = "settled" }, retainedProtection);
        }
        catch (Exception error) when (Recoverable(error)) { return StorageFailure(error); }
    }

    private static bool RegisterStorageClaim(Ledger state, Change change, CisAgentWorkReference reference,
        CisOwnedStoragePath claim, string? ignoreReservationId = null)
    {
        var admission = StorageClaimAdmission(state, change, reference, claim);
        var path = Path.GetFullPath(claim.Path);
        var storage = change.Storage!;
        var prior = storage.Claims.GetValueOrDefault(path);
        if (prior is { Required: true })
        {
            if (!claim.Required) return prior.OwnerTaskId != reference.OwnerTaskId;
            if (prior.OwnerTaskId != reference.OwnerTaskId)
            {
                try { ValidateSupersededClaim(state, change, reference, path, ignoreReservationId); }
                catch (InvalidDataException)
                {
                    storage.Claims[path] = prior with { SupersededByExecutionId = null };
                    return true;
                }
                // A pending replacement cannot transfer published dependencies
                // to another owner before its own evidence is successfully settled.
                if (prior.RetainedWorkReferences.Count != 0) return true;
            }
        }
        storage.Claims[path] = new(path, reference.OwnerTaskId, claim.Scratch, claim.Required,
            claim.Required ? reference.ExecutionId : null)
        { NativeScratchAdmission = admission, RetainedWorkReferences = prior?.RetainedWorkReferences ?? [] };
        return false;
    }

    private static CisOwnedStorageResult StorageProtectionResult(CisOwnedStorageResult result, int retainedProtection)
    {
        if (retainedProtection == 0 || !result.Allowed || result.Storage is not { } view) return result;
        return result with
        {
            Status = result.Status + "-protection-retained",
            Storage = view with
            {
                Coverage = view.Coverage + " Evidence retained its original protection for " + retainedProtection
                    + " paths still requiring their original cleanup or recovery authority. "
                    + (result.Status == "settled" ? "Current evidence remains protected and this stopped growth reservation is settled."
                        : "The prior evidence authority and this storage grant are unchanged.")
            }
        };
    }

    private static void SettleReconciledNativeStorage(Change change, IReadOnlyList<Execution> tree, bool verifiedStopped = false)
    {
        if (change.Storage is not { } storage || !verifiedStopped && !tree.Any(item => item.ConservativeRecovery)
            || tree.Any(item => !item.Stopped || !item.Finalized || item.RecoveryReason is not null)) return;
        var nativeReferences = tree.Where(item => item.Stage?.NativeOperation is not null).Select(item => item.Reference).ToHashSet();
        foreach (var pair in storage.Reservations.Where(pair => !pair.Value.Finalized && nativeReferences.Contains(pair.Value.Reference)).ToArray())
        {
            storage.Reservations[pair.Key] = pair.Value with { Finalized = true };
            // Unknown-interruption diagnostics remain necessary after the process
            // reservation is reconciled; releasing growth cannot make them eligible.
            foreach (var path in pair.Value.Paths)
                if (storage.Claims.TryGetValue(path, out var claim) && !claim.Required) storage.Claims[path] = claim with
                {
                    Required = true,
                    RequiredByExecutionId = pair.Value.Reference.ExecutionId
                };
        }
    }

    private static void ValidateStorageRecordBounds(Change change)
    {
        if (change.Storage is { } storage && (storage.Claims.Count > MaximumStorageRecords || storage.Reservations.Count > MaximumStorageRecords))
            throw new InvalidDataException("The bounded storage record inventory is full. Finish existing work and remove only eligible obsolete claims before another reservation; the previous ledger remains readable.");
    }

    private static void CompactStorageState(Ledger state, Change change)
    {
        if (change.Storage is not { } storage) return;
        foreach (var pair in storage.Reservations.Where(pair => pair.Value.Finalized
            && change.Executions.GetValueOrDefault(pair.Value.Reference.ExecutionId)?.Finalized == true).ToArray())
        {
            // An old exact admission receipt may be the only durable provenance.
            // Carry its validated binding into the claim before compacting it.
            foreach (var path in pair.Value.Paths)
                if (storage.Claims.TryGetValue(path, out var claim) && claim.NativeScratchAdmission is null)
                {
                    var admission = ValidateStorageClaim(state, change, new(path, claim.Scratch, claim.Required));
                    if (admission is not null) storage.Claims[path] = claim with { NativeScratchAdmission = admission };
                }
            storage.Reservations.Remove(pair.Key);
        }
        foreach (var claim in storage.Claims.Values.Where(claim => claim.Required && claim.RequiredByExecutionId is not null
            && claim.SupersededByExecutionId is not null).ToArray())
        {
            try
            {
                var replacement = change.Executions[claim.SupersededByExecutionId!].Reference;
                ValidateSupersededClaim(state, change, replacement, claim.Path);
                storage.Claims[claim.Path] = claim with
                {
                    Required = false,
                    RequiredByExecutionId = null,
                    SupersededByExecutionId = null,
                    RetainedWorkReferences = []
                };
            }
            catch (Exception error) when (Recoverable(error)) { /* Unknown or still-needed evidence keeps its original protection. */ }
        }
        if (storage.Claims.Count >= MaximumStorageRecords * 3 / 4) PruneMissingStorageClaims(change, storage.Claims.Keys.ToArray());
    }

    private static void PruneMissingStorageClaims(Change change, IEnumerable<string> paths)
    {
        if (change.Storage is not { } storage) return;
        var pending = storage.Reservations.Values.Where(item => !item.Finalized).SelectMany(item => item.Paths).ToHashSet(StoragePathComparer);
        foreach (var path in paths.Distinct(StoragePathComparer))
        {
            if (!storage.Claims.TryGetValue(path, out var claim) || claim.Required || pending.Contains(path)) continue;
            try { _ = File.GetAttributes(path); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { storage.Claims.Remove(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Unknown is retained, never assumed absent. */ }
        }
    }

    private static void ValidateSupersededClaim(Ledger state, Change change, CisAgentWorkReference reference, string path,
        string? ignoreReservationId = null)
    {
        var storage = change.Storage!;
        var claim = storage.Claims.GetValueOrDefault(path);
        ValidateStorageClaim(state, change, new(path, claim?.Scratch == true));
        if (claim?.Required != true) return;
        if (!MaySupersedeClaim(change, reference, claim))
            throw new InvalidDataException("Another owner's superseded evidence requires the original allocation actor or its delegated cleanup controller.");
        if (StorageGroup(state, change).SelectMany(item => item.Storage!.Reservations)
            .Where(pair => !pair.Value.Finalized && pair.Key != ignoreReservationId)
            .Any(pair => pair.Value.Paths.Any(pending => StorageOverlaps(pending, path))))
            throw new InvalidDataException("Superseded evidence still belongs to pending storage work; retain its protection.");
        var currentRoot = change.Executions[reference.ExecutionId].RootId;
        var blocking = claim.RequiredByExecutionId is { } origin && change.Executions.TryGetValue(origin, out var originating)
            ? change.Executions.Values.Where(item => item.RootId == originating.RootId)
            : change.Executions.Values.Where(item => item.Reference.OwnerTaskId == claim.OwnerTaskId && item.RootId != currentRoot);
        if (blocking.Any(item => item.NativeRestorationRequired || item.RecoveryReason is not null
            || !item.Stopped && (item.RootId != currentRoot || item.LaunchIntent || item.ProcessId is not null)))
            throw new InvalidDataException("Required evidence still supports live, uncertain or unresolved native restoration; supersession cannot release it.");
    }

    private static bool MaySupersedeClaim(Change change, CisAgentWorkReference reference, StorageClaim claim)
    {
        if (claim.OwnerTaskId == reference.OwnerTaskId) return true;
        var controller = change.Executions[reference.ExecutionId].Controller;
        return controller is not null && change.Controllers.Contains(controller, StringComparer.Ordinal)
            && (controller == change.Actor || change.Storage!.Policy.CleanupControllers.Contains(controller, StringComparer.Ordinal));
    }

    private StorageSnapshot SnapshotStorage(CisRepositoryContext authority, string changeId, string taskId,
        IReadOnlyList<CisOwnedStoragePath>? proposed = null, IReadOnlyList<string>? ancestors = null)
    {
        Ledger state;
        using (var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock)))
            state = Load(authority) ?? throw new InvalidDataException("Owned storage needs the existing canonical allocation; no unlimited ceiling was substituted.");
        var change = ChangeFor(state, changeId);
        var owner = new[] { taskId }.Concat(ancestors ?? []).Select(task => change.Bindings.GetValueOrDefault(task)
            ?? (change.Owners.ContainsKey(task) ? task : null)).FirstOrDefault(owner => owner is not null)
            ?? throw new InvalidDataException("Owned storage has no canonical task owner.");
        if (change.Storage is null) throw new InvalidDataException("The owning allocation has only a storage reference. Supply Storage.MaximumBytes and explicit Roots once through cis agent work allocate at "
            + changeId + "/" + taskId + "; native profiles cannot invent the missing ceiling.");
        foreach (var path in proposed ?? []) ValidateStorageClaim(state, change, path);
        var group = StorageGroup(state, change);
        var paths = group.SelectMany(item => PolicyPaths(item.Storage!.Policy))
            .Concat(group.SelectMany(item => item.Storage!.Claims.Keys)).Concat((proposed ?? []).Select(item => Path.GetFullPath(item.Path)))
            .Concat(new[] { CisAgentWorkPaths.Ledger, CisAgentWorkPaths.Adoption, CisAgentWorkPaths.Lock }
                .Select(relative => Path.GetFullPath(Path.Combine(authority.RepositoryPath, relative))))
            .Distinct(StoragePathComparer).ToArray();
        var protectedPaths = group.SelectMany(item => item.Storage!.Claims.Values.Where(claim => claim.Required).Select(claim => claim.Path))
            .Concat(group.SelectMany(item => item.Storage!.Policy.Roots.Where(root => root.Scratch)
                .Select(root => Path.GetFullPath(Path.Combine(root.RepositoryPath, root.Path)))))
            .Concat(group.SelectMany(item => item.Storage!.Reservations.Values.Where(reservation => !reservation.Finalized).SelectMany(reservation => reservation.Paths)))
            .Concat(group.SelectMany(item => item.Storage!.Policy.Roots.Select(root => Path.GetFullPath(Path.Combine(root.RepositoryPath, ".cis/local/artifacts/operation.lock")))))
            .Concat(new[] { CisAgentWorkPaths.Ledger, CisAgentWorkPaths.Adoption, CisAgentWorkPaths.Lock }
                .Select(relative => Path.GetFullPath(Path.Combine(authority.RepositoryPath, relative))))
            .Distinct(StoragePathComparer).ToArray();
        return new(state, change, owner, MeasureStoragePaths(paths), MeasureStoragePaths(protectedPaths), paths, protectedPaths);
    }

    private static void MatchStorageExecution(Ledger state, Change change, CisAgentWorkReference reference, bool requireLive)
    {
        if (reference.AuthorityRepositoryId != state.AuthorityId || reference.AdoptionId != state.AdoptionId
            || !change.Executions.TryGetValue(reference.ExecutionId, out var execution) || execution.Reference != reference
            || requireLive && execution.Finalized)
            throw new InvalidDataException("Storage needs its exact admitted canonical work reference; supplied run IDs cannot allocate capacity.");
    }

    private static Change[] StorageGroup(Ledger state, Change change)
        => state.Changes.Values.Where(item => item is not null && item.Storage is not null && item.StorageReference == change.StorageReference).ToArray();

    private static IEnumerable<string> PolicyPaths(CisOwnedStoragePolicy policy)
        => policy.Roots.Select(root => Path.GetFullPath(Path.Combine(root.RepositoryPath, root.Path)));

    private static NativeScratchAdmission? ValidateStorageClaim(Ledger state, Change change, CisOwnedStoragePath claim)
    {
        if (claim is null || !BoundedText(claim.Path) || !Path.IsPathFullyQualified(claim.Path))
            throw new InvalidDataException("Declare an exact absolute owned output or scratch path.");
        var path = Path.GetFullPath(claim.Path);
        var retained = change.Storage!.Claims.GetValueOrDefault(path);
        var admission = retained?.NativeScratchAdmission;
        var within = WithinStoragePolicy(state, change, path);
        if (!within)
        {
            var name = Path.GetFileName(path);
            if (!claim.Scratch || !change.Storage!.Policy.AllowNativeScratch
                || !System.Text.RegularExpressions.Regex.IsMatch(name, @"\A(?:cis-mutation-[a-f0-9]{32}|cis-[a-f0-9]{6}-[A-Za-z0-9_-]{15})\z"))
                throw new InvalidDataException("This output is outside the explicitly owned CIS roots and supported exact native scratch paths.");
            if (retained is not null)
                admission ??= RecoverNativeScratchAdmission(state, change, retained);
            else if (!StoragePathComparer.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(Path.GetTempPath())))
                throw new InvalidDataException("New native scratch must be an exact supported child of its admitting invocation's temporary directory.");
        }
        if (admission is not null) ValidateNativeScratchAdmission(state, change, path, claim.Scratch, admission);
        if (CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(path)!, path))
            throw new InvalidDataException("Owned storage crosses a linked or unreadable path; its usage is unknown, not zero.");
        return admission;
    }

    private static bool WithinStoragePolicy(Ledger state, Change change, string path)
        => StorageGroup(state, change).SelectMany(item => PolicyPaths(item.Storage!.Policy)).Any(root => StorageWithin(root, path));

    private static NativeScratchAdmission? StorageClaimAdmission(Ledger state, Change change,
        CisAgentWorkReference reference, CisOwnedStoragePath claim)
    {
        var admission = ValidateStorageClaim(state, change, claim);
        var path = Path.GetFullPath(claim.Path);
        if (admission is not null || WithinStoragePolicy(state, change, path)) return admission;
        // Only a new, already validated native claim captures the current context.
        // Historical claims never acquire provenance from the inspecting TEMP.
        admission = new(path, Path.GetDirectoryName(path)!, reference);
        ValidateNativeScratchAdmission(state, change, path, claim.Scratch, admission);
        return admission;
    }

    private static NativeScratchAdmission RecoverNativeScratchAdmission(Ledger state, Change change, StorageClaim claim)
    {
        var references = change.Storage!.Reservations.Values.Where(reservation =>
                reservation.Paths.Contains(claim.Path, StoragePathComparer)
                && reservation.OperationPaths.Contains(claim.Path, StoragePathComparer)
                && reservation.Reference.OwnerTaskId == claim.OwnerTaskId
                && change.Executions.TryGetValue(reservation.Reference.ExecutionId, out var execution)
                && execution.Reference == reservation.Reference && execution.Stage?.NativeOperation is not null)
            .Select(reservation => reservation.Reference).Distinct().ToArray();
        if (references.Length != 1)
            throw new InvalidDataException("Retained native scratch has no unique exact original native admission provenance. Preserve its claim and reservations; restore the original admission evidence before inspection or reconciliation. The current temporary directory cannot supply historical authority.");
        var admission = new NativeScratchAdmission(claim.Path, Path.GetDirectoryName(claim.Path)!, references[0]);
        ValidateNativeScratchAdmission(state, change, claim.Path, claim.Scratch, admission);
        return admission;
    }

    private static void ValidateNativeScratchAdmission(Ledger state, Change change, string path, bool scratch,
        NativeScratchAdmission admission)
    {
        if (!scratch || !change.Storage!.Policy.AllowNativeScratch || !BoundedText(admission.Path)
            || !BoundedText(admission.TemporaryRoot) || !Path.IsPathFullyQualified(admission.Path)
            || !Path.IsPathFullyQualified(admission.TemporaryRoot)
            || !StoragePathComparer.Equals(Path.GetFullPath(admission.Path), path)
            || !StoragePathComparer.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(Path.GetFullPath(admission.TemporaryRoot)))
            || admission.Reference is not { } reference || reference.AuthorityRepositoryId != state.AuthorityId
            || reference.AdoptionId != state.AdoptionId || reference.ChangeId != change.Id
            || !change.Executions.TryGetValue(reference.ExecutionId, out var execution)
            || execution.Reference != reference || execution.Stage?.NativeOperation is null)
            throw new InvalidDataException("Retained native scratch admission provenance does not match its exact path and original canonical native execution. Restore that evidence without changing claims or reservations.");
    }

    private static long MeasureStoragePaths(IEnumerable<string> paths)
    {
        var files = new HashSet<string>(StoragePathComparer);
        long bytes = 0;
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StoragePathComparer))
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            if (CisPathSafety.IsReparsePoint(path) || Directory.Exists(path) && CisPathSafety.ContainsReparsePointInTree(path))
                throw new InvalidDataException("Owned storage contains linked or unreadable material; repair its inventory before expanding.");
            foreach (var file in File.Exists(path) ? new[] { path } : CisPathSafety.EnumerateFiles(path))
                if (files.Add(file)) bytes = checked(bytes + new FileInfo(file).Length);
        }
        return bytes;
    }

    private static bool StorageWithin(string root, string path)
        => StoragePathComparer.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(path))
            || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, CisPathSafety.PlatformComparison);
    private static bool StorageOverlaps(string first, string second) => StorageWithin(first, second) || StorageWithin(second, first);
    private static long RemainingStorage(long maximum, long owned, long reserved, long restoration)
    {
        var afterOwned = owned >= maximum ? 0 : maximum - owned;
        var afterReserved = reserved >= afterOwned ? 0 : afterOwned - reserved;
        return restoration >= afterReserved ? 0 : afterReserved - restoration;
    }

    private static CisOwnedStorageView ViewStorage(StorageSnapshot snapshot, long? reserved = null)
    {
        var policy = snapshot.Change.Storage!.Policy;
        var pending = reserved ?? StorageGroup(snapshot.State, snapshot.Change).Sum(item => item.Storage!.Reservations.Values
            .Where(reservation => !reservation.Finalized).Sum(reservation => checked(reservation.MaximumGrowthBytes + reservation.RecordHeadroomBytes)));
        return new(snapshot.State.AuthorityId, snapshot.Change.Id, snapshot.OwnerTaskId, snapshot.Change.StorageReference,
            policy.MaximumBytes, snapshot.OwnedBytes, snapshot.ProtectedBytes, pending, snapshot.Paths, snapshot.ProtectedPaths,
            Revision(snapshot.State))
        { RestorationBytes = policy.RestorationBytes };
    }

    private static CisOwnedStorageResult StorageResult(StorageSnapshot snapshot, string status, IReadOnlyList<string>? errors = null)
        => new(status, true, ViewStorage(snapshot), errors ?? []);
    private static CisOwnedStorageResult StorageFailure(Exception error)
        => new(error is IOException ? "busy" : "unknown", true, null, [error.Message]);
    private static string StorageHold(StorageSnapshot snapshot, long pending, long restoration, long requested)
        => $"Owned storage prevents expansion: {snapshot.OwnedBytes} owned bytes, {snapshot.ProtectedBytes} protected bytes, {pending} pending bytes, {restoration} restoration bytes, ceiling {snapshot.Change.Storage!.Policy.MaximumBytes}, requested growth {requested}. Inspect cis artifacts plan and use only delegated confirmed cleanup, or resolve the original storage allocation authority; time and attempts are unchanged.";
}
