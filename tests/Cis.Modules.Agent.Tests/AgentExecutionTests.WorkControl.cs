using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    private const string WorkActor = "Fixture controller";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkControl_CorrectionsA_CancelStopsEveryRecordedOwnedProcess(bool latestAlreadyExited)
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        WithWorkProcess(first => WithWorkProcess(second =>
        {
            var inner = new FakeProvider(duringExecution: _ => clock.Advance(1));
            var provider = new WorkCallbackProvider(inner, executing: (_, emit, _) =>
            {
                emit(new("provider-event", "First owned process.", "work/process", ProcessId: first.Id,
                    ProcessStartedAtUtc: first.StartTime.ToUniversalTime().ToString("O")));
                emit(new("provider-event", "Replacement owned process.", "work/process", ProcessId: second.Id,
                    ProcessStartedAtUtc: second.StartTime.ToUniversalTime().ToString("O")));
            });
            var service = WorkController(provider, clock);
            AllocateWorkFixture(repository, service, 120, 120);
            var terminal = RunWork(service, repository, "fake", 30);
            Assert.Equal(CisAgentRunStates.Succeeded, terminal.Run!.Manifest.Status);
            if (latestAlreadyExited)
            {
                second.Kill(entireProcessTree: true);
                Assert.True(second.WaitForExit(5000));
            }
            Assert.False(first.HasExited);
            var other = new FakeProvider();
            var fresh = WorkController(other, clock);
            Assert.NotEqual(0, RunWork(fresh, repository, "fake", 30).ExitCode);
            Assert.Equal(0, other.ExecuteCalls);
            Assert.Equal(30_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ReservedMilliseconds);

            var cancelled = fresh.Cancel(repository.Path, terminal.Run.Manifest.RunId, WorkActor,
                "Stop every verified owned process before releasing this reservation.");

            Assert.True(cancelled.ExitCode == 0, string.Join("; ", cancelled.Diagnostics));
            Assert.True(first.HasExited, "The earlier recorded owned process survived cancellation.");
            Assert.True(second.HasExited, "The latest recorded owned process survived cancellation.");
            Assert.Equal(terminal.Run.Manifest.Status, cancelled.Run!.Manifest.Status);
            Assert.Equal(terminal.Run.Manifest.FailureKind, cancelled.Run.Manifest.FailureKind);
            Assert.Equal(terminal.Run.Manifest.CompletedAtUtc, cancelled.Run.Manifest.CompletedAtUtc);
            Assert.Equal(terminal.Run.Result!.Status, cancelled.Run!.Result!.Status);
            var settled = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(0, settled.Task!.ReservedMilliseconds);
            Assert.All(settled.Executions, execution => Assert.True(execution.Finalized));
            Assert.Null(settled.HoldReason);
            fresh.Cancel(repository.Path, terminal.Run.Manifest.RunId, WorkActor, "Repeated cancellation must not settle twice.");
            fresh.Recover(repository.Path, terminal.Run.Manifest.RunId, WorkActor, "Repeated recovery must retain the settled cost.");
            var repeated = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(settled.Task.ConsumedMilliseconds, repeated.Task!.ConsumedMilliseconds);
            Assert.Equal(settled.Change.ConsumedMilliseconds, repeated.Change.ConsumedMilliseconds);
            Assert.Equal(0, repeated.Task.ReservedMilliseconds);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkControl_CorrectionsA_PartialCancellationStopsVerifiedProcessAndRetainsUnknownIdentity(bool missingProcessId)
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        WithWorkProcess(first => WithWorkProcess(second =>
        {
            var provider = new WorkCallbackProvider(new FakeProvider(), executing: (_, emit, _) =>
            {
                emit(new("provider-event", "Verified first owned process.", "work/process", ProcessId: first.Id,
                    ProcessStartedAtUtc: first.StartTime.ToUniversalTime().ToString("O")));
                emit(missingProcessId
                    ? new("provider-event", "Changed process identity has no recorded PID.", "work/process",
                        ProcessStartedAtUtc: second.StartTime.ToUniversalTime().ToString("O"))
                    : new("provider-event", "Changed process identity has no recorded start time.", "work/process", ProcessId: second.Id));
            });
            var service = WorkController(provider, clock);
            AllocateWorkFixture(repository, service, 120, 120);
            var terminal = RunWork(service, repository, "fake", 30);
            var runId = terminal.Run!.Manifest.RunId;
            if (!missingProcessId)
            {
                // An exact OS match remains unsupported by the PID-only journal
                // entry. It must never confer ownership on this manifest pair.
                var manifest = terminal.Run.Manifest with { ProcessStartedAtUtc = second.StartTime.ToUniversalTime().ToString("O") };
                var runRoot = System.IO.Path.Combine(repository.Path, AgentService.RootPath, "runs", runId);
                var manifestPath = System.IO.Path.Combine(runRoot, "manifest.json");
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
                var inventoryPath = System.IO.Path.Combine(runRoot, "artifacts.json");
                var inventory = JsonSerializer.Deserialize<AgentRunArtifact[]>(File.ReadAllText(inventoryPath), JsonOptions)!;
                File.WriteAllText(inventoryPath, JsonSerializer.Serialize(inventory.Select(artifact => artifact.Path == "manifest.json"
                    ? artifact with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestPath))) }
                    : artifact), JsonOptions));
                Assert.Equal(second.Id, manifest.ProcessId);
                Assert.InRange(Math.Abs((second.StartTime.ToUniversalTime()
                    - DateTimeOffset.Parse(manifest.ProcessStartedAtUtc!).UtcDateTime).TotalSeconds), 0, 2);
            }
            terminal = service.Show(repository.Path, runId);
            Assert.Empty(terminal.Diagnostics);
            var expected = terminal.Run!.Manifest;
            var expectedResult = terminal.Run.Result!.Status;
            var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            void AssertOutcomePreserved(AgentRunView run)
            {
                Assert.Equal(expected.Status, run.Manifest.Status);
                Assert.Equal(expected.FailureKind, run.Manifest.FailureKind);
                Assert.Equal(expected.CompletedAtUtc, run.Manifest.CompletedAtUtc);
                Assert.Equal(expectedResult, run.Result!.Status);
            }
            clock.Advance(1);
            var other = new FakeProvider();
            var fresh = WorkController(other, clock);

            var cancelled = fresh.Cancel(repository.Path, runId,
                WorkActor, "Stop independently verified ownership while retaining unresolved identity and whole-execution accounting.");

            Assert.NotEqual(0, cancelled.ExitCode);
            Assert.Contains(cancelled.Diagnostics, message => message.Contains("identit", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unresolved", StringComparison.OrdinalIgnoreCase) || message.Contains("recover", StringComparison.OrdinalIgnoreCase));
            Assert.True(first.HasExited, "CIS did not stop the independently verified owned process.");
            Assert.False(second.HasExited);
            AssertOutcomePreserved(cancelled.Run!);
            Assert.Contains(cancelled.Run!.Events, item => item.Kind == "cancellation-partial");
            var held = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(30_000, held.Task!.ReservedMilliseconds);
            Assert.Equal(before.Task!.ConsumedMilliseconds, held.Task.ConsumedMilliseconds);
            Assert.Equal(before.Change.ConsumedMilliseconds, held.Change.ConsumedMilliseconds);
            Assert.False(Assert.Single(held.Executions).Finalized);
            Assert.NotNull(held.HoldReason);
            Assert.NotEqual(0, RunWork(fresh, repository, "fake", 30).ExitCode);
            Assert.Equal(0, other.ExecuteCalls);

            var recovered = fresh.Recover(repository.Path, runId,
                WorkActor, "The known process stopped, but an incomplete retained identity remains unresolved.");

            Assert.NotEqual(0, recovered.ExitCode);
            Assert.False(second.HasExited);
            AssertOutcomePreserved(recovered.Run!);
            var unresolved = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(30_000, unresolved.Task!.ReservedMilliseconds);
            Assert.Equal(held.Task.ConsumedMilliseconds, unresolved.Task.ConsumedMilliseconds);
            Assert.False(Assert.Single(unresolved.Executions).Finalized);
        }));
    }

    [Fact]
    public void WorkControl_PartialCancellationDoesNotRehashEvidenceChangedAfterInitialValidation()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        WithWorkProcess(first => WithWorkProcess(second =>
        {
            var provider = new WorkCallbackProvider(new FakeProvider(), executing: (_, emit, _) =>
            {
                emit(new("provider-event", "Independently verified owned process.", "work/process", ProcessId: first.Id,
                    ProcessStartedAtUtc: first.StartTime.ToUniversalTime().ToString("O")));
                emit(new("provider-event", "Another owned identity remains incomplete.", "work/process",
                    ProcessStartedAtUtc: second.StartTime.ToUniversalTime().ToString("O")));
            });
            var service = WorkController(provider, clock);
            AllocateWorkFixture(repository, service, 120, 120);
            var terminal = RunWork(service, repository, "fake", 30);
            var runId = terminal.Run!.Manifest.RunId;
            Assert.Empty(service.Show(repository.Path, runId).Diagnostics);
            var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var runRoot = System.IO.Path.Combine(repository.Path, AgentService.RootPath, "runs", runId);
            var resultPath = System.IO.Path.Combine(runRoot, "result.json");
            var inventoryPath = System.IO.Path.Combine(runRoot, "artifacts.json");
            var originalInventory = File.ReadAllBytes(inventoryPath);
            var changed = 0;
            var armed = false;
            var fresh = new AgentService(new CisRepositoryContextResolver(), [new FakeProvider()], clock: () =>
            {
                if (armed && Interlocked.CompareExchange(ref changed, 1, 0) == 0)
                    File.AppendAllText(resultPath, " ");
                return clock.Read();
            }, taskContracts: new MutableTaskContract());
            armed = true;

            var cancelled = fresh.Cancel(repository.Path, runId, WorkActor,
                "Preserve the original evidence inventory if evidence changes after initial cancellation validation.");

            Assert.Equal(1, changed);
            Assert.NotEqual(0, cancelled.ExitCode);
            Assert.True(first.HasExited);
            Assert.False(second.HasExited);
            Assert.Equal(originalInventory, File.ReadAllBytes(inventoryPath));
            var held = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(before.AdoptionId, held.AdoptionId);
            Assert.Equal(before.Task!.ConsumedMilliseconds, held.Task!.ConsumedMilliseconds);
            Assert.Equal(30_000, held.Task.ReservedMilliseconds);
            Assert.False(Assert.Single(held.Executions).Finalized);

            var shown = service.Show(repository.Path, runId);
            var recovered = fresh.Recover(repository.Path, runId, WorkActor,
                "The stale evidence inventory remains unresolved after partial cleanup.");

            Assert.Contains(shown.Diagnostics, message => message.Contains("artifact inventory", StringComparison.OrdinalIgnoreCase)
                && message.Contains("result.json", StringComparison.Ordinal));
            Assert.NotEqual(0, recovered.ExitCode);
            Assert.Contains(recovered.Diagnostics, message => message.Contains("artifact inventory", StringComparison.OrdinalIgnoreCase)
                && message.Contains("result.json", StringComparison.Ordinal));
            Assert.False(second.HasExited);
            Assert.Equal(originalInventory, File.ReadAllBytes(inventoryPath));
            Assert.Equal(30_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ReservedMilliseconds);
        }));
    }

    [Fact]
    public void WorkControl_CorrectionsB_ActivePreparationCannotBeReconciledAfterTheGracePeriod()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var inner = new FakeProvider(duringExecution: _ => clock.Advance(2));
        AgentService? service = null;
        CisAgentWorkResult? competing = null;
        CisAgentWorkView? protectedPreparation = null;
        var provider = new WorkCallbackProvider(inner, diagnosing: () =>
        {
            clock.Advance(31);
            var original = Assert.IsType<AgentService>(service);
            var current = original.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var execution = Assert.Single(current.Executions);
            var input = WriteWorkInput(repository, "reconcile-active-preparation.json", new CisAgentWorkDispositionRequest("reconcile", current.Revision)
            {
                ExecutionId = execution.ExecutionId,
                Evidence = ["execution:" + execution.ExecutionId],
                NextAction = "Inspect the original invocation before deciding whether preparation stopped.",
            });
            competing = WorkController(new FakeProvider(), clock).DisposeWork(repository.Path, "CIS-0001", "WORK-090", input,
                WorkActor, "Elapsed grace and an absent provider PID must not override the active preparer.");
            protectedPreparation = original.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work;
            clock.Advance(10);
        });
        service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 120, 120);

        var completed = RunWork(service, repository, "fake", 60);

        Assert.NotNull(competing);
        Assert.NotEqual(0, competing.ExitCode);
        Assert.Equal(60_000, protectedPreparation!.Task!.ReservedMilliseconds);
        Assert.False(Assert.Single(protectedPreparation.Executions).Finalized);
        Assert.True(completed.ExitCode == 0, string.Join("; ", completed.Diagnostics));
        Assert.Equal(1, inner.ExecuteCalls);
        var settled = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(43_000, settled.Task!.ConsumedMilliseconds);
        Assert.Equal(0, settled.Task.ReservedMilliseconds);
        Assert.True(Assert.Single(settled.Executions).Finalized);
        service.Recover(repository.Path, completed.Run!.Manifest.RunId, WorkActor, "The known preparer already settled its complete cost.");
        Assert.Equal(43_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void WorkControl_CorrectionsC_DelegatedJsonUpdatesPreserveTheTaskRestorationReserve()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        var clock = new WorkControlClock();
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(10));
        var service = WorkController(provider, clock);
        const string delegatedActor = "Fixture delegate";
        var allocation = new CisAgentWorkAllocationRequest(240, 12, 30,
            [new("WORK-090", 120, 10, 30, Fixed: false, AllowRedistribution: true),
             new("WORK-091", 60, 2, 0, Fixed: false, AllowRedistribution: true)],
            "fixture:delegated-transfer", "fixture:existing-storage-ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = [WorkActor, delegatedActor] };
        var initial = WriteWorkInput(repository, "delegated-allocation.json", allocation);
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001", initial, WorkActor, "Synthetic delegated allocation.").ExitCode);
        Assert.Equal(0, RunWork(service, repository, "fake", 30).ExitCode);
        var prior = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        var transfer = allocation with
        {
            ExpectedRevision = prior.Revision,
            Tasks = [new("WORK-090", 110, 10, 30, Fixed: false, AllowRedistribution: true),
                     new("WORK-091", 70, 2, 0, Fixed: false, AllowRedistribution: true)],
        };
        var transferred = service.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "valid-transfer.json", transfer),
            delegatedActor, "Transfer ten unreserved seconds while preserving both owners' protected capacity.");
        Assert.True(transferred.ExitCode == 0, string.Join("; ", transferred.Errors));
        var current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(110_000, current.Task!.AllocatedMilliseconds);
        Assert.Equal(70_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-091").Work!.Task!.AllocatedMilliseconds);
        var omitted = WriteWorkInput(repository, "omitted-task-restoration.json", new
        {
            changeSeconds = 240,
            changeAttempts = 12,
            restorationSeconds = 30,
            tasks = new[] { new { taskId = "WORK-090", seconds = 110, attempts = 10, @fixed = false, allowRedistribution = true } },
            decisionReference = allocation.DecisionReference,
            storageCeilingReference = allocation.StorageCeilingReference,
            expectedRevision = current.Revision,
        });

        var omittedResult = service.AllocateWork(repository.Path, "CIS-0001", omitted, delegatedActor,
            "Omitting an optional field must not spend protected task restoration.");

        Assert.True(omittedResult.ExitCode == 0 || omittedResult.Errors.Count > 0);
        current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(30_000, current.Task!.RestorationMilliseconds);
        var explicitZero = transfer with
        {
            ExpectedRevision = current.Revision,
            Tasks = [new("WORK-090", 110, 10, 0, Fixed: false, AllowRedistribution: true)],
        };
        var zeroResult = service.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "zero-task-restoration.json", explicitZero),
            delegatedActor, "Delegated redistribution does not authorize reducing a protected restoration reserve.");
        Assert.True(zeroResult.ExitCode == 0 || zeroResult.Errors.Count > 0);
        var preserved = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(30_000, preserved.Task!.RestorationMilliseconds);
        Assert.Equal(prior.Task!.ConsumedMilliseconds, preserved.Task.ConsumedMilliseconds);
        Assert.Equal(prior.Task.ReservedMilliseconds, preserved.Task.ReservedMilliseconds);
        Assert.Equal(prior.Task.UsedAttempts, preserved.Task.UsedAttempts);
        Assert.Equal(70_000, preserved.Task.RemainingMilliseconds);
        Assert.NotEqual(0, RunWork(service, repository, "fake", 80).ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
    }

    [Fact]
    public void WorkControl_CorrectionsD_CumulativeReflectionUsesHeadlessProgressWithoutClosingTheAttempt()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        var clock = new WorkControlClock();
        var stepSeconds = 1800;
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(stepSeconds));
        var service = WorkController(provider, clock);
        var allocation = WorkAllocation(10_800, 10_800, 0, ["WORK-090", "WORK-091"]) with
        { Tasks = [new("WORK-090", 10_770, 10), new("WORK-091", 30, 2)] };
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "reflection-allocation.json", allocation),
            WorkActor, "Synthetic allocation with a separate hard-limited owner.").ExitCode);
        var first = RunWork(service, repository, "fake", 1800);
        var second = RunWork(service, repository, "fake", 1800);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        var spent = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(3_600_000, spent.Task!.ConsumedMilliseconds);
        stepSeconds = 10;
        var fresh = WorkController(provider, clock);

        var due = RunWork(fresh, repository, "fake", 30);

        Assert.NotEqual(0, due.ExitCode);
        Assert.Equal(2, provider.ExecuteCalls);
        Assert.Contains(due.Diagnostics, message => message.Contains("reflect", StringComparison.OrdinalIgnoreCase)
            || message.Contains("progress", StringComparison.OrdinalIgnoreCase));
        var current = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        var checkpoint = new CisAgentWorkCheckpoint("Two bounded steps completed; the next verification remains in the accepted scope.",
            ["run:" + first.Run!.Manifest.RunId, "run:" + second.Run!.Manifest.RunId],
            "Continue through the existing approved module seam.", "continue", "Run one bounded verification step.")
        { Hypothesis = "The existing seam satisfies the accepted task.", ExpectedResult = "A verified bounded continuation.", CloseAttempt = false };
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(fresh, provider)).Build();
        var progress = InvokeWorkCommand(application, "agent", "work", "progress", "CIS-0001", "WORK-090", "--input",
            WriteWorkInput(repository, "due-reflection.json", new CisAgentWorkProgressRequest(checkpoint, current.Revision)),
            "--actor", WorkActor, "--repo", repository.Path, "--format", "json");
        Assert.Equal(0, progress.ExitCode);
        var reflected = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(spent.Task.ConsumedMilliseconds, reflected.Task!.ConsumedMilliseconds);
        Assert.Equal(spent.Task.UsedAttempts, reflected.Task.UsedAttempts);
        Assert.Equal(spent.LogicalAttemptId, reflected.LogicalAttemptId);
        Assert.Equal(0, RunWork(fresh, repository, "fake", 30).ExitCode);
        Assert.Equal(3, provider.ExecuteCalls);
        Assert.Equal(3_610_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
        Assert.Contains("task_status: Ready", File.ReadAllText(System.IO.Path.Combine(repository.Path,
            "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md")), StringComparison.Ordinal);

        stepSeconds = 30;
        var hardRun = RunWork(fresh, repository, "fake", 30, "WORK-091");
        Assert.Equal(0, hardRun.ExitCode);
        var hard = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-091").Work!;
        var hardCheckpoint = checkpoint with { Evidence = ["run:" + hardRun.Run!.Manifest.RunId] };
        var recorded = fresh.RecordWorkProgress(repository.Path, "CIS-0001", "WORK-091", WriteWorkInput(repository, "hard-reflection.json",
            new CisAgentWorkProgressRequest(hardCheckpoint, hard.Revision)), WorkActor);
        Assert.Equal(0, recorded.ExitCode);
        Assert.NotEqual(0, RunWork(fresh, repository, "fake", 30, "WORK-091").ExitCode);
        Assert.Equal(4, provider.ExecuteCalls);
        Assert.Equal(30_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-091").Work!.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void WorkControl_ConsumedAllowanceSurvivesFreshRunResumeAndProviderChange()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(30));
        var service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 35, 35, restorationSeconds: 5);

        var executed = RunWork(service, repository, "fake", 30);

        Assert.Equal(0, executed.ExitCode);
        Assert.Equal(CisAgentRunStates.Succeeded, executed.Run!.Manifest.Status);
        var consumed = Assert.IsType<CisAgentWorkView>(service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work);
        Assert.Equal("WORK-090", consumed.OwnerTaskId);
        Assert.Equal(30_000, consumed.Change.ConsumedMilliseconds);
        Assert.Equal(30_000, consumed.Task!.ConsumedMilliseconds);
        Assert.Equal(0, consumed.Task.RemainingMilliseconds);
        Assert.Equal(0, consumed.Task.ReservedMilliseconds);

        var alternate = new FakeProvider(id: "alternate");
        var fresh = WorkController(provider, clock, alternate);
        var repeated = RunWork(fresh, repository, "alternate", 30);
        var resumed = fresh.Resume(repository.Path, executed.Run.Manifest.RunId, "Continue the same task.", WorkActor,
            "Continue within the original allocation.", true, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, repeated.ExitCode);
        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Equal(0, alternate.ExecuteCalls);
        Assert.Equal(30_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
        var exhausted = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        var simplify = WriteWorkInput(repository, "simplify-exhausted.json", new CisAgentWorkDispositionRequest("simplify", exhausted.Revision)
        {
            Hypothesis = "Use one existing lookup instead of extending the traversal.",
            Evidence = ["run:" + executed.Run.Manifest.RunId],
            NextAction = "Inspect the existing lookup within the original scope.",
        });
        var hardHold = fresh.DisposeWork(repository.Path, "CIS-0001", "WORK-090", simplify, WorkActor, "Use a smaller in-scope approach.");
        Assert.NotNull(hardHold.Work!.HoldReason);
        Assert.Equal(30_000, hardHold.Work.Task!.ConsumedMilliseconds);
        Assert.NotEqual(0, RunWork(fresh, repository, "alternate", 30).ExitCode);
        Assert.Equal(0, alternate.ExecuteCalls);

        var before = WorkControlFiles(repository);
        var shown = fresh.Show(repository.Path, executed.Run.Manifest.RunId);
        fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090");
        Assert.Equal(CisAgentRunStates.Succeeded, shown.Run!.Manifest.Status);
        Assert.NotEmpty(shown.Run.Events);
        AssertWorkControlFilesUnchanged(repository, before);
    }

    [Fact]
    public async Task WorkControl_ChangeReservationProtectsDifferentTaskLocksAcrossControllers()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        var clock = new WorkControlClock();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var firstProvider = new FakeProvider(duringExecution: _ =>
        {
            clock.Advance(5);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        });
        var otherProvider = new FakeProvider(id: "other");
        var firstService = WorkController(firstProvider, clock);
        var secondService = WorkController(otherProvider, clock);
        AllocateWorkFixture(repository, firstService, 40, 60, restorationSeconds: 5, tasks: ["WORK-090", "WORK-091"]);
        var execution = Task.Run(() => RunWork(firstService, repository, "fake", 30), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            var reserved = secondService.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.True(reserved.Change.ReservedMilliseconds >= 30_000);

            var competing = RunWork(secondService, repository, "other", 30, "WORK-091");

            Assert.NotEqual(0, competing.ExitCode);
            Assert.Equal(0, otherProvider.ExecuteCalls);
            Assert.Equal("WORK-091", competing.Work!.TaskId);
            Assert.True(competing.Work.Task!.RemainingMilliseconds >= 30_000);
            Assert.True(competing.Work.Change.RemainingMilliseconds < 30_000);
        }
        finally { release.Set(); }
        var completed = await execution.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(0, completed.ExitCode);
        Assert.Equal(5_000, secondService.WorkStatus(repository.Path, "CIS-0001").Work!.Change.ConsumedMilliseconds);
        Assert.Equal(0, secondService.WorkStatus(repository.Path, "CIS-0001").Work!.Change.ReservedMilliseconds);
    }

    [Fact]
    public async Task WorkControl_InvalidResultAndConcurrentCancellationRetainCostWithoutDoubleSettlement()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var failedProvider = new FakeProvider(malformed: true, duringExecution: _ => clock.Advance(10));
        using var blockingProvider = new BlockingProcessProvider();
        var service = WorkController(failedProvider, clock, blockingProvider);
        AllocateWorkFixture(repository, service, 120, 120, restorationSeconds: 5);

        var failed = RunWork(service, repository, "fake", 30);

        Assert.NotEqual(0, failed.ExitCode);
        Assert.Equal(CisAgentRunStates.InvalidEvidence, failed.Run!.Manifest.Status);
        Assert.Equal(10_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
        var execution = Task.Run(() => service.Run(repository.Path, "CIS-0001", "WORK-090", "blocking", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, null, 30, false, WorkActor, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.True(blockingProvider.Started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        clock.Advance(7);
        var cancelled = WorkController(failedProvider, clock, blockingProvider).Cancel(repository.Path,
            blockingProvider.RunId!, WorkActor, "Stop this bounded owned execution.");
        var completed = await execution.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.True(cancelled.ExitCode == 0, string.Join("; ", cancelled.Diagnostics));
        Assert.Equal(CisAgentRunStates.Cancelled, completed.Run!.Manifest.Status);
        var settled = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(17_000, settled.Task!.ConsumedMilliseconds);
        Assert.Equal(0, settled.Task.ReservedMilliseconds);
        Assert.NotEqual(0, service.Cancel(repository.Path, blockingProvider.RunId!, WorkActor, "Repeated cancellation.").ExitCode);
        Assert.NotEqual(0, service.Recover(repository.Path, blockingProvider.RunId!, WorkActor, "Already settled execution.").ExitCode);
        var stable = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(settled.Change.ConsumedMilliseconds, stable.Change.ConsumedMilliseconds);
        Assert.Equal(settled.Task.ConsumedMilliseconds, stable.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void WorkControl_LaunchWithoutRecordedProcessIdentityCannotBeRecoveredAsUnusedCapacity()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        var clock = new WorkControlClock();
        var other = new FakeProvider(id: "other");
        var provider = new FakeProvider(duringExecution: request =>
        {
            clock.Advance(31);
            var fresh = WorkController(other, clock);
            var intent = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.True(intent.Change.ReservedMilliseconds >= 60_000);
            Assert.Contains(intent.Executions, execution => execution.ProcessId is null && execution.ReservedMilliseconds > 0);

            var recovered = fresh.Recover(repository.Path, request.RunId, WorkActor,
                "Inspect the interrupted launch without a recorded owned identity.");
            var competing = RunWork(fresh, repository, "other", 30, "WORK-091");

            Assert.NotEqual(0, recovered.ExitCode);
            Assert.Contains(recovered.Diagnostics, message => message.Contains("identity", StringComparison.OrdinalIgnoreCase)
                || message.Contains("accounting", StringComparison.OrdinalIgnoreCase) || message.Contains("uncertain", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, competing.ExitCode);
            Assert.Equal(1, other.ExecuteCalls);
            Assert.NotEqual(0, RunWork(fresh, repository, "other", 30).ExitCode);
            Assert.Equal(1, other.ExecuteCalls);
            Assert.True(fresh.WorkStatus(repository.Path, "CIS-0001").Work!.Change.ReservedMilliseconds >= 60_000);
        });
        var service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 80, 180, restorationSeconds: 5, tasks: ["WORK-090", "WORK-091"]);

        var completed = RunWork(service, repository, "fake", 60);

        Assert.Equal(0, completed.ExitCode);
        var settled = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(31_000, settled.Task!.ConsumedMilliseconds);
        Assert.Equal(0, settled.Task.ReservedMilliseconds);
    }

    [Fact]
    public void WorkControl_VerifiedTokenCancellationSettlesAndRepeatedProcessEventsDoNotRewriteAccounting()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var inner = new FakeProvider();
        AgentService? service = null;
        var provider = new WorkCallbackProvider(inner, executing: (_, emit, token) => WithWorkProcess(process =>
        {
            var started = process.StartTime.ToUniversalTime().ToString("O");
            emit(new("provider-event", "Owned process started.", "work/process", ProcessId: process.Id, ProcessStartedAtUtc: started));
            var controller = Assert.IsType<AgentService>(service);
            var revision = controller.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Revision;
            for (var index = 0; index < 20; index++)
                emit(new("provider-event", "Repeated owned identity.", "work/output", ProcessId: process.Id, ProcessStartedAtUtc: started));
            Assert.Equal(revision, controller.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Revision);
            clock.Advance(7);
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5000));
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        }));
        service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 120, 120);

        var cancelled = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Plan,
            CisAgentPermissions.ReadOnly, null, null, 30, false, WorkActor, cancellation.Token);

        Assert.Equal(CisAgentRunStates.Cancelled, cancelled.Run!.Manifest.Status);
        var settled = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(0, settled.Task!.ReservedMilliseconds);
        Assert.InRange(settled.Task.ConsumedMilliseconds, 7_000, 30_000);
        Assert.Null(settled.HoldReason);
        Assert.Equal("known", settled.RecoveryState);
        var fresh = WorkController(new FakeProvider(), clock);
        Assert.Equal(0, RunWork(fresh, repository, "fake", 30).ExitCode);
    }

    [Fact]
    public void WorkControl_SubsecondPreparationPreservesTheSupportedThirtySecondRunAndResume()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var inner = new FakeProvider(duringExecution: _ => clock.Advance(1));
        var provider = new WorkCallbackProvider(inner, diagnosing: () => clock.AdvanceMilliseconds(250));
        var service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 120, 120);

        var first = RunWork(service, repository, "fake", 30);

        Assert.True(first.ExitCode == 0, string.Join("; ", first.Diagnostics));
        Assert.Equal(1, inner.ExecuteCalls);
        var firstRequest = Assert.IsType<CisAgentExecutionRequest>(inner.LastRequest);
        Assert.InRange(firstRequest.Timeout.TotalMilliseconds, 29_000, 29_999);
        Assert.True(firstRequest.StartupTimeout <= firstRequest.Timeout);
        Assert.True(firstRequest.IdleTimeout <= firstRequest.Timeout);
        Assert.Equal(1_250, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);

        var resumed = service.Resume(repository.Path, first.Run!.Manifest.RunId, "Continue the bounded investigation.", WorkActor,
            "Use the same supported operation bound.", false, TestContext.Current.CancellationToken, timeoutSeconds: 30);

        Assert.True(resumed.ExitCode == 0, string.Join("; ", resumed.Diagnostics));
        Assert.Equal(2, inner.ExecuteCalls);
        Assert.InRange(Assert.IsType<CisAgentExecutionRequest>(inner.LastRequest).Timeout.TotalMilliseconds, 29_000, 29_999);
        Assert.Equal(2_500, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void WorkControl_ResumeAfterTransientPreparationLockUsesANewAdmissionIdentity()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(1));
        var service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 120, 120);
        var first = RunWork(service, repository, "fake", 30);
        Assert.Equal(0, first.ExitCode);
        var lockPath = System.IO.Path.Combine(repository.Path, AgentService.RootPath, "locks", "CIS-0001-WORK-090-agent-fixture.json");
        File.WriteAllText(lockPath, JsonSerializer.Serialize(new { runId = first.Run!.Manifest.RunId }, JsonOptions));
        using (var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var blocked = service.Resume(repository.Path, first.Run.Manifest.RunId, "Continue.", WorkActor,
                "Retry after a transient target lease conflict.", false, TestContext.Current.CancellationToken);
            Assert.NotEqual(0, blocked.ExitCode);
            Assert.Equal(1, provider.ExecuteCalls);
        }
        File.Delete(lockPath);
        var afterPreparation = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(0, afterPreparation.Task!.ReservedMilliseconds);

        var resumed = WorkController(provider, clock).Resume(repository.Path, first.Run.Manifest.RunId, "Continue.", WorkActor,
            "The transient lease is now released.", false, TestContext.Current.CancellationToken);

        Assert.True(resumed.ExitCode == 0, string.Join("; ", resumed.Diagnostics));
        Assert.Equal(2, provider.ExecuteCalls);
        Assert.Equal(2, resumed.Run!.Manifest.Attempt);
        var work = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(3, work.Executions.Count);
        Assert.Equal(3, work.Executions.Select(execution => execution.ExecutionId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2_000, work.Task!.ConsumedMilliseconds);
    }

    [Fact]
    public void WorkControl_TerminalAndPremanifestReservationsHaveBoundedRecoveryWithoutRestoringRetiredTasks()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        var clock = new WorkControlClock();
        using var process = new Process { StartInfo = SleepProcess(writeFirst: false) };
        Assert.True(process.Start());
        try
        {
            var provider = new WorkCallbackProvider(new FakeProvider(), executing: (_, emit, _) =>
                emit(new("provider-event", "Owned process remains active after provider return.", "work/process",
                    ProcessId: process.Id, ProcessStartedAtUtc: process.StartTime.ToUniversalTime().ToString("O"))));
            var service = WorkController(provider, clock);
            AllocateWorkFixture(repository, service, 90, 180, tasks: ["WORK-090", "WORK-091"]);
            var terminal = RunWork(service, repository, "fake", 30);
            Assert.True(CisAgentRunStates.IsTerminal(terminal.Run!.Manifest.Status));
            Assert.Equal(30_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ReservedMilliseconds);
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5000));
            clock.Advance(1);

            var recovered = WorkController(new FakeProvider(), clock).Recover(repository.Path, terminal.Run.Manifest.RunId, WorkActor,
                "The verified owned process has stopped; reconcile the terminal manifest's unsettled reservation.");

            Assert.True(recovered.ExitCode == 0, string.Join("; ", recovered.Diagnostics));
            Assert.Equal(terminal.Run.Manifest.Status, recovered.Run!.Manifest.Status);
            Assert.Equal(terminal.Run.Manifest.FailureKind, recovered.Run.Manifest.FailureKind);
            Assert.Equal(terminal.Run.Manifest.CompletedAtUtc, recovered.Run.Manifest.CompletedAtUtc);
            var current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(30_000, current.Task!.ConsumedMilliseconds);
            Assert.Equal(0, current.Task.ReservedMilliseconds);
            Assert.All(current.Executions, execution => Assert.True(execution.Finalized));

            var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
            var ledger = new AgentWorkLedger(clock.Read);
            WithWorkProcess(preparer =>
            {
                var owner = new CisAgentWorkPreparationOwner(Environment.MachineName, preparer.Id,
                    preparer.StartTime.ToUniversalTime().ToString("O"));
                var admission = ledger.Admit(context, new(context.RepositoryId, "CIS-0001", "WORK-091", [],
                    ["docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md"]), "fixture-premanifest-reservation", 30, 3600,
                    preparationOwner: owner);
                Assert.True(admission.Admitted, string.Join("; ", admission.Result.Errors));
                var reference = Assert.IsType<CisAgentWorkReference>(admission.Reference);
                Assert.False(admission.Result.Work!.Executions.Single().LaunchIntent);
                preparer.Kill(entireProcessTree: true);
                Assert.True(preparer.WaitForExit(5000));
                clock.Advance(31);
                var taskPath = System.IO.Path.Combine(repository.Path, "docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md");
                var retired = System.IO.Path.Combine(repository.Path, "docs/cis/changes/CIS-0001/agent-tasks/retired/WORK-091.md");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(retired)!);
                File.Move(taskPath, retired);
                var revision = service.WorkStatus(repository.Path, "CIS-0001", "WORK-091").Work!.Revision;
                var input = WriteWorkInput(repository, "reconcile-reservation.json", new CisAgentWorkDispositionRequest("reconcile", revision)
                {
                    ExecutionId = reference.ExecutionId,
                    Evidence = ["execution:" + reference.ExecutionId],
                    NextAction = "Retain the measured reservation and keep the retired task unavailable for execution.",
                });

                var reconciled = service.DisposeWork(repository.Path, "CIS-0001", "WORK-091", input, WorkActor,
                    "The verified preparer has stopped and never recorded launch intent; charge its abandoned reservation conservatively.");

                Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
                Assert.Equal(0, reconciled.Work!.Task!.ReservedMilliseconds);
                Assert.True(reconciled.Work.Task.ConsumedMilliseconds >= 30_000);
                Assert.True(reconciled.Work.Executions.Single().Finalized);
                Assert.False(File.Exists(taskPath));
                Assert.NotEqual(0, ledger.MarkLaunchIntent(context, reference).ExitCode);
                Assert.NotEqual(0, RunWork(service, repository, "fake", 30, "WORK-091").ExitCode);
            });
            var legacy = ledger.Admit(context, new(context.RepositoryId, "CIS-0001", "WORK-090", [],
                ["docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md"]), "fixture-legacy-unowned-preparation", 30, 3600);
            Assert.True(legacy.Admitted, string.Join("; ", legacy.Result.Errors));
            var legacyReference = Assert.IsType<CisAgentWorkReference>(legacy.Reference);
            clock.Advance(31);
            var unknown = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var legacyInput = WriteWorkInput(repository, "reconcile-unowned-preparation.json",
                new CisAgentWorkDispositionRequest("reconcile", unknown.Revision)
                {
                    ExecutionId = legacyReference.ExecutionId,
                    Evidence = ["execution:" + legacyReference.ExecutionId],
                    NextAction = "Restore verifiable preparation ownership before reconciling historical uncertainty.",
                });
            var refused = service.DisposeWork(repository.Path, "CIS-0001", "WORK-090", legacyInput, WorkActor,
                "A missing historical preparer identity is not proof that preparation stopped.");
            Assert.NotEqual(0, refused.ExitCode);
            var refusalMessages = refused.Errors.Concat(new[] { refused.Work?.HoldReason, refused.Work?.NextAction }
                .Where(message => message is not null).Select(message => message!));
            Assert.Contains(refusalMessages, error => error.Contains("prepar", StringComparison.OrdinalIgnoreCase)
                || error.Contains("owner", StringComparison.OrdinalIgnoreCase) || error.Contains("uncertain", StringComparison.OrdinalIgnoreCase));
            var retained = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(30_000, retained.Task!.ConsumedMilliseconds);
            Assert.Equal(30_000, retained.Task.ReservedMilliseconds);
            Assert.Null(retained.Executions.Single(execution => execution.ExecutionId == legacyReference.ExecutionId).PreparationOwner);
            Assert.False(retained.Executions.Single(execution => execution.ExecutionId == legacyReference.ExecutionId).Finalized);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
        }
    }

    [Fact]
    public void WorkControl_ChangedProcessIdentityRemainsRecoverableWithoutLosingTheTerminalOutcome()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        WithWorkProcess(first => WithWorkProcess(second =>
        {
            AgentService? service = null;
            string? beforeRepeats = null, afterRepeats = null;
            var provider = new WorkCallbackProvider(new FakeProvider(duringExecution: _ => clock.Advance(1)),
                executing: (_, emit, _) =>
                {
                    var firstStarted = first.StartTime.ToUniversalTime().ToString("O");
                    var secondStarted = second.StartTime.ToUniversalTime().ToString("O");
                    emit(new("provider-event", "First owned identity.", "work/process", ProcessId: first.Id, ProcessStartedAtUtc: firstStarted));
                    emit(new("provider-event", "Changed owned identity requires reconciliation.", "work/process", ProcessId: second.Id, ProcessStartedAtUtc: secondStarted));
                    var controller = Assert.IsType<AgentService>(service);
                    beforeRepeats = controller.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Revision;
                    for (var index = 0; index < 20; index++)
                        emit(new("provider-event", "Repeated uncertain identity.", "work/output", ProcessId: second.Id, ProcessStartedAtUtc: secondStarted));
                    afterRepeats = controller.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Revision;
                });
            service = WorkController(provider, clock);
            AllocateWorkFixture(repository, service, 120, 120);

            var terminal = RunWork(service, repository, "fake", 30);

            Assert.NotNull(beforeRepeats);
            Assert.Equal(beforeRepeats, afterRepeats);
            Assert.Equal(CisAgentRunStates.Succeeded, terminal.Run!.Manifest.Status);
            Assert.Equal(30_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ReservedMilliseconds);
            var fresh = WorkController(new FakeProvider(), clock);
            Assert.NotEqual(0, fresh.Recover(repository.Path, terminal.Run.Manifest.RunId, WorkActor,
                "Both recorded owned identities are still active.").ExitCode);
            first.Kill(entireProcessTree: true);
            Assert.True(first.WaitForExit(5000));
            Assert.NotEqual(0, fresh.Recover(repository.Path, terminal.Run.Manifest.RunId, WorkActor,
                "The ledger's first identity stopped, but the journal and manifest's replacement remains active.").ExitCode);
            Assert.Equal(30_000, fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ReservedMilliseconds);
            second.Kill(entireProcessTree: true);
            Assert.True(second.WaitForExit(5000));
            clock.Advance(2);

            var reconciled = fresh.Recover(repository.Path, terminal.Run.Manifest.RunId, WorkActor,
                "Every recorded owned identity is now proven stopped; retain the original provider outcome.");

            Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Diagnostics));
            Assert.Equal(terminal.Run.Manifest.Status, reconciled.Run!.Manifest.Status);
            Assert.Equal(terminal.Run.Manifest.FailureKind, reconciled.Run.Manifest.FailureKind);
            Assert.Equal(terminal.Run.Manifest.CompletedAtUtc, reconciled.Run.Manifest.CompletedAtUtc);
            var work = fresh.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            Assert.Equal(30_000, work.Task!.ConsumedMilliseconds);
            Assert.Equal(0, work.Task.ReservedMilliseconds);
            Assert.Equal("known", work.RecoveryState);
            Assert.Null(work.HoldReason);
            Assert.All(work.Executions, execution => Assert.True(execution.Finalized));
        }));
    }

    [Fact]
    public void WorkControl_RunListingsDoNotProjectAnArbitraryAccountingOwner()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        var clock = new WorkControlClock();
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(1));
        var service = WorkController(provider, clock);
        AllocateWorkFixture(repository, service, 90, 180, tasks: ["WORK-090", "WORK-091"]);
        Assert.Equal(0, RunWork(service, repository, "fake", 30).ExitCode);
        Assert.Equal(0, RunWork(service, repository, "fake", 30, "WORK-091").ExitCode);
        var before = WorkControlFiles(repository);

        var listed = service.Runs(repository.Path, latestPerTask: true);

        Assert.Equal(2, listed.Runs.Count);
        Assert.Null(listed.Work);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(service, provider)).Build();
        var output = InvokeWorkCommand(application, "agent", "runs", "--summary", "--latest-per-task", "--repo", repository.Path,
            "--format", "json");
        Assert.Equal(0, output.ExitCode);
        using var json = JsonDocument.Parse(output.Output);
        Assert.Equal(2, json.RootElement.GetProperty("runs").GetArrayLength());
        Assert.True(!json.RootElement.TryGetProperty("work", out var work) || work.ValueKind == JsonValueKind.Null);
        AssertWorkControlFilesUnchanged(repository, before);
    }

    [Fact]
    public void WorkControl_RuntimeCeilingRechecksResumeAndDeletedAdoptedStateCannotResetCapacity()
    {
        using var repository = AgentRepository.Create();
        var clock = new WorkControlClock();
        repository.AddEligibleChange();
        var historicalProvider = new FakeProvider();
        var historical = RunWork(WorkController(historicalProvider, clock), repository, "fake", 30);
        Assert.Equal(0, historical.ExitCode);
        AdoptWorkControl(repository);
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(10));
        var service = WorkController(provider, clock);
        var allocationPath = AllocateWorkFixture(repository, service, 10_000, 10_000, restorationSeconds: 5);
        Assert.Contains("unmeasured", service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Coverage,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(service.Show(repository.Path, historical.Run!.Manifest.RunId).Run);

        var oversized = RunWork(service, repository, "fake", 3_601);

        Assert.NotEqual(0, oversized.ExitCode);
        Assert.Equal(0, provider.ExecuteCalls);
        var completed = RunWork(service, repository, "fake", 30);
        Assert.Equal(0, completed.ExitCode);
        var request = Assert.IsType<CisAgentExecutionRequest>(provider.LastRequest);
        Assert.True(request.Timeout <= TimeSpan.FromSeconds(30));
        Assert.True(request.StartupTimeout <= request.Timeout);
        Assert.True(request.IdleTimeout <= request.Timeout);

        // A historical manifest may contain a timeout admitted by an older validator.
        // Preserve its evidence hashes so this isolates current admission, not tampering.
        var runRoot = System.IO.Path.Combine(repository.Path, AgentService.RootPath, "runs", completed.Run!.Manifest.RunId);
        var historic = completed.Run.Manifest with { TimeoutSeconds = 86_400 };
        File.WriteAllText(System.IO.Path.Combine(runRoot, "manifest.json"), JsonSerializer.Serialize(historic, JsonOptions));
        var inventoryPath = System.IO.Path.Combine(runRoot, "artifacts.json");
        var inventory = JsonSerializer.Deserialize<AgentRunArtifact[]>(File.ReadAllText(inventoryPath), JsonOptions)!;
        File.WriteAllText(inventoryPath, JsonSerializer.Serialize(inventory.Select(artifact => artifact.Path == "manifest.json"
            ? artifact with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(System.IO.Path.Combine(runRoot, "manifest.json")))) }
            : artifact), JsonOptions));
        var resumed = WorkController(provider, clock).Resume(repository.Path, historic.RunId, "Continue within current limits.",
            WorkActor, "Recheck the historical execution bound.", false, TestContext.Current.CancellationToken);
        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("3600", StringComparison.Ordinal));

        var ledger = System.IO.Path.Combine(repository.Path, AgentService.RootPath, "work-control.json");
        Assert.True(File.Exists(ledger));
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, AgentService.RootPath, "work-control-adoption.json")));
        File.Delete(ledger);
        var missing = RunWork(WorkController(provider, clock), repository, "fake", 30);
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.False(File.Exists(ledger));
        Assert.NotEqual(0, service.AllocateWork(repository.Path, "CIS-0001", allocationPath, WorkActor,
            "A missing adopted ledger must not bootstrap again.").ExitCode);
        Assert.False(File.Exists(ledger));
        Assert.NotNull(service.Show(repository.Path, historic.RunId).Run);
        Assert.NotNull(service.Show(repository.Path, historical.Run.Manifest.RunId).Run);
    }

    [Fact]
    public void WorkControl_HeadlessProgressHoldsUnchangedAttemptsAndAllowsJustifiedSimplification()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var provider = new FakeProvider(duringExecution: _ => clock.Advance(10));
        var service = WorkController(provider, clock);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(service, provider)).Build();
        var allocationPath = WriteWorkInput(repository, "allocation.json", WorkAllocation(180, 180, 5, ["WORK-090"]));
        var allocation = InvokeWorkCommand(application, "agent", "work", "allocate", "CIS-0001", "--input", allocationPath,
            "--actor", WorkActor, "--reason", "Synthetic bounded task allocation.", "--repo", repository.Path, "--format", "json");
        Assert.Equal(0, allocation.ExitCode);
        using (var json = JsonDocument.Parse(allocation.Output))
            Assert.Equal("agent-fixture", json.RootElement.GetProperty("work").GetProperty("authorityRepositoryId").GetString());

        for (var index = 0; index < 2; index++)
        {
            var executed = InvokeWorkRun(application, repository);
            Assert.Equal(0, executed.ExitCode);
            using var runJson = JsonDocument.Parse(executed.Output);
            var runId = runJson.RootElement.GetProperty("run").GetProperty("manifest").GetProperty("runId").GetString()!;
            var current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var checkpoint = new CisAgentWorkCheckpoint("The same interface mismatch still blocks this hypothesis.", ["run:" + runId],
                "Investigate the existing traversal extension.", "reconsider", "Choose a simpler bounded implementation.")
            {
                Hypothesis = "Extend the existing traversal to cover this task.",
                ExpectedResult = "A compatible bounded traversal extension.",
                BlockerKey = "same-interface-mismatch",
                MaterialNewInformation = false,
                CloseAttempt = true,
            };
            var input = WriteWorkInput(repository, "progress.json", new CisAgentWorkProgressRequest(checkpoint, current.Revision));
            var progress = InvokeWorkCommand(application, "agent", "work", "progress", "CIS-0001", "WORK-090", "--input", input,
                "--actor", WorkActor, "--repo", repository.Path, "--format", "json");
            Assert.True(progress.ExitCode is 0 or 4, progress.Output);
            using var progressJson = JsonDocument.Parse(progress.Output);
            Assert.Equal(index + 1, progressJson.RootElement.GetProperty("work").GetProperty("checkpoints").EnumerateArray()
                .Count(record => record.GetProperty("actor").GetString() == WorkActor
                    && record.GetProperty("checkpoint").GetProperty("closeAttempt").GetBoolean()));
        }
        var held = InvokeWorkRun(application, repository);
        Assert.NotEqual(0, held.ExitCode);
        Assert.Equal(2, provider.ExecuteCalls);
        using (var json = JsonDocument.Parse(held.Output))
        {
            var work = json.RootElement.GetProperty("work");
            Assert.Equal("WORK-090", work.GetProperty("ownerTaskId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(work.GetProperty("holdReason").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(work.GetProperty("nextAction").GetString()));
        }

        var before = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.Equal(2, before.RepeatedBlockerCount);
        var unchanged = WriteWorkInput(repository, "continue.json", new CisAgentWorkDispositionRequest("continue", before.Revision));
        var repeat = InvokeWorkCommand(application, "agent", "work", "disposition", "CIS-0001", "WORK-090", "--input", unchanged,
            "--actor", WorkActor, "--reason", "Continue.", "--repo", repository.Path, "--format", "json");
        Assert.True(repeat.ExitCode is 0 or 4, repeat.Output);
        var currentView = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        Assert.NotNull(currentView.HoldReason);
        Assert.NotEqual(0, InvokeWorkRun(application, repository).ExitCode);
        Assert.Equal(2, provider.ExecuteCalls);
        var simpler = WriteWorkInput(repository, "simplify.json", new CisAgentWorkDispositionRequest("simplify", currentView.Revision)
        {
            Hypothesis = "Use the existing descriptor map instead of expanding traversal.",
            Evidence = before.Checkpoints.SelectMany(checkpoint => checkpoint.Checkpoint.Evidence).ToArray(),
            NextAction = "Implement one bounded descriptor lookup under the original task.",
        });
        var disposition = InvokeWorkCommand(application, "agent", "work", "disposition", "CIS-0001", "WORK-090", "--input", simpler,
            "--actor", WorkActor, "--reason", "Use a simpler approach within the original accepted scope.",
            "--repo", repository.Path, "--format", "json");
        Assert.Equal(0, disposition.ExitCode);
        Assert.Equal(before.Task!.ConsumedMilliseconds,
            service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(0, InvokeWorkRun(application, repository).ExitCode);
        Assert.Equal(3, provider.ExecuteCalls);
        Assert.Equal(30_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
        foreach (var format in new[] { "human", "agent" })
        {
            var projected = InvokeWorkCommand(application, "agent", "work", "status", "CIS-0001", "--task", "WORK-090",
                "--repo", repository.Path, "--format", format);
            Assert.Equal(0, projected.ExitCode);
            Assert.Contains("WORK-090", projected.Output, StringComparison.Ordinal);
            Assert.Contains("30000", projected.Output, StringComparison.Ordinal);
            Assert.Contains("next", projected.Output, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AdoptWorkControl(AgentRepository repository, bool secondTask = false)
    {
        repository.AddEligibleChange();
        repository.Write(CisEngineeringPolicy.RelativePath, """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        repository.Write("docs/cis/references/agent-provider-profile.md", """
            | Setting | Value | Rationale |
            | --- | --- | --- |
            | direct-dirty-working-tree | denied | Synthetic isolation policy. |
            | maximum-run-seconds | 3600 | Synthetic adopted run ceiling. |
            | maximum-startup-seconds | 30 | Preserve startup monitoring. |
            | maximum-idle-seconds | 300 | Preserve idle monitoring. |
            """);
        if (secondTask)
            repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md",
                "---\ntask_status: Ready\ntargets: [\"agent-fixture\"]\n---\n# Separate task\nReview the second bounded behavior.\n");
    }

    private static AgentService WorkController(ICisAgentProvider provider, WorkControlClock clock, params ICisAgentProvider[] others)
        => new(new CisRepositoryContextResolver(), new[] { provider }.Concat(others), clock: clock.Read,
            taskContracts: new MutableTaskContract());

    private static AgentResult RunWork(AgentService service, AgentRepository repository, string provider, int timeout,
        string task = "WORK-090") => service.Run(repository.Path, "CIS-0001", task, provider, CisAgentRunModes.Plan,
            CisAgentPermissions.ReadOnly, null, null, timeout, false, WorkActor, TestContext.Current.CancellationToken);

    private static CisAgentWorkAllocationRequest WorkAllocation(long taskSeconds, long changeSeconds, long restorationSeconds,
        IReadOnlyList<string> tasks) => new(changeSeconds, 12, restorationSeconds,
            tasks.Select(task => new CisAgentWorkTaskAllocation(task, taskSeconds, 10, restorationSeconds)).ToArray(),
            "fixture:controller-approved-allocation", "fixture:existing-local-storage-ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = [WorkActor, "Fixture author", "Fixture reviewer"] };

    private static string AllocateWorkFixture(AgentRepository repository, AgentService service, long taskSeconds = 180,
        long changeSeconds = 180, long restorationSeconds = 5, IReadOnlyList<string>? tasks = null)
    {
        var input = WriteWorkInput(repository, "allocation.json", WorkAllocation(taskSeconds, changeSeconds, restorationSeconds, tasks ?? ["WORK-090"]));
        var allocated = service.AllocateWork(repository.Path, "CIS-0001", input, WorkActor, "Synthetic authorized test allocation.");
        Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
        return input;
    }

    private static string WriteWorkInput<T>(AgentRepository repository, string name, T input)
    {
        var relative = ".cis/local/work-test-input/" + name;
        repository.Write(relative, JsonSerializer.Serialize(input, JsonOptions));
        return System.IO.Path.Combine(repository.Path, relative);
    }

    private static IReadOnlyDictionary<string, byte[]> WorkControlFiles(AgentRepository repository)
        => Directory.EnumerateFiles(System.IO.Path.Combine(repository.Path, AgentService.RootPath), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWorkControlFilesUnchanged(AgentRepository repository, IReadOnlyDictionary<string, byte[]> original)
    {
        var current = WorkControlFiles(repository);
        Assert.Equal(original.Keys.Order(StringComparer.Ordinal), current.Keys.Order(StringComparer.Ordinal));
        foreach (var file in original) Assert.Equal(file.Value, current[file.Key]);
    }

    private static (int ExitCode, string Output) InvokeWorkCommand(CisApplication application, params string[] arguments)
    {
        var previous = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); return (application.Invoke(arguments), output.ToString()); }
        finally { Console.SetOut(previous); }
    }

    private static (int ExitCode, string Output) InvokeWorkRun(CisApplication application, AgentRepository repository)
        => InvokeWorkCommand(application, "agent", "run", "CIS-0001", "WORK-090", "--provider", "fake", "--mode", "plan",
            "--permission", "read-only", "--actor", WorkActor, "--timeout-seconds", "30", "--repo", repository.Path, "--format", "json");

    private sealed class WorkControlClock
    {
        private long _ticks = Clock().UtcTicks;
        public DateTimeOffset Read() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(int seconds) => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(seconds).Ticks);
        public void AdvanceMilliseconds(int milliseconds) => Interlocked.Add(ref _ticks, TimeSpan.FromMilliseconds(milliseconds).Ticks);
    }

    private static void WithWorkProcess(Action<Process> action)
    {
        using var process = new Process { StartInfo = SleepProcess(writeFirst: false) };
        Assert.True(process.Start());
        try { action(process); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
        }
    }

    private sealed class WorkCallbackProvider(FakeProvider inner, Action? diagnosing = null,
        Action<CisAgentExecutionRequest, Action<CisAgentProviderEvent>, CancellationToken>? executing = null) : ICisAgentProvider
    {
        public CisAgentProviderDescriptor Descriptor => inner.Descriptor;
        public CisAgentProviderDiagnosis Diagnose(string repositoryPath)
        {
            diagnosing?.Invoke();
            return inner.Diagnose(repositoryPath);
        }
        public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent,
            CancellationToken cancellationToken)
        {
            executing?.Invoke(request, onEvent, cancellationToken);
            return inner.Execute(request, onEvent, cancellationToken);
        }
    }

    private sealed class WorkControlFixtureModule(AgentService service, ICisAgentProvider provider) : ICisModule
    {
        public string Name => "work-control-fixture";
        public string Description => "Uses the existing deterministic provider and injected clock.";
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton(provider);
            services.AddSingleton(service);
        }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }
}
