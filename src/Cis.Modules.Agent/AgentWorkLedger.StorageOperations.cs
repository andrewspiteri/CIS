using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    internal bool NativeStorageSettled(CisRepositoryContext authority, CisAgentWorkReference reference)
    {
        using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
        var state = Load(authority) ?? throw new InvalidDataException("The original native accounting state is missing.");
        var change = ChangeFor(state, reference.ChangeId);
        MatchStorageExecution(state, change, reference, requireLive: false);
        var executions = NativeSubtree(change, reference.ExecutionId).Select(item => item.Reference.ExecutionId).ToHashSet(StringComparer.Ordinal);
        return change.Storage?.Reservations.Values.Any(item => !item.Finalized
            && executions.Contains(item.Reference.ExecutionId)) != true;
    }

    private static void ValidateStorageMutation(Ledger state, Change change, CisAgentWorkReference reference,
        string path, string? ignoreReservation = null)
    {
        // Outside-root scratch can be mutated only through its existing exact
        // admission; a caller-selected scratch flag cannot create that authority.
        ValidateStorageClaim(state, change, new(path, Scratch: change.Storage!.Claims.GetValueOrDefault(path)?.Scratch == true));
        foreach (var repository in StorageGroup(state, change).SelectMany(item => item.Storage!.Policy.Roots)
            .Select(item => item.RepositoryPath).Distinct(StoragePathComparer))
            if (new[] { CisAgentWorkPaths.Ledger, CisAgentWorkPaths.Adoption, CisAgentWorkPaths.Lock }
                .Any(control => StorageOverlaps(Path.GetFullPath(Path.Combine(repository, control)), path)))
                throw new InvalidDataException("Canonical work accounting controls cannot be replaced or removed through evidence mutation authority.");
        var execution = change.Executions[reference.ExecutionId];
        var root = execution.RootId;
        // Admission has already verified this exact original recipe and owner.
        // Its restoration may update its own evidence without superseding another lineage.
        var restorationRoot = change.Executions[root].RestorationOfExecutionId is { } originalId
            && change.Executions.TryGetValue(originalId, out var original)
            && original.Reference.OwnerTaskId == reference.OwnerTaskId ? original.RootId : null;
        foreach (var grouped in StorageGroup(state, change))
        {
            foreach (var pending in grouped.Storage!.Reservations.Where(pair => !pair.Value.Finalized && pair.Key != ignoreReservation))
            {
                var sameRoot = grouped == change && grouped.Executions[pending.Value.Reference.ExecutionId].RootId == root;
                if (!sameRoot && pending.Value.Paths.Any(owned => StorageOverlaps(owned, path)))
                    throw new InvalidDataException("Protected evidence belongs to another pending operation. Retain its bytes until that exact operation is safely settled.");
            }
            foreach (var claim in grouped.Storage.Claims.Values.Where(item => item.Required && StorageOverlaps(item.Path, path)))
            {
                var sameRoot = grouped == change && claim.RequiredByExecutionId is { } origin
                    && grouped.Executions.TryGetValue(origin, out var source)
                    && (source.RootId == root || claim.OwnerTaskId == reference.OwnerTaskId && source.RootId == restorationRoot);
                if (sameRoot) continue;
                if (grouped != change)
                    throw new InvalidDataException("Required evidence belongs to another change's canonical storage owner. Retain its original bytes and use that existing owner for authorized supersession.");
                try { ValidateSupersededClaim(state, grouped, reference, claim.Path, ignoreReservation); }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException("Required evidence cannot be changed by this execution. Retain its original bytes; use its authorized owner or existing delegated supersession controller. " + error.Message);
                }
            }
        }
    }

    private static (string[] Paths, string[] Accounting, long Initial, long Current) StorageOperation(
        CisRepositoryContext authority, Ledger state, Change change, CisAgentWorkReference reference,
        IReadOnlyList<CisOwnedStoragePath>? claims, StorageReservation? previous = null)
    {
        if (previous is { InitialOperationBytes: null })
            throw new InvalidDataException("The original storage operation has no recorded growth-domain baseline. Preserve its reservation and settle or reconcile that original work before expansion; no new allowance was created.");
        var paths = (previous?.OperationPaths ?? []).Concat((claims ?? []).Select(item => Path.GetFullPath(item.Path))).ToList();
        var execution = change.Executions[reference.ExecutionId];
        if (claims?.Any(item => item.IncludeRepositoryWrites) == true)
        {
            if (execution.Stage?.NativeOperation is null || execution.Stage.RepositoryPath is not { } target)
                throw new InvalidDataException("Declared native writable roots require the immutable admitted target repository.");
            paths.AddRange(StorageGroup(state, change).SelectMany(item => item.Storage!.Policy.Roots)
                .Where(root => StoragePathComparer.Equals(Path.GetFullPath(root.RepositoryPath), Path.GetFullPath(target)))
                .Select(root => Path.GetFullPath(Path.Combine(root.RepositoryPath, root.Path))));
        }
        var repositories = StorageGroup(state, change).SelectMany(item => item.Storage!.Policy.Roots)
            .Select(item => item.RepositoryPath).Append(authority.RepositoryPath).Distinct(StoragePathComparer);
        // Maintained authority/provider records are globally counted and have their
        // own reserved headroom; they are not bytes produced by a native test job.
        var accounting = repositories.Select(repository => Path.GetFullPath(Path.Combine(repository, ".cis/local/agents")))
            .Concat(previous?.AccountingPaths ?? []).Distinct(StoragePathComparer).ToArray();
        var normalized = paths.Distinct(StoragePathComparer).Where(path => !paths.Any(parent =>
                !StoragePathComparer.Equals(path, parent) && StorageWithin(parent, path)))
            .Where(path => !accounting.Any(parent => StorageWithin(parent, path))).Order(StringComparer.Ordinal).ToArray();
        var added = MeasureOperationPaths(normalized, accounting.Concat(previous?.OperationPaths ?? []));
        var initial = checked((previous?.InitialOperationBytes ?? 0) + added);
        return (normalized, accounting, initial, MeasureOperationPaths(normalized, accounting));
    }

    private static long MeasureOperationPaths(IEnumerable<string> paths, IEnumerable<string> excluded)
    {
        var excludedPaths = excluded.ToArray();
        var files = new HashSet<string>(StoragePathComparer);
        long bytes = 0;
        foreach (var path in paths)
        {
            if (excludedPaths.Any(parent => StorageWithin(parent, path))) continue;
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            if (CisPathSafety.IsReparsePoint(path) || Directory.Exists(path) && CisPathSafety.ContainsReparsePointInTree(path))
                throw new InvalidDataException("An operation growth domain contains linked or unreadable storage.");
            foreach (var file in File.Exists(path) ? new[] { path } : CisPathSafety.EnumerateFiles(path))
                if (!excludedPaths.Any(parent => StorageWithin(parent, file)) && files.Add(file))
                    bytes = checked(bytes + new FileInfo(file).Length);
        }
        return bytes;
    }

    private static void ValidateOperationOverlap(Ledger state, Change change, CisAgentWorkReference reference,
        IReadOnlyList<string> paths, IReadOnlyList<string> accounting, string? ignoreReservation = null)
    {
        var root = change.Executions[reference.ExecutionId].RootId;
        foreach (var grouped in StorageGroup(state, change))
            foreach (var pair in grouped.Storage!.Reservations.Where(pair => !pair.Value.Finalized && pair.Key != ignoreReservation))
            {
                if (grouped == change && grouped.Executions[pair.Value.Reference.ExecutionId].RootId == root) continue;
                var other = pair.Value.OperationPaths.Count > 0 ? pair.Value.OperationPaths : pair.Value.Paths;
                if (paths.Any(path => other.Any(candidate => StorageOverlaps(path, candidate)
                    && !accounting.Concat(pair.Value.AccountingPaths).Any(excluded => StorageWithin(excluded,
                        path.Length >= candidate.Length ? path : candidate)))))
                    throw new InvalidDataException("Another admitted operation owns an overlapping writable storage domain. Finish its existing lease before writing the shared location; disjoint owned domains remain independent.");
            }
    }

    private static void ValidateRequiredPromotion(Ledger state, Change change, CisAgentWorkReference reference,
        string path, StorageReservation reservation, string reservationId)
    {
        ValidateOperationOverlap(state, change, reference, [path], reservation.AccountingPaths, reservationId);
        var root = change.Executions[reference.ExecutionId].RootId;
        foreach (var grouped in StorageGroup(state, change))
            foreach (var pending in grouped.Storage!.Reservations.Where(pair => !pair.Value.Finalized && pair.Key != reservationId))
            {
                if (grouped == change && grouped.Executions[pending.Value.Reference.ExecutionId].RootId == root) continue;
                // Exact data claims still protect their bytes when an individual
                // growth domain excludes shared accounting from its byte total.
                if (pending.Value.Paths.Any(candidate => StorageOverlaps(candidate, path)))
                    throw new InvalidDataException("Required evidence promotion is pending another operation's existing writable lease. Preserve this reservation and retry settlement after that exact operation safely settles; no required claim was awarded.");
            }
    }

    private static CisOwnedStorageAdmission OperationAdmission(CisOwnedStorageResult result, string id, long growth,
        (string[] Paths, string[] Accounting, long Initial, long Current) operation)
        => new(result, id, growth)
        { Operation = new(operation.Paths, operation.Initial, operation.Current, growth) { AccountingPaths = operation.Accounting } };
}
