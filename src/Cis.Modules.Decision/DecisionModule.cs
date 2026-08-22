using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Decision;

public sealed class DecisionModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "decision";

    public string Description => "Manage change-local decisions and promote durable resolutions into ADRs.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DecisionService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<DecisionService>();
        var decision = new Command(Name, Description);
        decision.Subcommands.Add(CreateList(service));
        decision.Subcommands.Add(CreateCreate(service));
        decision.Subcommands.Add(CreateResolve(service));
        decision.Subcommands.Add(CreateDefer(service));
        decision.Subcommands.Add(CreatePromote(service));
        commands.Add(decision);
    }

    private static Command CreateList(DecisionService service)
    {
        var command = new Command("list", "List decisions, gates, evidence, and lifecycle state.");
        var changeId = new Argument<string>("change-id");
        var status = new Option<string?>("--status") { Description = "Optional open, resolved, or deferred filter." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(changeId);
        command.Options.Add(status);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.List(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(status));
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateCreate(DecisionService service)
    {
        var command = new Command("create", "Record a change-local question, options, evidence, and approval gate.");
        var changeId = new Argument<string>("change-id");
        var question = new Option<string>("--question") { Required = true, Description = "Question requiring human resolution." };
        var category = new Option<string>("--category") { Required = true, Description = "Decision category." };
        var options = new Option<string[]>("--option")
        {
            Required = true,
            Description = "Candidate option. Supply at least two times.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var advisory = new Option<bool>("--advisory") { Description = "Record a non-blocking advisory decision. Decisions block by default." };
        var requiredBefore = new Option<string>("--required-before")
        {
            DefaultValueFactory = _ => "plan approval",
            Description = "Gate by which a blocking decision must be resolved.",
        };
        var evidence = new Option<string>("--evidence") { Required = true, Description = "Evidence supporting the question and options." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(changeId);
        command.Options.Add(question);
        command.Options.Add(category);
        command.Options.Add(options);
        command.Options.Add(advisory);
        command.Options.Add(requiredBefore);
        command.Options.Add(evidence);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Create(new DecisionCreateRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(question) ?? string.Empty,
                parseResult.GetValue(category) ?? string.Empty,
                parseResult.GetValue(options) ?? [],
                !parseResult.GetValue(advisory),
                parseResult.GetValue(requiredBefore) ?? "plan approval",
                parseResult.GetValue(evidence) ?? string.Empty));
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateResolve(DecisionService service)
    {
        var command = new Command("resolve", "Record the human-selected option and rationale.");
        var changeId = new Argument<string>("change-id");
        var decisionId = new Argument<string>("decision-id");
        var option = new Option<string>("--option") { Required = true, Description = "One exact recorded option." };
        var rationale = new Option<string>("--rationale") { Required = true, Description = "Human resolution rationale." };
        var evidence = new Option<string?>("--evidence") { Description = "Optional additional resolution evidence." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(changeId);
        command.Arguments.Add(decisionId);
        command.Options.Add(option);
        command.Options.Add(rationale);
        command.Options.Add(evidence);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Resolve(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(decisionId) ?? string.Empty,
                parseResult.GetValue(option) ?? string.Empty,
                parseResult.GetValue(rationale) ?? string.Empty,
                parseResult.GetValue(evidence));
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateDefer(DecisionService service)
    {
        var command = new Command("defer", "Record explicit human deferral and its remaining gate effect.");
        var changeId = new Argument<string>("change-id");
        var decisionId = new Argument<string>("decision-id");
        var reason = new Option<string>("--reason") { Required = true, Description = "Human deferral rationale." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(changeId);
        command.Arguments.Add(decisionId);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Defer(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(decisionId) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty);
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreatePromote(DecisionService service)
    {
        var command = new Command("promote", "Promote a resolved durable decision into a catalogued ADR.");
        var changeId = new Argument<string>("change-id");
        var decisionId = new Argument<string>("decision-id");
        var title = new Option<string?>("--title") { Description = "Optional ADR title; defaults to the decision question." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(changeId);
        command.Arguments.Add(decisionId);
        command.Options.Add(title);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Promote(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(decisionId) ?? string.Empty,
                parseResult.GetValue(title));
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> RepositoryOption() => new("--repo")
    {
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        Description = "Repository path.",
    };

    private static Option<string> FormatOption() => new("--format")
    {
        DefaultValueFactory = _ => "human",
        Description = "Output format: human, json, or agent.",
    };

    private static string GetFormat(string? value) => value is "json" or "agent" ? value : "human";

    private static void Render(DecisionResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};changeId={result.ChangeId};decisions={result.Decisions.Count};applied={result.Applied.ToString().ToLowerInvariant()};exitCode={result.ExitCode}");
            foreach (var item in result.Decisions)
            {
                Console.WriteLine($"decision={item.Id};status={item.Status};category={item.Category};blocking={item.Blocking.ToString().ToLowerInvariant()};requiredBefore={Clean(item.RequiredBefore)};resolution={Clean(item.Resolution)};adr={Clean(item.PromotedAdr)};question={Clean(item.Question)}");
                Console.WriteLine($"options.{item.Id}={string.Join(';', item.Options.Select(Clean))}");
                Console.WriteLine($"evidence.{item.Id}={string.Join(';', item.Evidence.Select(Clean))}");
            }

            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={Clean(error)}");
            }

            return;
        }

        Console.WriteLine($"Status: {result.Status}");
        foreach (var item in result.Decisions)
        {
            Console.WriteLine($"{item.Id} [{item.Status}] {(item.Blocking ? "blocking" : "advisory")} {item.Question}");
            Console.WriteLine($"  Options: {string.Join(", ", item.Options)}");
            if (item.Resolution.Length > 0)
            {
                Console.WriteLine($"  Resolution: {item.Resolution} - {item.Rationale}");
            }
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"ERROR: {error}");
        }
    }

    private static string Clean(string value)
        => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
