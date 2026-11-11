using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    internal static NativeStorageReservation? BeginEvidenceMutation(ICisNativeWorkScope? scope,
        ICisOwnedStorageController? controller, IReadOnlyList<string> paths, long maximumGrowthBytes = 0,
        IReadOnlyDictionary<string, IReadOnlyList<CisAgentWorkReference>>? retainedWorkReferences = null)
    {
        if (scope is null) return null;
        Require(controller is not null, "Protected evidence changes require the installed owned-storage authority.");
        Require(paths.Count is > 0 and <= 100, "Evidence changes require one bounded exact path set.");
        var admission = controller.Reserve(scope.AuthorityPath, scope.Reference,
            "evidence-mutation/" + Guid.NewGuid().ToString("N"), maximumGrowthBytes,
            paths.Select(path => new CisOwnedStoragePath(Path.GetFullPath(path))
            {
                MutatesExisting = true,
                RetainedWorkReferences = retainedWorkReferences?.GetValueOrDefault(path)
            }).ToArray());
        Require(admission.Allowed, string.Join(" ", admission.Result.Errors));
        return new(scope.AuthorityPath, scope.Reference, admission, 0, controller);
    }
}
