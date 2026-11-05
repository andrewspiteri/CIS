using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void StoryWorkAllocation_CorrectionOnlyHistoryOptsIntoABoundedContinuationWithoutAdoptingQualityPolicy()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.Write("app.txt", "original"); repository.CommitAll("minimal bounded continuation baseline");
        var work = GovernedStoryWork(repository.Path);
        work = work with { Task = work.Task with { ChangeId = null, WorkOwnerTaskId = null } };
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { ReviewFailures = 3, DuringExecution = () => clock.Advance(2) };
        AgentService Controller() => new(new CisRepositoryContextResolver(), [provider], clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var first = Controller();
        var stopped = first.Execute(work, first.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Equal("changes-requested", stopped.Status);
        var retained = first.InspectWork(work).Control!.Work!;
        Assert.True(retained.CorrectionOnly);
        Assert.Equal([1, 2], retained.Corrections.Select(round => round.Number).ToArray());
        var bound = work with { CanonicalBinding = new("CIS-0001", "WORK-090"), PreviousExecution = null };
        Assert.True(first.InspectWork(bound).BindingCurrent);
        Assert.Contains("authority-wide finite work accounting", first.InspectWork(bound).NextAction, StringComparison.Ordinal);
        var sibling = work with { StoryId = "abcdef1234567891", Task = work.Task with { Id = "T2", InstanceKey = "feature:story:sibling" } };
        Assert.False(first.InspectWork(sibling).Required);
        var allocation = WorkAllocation(20_000, 20_000, 5, ["WORK-090"]) with { ExpectedRevision = retained.Revision };
        var input = WriteWorkInput(repository, "minimal-story-allocation.json", allocation);
        var allocated = first.AllocateWork(repository.Path, "CIS-0001", input, WorkActor,
            "Opt this retained story into an authorized finite allowance without adopting unrelated quality policy.");
        Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
        Assert.Contains("authority-wide finite work accounting", allocated.Work!.NextAction, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath)));
        var siblingStatus = first.InspectWork(sibling);
        Assert.True(siblingStatus.Required);
        Assert.False(siblingStatus.BindingCurrent);
        Assert.Contains("Sibling tasks", siblingStatus.NextAction, StringComparison.Ordinal);
        var beforeSibling = provider.Requests.Count;
        var heldSibling = first.Execute(sibling, first.SelectModels(sibling), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.False(heldSibling.Applied);
        Assert.NotEmpty(heldSibling.Errors);
        Assert.Equal(beforeSibling, provider.Requests.Count);
        var reconciliation = new CisAgentWorkDispositionRequest("reconcile-story-history", allocated.Work!.Revision)
        { CorrectionRelationshipId = retained.CorrectionRelationshipId, HistoricalRunIds = stopped.Runs.Select(run => run.RunId).ToArray() };
        var reconciled = first.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "minimal-story-history.json", reconciliation), WorkActor, "Retain all earlier native work through conservative once-only charges.");
        Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
        Assert.Equal(10_800_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        var current = first.WorkStatus(repository.Path, "CIS-0001", "WORK-090", retained.CorrectionRelationshipId).Work!;
        var disposition = new CisAgentWorkDispositionRequest("simplify", current.Revision)
        {
            CorrectionRelationshipId = retained.CorrectionRelationshipId,
            CorrectionFindingReferences = current.RequiredCorrectionFindingReferences,
            CorrectionMaximumSeconds = 60,
            Hypothesis = "Use the retained native implementation and correct only the named acceptance invariant.",
            NextAction = "Make one bounded correction and independently review the exact candidate.",
            Evidence = ["agent-run:" + stopped.Runs.Last(run => run.Stage == "Review").RunId]
        };
        var granted = first.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
            WriteWorkInput(repository, "minimal-story-grant.json", disposition), WorkActor, "Authorize one specific simpler step within the existing allowance.");
        Assert.True(granted.ExitCode == 0, string.Join("; ", granted.Errors));
        var before = provider.Requests.Count;
        var fresh = Controller();
        var completed = fresh.Execute(bound, fresh.SelectModels(bound), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        Assert.Equal(["implement", "review"], provider.Requests.Skip(before).Select(request => request.Mode).ToArray());
        Assert.Equal("corrected", repository.Read("app.txt"));
        var after = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090", retained.CorrectionRelationshipId).Work!;
        Assert.Equal([1, 2, 3], after.Corrections.Select(round => round.Number).ToArray());
        Assert.NotNull(after.Corrections[2].DispositionReference);
        Assert.Equal(10_804_000, after.Task!.ConsumedMilliseconds);
        Assert.Equal(1, after.Task.UsedAttempts);
        Assert.False(File.Exists(Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath)));
    }

    [Fact]
    public void StoryWorkAllocation_RegisteredParticipantControlsPermitTheRequiredAuthorityAllocationAndAdmission()
    {
        using var authority = AgentRepository.Create(git: true);
        using var participant = AgentRepository.Create(git: true);
        ConfigureParticipantTask(authority, participant);
        AdoptWorkControl(participant);
        authority.CommitAll("minimal authority allocation baseline");
        participant.Write("app.txt", "original"); participant.CommitAll("controlled participant baseline");
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        var workspace = registry.Resolve(authority.Path);
        Assert.Empty(workspace.Errors);
        var target = Assert.Single(workspace.Workspace!.Repositories, item => item.Id == "participant");
        var clock = new WorkControlClock();
        var provider = new StoryExecutionProvider { DuringExecution = () => clock.Advance(2) };
        var service = new AgentService(resolver, [provider], registry, clock: clock.Read,
            textGeneration: new StoryExecutionModels(), taskContracts: new StoryContinuationContracts());
        var work = GovernedStoryWork(authority.Path);
        work = work with { Repositories = [target], Task = work.Task with { RepositoryIds = [target.Id] } };
        var missing = service.InspectWork(work);
        Assert.True(missing.Required); Assert.True(missing.BindingCurrent);
        AllocateWorkFixture(authority, service, 180, 180);
        Assert.False(File.Exists(Path.Combine(authority.Path, CisEngineeringPolicy.RelativePath)));
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal("implemented", participant.Read("app.txt"));
        var measured = service.WorkStatus(authority.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(4_000, measured.Task!.ConsumedMilliseconds);
        Assert.Equal(1, measured.Task.UsedAttempts);
        Assert.All(result.Runs, run => Assert.Equal(measured.AuthorityRepositoryId, service.Show(authority.Path, run.RunId).Run!.Manifest.Work!.AuthorityRepositoryId));
        Assert.False(File.Exists(Path.Combine(participant.Path, CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void StoryWorkAllocation_OptionalUnprovedOwnerMetadataDoesNotAdoptAccountingOrBlockMinimalExecution()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.Write("app.txt", "original"); repository.CommitAll("optional owner metadata baseline");
        var provider = new StoryExecutionProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new StoryExecutionModels(),
            taskContracts: new StoryContinuationContracts());
        var work = GovernedStoryWork(repository.Path);
        var inspected = service.InspectWork(work);
        Assert.False(inspected.Required);
        Assert.False(inspected.BindingCurrent);
        Assert.Empty(inspected.Errors);
        var result = service.Execute(work, service.SelectModels(work), true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal("implemented", repository.Read("app.txt"));
        Assert.False(File.Exists(Path.Combine(repository.Path, CisAgentWorkPaths.Adoption)));
    }
}
