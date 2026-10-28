using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRecoveryResumeTests
{
    [Fact]
    public void HealthyRecoveryRetryReusesTheExactCompletedNativeReport()
    {
        using var fixture = FailedRecovery();
        var before = Stage(fixture);
        var report = Report(fixture, before);
        var bytes = File.ReadAllBytes(report);
        var calls = fixture.Calls();
        File.Delete(fixture.Full(".cis/recovery-failure"));

        var result = fixture.Run();

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Equal(0, result.ExecutedUnitBatches);
        Assert.Equal(1, result.ReusedMutationBatches);
        Assert.Equal(calls.Length + 2, fixture.Calls().Length);
        Assert.StartsWith("recovery ", fixture.Calls()[calls.Length], StringComparison.Ordinal);
        Assert.StartsWith("unit ", fixture.Calls()[calls.Length + 1], StringComparison.Ordinal);
        var after = Stage(fixture);
        Assert.True(after.Completed);
        Assert.False(fixture.SavedState().RecoveryRequired);
        Assert.Equal(before.ReportSha256, after.ReportSha256);
        Assert.Equal(bytes, File.ReadAllBytes(report));
        Assert.Equal(JsonSerializer.Serialize(before.Execution), JsonSerializer.Serialize(after.Execution));
        Assert.Equal(before.FinishedUtc, after.FinishedUtc);
        Assert.Equal(0, after.RecoveryExecution!.ExitCode);
        Assert.Equal(1, after.PostflightPassed);
        var history = JsonNode.Parse(File.ReadAllText(History(fixture)))!;
        var retained = Assert.Single(history["attempts"]!.AsArray())!["attempt"]!;
        Assert.Equal(1, retained["recoveryExecution"]!["exitCode"]!.GetValue<int>());
        Assert.Equal(before.ReportSha256, retained["reportSha256"]!.GetValue<string>());
    }

    [Fact]
    public void AnotherFailedRecoveryKeepsTheNativeReportAndItsOriginalFailureHeld()
    {
        using var fixture = FailedRecovery();
        var before = Stage(fixture);
        var report = File.ReadAllBytes(Report(fixture, before));
        Assert.Equal(4, fixture.Run().ExitCode);
        Assert.Single(fixture.Calls(), call => call.StartsWith("mutation ", StringComparison.Ordinal));
        Assert.True(fixture.SavedState().RecoveryRequired);
        Assert.False(Stage(fixture).Completed);
        Assert.Equal(before.ReportSha256, Stage(fixture).ReportSha256);
        Assert.Equal(report, File.ReadAllBytes(Report(fixture, before)));
        Assert.Single(JsonNode.Parse(File.ReadAllText(History(fixture)))!["attempts"]!.AsArray());
    }

    [Fact]
    public void TheRecoveryHistoryBoundHoldsBeforeAnotherNativeCommand()
    {
        using var fixture = FailedRecovery();
        Assert.Equal(4, fixture.Run().ExitCode);
        var history = JsonNode.Parse(File.ReadAllText(History(fixture)))!;
        var attempts = history["attempts"]!.AsArray();
        var attempt = Assert.Single(attempts)!.DeepClone();
        while (attempts.Count < 8) attempts.Add(attempt.DeepClone());
        File.WriteAllText(History(fixture), history.ToJsonString());
        File.Delete(fixture.Full(".cis/recovery-failure"));
        var calls = fixture.Calls();
        var result = fixture.Run();
        Assert.Equal(4, result.ExitCode);
        Assert.Equal(calls, fixture.Calls());
        Assert.True(fixture.SavedState().RecoveryRequired);
        Assert.False(Stage(fixture).Completed);
        Assert.Contains(result.Diagnostics, item => item.Contains("eight-attempt bound", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("test")]
    [InlineData("configuration")]
    [InlineData("report")]
    [InlineData("known-input-change")]
    [InlineData("helper")]
    public void ChangedOrUnverifiableEvidenceCannotAcquireCompletionFromRecovery(string fault)
    {
        using var fixture = FailedRecovery();
        switch (fault)
        {
            case "source": fixture.Write("src/A.cs", "public static class A { public static bool Value => false; }"); break;
            case "test": fixture.Write("tests/ATests.cs", "class ChangedTest {}"); break;
            case "configuration": fixture.Write("config.json", "{\"changed\":true}"); break;
            case "report": File.AppendAllText(Report(fixture, Stage(fixture)), " "); break;
            case "known-input-change": ChangeStage(fixture, stage => stage with { Diagnostics = [.. stage.Diagnostics, "Relevant inputs changed during execution; this result cannot be reused."] }); break;
            case "helper": ChangeStage(fixture, stage => stage with { Execution = stage.Execution! with { CisToolAssemblySha256 = new('a', 64) } }); break;
        }
        File.Delete(fixture.Full(".cis/recovery-failure"));
        var calls = fixture.Calls().Count(call => call.StartsWith("mutation ", StringComparison.Ordinal));
        var result = fixture.Run("unit");
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Equal(calls, fixture.Calls().Count(call => call.StartsWith("mutation ", StringComparison.Ordinal)));
        Assert.Null(result.CompleteScopeScore);
        Assert.False(fixture.SavedState().Batches.Values.Single().Mutation?.Completed ?? false);
    }

    [Fact]
    public void AnInvocationTimeoutCannotBecomeACompletedNativeResultAfterRecovery()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { SourceFiles = ["src/A.cs"], TimeoutSeconds = 1 });
        fixture.Fault("src/A.cs", "mutation:timeout");
        fixture.Write(".cis/recovery-failure", "fail only recovery");
        Assert.Equal(4, fixture.Run().ExitCode);
        Assert.Equal("timeout", Stage(fixture).Execution!.Status);
        File.Delete(fixture.Full(".cis/recovery-failure"));
        var result = fixture.Run("unit");
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Null(result.CompleteScopeScore);
        Assert.False(Stage(fixture).Completed);
        Assert.False(File.Exists(History(fixture)));
    }

    [Fact]
    public void ExplicitMutantTimeoutRemainsAConservativeMissAfterHealthyRecovery()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        fixture.Fault("src/B.cs", "mutation:reported-timeout");
        fixture.Write(".cis/recovery-failure", "fail only recovery");
        Assert.Equal(4, fixture.Run().ExitCode);
        File.Delete(fixture.Full(".cis/recovery-failure"));
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Equal(1, result.Killed);
        Assert.Equal(1, result.TimedOut);
        Assert.Equal(2, result.KnownDenominator);
        Assert.Equal(50, result.CompleteScopeScore);
        Assert.False(result.ThresholdMet);
        Assert.False(result.Qualified);
    }

    [Fact]
    public void ReviewedPreviousProducerMigrationLeavesNativePendingUntilRecoveryActuallyPasses()
    {
        using var fixture = FailedRecovery();
        using var producer = new PreviousProducer(fixture);
        var before = Stage(fixture);
        var calls = fixture.Calls();
        var service = new IncrementalTestService(fixture);
        var revalidated = service.Revalidate(new(fixture.Path, "profile.json"), producer.Assembly,
            "Reviewed compatible recovery-resume correction; native commands and assessment remain unchanged.");
        Assert.True(revalidated.ExitCode == 0, string.Join("; ", revalidated.Diagnostics));
        Assert.Equal(0, revalidated.ReusedMutationBatches);
        Assert.True(fixture.SavedState().RecoveryRequired);
        Assert.False(Stage(fixture).Completed);
        Assert.Single(Stage(fixture).ProducerRevalidations);
        Assert.Equal(calls, fixture.Calls());
        File.Delete(fixture.Full(".cis/recovery-failure"));
        var resumed = fixture.Run();
        Assert.True(resumed.ExitCode == 0, string.Join("; ", resumed.Diagnostics));
        Assert.Equal(0, resumed.ExecutedMutationBatches);
        Assert.True(Stage(fixture).Completed);
        Assert.Equal(before.ReportSha256, Stage(fixture).ReportSha256);
        Assert.Single(fixture.Calls(), call => call.StartsWith("mutation ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("helper")]
    [InlineData("report")]
    [InlineData("timeout")]
    [InlineData("fingerprint")]
    public void PreviousProducerReviewCannotAuthorizeChangedOrInvalidPendingNativeEvidence(string fault)
    {
        using var fixture = FailedRecovery();
        using var producer = new PreviousProducer(fixture);
        switch (fault)
        {
            case "source": fixture.Write("src/A.cs", "class Changed {}"); break;
            case "helper": ChangeStage(fixture, stage => stage with { Execution = stage.Execution! with { CisToolAssemblySha256 = new('a', 64) } }); break;
            case "report": ChangeStage(fixture, stage => stage with { ReportSha256 = new('a', 64) }); break;
            case "timeout": ChangeStage(fixture, stage => stage with { Execution = stage.Execution! with { Status = "timeout", ExitCode = null } }); break;
            case "fingerprint": ChangeStage(fixture, stage => stage with { Fingerprint = new('a', 64) }); break;
        }
        var calls = fixture.Calls();
        var result = new IncrementalTestService(fixture).Revalidate(new(fixture.Path, "profile.json"), producer.Assembly, "Reviewed compatible producer.");
        Assert.Equal(4, result.ExitCode);
        Assert.Equal(0, result.ReusedMutationBatches);
        Assert.True(fixture.SavedState().RecoveryRequired);
        Assert.False(Stage(fixture).Completed);
        Assert.Empty(Stage(fixture).ProducerRevalidations);
        Assert.Equal(calls, fixture.Calls());
    }

    private static IncrementalTestServiceTests.Fixture FailedRecovery()
    {
        var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { SourceFiles = ["src/A.cs"] });
        fixture.Write(".cis/recovery-failure", "fail only recovery");
        Assert.Equal(4, fixture.Run().ExitCode);
        Assert.True(fixture.SavedState().RecoveryRequired);
        Assert.False(Stage(fixture).Completed);
        Assert.Equal(1, Stage(fixture).Mutation!.Killed);
        return fixture;
    }

    private static IncrementalStageResult Stage(IncrementalTestServiceTests.Fixture fixture) => fixture.SavedState().Batches.Values.Single().Mutation!;
    private static string Report(IncrementalTestServiceTests.Fixture fixture, IncrementalStageResult stage) => fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + stage.ReportPath);
    private static string History(IncrementalTestServiceTests.Fixture fixture) => fixture.Full(IncrementalTestService.LocalRoot + "/sample/recovery-history-" + fixture.SavedState().Batches.Keys.Single() + ".json");
    private static void ChangeStage(IncrementalTestServiceTests.Fixture fixture, Func<IncrementalStageResult, IncrementalStageResult> change)
    {
        var state = fixture.SavedState();
        var key = state.Batches.Keys.Single();
        state.Batches[key] = state.Batches[key] with { Mutation = change(state.Batches[key].Mutation!) };
        fixture.WriteState(state);
    }

    private sealed class PreviousProducer : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("cis-recovery-previous-producer-").FullName;
        internal string Assembly => Path.Combine(_directory, "Cis.Modules.Testing.dll");
        internal PreviousProducer(IncrementalTestServiceTests.Fixture fixture)
        {
            File.Copy(typeof(IncrementalTestService).Assembly.Location, Assembly);
            File.AppendAllText(Assembly, "previous compatible fixture producer; never loaded");
            var helper = Path.Combine(_directory, "Cis.Abstractions.dll");
            File.Copy(typeof(CisProcessJob).Assembly.Location, helper);
            var service = new IncrementalTestService(fixture);
            var read = typeof(IncrementalTestService).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!;
            var select = typeof(IncrementalTestService).GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var input = read.Invoke(null, [new IncrementalTestOptions(fixture.Path, "profile.json"), Hash(Assembly), Hash(helper), false, null]);
            var state = fixture.SavedState();
            var selection = select.Invoke(service, [input, state, false])!;
            var batch = Assert.Single((IncrementalTestBatch[])selection.GetType().GetProperty("Batches")!.GetValue(selection)!);
            var saved = state.Batches[batch.Id];
            state.Batches[batch.Id] = saved with
            {
                Unit = saved.Unit! with { Fingerprint = batch.UnitFingerprint },
                Mutation = saved.Mutation! with { Fingerprint = batch.MutationFingerprint },
            };
            fixture.WriteState(state);
        }
        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
