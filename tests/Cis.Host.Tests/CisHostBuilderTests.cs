using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Host;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace Cis.Host.Tests;

public sealed class CisHostBuilderTests
{
    [Fact]
    public void AddModule_RejectsDuplicateModuleNames()
    {
        var builder = new CisHostBuilder().AddModule(new HostModule());

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddModule(new DuplicateHostModule()));

        Assert.Contains("already registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RegistersCommandsFromExplicitModules()
    {
        using var application = new CisHostBuilder()
            .AddModule(new TestModule())
            .Build();

        var exitCode = application.Invoke(["test", "ping"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void HostModules_RejectsUnsupportedOutputFormat()
    {
        using var application = new CisHostBuilder()
            .AddModule(new HostModule())
            .Build();

        var exitCode = application.Invoke(["host", "modules", "--format", "xml"]);

        Assert.Equal(2, exitCode);
    }

    private sealed class DuplicateHostModule : ICisModule
    {
        public string Name => "HOST";
        public string Description => "Duplicate.";
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }

    private sealed class TestModule : ICisModule
    {
        public string Name => "test";
        public string Description => "Test module.";
        public void RegisterServices(IServiceCollection services) { }

        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
        {
            var module = new Command(Name, Description);
            var ping = new Command("ping", "Return success.");
            ping.SetAction(_ => 0);
            module.Subcommands.Add(ping);
            commands.Add(module);
        }
    }
}
