using System.Text.Json;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class MutationRunServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PilotRootSettlementFailureRetainsNativeControlsUntilReadOnlyReconciliation(bool child)
    {
        using var fixture = new Target();
        fixture.Save(PilotProfile(fixture, true));
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path, required: false)
        { FailFinish = (operation, parent) => child ? operation == "pilot-mutation" : parent is null };

        var result = new MutationRunService(authority, authority).Run(new(fixture.Path, "profile.json", "canary"),
            TestContext.Current.CancellationToken);

        Assert.Equal("settlement-required", result.Status); Assert.Equal(4, result.ExitCode);
        Assert.Equal("canary-passed", result.NativeOutcomeStatus); Assert.Equal(0, result.NativeOutcomeExitCode);
        var pilot = Assert.IsType<NativeCorrectnessPilotResult>(result.Pilot);
        Assert.False(pilot.Ready); Assert.True(pilot.NativeReady); Assert.True(pilot.Restored);
        Assert.False(pilot.RecoveryRequired); Assert.Equal(5, pilot.Observations.Count);
        Assert.Equal(result.WorkReference, pilot.WorkReference);
        Assert.True(authority.SettlementFailures > 0);
        Assert.All(pilot.Observations, observation => Assert.True(observation.Execution.SafelyStopped));
        var runSummary = Path.Combine(Path.GetDirectoryName(fixture.Full(pilot.Observations[0].OutputPath!))!, "summary.json");
        foreach (var path in new[] { fixture.Full(pilot.SummaryPath!), runSummary })
        {
            var saved = JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(File.ReadAllBytes(path), Target.Json)!;
            Assert.False(saved.Ready); Assert.True(saved.NativeReady); Assert.True(saved.Restored);
            Assert.False(saved.RecoveryRequired); Assert.Equal(5, saved.Observations.Count);
        }
        var reports = pilot.Observations.SelectMany(observation => observation.ReportPath is { } report
                ? new[] { report }.Concat(observation.Artifacts.Select(artifact => artifact.Path))
                : observation.Artifacts.Select(artifact => artifact.Path))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, path => File.ReadAllBytes(fixture.Full(path)));
        var launches = authority.Launched;
        var held = MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json", workController: authority);
        Assert.False(held.Ready); Assert.True(held.NativeReady); Assert.True(held.Restored);
        Assert.Equal("settlement-required", held.Status);
        Assert.Equal(5, held.Observations.Count);
        var stablePath = fixture.Full(pilot.SummaryPath!);
        var stableBytes = File.ReadAllBytes(stablePath);
        var missingRoot = JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(stableBytes, Target.Json)!
            with
        { WorkReference = null, AuthorityPath = null };
        File.WriteAllText(stablePath, JsonSerializer.Serialize(missingRoot, Target.Json));
        Assert.False(MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json", workController: authority).Ready);
        File.WriteAllBytes(stablePath, stableBytes);

        authority.Reconciled = true;
        var reused = MutationRunService.InspectCorrectnessPilot(fixture.Path, "profile.json", workController: authority);

        Assert.True(reused.Ready); Assert.True(reused.NativeReady); Assert.True(reused.Restored);
        Assert.Equal(pilot.BasisSha256, reused.BasisSha256); Assert.Equal(launches, authority.Launched);
        foreach (var (path, bytes) in reports) Assert.Equal(bytes, File.ReadAllBytes(fixture.Full(path)));
        Assert.False(JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(File.ReadAllBytes(fixture.Full(pilot.SummaryPath!)), Target.Json)!.Ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredFinalSettlementFailureCannotPublishNativeQualification(bool child)
    {
        using var fixture = new Target();
        PrepareQualification(fixture);
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path, required: false)
        { FailFinish = (operation, parent) => child ? parent is not null && operation == "mutation-qualification" : parent is null };

        var result = new MutationRunService(authority, authority).Run(new(fixture.Path, "profile.json", "qualification"),
            TestContext.Current.CancellationToken);

        Assert.Equal("settlement-required", result.Status); Assert.Equal(4, result.ExitCode); Assert.False(result.Qualified);
        Assert.Equal("qualified", result.NativeOutcomeStatus); Assert.True(result.NativeOutcomeQualified);
        Assert.True(result.Assessment!.Mutation!.ValidEvidence); Assert.True(result.Assessment.Mutation.ThresholdMet);
        Assert.Equal(1, result.Assessment.Mutation.Killed); Assert.False(result.Assessment.Mutation.Qualified);
        Assert.True(authority.SettlementFailures > 0);
        var report = File.ReadAllBytes(fixture.Full(result.ReportPath!));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(report));
        Assert.Equal(result.Assessment.Mutation.ReportSha256, hash);
        foreach (var path in new[] { result.SummaryPath!, MutationRunService.LocalRoot + "/sample/latest-run.json" })
        {
            using var saved = JsonDocument.Parse(File.ReadAllBytes(fixture.Full(path)));
            Assert.Equal("settlement-required", saved.RootElement.GetProperty("status").GetString());
            Assert.False(saved.RootElement.GetProperty("qualified").GetBoolean());
        }
        using var sidecar = JsonDocument.Parse(File.ReadAllBytes(fixture.Full(result.ReportPath!) + ".assessment.json"));
        Assert.False(sidecar.RootElement.GetProperty("mutation").GetProperty("qualified").GetBoolean());
        Assert.True(sidecar.RootElement.GetProperty("cisRun").GetProperty("nativeOutcomeQualified").GetBoolean());
        Assert.False(CisNativeWorkSettlement.Complete(authority.InspectSettlement(fixture.Path, result.WorkReference!), result.WorkReference!));
        authority.Reconciled = true;
        Assert.True(CisNativeWorkSettlement.Complete(authority.InspectSettlement(fixture.Path, result.WorkReference!), result.WorkReference!));
        Assert.Equal(report, File.ReadAllBytes(fixture.Full(result.ReportPath!)));
    }
}
