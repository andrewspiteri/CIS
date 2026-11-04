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
            var admission = ledger.Admit(context, new(context.RepositoryId, "CIS-0001", "WORK-091", [],
                ["docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md"]), "fixture-premanifest-reservation", 30, 3600);
            Assert.True(admission.Admitted, string.Join("; ", admission.Result.Errors));
            var reference = Assert.IsType<CisAgentWorkReference>(admission.Reference);
            Assert.False(admission.Result.Work!.Executions.Single().LaunchIntent);
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
                "The old preparer never recorded launch intent and is no longer active after the recovery grace period.");

            Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
            Assert.Equal(0, reconciled.Work!.Task!.ReservedMilliseconds);
            Assert.True(reconciled.Work.Task.ConsumedMilliseconds >= 30_000);
            Assert.True(reconciled.Work.Executions.Single().Finalized);
            Assert.False(File.Exists(taskPath));
            Assert.NotEqual(0, ledger.MarkLaunchIntent(context, reference).ExitCode);
            Assert.NotEqual(0, RunWork(service, repository, "fake", 30, "WORK-091").ExitCode);
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
