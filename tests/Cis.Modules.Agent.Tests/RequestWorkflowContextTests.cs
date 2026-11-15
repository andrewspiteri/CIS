using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void OutcomeSeed_LinkedStoryWithoutContractReaderHoldsThenSelectsConcreteWork()
    {
        using var fixture = new OutcomeParticipantFixture(concreteCriteria: false);
        var provider = new StoryExecutionProvider();
        var resolver = new CisRepositoryContextResolver();
        var controller = new AgentService(resolver, [provider], new WorkspaceRegistry(resolver),
            clock: Clock, textGeneration: new StoryExecutionModels());
        var work = StoryWork(fixture.Authority.Path) with
        {
            Task = StoryWork(fixture.Authority.Path).Task with
            {
                RepositoryIds = [], Category = "implementation", Complexity = "medium", ApprovalGate = "none",
                InstanceKey = "accepted-linked-story", ChangeId = OutcomeParticipantFixture.Change,
                WorkOwnerTaskId = fixture.TaskId
            },
            Repositories = [], Actor = OutcomeParticipantFixture.Actor
        };
        var originalAuthorization = fixture.ReadChange("authorization.json");
        var ledgerPath = Path.Combine(fixture.Authority.Path, CisAgentWorkPaths.Ledger);
        var originalLedger = File.ReadAllBytes(ledgerPath);
        var held = controller.SelectModels(work);
        Assert.Contains(held.Errors, error => error.Contains("planning placeholders", StringComparison.Ordinal));
        var execution = controller.Execute(work, held, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.Contains(execution.Errors, error => error.Contains("planning placeholders", StringComparison.Ordinal));
        Assert.Empty(provider.Requests);

        var specified = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            [new("Inspect accepted participant behavior", ["main"], "The selected value remains available to the participant.",
                "Inspect the selected participant value through the controller.")],
            OutcomeParticipantFixture.Actor, "Resolve the canonical seed under the existing grant.", fixture.TaskId), "update");
        Assert.Equal(0, specified.ExitCode);
        Assert.Equal(0, fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            fixture.TaskId, "InProgress", OutcomeParticipantFixture.Actor, "Begin concrete accepted work.")).ExitCode);
        Assert.Empty(controller.SelectModels(work).Errors);
        Assert.Equal(originalAuthorization, fixture.ReadChange("authorization.json"));
        Assert.Equal(originalLedger, File.ReadAllBytes(ledgerPath));
        Assert.Empty(provider.Requests); // This check establishes admission/model readiness, not native execution.
    }

    [Theory]
    [InlineData(CisAgentRunModes.Implement)]
    [InlineData(CisAgentRunModes.Review)]
    public void RequestWorkflow_ActualGuidanceReachesParticipantWorkerAndReviewer(string mode)
    {
        using var fixture = new OutcomeParticipantFixture();
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "src/Cis.Host/Cis.Host.csproj")))
            checkout = checkout.Parent;
        Assert.NotNull(checkout);
        var paths = new[]
        {
            ".github/skills/cis-agent-execution/SKILL.md",
            ".github/skills/cis-validate-completion/SKILL.md",
            ".github/instructions/cis-agent-execution.instructions.md",
            ".github/instructions/cis-delivery-execution.instructions.md",
            ".github/instructions/cis-change-delivery.instructions.md"
        };
        var guidance = paths.ToDictionary(path => path, path => File.ReadAllText(Path.Combine(checkout.FullName, path)));
        foreach (var (path, text) in guidance) fixture.Authority.Write(path, text);
        var provider = new FakeProvider();
        var result = fixture.Service(provider).Run(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            fixture.TaskId, "fake", mode, CisAgentPermissions.ReadOnly, "participant", "fake-json", 30,
            false, OutcomeParticipantFixture.Actor, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        var envelope = Assert.IsType<AgentTaskEnvelope>(result.Envelope);
        foreach (var (path, text) in guidance)
        {
            Assert.Contains(path, envelope.ContextArtifacts);
            var snapshot = Assert.Single(envelope.AuthorityArtifacts!, artifact => artifact.Path == path);
            Assert.Equal(text, snapshot.Content);
            Assert.Equal(ContextSha(text), snapshot.Sha256);
            Assert.Contains(System.Text.Json.JsonSerializer.Serialize(text), provider.LastRequest!.Prompt, StringComparison.Ordinal);
        }
        // Assert delivery of the actual retained context, rather than mirroring its wording.
        Assert.All(envelope.Constraints, constraint => Assert.Contains(constraint, provider.LastRequest!.Prompt, StringComparison.Ordinal));
        Assert.Contains(".github/skills/cis-validate-completion/SKILL.md", provider.LastRequest!.Prompt, StringComparison.Ordinal);
        TestContext.Current.TestOutputHelper!.WriteLine($"{mode}: {paths.Length} actual guidance files bound, hashed and delivered unchanged to the participant; constraints and shared completion-policy route are in the provider request.");
    }
}
