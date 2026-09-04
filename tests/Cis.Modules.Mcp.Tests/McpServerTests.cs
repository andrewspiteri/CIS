using System.Text.Json;
using Cis.Modules.Mcp;

namespace Cis.Modules.Mcp.Tests;

public sealed class McpServerTests
{
    [Fact]
    public void LegacyInitializeAndReadOnlyToolCall_ReturnStableJsonRpcResults()
    {
        var input = new StringReader(string.Join('\n',
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"cis_repository_status\",\"arguments\":{}}}"));
        var output = new StringWriter();
        var server = new McpStdioServer(new FakeTools());

        Assert.Equal(0, server.Run("C:/repo", false, input, output));

        var responses = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        Assert.Equal(3, responses.Length);
        Assert.Equal(McpStdioServer.ProtocolVersion, responses[0].RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        var listed = responses[1].RootElement.GetProperty("result").GetProperty("tools");
        Assert.Single(listed.EnumerateArray());
        Assert.True(listed[0].GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.False(responses[2].RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("C:/repo", responses[2].RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("repository").GetString());
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public void MutationTools_AreHiddenUnlessEnabledAndStillRequireConfirmation()
    {
        var server = new McpStdioServer(new FakeTools());
        var hiddenOutput = new StringWriter();
        server.Run("C:/repo", false, new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}"), hiddenOutput);
        Assert.DoesNotContain("cis_mutate", hiddenOutput.ToString(), StringComparison.Ordinal);

        var enabledOutput = new StringWriter();
        server.Run("C:/repo", true, new StringReader(string.Join('\n',
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"cis_mutate\",\"arguments\":{\"confirm\":false}}}")), enabledOutput);
        Assert.Contains("cis_mutate", enabledOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("confirmation-required", enabledOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverProbe_ReturnsSupportedLegacyVersionsForSafeClientFallback()
    {
        var output = new StringWriter();
        new McpStdioServer(new FakeTools()).Run("C:/repo", false, new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"server/discover\",\"params\":{}}"), output);
        using var response = JsonDocument.Parse(output.ToString());
        Assert.Equal(-32022, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("2025-11-25", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_NegotiatesASupportedLegacyVersionAndRejectsUnknownVersions()
    {
        var output = new StringWriter();
        new McpStdioServer(new FakeTools()).Run("C:/repo", false, new StringReader(string.Join('\n',
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"unknown\"}}")), output);
        var responses = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();

        Assert.Equal("2024-11-05", responses[0].RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal(-32022, responses[1].RootElement.GetProperty("error").GetProperty("code").GetInt32());
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public void OversizedOrInvalidRequests_FailClosedAndTheNextRequestStillRuns()
    {
        var oversized = new string('x', McpStdioServer.MaximumMessageCharacters + 1);
        var input = new StringReader(string.Join('\n', oversized, "[]",
            "{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"ping\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}"));
        var output = new StringWriter();

        Assert.Equal(0, new McpStdioServer(new FakeTools()).Run("C:/repo", false, input, output));

        var responses = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToArray();
        Assert.Equal(4, responses.Length);
        Assert.Contains("exceeds", responses[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invalid Request", responses[1], StringComparison.Ordinal);
        Assert.Contains("Invalid Request", responses[2], StringComparison.Ordinal);
        Assert.Contains("\"result\"", responses[3], StringComparison.Ordinal);
    }

    [Fact]
    public void InternalErrors_DoNotExposeExceptionDetailsToTheProtocolClient()
    {
        var output = new StringWriter();
        new McpStdioServer(new ThrowingTools()).Run("C:/repo", false, new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"cis_throw\",\"arguments\":{}}}"), output);

        Assert.Contains("Internal error", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-local-path", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeTools : ICisMcpToolService
    {
        private static readonly JsonElement EmptySchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
        public IReadOnlyList<McpToolDefinition> ListTools(bool allowMutations)
        {
            var tools = new List<McpToolDefinition> { new("cis_repository_status", "read", EmptySchema, false) };
            if (allowMutations) tools.Add(new("cis_mutate", "mutate", EmptySchema, true));
            return tools;
        }

        public McpToolOutcome Call(string repositoryPath, bool allowMutations, string name, JsonElement arguments)
        {
            if (name == "cis_mutate" && (!allowMutations || !arguments.TryGetProperty("confirm", out var confirm) || !confirm.GetBoolean()))
                return new(new { status = "confirmation-required" }, true);
            return new(new { status = "resolved", repository = repositoryPath });
        }
    }

    private sealed class ThrowingTools : ICisMcpToolService
    {
        private static readonly JsonElement EmptySchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
        public IReadOnlyList<McpToolDefinition> ListTools(bool allowMutations) => [new("cis_throw", "throw", EmptySchema, false)];
        public McpToolOutcome Call(string repositoryPath, bool allowMutations, string name, JsonElement arguments) =>
            throw new InvalidOperationException("sensitive-local-path");
    }
}
