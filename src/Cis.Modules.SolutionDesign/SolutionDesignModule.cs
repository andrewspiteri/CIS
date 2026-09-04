using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cis.Modules.SolutionDesign;

public sealed class SolutionDesignModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "solution-design";

    public string Description => "Generate, validate, report, and approve the overall solution design and component sheet.";

    public void RegisterServices(IServiceCollection services)
    {
        services.TryAddSingleton<ISolutionDesignTechnicalIntentSource, SolutionDesignTechnicalIntentSource>();
        services.AddSingleton<SolutionDesignService>();
        services.AddSingleton<IChangeReadinessCheck>(provider => provider.GetRequiredService<SolutionDesignService>());
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<SolutionDesignService>();
        var command = new Command(Name, Description);
        command.Subcommands.Add(CreateSimple("init", "Generate or reconcile the governed overall solution-design bundle from Active technical intent.", service.Initialize));
        command.Subcommands.Add(CreateSimple("validate", "Validate structure, traceability, source currency, and approval readiness for both design artifacts.", service.Validate));
        command.Subcommands.Add(CreateSimple("status", "Report lifecycle and drift for the overall design and component sheet.", service.Status));
        command.Subcommands.Add(CreateApprove(service));
        commands.Add(command);
    }

    private static Command CreateSimple(string name, string description, Func<string, SolutionDesignResult> action)
    {
        var command = new Command(name, description);
        var workspace = WorkspaceOption();
        var format = FormatOption();
        command.Options.Add(workspace); command.Options.Add(format);
        command.SetAction(result => Render(action(result.GetValue(workspace) ?? Directory.GetCurrentDirectory()),
            result.GetValue(format) ?? "human"));
        return command;
    }

    private static Command CreateApprove(SolutionDesignService service)
    {
        var command = new Command("approve", "Atomically approve the exact overall solution design and component sheet.");
        var workspace = WorkspaceOption();
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Human approval rationale for the complete bundle." };
        var format = FormatOption();
        command.Options.Add(workspace); command.Options.Add(reviewer); command.Options.Add(reason); command.Options.Add(format);
        command.SetAction(result => Render(service.Approve(
            result.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
            result.GetValue(reviewer) ?? string.Empty,
            result.GetValue(reason) ?? string.Empty), result.GetValue(format) ?? "human"));
        return command;
    }

    private static Option<string> WorkspaceOption() => new("--workspace")
    {
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        Description = "CIS workspace path. Defaults to the current directory.",
    };

    private static Option<string> FormatOption() => new("--format")
    {
        DefaultValueFactory = _ => "human",
        Description = "Output format: human, json, or agent.",
    };

    private static int Render(SolutionDesignResult result, string format)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};components={result.Components.Count};effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"designPath={Clean(result.DesignPath)}");
            Console.WriteLine($"componentSheetPath={Clean(result.ComponentSheetPath)}");
            Console.WriteLine($"technicalIntentVersion={Clean(result.TechnicalIntentVersion)}");
            foreach (var component in result.Components)
                Console.WriteLine($"component={Clean(component.Id)};name={Clean(component.Name)};classification={Clean(component.Classification)};requirements={Clean(string.Join(',', component.RequirementIds))}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"warning={Clean(warning)}");
            foreach (var error in result.Validation?.Errors ?? result.Errors) Console.WriteLine($"error={Clean(error)}");
        }
        else if (format.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Overall solution design: {result.Status}");
            Console.WriteLine($"Design: {result.DesignPath ?? string.Empty}");
            Console.WriteLine($"Component sheet: {result.ComponentSheetPath ?? string.Empty}");
            Console.WriteLine($"Components: {result.Components.Count}");
            if (result.Validation is not null)
                Console.WriteLine($"Effective status: {result.Validation.EffectiveStatus}; valid: {result.Validation.Valid}; current: {result.Validation.Current}");
            foreach (var error in result.Validation?.Errors ?? result.Errors) Console.WriteLine($"Error: {error}");
            foreach (var warning in result.Validation?.Warnings ?? []) Console.WriteLine($"Warning: {warning}");
        }
        else
        {
            Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent.");
            return 2;
        }
        return result.ExitCode;
    }

    private static string Clean(string? value)
        => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
