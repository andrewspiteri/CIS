using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Api;

public sealed class ApiModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Name => "api";
    public string Description => "Discover, inventory, validate, and compare governed API contracts.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ApiGovernanceService>();
        services.AddSingleton<OpenApiCompatibilityService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, ApiDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var api = new Command(Name, Description);
        var governance = services.GetRequiredService<ApiGovernanceService>();
        api.Subcommands.Add(Discover(governance));
        api.Subcommands.Add(Inventory(governance));
        api.Subcommands.Add(Validate(governance));
        api.Subcommands.Add(Diff(services.GetRequiredService<OpenApiCompatibilityService>()));
        commands.Add(api);
    }

    private static Command Discover(ApiGovernanceService service)
    {
        var command = new Command("discover", "Correlate source, the API dictionary, and governed OpenAPI into local normalized state.");
        var repo = Repo();
        var openapi = new Option<string[]>("--openapi") { Description = "Optional repository-relative current OpenAPI JSON paths overriding the profile.", AllowMultipleArgumentsPerToken = true };
        var format = Format();
        command.Options.Add(repo); command.Options.Add(openapi); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Discover(parse.GetValue(repo)!, parse.GetValue(openapi));
            RenderDiscovery(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command Inventory(ApiGovernanceService service)
    {
        var command = new Command("inventory", "Display normalized API operations without rescanning the repository.");
        var repo = Repo(); var format = Format();
        var exposure = new Option<string?>("--exposure") { Description = "Filter by exposure class." };
        var module = new Option<string?>("--module") { Description = "Filter by exact module." };
        command.Options.Add(repo); command.Options.Add(exposure); command.Options.Add(module); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Inventory(parse.GetValue(repo)!, parse.GetValue(exposure), parse.GetValue(module));
            RenderInventory(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command Validate(ApiGovernanceService service)
    {
        var command = new Command("validate", "Enforce API governance and cross-artifact integrity.");
        var repo = Repo(); var format = Format();
        var strict = new Option<bool>("--strict") { Description = "Treat warnings as validation failures." };
        var noRefresh = new Option<bool>("--no-refresh") { Description = "Validate existing local API state without discovery." };
        command.Options.Add(repo); command.Options.Add(strict); command.Options.Add(noRefresh); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Validate(parse.GetValue(repo)!, parse.GetValue(strict), !parse.GetValue(noRefresh));
            RenderValidation(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Command Diff(OpenApiCompatibilityService service)
    {
        var command = new Command("diff", "Compare every governed supported baseline with current OpenAPI using forward-transitive compatibility.");
        var repo = Repo(); var format = Format();
        var baseline = new Option<string?>("--baseline") { Description = "Optional repository-relative single baseline override; otherwise every profile baseline is compared." };
        var current = new Option<string?>("--current") { Description = "Repository-relative current OpenAPI JSON path; defaults to the profile." };
        command.Options.Add(repo); command.Options.Add(baseline); command.Options.Add(current); command.Options.Add(format);
        command.SetAction(parse =>
        {
            var selected = GetFormat(parse.GetValue(format)); if (selected is null) return 2;
            var result = service.Diff(parse.GetValue(repo)!, parse.GetValue(baseline), parse.GetValue(current));
            RenderDiff(result, selected); return result.ExitCode;
        });
        return command;
    }

    private static Option<string> Repo() => new("--repo") { Description = "Initialized repository path. Defaults to current directory.", DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
    private static Option<string> Format() => new("--format") { Description = "Output format: human, json, or agent.", DefaultValueFactory = _ => "human" };
    private static string? GetFormat(string? value) { var format = (value ?? "human").ToLowerInvariant(); if (format is "human" or "json" or "agent") return format; Console.Error.WriteLine("Unsupported format. Use human, json, or agent."); return null; }

    private static void RenderDiscovery(ApiDiscoveryResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};operations={result.Operations};source={result.SourceOperations};dictionary={result.DictionaryOperations};openapi={result.OpenApiOperations};applied={result.Applied.ToString().ToLowerInvariant()};output={Safe(result.OutputPath)}");
            RenderDiagnostics(result.Diagnostics); return;
        }
        Console.WriteLine($"API discovery: {result.Status}; operations: {result.Operations}; source: {result.SourceOperations}; dictionary: {result.DictionaryOperations}; OpenAPI: {result.OpenApiOperations}");
        foreach (var diagnostic in result.Diagnostics) Console.WriteLine($"- [{diagnostic.Severity}] {diagnostic.Code} {diagnostic.Message}");
    }

    private static void RenderInventory(ApiInventoryResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};operations={result.Items.Count}");
            foreach (var item in result.Items) Console.WriteLine($"api={Safe(item.ApiId)};method={item.Method};path={Safe(item.Path)};module={Safe(item.Module)};exposure={Safe(item.Exposure)};status={Safe(item.Status)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Safe(error)}"); return;
        }
        Console.WriteLine($"API inventory: {result.Status}; operations: {result.Items.Count}");
        foreach (var item in result.Items) Console.WriteLine($"- {item.Method} {item.Path} [{item.Exposure}] {item.ApiId}");
        foreach (var error in result.Errors) Console.WriteLine($"- error: {error}");
    }

    private static void RenderValidation(ApiValidationResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent") { Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};operations={result.Operations};errors={result.Errors};warnings={result.Warnings};strict={result.Strict.ToString().ToLowerInvariant()}"); RenderDiagnostics(result.Diagnostics); return; }
        Console.WriteLine($"API validation: {result.Status}; operations: {result.Operations}; errors: {result.Errors}; warnings: {result.Warnings}");
        foreach (var item in result.Diagnostics) Console.WriteLine($"- [{item.Severity}] {item.Code} ({item.Rule}) {item.Message}\n  Fix: {item.Remediation}");
    }

    private static void RenderDiff(ApiDiffResult result, string format)
    {
        if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return; }
        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};breaking={result.Breaking};potentiallyBreaking={result.PotentiallyBreaking};nonBreaking={result.NonBreaking};baselines={result.Comparisons.Count};baseline={Safe(result.Baseline)};current={Safe(result.Current)}");
            foreach (var comparison in result.Comparisons) Console.WriteLine($"comparison={Safe(comparison.Baseline)};current={Safe(comparison.Current)};status={comparison.Status};breaking={comparison.Breaking};potentiallyBreaking={comparison.PotentiallyBreaking};nonBreaking={comparison.NonBreaking}");
            foreach (var item in result.Findings) Console.WriteLine($"finding={item.Code};severity={Safe(item.Severity)};classification={Safe(item.Classification)};method={item.Method};path={Safe(item.Path)};message={Safe(item.Message)};remediation={Safe(item.Remediation)}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Safe(error)}"); return;
        }
        Console.WriteLine($"API compatibility: {result.Status}; baselines: {result.Comparisons.Count}; breaking: {result.Breaking}; review: {result.PotentiallyBreaking}; non-breaking: {result.NonBreaking}");
        foreach (var comparison in result.Comparisons) Console.WriteLine($"- {comparison.Baseline} -> {comparison.Current}: {comparison.Status}");
        foreach (var item in result.Findings) Console.WriteLine($"- [{item.Severity}] {item.Method} {item.Path}: {item.Message}");
        foreach (var error in result.Errors) Console.WriteLine($"- error: {error}");
    }

    private static void RenderDiagnostics(IEnumerable<ApiDiagnostic> diagnostics)
    {
        foreach (var item in diagnostics) Console.WriteLine($"diagnostic={item.Code};severity={item.Severity};rule={item.Rule};message={Safe(item.Message)};remediation={Safe(item.Remediation)};evidence={Safe(string.Join(',', item.Evidence))}");
    }
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
}
