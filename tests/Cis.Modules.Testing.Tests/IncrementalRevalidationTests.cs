using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRevalidationTests
{
    [Fact]
    public void RevalidationSettlementFailureHoldsItsLatestReceiptWithoutRepeatingNativeWork()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, MaxRetainedBytes = 32 * 1024 * 1024 });
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var reports = fixture.SavedState().Batches.Values.Select(batch => batch.Unit!.ReportSha256).ToArray();
        var calls = fixture.Calls();
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path)
        { FailFinish = (operation, _) => operation == "incremental-revalidate" };
        var service = new IncrementalTestService(fixture, authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit");
        var result = service.Revalidate(options, typeof(IncrementalTestService).Assembly.Location, "Reviewed unchanged native producer.");
        Assert.Equal("settlement-required", result.Status); Assert.Equal(4, result.ExitCode);
        Assert.Equal(2, result.ReusedUnitBatches); Assert.Equal(0, authority.Launched);
        Assert.Equal("settlement-required", service.Plan(options).Status);
        Assert.Equal(calls, fixture.Calls());
        authority.Reconciled = true;
        Assert.Equal(0, service.Plan(options).ExitCode);
        Assert.All(service.Plan(options).Batches, batch => Assert.True(batch.UnitCurrent));
        Assert.Equal(reports, fixture.SavedState().Batches.Values.Select(batch => batch.Unit!.ReportSha256).ToArray());
        Assert.Equal(calls, fixture.Calls());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GovernedRevalidationCannotRewriteStateThroughAnUncomposedService(bool retainedAssociation)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        if (retainedAssociation)
            fixture.WriteState(new() { WorkAssociation = new("CIS-0001", "WORK-090", AuthorityPath: fixture.Path, Actor: "Fixture controller") });
        else fixture.Write(".cis/workspace.yml", "initialized fixture authority");
        var root = fixture.Full(IncrementalTestService.LocalRoot);
        var before = Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes)
            : new Dictionary<string, byte[]>();

        var result = Revalidate(fixture);

        Assert.Equal("held", result.Status);
        Assert.Equal("missing-work-controller", result.WorkCoverage);
        Assert.Empty(fixture.Calls());
        var after = Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));
        foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
    }

    [Fact]
    public void MetadataRevalidationCannotSpendAnOriginalNativeRestorationReserve()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var result = new IncrementalTestService(fixture).Revalidate(new(fixture.Path, "profile.json")
        { Work = new() { RecoveryOfExecutionId = "native/original" } }, typeof(IncrementalTestService).Assembly.Location, "Reviewed compatible update.");
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Metadata admission cannot use --recovery-of", StringComparison.Ordinal));
        Assert.False(Directory.Exists(fixture.Full(IncrementalTestService.LocalRoot)));
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void CompletedReportsAreRecheckedWithoutNativeExecution()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        var calls = fixture.Calls();
        var before = fixture.SavedState().Batches.Values.Single();

        var result = Revalidate(fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.ReusedUnitBatches);
        Assert.Equal(1, result.ReusedMutationBatches);
        Assert.Equal(calls, fixture.Calls());
        var after = fixture.SavedState().Batches.Values.Single();
        Assert.Equal(before.Unit!.ReportSha256, after.Unit!.ReportSha256);
        Assert.Equal(before.Mutation!.ReportSha256, after.Mutation!.ReportSha256);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before.Mutation.Execution), System.Text.Json.JsonSerializer.Serialize(after.Mutation.Execution));
        Assert.False(result.Qualified);
    }

    [Fact]
    public void ChangedInputsCannotBeRevalidatedAsCompleted()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        var calls = fixture.Calls();
        fixture.Write("src/A.cs", "public static class A { public static bool Value => false; }");
        var result = Revalidate(fixture);
        Assert.Equal(0, result.ReusedUnitBatches);
        Assert.Equal(0, result.ReusedMutationBatches);
        Assert.Equal(2, result.PendingMutationBatches);
        Assert.Equal(calls, fixture.Calls());
    }

    [Fact]
    public void FailedStageCannotAcquireCompletionFromItsReadableReport()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        var state = fixture.SavedState();
        var key = state.Batches.Keys.Single();
        var batch = state.Batches[key];
        state.Batches[key] = batch with { Mutation = batch.Mutation! with { Completed = false, Status = "failed" } };
        fixture.WriteState(state);
        var result = Revalidate(fixture);
        Assert.Equal(1, result.ReusedUnitBatches);
        Assert.Equal(0, result.ReusedMutationBatches);
        Assert.False(fixture.SavedState().Batches[key].Mutation!.Completed);
    }

    [Fact]
    public void RevalidationRequiresReviewReasonAndInstalledProducer()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var service = new IncrementalTestService(fixture);
        Assert.Equal(4, service.Revalidate(new(fixture.Path, "profile.json"), typeof(IncrementalTestService).Assembly.Location, "").ExitCode);
        fixture.Write("producer.dll", "not an installed producer");
        Assert.Equal(4, service.Revalidate(new(fixture.Path, "profile.json"), fixture.Full("producer.dll"), "Reviewed compatible update.").ExitCode);
        Assert.Empty(fixture.Calls());
    }

    private static IncrementalTestResult Revalidate(IncrementalTestServiceTests.Fixture fixture)
        => new IncrementalTestService(fixture).Revalidate(new(fixture.Path, "profile.json"), typeof(IncrementalTestService).Assembly.Location,
            "Reviewed compatible native-guard change; native assessment rules are unchanged.");

    [Fact]
    public void InvocationBudgetOverrideDoesNotRepeatCompletedResults()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var calls = fixture.Calls();
        var result = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json"), 30, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Equal(2, result.ReusedMutationBatches);
        Assert.Equal(calls, fixture.Calls());
        Assert.Contains(result.Diagnostics, item => item.Contains("30 seconds", StringComparison.Ordinal));
    }
}
