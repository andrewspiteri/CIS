using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Diagnostics;

public sealed class DiagnosticsModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "diagnostics";
    public string Description => "Inspect bounded redacted runtime evidence and persist deterministic analysis.";
    public void RegisterServices(IServiceCollection services) => services.AddSingleton<DiagnosticsService>();
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider provider)
    {
        var service = provider.GetRequiredService<DiagnosticsService>(); var root = new Command(Name, Description);
        root.Subcommands.Add(Simple("sources", "List configured evidence sources.", service.Sources));
        root.Subcommands.Add(Simple("doctor", "Validate diagnostics profiles and source readiness.", service.Doctor));
        root.Subcommands.Add(Simple("summary", "Summarise bounded evidence.", service.Summary));
        root.Subcommands.Add(Events(service)); root.Subcommands.Add(Tail(service));
        root.Subcommands.Add(Simple("analyse", "Persist deterministic diagnostic analysis.", service.Analyse));
        root.Subcommands.Add(Simple("export", "Write a bounded redacted normalized JSONL export.", service.Export)); commands.Add(root);
    }
    private static Command Events(DiagnosticsService service)
    {
        var command = new Command("events", "List bounded normalized events."); var source = new Option<string?>("--source");
        var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 500 }; var level = new Option<string?>("--level");
        var contains = new Option<string?>("--contains"); var since = new Option<int?>("--since-minutes");
        return Custom(command, [source, limit, level, contains, since], (parse, repo) => service.Events(parse.GetValue(repo)!, parse.GetValue(source), parse.GetValue(limit), parse.GetValue(level), parse.GetValue(contains), parse.GetValue(since)));
    }
    private static Command Tail(DiagnosticsService service)
    {
        var command = new Command("tail", "Read the latest bounded lines from one source; this command does not wait.");
        var source = new Argument<string>("source"); var lines = new Option<int>("--lines") { DefaultValueFactory = _ => 100 };
        command.Arguments.Add(source); return Custom(command, [lines], (parse, repo) => service.Tail(parse.GetValue(repo)!, parse.GetValue(source)!, parse.GetValue(lines)));
    }
    private static Command Simple(string name, string description, Func<string, DiagnosticsResult> action)
        => Custom(new Command(name, description), [], (parse, repo) => action(parse.GetValue(repo)!));
    private static Command Custom(Command command, IEnumerable<Option> options, Func<ParseResult, Option<string>, DiagnosticsResult> action)
    {
        var repo = Repo(); var format = Format(); foreach (var option in options) command.Options.Add(option); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse, repo), parse.GetValue(format)!)); return command;
    }
    private static int Render(DiagnosticsResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent") { Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};sources={result.Sources.Count};events={result.Events.Count};applied={result.Applied.ToString().ToLowerInvariant()}"); foreach (var finding in result.Errors) Console.WriteLine($"diagnostic={finding.Replace(';', ',')}"); }
        else if (format == "human") { Console.WriteLine($"Diagnostics: {result.Status}; sources={result.Sources.Count}; events={result.Events.Count}"); foreach (var item in result.Events) Console.WriteLine($"{item.Severity.ToUpperInvariant()} {item.Source}: {item.Message}"); foreach (var finding in result.Errors) Console.WriteLine(finding); }
        else return 2; return result.ExitCode;
    }
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
}
