using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Cis.Modules.Testing;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public async Task NativeWork_ActualHostCancellationReconcilesItsNamedJobWithoutErasingRestoration()
    {
        if (!OperatingSystem.IsWindows()) return; // Linux needs a retained owned-session proof, never a Windows inference.
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clockOffset = 0;
        var service = new AgentService(new CisRepositoryContextResolver(), clock: () => DateTimeOffset.UtcNow.AddSeconds(clockOffset));
        var allocation = WorkAllocation(180, 180, 30, ["WORK-090"]) with
        {
            Storage = new(128 * 1024 * 1024, [new(repository.Path, MutationRunService.LocalRoot)])
            { RestorationBytes = 8 * 1024 * 1024 }
        };
        Assert.Equal(0, service.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "native-orphan-allocation.json", allocation),
            WorkActor, "Bound actual native orphan recovery and retain its original restoration reserve.").ExitCode);
        repository.Write("subject.cs", "class Subject { bool Value = true; }");
        repository.Write("control.cs", "class Control { bool Value = true; }");
        repository.Write("native-config.json", "{}");
        repository.Write("native-orphan.ps1", """
            param([string]$Output, [string]$Mode)
            $ErrorActionPreference = 'Stop'
            [IO.Directory]::CreateDirectory($Output) | Out-Null
            if ($Mode -ne 'canary') { throw 'Only the controlled native canary may start.' }
            [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'subject.cs'), '// interrupted native instrumentation')
            $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $start.UseShellExecute = $false; $start.CreateNoWindow = $true
            foreach ($value in @('-NoProfile', '-NonInteractive', '-Command', 'while ($true) { Start-Sleep -Milliseconds 100 }')) {
                $start.ArgumentList.Add($value)
            }
            $child = [Diagnostics.Process]::Start($start)
            $ready = Join-Path $PSScriptRoot '.cis/local/testing/mutation/orphan-ready.json'
            [IO.File]::WriteAllText(($ready + '.tmp'), (@{ parent = $PID; child = $child.Id } | ConvertTo-Json -Compress))
            [IO.File]::Move(($ready + '.tmp'), $ready)
            while ($true) { Start-Sleep -Milliseconds 100 }
            """);
        var shell = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory.Trim('"'), "pwsh.exe")).First(File.Exists);
        string[] Command(string mode) => [shell, "-NoProfile", "-NonInteractive", "-File", "native-orphan.ps1", "-Output", "{output}", "-Mode", mode];
        var profile = new MutationRunProfile
        {
            SchemaVersion = 1,
            Id = "native-orphan",
            Command = [.. Command("mutation"), "--config-file", "native-config.json"],
            CanaryCommand = Command("canary"),
            SourceFiles = ["subject.cs", "control.cs"],
            ConfigFiles = ["native-orphan.ps1", "native-config.json"],
            ReportPath = "report.json",
            BreakThreshold = 90,
            TimeoutSeconds = 120,
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
        repository.Write("native-orphan.json", JsonSerializer.Serialize(profile, JsonOptions));
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (dotnet is null || !File.Exists(dotnet))
            dotnet = Path.GetFullPath("../../../dotnet.exe", RuntimeEnvironment.GetRuntimeDirectory());
        var tests = typeof(AgentExecutionTests).Assembly.Location;
        var start = new ProcessStartInfo(dotnet)
        { WorkingDirectory = repository.Path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(tests, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(tests, ".deps.json"), typeof(CisApplication).Assembly.Location,
            "test", "mutation", "run", "--kind", "canary", "--profile", "native-orphan.json", "--repo", repository.Path,
            "--change", "CIS-0001", "--task", "WORK-090", "--authority", repository.Path, "--actor", WorkActor, "--max-seconds", "120", "--format", "json" })
            start.ArgumentList.Add(argument);
        using var host = Process.Start(start)!;
        var stdout = host.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = host.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            var ready = Path.Combine(repository.Path, MutationRunService.LocalRoot, "orphan-ready.json");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(ready) && !host.HasExited && wait.Elapsed < TimeSpan.FromSeconds(25))
                await Task.Delay(25, TestContext.Current.CancellationToken);
            if (!File.Exists(ready))
            {
                if (!host.HasExited) { host.Kill(entireProcessTree: true); await host.WaitForExitAsync(TestContext.Current.CancellationToken); }
                Assert.Fail("The actual native canary did not start: " + await stdout + " " + await stderr);
            }
            using var observed = JsonDocument.Parse(File.ReadAllBytes(ready));
            var nativeProcesses = new[] { observed.RootElement.GetProperty("parent").GetInt32(), observed.RootElement.GetProperty("child").GetInt32() };
            var live = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var original = live.Executions.Single(item => item.ParentExecutionId is null);
            var driver = live.Executions.Single(item => item.NativeContainment is not null);
            Assert.Equal(host.Id, original.PreparationOwner!.ProcessId);
            Assert.False(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(driver.NativeContainment!));
            Assert.Throws<InvalidDataException>(() =>
            {
                if (OperatingSystem.IsWindows()) CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(driver.NativeContainment! with
                { WindowsSessionId = driver.NativeContainment!.WindowsSessionId + 1 });
            });
            var premature = service.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
                WriteWorkInput(repository, "native-live-reconcile.json", new CisAgentWorkDispositionRequest("reconcile", live.Revision)
                { ExecutionId = original.ExecutionId, Evidence = ["fixture:still-live-native-owner"] }), WorkActor, "A live controller cannot be reconciled.");
            Assert.Equal(4, premature.ExitCode);
            Assert.True(premature.Work!.Task!.ReservedMilliseconds > 0);
            var current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var cancelled = service.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
                WriteWorkInput(repository, "native-cancel.json", new CisAgentWorkDispositionRequest("cancel-native", current.Revision)
                { ExecutionId = original.ExecutionId, Evidence = ["fixture:controlled-host-cancellation"] }), WorkActor, "Stop the actual native host and its verified contained driver.");
            Assert.Equal(4, cancelled.ExitCode); // Cancellation alone does not release unknown accounting/storage.
            await host.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            wait.Restart();
            while (!CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(driver.NativeContainment!) && wait.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25, TestContext.Current.CancellationToken);
            Assert.True(CisProcessJob.IsNamedNativeJobEmptyOrDestroyed(driver.NativeContainment!));
            foreach (var id in nativeProcesses)
            {
                try { using var process = Process.GetProcessById(id); Assert.True(process.HasExited); }
                catch (ArgumentException) { }
            }
            clockOffset = 31; // Only passes reconciliation grace; OS job closure above supplies the stopped-child proof.
            current = service.WorkStatus(repository.Path, "CIS-0001", "WORK-090").Work!;
            var reconciled = service.DisposeWork(repository.Path, "CIS-0001", "WORK-090",
                WriteWorkInput(repository, "native-reconcile.json", new CisAgentWorkDispositionRequest("reconcile", current.Revision)
                { ExecutionId = original.ExecutionId, Evidence = ["fixture:exact-stopped-controller-and-empty-recorded-job"] }), WorkActor,
                "Reconcile the original exact native tree after named-job closure, retaining its original restoration recipe.");
            Assert.Equal(0, reconciled.ExitCode);
            Assert.All(reconciled.Work!.Executions, execution => Assert.True(execution.Finalized));
            Assert.Equal(120_000, reconciled.Work.Task!.ConsumedMilliseconds);
            Assert.Equal(0, reconciled.Work.Task.ReservedMilliseconds);
            Assert.Equal(1, reconciled.Work.Task.UsedAttempts);
            Assert.True(reconciled.Work.Executions.Single(item => item.ExecutionId == original.ExecutionId).NativeRestorationRequired);
            var storage = ((ICisOwnedStorageController)service).Inspect(repository.Path,
                new("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor));
            Assert.True(storage.Allowed, string.Join("; ", storage.Errors));
            Assert.Equal(0, storage.Storage!.ReservedBytes);
            Assert.NotEmpty(storage.Storage.ProtectedPaths);
            Assert.True(File.Exists(ready));
            Assert.Contains("interrupted native instrumentation", File.ReadAllText(Path.Combine(repository.Path, "subject.cs")), StringComparison.Ordinal);
        }
        finally
        {
            if (!host.HasExited) { host.Kill(entireProcessTree: true); await host.WaitForExitAsync(TestContext.Current.CancellationToken); }
        }
    }
}
