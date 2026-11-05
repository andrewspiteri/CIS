using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
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
