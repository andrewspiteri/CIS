using System.Text.Json;
using Cis.Abstractions;
using Cis.Providers.Agent.Codex;

namespace Cis.Modules.Agent.Tests;

public sealed class CodexAppServerThreadTests
{
    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("interrupted")]
    public void ChildTurnCannotFinishTheParentRun(string status)
    {
        using var child = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            method = "turn/completed",
            @params = new { threadId = "child-review", turn = new { status } },
        }));

        Assert.Null(CodexAgentProvider.AppServerTerminalState(child.RootElement, "parent", "current"));
    }

    [Fact]
    public void DelegatedReviewDoesNotReplaceTheParentCompletion()
    {
        var summary = string.Empty;
        long? input = null, output = null;
        foreach (var (thread, text) in new[] { ("parent", "Parent working"), ("child-review", "Child prose"), ("parent", "{\"summary\":\"Parent done\"}"), ("child-review", "Late child prose") })
        {
            var line = JsonSerializer.Serialize(new { method = "item/completed", @params = new { threadId = thread, turnId = "current", item = new { type = "agentMessage", text } } });
            using var message = JsonDocument.Parse(line);
            var previous = summary;
            CodexAgentProvider.ParseAppServerEvent(message.RootElement, line, "parent", "current", ref summary, ref input, ref output);
            Assert.Equal(thread == "parent" ? text : previous, summary);
        }
        using var completed = JsonDocument.Parse("""{"method":"turn/completed","params":{"threadId":"parent","turn":{"id":"current","status":"completed"}}}""");
        Assert.Equal(CisAgentRunStates.Succeeded, CodexAgentProvider.AppServerTerminalState(completed.RootElement, "parent", "current"));
        Assert.Equal("{\"summary\":\"Parent done\"}", summary);
    }

    [Theory]
    [InlineData("item/started")]
    [InlineData("item/completed")]
    public void ChildCompactionCannotChangeParentIdleTimeout(string method)
    {
        using var child = JsonDocument.Parse(JsonSerializer.Serialize(new { method, @params = new { threadId = "child-review", item = new { type = "contextCompaction" } } }));
        Assert.Null(CodexAgentProvider.ContextCompactionActivity(child.RootElement, "parent", "current"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("parent")]
    public void UnidentifiedCompletionCannotFinishTheRun(string? session)
    {
        using var notification = JsonDocument.Parse("""{"method":"turn/completed","params":{"turn":{"status":"completed"}}}""");
        Assert.Null(CodexAgentProvider.AppServerTerminalState(notification.RootElement, session, "current"));
    }

    [Fact]
    public void ChildErrorIsRetainedWithoutFailingTheParent()
    {
        using var child = JsonDocument.Parse("""{"method":"error","params":{"threadId":"child-review","willRetry":false}}""");
        using var parent = JsonDocument.Parse("""{"method":"error","params":{"threadId":"parent","willRetry":false}}""");
        Assert.Null(CodexAgentProvider.AppServerTerminalState(child.RootElement, "parent", "current"));
        Assert.Equal(CisAgentRunStates.Failed, CodexAgentProvider.AppServerTerminalState(parent.RootElement, "parent", "current"));
    }

    [Theory]
    [InlineData("previous")]
    [InlineData(null)]
    public void OtherOrUnidentifiedTurnCannotReplaceParentResult(string? turnId)
    {
        var line = JsonSerializer.Serialize(new { method = "item/completed", @params = new { threadId = "parent", turnId, item = new { type = "agentMessage", text = "Wrong result" } } });
        using var message = JsonDocument.Parse(line);
        var summary = "Retained parent result";
        long? input = null, output = null;
        CodexAgentProvider.ParseAppServerEvent(message.RootElement, line, "parent", "current", ref summary, ref input, ref output);
        Assert.Equal("Retained parent result", summary);
        using var completion = JsonDocument.Parse(JsonSerializer.Serialize(new { method = "turn/completed", @params = new { threadId = "parent", turn = new { id = turnId, status = "completed" } } }));
        Assert.Null(CodexAgentProvider.AppServerTerminalState(completion.RootElement, "parent", "current"));
    }

    [Fact]
    public void TurnStartResponseBindsTheAcknowledgedTurn()
    {
        using var response = JsonDocument.Parse("""{"id":2,"result":{"turn":{"id":"acknowledged","status":"inProgress"}}}""");
        Assert.Null(CodexAgentProvider.TurnResponseError(response.RootElement, out var turnId));
        Assert.Equal("acknowledged", turnId);
    }

    [Theory]
    [InlineData("{\"id\":2,\"error\":{\"message\":\"Cannot start turn\"}}", "Cannot start turn")]
    [InlineData("{\"id\":2,\"result\":null}", "turn identity")]
    [InlineData("{\"id\":2,\"result\":{\"turn\":{\"id\":\"\"}}}", "turn identity")]
    public void InvalidTurnStartFailsAsProtocolError(string json, string expected)
    {
        using var response = JsonDocument.Parse(json);
        Assert.Contains(expected, CodexAgentProvider.TurnResponseError(response.RootElement, out _), StringComparison.Ordinal);
    }
}
