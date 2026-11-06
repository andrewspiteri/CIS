using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Theory]
    [InlineData("current")]
    [InlineData("legacy")]
    [InlineData("missing")]
    public void WorkLedger_StorySummariesNeverReplaceAuthorizedAssessmentOrClearIndependentHolds(string storyOrigin)
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
        var recorded = Assert.Single(progress.Work!.Checkpoints, record => record.Origin == "story-execution-summary");
        Assert.Equal(relationship, recorded.StoryRelationshipId);
        Assert.Equal(digest, recorded.StoryContractDigest);
        Assert.False(recorded.Checkpoint.MaterialNewInformation);
        Assert.False(recorded.Checkpoint.CloseAttempt);
        Assert.Equal(before.NextAction, progress.Work.NextAction);
        Assert.Equal(before.Hypothesis, progress.Work.Hypothesis);

        var ledgerPath = Path.Combine(f.Context.RepositoryPath, AgentWorkLedger.RelativePath);
        if (storyOrigin != "current")
        {
            // Retained pre-fix automatic records must not acquire assessment credit
            // merely because their old producer called them controller progress.
            var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
            var retained = ledger["changes"]![identity.ChangeId]!["owners"]![identity.TaskId]!["checkpoints"]!.AsArray();
            var automatic = Assert.Single(retained, record => record?["origin"]?.GetValue<string>() == "story-execution-summary")!.AsObject();
            if (storyOrigin == "missing") automatic.Remove("origin");
            else automatic["origin"] = "story-controller-progress";
            File.WriteAllText(ledgerPath, ledger.ToJsonString());
        }
        var recreated = new AgentWorkLedger(() => f.Time);

        var direct = recreated.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.True(direct.ReflectionDue);
        Assert.Equal(0, direct.LastControllerReflectionMilliseconds);
        Assert.Equal(AgentWorkLedger.ReflectionAction, direct.HoldReason);
        var bytes = File.ReadAllBytes(ledgerPath);
        var scoped = recreated.Read(f.Context, identity.ChangeId, identity.TaskId, 30, stage).Work!;
        Assert.Equal(AgentWorkLedger.ReflectionAction, scoped.HoldReason);
        Assert.True(scoped.ReflectionDue);
        Assert.Equal(0, scoped.LastControllerReflectionMilliseconds);
        Assert.Equal(30_000, scoped.ReflectionWindowMilliseconds);
        Assert.Equal(before.LogicalAttemptId, scoped.LogicalAttemptId);
        Assert.Equal(before.Task!.ConsumedMilliseconds, scoped.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task.UsedAttempts, scoped.Task.UsedAttempts);
        Assert.Equal(bytes, File.ReadAllBytes(ledgerPath));
        Assert.False(recreated.Admit(f.Context, identity, "story/same-context-before-assessment", 30, 30, stage: stage, controller: "Controller").Admitted);
        Assert.False(recreated.Admit(f.Context, identity, "direct/blocked", 30, 30).Admitted);
        Assert.False(recreated.Admit(f.Context, identity, "story/other-relation", 30, 30,
            stage: stage with { RelationshipId = "different/story/task" }, controller: "Controller").Admitted);
        Assert.False(recreated.Admit(f.Context, identity, "story/changed-contract", 30, 30,
            stage: stage with { ContractDigest = "changed-task-contract" }, controller: "Controller").Admitted);

        var assessment = checkpoint with
        {
            Outcome = "The retained candidate is usable, but its review evidence needs a bounded protocol diagnosis after 30000ms of measured work.",
            Approach = "Keep the candidate and inspect the existing review result parser before retrying independent review.",
            Decision = "Continue with one diagnosis step using the current candidate and the remaining owner allowance.",
            NextAction = "Inspect the retained review parse failure, then retry only that review within thirty seconds.",
            Hypothesis = "The review protocol failure can be diagnosed without repeating implementation."
        };
        var current = recreated.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var assessed = recreated.Progress(f.Context, identity, new(assessment, current.Revision), "Controller");
        Assert.Equal(0, assessed.ExitCode);
        Assert.Contains(assessed.Work!.Checkpoints, record => record.Origin == "controller-progress" && record.ActiveMilliseconds == 30_000);
        foreach (var route in new CisAgentWorkStage?[] { null, stage, stage with { RelationshipId = "different/story/task" }, stage with { ContractDigest = "changed-task-contract" } })
        {
            var eligible = recreated.Read(f.Context, identity.ChangeId, identity.TaskId, 30, route).Work!;
            Assert.False(eligible.ReflectionDue); Assert.Null(eligible.HoldReason);
            Assert.Equal(30_000, eligible.LastControllerReflectionMilliseconds);
            Assert.Equal(before.LogicalAttemptId, eligible.LogicalAttemptId);
            Assert.Equal(before.Task.UsedAttempts, eligible.Task!.UsedAttempts);
        }
        var continued = recreated.Admit(f.Context, identity, "story/same-context", 30, 30, stage: stage, controller: "Controller");
        Assert.True(continued.Admitted, string.Join("; ", continued.Result.Errors));
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, recreated.Settle(f.Context, continued.Reference!, "Succeeded", true).ExitCode);

        current = recreated.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var held = recreated.Disposition(f.Context, identity, new("hold", current.Revision), "Controller", "Required permission remains unresolved.");
        Assert.Equal(0, held.ExitCode);
        var reflectedWhileHeld = recreated.Progress(f.Context, identity, new(assessment, held.Work!.Revision), "Controller");
        Assert.Equal(0, reflectedWhileHeld.ExitCode);
        var hardHold = recreated.Read(f.Context, identity.ChangeId, identity.TaskId, 30, stage).Work!;
        Assert.False(hardHold.ReflectionDue);
        Assert.Contains("Required permission remains unresolved", hardHold.HoldReason, StringComparison.Ordinal);
        Assert.False(recreated.Admit(f.Context, identity, "story/hard-hold", 30, 30, stage: stage, controller: "Controller").Admitted);
        Assert.Equal(31_000, hardHold.Task!.ConsumedMilliseconds);
        Assert.Equal(1, hardHold.Task.UsedAttempts);
    }

    [Fact]
    public void WorkLedger_AuthorizedAssessmentDoesNotClearAnUncertainOwnedExecution()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(180, 5, 5, [new(identity.TaskId, 120, 4, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Bound the approved task.").ExitCode);
        var root = f.Ledger.Admit(f.Context, identity, "uncertain/root", 30, 30);
        Assert.True(root.Admitted);
        f.Time = f.Time.AddSeconds(30);
        Assert.True(f.Ledger.MarkRecoveryUncertain(f.Context, root.Reference!, "The exact owned provider process is unresolved.").Applied);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.True(before.ReflectionDue);
        var assessment = new CisAgentWorkCheckpoint("The owned execution has unresolved process evidence after thirty seconds.",
            ["agent-run:uncertain/root"], "Preserve the reservation and inspect the existing ownership evidence.",
            "Diagnose the exact recovery hold before another provider invocation.", "Reconcile only after verifying that the recorded owned process stopped.")
        { CloseAttempt = false };
        var assessed = f.Ledger.Progress(f.Context, identity, new(assessment, before.Revision), "Controller");
        Assert.Equal(0, assessed.ExitCode);
        var after = assessed.Work!;
        Assert.False(after.ReflectionDue);
        Assert.Contains("exact owned provider process is unresolved", after.HoldReason, StringComparison.Ordinal);
        Assert.Equal(before.Task!.ConsumedMilliseconds, after.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task.ReservedMilliseconds, after.Task.ReservedMilliseconds);
        Assert.Equal(30_000, after.Task.ReservedMilliseconds);
        Assert.Equal(before.LogicalAttemptId, after.LogicalAttemptId);
        Assert.Equal(before.Task.UsedAttempts, after.Task.UsedAttempts);
        Assert.False(f.Ledger.Admit(f.Context, identity, "uncertain/blocked", 30, 30).Admitted);
    }
}
