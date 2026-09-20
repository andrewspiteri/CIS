using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cis.Modules.UiDirection;

public sealed class UiDirectionModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Name => "ui-direction";
    public string Description => "Capture, generate, validate, report, and approve workspace-level UI look and feel.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisUiBaselineDiscovery, UiBaselineDiscovery>();
        services.TryAddSingleton<IUiDirectionSolutionDesignSource, UiDirectionSolutionDesignSource>();
        services.AddSingleton<UiDirectionQuestionnaireService>();
        services.AddSingleton<UiDirectionService>();
        services.AddSingleton<IChangeReadinessCheck>(provider => provider.GetRequiredService<UiDirectionService>());
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var questionnaire = services.GetRequiredService<UiDirectionQuestionnaireService>();
        var service = services.GetRequiredService<UiDirectionService>();
        var command = new Command(Name, Description);

        var baseline = new Command("baseline", "Discover the existing owned UI implementation without recording design decisions.");
        var baselineWorkspace = Workspace(); var baselineFormat = Format();
        baseline.Options.Add(baselineWorkspace); baseline.Options.Add(baselineFormat);
        baseline.SetAction(parse => Render(questionnaire.Baseline(parse.GetValue(baselineWorkspace)!), parse.GetValue(baselineFormat)!));
        command.Subcommands.Add(baseline);

        var questions = new Command("questions", "Capture high-level experience and visual-direction choices.");
        questions.Subcommands.Add(SimpleQuestionnaire("init", "Initialize or reconcile the UI-direction questionnaire.", questionnaire.Initialize));
        questions.Subcommands.Add(SimpleQuestionnaire("status", "Report questionnaire progress and source currency.", questionnaire.Status));
        questions.Subcommands.Add(Answer(questionnaire));
        command.Subcommands.Add(questions);
        command.Subcommands.Add(Simple("init", "Generate or reconcile high-level UI direction from approved architecture and resolved choices.", service.Initialize));
        command.Subcommands.Add(Simple("validate", "Validate UI-direction completeness, provenance, source currency, and approval integrity.", service.Validate));
        command.Subcommands.Add(Simple("status", "Report lifecycle and drift for high-level UI direction.", service.Status));
        command.Subcommands.Add(Approve(service));
        commands.Add(command);
    }

    private static Command Simple(string name, string description, Func<string, UiDirectionResult> action)
    {
        var command = new Command(name, description); var workspace = Workspace(); var format = Format();
        command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse => Render(action(parse.GetValue(workspace)!), parse.GetValue(format)!));
        return command;
    }

    private static Command SimpleQuestionnaire(string name, string description, Func<string, UiDirectionQuestionnaireResult> action)
    {
        var command = new Command(name, description); var workspace = Workspace(); var format = Format(); var summary = new Option<bool>("--summary");
        command.Options.Add(workspace); command.Options.Add(format); command.Options.Add(summary);
        command.SetAction(parse => Render(action(parse.GetValue(workspace)!), parse.GetValue(format)!, parse.GetValue(summary)));
        return command;
    }

    private static Command Answer(UiDirectionQuestionnaireService service)
    {
        var command = new Command("answer", "Record or replace one UI-direction answer.");
        var id = new Argument<string>("id"); var answer = new Option<string>("--answer") { Required = true };
        var actor = new Option<string>("--actor") { Required = true }; var workspace = Workspace(); var format = Format();
        command.Arguments.Add(id); command.Options.Add(answer); command.Options.Add(actor); command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse => Render(service.Answer(parse.GetValue(workspace)!, parse.GetValue(id)!, parse.GetValue(answer)!, parse.GetValue(actor)!), parse.GetValue(format)!, false));
        return command;
    }

    private static Command Approve(UiDirectionService service)
    {
        var command = new Command("approve", "Approve the exact high-level UI direction.");
        var reviewer = new Option<string>("--reviewer") { Required = true }; var reason = new Option<string>("--reason") { Required = true };
        var workspace = Workspace(); var format = Format();
        command.Options.Add(reviewer); command.Options.Add(reason); command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(parse => Render(service.Approve(parse.GetValue(workspace)!, parse.GetValue(reviewer)!, parse.GetValue(reason)!), parse.GetValue(format)!));
        return command;
    }

    private static int Render(UiDirectionQuestionnaireResult result, string format, bool summary)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(JsonSerializer.Serialize(summary ? result with { Questions = [] } : result, JsonOptions));
        else if (format.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};complete={result.Complete.ToString().ToLowerInvariant()};current={result.Current.ToString().ToLowerInvariant()};answered={result.AnsweredCount};unanswered={result.UnansweredCount}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}"); Console.WriteLine($"path={Clean(result.RelativePath)}");
            if (!summary) foreach (var item in result.Questions) Console.WriteLine($"question={item.Id};area={Clean(item.Area)};status={item.Status};answer={Clean(item.Answer)};source={item.ResolutionSource};confidence={Clean(item.Confidence)}");
            foreach (var warning in result.Warnings) Console.WriteLine($"warning={Clean(warning)}"); foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else if (format.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"UI direction questionnaire: {result.Status}; {result.AnsweredCount}/{result.AnsweredCount + result.UnansweredCount} resolved; current={result.Current}");
            if (!summary) foreach (var item in result.Questions) Console.WriteLine($"- {item.Id} {item.Area}: {item.Status}");
            foreach (var warning in result.Warnings) Console.WriteLine($"Warning: {warning}"); foreach (var error in result.Errors) Console.WriteLine($"Error: {error}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }

    private static int Render(CisUiBaselineResult result, string format)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format.Equals("agent", StringComparison.OrdinalIgnoreCase) || format.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};repositories={result.Repositories.Count};cached={result.Cached.ToString().ToLowerInvariant()};sourceHash={result.SourceHash}");
            foreach (var repository in result.Repositories)
            {
                Console.WriteLine($"repository={Clean(repository.Id)};files={repository.FilesRead};candidates={repository.CandidateFiles};limited={repository.Limited.ToString().ToLowerInvariant()}");
                foreach (var fact in repository.Facts) Console.WriteLine($"observed={Clean(fact.Area)};summary={Clean(fact.Summary)}");
            }
            foreach (var warning in result.Warnings) Console.WriteLine($"warning={Clean(warning)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }

    private static int Render(UiDirectionResult result, string format)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}"); Console.WriteLine($"path={Clean(result.RelativePath)}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"warning={Clean(warning)}"); foreach (var error in result.Validation?.Errors ?? result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else if (format.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"High-level UI direction: {result.Status}; {result.Validation?.EffectiveStatus ?? "not assessed"}"); Console.WriteLine($"Path: {result.RelativePath}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"Warning: {warning}"); foreach (var error in result.Validation?.Errors ?? result.Errors) Console.WriteLine($"Error: {error}");
        }
        else { Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return 2; }
        return result.ExitCode;
    }

    private static Option<string> Workspace() => new("--workspace") { DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { DefaultValueFactory = _ => "human" };
    private static string Clean(string? value) => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
