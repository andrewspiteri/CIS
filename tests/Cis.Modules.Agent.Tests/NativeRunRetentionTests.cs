using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Artifacts;
using Cis.Modules.Repository;
using Cis.Modules.Testing;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void NativeWork_SupersededFailedRunBecomesEligibleWhileCurrentSummaryStaysProtected()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var resolver = new CisRepositoryContextResolver();
        var controller = new AgentService(resolver);
        var allocation = WorkAllocation(180, 180, 30, ["WORK-090"]) with
        {
            Storage = new(128 * 1024 * 1024, [new(repository.Path, MutationRunService.LocalRoot)])
            { RestorationBytes = 8 * 1024 * 1024 }
        };
        var allocated = controller.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "retention-allocation.json", allocation), WorkActor, "Bound the controlled native retention regression.");
        Assert.Equal(0, allocated.ExitCode);
        repository.Write("subject.cs", "class Subject { bool Value = true; }");
        repository.Write("canary-status", "Survived");
        repository.Write("native-retention.ps1", """
            param([string]$Output)
            $ErrorActionPreference = 'Stop'
            [IO.Directory]::CreateDirectory($Output) | Out-Null
            $statusPath = Join-Path $PSScriptRoot 'canary-status'
            $status = if ([IO.File]::Exists($statusPath)) { [IO.File]::ReadAllText($statusPath) } else { 'Killed' }
            $mutant = @{ id='0'; mutatorName='Boolean'; replacement='false'; status=$status
                location=@{ start=@{ line=1; column=1 }; end=@{ line=1; column=2 } } }
            $files = @{ 'subject.cs'=@{ language='cs'; source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'subject.cs')); mutants=@($mutant) } }
            @{ schemaVersion='2'; thresholds=@{ high=90; low=70 }; files=$files } |
                ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'report.json')
            """);
        var command = new[] { "pwsh", "-NoProfile", "-NonInteractive", "-File", "native-retention.ps1", "-Output", "{output}" };
        var profile = new MutationRunProfile
        {
            SchemaVersion = 1,
            Id = "retention",
            Command = command,
            CanaryCommand = command,
            SourceFiles = ["subject.cs"],
            ConfigFiles = ["native-retention.ps1"],
            ReportPath = "report.json",
            BreakThreshold = 90,
            TimeoutSeconds = 15,
            MaxRunBytes = 8 * 1024 * 1024,
            MaxRetainedBytes = 32 * 1024 * 1024,
            MinFreeBytes = 0,
        };
        repository.Write("native-retention.json", JsonSerializer.Serialize(profile, JsonOptions));
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action |
            |---|---|---:|---:|---|
            | mutation-runs | .cis/local/testing/mutation/retention/runs | 1 | 2 | delete |
            """);
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor);
        var runner = new MutationRunService(controller, controller);
        MutationRunResult Run() => runner.Run(new(repository.Path, "native-retention.json", "canary")
        { Work = association, MaximumSeconds = 45 }, TestContext.Current.CancellationToken);

        var failed = Run();
        Assert.Equal(4, failed.ExitCode);
        Assert.True(failed.NativeExecution!.SafelyStopped);
        Assert.True(File.Exists(Path.Combine(repository.Path, failed.ReportPath!)));
        File.Delete(Path.Combine(repository.Path, "canary-status"));
        var next = Run();
        var current = Run();
        Assert.True(next.ExitCode == 0 && current.ExitCode == 0,
            JsonSerializer.Serialize(new { next, current }, JsonOptions));
        var failedDirectory = Path.GetDirectoryName(Path.Combine(repository.Path, failed.SummaryPath!))!;
        var old = DateTime.UtcNow.AddDays(-3);
        foreach (var file in Directory.EnumerateFiles(failedDirectory, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, old);
        Directory.SetLastWriteTimeUtc(failedDirectory, old);

        var retention = new ArtifactRetentionService(resolver, nativeWork: controller, ownedStorage: controller);
        var plan = retention.Plan(repository.Path, "mutation-runs", association);

        Assert.True(plan.ExitCode == 0, string.Join("; ", plan.Errors));
        Assert.Contains(plan.Entries, entry => entry.Candidate && entry.Path == Path.GetRelativePath(repository.Path, failedDirectory).Replace('\\', '/'));
        Assert.Contains(plan.Entries, entry => entry.Protected && !entry.Candidate
            && entry.Path == Path.GetDirectoryName(current.SummaryPath!)!.Replace('\\', '/'));
        var protectedPaths = ((ICisOwnedStorageController)controller).Inspect(repository.Path, association).Storage!.ProtectedPaths;
        var currentSummary = Path.GetFullPath(Path.Combine(repository.Path, current.SummaryPath!));
        var supersededSummary = Path.GetFullPath(Path.Combine(repository.Path, failed.SummaryPath!));
        var supersededReport = Path.GetFullPath(Path.Combine(repository.Path, failed.ReportPath!));
        Assert.Contains(protectedPaths, path => string.Equals(path, currentSummary, CisPathSafety.PlatformComparison));
        Assert.DoesNotContain(protectedPaths, path => string.Equals(path, supersededSummary, CisPathSafety.PlatformComparison));
        Assert.DoesNotContain(protectedPaths, path => string.Equals(path, supersededReport, CisPathSafety.PlatformComparison));
    }
}
