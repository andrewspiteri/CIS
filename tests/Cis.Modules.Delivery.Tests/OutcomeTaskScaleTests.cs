using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Change;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomePlanning_OneDecisionCovers100CanonicalTasksWithinOneAggregateLimit()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Metadata delivery", "Deliver the same accepted behavior.", [])).Change);
        var request = OutcomeRequest(repository.Path) with
        {
            Seconds = 1000, Attempts = 100, RestorationSeconds = 100, VisualUi = false,
            Storage = new(64 * 1024 * 1024, [new(repository.Path, ".cis/local")]) { RestorationBytes = 1024 * 1024 }
        };
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize one outcome and shared finite total."));
        Assert.Equal(0, authorized.ExitCode);
        var authorityPath = services.Changes.DossierFile(change, "authorization.json");
        var authority = File.ReadAllBytes(authorityPath);
        Assert.Equal(0, services.Plans.Build(repository.Path, change.Id).ExitCode);
        var tasks = Enumerable.Range(2, 99).Select(index => new OutcomeTaskDefinition(
            $"Supporting task {index}", ["implementation"], "The accepted behavior remains valid.", "Review the relevant change.")
            { Paths = ["src/Orders.Api/Program.cs"] }).ToArray();
        var organized = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id, tasks,
            "Controller", "Organize the one authorized outcome without additional decisions."));
        Assert.True(organized.ExitCode == 0, string.Join("; ", organized.Errors.Concat(organized.Validation?.Errors ?? [])));
        Assert.Equal(100, organized.WorkItems.Count);
        Assert.Equal(100, Directory.GetFiles(Path.Combine(Path.GetDirectoryName(authorityPath)!, "agent-tasks"), "*.md").Length);
        var controller = new AgentService(new CisRepositoryContextResolver(), [], taskContracts: services.Plans);
        var allocation = new CisAgentWorkAllocationRequest(1000, 100, 100,
            organized.WorkItems.Select(task => new CisAgentWorkTaskAllocation(task.Id, 10, 1, 1, Fixed: false, AllowRedistribution: true)).ToArray(),
            request.DecisionReference, "fixture:100-task-shared-ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = request.Storage };
        var input = Path.Combine(repository.Path, "allocation.json");
        void Write(CisAgentWorkAllocationRequest value) => File.WriteAllText(input,
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Write(allocation with { Tasks = allocation.Tasks.Select((task, index) => index == 0 ? task with { Seconds = 11 } : task).ToArray() });
        var denied = controller.AllocateWork(repository.Path, change.Id, input, "Controller", "Attempt to exceed the unchanged total.");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Contains(denied.Errors, error => error.Contains("Aggregate", StringComparison.Ordinal));
        Write(allocation);
        var allocated = controller.AllocateWork(repository.Path, change.Id, input, "Controller", "Distribute the original total across all canonical tasks.");
        Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
        Assert.Equal(1_000_000, allocated.Work!.Change.AllocatedMilliseconds);
        Assert.Equal(100, allocated.Work.Change.AllocatedAttempts);
        Assert.Equal(authority, File.ReadAllBytes(authorityPath));
        var events = File.ReadAllLines(services.Changes.DossierFile(change, "events.jsonl"));
        Assert.Single(events, line => line.Contains("\"outcome-authorized\"", StringComparison.Ordinal));
        Assert.DoesNotContain(events, line => line.Contains("\"plan-approved\"", StringComparison.Ordinal));
        Assert.DoesNotContain(events, line => line.Contains("\"impact-disposition\"", StringComparison.Ordinal));
    }
}
