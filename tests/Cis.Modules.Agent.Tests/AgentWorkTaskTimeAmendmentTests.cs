using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(300, 300, 300)]
    [InlineData(0, 400, 323)]
    public void WorkControl_TaskTimeAmendmentPreservesHistoryAndNativeTaskAndChangeBounds(
        int taskRestorationSeconds, int requestedNativeSeconds, int admittedNativeSeconds)
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var service = new AgentService(new CisRepositoryContextResolver(), clock: clock.Read);
        var allocation = WorkAllocation(3300, 3600, 300, ["WORK-090"]) with
        {
            ChangeAttempts = 6,
            Tasks = [new("WORK-090", 3300, 6, taskRestorationSeconds)],
            Storage = new(1024 * 1024 * 1024, [new(repository.Path, ".cis/local")])
            { RestorationBytes = 64 * 1024 * 1024 },
        };
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "original-amendment-allocation.json", allocation), WorkActor,
            "Establish the synthetic original fixed task and containing-change limits.").ExitCode);
        var native = (ICisNativeWorkController)service;
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor);
        var original = native.Admit(new(repository.Path, "native/charged-before-amendment", "native-verification", 3000, association));
        Assert.True(original.Allowed, string.Join("; ", original.Errors));
        var report = Path.GetFullPath(Path.Combine(repository.Path, ".cis/local/testing/amendment-fixture/required.json"));
        repository.Write(".cis/local/testing/amendment-fixture/required.json", "{\"retained\":true}");
        using (var scope = original.Scope!)
        {
            var reservation = service.Reserve(repository.Path, scope.Reference, "required-original-report", 0,
                [new(report, Required: true)], actor: WorkActor);
            Assert.True(reservation.Allowed, string.Join("; ", reservation.Result.Errors));
            Assert.True(service.Settle(repository.Path, scope.Reference, reservation.ReservationId!, true, [report]).Allowed);
            // Exercise governed native accounting with a fake clock, not real
            // mutation acceptance or the original runtime pilot's allowance.
            scope.Launch();
            clock.AdvanceMilliseconds(2_976_185);
            scope.Returned(true);
            Assert.Equal(0, scope.Finish("native-finished", "Retain the exact synthetic performed work.", ["fixture:performed-work"]).ExitCode);
        }
        var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(2_976_185, before.Task!.ConsumedMilliseconds);
        Assert.Equal(323_815, before.Change.RemainingMilliseconds);
        Assert.Equal(3_300_000 - 2_976_185 - taskRestorationSeconds * 1000, before.Task.RemainingMilliseconds);
        var request = new CisAgentWorkTaskTimeAmendmentRequest(3577,
            "fixture:explicit-original-authority-task-time-decision", before.Revision);
        var input = WriteWorkInput(repository, "task-time-amendment.json", request);
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var beforeBytes = File.ReadAllBytes(ledgerPath);

        var amended = service.AmendWorkTaskTime(repository.Path, "CIS-0001", "WORK-090", input, WorkActor,
            "Raise only the existing task's absolute ceiling for one bounded synthetic native action.");

        Assert.True(amended.ExitCode == 0, string.Join("; ", amended.Errors));
        Assert.Equal("task-time-amended", amended.Status);
        var current = amended.Work!;
        Assert.Equal(3_577_000, current.Task!.AllocatedMilliseconds);
        Assert.Equal(3_600_000, current.Change.AllocatedMilliseconds);
        Assert.Equal(3_577_000 - 2_976_185 - taskRestorationSeconds * 1000, current.Task.RemainingMilliseconds);
        Assert.Equal(323_815, current.Change.RemainingMilliseconds);
        Assert.Equal(before.Task.ConsumedMilliseconds, current.Task.ConsumedMilliseconds);
        Assert.Equal(before.Change.ConsumedMilliseconds, current.Change.ConsumedMilliseconds);
        Assert.Equal(before.Task.RestorationMilliseconds, current.Task.RestorationMilliseconds);
        Assert.Equal(before.Change.RestorationMilliseconds, current.Change.RestorationMilliseconds);
        Assert.Equal(before.Task.AllocatedAttempts, current.Task.AllocatedAttempts);
        Assert.Equal(before.Task.UsedAttempts, current.Task.UsedAttempts);
        Assert.Equal(before.Change.AllocatedAttempts, current.Change.AllocatedAttempts);
        Assert.Equal(before.Change.UsedAttempts, current.Change.UsedAttempts);
        Assert.Equal(before.LogicalAttemptId, current.LogicalAttemptId);
        Assert.Equal(before.DecisionReference, current.DecisionReference);
        Assert.Equal(before.StorageCeilingReference, current.StorageCeilingReference);
        Assert.Equal(JsonSerializer.Serialize(before.Executions, JsonOptions), JsonSerializer.Serialize(current.Executions, JsonOptions));
        var amendment = Assert.Single(current.TaskTimeAmendments);
        Assert.NotEmpty(amendment.Reference);
        Assert.Equal(WorkActor, amendment.Actor);
        Assert.Equal(request.DecisionReference, amendment.DecisionReference);
        Assert.Equal(before.Revision, amendment.PreviousRevision);
        Assert.Equal(current.Revision, amendment.Revision);
        Assert.Equal(3_300_000, amendment.PreviousAllocatedMilliseconds);
        Assert.Equal(3_577_000, amendment.AllocatedMilliseconds);
        using (var oldLedger = JsonDocument.Parse(beforeBytes))
        using (var newLedger = JsonDocument.Parse(File.ReadAllBytes(ledgerPath)))
        {
            var oldChange = oldLedger.RootElement.GetProperty("changes").GetProperty("CIS-0001");
            var newChange = newLedger.RootElement.GetProperty("changes").GetProperty("CIS-0001");
            foreach (var property in new[] { "allocationHistory", "bindings", "storage", "executions", "controllers" })
                Assert.Equal(oldChange.GetProperty(property).GetRawText(), newChange.GetProperty(property).GetRawText());
            var oldOwner = oldChange.GetProperty("owners").GetProperty("WORK-090");
            var newOwner = newChange.GetProperty("owners").GetProperty("WORK-090");
            Assert.Equal(oldOwner.GetProperty("fixed").GetBoolean(), newOwner.GetProperty("fixed").GetBoolean());
            Assert.Equal(oldOwner.GetProperty("allowRedistribution").GetBoolean(), newOwner.GetProperty("allowRedistribution").GetBoolean());
        }

        var admission = native.Admit(new(repository.Path, "native/after-authorized-amendment", "native-verification",
            requestedNativeSeconds, association));
        Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
        using var root = admission.Scope!;
        Assert.Equal(admittedNativeSeconds, root.MaximumSeconds);
        Assert.Equal(before.LogicalAttemptId, root.Reference.LogicalAttemptId);
        Assert.Equal(admittedNativeSeconds * 1000L, admission.Control!.Work!.Task!.ReservedMilliseconds);
        Assert.Equal(admittedNativeSeconds * 1000L, admission.Control.Work.Change.ReservedMilliseconds);
        var nested = root.AdmitChild("native/amended-inclusive-child", "native-unit", requestedNativeSeconds);
        Assert.True(nested.Allowed, string.Join("; ", nested.Errors));
        using (var child = nested.Scope!)
        {
            Assert.Equal(admittedNativeSeconds, child.MaximumSeconds);
            child.Launch();
            clock.Advance(admittedNativeSeconds);
            child.Returned(true);
            Assert.Equal(0, child.Finish("native-finished", "The controlled native child safely stopped.", []).ExitCode);
        }
        var settled = root.Finish("native-finished", "Settle the inclusive amended native root once.", []);
        Assert.Equal(0, settled.ExitCode);
        Assert.Equal(2_976_185 + admittedNativeSeconds * 1000L, settled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(settled.Work.Task.ConsumedMilliseconds, settled.Work.Change.ConsumedMilliseconds);
        Assert.Equal(0, settled.Work.Task.ReservedMilliseconds);
        Assert.Equal(0, settled.Work.Change.ReservedMilliseconds);
        Assert.Equal(1, settled.Work.Task.UsedAttempts);
        Assert.Equal(before.LogicalAttemptId, settled.Work.LogicalAttemptId);
        Assert.All(settled.Work.Executions, item => Assert.True(item.Finalized));
        Assert.Equal("{\"retained\":true}", File.ReadAllText(report));
        var storage = ((ICisOwnedStorageController)service).Inspect(repository.Path, association).Storage!;
        Assert.Equal(1024 * 1024 * 1024, storage.MaximumBytes);
        Assert.Equal(64 * 1024 * 1024, storage.RestorationBytes);
        Assert.Equal(0, storage.ReservedBytes);
        Assert.Contains(report, storage.ProtectedPaths);
        var settledBytes = File.ReadAllBytes(ledgerPath);
        var exhausted = native.Admit(new(repository.Path, "native/caps-still-bind", "native-verification", 30, association));
        Assert.False(exhausted.Allowed);
        Assert.Equal(settledBytes, File.ReadAllBytes(ledgerPath));
        Assert.Equal(3_577_000, exhausted.Control!.Work!.Task!.AllocatedMilliseconds);
        Assert.Equal(3_600_000, exhausted.Control.Work.Change.AllocatedMilliseconds);
    }

    [Fact]
    public void WorkControl_TaskTimeAmendmentServiceRejectsDelegationStaleRevisionAndOrdinaryAllocationWithoutWriting()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var service = new AgentService(new CisRepositoryContextResolver(), clock: clock.Read);
        const string delegated = "Delegated controller";
        var allocation = WorkAllocation(3300, 3600, 300, ["WORK-090"]) with { Controllers = [WorkActor, delegated] };
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "fixed-task-allocation.json", allocation), WorkActor,
            "Retain original allocation authority separately from ordinary controller delegation.").ExitCode);
        var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        var request = new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:authorized-task-time-decision", before.Revision);
        var input = WriteWorkInput(repository, "original-authority-amendment.json", request);
        var path = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var original = File.ReadAllBytes(path);

        var denied = service.AmendWorkTaskTime(repository.Path, "CIS-0001", "WORK-090", input, delegated,
            "Ordinary controller delegation is not original amendment authority.");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Equal(original, File.ReadAllBytes(path));

        var amended = service.AmendWorkTaskTime(repository.Path, "CIS-0001", "WORK-090", input, WorkActor,
            "Apply the new original-authority decision once.");
        Assert.Equal(0, amended.ExitCode);
        var applied = File.ReadAllBytes(path);
        var staleInput = WriteWorkInput(repository, "stale-task-time-amendment.json",
            request with { Seconds = 3600, DecisionReference = "fixture:later-decision-with-stale-revision" });
        var stale = service.AmendWorkTaskTime(repository.Path, "CIS-0001", "WORK-090", staleInput, WorkActor,
            "A stale snapshot cannot authorize another ceiling change.");
        Assert.NotEqual(0, stale.ExitCode);
        Assert.Equal(applied, File.ReadAllBytes(path));

        var ordinary = allocation with
        {
            ExpectedRevision = amended.Work!.Revision,
            Tasks = [new("WORK-090", 3600, 10, 300)]
        };
        var fixedIncrease = service.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "ordinary-fixed-increase.json", ordinary), WorkActor,
            "Ordinary allocation must not become an implicit amendment route.");
        Assert.NotEqual(0, fixedIncrease.ExitCode);
        Assert.Equal(applied, File.ReadAllBytes(path));
        Assert.Single(service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.TaskTimeAmendments);
    }
}
