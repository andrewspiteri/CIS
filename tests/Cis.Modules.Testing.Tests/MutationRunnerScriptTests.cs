using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Workflow;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class MutationRunnerScriptTests
{
    [Fact]
    public void CanonicalMutationWorkflowCanBeLoadedByTheRuntime()
    {
        var result = new WorkflowService(new CisRepositoryContextResolver()).Describe(RepositoryRoot(), "mutation-assurance");

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Workflow);
        Assert.Single(result.Workflow.Steps);
    }

    [Fact]
    public void TimeoutStopsTheNativeProcessAndRetainsItsFirstOutput()
    {
        using var fixture = new RunnerFixture();
        var result = fixture.Run("Write-Output started; Start-Sleep -Seconds 30", "-TimeoutSeconds 3");

        Assert.Equal("timeout", result.GetProperty("status").GetString());
        Assert.Contains("started", File.ReadAllText(Path.Combine(fixture.RunRoot, "stdout.log")));
        Assert.InRange(result.GetProperty("durationMilliseconds").GetInt64(), 0, 8_000);
    }

    [Fact]
    public void StorageGrowthStopsTheProcessBeforeItCanReportSuccess()
    {
        using var fixture = new RunnerFixture();
        var output = Path.Combine(fixture.RunRoot, "payload.bin").Replace("'", "''");
        var result = fixture.Run($"[IO.File]::WriteAllBytes('{output}', [byte[]]::new(32768)); Start-Sleep -Seconds 30",
            "-MaximumRunBytes 16384");

        Assert.Equal("storage-limit", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("peakRunBytes").GetInt64() >= 32768);
        Assert.InRange(result.GetProperty("durationMilliseconds").GetInt64(), 0, 8_000);
    }

    [Fact]
    public void RetainedBudgetPreflightDoesNotLaunchAnotherProcess()
    {
        using var fixture = new RunnerFixture();
        Directory.CreateDirectory(fixture.Root);
        File.WriteAllBytes(Path.Combine(fixture.Root, "previous.bin"), new byte[2048]);
        var marker = Path.Combine(fixture.Root, "started.txt").Replace("'", "''");

        var result = fixture.Run($"[IO.File]::WriteAllText('{marker}', 'started')", "-MaximumRetainedBytes 1024");

        Assert.Equal("storage-limit", result.GetProperty("status").GetString());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "started.txt")));
        Assert.False(Directory.Exists(fixture.RunRoot));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("exitCode").ValueKind);
    }

    [Fact]
    public void ExternalScratchGrowthCountsAgainstRunAndRetainedBudgets()
    {
        using var fixture = new RunnerFixture();
        var scratch = Directory.CreateTempSubdirectory("cis-external-scratch-");
        try
        {
            var output = Path.Combine(scratch.FullName, "payload.bin").Replace("'", "''");
            var result = fixture.Run($"[IO.File]::WriteAllBytes('{output}', [byte[]]::new(32768)); Start-Sleep -Seconds 30",
                $"-ScratchRoot '{scratch.FullName.Replace("'", "''")}' -MaximumRetainedBytes 16384");
            Assert.Equal("storage-limit", result.GetProperty("status").GetString());
            Assert.True(result.GetProperty("peakRunBytes").GetInt64() >= 32768);
            Assert.True(result.GetProperty("peakRetainedBytes").GetInt64() >= 32768);
        }
        finally { scratch.Delete(recursive: true); }
    }

    [Fact]
    public void ExternalScratchRetainedBudgetIsCheckedBeforeLaunch()
    {
        using var fixture = new RunnerFixture();
        var scratch = Directory.CreateTempSubdirectory("cis-external-scratch-");
        try
        {
            File.WriteAllBytes(Path.Combine(scratch.FullName, "existing.bin"), new byte[32768]);
            var marker = Path.Combine(scratch.FullName, "started.txt");
            var result = fixture.Run($"[IO.File]::WriteAllText('{marker.Replace("'", "''")}', 'started')",
                $"-ScratchRoot '{scratch.FullName.Replace("'", "''")}' -MaximumRetainedBytes 16384");
            Assert.Equal("storage-limit", result.GetProperty("status").GetString());
            Assert.False(File.Exists(marker));
            Assert.Equal(JsonValueKind.Null, result.GetProperty("exitCode").ValueKind);
        }
        finally { scratch.Delete(recursive: true); }
    }

    [Fact]
    public void BoundedCaptureKeepsNativeFailureAndDrainsBothStreams()
    {
        using var fixture = new RunnerFixture();
        var result = fixture.Run("[Console]::Out.Write(('x' * 300000)); [Console]::Error.Write('native failure'); exit 7", "");

        Assert.Equal("exited", result.GetProperty("status").GetString());
        Assert.Equal(7, result.GetProperty("exitCode").GetInt32());
        Assert.True(result.GetProperty("outputTruncated").GetBoolean());
        Assert.Equal(262144, new FileInfo(Path.Combine(fixture.RunRoot, "stdout.log")).Length);
        Assert.Contains("native failure", File.ReadAllText(Path.Combine(fixture.RunRoot, "stderr.log")));
    }

    [Fact]
    public void GuardDoesNotLockTheLibraryThatNativeMutationBuildsMustReplace()
    {
        using var fixture = new RunnerFixture();
        var library = Path.Combine(RepositoryRoot(), "src", "Cis.Abstractions", "bin", "Release", "net10.0", "Cis.Abstractions.dll").Replace("'", "''");
        var result = fixture.Run($"$lease = [IO.File]::Open('{library}', [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite); $lease.Dispose(); Write-Output unlocked", "");

        Assert.Equal("exited", result.GetProperty("status").GetString());
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Contains("unlocked", File.ReadAllText(Path.Combine(fixture.RunRoot, "stdout.log")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildrenCannotOutliveTheirCompletedOrTimedOutParent(bool timeout)
    {
        using var fixture = new RunnerFixture();
        var pidPath = Path.Combine(fixture.RunRoot, "child.pid").Replace("'", "''");
        var script = $$"""
            $childInfo = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $childInfo.UseShellExecute = $false
            $childInfo.CreateNoWindow = $true
            $childInfo.RedirectStandardOutput = $true
            $childInfo.RedirectStandardError = $true
            foreach ($argument in @('-NoProfile','-Command','Start-Sleep -Seconds 30')) { $childInfo.ArgumentList.Add($argument) }
            $child = [Diagnostics.Process]::Start($childInfo)
            [IO.File]::WriteAllText('{{pidPath}}', [string]$child.Id)
            {{(timeout ? "Start-Sleep -Seconds 30" : "exit 0")}}
            """;

        var result = fixture.Run(script, timeout ? "-TimeoutSeconds 4" : "");

        Assert.Equal(timeout ? "timeout" : "child-leak", result.GetProperty("status").GetString());
        var childId = int.Parse(File.ReadAllText(Path.Combine(fixture.RunRoot, "child.pid")), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(childId);
            Assert.True(process.HasExited, "The owned child must be terminated before the guard returns.");
        }
        catch (ArgumentException) { /* Process already reaped. */ }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "MutationRunner.psm1")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed class RunnerFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-mutation-guard-" + Guid.NewGuid().ToString("N"));
        public string RunRoot => Path.Combine(Root, "run");

        public JsonElement Run(string childScript, string options)
        {
            var directory = new DirectoryInfo(RepositoryRoot());
            var module = Path.Combine(directory.FullName, "tools", "MutationRunner.psm1").Replace("'", "''");
            var encodedChild = Encode(childScript);
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module '{{module}}' -Force
                $result = Invoke-BoundedMutationProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-EncodedCommand', '{{encodedChild}}') -WorkingDirectory '{{directory.FullName.Replace("'", "''")}}' -RunRoot '{{RunRoot.Replace("'", "''")}}' -StorageRoot '{{Root.Replace("'", "''")}}' -MinimumFreeBytes 0 {{options}}
                $result | ConvertTo-Json -Compress
                """;
            var startInfo = new ProcessStartInfo("pwsh");
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script) })
                startInfo.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(startInfo, TimeSpan.FromSeconds(15));
            Assert.False(result.TimedOut, result.StandardError);
            Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
            using var json = JsonDocument.Parse(result.StandardOutput);
            return json.RootElement.Clone();
        }

        private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
