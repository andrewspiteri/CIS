using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    internal sealed record NativeStorageReservation(string AuthorityPath, CisAgentWorkReference Reference,
        CisOwnedStorageAdmission Admission, long InitialNativeBytes, ICisOwnedStorageController Controller) : IDisposable
    {
        internal bool SafelyStopped { get; set; } = true;
        internal IReadOnlyList<string>? RequiredPaths { get; set; }
        internal IReadOnlyList<string>? SupersededRequiredPaths { get; set; }
        internal bool SettlementAttempted { get; set; }
        public void Dispose()
        {
            if (SettlementAttempted) return;
            SettlementAttempted = true;
            Controller.Settle(AuthorityPath, Reference, Admission.ReservationId!, SafelyStopped, RequiredPaths, SupersededRequiredPaths);
        }
    }

    internal static NativeStorageReservation? BeginNativeStorage(string repository, string output, string storage,
        MutationRunProfile profile, ICisNativeWorkScope? scope, ICisOwnedStorageController? controller)
    {
        if (scope is null) return null;
        Require(controller is not null, "Governed native work requires the installed owned-storage controller and its finite allocation policy.");
        var initial = TreeBytes(storage);
        var growth = profile.MaxRetainedBytes - initial;
        Require(growth > 0, "The native retained-storage limit has no capacity for another attempt.");
        var admission = controller.Reserve(scope.AuthorityPath, scope.Reference, scope.Reference.ExecutionId,
            growth, [new(output) { IncludeRepositoryWrites = true }], clampToRemaining: true);
        Require(admission.Allowed, string.Join(" ", admission.Result.Errors));
        return new(scope.AuthorityPath, scope.Reference, admission, initial, controller);
    }

    internal static MutationRunProfile StorageLimits(MutationRunProfile profile, NativeStorageReservation? reservation,
        long additionalScratchBytes = 0)
    {
        if (reservation is null) return profile;
        var growth = reservation.Admission.MaximumGrowthBytes - additionalScratchBytes;
        Require(growth > 0, "The owned storage ceiling cannot fit the trusted native preparation and output.");
        return profile with
        {
            MaxRunBytes = Math.Min(profile.MaxRunBytes, growth),
            MaxRetainedBytes = Math.Min(profile.MaxRetainedBytes, checked(reservation.InitialNativeBytes + growth))
        };
    }

    internal static CisOwnedStorageResult? SettleNativeStorage(NativeStorageReservation? reservation,
        ICisOwnedStorageController? controller, bool safelyStopped, IReadOnlyList<string>? requiredPaths = null,
        IReadOnlyList<string>? supersededRequiredPaths = null)
    {
        if (reservation is null) return null;
        reservation.SafelyStopped = safelyStopped;
        reservation.RequiredPaths = requiredPaths;
        reservation.SupersededRequiredPaths = supersededRequiredPaths;
        reservation.SettlementAttempted = true;
        return controller!.Settle(reservation.AuthorityPath, reservation.Reference,
            reservation.Admission.ReservationId!, safelyStopped, requiredPaths, supersededRequiredPaths);
    }

    internal static void OwnedJson<T>(string path, T value, string repository, ICisNativeWorkScope? scope,
        ICisOwnedStorageController? controller, IReadOnlyList<string>? additionalRequiredPaths = null,
        IReadOnlyList<string>? supersededRequiredPaths = null,
        IReadOnlyList<CisAgentWorkReference>? retainedWorkReferences = null)
    {
        if (scope is null) { AtomicJson(path, value); return; }
        Require(controller is not null, "Governed native metadata requires the owned-storage controller.");
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Json).LongLength;
        var admission = controller.Reserve(scope.AuthorityPath, scope.Reference,
            scope.Reference.ExecutionId + "/metadata/" + Guid.NewGuid().ToString("N"), bytes,
            [new(path) { MutatesExisting = true, RetainedWorkReferences = retainedWorkReferences }]);
        Require(admission.Allowed, string.Join(" ", admission.Result.Errors));
        var published = false;
        try { AtomicJson(path, value); published = true; }
        finally
        {
            var settled = controller.Settle(scope.AuthorityPath, scope.Reference, admission.ReservationId!, true,
                published ? new[] { path }.Concat(additionalRequiredPaths ?? []).ToArray() : null,
                published ? supersededRequiredPaths : null);
            Require(settled.Allowed, string.Join(" ", settled.Errors));
        }
    }
}
