using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Testing.Tests;

[Collection("Incremental environment")]
public sealed class IncrementalGovernedUnitRetryTests
{
    [Theory]
    [InlineData("unit", false, false)]
    [InlineData("all", false, false)]
    [InlineData("unit", true, false)]
    [InlineData("unit", true, true)]
    public void SettledUnitInterruptionRetriesOnlyUnfinishedWorkWithoutMutationRestoration(string layer,
        bool mutationConfigured, bool recoveryConfigured)
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured, recoveryConfigured);
        var first = fixture.Run(maximumBatches: 1, layer: layer);
        Assert.True(first.ExitCode == 3, $"Expected one completed batch and one pending batch; got {first.Status}/{first.ExitCode}: "
            + string.Join("; ", first.Diagnostics));
        Assert.Equal(1, first.ExecutedUnitBatches);
        Assert.All(fixture.Work().Executions, execution => Assert.False(execution.NativeRestorationRequired));
        var accepted = fixture.Native.SavedState().Batches.Values.Single(batch => batch.Unit is not null).Unit!;
        var report = fixture.Native.Full(IncrementalTestService.LocalRoot + "/sample/" + accepted.ReportPath);
        var reportBytes = File.ReadAllBytes(report);

        var interrupted = fixture.InterruptedState(settled: true);
        var before = fixture.Work();
        Assert.True(CisNativeWorkSettlement.Complete(
            fixture.Controller.InspectSettlement(fixture.Native.Path, interrupted), interrupted));

        var retried = fixture.Run(layer: layer);

        Assert.True(retried.ExitCode == 0, string.Join("; ", retried.Diagnostics));
        Assert.True(retried.ScopeComplete);
        Assert.Equal(1, retried.ExecutedUnitBatches);
        Assert.Equal(1, retried.ReusedUnitBatches);
        Assert.Equal(0, retried.ExecutedMutationBatches);
        Assert.Equal(reportBytes, File.ReadAllBytes(report));
        Assert.Equal(2, fixture.Native.Calls().Length);
        Assert.All(fixture.Native.Calls(), call => Assert.StartsWith("unit ", call, StringComparison.Ordinal));
        var state = fixture.Native.SavedState();
        Assert.False(state.RecoveryRequired);
        Assert.Null(state.ActiveWorkReference);
        Assert.Null(state.ActiveExecutionUntilUtc);
        var after = fixture.Work();
        Assert.All(after.Executions, execution => Assert.NotEqual("native-restoration", execution.Stage?.Name));
        Assert.All(after.Executions, execution => Assert.False(execution.NativeRestorationRequired));
        Assert.Equal(before.Task!.AllocatedMilliseconds, after.Task!.AllocatedMilliseconds);
        Assert.Equal(before.Task.AllocatedAttempts, after.Task.AllocatedAttempts);
        Assert.True(after.Task.ConsumedMilliseconds >= before.Task.ConsumedMilliseconds);
        Assert.True(after.Task.UsedAttempts >= before.Task.UsedAttempts);
        Assert.Equal(0, after.Task.ReservedMilliseconds);
        Assert.Equal(0, fixture.Controller.Inspect(fixture.Native.Path, fixture.Association).Control!.ExitCode);
    }

    [Fact]
    public void LiveRetainedUnitWorkPreventsAnotherAdmissionEvenAfterItsDeadline()
    {
        using var fixture = new GovernedUnitFixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        fixture.InterruptedState(settled: false, deadline: DateTimeOffset.UtcNow.AddHours(-1));
        var before = fixture.Work();
        var stateBytes = File.ReadAllBytes(fixture.StatePath);
        var calls = fixture.Native.Calls();

        var held = fixture.Run();

        Assert.Equal(4, held.ExitCode);
        Assert.Contains(held.Diagnostics, error => error.Contains("previous unit invocation still needs settlement", StringComparison.Ordinal));
        Assert.Equal(calls, fixture.Native.Calls());
        Assert.Equal(stateBytes, File.ReadAllBytes(fixture.StatePath));
        Assert.Equal(before.Executions.Count, fixture.Work().Executions.Count);
        Assert.Equal(before.Task, fixture.Work().Task);
    }

    [Fact]
    public void SharedOutputLeaseIsAcquiredBeforeGovernedAdmission()
    {
        using var fixture = new GovernedUnitFixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        fixture.InterruptedState(settled: true);
        var before = File.ReadAllBytes(fixture.Native.Full(CisAgentWorkPaths.Ledger));
        var calls = fixture.Native.Calls();
        using var lease = new FileStream(fixture.Native.Full(".cis/local/testing/mutation/run.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var held = fixture.Run();

        Assert.Equal(4, held.ExitCode);
        Assert.Equal(calls, fixture.Native.Calls());
        Assert.Equal(before, File.ReadAllBytes(fixture.Native.Full(CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void UnitRetryCannotTransferItsRetainedInvocationToAnotherTask()
    {
        using var fixture = new GovernedUnitFixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        fixture.InterruptedState(settled: true);
        var before = File.ReadAllBytes(fixture.Native.Full(CisAgentWorkPaths.Ledger));
        var calls = fixture.Native.Calls();

        var held = fixture.Service.Run(fixture.Options() with
        { Work = fixture.Association with { TaskId = "WORK-091" } }, 0, TestContext.Current.CancellationToken);

        Assert.Equal(4, held.ExitCode);
        Assert.Contains(held.Diagnostics, error => error.Contains("retained change, task and authority", StringComparison.Ordinal));
        Assert.Equal(calls, fixture.Native.Calls());
        Assert.Equal(before, File.ReadAllBytes(fixture.Native.Full(CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void UnitLayerDoesNotClearAnActualMutationRestorationMarker()
    {
        using var fixture = new GovernedUnitFixture();
        Assert.Equal(3, fixture.Run(maximumBatches: 1).ExitCode);
        fixture.InterruptedState(settled: true);
        fixture.Native.WriteState(fixture.Native.SavedState() with { RecoveryRequired = true, RecoverySourceFile = "src/B.cs" });
        var calls = fixture.Native.Calls();

        var held = fixture.Run();

        Assert.Equal(4, held.ExitCode);
        Assert.True(fixture.Native.SavedState().RecoveryRequired);
        Assert.Equal(calls, fixture.Native.Calls());
    }

    [Theory]
    [InlineData("mutation")]
    [InlineData("all")]
    public void MutationExecutionStillRequiresItsRecoveryCommand(string layer)
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured: true);

        var held = fixture.Service.Plan(fixture.Options(layer: layer));

        Assert.Equal(2, held.ExitCode);
        Assert.Contains(held.Diagnostics, error => error.Contains("Mutation execution requires a recoveryCommand", StringComparison.Ordinal));
        Assert.Empty(fixture.Native.Calls());
    }

    // Compose the existing graph/native fixture with the real work and storage
    // authority. Only the crash point between stop and marker publication is injected.
    private sealed class GovernedUnitFixture : IDisposable
    {
        public IncrementalTestServiceTests.Fixture Native { get; } = new();
        public AgentService Controller { get; }
        public IncrementalTestService Service { get; }
        public CisNativeWorkOptions Association { get; }
        public string StatePath => Native.Full(IncrementalTestService.LocalRoot + "/sample/state.json");
        private readonly List<ICisNativeWorkScope> _held = [];

        public GovernedUnitFixture(bool mutationConfigured = false, bool recoveryConfigured = false)
        {
            Native.Save(Native.Profile with
            {
                MutationCommand = mutationConfigured ? Native.Profile.MutationCommand : null,
                RecoveryCommand = recoveryConfigured ? Native.Profile.RecoveryCommand : null,
                MaxRunBytes = 4 * 1024 * 1024,
                MaxRetainedBytes = 32 * 1024 * 1024
            });
            Native.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: sample\ndocumentation_root: docs/cis\n");
            Native.Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: sample\ndocuments: []\n");
            Native.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: false\n---\n# Unit verification\n");
            Native.Write("docs/cis/changes/CIS-0001/impact.md", "---\nstatus: Accepted\n---\n# Impact\n| Finding | Status |\n|---|---|\n| IMPACT-1 | accepted |\n");
            Native.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md",
                "---\ntask_status: InProgress\ntargets: [\"sample\"]\n---\n# Unit work\nVerify the existing selected cases.\n");
            Association = new("CIS-0001", "WORK-090", AuthorityPath: Native.Path, Actor: "Controller");
            Controller = new(new CisRepositoryContextResolver());
            var allocation = new CisAgentWorkAllocationRequest(1200, 6, 120,
                [new("WORK-090", 1200, 6, 120)], "fixture:unit-work", "fixture:finite-storage", AcknowledgeHistoricalUnknown: true)
            {
                Controllers = ["Controller"],
                Storage = new(64 * 1024 * 1024, [new(Native.Path, ".cis/local")]) { RestorationBytes = 1024 * 1024 }
            };
            Native.Write(".cis/local/unit-allocation.json", JsonSerializer.Serialize(allocation, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var allocated = Controller.AllocateWork(Native.Path, "CIS-0001", ".cis/local/unit-allocation.json", "Controller",
                "Allocate finite unit verification without a mutation restoration requirement.", adoptAccounting: true);
            Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
            Service = new(Native, Controller, Controller);
        }

        public IncrementalTestOptions Options(int maximumBatches = 4, string layer = "unit")
            => new(Native.Path, "profile.json", layer, maximumBatches, 120) { Work = Association };
        public IncrementalTestResult Run(int maximumBatches = 4, string layer = "unit")
            => Service.Run(Options(maximumBatches, layer), 0, TestContext.Current.CancellationToken);
        public CisAgentWorkView Work() => Controller.WorkStatus(Native.Path, "CIS-0001", "WORK-090").Work!;

        public CisAgentWorkReference InterruptedState(bool settled, DateTimeOffset? deadline = null)
        {
            var root = Controller.Admit(new(Native.Path, "native/incremental/interrupted-unit", "incremental-unit", 60, Association));
            Assert.True(root.Allowed, string.Join("; ", root.Errors));
            var rootScope = root.Scope!;
            var child = rootScope.AdmitChild("native/incremental/stage/interrupted-unit", "native-unit", 25);
            Assert.True(child.Allowed, string.Join("; ", child.Errors));
            var childScope = child.Scope!;
            if (settled)
            {
                Assert.Equal(0, childScope.Finish("Interrupted", "The invocation stopped before its unfinished unit stage launched.", []).ExitCode);
                Assert.Equal(0, rootScope.Finish("Interrupted", "Preserve the interrupted invocation's real preparation cost.", []).ExitCode);
                childScope.Dispose();
                rootScope.Dispose();
            }
            else
            {
                _held.Add(childScope);
                _held.Add(rootScope);
            }
            Native.WriteState(Native.SavedState() with
            {
                WorkReference = root.Reference,
                ActiveWorkReference = child.Reference,
                WorkAuthorityPath = Native.Path,
                WorkAssociation = Association,
                WorkCoverage = root.Coverage,
                ActiveExecutionUntilUtc = deadline ?? DateTimeOffset.UtcNow.AddMinutes(5)
            });
            return root.Reference!;
        }

        public void Dispose()
        {
            foreach (var scope in _held) scope.Dispose();
            Native.Dispose();
        }
    }
}
