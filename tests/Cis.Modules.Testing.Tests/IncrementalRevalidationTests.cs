using Cis.Abstractions;
using System.Text.Json;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRevalidationTests
{
    [Fact]
    public void FailedRevalidationKeepsItsLeaseThroughFinalizationAndDoesNotOverwriteASameParentInvocation()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, MaxRetainedBytes = 32 * 1024 * 1024 });
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path);
        var parentAdmission = authority.Admit(new(fixture.Path, "native/shared-revalidation-parent", "controlled-parent", 120));
        Assert.True(parentAdmission.Allowed);
        using var parent = parentAdmission.Scope!;
        var service = new IncrementalTestService(fixture, authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120)
        {
            Work = new("CIS-0001", "WORK-090", parent.Reference.ExecutionId, fixture.Path, "Controlled unit controller")
        };
        var seeded = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.True(seeded.ExitCode == 0, string.Join("; ", seeded.Diagnostics));
        var calls = fixture.Calls();
        var statePath = fixture.Full(IncrementalTestService.LocalRoot + "/sample/state.json");
        var latestPath = fixture.Full(IncrementalTestService.LocalRoot + "/sample/latest-run.json");
        var graphReads = 0;
        var expectedFailure = false;
        var finalizationObserved = false;
        IncrementalTestResult? competing = null;
        const string reason = "Reviewed compatible producer while exercising the failure-publication boundary.";
        fixture.BeforeGraphRead = () =>
        {
            if (++graphReads != 3) return;
            // Old/current selection have completed and the retained batches have
            // been updated. Fail the final selection before result publication.
            fixture.BeforeGraphRead = null;
            expectedFailure = true;
            throw new IOException("Controlled final graph read failed after retained cache entries were updated.");
        };
        authority.BeforeStorageReservation = (operation, reference, operationId) =>
        {
            if (!expectedFailure || finalizationObserved || operation != "incremental-revalidate"
                || !operationId.StartsWith("evidence-mutation/", StringComparison.Ordinal)) return;
            finalizationObserved = true;
            Assert.Equal(parent.Reference.ExecutionId, reference.ParentExecutionId);
            var stateBefore = File.ReadAllBytes(statePath);
            var latestBefore = File.ReadAllBytes(latestPath);
            var attempted = service.Run(options, 0, TestContext.Current.CancellationToken);
            competing = attempted;
            Assert.Equal(4, attempted.ExitCode);
            Assert.Null(attempted.WorkReference); // The exclusive lease rejects before governed admission.
            Assert.Equal(0, attempted.ExecutedUnitBatches);
            Assert.Contains(attempted.Diagnostics, diagnostic => diagnostic.Contains("run.lock", StringComparison.Ordinal));
            // Failure to acquire the native lease cannot publish a failure over
            // the state or latest marker owned by the first invocation.
            Assert.Equal(stateBefore, File.ReadAllBytes(statePath));
            Assert.Equal(latestBefore, File.ReadAllBytes(latestPath));
        };

        var failed = service.Revalidate(options, typeof(IncrementalTestService).Assembly.Location, reason);

        Assert.True(expectedFailure); Assert.True(finalizationObserved); Assert.NotNull(competing);
        Assert.Equal("revalidation-failed", failed.Status); Assert.Equal(4, failed.ExitCode);
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Contains("Controlled final graph read failed", StringComparison.Ordinal));
        Assert.True(CisNativeWorkSettlement.Complete(authority.InspectSettlement(fixture.Path, failed.WorkReference!), failed.WorkReference!));
        Assert.All(fixture.SavedState().Batches.Values,
            batch => Assert.Contains(batch.Unit!.Diagnostics, diagnostic => diagnostic.Contains(reason, StringComparison.Ordinal)));
        authority.BeforeStorageReservation = null;
        var accepted = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.True(accepted.ExitCode == 0, string.Join("; ", accepted.Diagnostics));
        Assert.Equal(0, accepted.ExecutedUnitBatches); Assert.Equal(2, accepted.ReusedUnitBatches);
        Assert.Equal(parent.Reference.ExecutionId, accepted.WorkReference!.ParentExecutionId);
        var finalState = fixture.SavedState();
        Assert.Equal(accepted.WorkReference, finalState.WorkReference);
        Assert.False(finalState.RecoveryRequired);
        Assert.Null(finalState.ActiveExecutionUntilUtc); Assert.Null(finalState.ActiveWorkReference);
        var latest = JsonSerializer.Deserialize<IncrementalTestResult>(File.ReadAllBytes(latestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(accepted.WorkReference, latest.WorkReference);
        Assert.Equal(accepted.Status, latest.Status);
        Assert.All(service.Plan(options).Batches, batch => Assert.True(batch.UnitCurrent));
        Assert.Equal(calls, fixture.Calls());
        using var released = new FileStream(fixture.Full(MutationRunService.LocalRoot + "/run.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(CisNativeWorkSettlement.Complete(parent.Finish("native-finished", "The controlled same-parent invocations settled.", []),
            parent.Reference));
    }

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
    public void RevalidationRequiresReviewReasonAndRejectsTargetRepositoryDiscoveryFiles()
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
