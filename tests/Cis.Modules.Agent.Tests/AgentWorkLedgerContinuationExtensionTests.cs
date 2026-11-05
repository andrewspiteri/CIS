using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_ExplicitBoundedStepExtendsOnlyTheSafelyStoppedExhaustedGrantedRound()
    {
        using var f = new Fixture();
        const string relationship = "feature/stable-instance";
        var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(700, 10, 5, [new(identity.TaskId, 600, 5, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "A finite correction allowance.").ExitCode);
        var finding = new CisStoryReviewFinding("F1", "major", "required", "acceptance", "app.txt", "Required output is missing.",
            "Correct the required output.", "backend", "native-review", "material:current", ["criterion"]);
        CisAgentWorkCorrectionRound Begin(CisAgentWorkReference root) => f.Ledger.BeginCorrection(f.Context, identity, root,
            relationship, "Controller", ["required:current"], "contract:current", "material:current", [finding],
            [new("backend", "baseline-tree", "candidate-tree")]).Round!;
        var defaults = f.Ledger.Admit(f.Context, identity, "defaults/root", 120, 3600,
            stage: new("story-task", relationship, "orchestration", "authority"), controller: "Controller");
        Assert.True(defaults.Admitted);
        for (var number = 1; number <= 2; number++)
        {
            var round = Begin(defaults.Reference!);
            Assert.Equal(number, round.Number);
            Assert.Equal(0, f.Ledger.CompleteCorrection(f.Context, identity, defaults.Reference, relationship, round.Id, "Controller").ExitCode);
        }
        Assert.Null(f.Ledger.BeginCorrection(f.Context, identity, defaults.Reference, relationship, "Controller", ["required:current"],
            "contract:current", "material:current", [finding], [new("backend", "baseline-tree", "candidate-tree")]).Round);
        Assert.Equal(0, f.Ledger.Settle(f.Context, defaults.Reference!, "changes-requested", true).ExitCode);
        var status = f.Ledger.ReadCorrections(f.Context, relationship).Work!;
        var request = new CisAgentWorkDispositionRequest("simplify", status.Revision)
        {
            CorrectionRelationshipId = relationship,
            CorrectionFindingReferences = ["required:current"],
            CorrectionMaximumSeconds = 60,
            Hypothesis = "Correct only the required native output.",
            NextAction = "Make one correction and review it.",
            Evidence = ["native-review"]
        };
        Assert.Equal(0, f.Ledger.Disposition(f.Context, identity, request, "Controller", "One explicit bounded correction.").ExitCode);
        var stage = new CisAgentWorkStage("story-task", relationship, "orchestration", "authority") { ContractDigest = "contract:current" };
        var pendingGrant = f.Ledger.ReadCorrections(f.Context, relationship).Work!.CorrectionContinuation!.Id;
        var failedPreparation = f.Ledger.Admit(f.Context, identity, "verification/root", 120, 3600,
            clampToRemaining: true, stage: stage, controller: "Controller");
        Assert.True(failedPreparation.Admitted); Assert.Equal(60, failedPreparation.TimeoutSeconds);
        f.Time = f.Time.AddSeconds(35);
        Assert.Equal(0, f.Ledger.Settle(f.Context, failedPreparation.Reference!, "material-verification-failed", true).ExitCode);
        var ledgerBeforeStatus = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        var pending = f.Ledger.ReadCorrections(f.Context, relationship, "contract:current");
        Assert.Equal("held", pending.Status);
        Assert.Equal(pendingGrant, pending.Work!.CorrectionContinuation!.Id);
        Assert.False(pending.Work.CorrectionContinuationAvailable);
        Assert.Contains(relationship, pending.Work.HoldReason!);
        Assert.Contains("replacement simplify", pending.Work.NextAction);
        Assert.Equal(35_000, pending.Work.Task!.ConsumedMilliseconds);
        Assert.Equal(2, pending.Work.Corrections.Count);
        var pendingOwner = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId, stage: stage);
        Assert.Equal(pending.Work.HoldReason, pendingOwner.Work!.HoldReason);
        Assert.Equal(pending.Work.NextAction, pendingOwner.Work.NextAction);
        Assert.Equal(ledgerBeforeStatus, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
        var pendingDenied = f.Ledger.Admit(f.Context, identity, "exhausted-verification/root", 120, 3600,
            clampToRemaining: true, stage: stage, controller: "Controller");
        Assert.False(pendingDenied.Admitted);
        Assert.Equal(pending.Work.HoldReason, pendingDenied.Result.Work!.HoldReason);
        Assert.DoesNotContain("allocation authority", pendingDenied.Result.Work.NextAction);
        var replacement = request with
        {
            ExpectedRevision = pending.Work.Revision,
            Hypothesis = "Restore the exact retained material basis before one bounded output correction.",
            NextAction = "Verify that restored basis and correct only the named output."
        };
        Assert.Equal(0, f.Ledger.Disposition(f.Context, identity, replacement, "Controller", "Authorize a distinct bounded replacement after failed preparation.").ExitCode);
        var replaced = f.Ledger.ReadCorrections(f.Context, relationship).Work!;
        Assert.NotEqual(pendingGrant, replaced.CorrectionContinuation!.Id);
        Assert.Equal(35_000, replaced.Task!.ConsumedMilliseconds); Assert.Equal(2, replaced.Corrections.Count);
        Assert.Equal(replacement.NextAction, f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!.NextAction);
        var root = f.Ledger.Admit(f.Context, identity, "granted/root", 120, 3600,
            clampToRemaining: true, stage: new("story-task", relationship, "orchestration", "authority")
            { ContractDigest = "contract:current" }, controller: "Controller");
        Assert.True(root.Admitted); Assert.Equal(60, root.TimeoutSeconds);
        var active = Begin(root.Reference!);
        Assert.Equal(3, active.Number);
        var firstDecision = active.DispositionReference;
        var child = f.Ledger.Admit(f.Context, identity, "granted/correction", 120, 3600, root.Reference!.ExecutionId,
            root.Reference.LogicalAttemptId, clampToRemaining: true,
            stage: new("story-task", relationship, "correction", "backend", active.Id), controller: "Controller");
        Assert.True(child.Admitted);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, child.Reference!).ExitCode);
        f.Time = f.Time.AddSeconds(60);
        f.Ledger.MarkRecoveryUncertain(f.Context, child.Reference!, "The correction process is unresolved.");
        var extension = request with
        {
            ExpectedRevision = f.Ledger.ReadCorrections(f.Context, relationship).Work!.Revision,
            Hypothesis = "Review the retained corrected output without repeating implementation.",
            NextAction = "Finish only the remaining independent review.",
            Evidence = ["corrected-stage"]
        };
        Assert.NotEqual(0, f.Ledger.Disposition(f.Context, identity, extension, "Controller", "Uncertain ownership cannot authorize another step.").ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, child.Reference!, "Succeeded", true).ExitCode);
        Assert.Equal(0, f.Ledger.RecordCorrectionStage(f.Context, identity, root.Reference, relationship, active.Id, "Controller",
            "backend", "corrected-stage", "corrected-tree", ["corrected-stage"]).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, root.Reference!, "review-held", true).ExitCode);
        var stopped = f.Ledger.ReadCorrections(f.Context, relationship).Work!;
        Assert.Equal(95_000, stopped.Task!.ConsumedMilliseconds);
        ledgerBeforeStatus = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        var stoppedProjection = f.Ledger.ReadCorrections(f.Context, relationship, "contract:current");
        Assert.Equal("held", stoppedProjection.Status);
        Assert.Contains(relationship, stoppedProjection.Work!.HoldReason!);
        Assert.Contains("same-round simplify extension", stoppedProjection.Work.NextAction);
        Assert.Equal(ledgerBeforeStatus, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
        var exhausted = f.Ledger.Admit(f.Context, identity, "exhausted/root", 120, 3600,
            clampToRemaining: true, stage: new("story-task", relationship, "orchestration", "authority")
            { ContractDigest = "contract:current" }, controller: "Controller");
        Assert.False(exhausted.Admitted);
        Assert.Equal(stoppedProjection.Work.HoldReason, exhausted.Result.Work!.HoldReason);
        Assert.DoesNotContain("allocation authority", exhausted.Result.Work.NextAction);
        Assert.NotEqual(0, f.Ledger.Disposition(f.Context, identity,
            extension with { Kind = "continue", ExpectedRevision = stopped.Revision }, "Controller", "A generic continuation cannot extend the cap.").ExitCode);
        var extended = f.Ledger.Disposition(f.Context, identity, extension with { ExpectedRevision = stopped.Revision }, "Controller",
            "Authorize sixty more seconds for this exact unfinished review, retaining all prior work.");
        Assert.True(extended.ExitCode == 0, string.Join("; ", extended.Errors));
        var retained = f.Ledger.ReadCorrections(f.Context, relationship).Work!;
        var same = retained.Corrections.Single(round => round.Id == active.Id);
        Assert.Equal(3, same.Number); Assert.Equal(120, same.MaximumSeconds);
        Assert.Equal(firstDecision, same.DispositionReference);
        var continuation = Assert.Single(same.ContinuationReferences!);
        Assert.Contains(retained.Checkpoints, checkpoint => checkpoint.Checkpoint.Decision.Contains(continuation, StringComparison.Ordinal));
        Assert.Equal(active.Context!.MaterialBasis, same.Context!.MaterialBasis);
        Assert.Equal("corrected-stage", Assert.Single(same.Stages).RunId);
        Assert.Equal(95_000, retained.Task!.ConsumedMilliseconds);
        Assert.Equal(stopped.Task.AllocatedMilliseconds, retained.Task.AllocatedMilliseconds);
        var retry = f.Ledger.Admit(f.Context, identity, "review/root", 120, 3600,
            clampToRemaining: true, stage: new("story-task", relationship, "orchestration", "authority")
            { ContractDigest = "contract:current" }, controller: "Controller");
        Assert.True(retry.Admitted); Assert.Equal(60, retry.TimeoutSeconds);
        f.Time = f.Time.AddSeconds(5);
        var resumed = Begin(retry.Reference!);
        Assert.Equal(active.Id, resumed.Id); Assert.Equal(3, resumed.Number);
        Assert.Equal("corrected-stage", Assert.Single(resumed.Stages).RunId);
        var review = f.Ledger.Admit(f.Context, identity, "remaining/review", 120, 3600, retry.Reference!.ExecutionId,
            retry.Reference.LogicalAttemptId, clampToRemaining: true,
            stage: new("story-task", relationship, "review", "backend", active.Id), controller: "Controller");
        Assert.True(review.Admitted); Assert.Equal(55, review.TimeoutSeconds);
        f.Time = f.Time.AddSeconds(2);
        Assert.Equal(0, f.Ledger.Settle(f.Context, review.Reference!, "Succeeded", true).ExitCode);
        Assert.Equal(0, f.Ledger.CompleteCorrection(f.Context, identity, retry.Reference, relationship, active.Id, "Controller").ExitCode);
        var finished = f.Ledger.Settle(f.Context, retry.Reference!, "Succeeded", true);
        Assert.Equal(102_000, finished.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(1, finished.Work.Task.UsedAttempts);
        Assert.Equal(3, finished.Work.Corrections.Count);
    }
}
