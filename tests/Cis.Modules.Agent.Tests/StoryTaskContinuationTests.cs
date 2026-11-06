using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void StoryContinuationAttachesItsFirstCanonicalBindingWithoutRefillingOrReplacingHistory()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md", "---\ntask_status: Ready\ntargets: [\"agent-fixture\"]\n---\n# Separate approved work\nKeep this separate from the retained owner.\n");
        repository.Write("app.txt", "original"); repository.CommitAll("first canonical binding baseline");
        var provider = new StoryExecutionProvider { ReviewFailures = 1, ReviewNeedsChanges = number => number != 2 };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var first = Controller(); var work = StoryWork(repository.Path);
        var retained = first.Execute(work, first.SelectModels(work), true,
            () => provider.Requests.Count(request => request.Mode == "review") < 2, TestContext.Current.CancellationToken, null);
        Assert.Equal("stale", retained.Status);
        var unbound = first.InspectWork(work);
        Assert.Null(unbound.Identity); Assert.True(unbound.Control!.Work!.CorrectionOnly);
        var original = Assert.Single(unbound.Control.Work.Corrections); Assert.Equal("completed", original.Status);
        var bound = work with { CanonicalBinding = new("CIS-0001", "WORK-090"), PreviousExecution = retained };
        var inspected = first.InspectWork(bound);
        Assert.True(inspected.BindingCurrent); Assert.False(inspected.Required);
        Assert.Contains("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md", inspected.Identity!.AcceptedOutcomeReferences);
        var fresh = Controller();
        var interrupted = fresh.Execute(bound, fresh.SelectModels(bound), true,
            () => provider.Requests.Count(request => request.Mode == "review") < 3, TestContext.Current.CancellationToken, null);
        Assert.Equal("stale", interrupted.Status);
        var attached = fresh.InspectWork(bound).Control!.Work!;
        Assert.Equal("CIS-0001", attached.ChangeId); Assert.Equal("WORK-090", attached.TaskId);
        Assert.True(attached.CorrectionOnly); Assert.Equal(new[] { 1, 2 }, attached.Corrections.Select(round => round.Number));
        Assert.Equal(original.Id, attached.Corrections[0].Id); Assert.Equal("started", attached.Corrections[1].Status);
        var wrong = bound with { CanonicalBinding = new("CIS-0001", "WORK-091"), PreviousExecution = interrupted };
        var before = File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger));
        var wrongStatus = fresh.InspectWork(wrong);
        Assert.Equal("held", wrongStatus.Status); Assert.False(wrongStatus.BindingCurrent);
        Assert.Contains("WORK-090", wrongStatus.NextAction); Assert.Contains("WORK-091", wrongStatus.NextAction);
        var calls = provider.Requests.Count;
        var rejected = fresh.Execute(wrong, fresh.SelectModels(wrong), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(rejected.Applied); Assert.Equal(calls, provider.Requests.Count);
        Assert.Contains(rejected.Errors, error => error.Contains("Restore the matching approved story binding", StringComparison.Ordinal));
        Assert.DoesNotContain(rejected.Errors, error => error.Contains("Work accounting must be restored", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger)));
        var completed = fresh.Execute(bound with { PreviousExecution = interrupted }, fresh.SelectModels(bound), true, () => true,
            TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", completed.Status);
        var current = fresh.InspectWork(bound).Control!.Work!;
        Assert.True(current.CorrectionOnly); Assert.Equal(new[] { 1, 2 }, current.Corrections.Select(round => round.Number));
        Assert.Equal(original.Id, current.Corrections[0].Id); Assert.All(current.Corrections, round => Assert.Equal("completed", round.Status));
        Assert.Contains("Two automatic correction rounds", current.HoldReason!);
        Assert.False(File.Exists(Path.Combine(repository.Path, CisAgentWorkPaths.Adoption)));
        Assert.False(File.Exists(Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath)));
    }

    [Fact]
    public void StoryContinuationLetsAnotherActorUseOnlyTheRemainingNonAdoptedCorrectionRound()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("bounded actor handoff baseline");
        var provider = new StoryExecutionProvider { ReviewFailures = 1, ReviewNeedsChanges = number => number != 2 };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var first = Controller(); var work = StoryWork(repository.Path);
        var retained = first.Execute(work, first.SelectModels(work), true,
            () => provider.Requests.Count(request => request.Mode == "review") < 2, TestContext.Current.CancellationToken, null);
        Assert.Equal("stale", retained.Status);
        var original = Assert.Single(first.InspectWork(work).Control!.Work!.Corrections);
        Assert.Equal("completed", original.Status);
        var second = Controller(); var continued = work with { Actor = "Next authorized controller", PreviousExecution = retained };
        var limited = second.Execute(continued, second.SelectModels(continued), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", limited.Status); Assert.False(limited.Applied);
        var status = second.InspectWork(continued);
        Assert.False(status.Required); Assert.True(status.Control!.Work!.CorrectionOnly);
        var rounds = status.Control.Work.Corrections;
        Assert.Equal(new[] { 1, 2 }, rounds.Select(round => round.Number));
        Assert.Equal(original.Id, rounds[0].Id); Assert.All(rounds, round => Assert.Equal("completed", round.Status));
        Assert.Contains("Two automatic correction rounds", status.Control.Work.HoldReason!);
        var secondStage = Assert.Single(limited.Runs, run => run.Stage == "Implementation" && run.CorrectionRoundId == rounds[1].Id);
        Assert.Equal(continued.Actor, second.Show(repository.Path, secondStage.RunId).Run!.Manifest.Actor);
        var calls = provider.Requests.Count;
        var retry = second.Execute(continued with { PreviousExecution = limited }, second.SelectModels(continued), true, () => true,
            TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", retry.Status); Assert.Equal(calls, provider.Requests.Count);
        Assert.Equal(2, second.InspectWork(continued).Control!.Work!.Corrections.Count);
    }

    [Fact]
    public void StoryContinuationKeepsAStartedNonAdoptedRoundWithItsOriginalActorAndAction()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("started actor boundary baseline");
        var provider = new StoryExecutionProvider { ReviewFailures = 1, FailImplementationNumber = 2 };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var first = Controller(); var work = StoryWork(repository.Path);
        var failed = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(failed.Applied);
        var original = Assert.Single(first.InspectWork(work).Control!.Work!.Corrections); Assert.Equal("started", original.Status);
        var before = File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger));
        var other = work with { Actor = "Another authorized controller", PreviousExecution = failed };
        var second = Controller(); var held = second.InspectWork(other);
        Assert.Equal("held", held.Status); Assert.Contains(work.Actor, held.NextAction);
        Assert.Contains("original controller", held.NextAction); Assert.Contains("authority-wide finite accounting", held.NextAction);
        var unspecified = second.InspectWork(work with { Actor = "" });
        Assert.NotEqual("held", unspecified.Status); Assert.Contains(work.Actor, unspecified.NextAction);
        Assert.NotEqual("held", first.InspectWork(work).Status);
        var calls = provider.Requests.Count;
        var rejected = second.Execute(other, second.SelectModels(other), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(rejected.Applied); Assert.Equal(calls, provider.Requests.Count);
        Assert.Contains(rejected.Errors, error => error.Contains("original controller", StringComparison.Ordinal));
        Assert.DoesNotContain(rejected.Errors, error => error.Contains("Work accounting must be restored", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger)));
        var resumed = first.Execute(work with { PreviousExecution = failed }, first.SelectModels(work), true, () => true,
            TestContext.Current.CancellationToken, null);
        Assert.True(resumed.Applied, string.Join("; ", resumed.Errors));
        var finalRound = Assert.Single(first.InspectWork(work).Control!.Work!.Corrections);
        Assert.Equal(original.Id, finalRound.Id); Assert.Equal("completed", finalRound.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryContinuationAllowsAnotherActorToReviewCompletedNonAdoptedWork(bool revisedContract)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("non-adopted actor continuation baseline");
        var provider = new StoryExecutionProvider { ReviewFailures = 1 };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var first = Controller(); var work = StoryWork(repository.Path);
        var retained = first.Execute(work, first.SelectModels(work), true,
            () => provider.Requests.Count(request => request.Mode == "review") < 2,
            TestContext.Current.CancellationToken, null);
        Assert.Equal("stale", retained.Status);
        var originalRound = Assert.Single(first.InspectWork(work).Control!.Work!.Corrections);
        Assert.Equal("completed", originalRound.Status);
        var second = Controller();
        var continued = work with { Actor = "Another authorized controller", PreviousExecution = retained };
        if (revisedContract) continued = continued with { Task = continued.Task with { Description = "Implement the explicitly approved revised display contract." } };
        var completed = second.Execute(continued, second.SelectModels(continued), true, () => true,
            TestContext.Current.CancellationToken, null);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        Assert.Equal(revisedContract ? 3 : 2, provider.Requests.Count(request => request.Mode == "implement"));
        Assert.Equal(3, provider.Requests.Count(request => request.Mode == "review"));
        var currentRound = Assert.Single(second.InspectWork(continued).Control!.Work!.Corrections);
        Assert.Equal(originalRound.Id, currentRound.Id); Assert.Equal("completed", currentRound.Status);
        Assert.False(second.InspectWork(continued).Required);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StoryContinuationValidatesGrantBeforeStartingAndRetainsPreparationCost(bool baselineChanged, bool legacyGrant)
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository); repository.Write("app.txt", "original"); repository.CommitAll("grant material baseline");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { ReviewFailures = 3, DuringExecution = () => clock.Advance(2) };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller(); AllocateWorkFixture(repository, first, 180, 180);
        var work = GovernedStoryWork(repository.Path);
        var held = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", held.Status);
        var before = first.InspectWork(work).Control!.Work!;
        var grant = new CisAgentWorkDispositionRequest("simplify", before.Revision)
        {
            CorrectionRelationshipId = before.CorrectionRelationshipId,
            CorrectionFindingReferences = before.RequiredCorrectionFindingReferences,
            CorrectionMaximumSeconds = 60,
            Hypothesis = "The existing native assertion can replace the custom comparator for the retained acceptance invariant.",
            NextAction = "Replace only the custom comparator with the existing native assertion and run its focused test.",
            Evidence = ["agent-run:" + held.Runs.Last(run => run.Stage == "Review").RunId]
        };
        var granted = first.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "retained-material-grant.json", grant), WorkActor, "Authorize the retained bounded correction.");
        Assert.True(granted.ExitCode == 0, string.Join("; ", granted.Errors));
        var grantId = first.InspectWork(work).Control!.Work!.CorrectionContinuation!.Id;
        if (legacyGrant)
        {
            var path = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
            var ledger = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            ledger["corrections"]![before.CorrectionRelationshipId!]!["grant"]!.AsObject().Remove("authorizedDirection");
            File.WriteAllText(path, ledger.ToJsonString());
        }
        var calls = provider.Requests.Count;
        if (baselineChanged)
        {
            repository.Write("unrelated.txt", "An unrelated accepted repository change.");
            repository.CommitAll("change retained repository baseline before granted correction");
        }
        var admittedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.Read().ToUnixTimeMilliseconds()).ToString("O");
        var fresh = Controller();
        var result = fresh.Execute(work with { PreviousExecution = held }, fresh.SelectModels(work), true, () => true,
            TestContext.Current.CancellationToken, progress =>
            {
                if (progress.Message.StartsWith("Preparing isolated task workspace:", StringComparison.Ordinal)) clock.Advance(7);
            });
        var after = fresh.InspectWork(work).Control!.Work!;
        if (baselineChanged)
        {
            Assert.False(result.Applied);
            Assert.Contains(result.Errors, error => error.Contains("baseline changed", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(grantId, after.CorrectionContinuation!.Id);
            Assert.Equal(new[] { 1, 2 }, after.Corrections.Select(round => round.Number));
            Assert.Equal(before.Task!.ConsumedMilliseconds + 7_000, after.Task!.ConsumedMilliseconds);
            Assert.Equal(calls, provider.Requests.Count);
            File.Delete(Path.Combine(repository.Path, "unrelated.txt"));
            repository.CommitAll("restore the exact retained baseline tree without discarding history");
            clock.Advance(86_400); // A settled user-wait gap is not active preparation.
            result = fresh.Execute(work with { PreviousExecution = held }, fresh.SelectModels(work), true, () => true,
                TestContext.Current.CancellationToken, progress =>
                {
                    if (progress.Message.StartsWith("Preparing isolated task workspace:", StringComparison.Ordinal)) clock.Advance(7);
                });
            after = fresh.InspectWork(work).Control!.Work!;
        }
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Null(after.CorrectionContinuation);
        var round = after.Corrections[2];
        Assert.Equal(grantId, round.DispositionReference); Assert.Equal(60, round.MaximumSeconds);
        Assert.Equal(admittedAt, round.StartedAtUtc);
        Assert.Equal(before.Task!.ConsumedMilliseconds + (baselineChanged ? 18_000 : 11_000), after.Task!.ConsumedMilliseconds);
        Assert.Equal(new[] { "implement", "review" }, provider.Requests.Skip(calls).Select(request => request.Mode));
        foreach (var request in provider.Requests.Skip(calls))
        {
            var direction = ReadAuthorizedCorrectionDirection(request);
            Assert.Equal(grant.Hypothesis, direction.GetProperty("applicable").GetProperty("hypothesis").GetString());
            Assert.Equal(grant.NextAction, direction.GetProperty("applicable").GetProperty("nextAction").GetString());
            Assert.Equal(grantId, direction.GetProperty("applicable").GetProperty("dispositionReference").GetString());
        }
        Assert.All(provider.Requests.Skip(calls), request => Assert.True(request.Timeout <= TimeSpan.FromSeconds(baselineChanged ? 46 : 53)));
        Assert.Equal(1, after.Task.UsedAttempts);
        Assert.All(after.Corrections, round => Assert.Equal("completed", round.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryReportContinuationReusesItsVerifiedScratchTarget(bool interruptedCorrection)
    {
        using var repository = AgentRepository.Create(git: true);
        var provider = new StoryExecutionProvider
        {
            FailReviewNumber = interruptedCorrection ? null : 1,
            ReviewFailures = interruptedCorrection ? 1 : 0,
            FailImplementationNumber = interruptedCorrection ? 2 : null
        };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path) with { Repositories = [] };
        work = work with { Task = work.Task with { RepositoryIds = [] } };
        var first = Controller(); var selection = first.SelectModels(work);
        var failed = first.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(failed.Applied);
        var implementation = failed.Runs.First(run => run.Stage == "Implementation");
        var originalTarget = first.Show(repository.Path, implementation.RunId).Run!.Manifest.TargetRepositoryPath;
        var fresh = Controller();
        var completed = fresh.Execute(work with { PreviousExecution = failed }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        Assert.All(completed.Runs, run => Assert.Equal(originalTarget, fresh.Show(repository.Path, run.RunId).Run!.Manifest.TargetRepositoryPath));
        Assert.Equal(interruptedCorrection ? 3 : 1, provider.Requests.Count(request => request.Mode == "implement"));
        Assert.All(completed.Runs, run => Assert.Equal("task-report", run.RepositoryId));
    }

    [Theory]
    [InlineData("optional", 0, true, 2)]
    [InlineData("minor-required", 1, true, 4)]
    [InlineData("mixed", 1, true, 4)]
    [InlineData("ready-required", 0, false, 2)]
    [InlineData("revise-optional", 0, false, 2)]
    [InlineData("bad-disposition", 0, false, 2)]
    public void StoryContinuationUsesDispositionAndRejectsInconsistentReviews(string kind, int failures, bool applies, int calls)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("review semantics baseline");
        var provider = new StoryExecutionProvider { ReviewKind = kind, ReviewFailures = failures };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path);
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal(applies, result.Applied);
        Assert.Equal(calls, provider.Requests.Count);
        if (!applies) { Assert.Equal("review-failed", result.Status); Assert.Equal("original", repository.Read("app.txt")); return; }
        Assert.Empty(result.Errors);
        if (kind is "optional" or "mixed")
        {
            var finding = Assert.Single(result.StructuredFindings!);
            Assert.Equal("optional", finding.Disposition); Assert.False(finding.RequiresDecision);
            Assert.Equal("minor", finding.Severity); Assert.NotEmpty(finding.RunId); Assert.NotEmpty(finding.MaterialBasis);
        }
        if (failures == 0) Assert.Empty(service.InspectWork(work).Control?.Work?.Corrections ?? []);
        else
        {
            Assert.Single(service.InspectWork(work).Control!.Work!.Corrections);
            var correction = provider.Requests.Single(request => request.Prompt.Contains("<review-findings>"));
            var required = correction.Prompt.Split("<review-findings>")[1].Split("</review-findings>")[0];
            Assert.Contains("Criterion not met", required); Assert.DoesNotContain("Optional future polish", required);
        }
        Assert.All(provider.Requests.Where(request => request.Mode == "review"), request =>
        {
            Assert.Contains("necessityAssessment", request.Prompt);
            Assert.Contains("Ordinary helpers", request.Prompt);
            Assert.Contains("CIS work and cost context", request.Prompt);
        });
    }

    [Fact]
    public void StoryContinuationKeepsPendingFindingsVisibleWhenSelectedModelsChange()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("retained finding baseline");
        var provider = new StoryExecutionProvider { Findings = true };
        var models = new StoryContinuationModels(["gpt-6-luna", "gpt-6-sol", "gpt-6-astra"]);
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: models);
        var first = Controller(); var work = StoryWork(repository.Path);
        var held = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", held.Status); Assert.NotEmpty(held.StructuredFindings!);
        var calls = provider.Requests.Count; models.Models = ["gpt-6-sol", "gpt-6-astra"];
        var fresh = Controller(); var selection = fresh.SelectModels(work); Assert.NotEqual(held.Selection.Hash, selection.Hash);
        var retried = fresh.Execute(work with { PreviousExecution = held }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", retried.Status); Assert.Equal(calls, provider.Requests.Count);
        Assert.Equal(held.Findings, retried.Findings); Assert.Equal(held.StructuredFindings, retried.StructuredFindings);
        Assert.Equal(held.Runs.Select(run => run.RunId), retried.Runs.Select(run => run.RunId));
    }

    [Fact]
    public void StoryContinuationStartsApprovedRevisionWithoutRefundingOldCorrectionHistory()
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository); repository.Write("app.txt", "original"); repository.CommitAll("approved revision baseline");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { ReviewFailures = 3, DuringExecution = () => clock.Advance(2) };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller(); AllocateWorkFixture(repository, first, 180, 180);
        var work = GovernedStoryWork(repository.Path);
        var held = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", held.Status); Assert.Equal(2, first.InspectWork(work).Control!.Work!.Corrections.Count);
        var revised = work with { Task = work.Task with { Description = "Implement the newly approved display revision while preserving its native verification contract." }, PreviousExecution = held };
        var fresh = Controller(); var completed = fresh.Execute(revised, fresh.SelectModels(revised), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        var current = fresh.InspectWork(revised).Control!.Work!;
        Assert.Equal(new[] { 1, 2 }, current.Corrections.Select(round => round.Number));
        Assert.Equal(16_000, current.Task!.ConsumedMilliseconds); Assert.Equal(1, current.Task.UsedAttempts);
        Assert.Equal(4, provider.Requests.Count(request => request.Mode == "implement"));
    }

    [Fact]
    public void StoryContinuationSupersedesInterruptedOldContractWithoutResettingItsRound()
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository); repository.Write("app.txt", "original"); repository.CommitAll("interrupted revision baseline");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { ReviewFailures = 1, FailImplementationNumber = 2, DuringExecution = () => clock.Advance(2) };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller(); AllocateWorkFixture(repository, first, 180, 180); var work = GovernedStoryWork(repository.Path);
        var failed = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        var originalRound = Assert.Single(first.InspectWork(work).Control!.Work!.Corrections);
        Assert.Equal("started", originalRound.Status);
        var revised = work with { Task = work.Task with { Description = "Implement the explicitly approved revised display behavior with its scoped checks." }, PreviousExecution = failed };
        var fresh = Controller(); var result = fresh.Execute(revised, fresh.SelectModels(revised), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        var retained = Assert.Single(fresh.InspectWork(revised).Control!.Work!.Corrections);
        Assert.Equal(originalRound.Id, retained.Id); Assert.Equal(1, retained.Number); Assert.Equal("superseded", retained.Status);
        Assert.Equal(10_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void StoryContinuationRequiresControllerAssessmentAfterFailedReviewsWithoutReimplementing()
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository);
        repository.Write("docs/cis/references/agent-provider-profile.md", "| maximum-run-seconds | 30 | Fixture adopted reflection window. |\n| direct-dirty-working-tree | denied | Preserve isolation. |\n");
        repository.Write("app.txt", "original"); repository.CommitAll("controller assessment baseline");
        var clock = new WorkControlClock();
        var unavailableReviewer = new StoryExecutionProvider { MalformedReview = true, DuringExecution = () => clock.Advance(10) };
        AgentService Controller(StoryExecutionProvider provider) => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller(unavailableReviewer);
        AllocateWorkFixture(repository, first, 180, 180);
        var work = GovernedStoryWork(repository.Path);
        var failed = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("review-failed", failed.Status);
        var implementation = Assert.Single(failed.Runs, run => run.Stage == "Implementation");
        var retryWork = work with { PreviousExecution = failed };
        var second = Controller(unavailableReviewer);
        var failedAgain = second.Execute(retryWork, second.SelectModels(retryWork), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("review-failed", failedAgain.Status);
        Assert.Equal(3, unavailableReviewer.Requests.Count);
        Assert.Single(unavailableReviewer.Requests, request => request.Mode == "implement");
        var before = second.InspectWork(work).Control!.Work!;
        Assert.Equal(30_000, before.Task!.ConsumedMilliseconds);
        Assert.True(before.ReflectionDue); Assert.Empty(before.Corrections);
        Assert.Contains(before.Checkpoints, checkpoint => checkpoint.Origin == "story-execution-summary"
            && checkpoint.Checkpoint.Outcome.Contains("Independent review failed", StringComparison.Ordinal)
            && checkpoint.Checkpoint.Evidence.Any(reference => reference.StartsWith("agent-run:", StringComparison.Ordinal)));
        Assert.DoesNotContain(before.Checkpoints, checkpoint => checkpoint.Origin == "controller-progress");
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var bytes = File.ReadAllBytes(ledgerPath);
        var fresh = Controller(unavailableReviewer);
        var inspect = fresh.InspectWork(work);
        Assert.Equal("controller-assessment-required", inspect.Status);
        Assert.Contains("cis agent work progress CIS-0001 WORK-090", inspect.NextAction, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(ledgerPath));
        var heldWork = work with { PreviousExecution = failedAgain };
        var held = fresh.Execute(heldWork, fresh.SelectModels(heldWork), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("held", held.Status);
        Assert.Equal(3, unavailableReviewer.Requests.Count);
        Assert.Equal(implementation.RunId, Assert.Single(held.Runs, run => run.Stage == "Implementation").RunId);
        Assert.Equal(30_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);

        var assessment = new CisAgentWorkCheckpoint(
            "The implementation is retained; two independent reviews returned invalid evidence. No required implementation defect has been established.",
            failedAgain.Runs.Select(run => "agent-run:" + run.RunId).ToArray(),
            "Repair the reviewer availability/output path and reuse the same implementation rather than rewrite unreviewed work.",
            "Retry one independent review after the reviewer is repaired, within the remaining owner allowance.",
            "Review the retained candidate once; retain any new failure for another engineering assessment.")
        { CloseAttempt = false };
        var progress = fresh.RecordWorkProgress(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "controller-assessment.json", new CisAgentWorkProgressRequest(assessment, inspect.Control!.Work!.Revision)), WorkActor);
        Assert.Equal(0, progress.ExitCode);
        Assert.Equal(30_000, progress.Work!.LastControllerReflectionMilliseconds);
        Assert.Equal(before.LogicalAttemptId, progress.Work.LogicalAttemptId);
        Assert.False(progress.Work.ReflectionDue);

        var repairedReviewer = new StoryExecutionProvider { DuringExecution = () => clock.Advance(10) };
        var continued = Controller(repairedReviewer);
        var resumedWork = work with { PreviousExecution = held };
        var completed = continued.Execute(resumedWork, continued.SelectModels(resumedWork), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        Assert.Equal("review", Assert.Single(repairedReviewer.Requests).Mode);
        Assert.Equal(implementation.RunId, Assert.Single(completed.Runs, run => run.Stage == "Implementation").RunId);
        var after = continued.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(40_000, after.Task!.ConsumedMilliseconds);
        Assert.Equal(before.OwnerTaskId, after.OwnerTaskId); Assert.Equal(1, after.Task.UsedAttempts);
        Assert.Equal(before.LogicalAttemptId, after.LogicalAttemptId); Assert.Empty(after.Corrections);
    }

    [Fact]
    public void StoryContinuationChangesModelsWithoutReimplementingOrRelabellingRetainedWork()
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository); repository.Write("app.txt", "original"); repository.CommitAll("model continuation baseline");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { FailReviewNumber = 1, DuringExecution = () => clock.Advance(2) };
        var models = new StoryContinuationModels(["gpt-6-luna", "gpt-6-sol", "gpt-6-astra"]);
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: models, taskContracts: new StoryContinuationContracts());
        var work = GovernedStoryWork(repository.Path); var first = Controller();
        AllocateWorkFixture(repository, first, 180, 180);
        var failed = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(failed.Applied); Assert.Equal("gpt-6-luna", failed.Selection.Implementation!.Model);
        Assert.True(failed.Status == "review-failed", failed.Status + ": " + string.Join("; ", failed.Errors));
        var original = failed.Runs.Single(run => run.Stage == "Implementation");
        models.Models = ["gpt-6-sol", "gpt-6-astra"];
        var fresh = Controller(); var selection = fresh.SelectModels(work);
        Assert.Equal("gpt-6-sol", selection.Implementation!.Model); Assert.NotEqual(failed.Selection.Hash, selection.Hash);
        var result = fresh.Execute(work with { PreviousExecution = failed }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors)); Assert.Single(provider.Requests, request => request.Mode == "implement");
        var retained = result.Runs.Single(run => run.Stage == "Implementation");
        Assert.Equal(original.RunId, retained.RunId); Assert.Equal("gpt-6-luna", retained.Model);
        var cost = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(6_000, cost.Task!.ConsumedMilliseconds); Assert.Equal(1, cost.Task.UsedAttempts);
    }

    [Fact]
    public void StoryContinuationHoldsWhenNewReviewerMatchesActualRetainedImplementer()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("independent retained reviewer baseline");
        var provider = new StoryExecutionProvider { FailReviewNumber = 1 };
        var models = new StoryContinuationModels(["gpt-6-astra", "gpt-6-sol"]);
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: models);
        var work = StoryWork(repository.Path, true); var first = Controller();
        var failed = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("gpt-6-astra", failed.Selection.Implementation!.Model);
        models.Models = ["gpt-7-astra", "gpt-6-astra"];
        var fresh = Controller(); var selection = fresh.SelectModels(work);
        Assert.Equal("gpt-6-astra", selection.Review!.Model);
        var result = fresh.Execute(work with { PreviousExecution = failed }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("held", result.Status); Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.Contains("matches a retained implementer"));
        Assert.Equal(2, provider.Requests.Count); Assert.Equal("original", repository.Read("app.txt"));
    }

    [Fact]
    public void StoryNecessityAssessmentUsesActualDiffAndExistingIndependentReview()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("necessity review baseline");
        var assessment = new CisReviewNecessityAssessment("Retain the existing exact comparison invariant", "Use the existing native assertion helper",
            "Unknown: no separate runtime measurement was required", "Preserve the existing pass meaning with one maintained helper",
            "Avoid a second comparator and repeated full-suite invocation", "Continue this bounded native helper change");
        var provider = new StoryExecutionProvider
        {
            NecessityAssessment = assessment,
            InspectRequest = request =>
            {
                if (request.Mode == "implement")
                {
                    Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, "tests"));
                    File.WriteAllText(Path.Combine(request.WorkingDirectory, "tests", "comparison-helper.cs"), "// Existing native assertion, no second comparator.\n");
                }
                else
                {
                    Assert.Contains("comparison-helper.cs", request.Prompt);
                    Assert.Contains("diff --git", request.Prompt);
                    Assert.Contains("CIS work and cost context", request.Prompt);
                    Assert.Contains("smallest capable alternative", request.Prompt);
                }
            }
        };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path); var selection = service.SelectModels(work);
        var result = service.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors)); Assert.Equal(2, provider.Requests.Count);
        var review = service.Show(repository.Path, result.Runs.Single(run => run.Stage == "Review").RunId).Run!.Result!.Review!;
        Assert.Equal(assessment, review.NecessityAssessment);
        Assert.Contains("agent-fixture/tests/comparison-helper.cs", result.ChangedFiles);
    }

    [Fact]
    public void StoryContinuationRetainsTwoRoundsAcrossControllersAndLostCallerResult()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("durable correction baseline");
        var provider = new StoryExecutionProvider { Findings = true };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path);
        var first = Controller(); var selection = first.SelectModels(work);
        var stopped = first.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", stopped.Status); Assert.False(stopped.Applied);
        Assert.Equal(3, provider.Requests.Count(request => request.Mode == "implement"));
        var second = Controller();
        // Simulate a controller that lost its final response; persisted stage/round evidence remains.
        var retried = second.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", retried.Status); Assert.False(retried.Applied);
        Assert.Equal(3, provider.Requests.Count(request => request.Mode == "implement"));
        var status = second.InspectWork(work);
        Assert.False(status.Required); Assert.True(status.Control!.Work!.CorrectionOnly);
        Assert.Equal(new[] { 1, 2 }, status.Control.Work.Corrections.Select(round => round.Number));
        Assert.All(status.Control.Work.Corrections, round => Assert.Equal("completed", round.Status));
        Assert.Equal("original", repository.Read("app.txt"));
    }

    [Fact]
    public void StoryContinuationResumesInterruptedCorrectionWithoutAnotherRound()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("interrupted correction baseline");
        var provider = new StoryExecutionProvider { ReviewFailures = 1, FailImplementationNumber = 2 };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var work = StoryWork(repository.Path); var first = Controller(); var selection = first.SelectModels(work);
        var failed = first.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(failed.Applied);
        var started = Assert.Single(first.InspectWork(work).Control!.Work!.Corrections);
        Assert.Equal("started", started.Status);
        var fresh = Controller();
        var result = fresh.Execute(work with { PreviousExecution = failed }, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        var completed = Assert.Single(fresh.InspectWork(work).Control!.Work!.Corrections);
        Assert.Equal(started.Id, completed.Id); Assert.Equal("completed", completed.Status);
        Assert.Equal(1, completed.Number); Assert.Single(completed.Stages);
    }

    [Fact]
    public void GovernedStoryStagesShareCanonicalOwnerAndInclusiveMeasuredCost()
    {
        using var authority = AgentRepository.Create(git: true);
        using var participant = AgentRepository.Create(git: true);
        AdoptWorkControl(authority);
        authority.Write("app.txt", "original"); authority.CommitAll("authority baseline");
        participant.Write("app.txt", "original"); participant.CommitAll("participant baseline");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { ReviewKind = "optional", DuringExecution = () => clock.Advance(2) };
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        AllocateWorkFixture(authority, service, 180, 180);
        var work = GovernedStoryWork(authority.Path) with
        {
            Repositories = [new("agent-fixture", authority.Path, "docs/cis", "authority"),
            new("participant", participant.Path, "docs/cis", "participant")]
        };
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors)); Assert.Equal(4, provider.Requests.Count);
        var view = service.WorkStatus(authority.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(8_000, view.Task!.ConsumedMilliseconds); Assert.Equal(8_000, view.Change.ConsumedMilliseconds);
        Assert.Equal(0, view.Task.ReservedMilliseconds); Assert.Equal(1, view.Task.UsedAttempts);
        Assert.Equal(5, view.Executions.Count); Assert.All(view.Executions, execution => Assert.Equal("WORK-090", execution.OwnerTaskId));
        Assert.Single(view.Executions.Select(execution => execution.RootExecutionId).Distinct());
        Assert.All(result.Runs, run =>
        {
            var manifest = service.Show(authority.Path, run.RunId).Run!.Manifest;
            Assert.Equal("story-task", manifest.ExecutionRoute); Assert.Equal("CIS-0001", manifest.Work!.ChangeId);
            Assert.Equal(view.LogicalAttemptId, manifest.Work.LogicalAttemptId);
        });
        var representative = Assert.Single(view.Checkpoints, checkpoint => checkpoint.Origin == "story-execution-summary"
            && checkpoint.Checkpoint.Outcome.StartsWith("First representative repository result retained", StringComparison.Ordinal));
        Assert.Equal(2_000, representative.ActiveMilliseconds);
        Assert.Equal(0, view.LastControllerReflectionMilliseconds);
        Assert.Contains("No engineering continuation decision", representative.Checkpoint.Decision, StringComparison.Ordinal);
        Assert.Empty(view.Corrections);
        var beforeCalls = provider.Requests.Count;
        var resumed = service.Resume(authority.Path, result.Runs[0].RunId, "continue", WorkActor,
            "Use the existing story controller", false, TestContext.Current.CancellationToken);
        Assert.Equal("story-continuation-required", resumed.Status); Assert.Equal(beforeCalls, provider.Requests.Count);
        Assert.Equal(8_000, service.WorkStatus(authority.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryOwnerAndAllocationHoldBeforeDiagnosisAndPreparation(bool adoptedParticipant)
    {
        using var authority = AgentRepository.Create(git: true);
        using var participant = AgentRepository.Create(git: true);
        AdoptWorkControl(adoptedParticipant ? participant : authority);
        var provider = new StoryExecutionProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var work = GovernedStoryWork(authority.Path);
        if (adoptedParticipant) work = work with { Repositories = [new("participant", participant.Path, "docs/cis", "participant")] };
        work = work with { Task = work.Task with { ChangeId = null, WorkOwnerTaskId = null } };
        var status = service.InspectWork(work); Assert.True(status.Required); Assert.False(status.BindingCurrent);
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(result.Applied); Assert.NotEmpty(result.Errors); Assert.Empty(provider.Requests); Assert.Equal(0, provider.DiagnoseCalls);
        Assert.False(Directory.Exists(Path.Combine(authority.Path, AgentService.RootPath, "worktrees")));
    }

    private static CisStoryTaskWork GovernedStoryWork(string path)
    {
        var work = StoryWork(path);
        return work with
        {
            Actor = WorkActor,
            Task = work.Task with
            {
                ChangeId = "CIS-0001",
                WorkOwnerTaskId = "WORK-090",
                InstanceKey = "feature:story:task:T1",
                Category = "documentation",
                Complexity = "low",
                TaskTypeKey = "core.documentation.contracts",
                TaskTypeVersion = "1.0",
                Validation = "Review app.txt against the approved criterion.",
                RequiredOutputs = ["The bounded app.txt change"],
                VerificationObligations = [new("V1", work.Task.AcceptanceCriteria, "Check the approved output", "review", ["Independent review of app.txt"])]
            }
        };
    }

    private sealed class StoryContinuationContracts : ICisEngineeringTaskContractReader
    {
        public string ReadTaskDigest(string repositoryPath, string changeId, string taskId) => "sha256:fixture-contract";
        public IReadOnlyList<string> ValidateTaskType(string repositoryPath, string key, string version, string category) => [];
    }

    private sealed class StoryContinuationModels(IReadOnlyList<string> models) : ICisTextGenerationService
    {
        public IReadOnlyList<string> Models { get; set; } = models;
        public CisAiStatus GetStatus() => new([new("codex", "available", "fixture", false, Models.Select(model => new CisAiModel(model)).ToArray(), null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request) => throw new InvalidOperationException("Model discovery must not generate text.");
    }
}
