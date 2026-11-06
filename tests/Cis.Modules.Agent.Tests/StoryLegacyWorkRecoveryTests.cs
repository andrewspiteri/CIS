using System.Security.Cryptography;
using System.Text.Json;
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
    public void StoryLegacyWorkRecovery_ReconcilesUnpricedNativeHistoryAndReusesImplementationAfterAdoption(bool assessmentAlsoDueBeforeReconciliation)
    {
        using var authority = AgentRepository.Create(git: true);
        using var target = AgentRepository.Create(git: true);
        authority.Write("authority.txt", "unchanged authority baseline");
        authority.CommitAll("legacy story authority baseline");
        target.Write("app.txt", "original");
        target.CommitAll("legacy story target baseline");
        var work = GovernedStoryWork(authority.Path);
        work = work with
        {
            Repositories = [new("target", target.Path, "docs/cis", "participant")],
            Task = work.Task with { RepositoryIds = ["target"], ChangeId = null, WorkOwnerTaskId = null }
        };
        var originalDigest = CisStoryEngineeringContract.Digest(work);
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { FailReviewNumber = 1, DuringExecution = () => clock.Advance(2) };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller();
        var selection = first.SelectModels(work);
        var interrupted = first.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("review-failed", interrupted.Status);
        Assert.False(interrupted.Applied);
        Assert.Equal(2, interrupted.Runs.Count);
        Assert.Equal("original", target.Read("app.txt"));
        var implementation = Assert.Single(interrupted.Runs, run => run.Stage == "Implementation");
        var relationship = first.Show(authority.Path, implementation.RunId).Run!.Manifest.StoryRelationshipId!;
        Assert.False(string.IsNullOrWhiteSpace(relationship));
        foreach (var run in interrupted.Runs)
        {
            Assert.Null(first.Show(authority.Path, run.RunId).Run!.Manifest.Work);
            var root = Path.Combine(authority.Path, AgentService.RootPath, "runs", run.RunId);
            var manifestPath = Path.Combine(root, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest.Remove("storyRelationshipId");
            manifest.Remove("executionRoute");
            File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions));
            var inventoryPath = Path.Combine(root, "artifacts.json");
            var inventory = JsonSerializer.Deserialize<AgentRunArtifact[]>(File.ReadAllText(inventoryPath), JsonOptions)!;
            File.WriteAllText(inventoryPath, JsonSerializer.Serialize(inventory.Select(artifact => artifact.Path == "manifest.json"
                ? artifact with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestPath))) }
                : artifact), JsonOptions));
            var retained = first.Show(authority.Path, run.RunId);
            Assert.Empty(retained.Diagnostics);
            Assert.All(retained.Run!.Artifacts, artifact => Assert.True(artifact.Valid));
        }

        AdoptWorkControl(authority);
        var governed = Controller();
        AllocateWorkFixture(authority, governed, 10800, 10800);
        var bound = work with { CanonicalBinding = new("CIS-0001", "WORK-090"), PreviousExecution = null };
        Assert.Equal(originalDigest, CisStoryEngineeringContract.Digest(bound));
        var knownCost = 0L;
        if (assessmentAlsoDueBeforeReconciliation)
        {
            // A separate bounded direct operation at this same owner can reach
            // reflection while this story's older provider runs remain unpriced.
            var context = new CisRepositoryContextResolver().Resolve(authority.Path).Context!;
            var ledger = new AgentWorkLedger(clock.Read);
            var identity = governed.InspectWork(bound).Identity!;
            var operation = ledger.Admit(context, identity, "direct-before-history/1", 3600, 3600);
            Assert.True(operation.Admitted, string.Join("; ", operation.Result.Errors));
            clock.Advance(3600);
            Assert.Equal(0, ledger.Settle(context, operation.Reference!, "Succeeded", true,
                "Retained measured direct preparation at the same approved owner.", ["fixture:direct-before-history"]).ExitCode);
            knownCost = 3_600_000;
        }
        var ledgerPath = Path.Combine(authority.Path, CisAgentWorkPaths.Ledger);
        var beforeStatus = File.ReadAllBytes(ledgerPath);
        var inspected = governed.InspectWork(bound);
        Assert.True(inspected.Required);
        Assert.True(inspected.BindingCurrent);
        Assert.Equal("held", inspected.Status);
        Assert.Contains("reconcile-story-history", inspected.NextAction, StringComparison.Ordinal);
        Assert.Equal(beforeStatus, File.ReadAllBytes(ledgerPath));
        if (assessmentAlsoDueBeforeReconciliation)
        {
            Assert.True(inspected.Control!.Work!.ReflectionDue);
            Assert.Contains(inspected.Errors, error => error.Contains("Controller assessment is due", StringComparison.Ordinal));
            Assert.DoesNotContain("--input <assessment.json>", inspected.NextAction, StringComparison.Ordinal);
        }
        Assert.All(interrupted.Runs, run => Assert.Contains(inspected.Errors, error => error.Contains(run.RunId, StringComparison.Ordinal)));
        var beforeCalls = provider.Requests.Count;
        var beforeDiagnosis = provider.DiagnoseCalls;
        var held = governed.Execute(bound, governed.SelectModels(bound), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(held.Applied);
        Assert.Equal(beforeCalls, provider.Requests.Count);
        Assert.Equal(beforeDiagnosis, provider.DiagnoseCalls);

        var current = governed.WorkStatus(authority.Path, "CIS-0001", "WORK-090").Work!;
        var request = new CisAgentWorkDispositionRequest("reconcile-story-history", current.Revision)
        {
            CorrectionRelationshipId = relationship,
            HistoricalRunIds = interrupted.Runs.Select(run => run.RunId).ToArray(),
            Evidence = interrupted.Runs.Select(run => "agent-run:" + run.RunId).ToArray()
        };
        var input = WriteWorkInput(authority, "legacy-story-reconciliation.json", request);
        var reconciled = governed.DisposeWork(authority.Path, "CIS-0001", "WORK-090", input, WorkActor,
            "Attach verified retained native story history to its approved owner and conservatively charge its bounds.");
        Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
        Assert.Equal(knownCost + 3_600_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        var repeatedInput = WriteWorkInput(authority, "legacy-story-reconciliation.json", request with { ExpectedRevision = reconciled.Work.Revision });
        var repeated = governed.DisposeWork(authority.Path, "CIS-0001", "WORK-090", repeatedInput, WorkActor,
            "Verify that the same retained work cannot be charged or reset twice.");
        Assert.Equal("unchanged", repeated.Status);
        Assert.Equal(knownCost + 3_600_000, repeated.Work!.Task!.ConsumedMilliseconds);

        var fresh = Controller();
        var assessmentState = fresh.InspectWork(bound);
        Assert.Equal("controller-assessment-required", assessmentState.Status);
        var assessmentHeld = fresh.Execute(bound, fresh.SelectModels(bound), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("held", assessmentHeld.Status); Assert.Equal(beforeCalls, provider.Requests.Count);
        var assessment = new CisAgentWorkCheckpoint(
            "Historical implementation and its failed review are retained; their unmeasured effort has been conservatively charged once.",
            interrupted.Runs.Select(run => "agent-run:" + run.RunId).ToArray(),
            "Reuse the hash-verified implementation and obtain current independent review rather than repeat implementation.",
            "The remaining allocation supports one bounded review of the retained candidate.",
            "Review the retained candidate through the governed story route, preserving the historical charge.")
        { CloseAttempt = false };
        var assessed = fresh.RecordWorkProgress(authority.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(authority, "legacy-history-assessment.json", new CisAgentWorkProgressRequest(assessment, assessmentState.Control!.Work!.Revision)), WorkActor);
        Assert.Equal(0, assessed.ExitCode);
        Assert.Equal(knownCost + 3_600_000, assessed.Work!.Task!.ConsumedMilliseconds);
        var result = fresh.Execute(bound, fresh.SelectModels(bound), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal("implemented", target.Read("app.txt"));
        Assert.Single(provider.Requests, execution => execution.Mode == "implement");
        Assert.Equal(2, provider.Requests.Count(execution => execution.Mode == "review"));
        Assert.Contains(result.Runs, run => run.RunId == implementation.RunId);
        var measured = fresh.WorkStatus(authority.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(knownCost + 3_602_000, measured.Task!.ConsumedMilliseconds);
        Assert.Equal(knownCost + 3_602_000, measured.Change.ConsumedMilliseconds);
        Assert.Equal(0, measured.Task.ReservedMilliseconds);
        Assert.Equal(1, measured.Task.UsedAttempts);
        var original = fresh.Show(authority.Path, implementation.RunId);
        Assert.Empty(original.Diagnostics);
        Assert.Null(original.Run!.Manifest.Work);
        Assert.Equal(originalDigest, original.Run.Manifest.TaskContractDigest);
        Assert.All(original.Run.Artifacts, artifact => Assert.True(artifact.Valid));
    }
}
