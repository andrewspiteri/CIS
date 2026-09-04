using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Frontend;

public sealed class FrontendModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "frontend";
    public string Description => "Discover and validate provider-neutral frontend screens, routes, components, navigation, state, and API calls.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisFrontendContextProvider, ReactNextFrontendProvider>();
        services.AddSingleton<ICisFrontendContextProvider, AngularFrontendProvider>();
        services.AddSingleton<ICisFrontendContextProvider, VueFrontendProvider>();
        services.AddSingleton<ICisFrontendContextProvider, SwiftUiFrontendProvider>();
        services.AddSingleton<ICisFrontendContextProvider, ComposeFrontendProvider>();
        services.AddSingleton<ICisFrontendContextProvider, GodotFrontendProvider>();
        services.AddSingleton<FrontendContextService>();
        services.AddSingleton<ICisGraphAugmenter>(provider => provider.GetRequiredService<FrontendContextService>());
        services.AddSingleton<ICisRepositoryDoctorCheck, FrontendDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<FrontendContextService>();
        var frontend = new Command(Name, Description);
        frontend.Subcommands.Add(Simple("discover", "Discover frontend context and write normalized derived state.", service.Discover));
        var inventory = new Command("inventory", "Read normalized frontend observations."); var invRepo = Repo(); var framework = new Option<string?>("--framework"); var kind = new Option<string?>("--kind"); var invFormat = Format();
        inventory.Options.Add(invRepo); inventory.Options.Add(framework); inventory.Options.Add(kind); inventory.Options.Add(invFormat);
        inventory.SetAction(parse => Render(service.Inventory(parse.GetValue(invRepo)!, parse.GetValue(framework), parse.GetValue(kind)), parse.GetValue(invFormat)!));
        frontend.Subcommands.Add(inventory);
        var validate = new Command("validate", "Validate provider conflicts, source evidence, route collisions, and canonical route drift.");
        var valRepo = Repo(); var strict = new Option<bool>("--strict"); var noRefresh = new Option<bool>("--no-refresh"); var valFormat = Format();
        validate.Options.Add(valRepo); validate.Options.Add(strict); validate.Options.Add(noRefresh); validate.Options.Add(valFormat);
        validate.SetAction(parse => Render(service.Validate(parse.GetValue(valRepo)!, parse.GetValue(strict), !parse.GetValue(noRefresh)), parse.GetValue(valFormat)!));
        frontend.Subcommands.Add(validate);
        commands.Add(frontend);
    }

    private static Command Simple(string name, string description, Func<string, FrontendContextResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var format = Format(); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!), parse.GetValue(format)!)); return command;
    }

    private static int Render(FrontendContextResult result, string format)
    {
        var selected = format.Trim().ToLowerInvariant();
        if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (selected == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};providers={result.Providers.Count};observations={result.Observations.Count};screens={result.Observations.Count(item => item.Kind == "screen")};routes={result.Observations.Count(item => item.Kind == "route")};errors={result.Errors};warnings={result.Warnings};strict={result.Strict.ToString().ToLowerInvariant()}");
            foreach (var item in result.Observations) Console.WriteLine($"observation={Clean(item.Id)};provider={Clean(item.Provider)};framework={Clean(item.Framework)};kind={Clean(item.Kind)};name={Clean(item.Name)};route={Clean(item.Route ?? string.Empty)};target={Clean(item.Target ?? string.Empty)};source={Clean(item.SourcePath)};line={item.Line}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Code};severity={diagnostic.Severity};message={Clean(diagnostic.Message)};evidence={Clean(string.Join(',', diagnostic.Evidence))}");
        }
        else if (selected == "human")
        {
            Console.WriteLine($"Frontend context: {result.Status}; providers={result.Providers.Count}; observations={result.Observations.Count}; errors={result.Errors}; warnings={result.Warnings}");
            foreach (var item in result.Observations) Console.WriteLine($"- {item.Framework} {item.Kind}: {item.Name}; route={item.Route ?? "-"}; {item.SourcePath}:{item.Line}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
    private static string Clean(string value) => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
