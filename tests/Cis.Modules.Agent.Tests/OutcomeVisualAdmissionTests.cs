using Cis.Abstractions;
using Cis.Modules.Agent;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("frontend")]
    [InlineData("wireframe")]
    [InlineData("design")]
    public void OutcomeAdmission_HoldsPreviouslyStartedVisualTaskWhenUiScopeIsWithdrawn(string category)
    {
        using var fixture = new OutcomeParticipantFixture();
        fixture.Amend("visual-ui");
        // Retain a canonical already-started visual task across the human scope
        // amendment; its old selected ScopeBasis intentionally remains unchanged.
        fixture.SetTaskCategory(category);
        fixture.Amend("no-visual-ui");
        var provider = new FakeProvider();
        // Agent must enforce this even when the optional Plan contract service is absent.
        var controller = fixture.Service(provider, withContractReader: false);
        var association = new CisNativeWorkOptions(OutcomeParticipantFixture.Change, fixture.TaskId,
            AuthorityPath: fixture.Authority.Path, Actor: OutcomeParticipantFixture.Actor);

        var inspected = controller.Inspect(fixture.Authority.Path, association);
        var native = controller.Admit(new(fixture.Authority.Path, "native/withdrawn-visual-scope", "incremental-unit", 30, association));
        var direct = fixture.Run(controller);

        Assert.Contains(inspected.Errors, error => error.Contains("no-UI scope", StringComparison.Ordinal));
        Assert.False(native.Allowed);
        Assert.Null(native.Scope);
        Assert.Contains(native.Errors, error => error.Contains("no-UI scope", StringComparison.Ordinal));
        Assert.Contains(direct.Diagnostics, error => error.Contains("no-UI scope", StringComparison.Ordinal));
        Assert.Equal(0, provider.ExecuteCalls);
        var retained = controller.WorkStatus(fixture.Authority.Path, OutcomeParticipantFixture.Change, fixture.TaskId).Work!;
        Assert.Empty(retained.Executions);
        Assert.Equal(0, retained.Change.ConsumedMilliseconds);
        Assert.Equal(0, retained.Change.ReservedMilliseconds);
    }
}
