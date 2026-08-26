using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Testing;

public sealed class TestingModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "test";
    public string Description => "Inventory test suites, reconcile native results, and prove executed feature traceability.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisTestResultAdapter, JUnitTestResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, CucumberJUnitTestResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, VitestJsonResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, PlaywrightJsonResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, TrxTestResultAdapter>();
        services.AddSingleton<ICisTestResultAdapter, StrykerJsonResultAdapter>();
        services.AddSingleton<TestingService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<TestingService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Simple("inventory", "Discover and persist the registered suite inventory.", service.Inventory));
        root.Subcommands.Add(Validate(service));
        root.Subcommands.Add(Reconcile(service));
        root.Subcommands.Add(Trace(service));
        root.Subcommands.Add(Status(service));
        commands.Add(root);
    }

    private static Command Simple(string name, string description, Func<string, TestingResult> action)
    {
        var command = new Command(name, description); var repository = Repo(); var format = Format();
        command.Options.Add(repository); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(repository)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Validate(TestingService service)
    {
        var command = new Command("validate", "Validate the canonical suite profile and result contracts.");
        var repository = Repo(); var format = Format(); var strict = new Option<bool>("--strict");
        command.Options.Add(repository); command.Options.Add(format); command.Options.Add(strict);
        command.SetAction(parse => Render(service.Validate(parse.GetValue(repository)!, parse.GetValue(strict)), parse.GetValue(format)!));
        return command;
    }

    private static Command Reconcile(TestingService service)
    {
        var command = new Command("reconcile", "Parse one workflow run into typed local test evidence.");
        var repository = Repo(); var format = Format(); var run = Required("--run");
        command.Options.Add(repository); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Reconcile(parse.GetValue(repository)!, parse.GetValue(run)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Trace(TestingService service)
    {
        var command = new Command("trace", "Prove generated manual cases executed and passed in a reconciled run.");
        var change = new Argument<string>("change-id"); var repository = Repo(); var format = Format(); var run = Required("--run");
        command.Arguments.Add(change); command.Options.Add(repository); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Trace(parse.GetValue(repository)!, parse.GetValue(change)!, parse.GetValue(run)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Status(TestingService service)
    {
        var command = new Command("status", "Report test traceability for a change using an exact or latest run.");
        var change = new Argument<string>("change-id"); var repository = Repo(); var format = Format(); var run = new Option<string?>("--run");
        command.Arguments.Add(change); command.Options.Add(repository); command.Options.Add(format); command.Options.Add(run);
        command.SetAction(parse => Render(service.Status(parse.GetValue(repository)!, parse.GetValue(change)!, parse.GetValue(run)), parse.GetValue(format)!));
        return command;
    }

    private static int Render(TestingResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};runId={result.RunId ?? "none"};suites={result.Suites.Count};trace={result.Trace.Count};applied={result.Applied.ToString().ToLowerInvariant()}");
            foreach (var suite in result.Manifest?.Suites ?? []) Console.WriteLine($"suite={suite.SuiteId};layer={suite.Layer};status={suite.Status};total={suite.Total};passed={suite.Passed};failed={suite.Failed};skipped={suite.Skipped};failureKind={suite.FailureKind}");
            foreach (var trace in result.Trace) Console.WriteLine($"case={trace.Id};status={trace.Status};suites={string.Join(',', trace.Suites)}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Replace(';', ',')}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"Testing: {result.Status}; run={result.RunId ?? "none"}; suites={result.Suites.Count}");
            foreach (var suite in result.Manifest?.Suites ?? []) Console.WriteLine($"- {suite.SuiteId} [{suite.Layer}]: {suite.Status} ({suite.Passed}/{suite.Total} passed)");
            foreach (var trace in result.Trace) Console.WriteLine($"- {trace.Id}: {trace.Status}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
        }
        else return 2;
        return result.ExitCode;
    }

    private static Option<string> Required(string name) => new(name) { Required = true };
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
}
