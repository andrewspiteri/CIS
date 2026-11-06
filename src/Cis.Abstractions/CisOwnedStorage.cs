namespace Cis.Abstractions;

/// <summary>A known CIS root or explicit count-only build/scratch root in a registered owned repository.</summary>
public sealed record CisOwnedStorageRoot(string RepositoryPath, string Path)
{
    /// <summary>Explicit count-only build/scratch ownership; never retention deletion authority.</summary>
    public bool Scratch { get; init; }
}

/// <summary>Finite storage authority supplied at the existing allocation boundary, not by a test profile.</summary>
public sealed record CisOwnedStoragePolicy(long MaximumBytes, IReadOnlyList<CisOwnedStorageRoot> Roots)
{
    public IReadOnlyList<string> CleanupControllers { get; init; } = [];
    public bool AllowNativeScratch { get; init; } = true;
    public long RestorationBytes { get; init; }
}

/// <summary>Exact additional native output/scratch ownership; scratch never names a whole temporary directory.</summary>
public sealed record CisOwnedStoragePath(string Path, bool Scratch = false, bool Required = false);

public sealed record CisOwnedStorageView(string AuthorityRepositoryId, string ChangeId, string OwnerTaskId,
    string CeilingReference, long MaximumBytes, long OwnedBytes, long ProtectedBytes, long ReservedBytes,
    IReadOnlyList<string> OwnedPaths, IReadOnlyList<string> ProtectedPaths, string Revision)
{
    public long RemainingBytes
    {
        get
        {
            var afterOwned = MaximumBytes - Math.Min(MaximumBytes, OwnedBytes);
            var afterPending = ReservedBytes >= afterOwned ? 0 : afterOwned - ReservedBytes;
            return Math.Max(0, afterPending - RestorationBytes);
        }
    }
    public long RestorationBytes { get; init; }
    public string Coverage { get; init; } = "Regular-file bytes in declared CIS roots and exact owned scratch; separate physical copies count separately. Full pending growth and bounded record headroom remain conservatively reserved until settlement. Filesystem allocation metadata, machine-wide and instantaneous enforcement are outside this measurement.";
}

public sealed record CisOwnedStorageResult(string Status, bool Required, CisOwnedStorageView? Storage,
    IReadOnlyList<string> Errors)
{
    public bool Allowed => Errors.Count == 0 && Status is not ("held" or "busy" or "unknown");
    public int ExitCode => Allowed ? 0 : 4;
}

public sealed record CisOwnedStorageAdmission(CisOwnedStorageResult Result, string? ReservationId, long MaximumGrowthBytes)
{
    public bool Allowed => Result.Allowed && (ReservationId is not null || !Result.Required);
}

/// <summary>Storage reservations share the existing canonical work ledger and its transaction lock.</summary>
public interface ICisOwnedStorageController
{
    CisOwnedStorageResult Inspect(string repositoryPath, CisNativeWorkOptions? association = null);
    CisOwnedStorageAdmission Reserve(string repositoryPath, CisAgentWorkReference reference, string operationId,
        long maximumGrowthBytes, IReadOnlyList<CisOwnedStoragePath>? claims = null,
        bool cleanup = false, bool clampToRemaining = false, string? actor = null);
    CisOwnedStorageResult Settle(string repositoryPath, CisAgentWorkReference reference, string reservationId,
        bool safelyStopped, IReadOnlyList<string>? requiredPaths = null, IReadOnlyList<string>? supersededRequiredPaths = null);
}
