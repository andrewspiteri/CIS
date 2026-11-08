using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_TaskTimeAmendmentRaisesOnlyTheAbsoluteTaskCeilingAndRetainsItsOriginalDecision()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = AllocateTaskTimeFixture(f, identity);
        var measured = f.Ledger.Admit(f.Context, identity, "measured/original", 3000, 3600, controller: "Controller");
        Assert.True(measured.Admitted);
        f.Time = f.Time.AddMilliseconds(2_976_185);
        Assert.Equal(0, f.Ledger.Settle(f.Context, measured.Reference!, "Succeeded", true,
            "Retain performed fixture work.", ["fixture:original-result"]).ExitCode);
        // Existing deployed ledgers predate the additive amendment history field.
        var legacy = TaskTimeSnapshot(f);
        legacy["changes"]![identity.ChangeId]!.AsObject().Remove("taskTimeAmendments");
        File.WriteAllText(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger), legacy.ToJsonString());
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Empty(before.TaskTimeAmendments);
        Assert.Equal(23_815, before.Task!.RemainingMilliseconds);
        var original = TaskTimeSnapshot(f);
        var request = new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:new-task-time-decision", before.Revision);

        var result = f.Ledger.AmendTaskTime(f.Context, identity, request, "Controller", "Authorize one finite increase without replacing prior accounting.");

        Assert.Equal("task-time-amended", result.Status); Assert.Equal(0, result.ExitCode); Assert.True(result.Applied);
        Assert.Equal(3_577_000, result.Work!.Task!.AllocatedMilliseconds);
        Assert.Equal(300_815, result.Work.Task.RemainingMilliseconds);
        Assert.Equal(3_600_000, result.Work.Change.AllocatedMilliseconds);
        Assert.Equal(323_815, result.Work.Change.RemainingMilliseconds);
        Assert.Equal(2_976_185, result.Work.Task.ConsumedMilliseconds);
        Assert.Equal(before.Task, result.Work.Task with { AllocatedMilliseconds = before.Task.AllocatedMilliseconds });
        Assert.Equal(before.Change, result.Work.Change);
        Assert.Equal(before.LogicalAttemptId, result.Work.LogicalAttemptId);
        Assert.Equal(allocation.DecisionReference, result.Work.DecisionReference);
        Assert.Equal(allocation.StorageCeilingReference, result.Work.StorageCeilingReference);
        var audit = Assert.Single(result.Work.TaskTimeAmendments);
        Assert.Equal(f.Context.RepositoryId, audit.AuthorityRepositoryId);
        Assert.Equal(identity.ChangeId, audit.ChangeId); Assert.Equal(identity.TaskId, audit.TaskId);
        Assert.Equal(identity.TaskId, audit.OwnerTaskId);
        Assert.Equal(3_300_000, audit.PreviousAllocatedMilliseconds); Assert.Equal(3_577_000, audit.AllocatedMilliseconds);
        Assert.Equal("Controller", audit.Actor); Assert.Equal(request.DecisionReference, audit.DecisionReference);
        Assert.Equal(before.Revision, audit.PreviousRevision); Assert.Equal(result.Work.Revision, audit.Revision);
        Assert.Equal(f.Time.ToString("O"), audit.RecordedAtUtc);
        Assert.True(Guid.TryParseExact(audit.Reference, "N", out _));
        AssertOnlyTaskTimeAmendmentChanged(original, TaskTimeSnapshot(f), identity);
        var reopened = new AgentWorkLedger(() => f.Time).Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(audit, Assert.Single(reopened.TaskTimeAmendments));
        var bytes = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        foreach (var replay in new[] { request, request with { ExpectedRevision = result.Work.Revision },
            request with { Seconds = 3578, ExpectedRevision = result.Work.Revision } })
        {
            Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, identity, replay, "Controller", "A replay is not another grant.").ExitCode);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
        }
        var ordinary = allocation with
        {
            ExpectedRevision = result.Work.Revision,
            Tasks = [allocation.Tasks[0] with { Seconds = 3578 }]
        };
        Assert.Equal(4, f.Ledger.Allocate(f.Context, identity.ChangeId, ordinary, [identity], "Controller", "Ordinary allocation still cannot raise a fixed cap.").ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void WorkLedger_TaskTimeAmendmentPreservesIndependentHoldsAndActiveTimeAndStorageReservations()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        AllocateTaskTimeFixture(f, identity);
        var active = f.Ledger.Admit(f.Context, identity, "active/original", 60, 3600, controller: "Controller");
        Assert.True(active.Admitted);
        var claim = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/retained/report.json"));
        Assert.True(f.Ledger.ReserveStorage(f.Context, active.Reference!, "retained-original-storage", 1024,
            [new(claim, Required: true)], false, false, "Controller").Allowed);
        var current = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(0, f.Ledger.Disposition(f.Context, identity, new("hold", current.Revision),
            "Controller", "Preserve this separate controller hold.").ExitCode);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var snapshot = TaskTimeSnapshot(f);

        var amended = f.Ledger.AmendTaskTime(f.Context, identity,
            new(3577, "fixture:decision-with-existing-obligations", before.Revision), "Controller", "Change the ceiling only.");

        Assert.Equal(0, amended.ExitCode);
        Assert.Equal(before.HoldReason, amended.Work!.HoldReason);
        Assert.Equal(before.Task!.ReservedMilliseconds, amended.Work.Task!.ReservedMilliseconds);
        Assert.Equal(before.Task.UsedAttempts, amended.Work.Task.UsedAttempts);
        Assert.Equal(before.LogicalAttemptId, amended.Work.LogicalAttemptId);
        Assert.Equal(before.Executions.Single().ReservedMilliseconds, amended.Work.Executions.Single().ReservedMilliseconds);
        Assert.True(f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.ReservedBytes > 0);
        AssertOnlyTaskTimeAmendmentChanged(snapshot, TaskTimeSnapshot(f), identity);
    }

    [Fact]
    public void WorkLedger_TaskTimeAmendmentRejectsDelegatesStaleRevisionsAndNonOwnerIdentitiesWithoutWrites()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        AllocateTaskTimeFixture(f, identity);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var request = new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:new-authority-decision", before.Revision);
        var bytes = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        var delegated = f.Ledger.AmendTaskTime(f.Context, identity, request, "Delegate", "Delegated operation is not amendment authority.");
        Assert.Equal(4, delegated.ExitCode);
        Assert.Contains(delegated.Errors, error => error.Contains("original allocation authority", StringComparison.Ordinal));
        Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, identity, request, "Unknown actor", "No authority.").ExitCode);
        Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, identity, request with { ExpectedRevision = "stale:1" },
            "Controller", "A current revision is required.").ExitCode);
        foreach (var wrong in new[] { identity with { AuthorityRepositoryId = "other-authority" },
            identity with { ChangeId = "CIS-OTHER" }, f.Identity("WORK-MISSING"), f.Identity("WORK-CHILD", [identity.TaskId]) })
            Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, wrong, request, "Controller", "Keep the exact existing owner.").ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void WorkLedger_TaskTimeAmendmentRejectsInvalidAbsoluteCeilingsOrMissingNewDecisionsWithoutWrites()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = AllocateTaskTimeFixture(f, identity);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var request = new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:new-time-decision", before.Revision);
        var bytes = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        foreach (var seconds in new long[] { -1, 0, 3299, 3300, 31_536_001, long.MaxValue })
            Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, identity, request with { Seconds = seconds },
                "Controller", "Require an absolute finite increase.").ExitCode);
        foreach (var decision in new[] { "", allocation.DecisionReference })
            Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, identity, request with { DecisionReference = decision },
                "Controller", "A new decision is required.").ExitCode);
        Assert.Equal(4, f.Ledger.AmendTaskTime(f.Context, identity, request, "Controller", "").ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void WorkLedger_TaskTimeAmendmentPersistenceFailureLeavesTheOriginalCeilingAndHistoryThenRetriesOnce()
    {
        if (!OperatingSystem.IsWindows()) return; // FileShare.Read prevents replacement only under Windows sharing semantics.
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        AllocateTaskTimeFixture(f, identity);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var request = new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:persisted-time-decision", before.Revision);
        var path = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var bytes = File.ReadAllBytes(path);
        using (var blocked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = f.Ledger.AmendTaskTime(f.Context, identity, request, "Controller", "Retain the original state if replacement fails.");
            Assert.Equal("busy", failed.Status); Assert.Equal(4, failed.ExitCode); Assert.False(failed.Applied);
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var recovered = f.Ledger.AmendTaskTime(f.Context, identity, request, "Controller", "Retry the same absolute decision after the file is available.");
        Assert.Equal(0, recovered.ExitCode); Assert.Single(recovered.Work!.TaskTimeAmendments);
        Assert.Equal(3_577_000, recovered.Work.Task!.AllocatedMilliseconds);
    }

    private static CisAgentWorkAllocationRequest AllocateTaskTimeFixture(Fixture fixture, CisAgentWorkIdentity identity)
    {
        var allocation = new CisAgentWorkAllocationRequest(3600, 6, 300, [new(identity.TaskId, 3300, 6, 300)],
            "fixture:original-allocation-decision", "fixture:original-storage-ceiling", AcknowledgeHistoricalUnknown: true)
        {
            Controllers = ["Controller", "Delegate"],
            Storage = new(8 * 1024 * 1024, [new(fixture.Context.RepositoryPath, ".cis/local")]) { RestorationBytes = 1024 * 1024 }
        };
        Assert.Equal(0, fixture.Ledger.Allocate(fixture.Context, identity.ChangeId, allocation, [identity], "Controller",
            "Retain the original finite fixture authority.").ExitCode);
        return allocation;
    }

    private static JsonObject TaskTimeSnapshot(Fixture fixture)
        => JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Context.RepositoryPath, CisAgentWorkPaths.Ledger)))!.AsObject();

    private static void AssertOnlyTaskTimeAmendmentChanged(JsonObject before, JsonObject after, CisAgentWorkIdentity identity)
    {
        var normalized = after.DeepClone().AsObject();
        normalized["revision"] = before["revision"]!.DeepClone();
        var priorChange = before["changes"]![identity.ChangeId]!.AsObject();
        var change = normalized["changes"]![identity.ChangeId]!.AsObject();
        change["owners"]![identity.TaskId]!["milliseconds"] = priorChange["owners"]![identity.TaskId]!["milliseconds"]!.DeepClone();
        change.Remove("taskTimeAmendments");
        if (priorChange.TryGetPropertyValue("taskTimeAmendments", out var history))
            change.Add("taskTimeAmendments", history!.DeepClone());
        Assert.True(JsonNode.DeepEquals(before, normalized), "Only the absolute task time, one distinct audit record and its transaction revision may change.");
    }
}
