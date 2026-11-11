using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Graph;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class IncrementalGovernedUnitRetryTests
{
    [Fact]
    public async Task LinuxActualGovernedUnitInterruptionReconcilesAndRetriesOnlyUnfinishedWork()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Actual Linux session interruption qualification.");
            return;
        }
        var clockOffset = 0;
        using var fixture = new GovernedUnitFixture(clock: () => DateTimeOffset.UtcNow.AddSeconds(clockOffset));
        fixture.Native.Save(fixture.Native.Profile with { TimeoutSeconds = 120 });
        fixture.Native.Write("docs/cis/catalog.yml", """
            schema_version: 1
            repository: sample
            documents:
              - id: sample:change:cis-0001:plan
                path: docs/cis/changes/CIS-0001/plan.md
                type: change-plan
                status: approved
                authority: canonical
              - id: sample:change:cis-0001:impact
                path: docs/cis/changes/CIS-0001/impact.md
                type: impact-analysis
                status: accepted
                authority: canonical
              - id: sample:change:cis-0001:task:work-090
                path: docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md
                type: agent-task
                status: in-progress
                authority: canonical
            """);
        var graph = await RunLinuxHost(fixture, "graph", "build", "--repo", fixture.Native.Path, "--format", "json");
        var built = JsonSerializer.Deserialize<GraphBuildResult>(graph.Output, LinuxJson)!;
        var graphClassification = $"Graph {built.Status}; errors={built.ErrorCount}; warnings={built.WarningCount}: "
            + string.Join("; ", built.Diagnostics.Select(diagnostic => diagnostic.Code + " " + diagnostic.Message));
        Assert.True(graph.ExitCode == 0 && built.ErrorCount == 0 && built.WarningCount == 0, graphClassification + graph.Error);
        var first = await RunLinuxHost(fixture, UnitArguments(fixture, "run", maximumBatches: 1));
        Assert.True(first.ExitCode == 3, graphClassification + "\n" + first.Output + first.Error);
        var firstResult = JsonSerializer.Deserialize<IncrementalTestResult>(first.Output, LinuxJson)!;
        Assert.Equal(1, firstResult.ExecutedUnitBatches);
        var accepted = fixture.Native.SavedState().Batches.Values.Single(batch => batch.Unit is not null).Unit!;
        var report = fixture.Native.Full(IncrementalTestService.LocalRoot + "/sample/" + accepted.ReportPath);
        var reportBefore = File.ReadAllBytes(report);
        var before = fixture.Work();
        fixture.Native.Write(".cis/local/fault-B.cs", "unit:unit-block");
        using var host = StartLinuxHost(fixture, UnitArguments(fixture, "run"));
        var stdout = host.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = host.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var cleanup = new List<(int Id, long StartTicks)>();
        try
        {
            var ready = fixture.Native.Full(".cis/native-block-ready.json");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(ready) && !host.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30))
                await Task.Delay(25, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(ready), host.HasExited ? await stdout + await stderr : "The actual Linux unit fixture did not become ready.");
            using var observed = JsonDocument.Parse(File.ReadAllBytes(ready));
            foreach (var name in new[] { "parent", "child" })
            {
                using var process = Process.GetProcessById(observed.RootElement.GetProperty(name).GetInt32());
                Assert.False(process.HasExited);
                cleanup.Add((process.Id, CisProcessSession.GetStartTicks(process.Id)));
            }
            var live = fixture.Work();
            var original = live.Executions.Single(execution => !execution.Finalized && execution.ParentExecutionId is null);
            var driver = live.Executions.Single(execution => execution.RootExecutionId == original.ExecutionId && execution.ProcessId is not null);
            Assert.Equal(host.Id, original.PreparationOwner!.ProcessId);
            Assert.Equal(CisNativeWorkContainment.LinuxOwnedSession, driver.NativeContainment!.Protocol);
            Assert.True(driver.NativeContainment.LinuxSessionId > 0);
            Assert.NotNull(driver.NativeContainment.LinuxSessionStartedAtUtc);
            Assert.True(driver.NativeContainment.LinuxSessionStartTicks >= 0);
            Assert.Equal(CisProcessSession.GetStartTicks(driver.NativeContainment.LinuxSessionId!.Value),
                driver.NativeContainment.LinuxSessionStartTicks!.Value);
            if (driver.ProcessId is { } driverId) cleanup.Add((driverId, CisProcessSession.GetStartTicks(driverId)));
            var interference = fixture.Native.Full(".cis/native-interference.lock");
            Assert.Throws<IOException>(() => { using var held = new FileStream(interference, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            var premature = DisposeLinuxWork(fixture, "reconcile", original.ExecutionId, "The actual controller and fixture child are still live.");
            Assert.NotEqual(0, premature.ExitCode);
            Assert.True(premature.Work!.Task!.ReservedMilliseconds > 0);
            var callsWhileLive = fixture.Native.Calls();
            var heldRetry = await RunLinuxHost(fixture, UnitArguments(fixture, "run"));
            Assert.NotEqual(0, heldRetry.ExitCode);
            Assert.Equal(callsWhileLive, fixture.Native.Calls());
            Assert.Equal(reportBefore, File.ReadAllBytes(report));

            // Kill only the real CLI preparer: its actual driver/session remains
            // the supported controller's responsibility, rather than test cleanup.
            host.Kill();
            await host.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var cancelled = DisposeLinuxWork(fixture, "cancel-native", original.ExecutionId,
                "Stop the recorded Linux native session after its real controller was interrupted.");
            Assert.Equal(4, cancelled.ExitCode);
            Assert.True(cancelled.Work!.Task!.ReservedMilliseconds > 0);
            wait.Restart();
            while (cleanup.Any(identity => !LinuxFixtureIdentityStopped(identity)) && wait.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25, TestContext.Current.CancellationToken);
            foreach (var identity in cleanup) Assert.True(LinuxFixtureIdentityStopped(identity));
            using (var released = new FileStream(interference, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            clockOffset = 31; // Only passes grace. Actual stopped identities/session supply process proof.
            var reconciled = DisposeLinuxWork(fixture, "reconcile", original.ExecutionId,
                "The exact recorded Linux session is empty and its original owner/driver have stopped.");
            Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
            var settled = fixture.Work();
            Assert.All(settled.Executions.Where(execution => execution.RootExecutionId == original.ExecutionId), execution =>
            {
                Assert.True(execution.Finalized);
                Assert.False(execution.NativeRestorationRequired);
            });
            Assert.True(original.ReservedMilliseconds > 0);
            Assert.Equal(before.Task!.ConsumedMilliseconds + original.ReservedMilliseconds, settled.Task!.ConsumedMilliseconds);
            Assert.Equal(live.Task!.UsedAttempts, settled.Task.UsedAttempts);
            Assert.Equal(0, settled.Task.ReservedMilliseconds);
            var close = new CisAgentWorkProgressRequest(new("Interrupted unit work was cleaned up and settled.",
                ["fixture:actual-linux-controller-stop-session-cleanup-and-unlocked-resource"], "Preserve the accepted first report.",
                "Close the interrupted logical attempt without discarding its charged cost.", "Retry only the unfinished unit batch.")
            { CloseAttempt = true, MaterialNewInformation = true }, settled.Revision);
            fixture.Native.Write(".cis/local/linux-close-attempt.json", JsonSerializer.Serialize(close, LinuxJson));
            Assert.Equal(0, fixture.Controller.RecordWorkProgress(fixture.Native.Path, "CIS-0001", "WORK-090",
                ".cis/local/linux-close-attempt.json", "Controller").ExitCode);
            File.Delete(fixture.Native.Full(".cis/local/fault-B.cs"));

            var retried = await RunLinuxHost(fixture, UnitArguments(fixture, "run"));

            Assert.True(retried.ExitCode == 0, retried.Output + retried.Error);
            var result = JsonSerializer.Deserialize<IncrementalTestResult>(retried.Output, LinuxJson)!;
            Assert.True(result.ScopeComplete);
            Assert.Equal(1, result.ReusedUnitBatches);
            Assert.Equal(1, result.ExecutedUnitBatches);
            Assert.Equal(0, result.ExecutedMutationBatches);
            Assert.Equal(reportBefore, File.ReadAllBytes(report));
            Assert.Equal(JsonSerializer.Serialize(accepted, LinuxJson),
                JsonSerializer.Serialize(fixture.Native.SavedState().Batches.Values.Single(batch => batch.SourceFile == "src/A.cs").Unit, LinuxJson));
            Assert.Equal(1, fixture.Native.Calls().Count(call => call.StartsWith("unit src/A.cs ", StringComparison.Ordinal)));
            Assert.Equal(1, fixture.Native.Calls().Count(call => call.StartsWith("unit-block src/B.cs ", StringComparison.Ordinal)));
            Assert.Equal(1, fixture.Native.Calls().Count(call => call.StartsWith("unit src/B.cs ", StringComparison.Ordinal)));
            var after = fixture.Work();
            Assert.Equal(before.Task.AllocatedMilliseconds, after.Task!.AllocatedMilliseconds);
            Assert.Equal(before.Task.AllocatedAttempts, after.Task.AllocatedAttempts);
            Assert.Equal(settled.Task.UsedAttempts + 1, after.Task.UsedAttempts);
            Assert.True(after.Task.ConsumedMilliseconds > settled.Task.ConsumedMilliseconds);
            Assert.Equal(0, after.Task.ReservedMilliseconds);
            Assert.All(after.Executions, execution => Assert.False(execution.NativeRestorationRequired));
            var calls = fixture.Native.Calls();
            var plan = await RunLinuxHost(fixture, UnitArguments(fixture, "plan"));
            var status = await RunLinuxHost(fixture, UnitArguments(fixture, "status"));
            var verified = await RunLinuxHost(fixture, UnitArguments(fixture, "verify"));
            Assert.True(plan.ExitCode == 0, plan.Output + plan.Error);
            Assert.True(status.ExitCode == 0, status.Output + status.Error);
            Assert.True(verified.ExitCode == 0, verified.Output + verified.Error);
            Assert.Equal(calls, fixture.Native.Calls());
            Assert.Equal(reportBefore, File.ReadAllBytes(report));
        }
        finally
        {
            if (!host.HasExited) { host.Kill(entireProcessTree: true); await host.WaitForExitAsync(TestContext.Current.CancellationToken); }
            foreach (var identity in cleanup)
                try
                {
                    using var process = Process.GetProcessById(identity.Id);
                    if (CisProcessSession.GetStartTicks(identity.Id) == identity.StartTicks && !process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (Exception error) when (error is ArgumentException or FileNotFoundException or DirectoryNotFoundException) { }
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static readonly JsonSerializerOptions LinuxJson = new(JsonSerializerDefaults.Web);

    private static CisAgentWorkResult DisposeLinuxWork(GovernedUnitFixture fixture, string kind, string execution, string reason)
    {
        var current = fixture.Work();
        var request = new CisAgentWorkDispositionRequest(kind, current.Revision)
        { ExecutionId = execution, Evidence = ["fixture:" + reason] };
        var path = ".cis/local/linux-" + kind + ".json";
        fixture.Native.Write(path, JsonSerializer.Serialize(request, LinuxJson));
        return fixture.Controller.DisposeWork(fixture.Native.Path, "CIS-0001", "WORK-090", path, "Controller", reason);
    }

    private static bool LinuxFixtureIdentityStopped((int Id, long StartTicks) identity)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        try { using var process = Process.GetProcessById(identity.Id); return CisProcessSession.GetStartTicks(identity.Id) != identity.StartTicks || process.HasExited; }
        catch (Exception error) when (error is ArgumentException or FileNotFoundException or DirectoryNotFoundException) { return true; }
    }

    private static string[] UnitArguments(GovernedUnitFixture fixture, string operation, int maximumBatches = 4)
    {
        var arguments = new List<string> { "test", "incremental", operation, "--repo", fixture.Native.Path, "--profile", "profile.json", "--layer", "unit", "--format", "json" };
        if (operation == "run") arguments.AddRange(["--max-batches", maximumBatches.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--max-seconds", "120", "--change", "CIS-0001", "--task", "WORK-090", "--authority", fixture.Native.Path, "--actor", "Controller"]);
        return arguments.ToArray();
    }

    private static Process StartLinuxHost(GovernedUnitFixture fixture, params string[] arguments)
    {
        var tests = typeof(IncrementalGovernedUnitRetryTests).Assembly.Location;
        var start = new ProcessStartInfo(fixture.Native.Profile.UnitCommand[0])
        { WorkingDirectory = fixture.Native.Path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(tests, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(tests, ".deps.json"), typeof(CisApplication).Assembly.Location }.Concat(arguments)) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunLinuxHost(GovernedUnitFixture fixture, params string[] arguments)
    {
        using var process = StartLinuxHost(fixture, arguments);
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
        return (process.ExitCode, await output, await error);
    }
}
