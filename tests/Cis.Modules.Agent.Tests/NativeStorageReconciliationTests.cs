using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void NativeWork_FinalizedNativeStorageSettlementCanReconcileWithoutLaunchingAgain()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var service = new AgentService(new CisRepositoryContextResolver(), clock: clock.Read);
        var allocation = WorkAllocation(180, 180, 30, ["WORK-090"]) with
        {
            Storage = new(16 * 1024 * 1024, [new(repository.Path, ".cis/local")]),
        };
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "storage-reconcile-allocation.json", allocation), WorkActor,
            "Retain a stopped native result when its storage transaction is temporarily busy.").ExitCode);
        var native = (ICisNativeWorkController)service;
        var storage = (ICisOwnedStorageController)service;
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor);
        var previousReport = Path.GetFullPath(Path.Combine(repository.Path, ".cis/local/testing/previous-result.json"));
        var previousAdmission = native.Admit(new(repository.Path, "native/previous-storage-result", "unit-verification", 30, association));
        Assert.True(previousAdmission.Allowed, string.Join("; ", previousAdmission.Errors));
        using var previous = previousAdmission.Scope!;
        var previousStorage = storage.Reserve(repository.Path, previous.Reference, "previous-result", 4096,
            [new(previousReport)], actor: WorkActor);
        Assert.True(previousStorage.Allowed, string.Join("; ", previousStorage.Result.Errors));
        Directory.CreateDirectory(Path.GetDirectoryName(previousReport)!);
        var previousBytes = "{\"previous\":\"required\"}"u8.ToArray();
        File.WriteAllBytes(previousReport, previousBytes);
        Assert.True(storage.Settle(repository.Path, previous.Reference, previousStorage.ReservationId!, true, [previousReport]).Allowed);
        Assert.Equal(0, previous.Finish("native-finished", "Retain the earlier required evidence under its original provenance.",
            ["fixture:previous-required-result"]).ExitCode);
        var admission = native.Admit(new(repository.Path, "native/storage-reconcile-root", "unit-verification", 30, association));
        Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
        using var root = admission.Scope!;
        var report = Path.GetFullPath(Path.Combine(repository.Path, ".cis/local/testing/native-result.json"));
        var reserved = storage.Reserve(repository.Path, root.Reference, "native-result", 4096,
            [new(report), new(previousReport)], actor: WorkActor);
        Assert.True(reserved.Allowed, string.Join("; ", reserved.Result.Errors));
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var original = "{\"nativeResult\":\"retained\"}"u8.ToArray();
        File.WriteAllBytes(report, original);
        var childAdmission = root.AdmitChild("native/storage-reconcile-child", "unit-native-driver", 20);
        Assert.True(childAdmission.Allowed, string.Join("; ", childAdmission.Errors));
        using var child = childAdmission.Scope!;
        child.Launch();
        WithWorkProcess(process =>
        {
            child.Started(process.Id, process.StartTime.ToUniversalTime().ToString("O"));
            clock.Advance(3);
            process.Kill(entireProcessTree: true); Assert.True(process.WaitForExit(5000));
            child.Returned(safelyStopped: true);
            Assert.Equal(0, child.Finish("native-finished", "The one actual native process stopped.", ["fixture:stopped-native-driver"]).ExitCode);
        });
        // Exercise an actual failed ledger transaction, rather than a fake
        // settlement response. The exact original growth grant remains pending.
        var ledgerBefore = File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger));
        using (var held = new FileStream(Path.Combine(repository.Path, CisAgentWorkPaths.Lock),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = storage.Settle(repository.Path, root.Reference, reserved.ReservationId!, true, [report]);
            Assert.False(failed.Allowed);
            Assert.Contains(failed.Errors, error => error.Contains(Path.GetFileName(CisAgentWorkPaths.Lock), StringComparison.Ordinal));
        }
        Assert.Equal(ledgerBefore, File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger)));
        Assert.Equal(0, root.Finish("native-finished", "The native tree is safely stopped despite the failed storage transaction.",
            ["fixture:retained-native-result"]).ExitCode);
        var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.All(before.Executions, execution => Assert.True(execution.Finalized));
        Assert.Equal("verified", native.InspectExecution(repository.Path, child.Reference).Status);
        Assert.False(CisNativeWorkSettlement.Complete(native.InspectSettlement(repository.Path, root.Reference), root.Reference));
        Assert.True(storage.Inspect(repository.Path, association).Storage!.ReservedBytes > 0);

        var fresh = new AgentService(new CisRepositoryContextResolver(), clock: clock.Read);
        var input = WriteWorkInput(repository, "reconcile-finalized-native-storage.json", new CisAgentWorkDispositionRequest("reconcile", before.Revision)
        { ExecutionId = root.Reference.ExecutionId, Evidence = ["fixture:already-stopped-native-receipts"] });
        var reconciled = fresh.DisposeWork(repository.Path, "CIS-0001", "WORK-090", input, WorkActor,
            "Settle the retained storage grant from the exact finalized native tree without executing again.");

        Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
        Assert.Equal("reconciled", reconciled.Status);
        var final = ((ICisNativeWorkController)fresh).InspectSettlement(repository.Path, root.Reference);
        Assert.True(CisNativeWorkSettlement.Complete(final, root.Reference), string.Join("; ", final.Errors));
        var inventory = ((ICisOwnedStorageController)fresh).Inspect(repository.Path, association).Storage!;
        Assert.Equal(0, inventory.ReservedBytes);
        Assert.Contains(report, inventory.ProtectedPaths);
        Assert.Contains(previousReport, inventory.ProtectedPaths);
        Assert.Equal(original, File.ReadAllBytes(report));
        Assert.Equal(previousBytes, File.ReadAllBytes(previousReport));
        var priorClaim = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger)))!
            ["changes"]!["CIS-0001"]!["storage"]!["claims"]![previousReport]!;
        Assert.Equal(previous.Reference.ExecutionId, priorClaim["requiredByExecutionId"]!.GetValue<string>());
        Assert.Equal(previous.Reference.OwnerTaskId, priorClaim["ownerTaskId"]!.GetValue<string>());
        Assert.Equal(before.Task!.ConsumedMilliseconds, final.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task.UsedAttempts, final.Work.Task.UsedAttempts);
        Assert.Equal(before.Executions.Select(item => item.ExecutionId).Order(StringComparer.Ordinal),
            final.Work.Executions.Select(item => item.ExecutionId).Order(StringComparer.Ordinal));
        Assert.Single(final.Work.Executions, item => item.LaunchIntent);
        Assert.All(final.Work.Executions, execution => { Assert.True(execution.Finalized); Assert.Null(execution.RecoveryReason); });
        var repeated = WriteWorkInput(repository, "reconcile-finalized-native-storage-again.json",
            new CisAgentWorkDispositionRequest("reconcile", final.Work.Revision)
            { ExecutionId = root.Reference.ExecutionId, Evidence = ["fixture:same-stopped-native-receipts"] });
        var unchanged = fresh.DisposeWork(repository.Path, "CIS-0001", "WORK-090", repeated, WorkActor, "The storage reconciliation is idempotent.");
        Assert.Equal("unchanged", unchanged.Status); Assert.False(unchanged.Applied);
        Assert.Equal(before.Task.ConsumedMilliseconds, unchanged.Work!.Task!.ConsumedMilliseconds);
    }
}
