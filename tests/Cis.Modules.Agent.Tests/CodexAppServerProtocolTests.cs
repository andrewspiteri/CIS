using System.Collections.Concurrent;
using System.Diagnostics;
using Cis.Abstractions;
using Cis.Providers.Agent.Codex;

namespace Cis.Modules.Agent.Tests;

public sealed class CodexAppServerProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReaderWaitsForAcknowledgedParentTurnDespiteInterleavedChildAndOldTurns(bool resume)
    {
        var events = new ConcurrentQueue<CisAgentProviderEvent>();
        var start = Server([
            """{"method":"turn/completed","params":{"threadId":"selected","turn":{"id":"previous","status":"failed"}}}""",
            """{"method":"item/completed","params":{"threadId":"selected","turnId":"current","item":{"type":"agentMessage","text":"parent result"}}}""",
            """{"id":2,"result":{"turn":{"id":"current"}}}""",
            """{"method":"item/completed","params":{"threadId":"child","turnId":"child-turn","item":{"type":"agentMessage","text":"child result"}}}""",
            """{"method":"turn/completed","params":{"threadId":"child","turn":{"id":"child-turn","status":"failed"}}}""",
            """{"method":"item/completed","params":{"threadId":"selected","turnId":"previous","item":{"type":"agentMessage","text":"stale result"}}}""",
            """{"method":"turn/completed","params":{"threadId":"selected","turn":{"id":"previous","status":"failed"}}}""",
            """{"method":"turn/completed","params":{"threadId":"selected","turn":{"id":"current","status":"completed"}}}""",
        ]);
        var result = CodexAgentProvider.ExecuteAppServer(Request(resume), events.Enqueue, TestContext.Current.CancellationToken, start);

        Assert.Equal(CisAgentRunStates.Succeeded, result.Status);
        Assert.Equal("selected", result.SessionId);
        Assert.Equal("parent result", result.Summary);
        Assert.Contains(events, item => item.RawJson?.Contains("child-turn", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ReaderReportsRejectedTurnStartWithoutWaitingForIdleExpiry()
    {
        var result = CodexAgentProvider.ExecuteAppServer(Request(false), _ => { }, TestContext.Current.CancellationToken,
            Server(["""{"id":2,"error":{"message":"Turn rejected"}}"""]));

        Assert.Equal(CisAgentRunStates.Failed, result.Status);
        Assert.Equal("provider-protocol", result.FailureKind);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Turn rejected", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"id\":1,\"result\":{\"thread\":{\"id\":\"replacement\"}}}")]
    [InlineData("{\"id\":2,\"result\":{\"turn\":{\"id\":\"replacement\"}}}")]
    [InlineData("{\"id\":2147483648,\"result\":{}}")]
    [InlineData("{\"id\":1.5,\"result\":{}}")]
    public void ReaderRejectsDuplicateAndUnsupportedResponseIds(string invalid)
    {
        var result = CodexAgentProvider.ExecuteAppServer(Request(false), _ => { }, TestContext.Current.CancellationToken,
            Server(["""{"id":2,"result":{"turn":{"id":"current"}}}""", invalid]));
        Assert.Equal(CisAgentRunStates.Failed, result.Status);
        Assert.Equal("provider-protocol", result.FailureKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReaderFailureIsReportedWithoutWaitingForIdleExpiry(bool accessDenied)
    {
        int? processId = null;
        var result = CodexAgentProvider.ExecuteAppServer(Request(false), item =>
        {
            if (item.Kind == "process-started") processId = item.ProcessId;
            if (item.ProviderEventType == "item/completed")
            {
                if (accessDenied) throw new UnauthorizedAccessException("Journal unavailable");
                throw new InvalidOperationException("Journal unavailable");
            }
        }, TestContext.Current.CancellationToken, Server([
            """{"id":2,"result":{"turn":{"id":"current"}}}""",
            """{"method":"item/completed","params":{"threadId":"selected","turnId":"current","item":{"type":"agentMessage","text":"result"}}}""",
        ]));
        Assert.Equal("provider-protocol", result.FailureKind);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Journal unavailable", StringComparison.Ordinal));
        Assert.NotNull(processId);
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            Assert.True(process.WaitForExit(5000), "Failed provider process was not terminated.");
        }
        catch (ArgumentException) { /* The process has already exited and its OS record was removed. */ }
    }

    [Fact]
    public void BufferOverflowRetainsEventsAndFailsOnce()
    {
        var events = new ConcurrentQueue<CisAgentProviderEvent>();
        const string message = """{"method":"item/completed","params":{"threadId":"selected","turnId":"current","item":{"type":"agentMessage","text":"buffered evidence"}}}""";
        var result = CodexAgentProvider.ExecuteAppServer(Request(false), events.Enqueue, TestContext.Current.CancellationToken,
            Server(Enumerable.Repeat(message, 129).ToArray()));
        Assert.Equal(CisAgentRunStates.InvalidEvidence, result.Status);
        Assert.Equal(129, events.Count(item => item.RawJson == message));
        Assert.Single(events, item => item.Kind == "invalid-provider-event");
        Assert.Empty(result.Summary);
    }

    private static CisAgentExecutionRequest Request(bool resume) => new("protocol-test", 1, "codex", "app-server",
        CisAgentRunModes.Implement, CisAgentPermissions.ReadOnly, Environment.CurrentDirectory, "test",
        TimeSpan.FromSeconds(20), resume ? "requested-resume" : null, false, "native-test",
        new Dictionary<string, string> { ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? "", ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "" });

    private static ProcessStartInfo Server(string[] notifications)
    {
        var windows = OperatingSystem.IsWindows();
        var start = CodexAgentProvider.CreateStartInfo(windows ? "powershell.exe" : "/bin/sh", Environment.CurrentDirectory);
        var read = windows ? "[Console]::ReadLine() | Out-Null" : "IFS= read -r request";
        string Write(string json) => windows ? "[Console]::WriteLine('" + json.Replace("'", "''", StringComparison.Ordinal) + "')"
            : "printf '%s\\n' '" + json.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        var commands = new List<string>
        {
            read, Write("""{"id":0,"result":{}}"""), read, read,
            Write("""{"id":1,"result":{"thread":{"id":"selected"}}}"""), read,
        };
        commands.AddRange(notifications.Select(Write));
        commands.Add(read);
        if (windows)
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
        }
        else start.ArgumentList.Add("-c");
        start.ArgumentList.Add(string.Join("\n", commands));
        return start;
    }
}
