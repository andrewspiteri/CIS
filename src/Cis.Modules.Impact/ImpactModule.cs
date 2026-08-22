using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Impact;

public sealed class ImpactModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "impact";

    public string Description => "Discover, review, and assess evidence-backed change impacts.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ImpactAnalysisService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<ImpactAnalysisService>();
        var impact = new Command(Name, Description);
        impact.Subcommands.Add(CreateAnalyse(service));
        impact.Subcommands.Add(CreateFindings(service));
        impact.Subcommands.Add(CreateDisposition("accept", "accepted", service));
        impact.Subcommands.Add(CreateDisposition("reject", "rejected", service));
        impact.Subcommands.Add(CreateDisposition("defer", "deferred", service));
        impact.Subcommands.Add(CreateCompleteness(service));
        commands.Add(impact);
    }

    private static Command CreateAnalyse(ImpactAnalysisService service)
    {
        var command = new Command("analyse", "Discover deterministic impact proposals from exact graph roots.");
        var id = new Argument<string>("change-id");
        var roots = new Option<string[]>("--root")
        {
            Description = "Override or supply an impact root as <node-id>[#<kind>]. Repeat as needed.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var depth = new Option<int>("--depth") { DefaultValueFactory = _ => 2, Description = "Graph depth from 1 to 10." };
        var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 200, Description = "Maximum nodes per root from 1 to 1000." };
        var includeProposed = new Option<bool>("--include-proposed") { Description = "Include proposed graph relationships." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        command.Options.Add(roots);
        command.Options.Add(depth);
        command.Options.Add(limit);
        command.Options.Add(includeProposed);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Analyse(new ImpactAnalyseRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                (parseResult.GetValue(roots) ?? []).Select(ParseRoot).ToArray(),
                parseResult.GetValue(depth),
                parseResult.GetValue(limit),
                parseResult.GetValue(includeProposed)));
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateFindings(ImpactAnalysisService service)
    {
        var command = new Command("findings", "List current impact findings and dispositions.");
        var id = new Argument<string>("change-id");
        var state = new Option<string?>("--state") { Description = "Optional proposed, accepted, rejected, or deferred filter." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        command.Options.Add(state);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Findings(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(state));
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateDisposition(string commandName, string state, ImpactAnalysisService service)
    {
        var command = new Command(commandName, $"Record explicit human {state} disposition for one finding.");
        var changeId = new Argument<string>("change-id");
        var findingId = new Argument<string>("finding-id");
        var reason = new Option<string>("--reason") { Required = true, Description = "Required review rationale." };
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(changeId);
        command.Arguments.Add(findingId);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Disposition(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(changeId) ?? string.Empty,
                parseResult.GetValue(findingId) ?? string.Empty,
                state,
                parseResult.GetValue(reason) ?? string.Empty);
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateCompleteness(ImpactAnalysisService service)
    {
        var command = new Command("completeness", "Assess review completeness and readiness for planning.");
        var id = new Argument<string>("change-id");
        var repo = RepositoryOption();
        var format = FormatOption();
        command.Arguments.Add(id);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Completeness(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty);
            Render(result, GetFormat(parseResult.GetValue(format)));
            return result.ExitCode;
        });
        return command;
    }

    private static ChangeRoot ParseRoot(string value)
    {
        var separator = value.LastIndexOf('#');
        return separator > 0
            ? new ChangeRoot(value[..separator].Trim(), value[(separator + 1)..].Trim())
            : new ChangeRoot(value.Trim(), null);
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

    private static void Render(ImpactResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};changeId={result.ChangeId};findings={result.Findings.Count};applied={result.Applied.ToString().ToLowerInvariant()};exitCode={result.ExitCode}");
            foreach (var finding in result.Findings)
            {
                Console.WriteLine($"finding={finding.Id};state={finding.State};category={finding.Category};confidence={finding.Confidence};target={Clean(finding.Target)};label={Clean(finding.Label)}");
            }

            if (result.Completeness is { } completeness)
            {
                Console.WriteLine($"completeness=reviewComplete:{completeness.ReviewComplete.ToString().ToLowerInvariant()},planReady:{completeness.PlanReady.ToString().ToLowerInvariant()},proposed:{completeness.Proposed},accepted:{completeness.Accepted},deferred:{completeness.Deferred},truncated:{completeness.Truncated.ToString().ToLowerInvariant()}");
                foreach (var gap in completeness.Gaps)
                {
                    Console.WriteLine($"gap={Clean(gap)}");
                }
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                Console.WriteLine($"diagnostic={Clean(diagnostic)}");
            }

            return;
        }

        Console.WriteLine($"Status: {result.Status}");
        foreach (var finding in result.Findings)
        {
            Console.WriteLine($"{finding.Id} [{finding.State}] {finding.Category} - {finding.Label}");
            Console.WriteLine($"  {finding.Rationale}");
        }

        if (result.Completeness is { } summary)
        {
            Console.WriteLine($"Review complete: {summary.ReviewComplete}; plan ready: {summary.PlanReady}");
            foreach (var gap in summary.Gaps)
            {
                Console.WriteLine($"  GAP: {gap}");
            }
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine(diagnostic);
        }
    }

    private static string Clean(string value)
        => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
