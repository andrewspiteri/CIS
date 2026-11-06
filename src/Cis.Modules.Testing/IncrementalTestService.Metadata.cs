using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private CisNativeWorkAdmission AdmitMetadata(IncrementalTestOptions options, string operation)
    {
        Require(options.Work?.RecoveryOfExecutionId is null,
            "Metadata admission cannot use --recovery-of or spend native restoration reserves. Restore through the original native run route.");
        var repository = Path.GetFullPath(options.RepositoryPath);
        var profile = ReadJson<IncrementalTestProfile>(Safe(repository, options.ProfilePath), 262144);
        Require(Slug(profile.Id), "Incremental profile id is invalid.");
        var statePath = Safe(repository, LocalRoot + "/" + profile.Id + "/state.json");
        var state = File.Exists(statePath) ? ReadJson<IncrementalTestState>(statePath, 16 * 1024 * 1024) : null;
        var reference = state?.WorkReference ?? state?.ActiveWorkReference;
        var retained = state?.WorkAssociation ?? (reference is null ? null
            : new CisNativeWorkOptions(reference.ChangeId, reference.TaskId, AuthorityPath: state?.WorkAuthorityPath));
        var requested = options.Work;
        var association = new CisNativeWorkOptions(requested?.ChangeId ?? retained?.ChangeId,
            requested?.TaskId ?? retained?.TaskId, requested?.ParentExecutionId,
            requested?.AuthorityPath ?? retained?.AuthorityPath, requested?.Actor ?? retained?.Actor);
        return MutationRunService.AdmitNative(_nativeWork, new(repository,
            "native/incremental/metadata/" + Guid.NewGuid().ToString("N"), operation, 30, association));
    }

    private Input MetadataInput(IncrementalTestOptions options, ICisNativeWorkScope? scope)
    {
        var input = Read(options) with { Work = scope, OwnedStorage = _ownedStorage };
        if (scope is not null)
        {
            Require(_ownedStorage is not null, "Governed metadata publication requires the installed owned-storage controller.");
            var storage = _ownedStorage.Inspect(scope.AuthorityPath,
                new(scope.Reference.ChangeId, scope.Reference.TaskId, AuthorityPath: scope.AuthorityPath, Actor: scope.Actor));
            Require(storage.Allowed, string.Join(" ", storage.Errors));
        }
        return input;
    }
}
