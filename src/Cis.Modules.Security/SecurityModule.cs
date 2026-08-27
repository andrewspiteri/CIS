using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Security;

public sealed class SecurityModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "security";
    public string Description => "Inventory, normalize, govern, reconcile, and summarize security scanner evidence.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisSecurityResultAdapter, SarifSecurityResultAdapter>();
        services.AddSingleton<ICisSecurityResultAdapter, SemgrepJsonSecurityResultAdapter>();
        services.AddSingleton<ICisSecurityResultAdapter, GitleaksJsonSecurityResultAdapter>();
        services.AddSingleton<ICisSecurityResultAdapter, TrivyJsonSecurityResultAdapter>();
        services.AddSingleton<ICisSecurityResultAdapter, ZapJsonSecurityResultAdapter>();
        services.AddSingleton<SecurityService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<SecurityService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Simple("inventory", "Discover and persist security-suite inventory.", service.Inventory));
        root.Subcommands.Add(Validate(service));
        root.Subcommands.Add(Reconcile(service));
        root.Subcommands.Add(Status(service));
        root.Subcommands.Add(Summarise(service));
        var exceptions = new Command("exceptions", "Validate governed accepted security findings.");
        exceptions.Subcommands.Add(ExceptionValidate(service)); root.Subcommands.Add(exceptions);
        commands.Add(root);
    }

    private static Command Simple(string name, string description, Func<string, SecurityResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var format = Format(); command.Options.Add(repo); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!), parse.GetValue(format)!)); return command;
    }
    private static Command Validate(SecurityService service) => ValidationCommand("validate", "Validate security suites and accepted-finding governance.", service.Validate);
    private static Command ExceptionValidate(SecurityService service) => ValidationCommand("validate", "Validate accepted security findings only.", service.ValidateExceptions);
    private static Command ValidationCommand(string name, string description, Func<string, bool, SecurityResult> action)
    {
        var command = new Command(name, description); var repo = Repo(); var format = Format(); var strict = new Option<bool>("--strict"); command.Options.Add(repo); command.Options.Add(format); command.Options.Add(strict);
        command.SetAction(parse => Render(action(parse.GetValue(repo)!, parse.GetValue(strict)), parse.GetValue(format)!)); return command;
    }
    private static Command Reconcile(SecurityService service)
    {
        var command = new Command("reconcile", "Normalize one workflow run into governed security evidence."); var repo = Repo(); var format = Format(); var run = Required("--run"); command.Options.Add(repo); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Reconcile(parse.GetValue(repo)!, parse.GetValue(run)!), parse.GetValue(format)!)); return command;
    }
    private static Command Status(SecurityService service)
    {
        var command = new Command("status", "Report an exact or latest reconciled security run."); var repo = Repo(); var format = Format(); var run = new Option<string?>("--run"); command.Options.Add(repo); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Status(parse.GetValue(repo)!, parse.GetValue(run)), parse.GetValue(format)!)); return command;
    }
    private static Command Summarise(SecurityService service)
    {
        var command = new Command("summarise", "Write deterministic security triage with optional local-only AI augmentation."); var repo = Repo(); var format = Format(); var run = Required("--run"); var noLlm = new Option<bool>("--no-llm"); command.Options.Add(repo); command.Options.Add(format); command.Options.Add(run); command.Options.Add(noLlm);
        command.SetAction(parse => Render(service.Summarise(parse.GetValue(repo)!, parse.GetValue(run)!, parse.GetValue(noLlm)), parse.GetValue(format)!)); return command;
    }

    private static int Render(SecurityResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};runId={result.RunId ?? "none"};suites={result.Suites.Count};acceptances={result.Acceptances.Count};applied={result.Applied.ToString().ToLowerInvariant()}");
            foreach (var suite in result.Manifest?.Suites ?? []) Console.WriteLine($"suite={suite.SuiteId};category={suite.Category};tool={suite.Tool};status={suite.Status};critical={suite.Critical};high={suite.High};medium={suite.Medium};low={suite.Low};accepted={suite.Accepted}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine("diagnostic=" + diagnostic.Replace(';', ','));
        }
        else if (format == "human")
        {
            Console.WriteLine($"Security: {result.Status}; run={result.RunId ?? "none"}; suites={result.Suites.Count}");
            foreach (var suite in result.Manifest?.Suites ?? []) Console.WriteLine($"- {suite.SuiteId} [{suite.Category}/{suite.Tool}]: {suite.Status}; C={suite.Critical}, H={suite.High}, M={suite.Medium}, L={suite.Low}, accepted={suite.Accepted}");
            if (result.SummaryPath is not null) Console.WriteLine("- Summary: " + result.SummaryPath);
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
        }
        else return 2;
        return result.ExitCode;
    }
    private static Option<string> Required(string name) => new(name) { Required = true };
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
}
