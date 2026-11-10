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
    public void WorkAccountingAdoption_ExplicitCommandAdoptsFiniteAccountingWithoutQualityPolicy(string format)
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var profilePath = Path.Combine(repository.Path, "docs/cis/references/agent-provider-profile.md");
        var profile = File.ReadAllBytes(profilePath);
        var provider = new FakeProvider();
        var service = WorkController(provider, new WorkControlClock());
        var request = FreshAccountingAllocation(repository);
        var input = WriteWorkInput(repository, "fresh-allocation.json", request);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(service, provider)).Build();

        var implicitResult = InvokeWorkCommand(application, "agent", "work", "allocate", "CIS-0001", "--input", input,
            "--actor", WorkActor, "--reason", "Record the explicit finite accounting decision.", "--repo", repository.Path, "--format", format);
        Assert.NotEqual(0, implicitResult.ExitCode);
        Assert.Contains("--adopt-accounting", implicitResult.Output, StringComparison.Ordinal);
        AssertNoAccountingBaseline(repository);

        var explicitResult = InvokeWorkCommand(application, "agent", "work", "allocate", "CIS-0001", "--input", input,
            "--actor", WorkActor, "--reason", "Record the explicit finite accounting decision.", "--adopt-accounting",
            "--repo", repository.Path, "--format", format);

        Assert.True(explicitResult.ExitCode == 0, explicitResult.Output);
        Assert.Contains("authority-wide finite work accounting", explicitResult.Output, StringComparison.Ordinal);
        Assert.Contains("Sibling tasks", explicitResult.Output, StringComparison.Ordinal);
        var allocated = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(request.DecisionReference, allocated.DecisionReference);
        Assert.Equal(request.StorageCeilingReference, allocated.StorageCeilingReference);
        Assert.Equal(180_000, allocated.Change.AllocatedMilliseconds);
        Assert.Equal(180_000, allocated.Task!.AllocatedMilliseconds);
        Assert.Equal(0, allocated.Task.ConsumedMilliseconds);
        Assert.True(File.Exists(Path.Combine(repository.Path, CisAgentWorkPaths.Adoption)));
        Assert.False(File.Exists(Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath)));
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis/engineering-adoption.json")));
        Assert.Equal(profile, File.ReadAllBytes(profilePath));
        using var adoption = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.Path, CisAgentWorkPaths.Adoption)));
        Assert.Equal(WorkActor, adoption.RootElement.GetProperty("actor").GetString());
        Assert.Equal("Record the explicit finite accounting decision.", adoption.RootElement.GetProperty("reason").GetString());
        Assert.Equal(0, provider.ExecuteCalls);
    }

    [Theory]
    [InlineData("owner-missing")]
    [InlineData("owner-ambiguous")]
    [InlineData("plan-draft")]
    [InlineData("plan-missing")]
    [InlineData("feature-stale")]
    [InlineData("impact-proposed")]
    [InlineData("impact-missing")]
    [InlineData("negative-time")]
    [InlineData("zero-attempts")]
    [InlineData("reserve-outside-limit")]
    [InlineData("storage-missing")]
    [InlineData("storage-zero")]
    [InlineData("storage-outside-authority")]
    [InlineData("storage-escaping")]
    [InlineData("historical-acknowledgment-missing")]
    [InlineData("actor-missing")]
    [InlineData("reason-missing")]
    [InlineData("decision-missing")]
    public void WorkAccountingAdoption_InvalidAuthorityOrAllocationCannotEstablishFreshAccounting(string invalid)
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var qualityPath = Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath);
        repository.Write(CisEngineeringPolicy.RelativePath, """{"schemaVersion":1,"requireTaskCompletion":false,"additionalGates":[]}""");
        var quality = File.ReadAllBytes(qualityPath);
        var request = FreshAccountingAllocation(repository);
        var actor = WorkActor;
        var reason = "Apply the authorized finite accounting-only decision.";
        switch (invalid)
        {
            case "owner-missing": File.Delete(Path.Combine(repository.Path, "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md")); break;
            case "owner-ambiguous":
                repository.Write("docs/cis/changes/CIS-0001/agent-tasks/duplicate.md", "---\ntask_id: WORK-090\ntask_status: Ready\n---\n# Duplicate owner\n");
                break;
            case "plan-draft": repository.AddEligibleChange("Draft"); break;
            case "plan-missing": File.Delete(Path.Combine(repository.Path, "docs/cis/changes/CIS-0001/plan.md")); break;
            case "feature-stale":
                repository.Write("feature.md", "# Existing bounded feature\n");
                repository.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\nfeature_spec_path: feature.md\nfeature_spec_sha256: sha256:stale\n---\n# Plan\n");
                break;
            case "impact-proposed": repository.Write("docs/cis/changes/CIS-0001/impact.md", "# Impact\n| IMPACT-1 | proposed |\n"); break;
            case "impact-missing": File.Delete(Path.Combine(repository.Path, "docs/cis/changes/CIS-0001/impact.md")); break;
            case "negative-time": request = request with { ChangeSeconds = -1 }; break;
            case "zero-attempts": request = request with { ChangeAttempts = 0 }; break;
            case "reserve-outside-limit": request = request with { RestorationSeconds = request.ChangeSeconds + 1 }; break;
            case "storage-missing": request = request with { Storage = null }; break;
            case "storage-zero": request = request with { Storage = request.Storage! with { MaximumBytes = 0 } }; break;
            case "storage-outside-authority": request = request with { Storage = new(1024, [new(Path.GetTempPath(), ".cis/local")]) }; break;
            case "storage-escaping": request = request with { Storage = new(1024, [new(repository.Path, "../outside")]) }; break;
            case "historical-acknowledgment-missing": request = request with { AcknowledgeHistoricalUnknown = false }; break;
            case "actor-missing": actor = ""; break;
            case "reason-missing": reason = ""; break;
            case "decision-missing": request = request with { DecisionReference = "" }; break;
            default: throw new InvalidOperationException(invalid);
        }
        var service = WorkController(new FakeProvider(), new WorkControlClock());

        var rejected = service.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "invalid-first-use.json", request),
            actor, reason, adoptAccounting: true);

        Assert.NotEqual(0, rejected.ExitCode);
        Assert.False(rejected.Applied);
        Assert.NotEmpty(rejected.Errors);
        AssertNoAccountingBaseline(repository);
        Assert.Equal(quality, File.ReadAllBytes(qualityPath));
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis/engineering-adoption.json")));
    }

    [Fact]
    public void WorkAccountingAdoption_RepeatedFlagPreservesActiveReservationsConsumptionAndInheritedCapacity()
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md", "---\ntask_id: WORK-091\ntask_status: Ready\ntargets: [\"agent-fixture\"]\n---\n# Separate bounded task\n");
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-092.md", "---\ntask_id: WORK-092\nparent_id: WORK-090\ntask_status: Ready\ntargets: [\"agent-fixture\"]\n---\n# Bounded child\n");
        var clock = new WorkControlClock();
        var request = FreshAccountingAllocation(repository);
        AgentService? service = null;
        CisAgentWorkView? activeBefore = null;
        CisAgentWorkView? activeAfter = null;
        var provider = new FakeProvider(duringExecution: _ =>
        {
            clock.Advance(2);
            var controller = Assert.IsType<AgentService>(service);
            var current = controller.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            activeBefore = current;
            var ledger = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
            var retained = File.ReadAllBytes(ledger);
            var reset = controller.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "not-a-new-baseline.json", request),
                WorkActor, "Repeating first-use intent cannot remove the required revision.", adoptAccounting: true);
            Assert.NotEqual(0, reset.ExitCode);
            Assert.Equal(retained, File.ReadAllBytes(ledger));
            var updated = controller.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "same-allocation.json",
                request with { ExpectedRevision = current.Revision }), WorkActor,
                "Retain the original allocation with its current revision.", adoptAccounting: true);
            Assert.True(updated.ExitCode == 0, string.Join("; ", updated.Errors));
            activeAfter = controller.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        });
        service = WorkController(provider, clock);
        var first = service.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "first-use.json", request),
            WorkActor, "Authorize finite accounting without engineering quality policy.", adoptAccounting: true);
        Assert.Equal(0, first.ExitCode);

        var sibling = RunWork(service, repository, "fake", 30, "WORK-091");
        Assert.NotEqual(0, sibling.ExitCode);
        Assert.Equal(0, provider.ExecuteCalls);
        var child = RunWork(service, repository, "fake", 30, "WORK-092");

        Assert.True(child.ExitCode == 0, string.Join("; ", child.Diagnostics));
        Assert.NotNull(activeBefore);
        Assert.Equal(30_000, activeBefore.Task!.ReservedMilliseconds);
        Assert.Equal(activeBefore.AdoptionId, activeAfter!.AdoptionId);
        Assert.Equal(activeBefore.Task.ReservedMilliseconds, activeAfter.Task!.ReservedMilliseconds);
        Assert.Equal(activeBefore.Task.ConsumedMilliseconds, activeAfter.Task.ConsumedMilliseconds);
        Assert.Equal(activeBefore.Task.UsedAttempts, activeAfter.Task.UsedAttempts);
        Assert.Equal(activeBefore.DecisionReference, activeAfter.DecisionReference);
        Assert.Equal("WORK-090", child.Run!.Manifest.Work!.OwnerTaskId);
        Assert.Equal("WORK-092", child.Run.Manifest.Work.TaskId);
        var settled = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(2_000, settled.Task!.ConsumedMilliseconds);
        Assert.Equal(0, settled.Task.ReservedMilliseconds);
        Assert.Equal(1, settled.Task.UsedAttempts);
        Assert.False(File.Exists(Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath)));
    }

    [Theory]
    [InlineData("missing-ledger")]
    [InlineData("missing-marker")]
    [InlineData("corrupt-ledger")]
    [InlineData("corrupt-marker")]
    [InlineData("historical-references")]
    public void WorkAccountingAdoption_FirstUseFlagCannotReplaceIncompleteAccountingOrGovernedHistory(string failure)
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var clock = new WorkControlClock();
        var service = WorkController(new FakeProvider(duringExecution: _ => clock.Advance(2)), clock);
        var input = WriteWorkInput(repository, "first-use.json", FreshAccountingAllocation(repository));
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001", input, WorkActor, "Authorized first use.", adoptAccounting: true).ExitCode);
        var run = RunWork(service, repository, "fake", 30);
        Assert.Equal(0, run.ExitCode);
        Assert.NotNull(run.Run!.Manifest.Work);
        var ledger = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var marker = Path.Combine(repository.Path, CisAgentWorkPaths.Adoption);
        switch (failure)
        {
            case "missing-ledger": File.Delete(ledger); break;
            case "missing-marker": File.Delete(marker); break;
            case "corrupt-ledger": File.WriteAllText(ledger, "{"); break;
            case "corrupt-marker": File.WriteAllText(marker, "{"); break;
            case "historical-references": File.Delete(ledger); File.Delete(marker); break;
        }
        var retainedLedger = File.Exists(ledger) ? File.ReadAllBytes(ledger) : null;
        var retainedMarker = File.Exists(marker) ? File.ReadAllBytes(marker) : null;

        var rejected = service.AllocateWork(repository.Path, "CIS-0001", input, WorkActor,
            "Explicit intent cannot reset retained consumption or uncertainty.", adoptAccounting: true);

        Assert.NotEqual(0, rejected.ExitCode);
        Assert.False(rejected.Applied);
        Assert.Equal(retainedLedger, File.Exists(ledger) ? File.ReadAllBytes(ledger) : null);
        Assert.Equal(retainedMarker, File.Exists(marker) ? File.ReadAllBytes(marker) : null);
        Assert.False(File.Exists(Path.Combine(repository.Path, CisEngineeringPolicy.RelativePath)));
    }

    [Fact]
    public void WorkAccountingAdoption_UnsupportedFormatCannotAdoptAccounting()
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var provider = new FakeProvider();
        var service = WorkController(provider, new WorkControlClock());
        var input = WriteWorkInput(repository, "first-use.json", FreshAccountingAllocation(repository));
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(service, provider)).Build();

        var rejected = InvokeWorkCommand(application, "agent", "work", "allocate", "CIS-0001", "--input", input,
            "--actor", WorkActor, "--reason", "Authorize accounting-only adoption.", "--adopt-accounting",
            "--repo", repository.Path, "--format", "xml");

        Assert.Equal(2, rejected.ExitCode);
        AssertNoAccountingBaseline(repository);
    }

    private static CisAgentWorkAllocationRequest FreshAccountingAllocation(AgentRepository repository)
        => WorkAllocation(180, 180, 5, ["WORK-090"]) with
        {
            Storage = new(16 * 1024 * 1024, [new(repository.Path, ".cis/local")])
            { RestorationBytes = 1024 * 1024, CleanupControllers = [WorkActor] }
        };

    private static void AssertNoAccountingBaseline(AgentRepository repository)
    {
        Assert.False(File.Exists(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger)));
        Assert.False(File.Exists(Path.Combine(repository.Path, CisAgentWorkPaths.Adoption)));
    }
}
