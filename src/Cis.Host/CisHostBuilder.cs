using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace Cis.Host;

[SuppressMessage("Maintainability", "CA1515", Justification = "The builder is the supported composition surface for external module hosts and integration tests.")]
public sealed class CisHostBuilder
{
    private readonly List<ICisModule> _modules = [];

    public CisHostBuilder AddModule(ICisModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (string.IsNullOrWhiteSpace(module.Name))
        {
            throw new ArgumentException("A CIS module must have a non-empty name.", nameof(module));
        }

        if (_modules.Any(existing =>
            string.Equals(existing.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"The CIS module '{module.Name}' is already registered.");
        }

        _modules.Add(module);
        return this;
    }

    public CisApplication Build()
    {
        var services = new ServiceCollection();
        var catalog = new CisModuleCatalog(_modules);
        services.AddSingleton<ICisModuleCatalog>(catalog);
        var rootCommand = new RootCommand("cis - Change Impact Studio modular engineering workflow.");
        services.AddSingleton<ICisCommandDispatcher>(new CisCommandDispatcher(rootCommand));

        foreach (var module in _modules)
        {
            module.RegisterServices(services);
        }

        var provider = services.BuildServiceProvider(validateScopes: true);
        var registry = new CisCommandRegistry(rootCommand);

        foreach (var module in _modules)
        {
            module.RegisterCommands(registry, provider);
        }

        return new CisApplication(rootCommand, provider);
    }
}
