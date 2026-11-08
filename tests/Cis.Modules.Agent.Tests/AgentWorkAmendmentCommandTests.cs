using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("human")]
    [InlineData("json")]
    [InlineData("agent")]
    public void WorkAmendmentCommandDispatchesAbsoluteTaskTimeAndPreservesAllocationDecision(string format)
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var provider = new FakeProvider(duringExecution: _ => clock.AdvanceMilliseconds(2_976_185));
        var service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, taskSeconds: 3300, changeSeconds: 3600, restorationSeconds: 300);
        var executed = RunWork(service, repository, "fake", 3000);
        Assert.Equal(0, executed.ExitCode);
        var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(23_815, before.Task!.RemainingMilliseconds);
        var input = WriteWorkInput(repository, "task-time-amendment.json",
            new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:user-authorized-task-time-decision", before.Revision));
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(service, provider)).Build();

        var result = InvokeWorkCommand(application, "agent", "work", "amend-time", "CIS-0001", "WORK-090",
            "--input", input, "--actor", WorkActor, "--reason", "Apply the explicit synthetic task time decision.",
            "--repo", repository.Path, "--format", format);

        Assert.Equal(0, result.ExitCode);
        var after = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(3_577_000, after.Task!.AllocatedMilliseconds); Assert.Equal(300_815, after.Task.RemainingMilliseconds);
        Assert.Equal(3_600_000, after.Change.AllocatedMilliseconds); Assert.Equal(323_815, after.Change.RemainingMilliseconds);
        Assert.Equal(before.DecisionReference, after.DecisionReference);
        Assert.Equal(before.Task.ConsumedMilliseconds, after.Task.ConsumedMilliseconds);
        Assert.Equal(before.LogicalAttemptId, after.LogicalAttemptId);
        Assert.Equal(before.Task.UsedAttempts, after.Task.UsedAttempts);
        var amendment = Assert.Single(after.TaskTimeAmendments);
        if (format == "json")
        {
            using var parsed = JsonDocument.Parse(result.Output);
            Assert.Equal("task-time-amended", parsed.RootElement.GetProperty("status").GetString());
            var work = parsed.RootElement.GetProperty("work");
            Assert.Equal(after.Revision, work.GetProperty("revision").GetString());
            Assert.Equal(before.DecisionReference, work.GetProperty("decisionReference").GetString());
            Assert.Equal(3_577_000, work.GetProperty("task").GetProperty("allocatedMilliseconds").GetInt64());
            Assert.Equal(300_815, work.GetProperty("task").GetProperty("remainingMilliseconds").GetInt64());
            Assert.Equal(amendment.Reference, work.GetProperty("taskTimeAmendments")[0].GetProperty("reference").GetString());
            Assert.Equal(amendment.DecisionReference, work.GetProperty("taskTimeAmendments")[0].GetProperty("decisionReference").GetString());
        }
        else
        {
            Assert.Contains(amendment.Reference, result.Output, StringComparison.Ordinal);
            Assert.Contains(amendment.DecisionReference, result.Output, StringComparison.Ordinal);
            Assert.Contains(before.DecisionReference, result.Output, StringComparison.Ordinal);
            Assert.Contains(after.Revision, result.Output, StringComparison.Ordinal);
            Assert.Contains("3577000", result.Output, StringComparison.Ordinal);
            Assert.Contains("300815", result.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorkAmendmentCommandRejectsAnUnsupportedFormatBeforeChangingAccounting()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var provider = new FakeProvider();
        var service = WorkController(provider, new WorkControlClock());
        AllocateWorkFixture(repository, service, taskSeconds: 3300, changeSeconds: 3600, restorationSeconds: 300);
        var current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        var input = WriteWorkInput(repository, "task-time-amendment.json",
            new CisAgentWorkTaskTimeAmendmentRequest(3577, "fixture:user-authorized-task-time-decision", current.Revision));
        var ledger = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var before = File.ReadAllBytes(ledger);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(service, provider)).Build();

        var result = InvokeWorkCommand(application, "agent", "work", "amend-time", "CIS-0001", "WORK-090",
            "--input", input, "--actor", WorkActor, "--reason", "Apply the explicit synthetic task time decision.",
            "--repo", repository.Path, "--format", "xml");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(ledger));
        var after = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(current.Revision, after.Revision);
        Assert.Empty(after.TaskTimeAmendments);
    }
}
