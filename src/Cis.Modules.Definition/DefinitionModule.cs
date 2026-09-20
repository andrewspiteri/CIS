using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Definition;

public sealed class DefinitionModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Name => "definition";
    public string Description => "Coordinate the high-level product-definition wizard and consolidated activation.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ProductDefinitionAuthority>();
        services.AddSingleton<ICisProductDefinitionAuthority>(provider =>
            provider.GetRequiredService<ProductDefinitionAuthority>());
        services.AddSingleton<DefinitionWizardService>();
        services.AddSingleton<ICisObservedReferencePreparer, ObservedReferenceWorkspacePreparer>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<DefinitionWizardService>();
        var root = new Command(Name, Description);
        root.Subcommands.Add(Simple("init", "Start or resume the high-level definition wizard.", service.Initialize));
        root.Subcommands.Add(Simple("status", "Report all wizard pages, artifacts, diagrams, dictionaries, and activation readiness.", service.Status));
        root.Subcommands.Add(Prepare(service));
        root.Subcommands.Add(Answer(service));
        root.Subcommands.Add(Approve(service));
        root.Subcommands.Add(Activate(service));
        commands.Add(root);
    }

    private static Command Simple(string name, string description, Func<string, CisDefinitionWizardResult> action)
    {
        var command = new Command(name, description); var workspace = Workspace(); var format = Format();
        command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(workspace)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Prepare(DefinitionWizardService service)
    {
        var command = new Command("prepare", "Prepare or refresh one wizard page from its governed upstream pages.");
        var page = new Option<string>("--page") { Required = true }; var workspace = Workspace(); var format = Format();
        var backlogMode = new Option<string?>("--backlog-mode") { Description = "Delivery only: requirements or no-planned-work." };
        var actor = new Option<string?>("--actor") { Description = "Human recording the no-planned-work choice." };
        command.Options.Add(page); command.Options.Add(workspace); command.Options.Add(format);
        command.Options.Add(backlogMode); command.Options.Add(actor);
        command.SetAction(parse => Render(service.Prepare(parse.GetValue(workspace)!, parse.GetValue(page)!, parse.GetValue(backlogMode), parse.GetValue(actor)), parse.GetValue(format)!));
        return command;
    }

    private static Command Answer(DefinitionWizardService service)
    {
        var command = new Command("answer", "Record a technical or experience questionnaire answer without leaving the wizard.");
        var page = new Option<string>("--page") { Required = true }; var id = new Option<string>("--id") { Required = true };
        var answer = new Option<string>("--answer") { Required = true }; var actor = new Option<string>("--actor") { Required = true };
        var workspace = Workspace(); var format = Format();
        command.Options.Add(page); command.Options.Add(id); command.Options.Add(answer); command.Options.Add(actor);
        command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse => Render(service.Answer(parse.GetValue(workspace)!, parse.GetValue(page)!, parse.GetValue(id)!,
            parse.GetValue(answer)!, parse.GetValue(actor)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Activate(DefinitionWizardService service)
    {
        var command = new Command("activate", "Approve and activate the exact complete high-level definition baseline transactionally.");
        var reviewer = new Option<string>("--reviewer") { Required = true }; var workspace = Workspace(); var format = Format();
        command.Options.Add(reviewer); command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse => Render(service.Activate(parse.GetValue(workspace)!, parse.GetValue(reviewer)!), parse.GetValue(format)!));
        return command;
    }

    private static Command Approve(DefinitionWizardService service)
    {
        var command = new Command("approve", "Approve the architecture, component sheet and diagrams without activating the remaining definition pages.");
        var page = new Option<string>("--page") { Required = true };
        var reviewer = new Option<string>("--reviewer") { Required = true }; var workspace = Workspace(); var format = Format();
        command.Options.Add(page); command.Options.Add(reviewer); command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse =>
        {
            if (parse.GetValue(page) != "architecture") { Console.Error.WriteLine("Only --page architecture supports this approval command."); return 2; }
            return Render(service.ApproveArchitecture(parse.GetValue(workspace)!, parse.GetValue(reviewer)!), parse.GetValue(format)!);
        });
        return command;
    }

    private static int Render(CisDefinitionWizardResult result, string format)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};active={result.Active.ToString().ToLowerInvariant()};readyToActivate={result.ReadyToActivate.ToString().ToLowerInvariant()};pages={result.Pages.Count};dictionaries={result.Dictionaries.Count};diagrams={result.Diagrams.Count};applied={result.Applied.ToString().ToLowerInvariant()}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)};authority={Clean(result.AuthorityRepositoryId)};session={Clean(result.SessionId)};currentPage={Clean(result.CurrentPage)}");
            foreach (var page in result.Pages) Console.WriteLine($"page={page.Ordinal};id={page.Id};status={Clean(page.Status)};complete={page.Complete.ToString().ToLowerInvariant()};current={page.Current.ToString().ToLowerInvariant()};path={Clean(page.PrimaryPath)}");
            foreach (var warning in result.Warnings) Console.WriteLine($"warning={Clean(warning)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else if (format.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"High-level definition wizard: {result.Status}; ready to activate={result.ReadyToActivate}");
            foreach (var page in result.Pages) Console.WriteLine($"{page.Ordinal}. {page.Title}: {page.Status}{(page.Complete ? string.Empty : " — needs attention")}");
            Console.WriteLine($"Dictionaries: {result.Dictionaries.Count(item => item.Applicable)} applicable; diagrams: {result.Diagrams.Count}; UI preview: {(result.Preview is null ? "missing" : result.Preview.Status)}");
            foreach (var warning in result.Warnings) Console.WriteLine($"Warning: {warning}");
            foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }

    private static Option<string> Workspace() => new("--workspace") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
    private static string Clean(string? value) => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
