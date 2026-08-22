using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Host;

public sealed class HostModule : ICisModule
{
    public string Name => "host";

    public string Description => "Inspect the CIS host and explicitly loaded modules.";

    public void RegisterServices(IServiceCollection services)
    {
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var catalog = services.GetRequiredService<ICisModuleCatalog>();
        var host = new Command(Name, Description);
        var modules = new Command("modules", "List explicitly loaded CIS modules.");
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };

        modules.Options.Add(format);
        modules.SetAction(parseResult =>
        {
            var selectedFormat = parseResult.GetValue(format) ?? "human";
            return RenderModules(catalog.Modules, selectedFormat);
        });

        host.Subcommands.Add(modules);
        commands.Add(host);
    }

    private static int RenderModules(IReadOnlyList<CisModuleDescriptor> modules, string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "human":
                foreach (var module in modules)
                {
                    Console.WriteLine($"{module.Name} {module.AssemblyVersion} - {module.Description}");
                }

                return 0;

            case "json":
                Console.WriteLine(JsonSerializer.Serialize(
                    new { modules },
                    new JsonSerializerOptions { WriteIndented = true }));
                return 0;

            case "agent":
                foreach (var module in modules)
                {
                    Console.WriteLine($"module={module.Name};assembly={module.AssemblyName};version={module.AssemblyVersion}");
                }

                return 0;

            default:
                Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent.");
                return 2;
        }
    }
}
