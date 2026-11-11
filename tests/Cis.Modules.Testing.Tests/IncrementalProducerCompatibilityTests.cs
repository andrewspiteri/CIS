using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalProducerCompatibilityTests
{
    [Fact]
    public void CompatibleProducerMetadataAndRemovedInstallationPreserveCurrentUnitReports()
    {
        using var fixture = UnitFixture();
        var service = new IncrementalTestService(fixture);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit");
        Assert.Equal(0, service.Run(options, 0, TestContext.Current.CancellationToken).ExitCode);
        var calls = fixture.Calls();
        var reports = ReportBytes(fixture);
        var state = fixture.SavedState();
        var obsolete = fixture.Full("obsolete-installation");
        Directory.CreateDirectory(obsolete);
        File.WriteAllText(Path.Combine(obsolete, "Cis.Modules.Testing.dll"), "Prior compatible producer fixture.");
        File.WriteAllText(Path.Combine(obsolete, "Cis.Abstractions.dll"), "Prior compatible helper fixture.");
        var testingHash = Digest(File.ReadAllBytes(Path.Combine(obsolete, "Cis.Modules.Testing.dll")));
        var helperHash = Digest(File.ReadAllBytes(Path.Combine(obsolete, "Cis.Abstractions.dll")));
        foreach (var pair in state.Batches.ToArray())
        {
            var unit = pair.Value.Unit!;
            // Model the recorded hashes of an older compatible producer. The
            // actual native fixture commands and accepted report bytes are kept.
            state.Batches[pair.Key] = pair.Value with
            {
                Unit = unit with
                {
                    TestingProducerSha256 = testingHash,
                    HelperProducerSha256 = helperHash,
                    Execution = unit.Execution! with { CisToolAssemblySha256 = helperHash },
                    ProducerRevalidations = [HistoricalLink(obsolete, unit, testingHash, helperHash)]
                }
            };
        }
        fixture.WriteState(state);
        File.Delete(Path.Combine(obsolete, "Cis.Modules.Testing.dll"));
        File.Delete(Path.Combine(obsolete, "Cis.Abstractions.dll"));
        Directory.Delete(obsolete);

        var plan = service.Plan(options);
        var status = service.Plan(options); // The CLI status command uses this same read-only entry point.
        var verified = service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" });
        var reused = service.Run(options, 0, TestContext.Current.CancellationToken);

        Assert.Equal(0, plan.ExitCode);
        Assert.All(plan.Batches, batch => Assert.True(batch.UnitCurrent));
        Assert.True(status.ScopeComplete);
        Assert.Equal(0, status.PendingUnitBatches);
        Assert.Equal(0, verified.ExitCode);
        Assert.Equal("unit", verified.Layer);
        Assert.DoesNotContain(verified.Inputs, input => input.Roles.Contains("producer-history", StringComparer.Ordinal));
        Assert.All(verified.Batches, batch => Assert.Equal(helperHash, batch.Executions.Single().HelperProducerSha256));
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(2, reused.ReusedUnitBatches);
        Assert.Equal(calls, fixture.Calls());
        AssertReportsUnchanged(reports);
    }

    [Fact]
    public void RepeatedCompatibilityRechecksStayCompactWithoutAnInstalledOldProducer()
    {
        using var fixture = UnitFixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var calls = fixture.Calls();
        var reports = ReportBytes(fixture);
        var service = new IncrementalTestService(fixture);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit");
        var originalState = fixture.SavedState();
        foreach (var pair in originalState.Batches.ToArray()) originalState.Batches[pair.Key] = pair.Value with
        { Unit = pair.Value.Unit! with { Diagnostics = ["Retain this useful native diagnostic."] } };
        fixture.WriteState(originalState);
        var removedInstallation = fixture.Full("removed-producer/Cis.Modules.Testing.dll");
        for (var transition = 0; transition < 12; transition++)
        {
            var result = service.Revalidate(options, removedInstallation, "Reviewed compatible transition " + transition);
            Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
            Assert.Equal(2, result.ReusedUnitBatches);
            Assert.Equal(0, result.ExecutedUnitBatches);
        }

        Assert.All(fixture.SavedState().Batches.Values, batch =>
        {
            Assert.Equal(CisIncrementalResultContract.Version, batch.Unit!.AssessmentContractVersion);
            Assert.Empty(batch.Unit.ProducerRevalidations);
            Assert.Contains("Retain this useful native diagnostic.", batch.Unit.Diagnostics);
            Assert.Single(batch.Unit.Diagnostics, message => message.StartsWith(
                "Completed native report rechecked under result-assessment contract ", StringComparison.Ordinal));
        });
        Assert.Equal(0, service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" }).ExitCode);
        Assert.Equal(calls, fixture.Calls());
        AssertReportsUnchanged(reports);
    }

    [Theory]
    [InlineData("helper-metadata")]
    [InlineData("native-command")]
    [InlineData("contract")]
    [InlineData("missing-report")]
    public void StatusReuseAndVerificationRejectTheSameDamagedAcceptedStage(string damage)
    {
        using var fixture = UnitFixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var calls = fixture.Calls();
        var state = fixture.SavedState();
        var key = state.Batches.Keys.Order(StringComparer.Ordinal).First();
        var batch = state.Batches[key];
        var original = batch.Unit!;
        var otherReport = state.Batches.Single(pair => pair.Key != key).Value.Unit!;
        var otherBytes = File.ReadAllBytes(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + otherReport.ReportPath));
        state.Batches[key] = batch with
        {
            Unit = damage switch
            {
                "helper-metadata" => original with { HelperProducerSha256 = new('a', 64) },
                "native-command" => original with { Execution = original.Execution! with { Command = [.. original.Execution!.Command!, "--changed"] } },
                "contract" => original with { AssessmentContractVersion = CisIncrementalResultContract.Version + 1 },
                _ => original
            }
        };
        fixture.WriteState(state);
        if (damage == "missing-report") File.Delete(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + original.ReportPath));
        var service = new IncrementalTestService(fixture);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit");

        Assert.Equal(1, service.Plan(options).Batches.Count(candidate => !candidate.UnitCurrent));
        Assert.Equal(1, service.Plan(options).PendingUnitBatches);
        Assert.Equal(4, service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" }).ExitCode);
        var retry = service.Run(options, 0, TestContext.Current.CancellationToken);

        Assert.Equal(0, retry.ExitCode);
        Assert.Equal(1, retry.ExecutedUnitBatches);
        Assert.Equal(1, retry.ReusedUnitBatches);
        Assert.Equal(calls.Length + 1, fixture.Calls().Length);
        Assert.Equal(otherBytes, File.ReadAllBytes(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + otherReport.ReportPath)));
        Assert.Equal(0, service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" }).ExitCode);
    }

    [Fact]
    public void AnUnprovableLegacyFingerprintCannotBeTranscribedIntoAnAcceptedContract()
    {
        using var fixture = UnitFixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var calls = fixture.Calls();
        var state = fixture.SavedState();
        foreach (var pair in state.Batches.ToArray()) state.Batches[pair.Key] = pair.Value with
        {
            Unit = pair.Value.Unit! with
            {
                AssessmentContractVersion = 0,
                Fingerprint = new('0', 64),
                TestingProducerSha256 = null,
                HelperProducerSha256 = null
            }
        };
        fixture.WriteState(state);
        var service = new IncrementalTestService(fixture);

        var result = service.Revalidate(new(fixture.Path, "profile.json", "unit"), "Recheck only provable retained material inputs.");

        Assert.Equal(0, result.ExecutedUnitBatches);
        Assert.Equal(0, result.ReusedUnitBatches);
        Assert.Equal(2, result.PendingUnitBatches);
        Assert.All(fixture.SavedState().Batches.Values, batch => Assert.Equal(0, batch.Unit!.AssessmentContractVersion));
        Assert.Equal(4, service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" }).ExitCode);
        Assert.Equal(calls, fixture.Calls());
    }

    [Fact]
    public void LegacyDigestHistoryMigratesAfterItsOldInstallationIsRemovedWithoutAnEightTransitionRefresh()
    {
        using var fixture = UnitFixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var calls = fixture.Calls();
        var reports = ReportBytes(fixture);
        var state = fixture.SavedState();
        var service = new IncrementalTestService(fixture);
        var helper = Digest(File.ReadAllBytes(typeof(CisProcessJob).Assembly.Location));
        var producer = Digest(File.ReadAllBytes(typeof(IncrementalTestService).Assembly.Location));
        var environment = LegacyEnvironment(fixture.Profile);
        var obsolete = fixture.Full("obsolete-legacy-installation");
        Directory.CreateDirectory(obsolete);
        File.Copy(typeof(IncrementalTestService).Assembly.Location, Path.Combine(obsolete, "Cis.Modules.Testing.dll"));
        File.Copy(typeof(CisProcessJob).Assembly.Location, Path.Combine(obsolete, "Cis.Abstractions.dll"));
        foreach (var pair in state.Batches.ToArray())
        {
            var stage = pair.Value.Unit!;
            var testing = producer;
            var fingerprint = LegacyBatches(fixture, service, testing, helper, environment)[pair.Key].UnitFingerprint;
            var history = new List<IncrementalProducerRevalidation>();
            for (var transition = 0; transition < 12; transition++)
            {
                var next = Digest(Encoding.UTF8.GetBytes("compatible-testing-producer-" + transition));
                var nextFingerprint = LegacyBatches(fixture, service, next, helper, environment)[pair.Key].UnitFingerprint;
                history.Add(new(Path.Combine(obsolete, "Cis.Modules.Testing.dll"), Path.Combine(obsolete, "Cis.Abstractions.dll"),
                    testing, helper, next, helper, fingerprint, nextFingerprint, stage.ReportSha256,
                    environment, environment, false, "Reviewed unchanged native assessment.", stage.FinishedUtc));
                testing = next;
                fingerprint = nextFingerprint;
            }
            state.Batches[pair.Key] = pair.Value with
            {
                Unit = stage with
                {
                    AssessmentContractVersion = 0,
                    Fingerprint = fingerprint,
                    TestingProducerSha256 = null,
                    HelperProducerSha256 = null,
                    ProducerRevalidations = history.ToArray()
                }
            };
        }
        fixture.WriteState(state);
        File.Delete(Path.Combine(obsolete, "Cis.Modules.Testing.dll"));
        File.Delete(Path.Combine(obsolete, "Cis.Abstractions.dll"));
        Directory.Delete(obsolete);

        var result = service.Revalidate(new(fixture.Path, "profile.json", "unit"), "Recheck retained digest-linked legacy inputs.");

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(2, result.ReusedUnitBatches);
        Assert.Equal(0, result.ExecutedUnitBatches);
        Assert.All(fixture.SavedState().Batches.Values, batch =>
        {
            Assert.Equal(CisIncrementalResultContract.Version, batch.Unit!.AssessmentContractVersion);
            Assert.Empty(batch.Unit.ProducerRevalidations);
        });
        Assert.Equal(0, service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" }).ExitCode);
        var reused = service.Run(new(fixture.Path, "profile.json", "unit"), 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, reused.ExitCode);
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(calls, fixture.Calls());
        AssertReportsUnchanged(reports);
    }

    private static IncrementalTestServiceTests.Fixture UnitFixture()
    {
        var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, RecoveryCommand = null });
        return fixture;
    }

    private static IncrementalProducerRevalidation HistoricalLink(string directory, IncrementalStageResult stage,
        string testing, string helper)
        => new(Path.Combine(directory, "Cis.Modules.Testing.dll"), Path.Combine(directory, "Cis.Abstractions.dll"),
            testing, helper, testing, helper, stage.Fingerprint, stage.Fingerprint, stage.ReportSha256,
            new('b', 64), new('b', 64), false, "Prior compatible producer metadata.", stage.FinishedUtc);

    // Use the existing legacy identity implementation to seed historical fixtures;
    // the behavior under test uses only public Revalidate/Plan/Verify/Run methods.
    private static IReadOnlyDictionary<string, IncrementalTestBatch> LegacyBatches(IncrementalTestServiceTests.Fixture fixture,
        IncrementalTestService service, string testing, string helper, string environment)
    {
        var type = typeof(IncrementalTestService);
        var input = type.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [new IncrementalTestOptions(fixture.Path, "profile.json", "unit"), testing, helper, false, environment]);
        var selected = type.GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service,
            [input, new IncrementalTestState(), false])!;
        return ((IncrementalTestBatch[])selected.GetType().GetProperty("Batches")!.GetValue(selected)!)
            .ToDictionary(batch => batch.Id, StringComparer.Ordinal);
    }

    private static string LegacyEnvironment(IncrementalTestProfile profile)
        => (string)typeof(IncrementalTestService).GetMethod("AmbientEnvironmentIdentity", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [profile, false, false])!;

    private static Dictionary<string, byte[]> ReportBytes(IncrementalTestServiceTests.Fixture fixture)
        => fixture.SavedState().Batches.Values.ToDictionary(batch => fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + batch.Unit!.ReportPath),
            batch => File.ReadAllBytes(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + batch.Unit!.ReportPath)), StringComparer.Ordinal);

    private static void AssertReportsUnchanged(Dictionary<string, byte[]> reports)
    { foreach (var report in reports) Assert.Equal(report.Value, File.ReadAllBytes(report.Key)); }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
