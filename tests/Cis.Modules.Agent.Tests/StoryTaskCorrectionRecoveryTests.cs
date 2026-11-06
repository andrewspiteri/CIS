using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void StoryCorrectionRecovery_DeliversGrantedAndExtendedDirectionInActualProviderRequests()
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository); repository.Write("app.txt", "original"); repository.CommitAll("direction recovery baseline");
        var clock = new WorkControlClock();
        var providerSeconds = 2;
        var provider = new StoryExecutionProvider
        {
            ReviewFailures = 3,
            FailImplementationNumber = 4,
            DuringExecution = () => clock.Advance(providerSeconds)
        };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller(); AllocateWorkFixture(repository, first, 240, 240);
        var work = GovernedStoryWork(repository.Path);
        var automatic = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", automatic.Status);
        var before = first.InspectWork(work).Control!.Work!;
        var grant = new CisAgentWorkDispositionRequest("simplify", before.Revision)
        {
            CorrectionRelationshipId = before.CorrectionRelationshipId,
            CorrectionFindingReferences = before.RequiredCorrectionFindingReferences,
            CorrectionMaximumSeconds = 60,
            Hypothesis = "Use the existing native assertion to replace the custom comparator.",
            NextAction = "Replace the comparator in the bounded acceptance check and run that one native test.",
            Evidence = ["agent-run:" + automatic.Runs.Last(run => run.Stage == "Review").RunId]
        };
        Assert.Equal(0, first.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "direction-grant.json", grant), WorkActor, "Authorize the native assertion correction.").ExitCode);
        var originalGrant = first.InspectWork(work).Control!.Work!.CorrectionContinuation!.Id;
        providerSeconds = 60;
        var initial = Controller();
        var interrupted = initial.Execute(work with { PreviousExecution = automatic }, initial.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("failed", interrupted.Status);
        var initialDirection = ReadAuthorizedCorrectionDirection(provider.Requests.Last());
        Assert.Equal("implement", provider.Requests.Last().Mode);
        Assert.Equal(grant.Hypothesis, initialDirection.GetProperty("applicable").GetProperty("hypothesis").GetString());
        Assert.Equal(grant.NextAction, initialDirection.GetProperty("applicable").GetProperty("nextAction").GetString());
        var stopped = initial.InspectWork(work).Control!.Work!;
        var startedRound = stopped.Corrections.Single(round => round.Status == "started");
        Assert.Equal(3, startedRound.Number); Assert.Equal(originalGrant, startedRound.DispositionReference);
        Assert.Equal(before.Task!.ConsumedMilliseconds + 60_000, stopped.Task!.ConsumedMilliseconds);

        // Emulate a pre-fix retained round: recover only its exact saved controller disposition.
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        ledger["corrections"]![before.CorrectionRelationshipId!]!["rounds"]![2]!.AsObject().Remove("authorizedDirections");
        File.WriteAllText(ledgerPath, ledger.ToJsonString());
        var extension = grant with
        {
            ExpectedRevision = stopped.Revision,
            Hypothesis = "The retained candidate supports the existing collection assertion without rebuilding its comparator.",
            NextAction = "Use the existing collection assertion directly and independently review only the retained corrected output.",
            Evidence = ["agent-run:" + interrupted.Runs.First(run => run.Stage == "Implementation").RunId]
        };
        var resumedController = Controller();
        var extended = resumedController.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "direction-extension.json", extension), WorkActor, "Authorize the next exact bounded native assertion step.");
        Assert.True(extended.ExitCode == 0, string.Join("; ", extended.Errors));
        var calls = provider.Requests.Count;
        providerSeconds = 2;
        var resumed = Controller();
        var result = resumed.Execute(work with { PreviousExecution = interrupted }, resumed.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal(new[] { "implement", "review" }, provider.Requests.Skip(calls).Select(request => request.Mode));
        foreach (var request in provider.Requests.Skip(calls))
        {
            var direction = ReadAuthorizedCorrectionDirection(request);
            var applicable = direction.GetProperty("applicable");
            Assert.Equal(extension.Hypothesis, applicable.GetProperty("hypothesis").GetString());
            Assert.Equal(extension.NextAction, applicable.GetProperty("nextAction").GetString());
            Assert.NotEqual(originalGrant, applicable.GetProperty("dispositionReference").GetString());
            var prior = Assert.Single(direction.GetProperty("prior").EnumerateArray());
            Assert.Equal(originalGrant, prior.GetProperty("dispositionReference").GetString());
            Assert.Equal(grant.Hypothesis, prior.GetProperty("hypothesis").GetString());
            Assert.Equal(grant.NextAction, prior.GetProperty("nextAction").GetString());
        }
        var after = resumed.InspectWork(work).Control!.Work!;
        var retained = after.Corrections.Single(round => round.Id == startedRound.Id);
        Assert.Equal(3, retained.Number); Assert.Equal(120, retained.MaximumSeconds);
        Assert.Equal(before.Corrections.Select(round => round.Id), after.Corrections.Take(2).Select(round => round.Id));
        Assert.Equal(stopped.Task.ConsumedMilliseconds + 4_000, after.Task!.ConsumedMilliseconds);
        Assert.Equal(stopped.Task.AllocatedMilliseconds, after.Task.AllocatedMilliseconds);
        Assert.Equal(1, after.Task.UsedAttempts);
        Assert.All(after.Corrections, round => Assert.Equal("completed", round.Status));
    }

    private static JsonElement ReadAuthorizedCorrectionDirection(CisAgentExecutionRequest request)
    {
        const string startTag = "<authorized-correction-direction>";
        const string endTag = "</authorized-correction-direction>";
        var start = request.Prompt.IndexOf(startTag, StringComparison.Ordinal);
        Assert.True(start >= 0, "The actual provider request must carry the authorized correction direction explicitly.");
        start += startTag.Length;
        var end = request.Prompt.IndexOf(endTag, start, StringComparison.Ordinal);
        Assert.True(end > start);
        using var document = JsonDocument.Parse(request.Prompt[start..end]);
        return document.RootElement.Clone();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void StoryCorrectionRecovery_HoldsMissingDispositionThenReconcilesFutureDirectionWithoutRefillingTheRound(int fractionalMilliseconds)
    {
        using var repository = AgentRepository.Create(git: true);
        AdoptWorkControl(repository); repository.Write("app.txt", "original"); repository.CommitAll("missing direction baseline");
        var clock = new WorkControlClock();
        var providerMilliseconds = 2000;
        var provider = new StoryExecutionProvider
        {
            ReviewFailures = 3,
            FailImplementationNumber = 4,
            DuringExecution = () => clock.AdvanceMilliseconds(providerMilliseconds)
        };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller(); AllocateWorkFixture(repository, first, 240, 240);
        var work = GovernedStoryWork(repository.Path);
        var automatic = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        var before = first.InspectWork(work).Control!.Work!;
        var grant = new CisAgentWorkDispositionRequest("simplify", before.Revision)
        {
            CorrectionRelationshipId = before.CorrectionRelationshipId,
            CorrectionFindingReferences = before.RequiredCorrectionFindingReferences,
            CorrectionMaximumSeconds = 60,
            Hypothesis = "Replace the custom comparator with the existing native assertion.",
            NextAction = "Apply the one native assertion substitution and run its bounded check.",
            Evidence = ["agent-run:" + automatic.Runs.Last(run => run.Stage == "Review").RunId]
        };
        Assert.Equal(0, first.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "missing-direction-grant.json", grant), WorkActor, "Authorize this bounded correction.").ExitCode);
        providerMilliseconds += fractionalMilliseconds;
        var interrupted = first.Execute(work with { PreviousExecution = automatic }, first.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("failed", interrupted.Status);
        var stopped = first.InspectWork(work).Control!.Work!;
        Assert.Equal(before.Task!.ConsumedMilliseconds + 2000 + fractionalMilliseconds, stopped.Task!.ConsumedMilliseconds);
        providerMilliseconds = 2000;
        var active = stopped.Corrections.Single(round => round.Status == "started");
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        ledger["corrections"]![before.CorrectionRelationshipId!]!["rounds"]![2]!.AsObject().Remove("authorizedDirections");
        var checkpoints = ledger["changes"]!["CIS-0001"]!["owners"]!["WORK-090"]!["checkpoints"]!.AsArray();
        var exact = checkpoints.Single(checkpoint => checkpoint?["origin"]?.GetValue<string>() == "controller-disposition"
            && checkpoint["checkpoint"]!["decision"]!.GetValue<string>().Contains("[continuation:" + active.DispositionReference + "]", StringComparison.Ordinal));
        checkpoints.Remove(exact);
        File.WriteAllText(ledgerPath, ledger.ToJsonString());
        var missingBytes = File.ReadAllBytes(ledgerPath);
        var next = Controller();
        var status = next.InspectWork(work);
        Assert.Equal("held", status.Status);
        Assert.Contains(active.DispositionReference!, status.Control!.Work!.HoldReason!);
        Assert.Contains("cis agent work disposition", status.Control.Work.NextAction);
        Assert.Equal(missingBytes, File.ReadAllBytes(ledgerPath));
        var calls = provider.Requests.Count;
        var denied = next.Execute(work with { PreviousExecution = interrupted }, next.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("held", denied.Status); Assert.Equal(calls, provider.Requests.Count);
        var reconcile = grant with
        {
            ExpectedRevision = next.InspectWork(work).Control!.Work!.Revision,
            CorrectionMaximumSeconds = 30,
            Hypothesis = "The retained output permits the existing collection assertion directly.",
            NextAction = "Use that collection assertion for the named acceptance criterion and review the exact corrected candidate."
        };
        var reconciled = next.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "reconcile-future-direction.json", reconcile), WorkActor,
            "Authorize only future direction; the unavailable original action remains unknown.");
        Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
        var resolved = next.InspectWork(work).Control!.Work!;
        var same = resolved.Corrections.Single(round => round.Id == active.Id);
        Assert.Equal(fractionalMilliseconds == 0 ? 32 : 33, same.MaximumSeconds);
        Assert.True(same.MaximumSeconds <= active.MaximumSeconds);
        Assert.Equal(active.DispositionReference, Assert.Single(same.UnresolvedDirectionReferences!));
        Assert.Equal(stopped.Task!.ConsumedMilliseconds, resolved.Task!.ConsumedMilliseconds);
        Assert.Equal(stopped.OwnerTaskId, resolved.OwnerTaskId); Assert.Equal(stopped.LogicalAttemptId, resolved.LogicalAttemptId);
        var resumed = Controller();
        var result = resumed.Execute(work with { PreviousExecution = interrupted }, resumed.SelectModels(work), true,
            () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal(new[] { "implement", "review" }, provider.Requests.Skip(calls).Select(request => request.Mode));
        foreach (var request in provider.Requests.Skip(calls))
        {
            var direction = ReadAuthorizedCorrectionDirection(request);
            Assert.Equal(reconcile.NextAction, direction.GetProperty("applicable").GetProperty("nextAction").GetString());
            Assert.Equal(reconcile.Hypothesis, direction.GetProperty("applicable").GetProperty("hypothesis").GetString());
            Assert.Empty(direction.GetProperty("prior").EnumerateArray());
            Assert.Equal(active.DispositionReference, Assert.Single(direction.GetProperty("unresolvedPriorDispositionReferences").EnumerateArray()).GetString());
            Assert.True(request.Timeout <= TimeSpan.FromSeconds(30));
        }
        var after = resumed.InspectWork(work).Control!.Work!;
        Assert.Equal(stopped.Task.ConsumedMilliseconds + 4_000, after.Task!.ConsumedMilliseconds);
        Assert.Equal(stopped.Task.AllocatedMilliseconds, after.Task.AllocatedMilliseconds);
        Assert.Equal(active.Id, after.Corrections[2].Id); Assert.Equal(3, after.Corrections.Count);
    }

    [Fact]
    public void StoryCorrectionRecovery_ReconstructsLostStageFromNativeEvidenceBeforeClosingItsRound()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original");
        repository.CommitAll("correction recovery baseline");
        var provider = new StoryExecutionProvider { ReviewFailures = 3, FailReviewNumber = 2 };
        var work = StoryWork(repository.Path, true);
        var firstController = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var selection = firstController.SelectModels(work);
        var interrupted = firstController.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("review-failed", interrupted.Status);
        Assert.False(interrupted.Applied);
        Assert.Equal("original", repository.Read("app.txt"));
        Assert.Equal(2, provider.Requests.Count(request => request.Mode == "implement"));
        var validReview = interrupted.Runs.First(run => run.Stage == "Review");
        var failedReview = interrupted.Runs.Last(run => run.Stage == "Review");
        Assert.NotEqual(validReview.RunId, failedReview.RunId);
        Assert.NotEmpty(interrupted.Findings);
        Assert.Equal(validReview.RunId, Assert.Single(interrupted.StructuredFindings!).RunId);
        Assert.Contains(validReview.RunId, interrupted.ReviewEvidenceRunIds!);
        Assert.DoesNotContain(failedReview.RunId, interrupted.ReviewEvidenceRunIds!);
        var firstRound = Assert.Single(interrupted.Work!.Control!.Work!.Corrections);
        Assert.Equal(1, firstRound.Number);
        Assert.Equal("started", firstRound.Status);
        var completedStage = Assert.Single(firstRound.Stages);
        var retainedRun = firstController.Show(repository.Path, completedStage.RunId).Run!;
        Assert.Equal(firstRound.Id, retainedRun.Manifest.StoryCorrectionRoundId);
        Assert.Equal(CisAgentRunStates.Succeeded, retainedRun.Manifest.Status);
        Assert.All(retainedRun.Artifacts, artifact => Assert.True(artifact.Valid));

        // The native run and candidate survived, but the controller stopped before
        // the completed-stage projection reached the ledger's atomic replacement.
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var round = ledger["corrections"]![firstRound.RelationshipId]!["rounds"]![0]!;
        round["stages"] = new JsonArray();
        File.WriteAllText(ledgerPath, ledger.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var nextController = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels());
        var retryWork = work with { PreviousExecution = interrupted };
        var resumed = nextController.Execute(retryWork, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(resumed.Applied, string.Join("; ", resumed.Errors));
        Assert.Equal("corrected", repository.Read("app.txt"));
        Assert.Equal(3, provider.Requests.Count(request => request.Mode == "implement"));
        Assert.Equal(4, provider.Requests.Count(request => request.Mode == "review"));
        var rounds = resumed.Work!.Control!.Work!.Corrections.OrderBy(item => item.Number).ToArray();
        Assert.Equal([1, 2], rounds.Select(item => item.Number).ToArray());
        Assert.All(rounds, item => Assert.Equal("completed", item.Status));
        Assert.Equal(firstRound.Id, rounds[0].Id);
        Assert.Equal(completedStage.RunId, Assert.Single(rounds[0].Stages).RunId);
        Assert.Single(nextController.Runs(repository.Path).Runs, run => run.Mode == "implement" && run.StoryCorrectionRoundId == firstRound.Id);
        var oldRun = nextController.Show(repository.Path, completedStage.RunId).Run!;
        Assert.Equal(CisAgentRunStates.Succeeded, oldRun.Manifest.Status);
        Assert.All(oldRun.Artifacts, artifact => Assert.True(artifact.Valid));
    }
}
