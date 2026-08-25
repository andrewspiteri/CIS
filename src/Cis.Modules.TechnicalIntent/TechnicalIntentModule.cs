using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.TechnicalIntent;

public sealed class TechnicalIntentModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "technical-intent";

    public string Description => "Initialize, validate, report, and approve workspace technical intent.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<TechnicalIntentService>();
        services.AddSingleton<GovernanceRefreshService>();
        services.AddSingleton<IChangeReadinessCheck>(provider =>
            provider.GetRequiredService<TechnicalIntentService>());
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<TechnicalIntentService>();
        var command = new Command(Name, Description);
        command.Subcommands.Add(CreateSimple("init", "Initialize or reconcile the workspace technical-intent baseline.", service.Initialize));
        command.Subcommands.Add(CreateSimple("validate", "Validate technical-intent completeness, currency, and approval readiness.", service.Validate));
        command.Subcommands.Add(CreateSimple("status", "Report effective technical-intent lifecycle and drift status.", service.Status));
        command.Subcommands.Add(CreateRefresh(services.GetRequiredService<GovernanceRefreshService>()));
        command.Subcommands.Add(CreateApprove(service));
        commands.Add(command);
    }

    private static Command CreateSimple(
        string name,
        string description,
        Func<string, TechnicalIntentResult> action)
    {
        var command = new Command(name, description);
        var workspace = WorkspaceOption();
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = SelectFormat(parseResult.GetValue(format));
            if (selected is null) return 2;
            var result = action(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory());
            Render(result, selected);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateApprove(TechnicalIntentService service)
    {
        var command = new Command("approve", "Record explicit human approval of a valid, current technical intent.");
        var workspace = WorkspaceOption();
        var reviewer = new Option<string>("--reviewer") { Required = true, Description = "Human reviewer identity." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Human approval rationale." };
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(reviewer);
        command.Options.Add(reason);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = SelectFormat(parseResult.GetValue(format));
            if (selected is null) return 2;
            var result = service.Approve(
                parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(reviewer) ?? string.Empty,
                parseResult.GetValue(reason) ?? string.Empty);
            Render(result, selected);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateRefresh(GovernanceRefreshService service)
    {
        var command = new Command("refresh", "Safely refresh BRD, technical-intent, and high-level-backlog baselines without duplicate approval when semantic authority is unchanged.");
        var workspace = WorkspaceOption();
        var format = FormatOption();
        command.Options.Add(workspace);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selected = SelectFormat(parseResult.GetValue(format));
            if (selected is null) return 2;
            var result = service.Refresh(parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory());
            RenderRefresh(result, selected);
            return result.ExitCode;
        });
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

    private static string? SelectFormat(string? value)
    {
        var format = (value ?? "human").ToLowerInvariant();
        if (format is "human" or "json" or "agent") return format;
        Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent.");
        return null;
    }

    private static void Render(TechnicalIntentResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};" +
                $"effectiveStatus={Clean(result.Validation?.EffectiveStatus)};valid={result.Validation?.Valid.ToString().ToLowerInvariant() ?? "false"};" +
                $"current={result.Validation?.Current.ToString().ToLowerInvariant() ?? "false"}");
            Console.WriteLine($"workspace={Clean(result.WorkspacePath)}");
            Console.WriteLine($"authority={Clean(result.AuthorityRepositoryId)}");
            Console.WriteLine($"canonicalPath={Clean(result.CanonicalPath)}");
            foreach (var baseline in result.Baselines)
                Console.WriteLine($"baseline={Clean(baseline.Kind)};id={Clean(baseline.Id)};version={Clean(baseline.Version)};evidence={Clean(baseline.Evidence)}");
            foreach (var warning in result.Validation?.Warnings ?? result.Warnings)
                Console.WriteLine($"validationWarning={Clean(warning)}");
            foreach (var error in result.Validation?.Errors ?? [])
                Console.WriteLine($"validationError={Clean(error)}");
            foreach (var error in result.Errors)
                Console.WriteLine($"error={Clean(error)}");
            return;
        }

        Console.WriteLine($"Technical intent: {result.Status}");
        Console.WriteLine($"Canonical path: {result.CanonicalPath ?? string.Empty}");
        if (result.Validation is not null)
            Console.WriteLine($"Effective status: {result.Validation.EffectiveStatus}; valid: {result.Validation.Valid}; current: {result.Validation.Current}");
        foreach (var error in result.Validation?.Errors ?? result.Errors)
            Console.WriteLine($"Error: {error}");
        foreach (var warning in result.Validation?.Warnings ?? result.Warnings)
            Console.WriteLine($"Warning: {warning}");
    }

    private static void RenderRefresh(GovernanceRefreshResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }
        if (format == "agent")
        {
            Console.WriteLine($"status={Clean(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};brd={Clean(result.Brd.Validation?.EffectiveStatus)};technicalIntent={Clean(result.TechnicalIntent?.Validation?.EffectiveStatus)};backlog={Clean(result.Backlog?.Validation?.EffectiveStatus)}");
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"diagnostic={Clean(diagnostic)}");
            foreach (var warning in result.Brd.Validation?.Warnings ?? []) Console.WriteLine($"brdWarning={Clean(warning)}");
            foreach (var warning in result.TechnicalIntent?.Validation?.Warnings ?? []) Console.WriteLine($"technicalIntentWarning={Clean(warning)}");
            foreach (var warning in result.Backlog?.Validation?.Warnings ?? []) Console.WriteLine($"backlogWarning={Clean(warning)}");
            return;
        }
        Console.WriteLine($"Governance refresh: {result.Status}");
        Console.WriteLine($"BRD: {result.Brd.Validation?.EffectiveStatus ?? "not-run"}; technical intent: {result.TechnicalIntent?.Validation?.EffectiveStatus ?? "not-run"}; backlog: {result.Backlog?.Validation?.EffectiveStatus ?? "not-run"}");
        foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
    }

    private static string Clean(string? value)
        => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
