using System.Text;
using System.Text.Json;
using Cis.Host;
using Cis.Modules.Change;
using Cis.Modules.Context;
using Cis.Modules.Decision;
using Cis.Modules.Design;
using Cis.Modules.Docs;
using Cis.Modules.Feedback;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void PlanRoutineSummary_BoundsRowsAndUnicodeBlockersWithoutHidingFailureCounts()
    {
        string text = string.Concat(Enumerable.Repeat("日本語 😀 ", 2_000));
        var items = Enumerable.Range(1, 100).Select(index => new PlanWorkItem($"WORK-{index:000}", "implementation", "medium",
            null, "docs/cis/tasks/" + text, text, [], [], [], text, text, "InProgress")).ToArray();
        var result = new PlanResult("blocked", "CIS-0001", "Approved", items,
            new(false, [text], [text], 1, 1, 0), Enumerable.Repeat(text, 200).ToArray(), false);

        string output = PlanRoutineSummary.Render(result, null);

        using var json = JsonDocument.Parse(output);
        Assert.Equal(201, json.RootElement.GetProperty("blockerCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("blockers").GetArrayLength() > 0);
        Assert.True(json.RootElement.GetProperty("omittedBlockers").GetInt32() > 0);
        Assert.True(json.RootElement.GetProperty("blockerTextTruncated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("omittedWorkItems").GetInt32() > 0);
        Assert.Equal(result.ExitCode, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.True(output.Length + Environment.NewLine.Length <= 12_000);
        Assert.True(Encoding.UTF8.GetByteCount(output + Environment.NewLine) <= 48_000);
    }

    [Fact]
    public void PlanRoutineSummary_PreservesLegacyJsonAndReturnsAffectedTaskAndBlockers()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = CreateAdHocChange(repository, services, AdHocFinding("IMPACT-ALPHA", "orders-api-alpha"));
        var built = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(built);
        string taskId = Assert.Single(built.WorkItems).Id;
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule()).AddModule(new DocsModule()).AddModule(new GraphModule())
            .AddModule(new ContextModule()).AddModule(new DecisionModule()).AddModule(new ChangeModule())
            .AddModule(new ImpactModule()).AddModule(new PlanModule()).AddModule(new DesignModule())
            .AddModule(new FeedbackModule()).Build();
        (int ExitCode, string Output) Invoke(params string[] arguments)
        {
            var previous = Console.Out;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                int exit = application.Invoke([.. arguments, "--repo", repository.Path, "--format", "json"]);
                return (exit, output.ToString());
            }
            finally
            {
                Console.SetOut(previous);
            }
        }

        var complete = Invoke("plan", "status", change.Id);
        using var legacy = JsonDocument.Parse(complete.Output);
        Assert.True(legacy.RootElement.GetProperty("workItems")[0].TryGetProperty("acceptanceCriteria", out _));
        var compact = Invoke("plan", "status", change.Id, "--summary");
        using var summary = JsonDocument.Parse(compact.Output);
        Assert.Equal(complete.ExitCode, compact.ExitCode);
        Assert.Equal("routine-summary", summary.RootElement.GetProperty("kind").GetString());
        Assert.False(summary.RootElement.GetProperty("workItems")[0].TryGetProperty("acceptanceCriteria", out _));

        var refusal = Invoke("plan", "task", "transition", change.Id, taskId, "--status", "Unknown",
            "--actor", "Synthetic controller", "--reason", "Exercise bounded refusal.", "--summary");
        using var refused = JsonDocument.Parse(refusal.Output);
        Assert.NotEqual(0, refusal.ExitCode);
        Assert.Equal(taskId, refused.RootElement.GetProperty("affectedTaskId").GetString());
        Assert.True(refused.RootElement.GetProperty("blockerCount").GetInt32() > 0);
        Assert.True(refusal.Output.Length <= 12_000);
        Assert.True(Encoding.UTF8.GetByteCount(refusal.Output) <= 48_000);
        var usage = new ToolUsageStore().Read(repository.Path);
        var captured = Assert.Single(usage.Entries, entry => entry.Observations?.Operation == "plan task transition");
        Assert.NotNull(captured.Observations);
        Assert.Equal(change.Id, captured.Observations.ChangeId);
        Assert.Equal(taskId, captured.Observations.TaskId);
        Assert.Equal(refusal.ExitCode, captured.ExitCode);
    }
}
