using System.Diagnostics;
using Cis.Abstractions;
using Cis.Providers.Agent.Codex;
using Cis.Providers.Agent.Claude;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClaudePermissionDenialsCannotReportSuccessfulExecution(bool denied)
    {
        string? session = null;
        var summary = new System.Text.StringBuilder();
        long? input = null, output = null;
        decimal? cost = null;
        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result", subtype = "success", is_error = false, result = "Source was inspected.",
            permission_denials = denied ? new[] { new { tool_name = "PowerShell" } } : [],
        });
        var parsed = ClaudeAgentProvider.ParseEvent(line, ref session, summary, ref input, ref output, ref cost);
        var completion = ClaudeAgentProvider.ClassifyCompletion(new(0, false, false, false, 1, ""),
            false, null, parsed.Kind == "provider-permission-denied");
        Assert.Equal(denied ? CisAgentRunStates.Failed : CisAgentRunStates.Succeeded, completion.Status);
        Assert.Equal(denied ? "permission-required" : null, completion.FailureKind);
        Assert.Equal("Source was inspected.", summary.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexJsonStreamRejectsMalformedEventsAfterValidCompletion(bool malformed)
    {
        var windows = OperatingSystem.IsWindows();
        var start = CodexAgentProvider.CreateStartInfo(windows ? "powershell.exe" : "/bin/sh", Environment.CurrentDirectory);
        const string completed = """{"type":"item.completed","item":{"type":"agent_message","text":"structured completion"}}""";
        string Write(string line) => windows ? "[Console]::WriteLine('" + line + "')" : "printf '%s\\n' '" + line + "'";
        var script = (windows ? "[Console]::ReadLine() | Out-Null" : "IFS= read -r request") + "\n" + Write(completed);
        if (malformed) script += "\n" + Write("malformed-json");
        if (windows) { start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command"); }
        else start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        var result = CodexAgentProvider.ExecuteJson(Request(Environment.CurrentDirectory, false) with { Prompt = "test" },
            _ => { }, TestContext.Current.CancellationToken, start);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(malformed ? CisAgentRunStates.InvalidEvidence : CisAgentRunStates.Succeeded, result.Status);
        Assert.Equal("structured completion", result.Summary);
        if (malformed) Assert.Equal("invalid-provider-event", result.FailureKind);
    }

    [Theory]
    [InlineData(CisAgentRunModes.Review, false)]
    [InlineData(CisAgentRunModes.Implement, true)]
    public void ClaudeOnlyResumesPersistedSessions(string mode, bool resumable)
    {
        var start = new ProcessStartInfo("claude");
        ClaudeAgentProvider.ConfigureExecutionArguments(start, Request(Environment.CurrentDirectory, false) with
        { Mode = mode, ResumeSessionId = "previous-session" });
        Assert.Equal(resumable, start.ArgumentList.Contains("--resume"));
        Assert.Equal(!resumable, start.ArgumentList.Contains("--no-session-persistence"));
    }
}
