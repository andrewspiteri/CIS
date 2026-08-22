using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Ai;

public sealed class AiModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "ai";

    public string Description => "Inspect provider-neutral model availability and routing policy.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IAiProvider, OllamaAiProvider>();
        services.AddSingleton<IAiProvider, OpenAiCompatibleProvider>();
        services.AddSingleton<ICisTextGenerationService>(provider =>
            new AiTextGenerationService(provider.GetServices<IAiProvider>()));
        services.AddSingleton<AiGovernanceService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<ICisTextGenerationService>();
        var ai = new Command(Name, Description);
        var status = new Command("status", "Report local and explicitly configured remote model providers.");
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        status.Options.Add(format);
        status.SetAction(parseResult =>
        {
            var result = service.GetStatus();
            var selected = (parseResult.GetValue(format) ?? "human").Trim().ToLowerInvariant();
            if (selected == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            }
            else if (selected == "agent")
            {
                Console.WriteLine($"status=reported;providers={result.Providers.Count};exitCode=0");
                foreach (var provider in result.Providers)
                {
                    Console.WriteLine(
                        $"provider={Normalize(provider.Name)};status={Normalize(provider.Status)};" +
                        $"local={provider.IsLocal.ToString().ToLowerInvariant()};models={Normalize(string.Join(',', provider.Models.Select(model => model.Name)))}");
                }
            }
            else if (selected == "human")
            {
                Console.WriteLine("CIS AI providers");
                foreach (var provider in result.Providers)
                {
                    Console.WriteLine($"- {provider.Name}: {provider.Status}; local={provider.IsLocal}; models={string.Join(", ", provider.Models.Select(model => model.Name))}");
                }
            }
            else
            {
                Console.Error.WriteLine("Unsupported format. Use human, json, or agent.");
                return 2;
            }

            return 0;
        });
        ai.Subcommands.Add(status);
        if (services.GetService<ICisRepositoryContextResolver>() is not null)
        {
            var governance = services.GetRequiredService<AiGovernanceService>();
            ai.Subcommands.Add(Report("providers", "List provider availability and configured routes.", governance.Providers));
            ai.Subcommands.Add(Report("models", "List selectable models by provider.", governance.Providers));
            ai.Subcommands.Add(Report("routes", "List repository-owned capability routes.", governance.Providers));
            ai.Subcommands.Add(Usage(governance));
            ai.Subcommands.Add(Evaluate(governance));
            var cache = new Command("cache", "Inspect model-response cache state.");
            cache.Subcommands.Add(Report("status", "Report disposable model-response cache entries.", governance.Providers));
            ai.Subcommands.Add(cache);
        }
        commands.Add(ai);
    }

    private static Command Report(string name, string description, Func<string, AiGovernanceResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var format = Format();
        command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!), parse.GetValue(format)!)); return command;
    }

    private static Command Usage(AiGovernanceService service)
    {
        var command = new Command("usage", "List sanitized AI usage without prompt or generated content.");
        var repo = Repo(); var format = Format(); var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 100 };
        command.Options.Add(repo); command.Options.Add(format); command.Options.Add(limit);
        command.SetAction(parse => Render(service.Usage(parse.GetValue(repo)!, parse.GetValue(limit)), parse.GetValue(format)!)); return command;
    }

    private static Command Evaluate(AiGovernanceService service)
    {
        var command = new Command("evaluate", "Run one governed capability route with optional disposable caching.");
        var capability = new Argument<string>("capability"); var prompt = new Option<string>("--prompt") { Required = true };
        var allow = new Option<bool>("--allow-remote"); var noCache = new Option<bool>("--no-cache"); var repo = Repo(); var format = Format();
        command.Arguments.Add(capability); command.Options.Add(prompt); command.Options.Add(allow); command.Options.Add(noCache); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(service.Evaluate(parse.GetValue(repo)!, parse.GetValue(capability)!, parse.GetValue(prompt)!, parse.GetValue(allow), !parse.GetValue(noCache)), parse.GetValue(format)!));
        return command;
    }

    private static int Render(AiGovernanceResult result, string format)
    {
        var selected = format.Trim().ToLowerInvariant();
        if (selected == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (selected == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};providers={result.Providers.Count};routes={result.Routes.Count};usage={result.Usage.Count};cacheEntries={result.CacheEntries}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Normalize(error)}");
        }
        else if (selected == "human")
        {
            Console.WriteLine($"AI governance: {result.Status}; providers={result.Providers.Count}; routes={result.Routes.Count}; cache={result.CacheEntries}");
            foreach (var route in result.Routes) Console.WriteLine($"- {route.Capability}: {route.Provider}/{route.Model}; remote={route.AllowRemote}; cache={route.Cache}");
            if (result.Generation?.Text is not null) Console.WriteLine(result.Generation.Text);
            foreach (var error in result.Errors) Console.WriteLine($"ERROR: {error}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }

    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };

    private static string Normalize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
}
