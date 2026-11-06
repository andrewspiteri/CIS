using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryShortWindow_UsesThePreparedParentOrGrantRemainderWithoutRefillingIt(bool boundedCorrection)
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository);
        repository.Write("docs/cis/references/agent-provider-profile.md",
            "| maximum-run-seconds | 30 | Fixture adopted execution window. |\n| direct-dirty-working-tree | denied | Preserve isolation. |\n");
        repository.Write("app.txt", "original"); repository.CommitAll("short nested window baseline");
        var clock = new WorkControlClock();
        var launchDelayArmed = false; var launchDelayApplied = false;
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var providerStarts = new List<DateTimeOffset>();
        DateTimeOffset ReadClock()
        {
            if (launchDelayArmed && !launchDelayApplied)
            {
                var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
                if (ledger["changes"]!["CIS-0001"]!["executions"]!.AsObject().Any(entry =>
                    entry.Value!["reference"]!["parentExecutionId"] is not null
                    && entry.Value["launchIntent"]!.GetValue<bool>() && !entry.Value["finalized"]!.GetValue<bool>()))
                {
                    launchDelayApplied = true;
                    clock.Advance(1);
                }
            }
            return clock.Read();
        }
        var provider = new StoryExecutionProvider
        {
            ReviewFailures = boundedCorrection ? 3 : 0,
            DuringExecution = () => { providerStarts.Add(ReadClock()); clock.Advance(2); }
        };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: ReadClock,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var controller = Controller(); AllocateWorkFixture(repository, controller, 180, 180);
        var work = GovernedStoryWork(repository.Path);
        if (boundedCorrection) work = GrantShortWindowCorrection(repository, controller, work);
        var before = controller.InspectWork(work).Control!.Work!;
        var calls = provider.Requests.Count;
        var rootStarted = clock.Read();
        providerStarts.Clear(); launchDelayArmed = true;
        controller = Controller();
        var result = controller.Execute(work, controller.SelectModels(work), true, () => true,
            TestContext.Current.CancellationToken, progress =>
            {
                if (progress.Message.StartsWith("Preparing isolated task workspace:", StringComparison.Ordinal)) clock.AdvanceMilliseconds(1250);
                if (progress.Message.StartsWith("implement:", StringComparison.Ordinal)
                    || progress.Message.StartsWith("review:", StringComparison.Ordinal))
                {
                    clock.AdvanceMilliseconds(250);
                }
            });
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        var requests = provider.Requests.Skip(calls).ToArray();
        Assert.Equal(new[] { "implement", "review" }, requests.Select(request => request.Mode));
        // Child admission floors 28.75s to 28s. Stage preparation and the
        // durable launch-intent delay also consume that same admitted window.
        Assert.True(launchDelayApplied);
        Assert.Equal(TimeSpan.FromMilliseconds(26_750), requests[0].Timeout);
        Assert.Equal(TimeSpan.FromMilliseconds(24_750), requests[1].Timeout);
        Assert.Equal(requests.Length, providerStarts.Count);
        for (var index = 0; index < requests.Length; index++)
            Assert.True(providerStarts[index] + requests[index].Timeout <= rootStarted.AddSeconds(30));
        Assert.All(requests, request =>
        {
            Assert.True(request.StartupTimeout <= request.Timeout);
            Assert.True(request.IdleTimeout <= request.Timeout);
        });
        var after = controller.InspectWork(work).Control!.Work!;
        Assert.Equal(before.Task!.ConsumedMilliseconds + 6750, after.Task!.ConsumedMilliseconds);
        Assert.Equal(after.Task.ConsumedMilliseconds, after.Change.ConsumedMilliseconds);
        Assert.Equal(0, after.Task.ReservedMilliseconds); Assert.Equal(1, after.Task.UsedAttempts);
        Assert.Equal(before.Task.RestorationMilliseconds, after.Task.RestorationMilliseconds);
        var newExecutions = after.Executions.Where(execution => !before.Executions.Any(old => old.ExecutionId == execution.ExecutionId)).ToArray();
        var root = Assert.Single(newExecutions, execution => execution.ParentExecutionId is null);
        Assert.Equal(6750, root.ActiveMilliseconds);
        Assert.Equal(rootStarted, DateTimeOffset.Parse(root.StartedAtUtc));
        Assert.All(newExecutions, execution => Assert.Equal(root.ExecutionId, execution.RootExecutionId));
        foreach (var run in result.Runs.Where(run => requests.Any(request => request.RunId == run.RunId)))
        {
            var retained = controller.Show(repository.Path, run.RunId);
            Assert.Empty(retained.Diagnostics);
            Assert.All(retained.Run!.Artifacts, artifact => Assert.True(artifact.Valid));
            var manifest = retained.Run.Manifest;
            Assert.InRange(manifest.TimeoutSeconds, 1, 29);
            Assert.Equal(root.ExecutionId, manifest.Work!.ParentExecutionId);
        }
        if (boundedCorrection)
        {
            Assert.Equal(3, after.Corrections.Count);
            Assert.Equal(before.CorrectionContinuation!.Id, after.Corrections[2].DispositionReference);
            Assert.Equal(30, after.Corrections[2].MaximumSeconds);
            Assert.Equal(before.Corrections.Select(round => round.Id), after.Corrections.Take(2).Select(round => round.Id));
        }
        else Assert.Empty(after.Corrections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryShortWindow_ExhaustedPreparationPreventsProviderLaunchAndRemainsChargedOnce(bool boundedCorrection)
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository);
        repository.Write("docs/cis/references/agent-provider-profile.md",
            "| maximum-run-seconds | 30 | Fixture adopted execution window. |\n| direct-dirty-working-tree | denied | Preserve isolation. |\n");
        repository.Write("app.txt", "original"); repository.CommitAll("exhausted nested window baseline");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { ReviewFailures = boundedCorrection ? 3 : 0, DuringExecution = () => clock.Advance(2) };
        var controller = new AgentService(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        AllocateWorkFixture(repository, controller, 180, 180);
        var work = GovernedStoryWork(repository.Path);
        if (boundedCorrection) work = GrantShortWindowCorrection(repository, controller, work);
        var before = controller.InspectWork(work).Control!.Work!;
        var calls = provider.Requests.Count;
        var result = controller.Execute(work, controller.SelectModels(work), true, () => true,
            TestContext.Current.CancellationToken, progress =>
            {
                if (progress.Message.StartsWith("Preparing isolated task workspace:", StringComparison.Ordinal)) clock.Advance(30);
            });
        Assert.False(result.Applied); Assert.Equal(calls, provider.Requests.Count);
        var after = controller.InspectWork(work).Control!.Work!;
        Assert.Equal(before.Task!.ConsumedMilliseconds + 30_000, after.Task!.ConsumedMilliseconds);
        Assert.Equal(after.Task.ConsumedMilliseconds, after.Change.ConsumedMilliseconds);
        Assert.Equal(0, after.Task.ReservedMilliseconds);
        Assert.Equal(before.Corrections.Select(round => round.Id), after.Corrections.Select(round => round.Id));
        Assert.Equal("original", repository.Read("app.txt"));
        if (boundedCorrection)
        {
            Assert.Equal(before.CorrectionContinuation!.Id, after.CorrectionContinuation!.Id);
            Assert.False(after.CorrectionContinuationAvailable);
        }
        var retried = controller.Execute(work with { PreviousExecution = result }, controller.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.False(retried.Applied); Assert.Equal(calls, provider.Requests.Count);
        Assert.Equal(after.Task.ConsumedMilliseconds, controller.InspectWork(work).Control!.Work!.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void StoryShortWindow_ExhaustedChildLaunchIntentSettlesKnownUninvokedWorkWithoutRefundingTheGrant()
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository);
        repository.Write("docs/cis/references/agent-provider-profile.md",
            "| maximum-run-seconds | 30 | Fixture adopted execution window. |\n| direct-dirty-working-tree | denied | Preserve isolation. |\n");
        repository.Write("app.txt", "original"); repository.CommitAll("expired durable launch intent baseline");
        var clock = new WorkControlClock();
        var launchDelayArmed = false; var launchDelayApplied = false;
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        DateTimeOffset ReadClock()
        {
            if (launchDelayArmed && !launchDelayApplied)
            {
                var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
                if (ledger["changes"]!["CIS-0001"]!["executions"]!.AsObject().Any(entry =>
                    entry.Value!["reference"]!["parentExecutionId"] is not null
                    && entry.Value["launchIntent"]!.GetValue<bool>() && !entry.Value["finalized"]!.GetValue<bool>()))
                {
                    launchDelayApplied = true;
                    clock.Advance(30);
                }
            }
            return clock.Read();
        }
        var provider = new StoryExecutionProvider
        {
            ReviewFailures = 3,
            DuringExecution = () => { ReadClock(); clock.Advance(2); }
        };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: ReadClock,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var controller = Controller(); AllocateWorkFixture(repository, controller, 180, 180);
        var work = GrantShortWindowCorrection(repository, controller, GovernedStoryWork(repository.Path));
        var before = controller.InspectWork(work).Control!.Work!;
        var calls = provider.Requests.Count;
        launchDelayArmed = true;
        controller = Controller();
        var result = controller.Execute(work, controller.SelectModels(work), true, () => true,
            TestContext.Current.CancellationToken, null);
        Assert.True(launchDelayApplied);
        Assert.False(result.Applied); Assert.Equal(calls, provider.Requests.Count);
        var after = controller.InspectWork(work).Control!.Work!;
        Assert.Equal("known", after.RecoveryState);
        Assert.Equal(before.OwnerTaskId, after.OwnerTaskId);
        Assert.Equal(before.LogicalAttemptId, after.LogicalAttemptId);
        Assert.Equal(before.Task!.UsedAttempts, after.Task!.UsedAttempts);
        Assert.Equal(before.Change.UsedAttempts, after.Change.UsedAttempts);
        Assert.Equal(before.Task.ConsumedMilliseconds + 30_000, after.Task.ConsumedMilliseconds);
        Assert.Equal(after.Task.ConsumedMilliseconds, after.Change.ConsumedMilliseconds);
        Assert.Equal(0, after.Task.ReservedMilliseconds);
        Assert.Equal(before.Task.RestorationMilliseconds, after.Task.RestorationMilliseconds);
        var child = Assert.Single(after.Executions.Where(execution => !before.Executions.Any(old => old.ExecutionId == execution.ExecutionId)),
            execution => execution.ParentExecutionId is not null);
        Assert.True(child.LaunchIntent); Assert.True(child.Finalized);
        Assert.Null(child.ProcessId); Assert.Null(child.RecoveryReason);
        Assert.Equal(30_000, child.ActiveMilliseconds);
        Assert.Equal(before.Corrections.Select(round => round.Id), after.Corrections.Take(2).Select(round => round.Id));
        var round = after.Corrections[2];
        Assert.Equal("started", round.Status); Assert.Equal(30, round.MaximumSeconds);
        Assert.Equal(before.CorrectionContinuation!.Id, round.DispositionReference);
        Assert.Equal("original", repository.Read("app.txt"));
        var retried = controller.Execute(work with { PreviousExecution = result }, controller.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.False(retried.Applied); Assert.Equal(calls, provider.Requests.Count);
        var retained = controller.InspectWork(work).Control!.Work!;
        Assert.Equal(round.Id, retained.Corrections[2].Id);
        Assert.Equal(after.Task.ConsumedMilliseconds, retained.Task!.ConsumedMilliseconds);
        Assert.Equal(0, retained.Task.ReservedMilliseconds);
    }

    private static CisStoryTaskWork GrantShortWindowCorrection(AgentRepository repository, AgentService controller, CisStoryTaskWork work)
    {
        var held = controller.Execute(work, controller.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", held.Status);
        var before = controller.InspectWork(work).Control!.Work!;
        Assert.Equal(2, before.Corrections.Count);
        var request = new CisAgentWorkDispositionRequest("simplify", before.Revision)
        {
            CorrectionRelationshipId = before.CorrectionRelationshipId,
            CorrectionFindingReferences = before.RequiredCorrectionFindingReferences,
            CorrectionMaximumSeconds = 30,
            Hypothesis = "Correct only the retained acceptance invariant using the existing native implementation.",
            NextAction = "Apply the one bounded correction and independently review its retained candidate.",
            Evidence = ["agent-run:" + held.Runs.Last(run => run.Stage == "Review").RunId]
        };
        var granted = controller.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "short-window-grant.json", request), WorkActor, "Authorize one finite thirty-second correction.");
        Assert.True(granted.ExitCode == 0, string.Join("; ", granted.Errors));
        return work with { PreviousExecution = held };
    }
}

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_ShortChildrenRequireALiveParentAndConservativelyRetainItsDeadline()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(180, 5, 5, [new(identity.TaskId, 120, 4, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "A finite short-window allowance.").ExitCode);
        Assert.False(f.Ledger.Admit(f.Context, identity, "invalid-short-root", 29, 30).Admitted);
        Assert.False(f.Ledger.Admit(f.Context, identity, "invalid-parent-child", 1, 30, "missing-parent").Admitted);
        var root = f.Ledger.Admit(f.Context, identity, "short-root", 30, 30);
        Assert.True(root.Admitted);
        f.Time = f.Time.AddSeconds(1);
        var directChild = f.Ledger.Admit(f.Context, identity, "unclamped-direct-child", 30, 30,
            root.Reference!.ExecutionId, root.Reference.LogicalAttemptId);
        Assert.False(directChild.Admitted);
        Assert.Contains("direct", directChild.Result.Work!.HoldReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parent", directChild.Result.Work.HoldReason!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shorter execution bound", directChild.Result.Work.HoldReason!, StringComparison.Ordinal);
        f.Time = f.Time.AddMilliseconds(250);
        var child = f.Ledger.Admit(f.Context, identity, "short-child", 30, 30, root.Reference!.ExecutionId,
            root.Reference.LogicalAttemptId, clampToRemaining: true);
        Assert.True(child.Admitted); Assert.Equal(28, child.TimeoutSeconds);
        f.Time = f.Time.AddSeconds(28);
        Assert.Equal(0, f.Ledger.Settle(f.Context, child.Reference!, "Succeeded", true).ExitCode);
        var lessThanOneSecond = f.Ledger.Admit(f.Context, identity, "fractional-remainder", 1, 30,
            root.Reference.ExecutionId, root.Reference.LogicalAttemptId, clampToRemaining: true);
        Assert.False(lessThanOneSecond.Admitted);
        f.Time = f.Time.AddMilliseconds(750);
        Assert.False(f.Ledger.Admit(f.Context, identity, "exhausted-child", 1, 30,
            root.Reference.ExecutionId, root.Reference.LogicalAttemptId, clampToRemaining: true).Admitted);
        var settled = f.Ledger.Settle(f.Context, root.Reference, "Succeeded", true);
        Assert.Equal(30_000, settled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(30_000, settled.Work.Change.ConsumedMilliseconds);
        Assert.Equal(0, settled.Work.Change.ReservedMilliseconds);
        Assert.Equal(5_000, settled.Work.Task.RestorationMilliseconds);
        Assert.Equal("unchanged", f.Ledger.Settle(f.Context, root.Reference, "Succeeded", true).Status);
    }
}
