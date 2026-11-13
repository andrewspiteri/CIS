using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Change;
using Cis.Modules.Decision;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Plan;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutcomeAdmission_ProductReadinessDoesNotGateDraftsOrDistributionButStillGatesExecution(bool applicable)
    {
        using var repository = AgentRepository.Create();
        const string actor = "Readiness controller";
        repository.Write("src/X.cs", "class X { }");
        repository.Write("src/Y.cs", "class Y { }");
        repository.Write("tests/XTests.cs", "class XTests { }");
        repository.Write("tests/YTests.cs", "class YTests { }");
        repository.Write("config.json", "{}");
        var graph = new ProtectedEvidenceGraph(repository.Path);
        repository.Write(".cis/local/graph/manifest.json", JsonSerializer.Serialize(graph.Read(repository.Path).Manifest, JsonOptions));
        var resolver = new CisRepositoryContextResolver();
        var readiness = new OutcomeProductReadiness(applicable);
        var changes = new ChangeDossierStore(resolver, readinessChecks: [readiness]);
        var storage = new CisOwnedStoragePolicy(128 * 1024 * 1024, [new(repository.Path, ".cis/local")]);
        var grant = new CisOutcomeAuthorizationRequest("Deliver the neutral readiness example.",
            [new("example", "The neutral source and its tests.", ["src", "tests"])], ["No unrelated product scope."],
            300, 6, 30, storage, [actor], true, true, false, false, "fixture:readiness-outcome");
        var created = changes.Create(new(repository.Path, "Readiness example", grant.Outcome, [], "CIS-0001")
        { OutcomeAuthorization = grant });
        Assert.True(created.ExitCode == 0, string.Join("; ", created.Errors));
        var authorized = changes.Authorize(new(repository.Path, "CIS-0001", grant, "Maintainer", "One bounded synthetic outcome authorization."));
        Assert.True(authorized.ExitCode == 0, string.Join("; ", authorized.Errors));
        var planner = new PlanningService(changes, new ImpactAnalysisService(changes, new GraphQueryService(resolver)),
            new DecisionService(changes, resolver), resolver, readinessChecks: [readiness]);
        var draft = planner.Build(repository.Path, "CIS-0001");
        Assert.True(draft.ExitCode == 0, string.Join("; ", draft.Errors));
        var task = Assert.Single(draft.WorkItems);
        var provider = new FakeProvider();
        var fixture = new OutcomeReadinessServices(readiness, provider);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(fixture).Build();
        var controller = Assert.IsType<AgentService>(fixture.Controller);
        var prepared = controller.Prepare(repository.Path, "CIS-0001", task.Id, "fake");
        Assert.Empty(prepared.Diagnostics);
        Assert.Contains("docs/cis/changes/CIS-0001/authorization.json", prepared.Envelope!.ContextArtifacts);
        Assert.Contains("docs/cis/changes/CIS-0001/proposal.md", prepared.Envelope.ContextArtifacts);
        var evaluations = readiness.Evaluations;
        var allocation = new CisAgentWorkAllocationRequest(300, 6, 30, [new(task.Id, 240, 4, 30)],
            grant.DecisionReference, "fixture:readiness-storage", AcknowledgeHistoricalUnknown: true)
        { Storage = storage, Controllers = [actor] };
        var allocated = controller.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "readiness-allocation.json", allocation),
            actor, "Distribute already authorized resources while product readiness is unresolved.");
        Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
        Assert.Equal(evaluations, readiness.Evaluations);

        // The task was eligible earlier, but the canonical product prerequisite subsequently became stale.
        readiness.Ready = true;
        var started = planner.TransitionTask(new(repository.Path, "CIS-0001", task.Id, "InProgress", actor, "Start while product prerequisites are current."));
        Assert.True(started.ExitCode == 0, string.Join("; ", started.Errors));
        readiness.Ready = false;
        var association = new CisNativeWorkOptions("CIS-0001", task.Id, AuthorityPath: repository.Path, Actor: actor);
        if (!applicable)
        {
            // This Agent-only composition has no planning contract reader. Its own
            // header checks must still prevent a view from inventing another owner.
            var taskPath = Path.Combine(repository.Path, "docs/cis/changes/CIS-0001", task.TaskPath!);
            var original = File.ReadAllText(taskPath);
            File.WriteAllText(taskPath, original.Replace("work_owner_task_id: null", "work_owner_task_id: WORK-999", StringComparison.Ordinal));
            Assert.Contains(controller.Inspect(repository.Path, association).Errors,
                error => error.Contains("ownership", StringComparison.Ordinal));
            File.WriteAllText(taskPath, original.Replace("parent_id: none", "parent_id: \"none\"", StringComparison.Ordinal));
            Assert.Empty(controller.Inspect(repository.Path, association).Errors);
            File.WriteAllText(taskPath, original);
        }
        var inspection = controller.Inspect(repository.Path, association);
        if (applicable)
        {
            Assert.Contains(inspection.Errors, error => error.Contains("technical-intent", StringComparison.Ordinal));
            var native = controller.Admit(new(repository.Path, "native/blocked-outcome-readiness", "incremental-unit", 30, association));
            Assert.False(native.Allowed);
            Assert.Null(native.Scope);
            Assert.Contains(native.Errors, error => error.Contains("canonical technical intent", StringComparison.Ordinal));
            var direct = controller.Run(repository.Path, "CIS-0001", task.Id, "fake", CisAgentRunModes.Plan,
                CisAgentPermissions.ReadOnly, null, null, 30, false, actor, TestContext.Current.CancellationToken);
            Assert.Contains(direct.Diagnostics, error => error.Contains("canonical technical intent", StringComparison.Ordinal));
        }
        else Assert.Empty(inspection.Errors);
        Assert.Equal(0, provider.ExecuteCalls);
        var retained = controller.WorkStatus(repository.Path, "CIS-0001", task.Id).Work!;
        Assert.Empty(retained.Executions);
        Assert.Equal(0, retained.Change.ConsumedMilliseconds);
        Assert.Equal(0, retained.Change.ReservedMilliseconds);
    }

    private sealed class OutcomeProductReadiness(bool applicable) : IChangeReadinessCheck
    {
        public bool Ready { get; set; }
        public int Evaluations { get; private set; }
        public ChangeReadinessResult Evaluate(string repositoryPath)
        {
            Evaluations++;
            return new("technical-intent", applicable, Ready,
                Ready ? [] : ["An Active, current canonical technical intent is missing."]);
        }
    }

    private sealed class OutcomeReadinessServices(IChangeReadinessCheck readiness, ICisAgentProvider provider) : ICisModule
    {
        public string Name => "outcome-readiness-test";
        public string Description => "Register neutral readiness and provider services for the real Agent module.";
        public AgentService? Controller { get; private set; }
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton(readiness);
            services.AddSingleton(provider);
        }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
            => Controller = services.GetRequiredService<AgentService>();
    }
}
