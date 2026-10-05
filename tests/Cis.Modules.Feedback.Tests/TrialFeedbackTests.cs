using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;

namespace Cis.Modules.Feedback.Tests;

public sealed partial class FeedbackModuleTests
{
    [Fact]
    public void CompositeCommandRejectsNestedSavingsAndRetainsStructuredBlockAndVersion()
    {
        InitializeRepository();
        var store = new ToolUsageStore();
        store.Record(new CisToolUsageCapture(["workspace", "snapshot", "--repo", _root], _root,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 2, 100_000, 0,
            [new(61, null, "nested agent summary", "high", "agent runs")], "blocked", "fixture-build"));
        var entry = Assert.Single(store.Read(_root, null, 20).Entries);
        Assert.Equal("blocked", entry.Outcome);
        Assert.Equal("fixture-build", entry.BuildIdentity);
        Assert.Equal("none", entry.SavingsConfidence);
        Assert.Equal(entry.ActualEstimatedTokens, entry.BaselineEstimatedTokens);
        Assert.Contains(new FeedbackService(store).Opportunities(_root, null).Opportunities,
            item => item.Code == "CIS-FEEDBACK-COMPACTION" && item.Command == "workspace snapshot");
    }

    [Fact]
    public void ParserErrorsAreInvalidRequestsRatherThanExecutionFailures()
    {
        InitializeRepository();
        using var app = new CisHostBuilder().AddModule(new FeedbackModule()).AddModule(new ProbeModule(false)).Build();
        Assert.Equal(2, app.Invoke(["probe", "run", "--repo", _root, "--unknown-option"]));
        var entry = Assert.Single(new ToolUsageStore().Read(_root, null, 20).Entries);
        Assert.Equal("invalid-request", entry.Outcome);
        Assert.NotNull(entry.BuildIdentity);
    }
}
