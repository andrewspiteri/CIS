using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Providers.Agent.Codex;

namespace Cis.Modules.Agent.Tests;

public sealed class CodexTextGenerationProviderTests
{
    [Fact]
    public void RemoteAuthorizationIsRequiredBeforeLaunchingAnyProcess()
    {
        var provider = new CodexTextGenerationProvider("nonexistent-codex", "nonexistent-cache");
        var result = provider.Generate(new("private guidance"), "requested-model");
        Assert.Equal("remote-approval-required", result.Status);
        Assert.False(result.IsLocal);
    }

    [Fact]
    public void TextOnlyArgumentsKeepModelExplicitAndDisableRepositoryExecution()
    {
        var directory = Directory.CreateTempSubdirectory("cis-text-test-").FullName;
        try
        {
            var start = CodexTextGenerationProvider.CreateStartInfo("codex", directory, "requested-model", "{\"type\":\"object\"}");
            Assert.False(start.UseShellExecute);
            Assert.True(start.CreateNoWindow);
            Assert.Equal("utf-8", start.StandardOutputEncoding!.WebName);
            Assert.Equal("utf-8", start.StandardErrorEncoding!.WebName);
            Assert.Equal("utf-8", start.StandardInputEncoding!.WebName);
            Assert.Equal(directory, start.WorkingDirectory);
            var args = start.ArgumentList.ToArray();
            Assert.Equal("requested-model", args[Array.IndexOf(args, "--model") + 1]);
            Assert.Contains("--ephemeral", args);
            Assert.Contains("--ignore-user-config", args);
            Assert.Contains("read-only", args);
            Assert.Contains("approval_policy=\"never\"", args);
            Assert.Contains("project_doc_max_bytes=0", args);
            Assert.Contains("features.shell_tool=false", args);
            Assert.Contains("features.multi_agent=false", args);
            Assert.Contains("features.apps=false", args);
            Assert.Equal("-", args[^1]);
            Assert.True(File.Exists(args[Array.IndexOf(args, "--output-schema") + 1]));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("agent_message", false)]
    [InlineData("reasoning", false)]
    [InlineData("error", false)]
    [InlineData("command_execution", true)]
    [InlineData("mcp_tool_call", true)]
    [InlineData("file_change", true)]
    [InlineData("web_search", true)]
    public void ToolEventsAreRejectedButErrorMessagesCanBeReported(string type, bool tool) =>
        Assert.Equal(tool, CodexTextGenerationProvider.IsToolEvent(JsonSerializer.Serialize(new { item = new { type } })));

    [Fact]
    public void OnlyVisibleModelsAreOfferedFromTheLocalCatalogue()
    {
        using var json = JsonDocument.Parse("""{"models":[{"slug":"strong-model","visibility":"list"},{"slug":"hidden","visibility":"hide"}]}""");
        Assert.Equal("strong-model", Assert.Single(CodexTextGenerationProvider.ReadModels(json.RootElement)).Name);
    }
}
