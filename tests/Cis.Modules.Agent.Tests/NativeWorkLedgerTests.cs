using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void NativeWork_VerifiedPartialCancellationClearsStoppedRootUncertaintyWithoutClearingItsRecipe()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(180, 3, 30, [new(identity.TaskId, 180, 3, 30)],
            "approved native allocation", "approved native storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(2 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local/native")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Retain partial native cancellation evidence.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture", "mutation", f.Context.RepositoryId)
        { NativeOperation = "mutation", NativeRecoveryBasis = "sha256:retained-native-restoration-recipe" };
        var root = f.Ledger.Admit(f.Context, identity, "native/partial-root", 60, 180, stage: stage, controller: "Controller");
        Assert.True(root.Admitted);
        var child = f.Ledger.Admit(f.Context, identity, "native/partial-child", 30, 180, root.Reference!.ExecutionId,
            stage: stage, controller: "Controller", nativeChildContinuation: true);
        Assert.True(child.Admitted);
        var output = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/partial-diagnostic"));
        var reservation = f.Ledger.ReserveStorage(f.Context, child.Reference!, "native", 64 * 1024, [new(output)], false, false, "Controller");
        Assert.True(reservation.Allowed);
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "retained.log"), "necessary interrupted diagnostic");
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, child.Reference!).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, root.Reference, "RecoveryRequired", true).ExitCode);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(0, f.Ledger.RequestNativeCancellation(f.Context, root.Reference, before.Revision, "Controller", "Record partial native cancellation.").ExitCode);
        f.Ledger.MarkRecoveryUncertain(f.Context, root.Reference, "Stopped root awaits its unresolved child's containment proof.");
        f.Ledger.MarkRecoveryUncertain(f.Context, child.Reference!, "Child awaits containment proof.");
        f.Time = f.Time.AddSeconds(31);
        var current = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        // The actual Host test exercises OS verification. This unit exercises only
        // atomic settlement after that internal boundary supplies the exact snapshot.
        var snapshot = f.Ledger.NativeExecutionTree(f.Context, root.Reference);
        var result = f.Ledger.Disposition(f.Context, identity, new("reconcile", current.Revision)
        { ExecutionId = root.Reference.ExecutionId, Evidence = ["fixture:verified-native-tree-snapshot"] }, "Controller",
            "Reconcile the verified partial native tree without resetting its original recovery recipe.", verifiedNativeTree: snapshot);
        Assert.Equal(0, result.ExitCode);
        Assert.All(result.Work!.Executions, execution => { Assert.True(execution.Finalized); Assert.Null(execution.RecoveryReason); });
        Assert.Equal(60_000, result.Work.Task!.ConsumedMilliseconds);
        Assert.Equal(0, result.Work.Task.ReservedMilliseconds);
        Assert.True(result.Work.Executions.Single(execution => execution.ExecutionId == root.Reference.ExecutionId).NativeRestorationRequired);
        var storage = f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!;
        Assert.Equal(0, storage.ReservedBytes);
        Assert.Contains(output, storage.ProtectedPaths);
        Assert.True(File.Exists(Path.Combine(output, "retained.log")));
    }

    [Fact]
    public void NativeWork_RestorationUsesOnlyItsOriginalRecipeReserveAndRetainsTheAttempt()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(180, 3, 60, [new(identity.TaskId, 180, 3, 60)],
            "approved native allocation", "approved storage reference", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Bound the native mutation and its restoration.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture-profile", "mutation", f.Context.RepositoryId)
        { NativeOperation = "mutation-run", NativeRecoveryBasis = "sha256:approved-recovery-recipe", RepositoryPath = f.Context.RepositoryPath };
        var original = f.Ledger.Admit(f.Context, identity, "native/original", 120, 180, stage: stage, controller: "Controller");
        Assert.True(original.Admitted, string.Join("; ", original.Result.Errors));
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, original.Reference!).ExitCode);
        var liveRecovery = f.Ledger.Admit(f.Context, identity, "native/recovery-too-early", 60, 180,
            stage: stage with { Name = "native-restoration", NativeOperation = "mutation-recovery" }, controller: "Controller",
            restorationOfExecutionId: original.Reference!.ExecutionId);
        Assert.False(liveRecovery.Admitted);
        f.Time = f.Time.AddSeconds(120);
        var stopped = f.Ledger.Settle(f.Context, original.Reference, "failed", true, "Native mutation stopped; restore the original build.", ["fixture:native-failure"]);
        Assert.Equal(0, stopped.ExitCode);
        Assert.Equal(120_000, stopped.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(60_000, stopped.Work.Task.RestorationMilliseconds);
        Assert.True(Assert.Single(stopped.Work.Executions).NativeRestorationRequired);
        var changedProfile = f.Ledger.Admit(f.Context, identity, "native/different-profile", 30, 180,
            stage: stage with { RelationshipId = "different-profile" }, controller: "Controller");
        Assert.False(changedProfile.Admitted);
        Assert.Contains("requires restoration", changedProfile.Result.Work!.HoldReason!, StringComparison.Ordinal);
        var changedRecipe = f.Ledger.Admit(f.Context, identity, "native/wrong-recipe", 60, 180,
            stage: stage with { Name = "native-restoration", NativeRecoveryBasis = "sha256:different-recovery-recipe" },
            controller: "Controller", restorationOfExecutionId: original.Reference.ExecutionId);
        Assert.False(changedRecipe.Admitted);
        var recovery = f.Ledger.Admit(f.Context, identity, "native/recovery", 60, 180,
            stage: stage with { Name = "native-restoration", NativeOperation = "mutation-recovery" }, controller: "Controller",
            restorationOfExecutionId: original.Reference.ExecutionId);
        Assert.True(recovery.Admitted, string.Join("; ", recovery.Result.Errors));
        Assert.Equal(original.Reference.LogicalAttemptId, recovery.Reference!.LogicalAttemptId);
        Assert.Equal(1, recovery.Result.Work!.Task!.UsedAttempts);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, recovery.Reference).ExitCode);
        f.Time = f.Time.AddSeconds(10);
        var restored = f.Ledger.Settle(f.Context, recovery.Reference, "Restored", true, "Original build and ordinary test health are restored.", ["fixture:restored-health"]);
        Assert.Equal(0, restored.ExitCode);
        Assert.Equal(130_000, restored.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(130_000, restored.Work.Change.ConsumedMilliseconds);
        Assert.Equal(50_000, restored.Work.Task.RestorationMilliseconds);
        Assert.Equal(50_000, restored.Work.Change.RestorationMilliseconds);
        Assert.Equal(0, restored.Work.Task.RemainingMilliseconds);
        Assert.Equal(0, restored.Work.Task.ReservedMilliseconds);
        Assert.Equal(1, restored.Work.Task.UsedAttempts);
        Assert.All(restored.Work.Executions, execution => { Assert.True(execution.Finalized); Assert.False(execution.NativeRestorationRequired); });
        Assert.Equal(original.Reference.ExecutionId,
            restored.Work.Executions.Single(execution => execution.ExecutionId == recovery.Reference.ExecutionId).RestorationOfExecutionId);
        Assert.Equal("failed", restored.Work.Executions.Single(execution => execution.ExecutionId == original.Reference.ExecutionId).Status);
        var again = f.Ledger.Settle(f.Context, recovery.Reference, "Restored", true);
        Assert.Equal("unchanged", again.Status);
        Assert.Equal(130_000, again.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(50_000, again.Work.Task.RestorationMilliseconds);
        Assert.False(f.Ledger.Admit(f.Context, identity, "native/no-ordinary-refill", 30, 180, stage: stage, controller: "Controller").Admitted);
    }

    [Fact]
    public void NativeWork_PreparationReconciliationClearsOnlyNeverLaunchedRestorationNeed()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(360, 3, 60, [new(identity.TaskId, 300, 3, 60)],
            "approved native allocation", "approved storage reference", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Bound native preparation and recovery.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture-profile", "mutation", f.Context.RepositoryId)
        { NativeOperation = "mutation-run", NativeRecoveryBasis = "sha256:fixed-recovery-recipe" };
        var preparation = f.Ledger.Admit(f.Context, identity, "native/preparation", 60, 180, stage: stage, controller: "Controller");
        Assert.True(preparation.Admitted);
        f.Time = f.Time.AddSeconds(5);
        var reconciled = f.Ledger.Settle(f.Context, preparation.Reference!, "Cancelled", true,
            "The controlled preparer stopped before any launch intent.", ["fixture:stopped-preparer"], conservativeRecovery: true);
        Assert.Equal(0, reconciled.ExitCode);
        Assert.Equal(60_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        Assert.False(Assert.Single(reconciled.Work.Executions).NativeRestorationRequired);
        var launched = f.Ledger.Admit(f.Context, identity, "native/unknown-launch", 60, 180, stage: stage, controller: "Controller");
        Assert.True(launched.Admitted, string.Join("; ", launched.Result.Errors));
        Assert.Equal(preparation.Reference!.LogicalAttemptId, launched.Reference!.LogicalAttemptId);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, launched.Reference).ExitCode);
        f.Time = f.Time.AddSeconds(2);
        var uncertain = f.Ledger.Settle(f.Context, launched.Reference, "Interrupted", false);
        Assert.NotEqual(0, uncertain.ExitCode);
        Assert.Equal(60_000, uncertain.Work!.Task!.ReservedMilliseconds);
        var retained = uncertain.Work.Executions.Single(execution => execution.ExecutionId == launched.Reference.ExecutionId);
        Assert.True(retained.LaunchIntent); Assert.True(retained.NativeRestorationRequired); Assert.False(retained.Finalized);
        Assert.NotNull(retained.RecoveryReason);
        var next = f.Ledger.Admit(f.Context, identity, "native/new-profile-cannot-clear", 30, 180,
            stage: stage with { RelationshipId = "new-profile" }, controller: "Controller");
        Assert.False(next.Admitted);
        var recovery = f.Ledger.Admit(f.Context, identity, "native/uncertain-is-not-restorable", 30, 180,
            stage: stage with { Name = "native-restoration" }, controller: "Controller", restorationOfExecutionId: launched.Reference.ExecutionId);
        Assert.False(recovery.Admitted);
        var after = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(60_000, after.Task!.ConsumedMilliseconds);
        Assert.Equal(60_000, after.Task.ReservedMilliseconds);
        Assert.Equal(1, after.Task.UsedAttempts);
    }

    [Fact]
    public void NativeWork_VerifiedReconciliationReleasesOnlyPendingStorageAndOriginalRestorationCanUseItsReserve()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        const long ceiling = 2 * 1024 * 1024;
        var allocation = new CisAgentWorkAllocationRequest(180, 3, 60, [new(identity.TaskId, 180, 3, 60)],
            "approved native allocation", "approved finite native storage", AcknowledgeHistoricalUnknown: true)
        {
            Controllers = ["Controller"],
            Storage = new(ceiling, [new(f.Context.RepositoryPath, ".cis/local")]) { RestorationBytes = ceiling / 2 }
        };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Preserve bounded native recovery.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture-profile", "mutation", f.Context.RepositoryId)
        { NativeOperation = "mutation-run", NativeRecoveryBasis = "sha256:original-storage-recovery-recipe" };
        var original = f.Ledger.Admit(f.Context, identity, "native/storage-original", 120, 180, stage: stage, controller: "Controller");
        Assert.True(original.Admitted);
        var output = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/original"));
        var payload = Path.Combine(output, "necessary-diagnostic.bin");
        var reserved = f.Ledger.ReserveStorage(f.Context, original.Reference!, "native", ceiling, [new(output)], false, true, "Controller");
        Assert.True(reserved.Allowed, string.Join("; ", reserved.Result.Errors));
        Directory.CreateDirectory(output);
        File.WriteAllBytes(payload, new byte[checked((int)reserved.MaximumGrowthBytes)]);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, original.Reference!).ExitCode);
        f.Time = f.Time.AddSeconds(2);
        Assert.NotEqual(0, f.Ledger.Settle(f.Context, original.Reference!, "Interrupted", false).ExitCode);
        Assert.False(f.Ledger.SettleStorage(f.Context, original.Reference!, reserved.ReservationId!, false, null).Allowed);
        Assert.True(f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.ReservedBytes > 0);

        var reconciled = f.Ledger.Settle(f.Context, original.Reference!, "Interrupted", true,
            "The exact native job and preparing controller were verified stopped.", ["fixture:verified-stopped-native-tree"], conservativeRecovery: true);
        Assert.Equal(0, reconciled.ExitCode);
        var surviving = f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!;
        Assert.Equal(0, surviving.ReservedBytes);
        Assert.Contains(output, surviving.ProtectedPaths);
        Assert.True(File.Exists(payload));
        Assert.True(surviving.RemainingBytes < 256 * 1024);
        Assert.Equal(120_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(1, reconciled.Work.Task.UsedAttempts);

        var recovery = f.Ledger.Admit(f.Context, identity, "native/storage-restoration", 60, 180,
            stage: stage with { Name = "native-restoration", NativeOperation = "mutation-recovery" }, controller: "Controller",
            restorationOfExecutionId: original.Reference!.ExecutionId);
        Assert.True(recovery.Admitted, string.Join("; ", recovery.Result.Errors));
        Assert.Equal(original.Reference.LogicalAttemptId, recovery.Reference!.LogicalAttemptId);
        var restoreOutput = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/restoration"));
        var restoredStorage = f.Ledger.ReserveStorage(f.Context, recovery.Reference, "native", 256 * 1024,
            [new(restoreOutput)], false, false, "Controller");
        Assert.True(restoredStorage.Allowed, string.Join("; ", restoredStorage.Result.Errors));
        Assert.True(restoredStorage.MaximumGrowthBytes > surviving.RemainingBytes);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, recovery.Reference).ExitCode);
        f.Time = f.Time.AddSeconds(10);
        Assert.True(f.Ledger.SettleStorage(f.Context, recovery.Reference, restoredStorage.ReservationId!, true, [payload]).Allowed);
        var restored = f.Ledger.Settle(f.Context, recovery.Reference, "Restored", true);
        Assert.Equal(130_000, restored.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(50_000, restored.Work.Task.RestorationMilliseconds);
        Assert.Equal(1, restored.Work.Task.UsedAttempts);
        Assert.Equal(0, f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.ReservedBytes);
        Assert.True(f.Ledger.SettleStorage(f.Context, original.Reference, reserved.ReservationId!, true, requiredPaths: null).Allowed);
        Assert.Equal("unchanged", f.Ledger.Settle(f.Context, recovery.Reference, "Restored", true).Status);
        Assert.False(f.Ledger.Admit(f.Context, identity, "native/storage-no-refill", 30, 180, stage: stage, controller: "Controller").Admitted);
    }
}
