using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Artifacts;

public sealed partial class ArtifactRetentionService
{
    private readonly AsyncLocal<ArtifactWork?> _artifactWork = new();
    private sealed record ArtifactWork(ICisNativeWorkScope Scope, CisNativeWorkOptions Association,
        string ReservationId, string RepositoryPath, Stopwatch Monitor, string? OperationId = null,
        long MaximumGrowthBytes = 0, bool Cleanup = false);

    public ArtifactOperationResult Compact(string repositoryPath, string family, bool confirmed, CisNativeWorkOptions? association = null)
        => GovernedOperation(repositoryPath, "compact", association, confirmed,
            () => EstimateGrowth(Inventory(repositoryPath, family, association), archive: true),
            () => CompactCore(repositoryPath, family, confirmed), family);

    public ArtifactOperationResult Clean(string repositoryPath, string family, bool confirmed, CisNativeWorkOptions? association = null)
        => GovernedOperation(repositoryPath, "clean", association, confirmed,
            () => EstimateGrowth(Inventory(repositoryPath, family, association), archive: false),
            () => CleanCore(repositoryPath, family, confirmed), family);

    public ArtifactOperationResult Retrieve(string repositoryPath, string archiveId, string? entry = null, CisNativeWorkOptions? association = null)
        => GovernedOperation(repositoryPath, "retrieve", association, true,
            () => EstimateArchiveGrowth(repositoryPath, archiveId, entry, staging: false),
            () => RetrieveCore(repositoryPath, archiveId, entry));

    public ArtifactOperationResult Restore(string repositoryPath, string archiveId, bool confirmed, CisNativeWorkOptions? association = null)
        => GovernedOperation(repositoryPath, "restore", association, confirmed,
            () => EstimateArchiveGrowth(repositoryPath, archiveId, null, staging: true),
            () => RestoreCore(repositoryPath, archiveId, confirmed),
            destinations: () => RestoreDestinationPaths(repositoryPath, archiveId));

    private ArtifactOperationResult GovernedOperation(string repositoryPath, string operation, CisNativeWorkOptions? association,
        bool confirmed, Func<long> growth, Func<ArtifactOperationResult> execute, string? family = null,
        Func<IReadOnlyList<string>>? destinations = null)
    {
        if (!confirmed) return execute(); // Existing confirmation semantics make no writes.
        var nativeWork = NativeWorkController;
        var ownedStorage = OwnedStorageController;
        if (nativeWork is null)
            return CisNativeWorkEnvironment.RequiresController(repositoryPath, association)
                ? Failed(["Governed artifact expansion needs the composed native work/storage authority. Supply its canonical association; no unaccounted write was started."])
                : execute() with { OwnedStorage = new("unaccounted", false, null, []) };
        var admission = nativeWork.Admit(new(repositoryPath, "artifacts/" + operation + "/" + Guid.NewGuid().ToString("N"),
            "artifacts-" + operation, 3600, association));
        if (!admission.Allowed) return Failed(admission.Errors);
        if (!admission.Required || admission.Scope is null)
            return execute() with { OwnedStorage = new("unaccounted", false, null, []) };
        var scope = admission.Scope;
        var boundAssociation = new CisNativeWorkOptions(scope.Reference.ChangeId, scope.Reference.TaskId,
            AuthorityPath: scope.AuthorityPath, Actor: scope.Actor);
        CisOwnedStorageAdmission? reserved = null;
        CisOwnedStorageResult? settled = null;
        CisAgentWorkResult? finished = null;
        ArtifactOperationResult? outcome = null;
        var attempted = false;
        try
        {
            if (ownedStorage is null) throw new InvalidDataException("The composed owned-storage authority is unavailable; no storage ceiling was inferred from retention policy.");
            _artifactWork.Value = new(scope, boundAssociation, "", repositoryPath, Stopwatch.StartNew());
            var maximumGrowth = growth();
            var management = Path.GetFullPath(Path.Combine(repositoryPath, StateRoot));
            reserved = ownedStorage.Reserve(scope.AuthorityPath, scope.Reference, operation, maximumGrowth,
                [new(management)], cleanup: operation is "clean" or "compact" or "restore", actor: scope.Actor);
            if (!reserved.Allowed) throw new InvalidDataException(string.Join(" ", reserved.Result.Errors));
            var admittedStorage = reserved.Result.Storage
                ?? throw new InvalidDataException("The admitted artifact growth has no readable owned-storage inventory; unknown usage is not zero.");
            if (admittedStorage.OwnedBytes < 0 || admittedStorage.MaximumBytes <= 0 || reserved.MaximumGrowthBytes < 0)
                throw new InvalidDataException("The admitted artifact storage bounds are invalid; no expansion was started.");
            if (reserved.Operation is null)
                throw new InvalidDataException("The artifact operation has no attributed storage domain; unknown usage is not zero.");
            _artifactWork.Value = new(scope, boundAssociation, reserved.ReservationId!, repositoryPath, Stopwatch.StartNew(),
                operation, reserved.MaximumGrowthBytes, operation is "clean" or "compact" or "restore");
            CheckArtifactWork(force: true);
            Directory.CreateDirectory(management);
            // One existing native output lease also excludes retention while Testing writes.
            using var artifactLease = new FileStream(Path.Combine(management, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            FileStream? nativeLease = null;
            try
            {
                var targets = destinations?.Invoke() ?? [];
                var testingRoot = Path.GetFullPath(Path.Combine(repositoryPath, ".cis/local/testing"));
                if (targets.Any(path => UnderStoragePath(testingRoot, path))
                    || family is not null && Inventory(repositoryPath, family, boundAssociation).Entries.Any(item =>
                        item.Path == ".cis/local/testing" || item.Path.StartsWith(".cis/local/testing/", StringComparison.Ordinal)))
                {
                    var nativeLock = Path.Combine(repositoryPath, ".cis/local/testing/mutation/run.lock");
                    var lockClaim = ownedStorage.Reserve(scope.AuthorityPath, scope.Reference, operation, maximumGrowth,
                        [new(nativeLock)], cleanup: operation is "clean" or "compact" or "restore", actor: scope.Actor);
                    if (!lockClaim.Allowed) throw new InvalidDataException(string.Join(" ", lockClaim.Result.Errors));
                    Directory.CreateDirectory(Path.GetDirectoryName(nativeLock)!);
                    nativeLease = new(nativeLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                if (targets.Count > 0)
                {
                    // Inspect under the output leases before adding our own target claims,
                    // which will themselves become protected while restoration writes.
                    var ownership = ownedStorage.Inspect(repositoryPath, boundAssociation);
                    if (!ownership.Allowed) throw new InvalidDataException(string.Join(" ", ownership.Errors));
                    if (ownership.Storage is null || targets.Any(path => OwnedPathProtected(path, ownership)))
                        throw new InvalidDataException("Restore destination is outside the current owned roots or contains protected current evidence; resolve its original storage authority before restoring.");
                    var targetClaims = ownedStorage.Reserve(scope.AuthorityPath, scope.Reference, operation, maximumGrowth,
                        targets.Select(path => new CisOwnedStoragePath(path)).ToArray(), cleanup: true, actor: scope.Actor);
                    if (!targetClaims.Allowed) throw new InvalidDataException(string.Join(" ", targetClaims.Result.Errors));
                }
                attempted = true;
                outcome = execute();
            }
            finally { nativeLease?.Dispose(); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            outcome = outcome is null ? Failed([error.Message]) with { Status = attempted ? "partial" : "failed", Applied = attempted }
                : outcome with { Status = "partial", Errors = outcome.Errors.Append(error.Message).ToArray() };
        }
        finally
        {
            try
            {
                if (reserved?.ReservationId is not null) CheckArtifactWork(force: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
                or OverflowException or JsonException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                outcome = outcome is null ? Failed([error.Message]) with { Status = attempted ? "partial" : "failed", Applied = attempted }
                    : outcome with { Status = "partial", Errors = outcome.Errors.Append(error.Message).ToArray() };
            }
            _artifactWork.Value = null;
            if (reserved?.ReservationId is { } reservation)
                settled = ownedStorage!.Settle(scope.AuthorityPath, scope.Reference, reservation, safelyStopped: true);
            finished = scope.Finish(outcome?.Status ?? "held", "Artifact " + operation + " completed under its canonical owner.", outcome?.Outputs ?? []);
            scope.Dispose();
        }
        outcome ??= Failed(["The artifact operation did not retain an outcome."]);
        var finalStorage = ownedStorage?.Inspect(repositoryPath, boundAssociation) ?? settled;
        return outcome with
        {
            Work = finished,
            OwnedStorage = finalStorage,
            Errors = outcome.Errors.Concat(settled?.Errors ?? []).Concat(finished?.Errors ?? [])
                .Concat(finalStorage?.Errors ?? []).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private CisOwnedStorageResult InspectOwnedStorage(string repositoryPath, CisNativeWorkOptions? association)
        => OwnedStorageController?.Inspect(repositoryPath, association)
            ?? (CisNativeWorkEnvironment.RequiresController(repositoryPath, association)
                ? new("unknown", true, null, ["Owned storage cannot be measured without the composed authority and canonical association; missing ownership is not zero usage."])
                : new("unaccounted", false, null, []));

    private static LocalArtifactEntry ProtectOwnedEntry(CisRepositoryContext context, LocalArtifactEntry entry, CisOwnedStorageResult owned)
    {
        var full = Path.GetFullPath(Path.Combine(context.RepositoryPath, entry.Path));
        return OwnedPathProtected(full, owned) ? entry with { Candidate = false, Action = "retain", Protected = true } : entry;
    }

    private static bool OwnedPathProtected(string full, CisOwnedStorageResult owned)
    {
        if (!owned.Required) return false;
        var known = owned.Storage is { } storage && storage.OwnedPaths.Any(path => UnderStoragePath(path, full));
        return !known || owned.Storage!.ProtectedPaths.Any(path => UnderStoragePath(full, path) || UnderStoragePath(path, full));
    }

    private static bool UnderStoragePath(string root, string path)
        => Path.TrimEndingDirectorySeparator(root).Equals(Path.TrimEndingDirectorySeparator(path), CisPathSafety.PlatformComparison)
            || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, CisPathSafety.PlatformComparison);

    private static long EstimateGrowth(ArtifactOperationResult plan, bool archive)
    {
        if (plan.Errors.Count > 0) throw new InvalidDataException(string.Join(" ", plan.Errors));
        var candidates = plan.Entries.Where(entry => entry.Candidate).ToArray();
        if (candidates.Length == 0) return 0;
        var metadata = checked(JsonSerializer.SerializeToUtf8Bytes(candidates, JsonOptions).LongLength * 3 + 65536);
        if (!archive) return metadata;
        if (plan.RepositoryPath is null) throw new InvalidDataException("Artifact growth requires its original repository inventory.");
        long headers = 0;
        var entries = 0;
        foreach (var candidate in candidates)
        {
            if (!CisPathSafety.TryResolveUnderRoot(plan.RepositoryPath, candidate.Path, out var full)
                || CisPathSafety.ContainsReparsePoint(plan.RepositoryPath, full))
                throw new InvalidDataException("An eligible artifact path changed or crosses a link before archive admission.");
            if (candidate.Kind == "file") AddHeader(candidate.Path);
            else
            {
                // Match the existing archiver's eligible-tree enumeration, including
                // its single entry for an empty directory. No unrelated roots are scanned.
                var files = CisPathSafety.EnumerateFiles(full).ToArray();
                if (files.Length == 0) AddHeader(candidate.Path.TrimEnd('/') + "/");
                foreach (var file in files) AddHeader(Path.GetRelativePath(plan.RepositoryPath, file).Replace('\\', '/'));
            }
        }
        return checked(candidates.Sum(item => item.SizeBytes) * 2 + headers + metadata);

        void AddHeader(string name)
        {
            if (++entries > MaximumArchiveEntries) throw new InvalidDataException("Archive entry count exceeds its existing configured limit.");
            // Both local and central headers retain the UTF-8 name. The fixed
            // allowance also covers descriptors, ZIP64 fields and per-file deflate overhead.
            headers = checked(headers + 512 + 2L * System.Text.Encoding.UTF8.GetByteCount(name));
        }
    }

    private long EstimateArchiveGrowth(string repositoryPath, string archiveId, string? entry, bool staging)
    {
        var context = Resolve(repositoryPath, out var errors) ?? throw new InvalidDataException(string.Join(" ", errors));
        var manifest = FindArchive(context, archiveId, errors) ?? throw new InvalidDataException(string.Join(" ", errors));
        var selected = SelectEntries(manifest, entry, errors);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        return checked(selected.Sum(item => item.SizeBytes) * (staging ? 2 : 1) + 65536);
    }

    private IReadOnlyList<string> RestoreDestinationPaths(string repositoryPath, string archiveId)
    {
        var context = Resolve(repositoryPath, out var errors) ?? throw new InvalidDataException(string.Join(" ", errors));
        var manifest = FindArchive(context, archiveId, errors) ?? throw new InvalidDataException(string.Join(" ", errors));
        var targets = manifest.Entries.Select(entry => FullLocalPath(context, entry.Path, errors)).ToArray();
        if (errors.Count > 0 || targets.Any(path => path is null)) throw new InvalidDataException(string.Join(" ", errors));
        return targets.Select(path => path!).ToArray();
    }

    private void CheckArtifactWork() => CheckArtifactWork(force: false);

    private void CheckArtifactWork(bool force)
    {
        if (_artifactWork.Value is not { } active) return;
        if (active.Scope.Remaining <= TimeSpan.Zero) throw new InvalidDataException("The admitted artifact operation window is exhausted; retain its evidence and resume under the same owner.");
        if (active.ReservationId.Length == 0) return; // Read-only estimation precedes the storage reservation.
        // Intermediate checks are sampled. Settlement forces a fresh inventory even
        // when the operation completed inside this interval.
        if (!force && active.Monitor.ElapsedMilliseconds < 1000) return;
        active.Monitor.Restart();
        // Refresh the SAME immutable reservation. Another admitted operation's
        // disjoint writes count globally, but never spend this operation's grant.
        var current = OwnedStorageController!.Reserve(active.Scope.AuthorityPath, active.Scope.Reference,
            active.OperationId!, active.MaximumGrowthBytes, cleanup: active.Cleanup, actor: active.Scope.Actor);
        if (!current.Allowed || current.ReservationId != active.ReservationId || current.Result.Storage is not { } storage
            || current.Operation is not { } operation || storage.OwnedBytes < 0 || storage.MaximumBytes <= 0
            || operation.InitialBytes < 0 || operation.CurrentBytes < 0 || operation.MaximumGrowthBytes < 0)
            throw new InvalidDataException("Owned artifact inventory or its admitted growth domain is unreadable; unknown usage is not zero. " + string.Join(" ", current.Result.Errors));
        if (storage.OwnedBytes > storage.MaximumBytes)
            throw new InvalidDataException("The global owned artifact ceiling was exceeded; all known owned copies remain counted.");
        if (operation.CurrentBytes > checked(operation.InitialBytes + operation.MaximumGrowthBytes))
            throw new InvalidDataException("Owned artifact growth exceeded this operation's admitted growth; other operations' grants and restoration capacity remain reserved.");
    }
}
