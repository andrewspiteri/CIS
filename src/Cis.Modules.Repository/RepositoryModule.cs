using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Repository;

public sealed class RepositoryModule : ICisModule
{
    private static readonly string[] SupportedFormats = ["human", "json", "agent"];

    public string Name => "repo";

    public string Description => "Initialize, inspect, and maintain CIS repository state.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisRepositoryContextResolver, CisRepositoryContextResolver>();
        services.AddSingleton<WorkspaceRegistry>();
        services.AddSingleton<ICisWorkspaceRegistry>(services =>
            services.GetRequiredService<WorkspaceRegistry>());
        services.AddSingleton<RepositoryInitializer>();
        services.AddSingleton<RepositoryImporter>();
        services.AddSingleton<IOllamaProbe, OllamaProbe>();
        services.AddSingleton<ICisRepositoryDoctorCheck, JavaScriptToolingDoctorCheck>();
        services.AddSingleton<ICisRepositoryDoctorCheck, TestSuiteProfileDoctorCheck>();
        services.AddSingleton<RepositoryDoctor>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var initializer = services.GetRequiredService<RepositoryInitializer>();
        var doctorService = services.GetRequiredService<RepositoryDoctor>();
        var savingsCollector = services.GetService<ICisTokenSavingsCollector>();
        var repository = new Command(Name, Description);
        var init = new Command("init", "Initialize CIS in a target repository.");
        var root = new Option<string>("--root")
        {
            Description = "Required repository-relative documentation root.",
            Required = true,
        };
        var repo = new Option<string>("--repo")
        {
            Description = "Repository path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Plan and report changes without writing files.",
        };
        var yes = new Option<bool>("--yes")
        {
            Description = "Confirm the displayed classification and repository artifact changes.",
        };
        var acceptCurrent = new Option<bool>("--accept-current")
        {
            Description = "After review, retain conflicting current files as human-owned canonical artifacts.",
        };
        var quarantineObsolete = new Option<bool>("--quarantine-obsolete")
        {
            Description = "Move unchanged, obsolete CIS-managed artifacts into a recoverable .cis/quarantine tree.",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        var details = new Option<bool>("--details")
        {
            Description = "Include component, starter, and retained-path detail in agent output.",
        };

        init.Options.Add(root);
        init.Options.Add(repo);
        init.Options.Add(dryRun);
        init.Options.Add(yes);
        init.Options.Add(acceptCurrent);
        init.Options.Add(quarantineObsolete);
        init.Options.Add(format);
        init.Options.Add(details);
        init.SetAction(parseResult =>
        {
            var selectedFormat = (parseResult.GetValue(format) ?? "human").ToLowerInvariant();
            if (!SupportedFormats.Contains(selectedFormat, StringComparer.Ordinal))
            {
                Console.Error.WriteLine(
                    $"Unsupported format '{selectedFormat}'. Expected human, json, or agent.");
                return 2;
            }

            var request = new RepositoryInitRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(root) ?? string.Empty,
                parseResult.GetValue(dryRun),
                parseResult.GetValue(yes),
                parseResult.GetValue(acceptCurrent),
                parseResult.GetValue(quarantineObsolete));
            var result = initializer.Initialize(request);
            if (selectedFormat == "agent" && !parseResult.GetValue(details))
                savingsCollector?.Add(new CisTokenSavingsCandidate(
                    EstimateDetailedAgentTokens(result),
                    null,
                    "deterministic compact repo-init output versus the same result with --details",
                    "high"));
            Render(result, selectedFormat, parseResult.GetValue(details));
            return result.ExitCode;
        });

        repository.Subcommands.Add(init);
        repository.Subcommands.Add(CreateImportCommand(
            services.GetRequiredService<RepositoryImporter>()));
        repository.Subcommands.Add(CreateListCommand(
            services.GetRequiredService<ICisWorkspaceRegistry>()));
        repository.Subcommands.Add(CreateDoctorCommand(doctorService));
        commands.Add(repository);
    }

    private static Command CreateImportCommand(RepositoryImporter importer)
    {
        var command = new Command(
            "import",
            "Initialize and register one or more existing repositories in a CIS workspace.");
        var workspace = new Option<string>("--workspace")
        {
            Description = "Workspace path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };
        var source = new Option<string[]>("--source")
        {
            Description = "Existing repository path. Repeat or provide several values, up to 20.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var root = new Option<string>("--root")
        {
            Description = "Required repository-relative documentation root used for every imported repository.",
            Required = true,
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Plan initialization and workspace registration without writing files.",
        };
        var yes = new Option<bool>("--yes")
        {
            Description = "Confirm all repository initialization and workspace registry changes.",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        command.Options.Add(workspace);
        command.Options.Add(source);
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

            var result = importer.Import(new RepositoryImportRequest(
                parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(root) ?? string.Empty,
                parseResult.GetValue(source) ?? [],
                parseResult.GetValue(dryRun),
                parseResult.GetValue(yes)));
            RenderImport(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateListCommand(ICisWorkspaceRegistry registry)
    {
        var command = new Command("list", "List repositories registered in a CIS workspace.");
        var workspace = new Option<string>("--workspace")
        {
            Description = "Workspace path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        command.Options.Add(workspace);
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

            var result = registry.Resolve(
                parseResult.GetValue(workspace) ?? Directory.GetCurrentDirectory());
            RenderWorkspace(result, selectedFormat);
            return result.IsSuccess ? 0 : 2;
        });
        return command;
    }

    private static Command CreateDoctorCommand(RepositoryDoctor doctor)
    {
        var command = new Command(
            "doctor",
            "Inspect CIS repository readiness and report evidence-backed suggested fixes.");
        var repo = new Option<string>("--repo")
        {
            Description = "Repository path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };
        var root = new Option<string>("--root")
        {
            Description = "Optional documentation-root hint when diagnosing a failed initialization.",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        command.Options.Add(repo);
        command.Options.Add(root);
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

            var result = doctor.Inspect(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(root));
            RenderDoctor(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static void Render(RepositoryInitResult result, string format, bool details = false)
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
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};" +
                $"confirmationRequired={result.ConfirmationRequired.ToString().ToLowerInvariant()}");
            Console.WriteLine($"repository={result.RepositoryPath ?? string.Empty}");
            Console.WriteLine($"documentationRoot={result.DocumentationRoot ?? string.Empty}");
            if (result.Classification is not null)
            {
                Console.WriteLine(
                    $"classification={result.Classification.Shape};components={result.Classification.Components.Count}");
                foreach (var component in details ? result.Classification.Components : [])
                {
                    Console.WriteLine(
                        $"component={component.Id};root={component.Root};languages={string.Join(',', component.Languages)};" +
                        $"frameworks={string.Join(',', component.Frameworks)};roles={string.Join(',', component.Roles)};" +
                        $"capabilities={string.Join(',', component.Capabilities)};confidence={component.Confidence}");
                    RenderAgentItems($"evidence.{component.Id}", component.Evidence);
                }
            }

            const int starterLimit = 20;
            foreach (var selection in details ? result.StarterSelections.Take(starterLimit) : [])
            {
                Console.WriteLine($"starter={selection.Definition};reason={selection.Reason}");
            }
            if (result.StarterSelections.Count > starterLimit)
            {
                Console.WriteLine($"starter.omitted={result.StarterSelections.Count - starterLimit}");
            }

            RenderAgentItems("createDirectory", result.DirectoriesToCreate);
            RenderAgentItems("createFile", result.FilesToCreate);
            RenderAgentItems("updateFile", result.FilesToUpdate);
            RenderAgentItems("quarantine", result.QuarantinedPaths);
            if (details) RenderAgentItems("retain", result.RetainedPaths);
            RenderAgentItems("warning", result.Warnings);
            RenderAgentItems("collision", result.Collisions);
            RenderAgentItems("error", result.Errors);
            return;
        }

        Console.WriteLine($"Repository initialization: {result.Status}");
        if (result.RepositoryPath is not null)
        {
            Console.WriteLine($"Repository: {result.RepositoryPath}");
        }

        if (result.DocumentationRoot is not null)
        {
            Console.WriteLine($"Documentation root: {result.DocumentationRoot}");
        }

        if (result.Classification is not null)
        {
            Console.WriteLine(
                $"Classification: {result.Classification.Shape} ({result.Classification.Components.Count} components)");
            foreach (var component in result.Classification.Components)
            {
                Console.WriteLine(
                    $"Component: {component.Id} [{string.Join(", ", component.Languages)}; " +
                    $"{string.Join(", ", component.Frameworks)}; {string.Join(", ", component.Roles)}]");
                RenderHumanItems("  Evidence", component.Evidence);
            }
        }

        foreach (var selection in result.StarterSelections)
        {
            Console.WriteLine($"Selected starter: {selection.Definition} - {selection.Reason}");
        }

        RenderHumanItems("Create directory", result.DirectoriesToCreate);
        RenderHumanItems("Create file", result.FilesToCreate);
        RenderHumanItems("Update file", result.FilesToUpdate);
        RenderHumanItems("Quarantine", result.QuarantinedPaths);
        RenderHumanItems("Retain", result.RetainedPaths);
        RenderHumanItems("Warning", result.Warnings);
        RenderHumanItems("Collision", result.Collisions);
        RenderHumanItems("Error", result.Errors);

        if (result.ConfirmationRequired)
        {
            Console.WriteLine("No files were changed. Review the plan and rerun with --yes to confirm.");
        }
    }

    private static int EstimateDetailedAgentTokens(RepositoryInitResult result)
    {
        var characters = 400;
        if (result.Classification is not null)
            characters += result.Classification.Components.Sum(component =>
                120 + component.Id.Length + component.Root.Length
                + component.Languages.Sum(x => x.Length) + component.Frameworks.Sum(x => x.Length)
                + component.Roles.Sum(x => x.Length) + component.Capabilities.Sum(x => x.Length)
                + component.Evidence.Sum(x => x.Length + 20));
        characters += result.StarterSelections.Sum(x => x.Definition.Length + x.Reason.Length + 25);
        characters += result.RetainedPaths.Sum(x => x.Length + 10);
        characters += result.FilesToCreate.Concat(result.FilesToUpdate).Concat(result.DirectoriesToCreate)
            .Concat(result.QuarantinedPaths).Concat(result.Warnings).Concat(result.Collisions).Concat(result.Errors)
            .Sum(x => x.Length + 15);
        return Math.Max(1, (int)Math.Ceiling(characters / 4d));
    }

    private static void RenderImport(RepositoryImportResult result, string format)
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
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};" +
                $"confirmationRequired={result.ConfirmationRequired.ToString().ToLowerInvariant()};" +
                $"repositories={result.Repositories.Count}");
            Console.WriteLine($"workspace={result.WorkspacePath ?? string.Empty}");
            Console.WriteLine($"configuration={result.ConfigurationPath ?? string.Empty}");
            foreach (var repository in result.Repositories)
            {
                Console.WriteLine(
                    $"repository={repository.Id};path={NormalizeAgentValue(repository.RepositoryPath)};" +
                    $"documentationRoot={repository.DocumentationRoot};status={repository.Status}");
                RenderAgentItems($"createFile.{repository.Id}", repository.FilesToCreate);
                RenderAgentItems($"updateFile.{repository.Id}", repository.FilesToUpdate);
            }

            RenderAgentItems("warning", result.Warnings.Select(NormalizeAgentValue));
            RenderAgentItems("collision", result.Collisions.Select(NormalizeAgentValue));
            RenderAgentItems("error", result.Errors.Select(NormalizeAgentValue));
            return;
        }

        Console.WriteLine($"Repository import: {result.Status}");
        Console.WriteLine($"Workspace: {result.WorkspacePath ?? string.Empty}");
        Console.WriteLine($"Configuration: {result.ConfigurationPath ?? string.Empty}");
        foreach (var repository in result.Repositories)
        {
            Console.WriteLine(
                $"Repository: {repository.Id} [{repository.Status}] {repository.RepositoryPath} " +
                $"(docs: {repository.DocumentationRoot})");
        }

        RenderHumanItems("Warning", result.Warnings);
        RenderHumanItems("Collision", result.Collisions);
        RenderHumanItems("Error", result.Errors);
        if (result.ConfirmationRequired)
        {
            Console.WriteLine("No files were changed. Review the plan and rerun with --yes to confirm all imports.");
        }
    }

    private static void RenderWorkspace(CisWorkspaceResolution result, string format)
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
            Console.WriteLine(
                $"status={(result.IsSuccess ? "valid" : "invalid")};exitCode={(result.IsSuccess ? 0 : 2)};" +
                $"repositories={result.Workspace?.Repositories.Count ?? 0}");
            Console.WriteLine($"workspace={result.Workspace?.WorkspacePath ?? string.Empty}");
            Console.WriteLine($"configuration={result.Workspace?.ConfigurationPath ?? string.Empty}");
            foreach (var repository in result.Workspace?.Repositories ?? [])
            {
                Console.WriteLine(
                    $"repository={repository.Id};path={NormalizeAgentValue(repository.RepositoryPath)};" +
                    $"documentationRoot={repository.DocumentationRoot};role={repository.Role}");
            }

            RenderAgentItems("error", result.Errors.Select(NormalizeAgentValue));
            return;
        }

        Console.WriteLine($"CIS workspace: {(result.IsSuccess ? "valid" : "invalid")}");
        if (result.Workspace is not null)
        {
            Console.WriteLine($"Workspace: {result.Workspace.WorkspacePath}");
            foreach (var repository in result.Workspace.Repositories)
            {
                Console.WriteLine(
                    $"Repository: {repository.Id} - {repository.RepositoryPath} " +
                    $"(docs: {repository.DocumentationRoot}; role: {repository.Role})");
            }
        }

        RenderHumanItems("Error", result.Errors);
    }

    private static void RenderDoctor(RepositoryDoctorResult result, string format)
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
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};errors={result.ErrorCount};" +
                $"warnings={result.WarningCount};information={result.InformationCount}");
            Console.WriteLine($"repository={result.RepositoryPath ?? string.Empty}");
            Console.WriteLine($"documentationRoot={result.DocumentationRoot ?? string.Empty}");
            Console.WriteLine(
                $"ollama={result.Ollama.Status};endpoint={NormalizeAgentValue(result.Ollama.Endpoint)};" +
                $"models={string.Join(',', result.Ollama.Models.Select(NormalizeAgentValue))}");
            foreach (var finding in result.Findings)
            {
                Console.WriteLine(
                    $"finding={finding.Code};severity={finding.Severity};category={finding.Category};" +
                    $"fixability={finding.Fixability};message={NormalizeAgentValue(finding.Message)}");
                RenderAgentItems(
                    $"evidence.{finding.Code}",
                    finding.Evidence.Select(NormalizeAgentValue));
                Console.WriteLine(
                    $"suggestedFix.{finding.Code}={NormalizeAgentValue(finding.SuggestedFix)}");
                if (!string.IsNullOrWhiteSpace(finding.FixCommand))
                {
                    Console.WriteLine(
                        $"fixCommand.{finding.Code}={NormalizeAgentValue(finding.FixCommand)}");
                }
            }

            return;
        }

        Console.WriteLine($"Repository doctor: {result.Status}");
        if (result.RepositoryPath is not null)
        {
            Console.WriteLine($"Repository: {result.RepositoryPath}");
        }

        if (result.DocumentationRoot is not null)
        {
            Console.WriteLine($"Documentation root: {result.DocumentationRoot}");
        }

        Console.WriteLine(
            $"Ollama: {result.Ollama.Status} ({result.Ollama.Endpoint})" +
            (result.Ollama.Models.Count > 0
                ? $" - {string.Join(", ", result.Ollama.Models)}"
                : string.Empty));
        Console.WriteLine(
            $"Summary: {result.ErrorCount} error(s), {result.WarningCount} warning(s), " +
            $"{result.InformationCount} information item(s)");
        foreach (var finding in result.Findings)
        {
            Console.WriteLine();
            Console.WriteLine($"[{finding.Severity.ToUpperInvariant()}] {finding.Code} - {finding.Message}");
            RenderHumanItems("  Evidence", finding.Evidence);
            Console.WriteLine($"  Suggested fix: {finding.SuggestedFix}");
            if (!string.IsNullOrWhiteSpace(finding.FixCommand))
            {
                Console.WriteLine($"  Command: {finding.FixCommand}");
            }
        }
    }

    private static string NormalizeAgentValue(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();

    private static void RenderAgentItems(string name, IEnumerable<string> values)
    {
        const int limit = 10;
        var materialized = values.ToArray();
        foreach (var value in materialized.Take(limit))
        {
            Console.WriteLine($"{name}={value}");
        }

        if (materialized.Length > limit)
        {
            Console.WriteLine($"{name}.omitted={materialized.Length - limit}");
        }
    }

    private static void RenderHumanItems(string label, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            Console.WriteLine($"{label}: {value}");
        }
    }
}
