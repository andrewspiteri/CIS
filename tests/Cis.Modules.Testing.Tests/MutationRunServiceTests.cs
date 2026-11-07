using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

[Collection("Incremental environment")]
public sealed partial class MutationRunServiceTests
{
    [Fact]
    public void InitializedTargetsCannotBypassNativeWorkByConstructingAnUncomposedService()
    {
        using var fixture = new Target();
        fixture.Write(".cis/repository.yml", "fixture initialized marker");
        var result = fixture.Run();
        Assert.Equal("held", result.Status);
        Assert.Equal("missing-work-controller", result.WorkCoverage);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("composed work controller", StringComparison.Ordinal));
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Theory]
    [InlineData(".cis/repository.yml")]
    [InlineData(".cis/workspace.yml")]
    public void NestedTargetsCannotHideInitializedAuthorityFromAnUncomposedService(string marker)
    {
        using var fixture = new Target();
        fixture.Write(marker, "fixture initialized authority marker");
        fixture.Write("nested/target/profile.json", JsonSerializer.Serialize(fixture.Profile, Target.Json));
        fixture.Write("nested/target/subject.cs", File.ReadAllText(fixture.Full("subject.cs")));
        fixture.Write("nested/target/native.ps1", File.ReadAllText(fixture.Full("native.ps1")));

        var result = new MutationRunService().Run(new(fixture.Full("nested/target"), "profile.json"), TestContext.Current.CancellationToken);

        Assert.Equal("held", result.Status);
        Assert.Equal("missing-work-controller", result.WorkCoverage);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("composed work controller", StringComparison.Ordinal));
        Assert.False(Directory.Exists(fixture.Full("nested/target/" + MutationRunService.LocalRoot)));
    }

    [Fact]
    public void DeniedNativeAssociationHoldsBeforeOutputAndNativePreparation()
    {
        using var fixture = new Target();
        var controller = new DeniedNativeController();
        var result = new MutationRunService(controller).Run(new(fixture.Path, "profile.json")
        { Work = new("CIS-0001", "WORK-090", Actor: "Fixture controller") }, TestContext.Current.CancellationToken);
        Assert.Equal("held", result.Status);
        Assert.Single(controller.Requests);
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void GovernedCanaryWithoutANarrowCommandCannotLaunchTheBroadCommand()
    {
        using var fixture = new Target();
        fixture.Save(fixture.Profile with { CanaryCommand = null, Command = fixture.Command("Killed", marker: true) });
        var controller = new CanaryAdmissionController();

        var result = new MutationRunService(controller).Run(new(fixture.Path, "profile.json", "canary"), TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, message => message.Contains("separate narrow canaryCommand", StringComparison.Ordinal));
        Assert.Null(result.NativeExecution);
        Assert.False(File.Exists(fixture.Full("broad-started")));
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    private sealed class CanaryAdmissionController : ICisNativeWorkController
    {
        public CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null)
            => new(true, "admitted", "controlled prelaunch admission", null, null, []);
        public CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference reference) => Inspect(authorityPath);
        public CisNativeWorkAdmission Admit(CisNativeWorkRequest request) => Inspect(request.RepositoryPath);
    }

    [Fact]
    public async Task ControlledNativeCancellationReturnsActualStoppedJobEvidence()
    {
        using var fixture = new Target();
        fixture.Write("native.ps1", """
            param([string]$Output)
            $ready = Join-Path $PSScriptRoot 'native-ready'
            [IO.File]::WriteAllText(($ready + '.tmp'), [string]$PID)
            [IO.File]::Move(($ready + '.tmp'), $ready)
            while ($true) { Start-Sleep -Milliseconds 100 }
            """);
        fixture.Save(fixture.Profile with { Command = ["pwsh", "-NoProfile", "-NonInteractive", "-File", "native.ps1", "-Output", "{output}"] });
        using var cancellation = new CancellationTokenSource();
        var running = Task.Run(() => new MutationRunService().Run(new(fixture.Path, "profile.json"), cancellation.Token));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(fixture.Full("native-ready")) && !running.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(fixture.Full("native-ready")), "The controlled native target did not reach its ready observation.");
        var processId = int.Parse(File.ReadAllText(fixture.Full("native-ready")), System.Globalization.CultureInfo.InvariantCulture);
        cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("cancelled", result.Status);
        Assert.True(result.NativeExecution!.SafelyStopped);
        Assert.False(result.Qualified);
        try { using var process = System.Diagnostics.Process.GetProcessById(processId); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }

    private sealed class DeniedNativeController : ICisNativeWorkController
    {
        public List<CisNativeWorkRequest> Requests { get; } = [];
        public CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null)
            => new(true, "held", "governed-native", null, null, ["Select the existing canonical owner before native preparation."]);
        public CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference reference) => Inspect(authorityPath);
        public CisNativeWorkAdmission Admit(CisNativeWorkRequest request) { Requests.Add(request); return Inspect(request.RepositoryPath); }
    }

    [Theory]
    [InlineData("id", "../other")]
    [InlineData("workingDirectory", "../other")]
    [InlineData("reportPath", "../report.json")]
    [InlineData("timeoutSeconds", 0)]
    [InlineData("breakThreshold", 101)]
    [InlineData("minimumCanaryKills", 0)]
    [InlineData("unknownOption", true)]
    public void InvalidProfileDoesNotCreateAnExecutionDirectory(string field, object value)
    {
        using var fixture = new Target();
        var profile = JsonSerializer.SerializeToNode(fixture.Profile, Target.Json)!.AsObject();
        profile[field] = JsonSerializer.SerializeToNode(value);
        fixture.Write("profile.json", profile.ToJsonString());

        var result = fixture.Run("qualification");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-profile", result.Status);
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void QualificationRequiresDeclaredSourcesAndASeparateCanary()
    {
        using var fixture = new Target();
        fixture.Save(fixture.Profile with { SourceFiles = [], CanaryCommand = null });

        var result = fixture.Run("qualification");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Contains("Qualification requires", StringComparison.Ordinal));
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Theory]
    [InlineData("empty-name")]
    [InlineData("long-name")]
    [InlineData("equals-name")]
    [InlineData("nul-name")]
    [InlineData("null-value")]
    [InlineData("long-value")]
    [InlineData("nul-value")]
    [InlineData("too-many")]
    public void InvalidEnvironmentFailsBeforeExecution(string fault)
    {
        using var fixture = new Target();
        var environment = fault switch
        {
            "empty-name" => new Dictionary<string, string> { [""] = "value" },
            "long-name" => new Dictionary<string, string> { [new string('x', 129)] = "value" },
            "equals-name" => new Dictionary<string, string> { ["invalid=name"] = "value" },
            "nul-name" => new Dictionary<string, string> { ["invalid\0name"] = "value" },
            "null-value" => new Dictionary<string, string> { ["NAME"] = null! },
            "long-value" => new Dictionary<string, string> { ["NAME"] = new string('x', 32769) },
            "nul-value" => new Dictionary<string, string> { ["NAME"] = "invalid\0value" },
            "too-many" => Enumerable.Range(0, 65).ToDictionary(value => "NAME" + value, _ => "value"),
            _ => throw new InvalidOperationException("Unknown fixture."),
        };
        fixture.Save(fixture.Profile with { Environment = environment });

        var result = fixture.Run();

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-profile", result.Status);
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void ProfileEnvironmentReachesNativeTargetWithoutMutatingCallerOrDuplicatingValuesInResults()
    {
        using var fixture = new Target();
        var name = "CIS_MUTATION_TARGET_ONLY_" + Guid.NewGuid().ToString("N");
        var value = "opaque-profile-value-" + Guid.NewGuid().ToString("N");
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
        Assert.Null(System.Environment.GetEnvironmentVariable(name));
        fixture.Save(fixture.Profile with
        {
            Environment = new() { [name] = value },
            Command = fixture.Command("Killed", requiredEnvironmentName: name, requiredEnvironmentHash: digest),
        });

        var result = fixture.Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Assessment!.Mutation!.Killed);
        Assert.Null(System.Environment.GetEnvironmentVariable(name));
        Assert.DoesNotContain(value, JsonSerializer.Serialize(result, Target.Json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(8 * 1024 * 1024, 0, 0, "exited")]
    [InlineData(1024 * 1024, 0, 4, "storage-limit")]
    [InlineData(3 * 1024 * 1024, 1536 * 1024, 4, "storage-limit")]
    public void NativeTemporaryFilesAreBudgetedInsideOwnedStorageAndRemoved(long budget, int extraSourceBytes, int exitCode, string nativeStatus)
    {
        using var fixture = new Target();
        const int temporaryBytes = 2 * 1024 * 1024;
        var callerEnvironment = new[] { "TEMP", "TMP", "TMPDIR" }
            .ToDictionary(name => name, System.Environment.GetEnvironmentVariable);
        fixture.Save(fixture.Profile with
        {
            Command = fixture.Command("Killed", extraStatus: extraSourceBytes > 0 ? "Ignored" : null,
                extraSourceBytes: extraSourceBytes, temporaryBytes: temporaryBytes),
            MaxRunBytes = budget,
        });

        var result = fixture.Run();

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(nativeStatus, result.NativeExecution!.Status);
        Assert.True(result.NativeExecution.PeakRunBytes >= temporaryBytes);
        if (extraSourceBytes > 0)
        {
            Assert.True(result.NativeExecution.PeakRunBytes >= budget);
        }
        using var observed = JsonDocument.Parse(File.ReadAllText(fixture.Full("native-temp-observed.json")));
        var scratch = observed.RootElement.GetProperty("file").GetString()!;
        var runRoot = System.IO.Path.GetDirectoryName(fixture.Full(result.SummaryPath!))!;
        var scratchStorage = OperatingSystem.IsLinux() ? System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar)
            : fixture.Full(MutationRunService.LocalRoot + "/.tmp");
        Assert.StartsWith(scratchStorage + System.IO.Path.DirectorySeparatorChar, scratch, CisPathSafety.PlatformComparison);
        Assert.False(CisPathSafety.IsUnderRoot(runRoot, scratch, allowRoot: true));
        Assert.Equal(OperatingSystem.IsLinux() ? 26 : 22, new DirectoryInfo(System.IO.Path.GetDirectoryName(scratch)!).Name.Length);
        if (OperatingSystem.IsLinux()) Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(System.IO.Path.GetDirectoryName(scratch)!), 1, 40);
        Assert.False(File.Exists(scratch));
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(scratch)));
        foreach (var name in callerEnvironment.Keys)
        {
            Assert.Equal(System.IO.Path.GetDirectoryName(scratch), observed.RootElement.GetProperty(name).GetString());
            Assert.Equal(callerEnvironment[name], System.Environment.GetEnvironmentVariable(name));
        }
    }

    [Fact]
    public void ExplicitNativeTemporaryDirectoryOverridesArePreserved()
    {
        using var fixture = new Target();
        var explicitTemporary = fixture.Full("explicit-temporary");
        Directory.CreateDirectory(explicitTemporary);
        fixture.Save(fixture.Profile with
        {
            Command = fixture.Command("Killed", temporaryBytes: 1024),
            Environment = new[] { "TEMP", "TMP", "TMPDIR" }.ToDictionary(
                name => OperatingSystem.IsWindows() ? name.ToLowerInvariant() : name,
                _ => explicitTemporary),
        });

        var result = fixture.Run();

        Assert.Equal(0, result.ExitCode);
        using var observed = JsonDocument.Parse(File.ReadAllText(fixture.Full("native-temp-observed.json")));
        var scratch = observed.RootElement.GetProperty("file").GetString()!;
        Assert.Equal(explicitTemporary, System.IO.Path.GetDirectoryName(scratch));
        Assert.Equal(1024L, new FileInfo(scratch).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTextDiagnosticsAreRetainedOnlyForFailedRuns(bool failBeforeReport)
    {
        using var fixture = new Target();
        fixture.Save(fixture.Profile with
        {
            Command = fixture.Command("Killed", temporaryBytes: 1024, nativeLogs: true, failBeforeReport: failBeforeReport),
        });

        var result = fixture.Run();

        Assert.Equal(failBeforeReport ? 4 : 0, result.ExitCode);
        Assert.Equal(failBeforeReport ? 7 : 0, result.NativeExecution!.ExitCode);
        var runRoot = System.IO.Path.GetDirectoryName(fixture.Full(result.SummaryPath!))!;
        var log = System.IO.Path.Combine(runRoot, "logs", "nested", "analysis.log");
        Assert.Equal(failBeforeReport, File.Exists(log));
        if (failBeforeReport)
        {
            Assert.Equal("The native command failed before producing its report.", File.ReadAllText(log));
            Assert.Null(result.Assessment);
            Assert.False(result.Qualified);
        }
        Assert.False(File.Exists(System.IO.Path.Combine(runRoot, "logs", "native-cache.bin")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(runRoot, "native-cache")));
        using var observed = JsonDocument.Parse(File.ReadAllText(fixture.Full("native-temp-observed.json")));
        var scratch = observed.RootElement.GetProperty("file").GetString()!;
        Assert.False(File.Exists(scratch));
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(scratch)));
    }

    [Fact]
    public void FailedNativeLogRetentionKeepsWholeFilesWithinCountAndByteLimits()
    {
        using var fixture = new Target();
        fixture.Save(fixture.Profile with
        {
            Command = fixture.Command("Killed", nativeLogs: true, failBeforeReport: true, oversizedLogs: true),
            MaxRunBytes = 64 * 1024 * 1024,
            MaxRetainedBytes = 128 * 1024 * 1024,
        });

        var result = fixture.Run();

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(7, result.NativeExecution!.ExitCode);
        var runRoot = System.IO.Path.GetDirectoryName(fixture.Full(result.SummaryPath!))!;
        var logs = System.IO.Path.Combine(runRoot, "logs");
        var retained = Directory.GetFiles(logs, "*", SearchOption.AllDirectories);
        var expected = new[] { "01-large.log" }.Concat(Enumerable.Range(3, 31).Select(number => $"{number:D2}-trace.log"));
        Assert.Equal(expected, retained.Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(retained.Sum(path => new FileInfo(path).Length) <= 16 * 1024 * 1024);
        Assert.False(File.Exists(System.IO.Path.Combine(logs, "00-oversized.txt")));
        Assert.Equal(10 * 1024 * 1024L, new FileInfo(System.IO.Path.Combine(logs, "01-large.log")).Length);
        Assert.False(File.Exists(System.IO.Path.Combine(logs, "02-large.log")));
        Assert.Null(result.Assessment);
        Assert.False(result.Qualified);
    }

    [Fact]
    public void QualificationWithoutRepresentativeProofHoldsBeforeNativePreparation()
    {
        using var fixture = new Target();

        var result = fixture.Run("qualification");

        Assert.Equal("pilot-required", result.Status);
        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.Null(result.NativeExecution);
        Assert.Null(result.Canary);
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void SuccessfulQualificationKeepsNativeReportsAndOnlyRunSummaries()
    {
        using var fixture = new Target();
        PrepareQualification(fixture);

        var result = fixture.Run("qualification");

        Assert.True(result.Qualified, JsonSerializer.Serialize(result, Target.Json));
        Assert.Equal("qualified", result.Status);
        Assert.False(result.Partial);
        Assert.True(result.SourceIdentity!.Unchanged);
        Assert.True(result.Assessment!.Mutation!.ValidEvidence);
        Assert.True(result.Assessment.Mutation.ThresholdMet);
        Assert.Equal(64, result.NativeExecution!.CisToolAssemblySha256!.Length);
        Assert.True(System.IO.Path.IsPathRooted(result.NativeExecution.ExecutablePath!));
        Assert.Equal("cleaned-after-assessment", result.Canary!.ReportAvailability);
        Assert.Null(result.Canary.ReportPath);
        Assert.Empty(result.Canary.Assessment!.Artifacts);
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(fixture.Full(result.SummaryPath!))!));
        Assert.Equal(File.ReadAllBytes(fixture.Latest), File.ReadAllBytes(fixture.Full(MutationRunService.LocalRoot + "/sample/last-qualification.json")));
        using var sidecar = JsonDocument.Parse(File.ReadAllText(fixture.Latest + ".assessment.json"));
        Assert.True(sidecar.RootElement.GetProperty("cisRun").GetProperty("qualified").GetBoolean());
        Assert.Equal(1, sidecar.RootElement.GetProperty("cisRun").GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void DiagnosticRemainsPartialEvenWithACompleteKillScore()
    {
        using var fixture = new Target();

        var result = fixture.Run();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Partial);
        Assert.False(result.Qualified);
        Assert.False(result.Assessment!.Mutation!.Qualified);
        Assert.Equal(100, result.Assessment.Mutation.Score);
        Assert.True(File.Exists(fixture.Full(result.ReportPath!)));
        Assert.False(File.Exists(fixture.Full(MutationRunService.LocalRoot + "/sample/last-qualification.json")));
        using var sidecar = JsonDocument.Parse(File.ReadAllText(fixture.Latest + ".assessment.json"));
        Assert.True(sidecar.RootElement.GetProperty("cisRun").GetProperty("partial").GetBoolean());
    }

    [Theory]
    [InlineData("File ignored by mutate filter", "None", true)]
    [InlineData("different source", "None", false)]
    [InlineData("File ignored by mutate filter", "Killed", false)]
    [InlineData("File ignored by mutate filter", "Ignored", false)]
    [InlineData("File ignored by mutate filter", "CompileError", false)]
    public void NarrowCanaryAcceptsOnlyTheEmptyNativeFilterPlaceholder(string source, string status, bool allowed)
    {
        using var fixture = new Target();
        const string actual = "class Other { bool Value = true; }";
        fixture.Write("other.cs", actual);
        fixture.Save(fixture.Profile with
        {
            SourceFiles = ["subject.cs", "other.cs"],
            CanaryCommand = fixture.Command("Killed", declaredOtherSource: source, declaredOtherStatus: status),
            Command = fixture.Command("Killed", marker: true, declaredOtherSource: actual, declaredOtherStatus: "Killed"),
        });

        var result = fixture.Run("canary");

        Assert.Equal(allowed ? 0 : 4, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.False(File.Exists(fixture.Full("broad-started")));
        if (allowed) Assert.Equal(1, result.Assessment!.Mutation!.Killed);
    }

    [Fact]
    public void PartialDiagnosticPlaceholderRetainsCountsWithoutClaimingCompleteScope()
    {
        using var fixture = new Target();
        fixture.Write("other.cs", "class Other { bool Value = true; }");
        fixture.Save(fixture.Profile with
        {
            SourceFiles = ["subject.cs", "other.cs"],
            Command = fixture.Command("Killed", declaredOtherSource: "File ignored by mutate filter", declaredOtherStatus: "None"),
        });

        var result = fixture.Run();

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.True(result.Partial);
        Assert.Equal(1, result.Assessment!.Mutation!.Killed);
    }

    [Fact]
    public void FullQualificationCannotUseTheCanaryPlaceholderForADeclaredFile()
    {
        using var fixture = new Target();
        var profile = PrepareQualification(fixture);
        var command = profile.Command.ToArray();
        command[^1] = "subject.cs,other.cs";
        fixture.Save(profile with { SourceFiles = ["subject.cs", "other.cs"], Command = command });
        fixture.Write(".cis/native-protocol-fault.json", "{\"declaredOtherSource\":\"File ignored by mutate filter\",\"declaredOtherStatus\":\"None\"}");

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(0, result.Canary!.ExitCode);
        Assert.NotNull(result.NativeExecution);
        Assert.False(result.Qualified);
        Assert.Contains(result.Diagnostics, item => item.Contains("does not match the declared source bytes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("diagnostic", 0, "diagnostic-completed", TestFailureKind.None)]
    [InlineData("qualification", 4, "failed", TestFailureKind.Product)]
    public void ThresholdExitPreservesNormalDiagnosticExecutionAndQualificationFindings(string kind, int exitCode,
        string status, TestFailureKind failureKind)
    {
        using var fixture = new Target();
        if (kind == "qualification")
        {
            PrepareQualification(fixture);
            fixture.Write(".cis/native-protocol-fault.json", "{\"status\":\"Survived\",\"nativeExit\":2}");
        }
        else fixture.Save(fixture.Profile with { Command = fixture.Command("Survived", nativeExit: 2) });

        var result = fixture.Run(kind);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(status, result.Status);
        Assert.Equal(2, result.NativeExecution!.ExitCode);
        Assert.Equal(0, result.Assessment!.Mutation!.Score);
        Assert.Equal(1, result.Assessment.Mutation.Survived);
        Assert.Equal(failureKind, result.Assessment.FailureKind);
        Assert.False(result.Qualified);
        Assert.False(result.Assessment.Mutation.Qualified);
        Assert.True(File.Exists(fixture.Latest));
    }

    [Fact]
    public void FailedCanaryPreventsTheBroadCommand()
    {
        using var fixture = new Target();
        fixture.Save(PilotProfile(fixture, true, marker: true) with { CanaryCommand = fixture.Command("Survived") });

        var result = fixture.Run("canary");

        Assert.Equal("failed", result.Status);
        Assert.Equal(4, result.ExitCode);
        Assert.False(File.Exists(fixture.Full("broad-started")));
        Assert.NotNull(result.Pilot);
        Assert.Equal("canary-failed", result.Pilot.Status);
        Assert.False(result.Pilot.Ready);
        Assert.True(result.Pilot.RecoveryRequired);
        Assert.Empty(result.Pilot.Observations);
        var summary = fixture.Full(MutationRunService.LocalRoot + "/sample/representative-pilot.json");
        Assert.True(File.Exists(summary));
        var retained = JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(File.ReadAllBytes(summary), Target.Json)!;
        Assert.True(retained.RecoveryRequired);
        Assert.False(retained.Ready);
        Assert.Empty(retained.Observations);
        Assert.Equal(0, result.Assessment!.Mutation!.Killed);
        Assert.True(File.Exists(fixture.Full(result.ReportPath!)));
    }

    [Fact]
    public void SourceDriftRetainsCountsAndPreventsQualification()
    {
        using var fixture = new Target();
        PrepareQualification(fixture);
        fixture.Write(".cis/native-protocol-fault.json", "{\"changeSource\":true}");

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.False(result.SourceIdentity!.Unchanged);
        Assert.Equal(1, result.Assessment!.Mutation!.Killed);
        Assert.True(File.Exists(fixture.Latest));
        Assert.False(result.Assessment.Mutation.Qualified);
    }

    [Fact]
    public void FailedNativeCommandPublishesItsReadableReportWithoutPublishingQualification()
    {
        using var fixture = new Target();
        PrepareQualification(fixture);
        fixture.Write(".cis/native-protocol-fault.json", "{\"nativeExit\":7}");

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(7, result.NativeExecution!.ExitCode);
        Assert.Equal(1, result.Assessment!.Mutation!.Killed);
        Assert.False(result.Assessment.Mutation.Qualified);
        Assert.True(File.Exists(fixture.Latest));
        Assert.False(File.Exists(fixture.Full(MutationRunService.LocalRoot + "/sample/last-qualification.json")));
    }

    [Fact]
    public void UnprovenQualificationDoesNotStartCostlyPublicationDespiteSufficientNativeBudget()
    {
        using var fixture = new Target();
        const long budget = 200_000;
        fixture.Save(fixture.Profile with
        {
            Command = fixture.Command("Killed", extraStatus: "Ignored", extraSourceBytes: 131_072),
            MaxRetainedBytes = budget,
        });

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.Equal("pilot-required", result.Status);
        Assert.Null(result.NativeExecution);
        Assert.False(File.Exists(fixture.Latest));
        Assert.False(File.Exists(fixture.Full(MutationRunService.LocalRoot + "/sample/last-qualification.json")));
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void PublicationBudgetBlocksTheSecondReportCopyWithoutLosingNativeCounts()
    {
        using var fixture = new Target();
        var profile = PrepareQualification(fixture);
        var existingBytes = Directory.EnumerateFiles(fixture.Full(MutationRunService.LocalRoot), "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        var budget = existingBytes + 200_000;
        fixture.Write(".cis/native-protocol-fault.json", "{\"extraStatus\":\"Ignored\",\"extraSourceBytes\":131072}");
        fixture.Save(profile with { MaxRetainedBytes = budget });

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Qualified);
        Assert.Equal("exited", result.NativeExecution!.Status);
        Assert.Equal(0, result.NativeExecution.ExitCode);
        Assert.Equal(1, result.Assessment!.Mutation!.Killed);
        Assert.Equal(1, result.Assessment.Mutation.Ignored);
        Assert.True(result.SourceIdentity!.Unchanged);
        Assert.Contains(result.Diagnostics, item => item.Contains("retained storage limit", StringComparison.Ordinal));
        Assert.True(File.Exists(fixture.Latest));
        Assert.False(File.Exists(fixture.Full(MutationRunService.LocalRoot + "/sample/last-qualification.json")));
        Assert.True(Directory.EnumerateFiles(fixture.Full(MutationRunService.LocalRoot), "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length) < budget);
        using var marker = JsonDocument.Parse(File.ReadAllText(fixture.Full(MutationRunService.LocalRoot + "/sample/latest-run.json")));
        Assert.Equal(4, marker.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(marker.RootElement.GetProperty("qualified").GetBoolean());
    }

    [Fact]
    public void LaterFailedCanaryMarksUnchangedSourceQualificationAsHistorical()
    {
        using var fixture = new Target();
        PrepareQualification(fixture);
        var passed = fixture.Run("qualification");
        Assert.True(passed.Qualified, JsonSerializer.Serialize(passed, Target.Json));
        var report = File.ReadAllBytes(fixture.Latest);
        // A new native failure can occur with the same pinned source/config/profile.
        fixture.Write("canary-fail", "a runtime prerequisite became unavailable");

        var failed = fixture.Run("qualification");

        Assert.Equal("canary-failed", failed.Status);
        Assert.Equal(passed.SourceIdentity!.Digest, failed.SourceIdentity!.Digest);
        Assert.True(failed.SourceIdentity.Unchanged);
        Assert.Equal(report, File.ReadAllBytes(fixture.Latest));
        using var marker = JsonDocument.Parse(File.ReadAllText(fixture.Full(MutationRunService.LocalRoot + "/sample/latest-run.json")));
        Assert.Equal(failed.RunId, marker.RootElement.GetProperty("runId").GetString());
        Assert.NotEqual(passed.RunId, marker.RootElement.GetProperty("runId").GetString());
        Assert.Equal(4, marker.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(marker.RootElement.GetProperty("qualified").GetBoolean());
    }

    [Fact]
    public void EarlyPublicationFailureLeavesTheRetainedSidecarUnqualified()
    {
        using var fixture = new Target();
        var profile = PrepareQualification(fixture);
        fixture.Write(".cis/native-protocol-fault.json", "{\"extraStatus\":\"Ignored\",\"extraSourceBytes\":65536}");
        var baseline = fixture.Run("qualification");
        Assert.True(baseline.Qualified, JsonSerializer.Serialize(baseline, Target.Json));
        var reportBytes = new FileInfo(fixture.Latest).Length;
        var nativePeak = baseline.NativeExecution!.PeakRunBytes;
        // This host's actual guard footprint fits, but the assessment sidecar cannot.
        Assert.True(new FileInfo(fixture.Latest + ".assessment.json").Length > nativePeak - reportBytes + 1);
        fixture.Save(profile with { MaxRunBytes = nativePeak + 1 });

        var failed = fixture.Run("qualification");

        Assert.Equal(4, failed.ExitCode);
        Assert.Equal("exited", failed.NativeExecution!.Status);
        Assert.Equal(0, failed.NativeExecution.ExitCode);
        Assert.Equal(1, failed.Assessment!.Mutation!.Killed);
        Assert.False(failed.Assessment.Mutation.Qualified);
        Assert.Contains(failed.Diagnostics, item => item.Contains("run storage limit", StringComparison.Ordinal));
        using var sidecar = JsonDocument.Parse(File.ReadAllText(fixture.Full(failed.ReportPath!) + ".assessment.json"));
        Assert.False(sidecar.RootElement.GetProperty("mutation").GetProperty("qualified").GetBoolean());
        Assert.False(sidecar.RootElement.GetProperty("cisRun").GetProperty("qualified").GetBoolean());
        Assert.Equal("failed", sidecar.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Ignored", true)]
    [InlineData("Killed", false)]
    public void OutOfScopeFilesMayContainOnlyNonEligibleDefinitions(string extraStatus, bool accepted)
    {
        using var fixture = new Target();
        PrepareQualification(fixture);
        fixture.Write(".cis/native-protocol-fault.json", JsonSerializer.Serialize(new { extraStatus }, Target.Json));

        var result = fixture.Run("qualification");

        Assert.Equal(accepted, result.Qualified);
        Assert.Equal(accepted ? 0 : 4, result.ExitCode);
        Assert.NotNull(result.Assessment!.Mutation);
    }

    [Fact]
    public void FailedPostflightHealthCheckPreservesNativeCountsAndBlocksQualification()
    {
        using var fixture = new Target();
        var profile = PrepareQualification(fixture);
        fixture.Save(profile with
        {
            HealthCheckCommand = ["pwsh", "-NoProfile", "-Command", "Write-Output 'checking {output}'; exit 7"],
        });

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        // A failing configured hook stops the canary before the broad command too.
        Assert.Equal(7, result.Canary!.HealthCheckExecution!.ExitCode);
        Assert.Equal(1, result.Canary.Assessment!.Mutation!.Killed);
        Assert.False(result.Qualified);
    }

    [Fact]
    public void PostflightDeletionCannotLeaveTheSourceIdentityMarkedUnchanged()
    {
        using var fixture = new Target();
        var profile = PrepareQualification(fixture);
        fixture.Save(profile with
        {
            HealthCheckCommand = ["pwsh", "-NoProfile", "-Command", "[IO.File]::Delete((Join-Path (Get-Location) 'subject.cs')); exit 0"],
        });

        var result = fixture.Run("qualification");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Canary!.SourceIdentity!.Unchanged);
        Assert.False(result.SourceIdentity!.Unchanged);
        Assert.Equal(1, result.Canary.Assessment!.Mutation!.Killed);
    }

    private sealed class Target : IDisposable
    {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-mutation-target-" + Guid.NewGuid().ToString("N"));
        internal string Latest => Full(MutationRunService.LocalRoot + "/sample/latest-result.json");
        internal MutationRunProfile Profile { get; }

        internal Target()
        {
            Directory.CreateDirectory(Path);
            Write("subject.cs", "class Subject { bool Value = true; }");
            Write("native.ps1", """
                param([string]$Output, [string]$Status = 'Killed', [switch]$ChangeSource, [switch]$Marker, [string]$ExtraStatus,
                    [int]$NativeExit = 0, [int]$ExtraSourceBytes = 0, [switch]$Canary,
                    [string]$RequiredEnvironmentName, [string]$RequiredEnvironmentHash, [int]$TemporaryBytes = 0,
                    [switch]$NativeLogs, [switch]$FailBeforeReport, [switch]$OversizedLogs,
                    [string]$DeclaredOtherSource, [string]$DeclaredOtherStatus)
                $ErrorActionPreference = 'Stop'
                $environmentProbe = Join-Path $PSScriptRoot '.cis/environment-probe.json'
                if ([IO.File]::Exists($environmentProbe)) {
                    $values = @{}
                    foreach ($name in ([IO.File]::ReadAllText($environmentProbe) | ConvertFrom-Json)) {
                        $values[$name] = [Environment]::GetEnvironmentVariable($name)
                    }
                    $probe = @{ kind = 'mutation'; output = [IO.Path]::GetFileName($Output); values = $values }
                    if ($Canary) { $probe.kind = 'canary' }
                    [IO.File]::AppendAllText((Join-Path $PSScriptRoot '.cis/environment-launches.jsonl'),
                        (($probe | ConvertTo-Json -Depth 4 -Compress) + [Environment]::NewLine))
                }
                $configurationIndex = [Array]::IndexOf($args, '--config-file')
                if ($configurationIndex -ge 0 -and -not [IO.File]::Exists((Join-Path $PSScriptRoot $args[$configurationIndex + 1]))) {
                    [Console]::Error.WriteLine('The synthetic native configuration is missing.'); exit 7
                }
                # These explicit runtime faults test report/publication consumers,
                # not genuine mutant activation or governed representative proof.
                $faultPath = Join-Path $PSScriptRoot '.cis/native-protocol-fault.json'
                if (-not $Canary -and [IO.File]::Exists($faultPath)) {
                    $fault = [IO.File]::ReadAllText($faultPath) | ConvertFrom-Json
                    if ($null -ne $fault.status) { $Status = $fault.status }
                    if ($null -ne $fault.changeSource) { $ChangeSource = $fault.changeSource }
                    if ($null -ne $fault.nativeExit) { $NativeExit = $fault.nativeExit }
                    if ($null -ne $fault.extraStatus) { $ExtraStatus = $fault.extraStatus }
                    if ($null -ne $fault.extraSourceBytes) { $ExtraSourceBytes = $fault.extraSourceBytes }
                    if ($null -ne $fault.declaredOtherSource) { $DeclaredOtherSource = $fault.declaredOtherSource }
                    if ($null -ne $fault.declaredOtherStatus) { $DeclaredOtherStatus = $fault.declaredOtherStatus }
                }
                if ($RequiredEnvironmentName) {
                    $actual = [Environment]::GetEnvironmentVariable($RequiredEnvironmentName)
                    if ($null -eq $actual) { throw 'The native target did not receive its scoped environment.' }
                    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($actual))).ToLowerInvariant()
                    if ($hash -cne $RequiredEnvironmentHash) { throw 'The native target received the wrong scoped environment.' }
                }
                if ($Canary -and [IO.File]::Exists((Join-Path $PSScriptRoot 'canary-fail'))) { $Status = 'Survived' }
                if ($Marker) { [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'broad-started'), 'started') }
                [IO.Directory]::CreateDirectory($Output) | Out-Null
                if ($TemporaryBytes -gt 0) {
                    $temporaryFile = Join-Path ([IO.Path]::GetTempPath()) ('native-scratch-' + [guid]::NewGuid().ToString('N') + '.bin')
                    $observed = @{ file = $temporaryFile }
                    foreach ($name in @('TEMP', 'TMP', 'TMPDIR')) { $observed[$name] = [Environment]::GetEnvironmentVariable($name) }
                    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'native-temp-observed.json'), ($observed | ConvertTo-Json))
                    [IO.File]::WriteAllBytes($temporaryFile, [byte[]]::new($TemporaryBytes))
                }
                if ($NativeLogs) {
                    $logs = Join-Path $Output 'logs'
                    [IO.Directory]::CreateDirectory((Join-Path $logs 'nested')) | Out-Null
                    [IO.File]::WriteAllText((Join-Path $logs 'nested/analysis.log'), 'The native command failed before producing its report.')
                    [IO.File]::WriteAllBytes((Join-Path $logs 'native-cache.bin'), [byte[]]::new(1024))
                    $cache = Join-Path $Output 'native-cache'
                    [IO.Directory]::CreateDirectory($cache) | Out-Null
                    [IO.File]::WriteAllBytes((Join-Path $cache 'deployment.dll'), [byte[]]::new(1024))
                    if ($OversizedLogs) {
                        foreach ($entry in @(@{name='00-oversized.txt';bytes=17MB}, @{name='01-large.log';bytes=10MB}, @{name='02-large.log';bytes=10MB})) {
                            $stream = [IO.File]::Create((Join-Path $logs $entry.name))
                            try { $stream.SetLength($entry.bytes) } finally { $stream.Dispose() }
                        }
                        for ($number = 3; $number -lt 43; $number++) {
                            [IO.File]::WriteAllText((Join-Path $logs ('{0:D2}-trace.log' -f $number)), 'trace')
                        }
                    }
                }
                if ($FailBeforeReport) { exit 7 }
                $mutant = @{ id = '0'; mutatorName = 'Boolean'; replacement = 'false'; status = $Status
                    location = @{ start = @{ line = 1; column = 1 }; end = @{ line = 1; column = 2 } } }
                $files = @{ 'subject.cs' = @{ language = 'cs'; source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'subject.cs')); mutants = @($mutant) } }
                if ($ExtraStatus) {
                    $extra = $mutant.Clone(); $extra.status = $ExtraStatus
                    $extraPath = if ($DeclaredOtherSource) { 'extra.cs' } else { 'other.cs' }
                    $files[$extraPath] = @{ language = 'cs'; source = 'class Other {} //' + ('x' * $ExtraSourceBytes); mutants = @($extra) }
                }
                if ($DeclaredOtherSource) {
                    $others = @()
                    if ($DeclaredOtherStatus -ne 'None') {
                        $other = $mutant.Clone(); $other.id = '1'; $other.status = $DeclaredOtherStatus; $others = @($other)
                    }
                    $files['other.cs'] = @{ language = 'cs'; source = $DeclaredOtherSource; mutants = $others }
                }
                $selectionIndex = [Array]::IndexOf($args, '--mutate')
                if ($selectionIndex -ge 0 -and $args[$selectionIndex + 1] -eq 'subject.cs') { $files.Remove('other.cs') }
                @{ schemaVersion = '2'; thresholds = @{ high = 90; low = 70 }; files = $files } |
                    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'report.json')
                $scratch = Join-Path $Output 'disposable'; [IO.Directory]::CreateDirectory($scratch) | Out-Null
                [IO.File]::WriteAllBytes((Join-Path $scratch 'native-build.bin'), [byte[]]::new(1024))
                if ($ChangeSource) { [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'subject.cs'), '// changed') }
                exit $NativeExit
                """);
            Profile = new()
            {
                SchemaVersion = 1,
                Id = "sample",
                Command = Command("Killed"),
                CanaryCommand = Command("Killed", canary: true),
                ReportPath = "report.json",
                BreakThreshold = 90,
                SourceFiles = ["subject.cs"],
                ConfigFiles = ["native.ps1"],
                TimeoutSeconds = 15,
                MaxRunBytes = 8 * 1024 * 1024,
                MaxRetainedBytes = 32 * 1024 * 1024,
                MinFreeBytes = 0,
            };
            Save(Profile);
        }

        internal string[] Command(string status, bool changeSource = false, bool marker = false, string? extraStatus = null, int nativeExit = 0,
            int extraSourceBytes = 0, bool canary = false, string? requiredEnvironmentName = null, string? requiredEnvironmentHash = null,
            int temporaryBytes = 0, bool nativeLogs = false, bool failBeforeReport = false, bool oversizedLogs = false,
            string? declaredOtherSource = null, string? declaredOtherStatus = null)
        {
            var values = new List<string> { "pwsh", "-NoProfile", "-NonInteractive", "-File", "native.ps1", "-Output", "{output}", "-Status", status };
            if (changeSource) values.Add("-ChangeSource");
            if (marker) values.Add("-Marker");
            if (extraStatus is not null) values.AddRange(["-ExtraStatus", extraStatus]);
            if (nativeExit != 0) values.AddRange(["-NativeExit", nativeExit.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (extraSourceBytes != 0) values.AddRange(["-ExtraSourceBytes", extraSourceBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (canary) values.Add("-Canary");
            if (requiredEnvironmentName is not null) values.AddRange(["-RequiredEnvironmentName", requiredEnvironmentName, "-RequiredEnvironmentHash", requiredEnvironmentHash!]);
            if (temporaryBytes != 0) values.AddRange(["-TemporaryBytes", temporaryBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (nativeLogs) values.Add("-NativeLogs");
            if (failBeforeReport) values.Add("-FailBeforeReport");
            if (oversizedLogs) values.Add("-OversizedLogs");
            if (declaredOtherSource is not null) values.AddRange(["-DeclaredOtherSource", declaredOtherSource, "-DeclaredOtherStatus", declaredOtherStatus ?? "None"]);
            return values.ToArray();
        }
        internal void Save(MutationRunProfile profile) => Write("profile.json", JsonSerializer.Serialize(profile, Json));
        internal MutationRunResult Run(string kind = "diagnostic") => new MutationRunService().Run(new(Path, "profile.json", kind));
        internal string Full(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        internal void Write(string relative, string text)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Full(relative))!);
            File.WriteAllText(Full(relative), text);
        }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
