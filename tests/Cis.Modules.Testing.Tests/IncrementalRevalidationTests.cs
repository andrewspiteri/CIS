using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRevalidationTests
{
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
        var result = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json"), 30);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Equal(2, result.ReusedMutationBatches);
        Assert.Equal(calls, fixture.Calls());
        Assert.Contains(result.Diagnostics, item => item.Contains("30 seconds", StringComparison.Ordinal));
    }
}
