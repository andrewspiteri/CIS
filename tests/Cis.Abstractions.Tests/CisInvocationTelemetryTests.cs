namespace Cis.Abstractions.Tests;

public sealed class CisInvocationTelemetryTests
{
    [Fact]
    public void MissingIntegrationRemainsUnobservedAndCallsOutsideScopeDoNothing()
    {
        CisInvocationTelemetry.RecordBlocker("outside-scope");
        CisInvocationTelemetry.RecordDeliveredContext(99);
        using var scope = CisInvocationTelemetry.Begin("test plan");
        var result = scope.Snapshot();
        Assert.Null(result.DeliveredContextUtf8Bytes);
        Assert.Null(result.ChangeId);
        Assert.Null(result.TaskId);
        Assert.Null(result.ParentInvocationId);
        Assert.Empty(result.BlockerCodes);
    }

    [Fact]
    public void NestedInvocationHasItsOwnIdentityAndRestoresParentAssociation()
    {
        using var parent = CisInvocationTelemetry.Begin("plan task transition");
        CisInvocationTelemetry.AssociateTask("CIS-0005", "WORK-006");
        using (var child = CisInvocationTelemetry.Begin("test incremental plan"))
        {
            Assert.NotEqual(parent.InvocationId, child.InvocationId);
            Assert.Equal(parent.InvocationId, child.Snapshot().ParentInvocationId);
            CisInvocationTelemetry.RecordBlocker("child-blocker");
            Assert.Null(child.Snapshot().ChangeId);
        }
        CisInvocationTelemetry.RecordBlocker("parent-blocker");
        Assert.Equal(["parent-blocker"], parent.Snapshot().BlockerCodes);
        Assert.Equal("WORK-006", parent.Snapshot().TaskId);
    }

    [Fact]
    public void InvalidValuesAreOmittedRatherThanPersistedAndTaskCannotBeRelabelled()
    {
        using var scope = CisInvocationTelemetry.Begin("plan task transition");
        CisInvocationTelemetry.AssociateTask("CIS-0005", "WORK-006");
        CisInvocationTelemetry.AssociateTask("CIS-0005", "WORK-007");
        CisInvocationTelemetry.RecordGraph("context.task", "sha256:" + new string('a', 64), "reused", "current-inputs");
        CisInvocationTelemetry.RecordGraph("context.task", "C:/private/secret", "reused", "unsafe-path");
        CisInvocationTelemetry.RecordVerification("unit", "reused", 2, "material-inputs-match");
        CisInvocationTelemetry.RecordVerification("unit", "passed", 2, "not-an-execution-disposition");
        CisInvocationTelemetry.RecordBlocker("authorization header=value");
        CisInvocationTelemetry.RecordDeliveredContext(-1);
        var result = scope.Snapshot();
        Assert.Equal("WORK-006", result.TaskId);
        Assert.Single(result.Graph);
        Assert.Single(result.Verification);
        Assert.Empty(result.BlockerCodes);
        Assert.Null(result.DeliveredContextUtf8Bytes);
        Assert.Equal(5, result.DroppedObservations);
    }

    [Fact]
    public void ObservationsAreBoundedDeduplicatedAndSnapshotsAreDetached()
    {
        using var scope = CisInvocationTelemetry.Begin("graph find");
        for (var index = 0; index < 40; index++)
            CisInvocationTelemetry.RecordGraph("consumer-" + index, null, "reused", "current-inputs");
        CisInvocationTelemetry.RecordGraph("consumer-0", null, "reused", "current-inputs");
        var previous = scope.Snapshot();
        Assert.Equal(CisInvocationTelemetryScope.MaximumObservationsPerKind, previous.Graph.Count);
        Assert.Equal(24, previous.DroppedObservations);
        CisInvocationTelemetry.RecordBlocker("graph-partial");
        Assert.Empty(previous.BlockerCodes);
        Assert.Single(scope.Snapshot().BlockerCodes);
    }

    [Fact]
    public void ExplicitZeroAndMultipleDeliveredEnvelopesAreMeasuredWithoutOverflow()
    {
        using var scope = CisInvocationTelemetry.Begin("agent prepare");
        CisInvocationTelemetry.RecordDeliveredContext(0);
        Assert.Equal(0L, scope.Snapshot().DeliveredContextUtf8Bytes);
        CisInvocationTelemetry.RecordDeliveredContext(17);
        CisInvocationTelemetry.RecordDeliveredContext(19);
        CisInvocationTelemetry.RecordDeliveredContext(long.MaxValue);
        Assert.Equal(36L, scope.Snapshot().DeliveredContextUtf8Bytes);
        Assert.Equal(1, scope.Snapshot().DroppedObservations);
    }

    [Fact]
    public void PhaseScopesAreInclusiveMeasuredOnceAndRetainedWithoutStderrTracing()
    {
        using var scope = CisInvocationTelemetry.Begin("test incremental plan");
        var timing = CisInvocationTelemetry.MeasurePhase("identity");
        timing.Dispose();
        timing.Dispose();
        using (CisPerformanceTrace.Start("GraphSnapshot.Read")) { }
        var phases = scope.Snapshot().Phases;
        Assert.Equal(1, phases.Single(phase => phase.Phase == "identity").Count);
        Assert.Contains(phases, phase => phase.Phase == "graph" && phase.ElapsedMilliseconds >= 0);
        Assert.All(phases, phase => Assert.True(double.IsFinite(phase.ElapsedMilliseconds)));
    }

    [Fact]
    public void LowLevelTracesAggregateWithoutCrowdingExplicitPhasesOrRetainingPaths()
    {
        using var scope = CisInvocationTelemetry.Begin("plan task transition");
        for (var index = 0; index < 64; index++)
        {
            using (CisPerformanceTrace.Start("C:/private/source-" + index + ".cs")) { }
            using (CisPerformanceTrace.Start("GraphReader" + index + ".Read")) { }
        }
        using (CisInvocationTelemetry.MeasurePhase("native")) { }
        using (CisInvocationTelemetry.MeasurePhase("render")) { }
        var observation = scope.Snapshot();
        Assert.Equal(0, observation.DroppedObservations);
        Assert.Equal(4, observation.Phases.Count);
        Assert.Equal(64, observation.Phases.Single(phase => phase.Phase == "graph").Count);
        Assert.Equal(64, observation.Phases.Single(phase => phase.Phase == "other").Count);
        Assert.Contains(observation.Phases, phase => phase.Phase == "native");
        Assert.Contains(observation.Phases, phase => phase.Phase == "render");
        Assert.DoesNotContain("C:/private", System.Text.Json.JsonSerializer.Serialize(observation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentObserversShareOnlyTheirInvocationAndRetainTheBound()
    {
        using var scope = CisInvocationTelemetry.Begin("test incremental run");
        await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            CisInvocationTelemetry.RecordVerification("unit-" + index, "executed", 1, "native-launched"),
            TestContext.Current.CancellationToken)));
        Assert.Equal(CisInvocationTelemetryScope.MaximumObservationsPerKind, scope.Snapshot().Verification.Count);
        Assert.Equal(48, scope.Snapshot().DroppedObservations);
    }
}
