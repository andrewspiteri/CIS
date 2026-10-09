using System.Diagnostics;
using Cis.Abstractions;
using Cis.Host;
using Microsoft.Extensions.DependencyInjection;
using Cis.Modules.Repository;
using Cis.Modules.Agent;
using Cis.Providers.Agent.Claude;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void ExplicitCommandsCliAcceptsRepeatedOptionsAndRecordsEach()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.CommitAll("eligible task");
        var provider = new FakeProvider(explicitCommands: true);
        using var app = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new CommandPermissionProviderModule(provider)).Build();
        var exit = app.Invoke(["agent", "run", "CIS-0001", "WORK-090", "--provider", "fake",
            "--mode", "implement", "--permission", "workspace-write", "--actor", "Reviewer",
            "--repo", repository.Path, "--allow-command", "dotnet build -c Release",
            "--allow-command", "dotnet bin/Release/net10.0/IntervalDemo.dll --help"]);
        Assert.Equal(0, exit);
        Assert.Equal(["dotnet build -c Release", "dotnet bin/Release/net10.0/IntervalDemo.dll --help"], provider.LastRequest!.AllowedCommands);
        Assert.Equal(1, app.Invoke(["agent", "run", "CIS-0001", "WORK-090", "--provider", "fake",
            "--mode", "implement", "--permission", "workspace-write", "--actor", "Reviewer", "--allow-command"]));
    }

    private sealed class CommandPermissionProviderModule(FakeProvider provider) : ICisModule
    {
        public string Name => "command-permission-fixture";
        public string Description => "Native CLI permission contract fixture.";
        public void RegisterServices(IServiceCollection services) => services.AddSingleton<ICisAgentProvider>(provider);
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }

    [Theory]
    [InlineData("dotnet *")]
    [InlineData("dotnet build; whoami")]
    [InlineData("dotnet build && whoami")]
    [InlineData("dotnet build$(whoami)")]
    [InlineData("dotnet build\nwhoami")]
    [InlineData("dotnet build > output")]
    [InlineData("dotnet build),Bash(*)")]
    [InlineData("dotnet")]
    [InlineData(" dotnet build")]
    [InlineData("dotnet  build")]
    [InlineData("dotnet build `whoami`")]
    [InlineData("dotnet build \"project.csproj\"")]
    public void ExplicitCommandsRejectUnboundedOrCompoundRules(string command)
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var provider = new FakeProvider(explicitCommands: true);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);
        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Implement,
            CisAgentPermissions.WorkspaceWrite, null, null, 60, false, "Reviewer",
            TestContext.Current.CancellationToken, allowedCommands: [command]);
        Assert.Equal("blocked", result.Status);
        Assert.Equal(0, provider.ExecuteCalls);
        Assert.Null(result.Run);
        Assert.Contains(result.Diagnostics, item => item.Contains("Explicit commands", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, CisAgentRunModes.Implement, CisAgentPermissions.WorkspaceWrite)]
    [InlineData(true, CisAgentRunModes.Review, CisAgentPermissions.ReadOnly)]
    [InlineData(true, CisAgentRunModes.Implement, CisAgentPermissions.ReadOnly)]
    [InlineData(true, CisAgentRunModes.Plan, CisAgentPermissions.WorkspaceWrite)]
    public void ExplicitCommandsRequireCapabilityAndImplementationCeiling(bool supported, string mode, string permission)
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        var provider = new FakeProvider(explicitCommands: supported);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);
        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", mode, permission,
            null, null, 60, false, "Reviewer", TestContext.Current.CancellationToken, allowedCommands: ["dotnet build -c Release"]);
        Assert.Equal("blocked", result.Status);
        Assert.Equal(0, provider.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCommandsAreAuditedAndResumeRequiresFreshAuthorization(bool reauthorize)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.CommitAll("eligible task");
        var provider = new FakeProvider(explicitCommands: true);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);
        string[] commands = ["dotnet build -c Release", "dotnet bin/Release/net10.0/IntervalDemo.dll --help"];
        var first = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Implement,
            CisAgentPermissions.WorkspaceWrite, null, null, 60, false, "Reviewer",
            TestContext.Current.CancellationToken, allowedCommands: commands);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(commands, provider.LastRequest!.AllowedCommands);
        Assert.Equal(commands, first.Run!.Manifest.AllowedCommands);
        Assert.All(first.Run.Permissions, item =>
        {
            Assert.Equal("Reviewer", item.Actor);
            Assert.Equal(1, item.Attempt);
            Assert.Equal("explicit-command", item.Capability);
            Assert.Equal("accepted-within-ceiling", item.Decision);
        });
        Assert.Equal(commands, first.Run.Permissions.Select(item => item.Target));
        var resumed = service.Resume(repository.Path, first.Run.Manifest.RunId, "Continue", "Second reviewer", "Recheck", false,
            TestContext.Current.CancellationToken, allowedCommands: reauthorize ? commands : null);
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(reauthorize ? commands : [], provider.LastRequest!.AllowedCommands);
        Assert.Null(provider.LastRequest.ResumeSessionId);
        Assert.Equal(reauthorize ? 4 : 2, resumed.Run!.Permissions.Count);
        Assert.All(resumed.Run.Permissions.Where(item => item.Attempt == 2), item => Assert.Equal("Second reviewer", item.Actor));
        Assert.All(resumed.Run.Artifacts, item => Assert.True(item.Valid));
    }

    [Fact]
    public void ClaudeExplicitCommandRulesDoNotGrantBroadShellAccessOrResumeNativePermissions()
    {
        var start = new ProcessStartInfo("claude");
        ClaudeAgentProvider.ConfigureExecutionArguments(start, Request(Environment.CurrentDirectory, false) with
        { AllowedCommands = ["dotnet build -c Release"], ResumeSessionId = "old-permissions" });
        var rules = start.ArgumentList[start.ArgumentList.IndexOf("--allowedTools") + 1];
        Assert.Equal("Bash(dotnet build -c Release),PowerShell(dotnet build -c Release)", rules);
        Assert.DoesNotContain("--resume", start.ArgumentList);
        Assert.DoesNotContain("--dangerously-skip-permissions", start.ArgumentList);
        Assert.Throws<ArgumentException>(() => ClaudeAgentProvider.ConfigureExecutionArguments(new("claude"),
            Request(Environment.CurrentDirectory, false) with { AllowedCommands = ["dotnet *"] }));
    }
}
