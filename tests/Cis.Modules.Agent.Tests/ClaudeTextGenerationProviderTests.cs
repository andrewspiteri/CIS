using System.Text.Json;
using Cis.Abstractions;
using Cis.Providers.Agent.Claude;
using Cis.Providers.Agent.Codex;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Agent.Tests;

public sealed class ClaudeTextGenerationProviderTests
{
    [Fact]
    public void AccountProvidersAreRegisteredOnceForGeneralTextGeneration()
    {
        var services = new ServiceCollection();
        new ClaudeAgentProviderModule().RegisterServices(services);
        new CodexAgentProviderModule().RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(new[] { "claude", "codex" }, provider.GetServices<ICisAiProvider>().Select(item => item.Name));
        // Repository import delegates to the general service, avoiding duplicate provider-name conflicts.
        Assert.Empty(provider.GetServices<ICisRepositoryGuidanceProvider>());
        Assert.Equal(2, provider.GetServices<ICisAgentProvider>().Count());
    }

    [Fact]
    public void RemoteAuthorizationIsRequiredBeforeLaunchingAnyProcess()
    {
        var result = new ClaudeTextGenerationProvider("nonexistent-claude").Generate(new("private BRD"), "default");
        Assert.Equal("remote-approval-required", result.Status);
        Assert.False(result.IsLocal);
    }

    [Fact]
    public void ModelChoicesUseInstalledCliAliasesAndAlwaysOfferItsDefault()
    {
        const string help = """
              --model <model>  Model for the session. Provide an alias for the latest model (e.g.
                              'new-alias', 'opus', or 'sonnet') or a model's full name (e.g.
                              'specific-version').
              --name <name>   Name.
            """;
        Assert.Equal(new[] { "default", "new-alias", "opus", "sonnet" }, ClaudeTextGenerationProvider.ReadModels(help).Select(item => item.Name));
        Assert.Equal("default", Assert.Single(ClaudeTextGenerationProvider.ReadModels("no catalogue")).Name);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("explicit-model")]
    public void TextGenerationDisablesToolsAndCustomizationButPreservesAccountAuthentication(string model)
    {
        var start = ClaudeTextGenerationProvider.CreateStartInfo("claude", Path.GetTempPath(), model, "{}");
        var args = start.ArgumentList.ToArray();
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal("utf-8", start.StandardInputEncoding!.WebName);
        Assert.Contains("--safe-mode", args);
        Assert.Contains("--restricted", args);
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--disable-slash-commands", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.DoesNotContain("--bare", args); // Bare mode disables account OAuth.
        Assert.Equal("", args[Array.IndexOf(args, "--tools") + 1]);
        Assert.Equal("dontAsk", args[Array.IndexOf(args, "--permission-mode") + 1]);
        Assert.Equal(model != "default", args.Contains("--model"));
        if (model != "default") Assert.Equal(model, args[Array.IndexOf(args, "--model") + 1]);
        Assert.Equal("{}", args[Array.IndexOf(args, "--json-schema") + 1]);
    }

    [Theory]
    [InlineData("text", false)]
    [InlineData("thinking", false)]
    [InlineData("tool_use", true)]
    [InlineData("server_tool_use", true)]
    [InlineData("tool_result", true)]
    public void ToolOutputIsRejected(string type, bool rejected) => Assert.Equal(rejected,
        ClaudeTextGenerationProvider.IsToolEvent(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type } } } })));

    [Theory]
    [InlineData("success", false, true)]
    [InlineData("success", true, false)]
    [InlineData("error_max_turns", false, false)]
    public void IncompleteOrErrorResultsCannotBecomeProposals(string subtype, bool isError, bool expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { subtype, is_error = isError }));
        Assert.Equal(expected, ClaudeTextGenerationProvider.IsSuccessfulResult(document.RootElement));
    }
}
