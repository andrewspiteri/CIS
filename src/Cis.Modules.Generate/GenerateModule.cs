using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Generate;

public sealed class GenerateModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "generate";
    public string Description => "Discover, validate, and render deterministic repository-owned templates.";
    public void RegisterServices(IServiceCollection services) => services.AddSingleton<GenerateService>();
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<GenerateService>(); var command = new Command(Name, Description);
        command.Subcommands.Add(Simple("status", "Report deterministic generator and template availability.", service.Templates));
        command.Subcommands.Add(Simple("templates", "List deterministic templates.", service.Templates));
        command.Subcommands.Add(ApplicableCommand(service));
        command.Subcommands.Add(WithId("describe", "Describe a template and its variables.", service.Describe));
        command.Subcommands.Add(WithId("validate", "Validate a template and its identity.", service.Validate));
        command.Subcommands.Add(RenderCommand(service)); commands.Add(command);
    }
    private static Command ApplicableCommand(GenerateService service)
    { var c = new Command("applicable", "Find deterministic templates applicable to a task and changed files.");
      var task = new Option<string>("--task") { Required = true }; var changed = new Option<string[]>("--changed-file") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true };
      var repo = Repo(); var format = Format(); c.Options.Add(task); c.Options.Add(changed); c.Options.Add(repo); c.Options.Add(format);
      c.SetAction(p => RenderApplicable(service.Applicable(p.GetValue(repo)!, p.GetValue(task)!, p.GetValue(changed) ?? []), p.GetValue(format)!)); return c; }
    private static Command Simple(string name, string description, Func<string, GenerateResult> action)
    { var c = new Command(name, description); var repo = Repo(); var format = Format(); c.Options.Add(repo); c.Options.Add(format); c.SetAction(p => Render(action(p.GetValue(repo)!), p.GetValue(format)!)); return c; }
    private static Command WithId(string name, string description, Func<string, string, GenerateResult> action)
    { var c = new Command(name, description); var id = new Argument<string>("template"); var repo = Repo(); var format = Format(); c.Arguments.Add(id); c.Options.Add(repo); c.Options.Add(format); c.SetAction(p => Render(action(p.GetValue(repo)!, p.GetValue(id)!), p.GetValue(format)!)); return c; }
    private static Command RenderCommand(GenerateService service)
    { var c = new Command("render", "Render a deterministic template to a repository path."); var id = new Argument<string>("template");
      var output = new Option<string>("--output") { Required = true }; var set = new Option<string[]>("--set") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true };
      var repo = Repo(); var format = Format(); c.Arguments.Add(id); c.Options.Add(output); c.Options.Add(set); c.Options.Add(repo); c.Options.Add(format);
      c.SetAction(p => Render(service.Render(p.GetValue(repo)!, p.GetValue(id)!, p.GetValue(output)!, p.GetValue(set) ?? []), p.GetValue(format)!)); return c; }
    private static int Render(GenerateResult result, string format)
    { if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
      else if (format == "agent") { Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};templates={result.Templates.Count};output={result.OutputPath ?? "none"};applied={result.Applied.ToString().ToLowerInvariant()}"); foreach (var d in result.Diagnostics) Console.WriteLine($"diagnostic={d.Replace(';', ',')}"); }
      else if (format == "human") { Console.WriteLine($"Generation: {result.Status}; templates={result.Templates.Count}; output={result.OutputPath ?? "none"}"); foreach (var t in result.Templates) Console.WriteLine($"- {t.Id}: {string.Join(", ", t.Variables)}"); foreach (var d in result.Diagnostics) Console.WriteLine(d); }
      else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; } return result.ExitCode; }
    private static int RenderApplicable(GenerateApplicabilityResult result, string format)
    { if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
      else if (format == "agent") { Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};applicable={result.Candidates.Count};decision={result.Decision}"); foreach (var item in result.Candidates) Console.WriteLine($"template={item.Template.Id};confidence={item.Confidence};reason={item.Reason.Replace(';', ',')}"); foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={diagnostic.Replace(';', ',')}"); }
      else if (format == "human") { Console.WriteLine($"Generation applicability: {result.Status}; decision={result.Decision}"); foreach (var item in result.Candidates) Console.WriteLine($"- {item.Template.Id} [{item.Confidence}]: {item.Reason}"); foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic); }
      else return 2; return result.ExitCode; }
    private static Option<string> Repo() => new("--repo") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
}
