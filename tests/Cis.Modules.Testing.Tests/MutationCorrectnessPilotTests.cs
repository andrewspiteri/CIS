using System.Runtime.InteropServices;
using System.Text.Json;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class MutationRunServiceTests
{
    [Fact]
    public void MissingPilotAuthorityProvenanceRequestsARerunRatherThanSettlementReconciliation()
    {
        using var fixture = new Target();
        fixture.Save(PilotProfile(fixture, true));
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path, required: false);
        var service = new MutationRunService(authority, authority);
        var first = service.Run(new(fixture.Path, "profile.json", "canary"), TestContext.Current.CancellationToken);
        var original = Assert.IsType<NativeCorrectnessPilotResult>(first.Pilot);
        Assert.True(original.Ready, JsonSerializer.Serialize(first, Target.Json));
        Assert.NotNull(original.WorkReference);
        var launches = authority.Launched;
        File.WriteAllText(fixture.Full(original.SummaryPath!),
            JsonSerializer.Serialize(original with { AuthorityPath = null }, Target.Json));

        var invalid = MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json", workController: authority);

        Assert.Equal("pilot-required", invalid.Status); Assert.False(invalid.Ready); Assert.False(invalid.NativeReady);
        Assert.Equal(original.Observations.Select(item => (item.Role, item.ReportSha256)),
            invalid.Observations.Select(item => (item.Role, item.ReportSha256))); Assert.True(invalid.Restored);
        var repeated = service.Run(new(fixture.Path, "profile.json", "canary"), TestContext.Current.CancellationToken);
        var repaired = Assert.IsType<NativeCorrectnessPilotResult>(repeated.Pilot);
        Assert.True(repaired.Ready, JsonSerializer.Serialize(repeated, Target.Json));
        Assert.Equal(5, repaired.Observations.Count); Assert.True(repaired.Restored);
        Assert.NotEqual(original.WorkReference, repaired.WorkReference); Assert.True(authority.Launched > launches);
        Assert.True(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json", workController: authority).Ready);
    }

    // Controlled protocol faults validate admission. The maintained real MSTest /
    // Stryker fixture separately establishes genuine mutant activation and survival.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorrectnessPilotRequiresTheNativeSurvivorControlAndHealthyRestoration(bool survivor)
    {
        using var fixture = new Target();
        var profile = PilotProfile(fixture, survivor);
        fixture.Save(profile);
        var result = fixture.Run("canary");
        Assert.True(result.ExitCode == (survivor ? 0 : 4) && result.Pilot is not null,
            JsonSerializer.Serialize(result, Target.Json));
        Assert.NotNull(result.Pilot);
        Assert.Equal(survivor, result.Pilot.Ready);
        Assert.False(result.Qualified);
        Assert.False(result.Pilot.RecoveryRequired);
        Assert.Contains(result.Pilot.Observations, observation => observation.Role == "restoration" && observation.Execution.ExitCode == 0);
        Assert.Contains(result.Pilot.Observations, observation => observation.Role == "postflight" && observation.ReportSha256 is not null);
        if (!survivor)
        {
            Assert.Contains(result.Pilot.Diagnostics, message => message.Contains("all-killed adapter", StringComparison.Ordinal));
            Assert.False(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json").Ready);
            return;
        }
        var measured = result.Pilot.Observations.Single(observation => observation.Role == "mutation");
        var suite = new TestSuiteProfile("pilot", "pilot", "mutation", "native", "run", ".", "stryker-json",
            measured.ReportPath!, "-", "-", "-", "always", "local", "current-result");
        var native = new StrykerJsonResultAdapter().Read(new(fixture.Path, suite, fixture.Full(measured.ReportPath!), null, null, 90));
        Assert.True(native.Mutation!.ValidEvidence); Assert.False(native.Mutation.ThresholdMet); Assert.False(native.Mutation.Qualified);
        Assert.Equal(1, native.Mutation.Survived); Assert.Equal(1, native.Mutation.Killed);

        var current = MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json");
        Assert.True(current.Ready); Assert.False(current.ControllerAssessed);
        Assert.Equal(result.Pilot.BasisSha256, current.BasisSha256);
        var reference = new CisAgentWorkReference("adoption", "authority", "CIS-0001", "WORK-010", "WORK-010", "next-native", "attempt", null);
        var governed = MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json",
            new(true, "admitted", "governed-native", reference, null, []));
        Assert.False(governed.Ready);
        Assert.Contains("Unaccounted pilot observations", governed.NextAction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--test-projects")]
    public void DifferentNativeProjectOverridesCannotBorrowADeclaredPilot(string selector)
    {
        using var fixture = new Target();
        var representative = fixture.Profile with { Command = [.. fixture.Profile.Command, selector, "representative.csproj"] };
        fixture.Write("representative.json", JsonSerializer.Serialize(representative, Target.Json));
        fixture.Save(fixture.Profile with
        {
            Command = [.. fixture.Command("Killed", marker: true), selector, "different.csproj"],
            CorrectnessPilotProfile = "representative.json",
        });
        var result = fixture.Run();
        Assert.Equal(4, result.ExitCode);
        Assert.Equal("pilot-path-mismatch", result.Pilot!.Status);
        Assert.Contains("pinned native configuration", result.Pilot.NextAction, StringComparison.Ordinal);
        Assert.Null(result.NativeExecution);
        Assert.False(File.Exists(fixture.Full("broad-started")));
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void UnrelatedAmbientChangePreservesExactRecoveryRecipeWithoutGrantingPilotReadiness()
    {
        var name = "CIS_PILOT_UNRELATED_INVOCATION_" + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            using var fixture = new Target();
            fixture.Save(PilotProfile(fixture, true) with { CanaryCommand = fixture.Command("Killed", marker: true) });
            fixture.Write(".cis/instrumented", "controlled unhealthy native baseline");
            var failed = fixture.Run("canary");
            Assert.True(failed.ExitCode == 4 && failed.Pilot?.RecoveryRequired == true,
                JsonSerializer.Serialize(failed, Target.Json));
            File.Delete(fixture.Full("broad-started"));
            Environment.SetEnvironmentVariable(name, "changed unrelated invocation value");
            var restored = fixture.Run("canary");
            Assert.True(restored.ExitCode == 0 && restored.Pilot?.Restored == true,
                JsonSerializer.Serialize(restored, Target.Json));
            Assert.Equal(failed.Pilot!.RecoveryBasis, restored.Pilot!.RecoveryBasis);
            Assert.NotEqual(failed.Pilot.BasisSha256, restored.Pilot.BasisSha256);
            Assert.False(restored.Pilot.Ready);
            Assert.False(restored.Qualified);
            Assert.False(File.Exists(fixture.Full("broad-started")));
            Assert.False(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json").Ready);
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }

    [Fact]
    public void ChangedRestorationCommandCannotReplaceTheRetainedPilotRecipe()
    {
        using var fixture = new Target();
        var profile = PilotProfile(fixture, true);
        fixture.Save(profile);
        fixture.Write(".cis/instrumented", "controlled unhealthy native baseline");
        var failed = fixture.Run("canary");
        Assert.True(failed.Pilot?.RecoveryRequired == true, JsonSerializer.Serialize(failed, Target.Json));
        var retained = File.ReadAllBytes(fixture.Full(failed.Pilot!.SummaryPath!));
        var recipe = profile.CorrectnessPilot!;
        fixture.Save(profile with
        {
            CorrectnessPilot = recipe with
            { RestorationCommand = [.. recipe.RestorationCommand, "changed-recipe"] }
        });
        var held = fixture.Run("canary");
        Assert.Equal("held", held.Status);
        Assert.Contains(held.Diagnostics, message => message.Contains("recovery recipe changed", StringComparison.Ordinal));
        Assert.Null(held.NativeExecution);
        Assert.Equal(retained, File.ReadAllBytes(fixture.Full(failed.Pilot.SummaryPath!)));
    }

    [Fact]
    public void AFalseLocalRecoveryFlagCannotMigrateAnOpaqueOriginalRecipe()
    {
        using var fixture = new Target();
        fixture.Save(PilotProfile(fixture, true));
        fixture.Write(".cis/instrumented", "controlled unhealthy native baseline");
        var failed = fixture.Run("canary");
        Assert.True(failed.Pilot?.RecoveryRequired == true, JsonSerializer.Serialize(failed, Target.Json));
        // A controlled negative metadata fault supplies no durable native authority.
        var reference = new CisAgentWorkReference("fixture", "authority", "CIS-0001", "WORK-090", "WORK-090", "retained-native", "attempt", null);
        var path = fixture.Full(failed.Pilot!.SummaryPath!);
        File.WriteAllText(path, JsonSerializer.Serialize(failed.Pilot with
        { RecoveryRequired = false, Restored = false, WorkReference = reference, RecoveryBasis = "native-pilot-recovery:opaque-legacy" }, Target.Json));
        var controller = new DeniedNativeController();
        var held = new MutationRunService(controller).Run(new(fixture.Path, "profile.json", "canary")
        { Work = new() { RecoveryOfExecutionId = reference.ExecutionId } }, TestContext.Current.CancellationToken);
        Assert.Equal("held", held.Status);
        Assert.Contains(held.Diagnostics, message => message.Contains("legacy opaque environment basis", StringComparison.Ordinal));
        Assert.Empty(controller.Requests);
        Assert.Null(held.NativeExecution);
    }

    [Fact]
    public void AnUnrelatedFailingExecutableCannotSupplyThePilotInfrastructureControl()
    {
        using var fixture = new Target();
        var profile = PilotProfile(fixture, true);
        var pilot = profile.CorrectnessPilot!;
        fixture.Save(profile with
        {
            CorrectnessPilot = pilot with
            { InfrastructureCommand = [pilot.BaselineCommand[0], "--config-file", "missing-pilot-native.json", "--output", "{output}"] },
        });
        var result = fixture.Run("canary");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Diagnostics, message => message.Contains("same actual native mutation runner", StringComparison.Ordinal));
        Assert.Null(result.NativeExecution);
        Assert.False(Directory.Exists(fixture.Full(MutationRunService.LocalRoot)));
    }

    [Fact]
    public void CorrectnessPilotCannotReplaceItsFullDeclaredControlWithACanaryPlaceholder()
    {
        using var fixture = new Target();
        fixture.Save(PilotProfile(fixture, true, controlPlaceholder: true));
        var result = fixture.Run("canary");
        Assert.True(result.ExitCode == 4 && result.Pilot is not null, JsonSerializer.Serialize(result, Target.Json));
        Assert.False(result.Pilot!.Ready);
        Assert.True(result.Pilot.Restored);
        Assert.Contains(result.Pilot.Diagnostics, message => message.Contains("source scope", StringComparison.Ordinal));
        Assert.False(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json").Ready);
    }

    [Fact]
    public void PilotReadinessCannotBeSuppliedByABooleanOrTamperedNativeReport()
    {
        using var fixture = new Target(); fixture.Save(PilotProfile(fixture, true));
        var result = fixture.Run("canary");
        Assert.True(result.ExitCode == 0 && result.Pilot is not null, JsonSerializer.Serialize(result, Target.Json));
        var summary = fixture.Full(result.Pilot!.SummaryPath!);
        var bytes = File.ReadAllBytes(summary);
        var noObservations = result.Pilot with { Ready = true, Observations = [] };
        File.WriteAllText(summary, JsonSerializer.Serialize(noObservations, Target.Json));
        Assert.False(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json").Ready);
        File.WriteAllBytes(summary, bytes);
        var mutation = result.Pilot.Observations.Single(item => item.Role == "mutation");
        File.AppendAllText(fixture.Full(mutation.ReportPath!), " ");
        Assert.False(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json").Ready);
    }

    private static MutationRunProfile PrepareQualification(Target fixture)
    {
        // Exercise the existing controlled runner first. This is synthetic protocol
        // evidence for consumer tests, never a substitute for the real native pilot.
        var representative = PilotProfile(fixture, true) with { Id = "representative" };
        fixture.Write("subject.csproj", "<Project />");
        fixture.Write("tests.csproj", "<Project />");
        fixture.Write("pilot-native.json", "{\"stryker-config\":{\"project\":\"subject.csproj\",\"test-projects\":[\"tests.csproj\"],\"mutate\":[\"subject.cs\",\"other.cs\"]}}");
        var command = representative.Command.Concat(["--mutate", "subject.cs,other.cs"]).ToArray();
        var infrastructure = representative.CorrectnessPilot!.InfrastructureCommand.Concat(["--mutate", "subject.cs,other.cs"]).ToArray();
        representative = representative with
        {
            Command = command,
            ConfigFiles = [.. representative.ConfigFiles, "subject.csproj", "tests.csproj"],
            CorrectnessPilot = representative.CorrectnessPilot with { InfrastructureCommand = infrastructure },
        };
        fixture.Write("representative.json", JsonSerializer.Serialize(representative, Target.Json));
        var observed = new MutationRunService().Run(new(fixture.Path, "representative.json", "canary"), TestContext.Current.CancellationToken);
        Assert.True(observed.ExitCode == 0 && observed.Pilot?.Ready == true, JsonSerializer.Serialize(observed, Target.Json));
        command = command.ToArray(); command[^1] = "subject.cs";
        var target = representative with
        {
            Id = "sample",
            SourceFiles = ["subject.cs"],
            Command = command,
            CorrectnessPilot = null,
            CorrectnessPilotProfile = "representative.json",
        };
        fixture.Save(target);
        return target;
    }

    private static MutationRunProfile PilotProfile(Target fixture, bool survivor, bool controlPlaceholder = false, bool marker = false)
    {
        fixture.Write("other.cs", "class Other { bool Control = true; }");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (dotnet is null || !File.Exists(dotnet))
            dotnet = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var nativeFixture = Path.Combine(AppContext.BaseDirectory, "native-fixture", "Cis.Testing.NativeFixture.dll");
        var shell = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory.Trim('"'), "pwsh" + (OperatingSystem.IsWindows() ? ".exe" : "")))
            .First(File.Exists);
        var baseline = new[] { dotnet, nativeFixture, "unit", "{output}", "subject.cs" };
        fixture.Write("pilot-native.json", "{}");
        var mutation = fixture.Command("Killed", marker: marker,
            declaredOtherSource: controlPlaceholder ? "File ignored by mutate filter" : "class Other { bool Control = true; }",
            declaredOtherStatus: controlPlaceholder ? "None" : survivor ? "Survived" : "Killed")
            .Concat(["--config-file", "pilot-native.json"]).ToArray();
        var infrastructure = mutation.ToArray(); infrastructure[^1] = "missing-pilot-native.json";
        return fixture.Profile with
        {
            SourceFiles = ["subject.cs", "other.cs"],
            ConfigFiles = [.. fixture.Profile.ConfigFiles, "pilot-native.json"],
            Command = mutation,
            CorrectnessPilot = new()
            {
                BaselineCommand = baseline,
                ControlSourceFile = "other.cs",
                BaselineReportPath = "unit.trx",
                InfrastructureCommand = infrastructure,
                MissingInfrastructurePath = "missing-pilot-native.json",
                RestorationCommand = [dotnet, nativeFixture, "recovery", "{output}", "subject.cs"],
                RecoveryTimeoutSeconds = 10,
                ToolFiles = [dotnet, nativeFixture, shell],
            }
        };
    }
}
