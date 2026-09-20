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

    [Fact]
    public void DispatcherPreservesStructuredFailuresAndRestoresOutputAfterExceptions()
    {
        var module = new CaptureProbeModule();
        using var application = new CisHostBuilder().AddModule(module).Build();
        var failed = module.Dispatcher!.Capture(["capture-probe", "blocked"]);
        Assert.Equal(5, failed.ExitCode);
        Assert.Equal("{\"status\":\"blocked\"}", failed.StandardOutput.Trim());
        Assert.Equal("existing finding", failed.StandardError.Trim());
        var thrown = module.Dispatcher.Capture(["capture-probe", "throw"]);
        Assert.Equal(1, thrown.ExitCode);
        Assert.Contains("InvalidOperationException", thrown.StandardError);
        Assert.DoesNotContain("sensitive exception detail", thrown.StandardError);
        Assert.Equal(failed, module.Dispatcher.Capture(["capture-probe", "blocked"]));
        Assert.NotEqual(0, module.Dispatcher.Capture(["not-loaded"]).ExitCode);
    }

    private sealed class CaptureProbeModule : ICisModule
    {
        public string Name => "capture-probe";
        public string Description => "Capture test.";
        public ICisCommandDispatcher? Dispatcher { get; private set; }
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
        {
            Dispatcher = services.GetRequiredService<ICisCommandDispatcher>();
            var command = new Command(Name);
            var blocked = new Command("blocked");
            blocked.SetAction(_ => { Console.WriteLine("{\"status\":\"blocked\"}"); Console.Error.WriteLine("existing finding"); return 5; });
            var throws = new Command("throw");
            throws.SetAction((Func<ParseResult, int>)(_ => throw new InvalidOperationException("sensitive exception detail")));
            command.Subcommands.Add(blocked);
            command.Subcommands.Add(throws);
            commands.Add(command);
        }
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
