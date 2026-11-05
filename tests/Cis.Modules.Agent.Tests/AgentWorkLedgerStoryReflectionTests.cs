using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_StoryReflectionQualifiesOnlyItsApprovedContextAndRetainsDirectAndHardHolds()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(1000, 10, 0, [new(identity.TaskId, 900, 10)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Bound the approved task.").ExitCode);
        var initial = f.Ledger.Admit(f.Context, identity, "direct/initial", 30, 30);
        Assert.True(initial.Admitted);
        f.Time = f.Time.AddSeconds(30);
        Assert.Equal(0, f.Ledger.Settle(f.Context, initial.Reference!, "Succeeded", true, "Native result retained.", ["agent-run:direct/initial"]).ExitCode);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.True(before.ReflectionDue);
        Assert.Equal(AgentWorkLedger.ReflectionAction, before.HoldReason);

        const string relationship = "fixture-feature/story/task-instance";
        const string digest = "approved-task-contract";
        var stage = new CisAgentWorkStage("story-task", relationship, "orchestration", f.Context.RepositoryId) { ContractDigest = digest };
        var checkpoint = new CisAgentWorkCheckpoint("Native result succeeded; measured owner cost is 30000ms and the approved story contract is unchanged.",
            ["agent-run:direct/initial", "approved-story-contract:" + digest], "Inspect the retained candidate and its independent review.",
            "Continue the next bounded stage under the existing delegation.", "Review this same candidate before applying it.")
        { CloseAttempt = false };
        var progress = f.Ledger.Progress(f.Context, identity, new(checkpoint, before.Revision), "Controller", relationship, digest);
        Assert.Equal(0, progress.ExitCode);
        var recorded = Assert.Single(progress.Work!.Checkpoints, record => record.Origin == "story-controller-progress");
        Assert.Equal(relationship, recorded.StoryRelationshipId);
        Assert.Equal(digest, recorded.StoryContractDigest);
        Assert.False(recorded.Checkpoint.MaterialNewInformation);
        Assert.False(recorded.Checkpoint.CloseAttempt);

        var direct = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.True(direct.ReflectionDue);
        Assert.Equal(0, direct.LastControllerReflectionMilliseconds);
        Assert.Equal(AgentWorkLedger.ReflectionAction, direct.HoldReason);
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, AgentWorkLedger.RelativePath);
        var bytes = File.ReadAllBytes(ledgerPath);
        var scoped = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId, 30, stage).Work!;
        Assert.Null(scoped.HoldReason);
        Assert.True(scoped.ReflectionDue);
        Assert.Equal(0, scoped.LastControllerReflectionMilliseconds);
        Assert.Equal(bytes, File.ReadAllBytes(ledgerPath));

        var continued = f.Ledger.Admit(f.Context, identity, "story/same-context", 30, 30, stage: stage, controller: "Controller");
        Assert.True(continued.Admitted, string.Join("; ", continued.Result.Errors));
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, f.Ledger.Settle(f.Context, continued.Reference!, "Succeeded", true).ExitCode);
        Assert.False(f.Ledger.Admit(f.Context, identity, "direct/blocked", 30, 30).Admitted);
        Assert.False(f.Ledger.Admit(f.Context, identity, "story/other-relation", 30, 30,
            stage: stage with { RelationshipId = "different/story/task" }, controller: "Controller").Admitted);
        Assert.False(f.Ledger.Admit(f.Context, identity, "story/changed-contract", 30, 30,
            stage: stage with { ContractDigest = "changed-task-contract" }, controller: "Controller").Admitted);

        var current = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var held = f.Ledger.Disposition(f.Context, identity, new("hold", current.Revision), "Controller", "Required permission remains unresolved.");
        Assert.Equal(0, held.ExitCode);
        var reflectedWhileHeld = f.Ledger.Progress(f.Context, identity, new(checkpoint, held.Work!.Revision), "Controller", relationship, digest);
        Assert.Equal(0, reflectedWhileHeld.ExitCode);
        var hardHold = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId, 30, stage).Work!;
        Assert.Contains("Required permission remains unresolved", hardHold.HoldReason, StringComparison.Ordinal);
        Assert.False(f.Ledger.Admit(f.Context, identity, "story/hard-hold", 30, 30, stage: stage, controller: "Controller").Admitted);
        Assert.Equal(31_000, hardHold.Task!.ConsumedMilliseconds);
        Assert.Equal(1, hardHold.Task.UsedAttempts);
    }
}
