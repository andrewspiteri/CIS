using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cis.Modules.Repository;

public sealed class WorkspaceModule : ICisModule
{
    private static readonly string[] SupportedFormats = ["human", "json", "agent"];

    public string Name => "workspace";

    public string Description => "Initialize a documentation-authority workspace for multiple repositories.";

    public void RegisterServices(IServiceCollection services)
    {
        services.TryAddSingleton<ICisRepositoryContextResolver, CisRepositoryContextResolver>();
        services.TryAddSingleton<WorkspaceRegistry>();
        services.TryAddSingleton<ICisWorkspaceRegistry>(provider =>
            provider.GetRequiredService<WorkspaceRegistry>());
        services.TryAddSingleton<RepositoryInitializer>();
        services.TryAddSingleton<WorkspaceInitializer>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var workspace = new Command(Name, Description);
        workspace.Subcommands.Add(CreateInitCommand(
            services.GetRequiredService<WorkspaceInitializer>()));
        commands.Add(workspace);
    }

    private static Command CreateInitCommand(WorkspaceInitializer initializer)
    {
        var command = new Command(
            "init",
            "Initialize the current repository as the canonical documentation authority for a CIS workspace.");
        var repo = new Option<string>("--repo")
        {
            Description = "Documentation-authority repository path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };
        var root = new Option<string>("--root")
        {
            Description = "Required repository-relative documentation root.",
            Required = true,
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Plan authority repository and workspace changes without writing.",
        };
        var yes = new Option<bool>("--yes")
        {
            Description = "Confirm the reviewed authority repository and workspace changes.",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        command.Options.Add(repo);
        command.Options.Add(root);
        command.Options.Add(dryRun);
        command.Options.Add(yes);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = (parseResult.GetValue(format) ?? "human").ToLowerInvariant();
            if (!SupportedFormats.Contains(selectedFormat, StringComparer.Ordinal))
            {
                Console.Error.WriteLine(
                    $"Unsupported format '{selectedFormat}'. Expected human, json, or agent.");
                return 2;
            }

            var result = initializer.Initialize(new WorkspaceInitRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(root) ?? string.Empty,
                parseResult.GetValue(dryRun),
                parseResult.GetValue(yes)));
            Render(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static void Render(WorkspaceInitResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }));
            return;
        }

        if (format == "agent")
        {
            foreach (var line in RenderAgentLines(result))
            {
                Console.WriteLine(line);
            }

            return;
        }

        Console.WriteLine($"Workspace initialization: {result.Status}");
        Console.WriteLine($"Workspace: {result.WorkspacePath ?? string.Empty}");
        Console.WriteLine($"Authority: {result.AuthorityRepositoryId ?? string.Empty}");
        Console.WriteLine($"Documentation root: {result.DocumentationRoot ?? string.Empty}");
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        foreach (var collision in result.Collisions)
        {
            Console.WriteLine($"Collision: {collision}");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }

        if (result.ConfirmationRequired)
        {
            Console.WriteLine("No files were changed. Review the plan and rerun with --yes.");
        }
    }

    internal static IReadOnlyList<string> RenderAgentLines(WorkspaceInitResult result)
    {
        var lines = new List<string>
        {
            $"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};" +
            $"confirmationRequired={result.ConfirmationRequired.ToString().ToLowerInvariant()}",
            $"workspace={Clean(result.WorkspacePath)}",
            $"configuration={Clean(result.ConfigurationPath)}",
            $"authority={Clean(result.AuthorityRepositoryId)}",
            $"documentationRoot={Clean(result.DocumentationRoot)}",
        };

        if (result.RepositoryInitialization is { } repository)
        {
            lines.Add(
                $"repositoryInitialization={repository.Status};applied={repository.Applied.ToString().ToLowerInvariant()};" +
                $"confirmationRequired={repository.ConfirmationRequired.ToString().ToLowerInvariant()};" +
                $"createDirectories={repository.DirectoriesToCreate.Count};createFiles={repository.FilesToCreate.Count};" +
                $"updateFiles={repository.FilesToUpdate.Count};quarantined={repository.QuarantinedPaths.Count};" +
                $"retained={repository.RetainedPaths.Count};warnings={repository.Warnings.Count};" +
                $"collisions={repository.Collisions.Count};errors={repository.Errors.Count}");

            if (repository.Classification is { } classification)
            {
                lines.Add($"classification={Clean(classification.Shape)};components={classification.Components.Count}");
                foreach (var component in classification.Components)
                {
                    lines.Add(
                        $"component={Clean(component.Id)};root={Clean(component.Root)};" +
                        $"languages={Clean(string.Join(',', component.Languages))};" +
                        $"frameworks={Clean(string.Join(',', component.Frameworks))};" +
                        $"roles={Clean(string.Join(',', component.Roles))};" +
                        $"capabilities={Clean(string.Join(',', component.Capabilities))};" +
                        $"confidence={Clean(component.Confidence)}");
                    AddAgentItems(lines, $"evidence.{Clean(component.Id)}", component.Evidence);
                }
            }

            const int starterLimit = 20;
            foreach (var selection in repository.StarterSelections.Take(starterLimit))
            {
                lines.Add($"starter={Clean(selection.Definition)};reason={Clean(selection.Reason)}");
            }
            if (repository.StarterSelections.Count > starterLimit)
            {
                lines.Add($"starter.omitted={repository.StarterSelections.Count - starterLimit}");
            }

            AddAgentItems(lines, "createDirectory", repository.DirectoriesToCreate);
            AddAgentItems(lines, "createFile", repository.FilesToCreate);
            AddAgentItems(lines, "updateFile", repository.FilesToUpdate);
            AddAgentItems(lines, "quarantine", repository.QuarantinedPaths);
            AddAgentItems(lines, "retain", repository.RetainedPaths);
        }

        AddAgentItems(lines, "warning", result.Warnings);
        AddAgentItems(lines, "collision", result.Collisions);
        AddAgentItems(lines, "error", result.Errors);
        return lines;
    }

    private static void AddAgentItems(List<string> lines, string name, IEnumerable<string> values)
    {
        const int limit = 10;
        var materialized = values.ToArray();
        foreach (var value in materialized.Take(limit))
        {
            lines.Add($"{name}={Clean(value)}");
        }

        if (materialized.Length > limit)
        {
            lines.Add($"{name}.omitted={materialized.Length - limit}");
        }
    }

    private static string Clean(string? value)
        => (value ?? string.Empty).Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
