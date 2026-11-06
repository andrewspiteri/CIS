using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Cis.Modules.Testing;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void NativeWork_FailedCanaryRetainsOriginalRestorationBeforeAnyPilotPhase()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var service = new AgentService(new CisRepositoryContextResolver());
        var allocation = WorkAllocation(180, 180, 30, ["WORK-090"]) with
        {
            Storage = new(128 * 1024 * 1024, [new(repository.Path, MutationRunService.LocalRoot)])
            { RestorationBytes = 8 * 1024 * 1024 }
        };
        var allocated = service.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "native-canary-allocation.json", allocation), WorkActor, "Bound the failed native canary and its original restoration.");
        Assert.Equal(0, allocated.ExitCode);
        repository.Write("subject.cs", "class Subject { bool Value = true; }");
        repository.Write("control.cs", "class Control { bool Value = true; }");
        repository.Write("native-config.json", "{}");
        repository.Write("native-canary.ps1", """
            param([string]$Output, [string]$Mode)
            $ErrorActionPreference = 'Stop'
            [IO.Directory]::CreateDirectory($Output) | Out-Null
            if ($Mode -ne 'canary') { throw 'The failed canary must not enter a representative pilot phase.' }
            [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'subject.cs'), '// instrumented by failed canary')
            exit 7
            """);
        var shell = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory.Trim('"'), "pwsh" + (OperatingSystem.IsWindows() ? ".exe" : "")))
            .First(File.Exists);
        string[] Command(string mode) => [shell, "-NoProfile", "-NonInteractive", "-File", "native-canary.ps1", "-Output", "{output}", "-Mode", mode];
        var profile = new MutationRunProfile
        {
            SchemaVersion = 1,
            Id = "failed-native-canary",
            Command = [.. Command("mutation"), "--config-file", "native-config.json"],
            CanaryCommand = Command("canary"),
            SourceFiles = ["subject.cs", "control.cs"],
            ConfigFiles = ["native-canary.ps1", "native-config.json"],
            ReportPath = "report.json",
            BreakThreshold = 90,
            TimeoutSeconds = 15,
            MaxRunBytes = 8 * 1024 * 1024,
            MaxRetainedBytes = 32 * 1024 * 1024,
            MinFreeBytes = 0,
            CorrectnessPilot = new()
            {
                BaselineCommand = Command("baseline"),
                ControlSourceFile = "control.cs",
                InfrastructureCommand = [.. Command("mutation"), "--config-file", "missing-native-config.json"],
                MissingInfrastructurePath = "missing-native-config.json",
                RestorationCommand = Command("restoration"),
                RecoveryTimeoutSeconds = 10,
                ToolFiles = [shell]
            }
        };
        repository.Write("native-canary.json", JsonSerializer.Serialize(profile, JsonOptions));
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor);

        var result = new MutationRunService(service, service).Run(new(repository.Path, "native-canary.json", "canary")
        { Work = association, MaximumSeconds = 120 }, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.NotNull(result.Pilot);
        Assert.Equal("canary-failed", result.Pilot.Status);
        Assert.False(result.Pilot.Ready);
        Assert.True(result.Pilot.RecoveryRequired);
        Assert.Empty(result.Pilot.Observations);
        Assert.False(result.Qualified);
        Assert.Equal(7, result.NativeExecution!.ExitCode);
        Assert.True(result.NativeExecution.SafelyStopped);
        Assert.Contains("instrumented by failed canary", File.ReadAllText(Path.Combine(repository.Path, "subject.cs")), StringComparison.Ordinal);
        var retained = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
        var original = retained.Executions.Single(execution => execution.ExecutionId == result.WorkReference!.ExecutionId);
        Assert.True(original.Finalized);
        Assert.Equal("RecoveryRequired", original.Status);
        Assert.True(original.NativeRestorationRequired);
        Assert.DoesNotContain(retained.Executions, execution => execution.Stage?.NativeOperation?.StartsWith("pilot-", StringComparison.Ordinal) == true);
        Assert.Contains(original.ExecutionId, retained.HoldReason!, StringComparison.Ordinal);
        var followup = ((ICisNativeWorkController)service).Admit(new(repository.Path, "native/failed-canary-cannot-reset", "diagnostic", 30, association));
        Assert.False(followup.Allowed);
        Assert.Contains(followup.Errors, error => error.Contains(original.ExecutionId, StringComparison.Ordinal));
    }

    [Fact]
    public void NativeWork_ServiceKeepsCanonicalParentActorAndVerifiedProcessAtTheActualHandoff()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var service = new AgentService(new CisRepositoryContextResolver(), clock: clock.Read);
        AllocateWorkFixture(repository, service, 180, 180, restorationSeconds: 30);
        var native = (ICisNativeWorkController)service;
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor);
        var ledger = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var snapshot = File.ReadAllBytes(ledger);
        var read = native.Inspect(repository.Path, association);
        Assert.Equal("current", read.Status); Assert.True(read.Required);
        Assert.Equal(File.ReadAllBytes(ledger), snapshot);
        var denied = native.Admit(new(repository.Path, "native/unapproved-controller", "unit", 30,
            association with { Actor = "Unapproved fixture provider" }));
        Assert.False(denied.Allowed); Assert.Null(denied.Scope);
        Assert.Equal(snapshot, File.ReadAllBytes(ledger));
        var invented = native.Admit(new(repository.Path, "native/invented-parent", "unit", 30,
            association with { ParentExecutionId = "provider-written-run-id" }));
        Assert.False(invented.Allowed); Assert.Null(invented.Scope);
        Assert.Equal(snapshot, File.ReadAllBytes(ledger));
        var admitted = native.Admit(new(repository.Path, "native/service-root", "unit-verification", 30, association));
        Assert.True(admitted.Allowed, string.Join("; ", admitted.Errors));
        using var root = admitted.Scope!;
        Assert.Equal(WorkActor, root.Actor); Assert.Equal(repository.Path, root.AuthorityPath);
        var parent = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Executions.Single();
        Assert.Equal(Environment.ProcessId, parent.PreparationOwner!.ProcessId);
        Assert.Equal(Environment.MachineName, parent.PreparationOwner.HostId);
        using (var controller = Process.GetCurrentProcess())
            Assert.Equal(controller.StartTime.ToUniversalTime(), DateTimeOffset.Parse(parent.PreparationOwner.ProcessStartedAtUtc).UtcDateTime);
        clock.Advance(2);
        var childAdmission = root.AdmitChild("native/service-child", "unit-native-driver", 30);
        Assert.True(childAdmission.Allowed, string.Join("; ", childAdmission.Errors));
        using var child = childAdmission.Scope!;
        Assert.Equal(28, child.MaximumSeconds);
        Assert.Equal(root.Reference.ExecutionId, child.Reference.ParentExecutionId);
        Assert.Equal(root.Reference.LogicalAttemptId, child.Reference.LogicalAttemptId);
        Assert.Equal(root.Reference.OwnerTaskId, child.Reference.OwnerTaskId);
        Assert.Equal(WorkActor, child.Actor);
        child.Launch();
        WithWorkProcess(process =>
        {
            child.Started(process.Id, process.StartTime.ToUniversalTime().ToString("O"));
            clock.Advance(3);
            process.Kill(entireProcessTree: true); Assert.True(process.WaitForExit(5000));
            child.Returned(safelyStopped: true);
            Assert.Equal(0, child.Finish("native-finished", "Controlled native process stopped with its original owner.", ["fixture:owned-driver"]).ExitCode);
        });
        var completed = root.Finish("native-finished", "Native preparation and child share one inclusive interval.", ["fixture:inclusive-native-cost"]);
        Assert.Equal(0, completed.ExitCode);
        Assert.Equal(5_000, completed.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(5_000, completed.Work.Change.ConsumedMilliseconds);
        Assert.Equal(0, completed.Work.Task.ReservedMilliseconds);
        Assert.Equal(1, completed.Work.Task.UsedAttempts);
        var evidence = native.InspectExecution(repository.Path, child.Reference);
        Assert.Equal("verified", evidence.Status); Assert.Empty(evidence.Errors);
        Assert.Equal(child.Reference, evidence.Reference);
        Assert.Equal(0, child.Finish("native-finished", "Repeated settlement retains measured cost.", []).ExitCode);
        Assert.Equal(5_000, service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!.Task!.ConsumedMilliseconds);
    }
}
