using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Workflow;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class ExecutionModulesTests
{
    [Fact]
    public void WorkflowNoOpResumePreservesTheRecordedExecutionTime()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/workflows/no-op.md", """
            | Step | Command | Depends on | Continue on failure | Timeout seconds |
            |---|---|---|---|---:|
            | version | dotnet --version | - | no | 30 |
            """);
        var time = DateTimeOffset.UtcNow;
        var service = new WorkflowService(new CisRepositoryContextResolver(), () => time);
        var first = service.Run(repository.Path, "no-op", "UNCHANGED");
        time = time.AddMinutes(1);

        var resumed = service.Run(repository.Path, "no-op", "UNCHANGED");

        Assert.Equal("succeeded", first.Status);
        Assert.False(resumed.Applied);
        Assert.Equal(first.Run!.UpdatedAtUtc, resumed.Run!.UpdatedAtUtc);
        Assert.Equal(first.Run.Steps, resumed.Run.Steps);
    }

    [Fact]
    public void WorkflowEnvironmentIsPassedToChildWithDistinctAttemptPaths()
    {
        using var repository = ExecutionRepository.Create();
        var inherited = Environment.GetEnvironmentVariable("CIS_TEST_REPORT");
        var command = OperatingSystem.IsWindows()
            ? "cmd.exe /d /s /c \"echo %CIS_TEST_REPORT% & exit /b 1\""
            : "/bin/sh -c \"printenv CIS_TEST_REPORT; exit 1\"";
        repository.Write("docs/cis/workflows/environment.md", $$"""
            | Step | Command | Depends on | Continue on failure | Timeout seconds | Environment |
            |---|---|---|---|---:|---|
            | browser | {{command}} | - | no | 30 | CIS_TEST_REPORT="reports/{run-id}/attempt-{attempt}/native result.json" |
            """);
        var service = new WorkflowService(new CisRepositoryContextResolver());

        var first = service.Run(repository.Path, "environment", "BROWSER-ENV");
        var second = service.Run(repository.Path, "environment", "BROWSER-ENV");

        Assert.Equal("failed", first.Status);
        Assert.Equal("failed", second.Status);
        var firstStep = Assert.Single(first.Run!.Steps);
        var secondStep = Assert.Single(second.Run!.Steps);
        Assert.Equal(1, firstStep.Attempt);
        Assert.Equal(2, secondStep.Attempt);
        var logs = Path.Combine(repository.Path, WorkflowService.RunsPath, "BROWSER-ENV");
        Assert.Contains("reports/BROWSER-ENV/attempt-1/native result.json", File.ReadAllText(Path.Combine(logs, firstStep.OutputPath)), StringComparison.Ordinal);
        Assert.Contains("reports/BROWSER-ENV/attempt-2/native result.json", File.ReadAllText(Path.Combine(logs, secondStep.OutputPath)), StringComparison.Ordinal);
        Assert.Equal(inherited, Environment.GetEnvironmentVariable("CIS_TEST_REPORT"));
    }

    [Theory]
    [InlineData("MISSING_VALUE")]
    [InlineData("BAD-NAME=value")]
    [InlineData("REPORT=one report=two")]
    [InlineData("CIS_WORKFLOW_RUN_ID=forged")]
    [InlineData("REPORT=\"unclosed")]
    public void WorkflowEnvironmentRejectsMalformedOrConflictingBindingsBeforeLaunch(string environment)
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/workflows/environment.md", $"""
            | Step | Command | Depends on | Continue on failure | Timeout seconds | Environment |
            |---|---|---|---|---:|---|
            | browser | nonexistent-command | - | no | 30 | {environment} |
            """);

        var result = new WorkflowService(new CisRepositoryContextResolver()).Run(repository.Path, "environment", "INVALID-ENV");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Null(result.Run);
    }
}
