using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Design;

public sealed class DesignModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "design";
    public string Description => "Scaffold, render, validate, review, and approve guideline-bound Sharp/SVG design packs.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DesignTemplateCatalog>();
        services.AddSingleton<IDesignProcessRunner, NodeDesignProcessRunner>();
        services.AddSingleton<DesignService>();
        services.AddSingleton<ICisFeatureScreenGenerator>(provider => new FeatureScreenGenerator(
            provider.GetService<ICisTextGenerationService>() ?? new UnavailableTextGenerationService(),
            provider.GetServices<ICisUiBaselineDiscovery>(), provider.GetRequiredService<IDesignProcessRunner>()));
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<DesignService>();
        var design = new Command(Name, Description);
        design.Subcommands.Add(TemplatesCommand(service));
        design.Subcommands.Add(ReuseCommand(service));
        design.Subcommands.Add(ScaffoldCommand(service));
        design.Subcommands.Add(Simple("wireframe-validate", "Validate textual screen behavior, classification, states, actions, paths, and coverage before human review.", service.ValidateWireframes));
        design.Subcommands.Add(Review("wireframe-approve", "Optionally approve textual screen behavior separately; the default design approval records both exact digests.", service.ApproveWireframes));
        design.Subcommands.Add(Review("wireframe-reject", "Reject textual wireframes with review rationale.", service.RejectWireframes));
        design.Subcommands.Add(Simple("render", "Render PNGs and enter the global design-review pause.", service.Render));
        design.Subcommands.Add(Simple("validate", "Validate wireframe provenance, renderer, guideline provenance, and PNG evidence.", service.Validate));
        design.Subcommands.Add(Simple("reconcile", "Carry current approved feature authority across a provenance-only refresh when rendered pixels are unchanged.", service.Reconcile));
        design.Subcommands.Add(Review("approve", "Approve the exact wireframe, renderer, and PNG manifest together and release the global gate.", service.Approve));
        design.Subcommands.Add(Review("reject", "Reject the design, preserve hashes/rationale, remove rejected PNGs, and keep work paused.", service.Reject));
        design.Subcommands.Add(Simple("status", "Show the current design gate, approval, renderer, and artifact state.", service.Status));
        commands.Add(design);
    }

    private static Command ReuseCommand(DesignService service)
    {
        var command = new Command("reuse", "Carry exact PNGs from an approved design into a new target screen with verified provenance.");
        var id = new Argument<string>("change-id");
        var sourceChange = new Option<string>("--source-change") { Required = true, Description = "Approved source change ID." };
        var sourceScreen = new Option<string>("--source-screen") { Required = true, Description = "Source PNG manifest screen ID." };
        var targetScreen = new Option<string>("--target-screen") { Required = true, Description = "Target wireframe screen ID satisfied by the reused artifacts." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Why the approved visual remains compatible with the target screen." };
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        command.Options.Add(sourceChange);
        command.Options.Add(sourceScreen);
        command.Options.Add(targetScreen);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Reuse(new DesignReuseRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(sourceChange) ?? string.Empty,
                parseResult.GetValue(sourceScreen) ?? string.Empty,
                parseResult.GetValue(targetScreen) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty));
            Render(result, parseResult.GetValue(format) ?? "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command TemplatesCommand(DesignService service)
    {
        var command = new Command("templates", "List reusable application-shell and visual component templates with possible token savings.");
        var format = Format();
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Templates();
            Render(result, parseResult.GetValue(format) ?? "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command ScaffoldCommand(DesignService service)
    {
        var command = new Command("scaffold", "Generate one self-contained Sharp/SVG renderer from valid textual wireframes and reusable templates.");
        var id = new Argument<string>("change-id");
        var feature = new Option<string>("--feature") { Required = true, Description = "Feature name used for renderer identity." };
        var shell = new Option<string>("--shell") { DefaultValueFactory = _ => "shell.standard-app", Description = "Required application-shell template ID." };
        var component = new Option<string[]>("--component") { Arity = ArgumentArity.ZeroOrMore, AllowMultipleArgumentsPerToken = true, Description = "Reusable component template IDs." };
        var force = new Option<bool>("--force") { Description = "Replace an existing renderer after explicit review." };
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        command.Options.Add(feature);
        command.Options.Add(shell);
        command.Options.Add(component);
        command.Options.Add(force);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = service.Scaffold(new DesignScaffoldRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(feature) ?? string.Empty,
                parseResult.GetValue(shell) ?? "shell.standard-app",
                parseResult.GetValue(component) ?? [],
                parseResult.GetValue(force)));
            Render(result, parseResult.GetValue(format) ?? "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command Simple(string name, string description, Func<string, string, DesignResult> action)
    {
        var command = new Command(name, description);
        var id = new Argument<string>("change-id");
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = action(parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(), parseResult.GetValue(id) ?? string.Empty);
            Render(result, parseResult.GetValue(format) ?? "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Command Review(string name, string description, Func<DesignReviewRequest, DesignResult> action)
    {
        var command = new Command(name, description);
        var id = new Argument<string>("change-id");
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Human approval or rejection rationale." };
        var repo = Repo();
        var format = Format();
        command.Arguments.Add(id);
        command.Options.Add(reviewer);
        command.Options.Add(reason);
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var result = action(new DesignReviewRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty));
            Render(result, parseResult.GetValue(format) ?? "human");
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> Repo() => new("--repo")
    {
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        Description = "Repository path.",
    };

    private static Option<string> Format() => new("--format")
    {
        DefaultValueFactory = _ => "human",
        Description = "Output format: human, json, or agent.",
    };

    private static void Render(DesignResult result, string format)
    {
        format = format is "json" or "agent" ? format : "human";
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};changeId={result.ChangeId};gate={result.GateStatus};approval={result.ApprovalStatus};renderer={result.RendererPath};templates={result.Templates.Count};artifacts={result.Artifacts.Count};applied={result.Applied.ToString().ToLowerInvariant()};exitCode={result.ExitCode}");
            foreach (var template in result.Templates) Console.WriteLine($"template={template.Id};version={template.Version};kind={template.Kind};possibleTokenSavings={template.EstimatedSavedTokens}");
            foreach (var artifact in result.Artifacts) Console.WriteLine($"artifact={artifact.Path};screen={artifact.ScreenId};state={artifact.State};viewport={artifact.Viewport};dimensions={artifact.Width}x{artifact.Height};sha256={artifact.Sha256}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={Clean(diagnostic)}");
            return;
        }

        Console.WriteLine($"Design: {result.Status}; gate: {result.GateStatus ?? "n/a"}; approval: {result.ApprovalStatus ?? "n/a"}");
        if (result.RendererPath is not null) Console.WriteLine($"Renderer: {result.RendererPath}");
        foreach (var template in result.Templates) Console.WriteLine($"- {template.Id}@{template.Version} ({template.Kind}; possible token savings {template.EstimatedSavedTokens})");
        foreach (var artifact in result.Artifacts) Console.WriteLine($"- {artifact.Path} ({artifact.Width}x{artifact.Height}; {artifact.Sha256})");
        foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
    }

    private static string Clean(string value) => value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
