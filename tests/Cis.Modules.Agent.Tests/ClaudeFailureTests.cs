using System.Text;
using Cis.Abstractions;
using Cis.Providers.Agent.Claude;

namespace Cis.Modules.Agent.Tests;

public sealed class ClaudeFailureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExpiredSessionInSuccessSubtypeIsAVisibleAuthenticationFailure(int exitCode)
    {
        string? session = null; var summary = new StringBuilder(); long? input = null, output = null; decimal? cost = null;
        var parsed = ClaudeAgentProvider.ParseEvent("""
            {"type":"result","subtype":"success","is_error":true,"result":"Failed to authenticate: OAuth session expired and could not be refreshed","usage":{"input_tokens":0,"output_tokens":0},"total_cost_usd":0}
            """, ref session, summary, ref input, ref output, ref cost);
        Assert.Equal("provider-error", parsed.Kind);
        Assert.Contains("session expired", parsed.Message);
        Assert.Contains("Authenticate Agent Provider", parsed.Message);
        Assert.DoesNotContain("completed the request", parsed.Message);
        var completion = ClaudeAgentProvider.ClassifyCompletion(new(exitCode, false, false, false, 1, ""), false, parsed.Message);
        Assert.Equal(CisAgentRunStates.Failed, completion.Status);
        Assert.Equal("authentication-required", completion.FailureKind);
        Assert.Equal(0, input);
    }

    [Fact]
    public void ErrorSubtypeWithoutResultStillRetainsRedactedDiagnostics()
    {
        string? session = null; var summary = new StringBuilder("partial output"); long? input = null, output = null; decimal? cost = null;
        var parsed = ClaudeAgentProvider.ParseEvent("""
            {"type":"result","subtype":"error_during_execution","errors":["Rejected token=secret-value"]}
            """, ref session, summary, ref input, ref output, ref cost);
        Assert.Equal("provider-error", parsed.Kind);
        Assert.Contains("Rejected", parsed.Message);
        Assert.DoesNotContain("secret-value", parsed.Message);
        Assert.DoesNotContain("secret-value", summary.ToString());
        Assert.DoesNotContain("partial output", summary.ToString());
        Assert.Equal("provider-failure", ClaudeAgentProvider.ClassifyCompletion(new(0, false, false, false, 1, ""), false, parsed.Message).FailureKind);
    }

    [Fact]
    public void NormalCompletionRemainsSuccessfulAndCancellationTakesPrecedence()
    {
        Assert.Equal(CisAgentRunStates.Succeeded, ClaudeAgentProvider.ClassifyCompletion(new(0, false, false, false, 1, ""), false, null).Status);
        Assert.Equal("cancellation", ClaudeAgentProvider.ClassifyCompletion(new(1, false, true, false, 1, ""), false, "OAuth failure").FailureKind);
    }
}
