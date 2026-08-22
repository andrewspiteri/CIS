using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Docs;

public sealed class DocsModule : ICisModule
{
    private static readonly string[] SupportedFormats = ["human", "json", "agent"];

    public string Name => "docs";

    public string Description => "Inventory and validate repository-owned documentation.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DocumentationCatalogReader>();
        services.AddSingleton<MarkdownDocumentInspector>();
        services.AddSingleton<DocumentationInventoryService>();
        services.AddSingleton<DocumentationValidationService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, DocumentationDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var inventoryService = services.GetRequiredService<DocumentationInventoryService>();
        var validationService = services.GetRequiredService<DocumentationValidationService>();
        var docs = new Command(Name, Description);

        docs.Subcommands.Add(CreateInventoryCommand(inventoryService));
        docs.Subcommands.Add(CreateValidateCommand(validationService));
        commands.Add(docs);
    }

    private static Command CreateInventoryCommand(DocumentationInventoryService service)
    {
        var command = new Command("inventory", "Inventory Markdown in the configured documentation root.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        command.Options.Add(repo);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult, format);
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Inventory(parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory());
            RenderInventory(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateValidateCommand(DocumentationValidationService service)
    {
        var command = new Command("validate", "Validate the documentation catalog against repository files.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var strict = new Option<bool>("--strict")
        {
            Description = "Treat documentation warnings as validation failures.",
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(strict);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult, format);
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Validate(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(strict));
            RenderValidation(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> CreateRepositoryOption() => new("--repo")
    {
        Description = "Repository path. Defaults to the current directory.",
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
    };

    private static Option<string> CreateFormatOption() => new("--format")
    {
        Description = "Output format: human, json, or agent.",
        DefaultValueFactory = _ => "human",
    };

    private static string? GetFormat(ParseResult parseResult, Option<string> option)
    {
        var format = (parseResult.GetValue(option) ?? "human").ToLowerInvariant();
        if (SupportedFormats.Contains(format, StringComparer.Ordinal))
        {
            return format;
        }

        Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent.");
        return null;
    }

    private static void RenderInventory(DocumentationInventoryResult result, string format)
    {
        if (format == "json")
        {
            RenderJson(result);
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};documents={result.Documents.Count};" +
                $"warnings={result.Warnings.Count};errors={result.Errors.Count}");
            Console.WriteLine($"repository={result.RepositoryPath ?? string.Empty}");
            Console.WriteLine($"documentationRoot={result.DocumentationRoot ?? string.Empty}");
            foreach (var document in result.Documents)
            {
                Console.WriteLine(
                    $"document={document.Id};path={document.Path};type={document.Type};" +
                    $"authority={document.Authority};cataloged={document.Cataloged.ToString().ToLowerInvariant()}");
            }

            RenderAgentMessages("warning", result.Warnings);
            RenderAgentMessages("error", result.Errors);
            return;
        }

        Console.WriteLine($"Documentation inventory: {result.Status}");
        Console.WriteLine($"Documents: {result.Documents.Count}");
        foreach (var document in result.Documents)
        {
            var catalogState = document.Cataloged ? "cataloged" : "uncataloged";
            Console.WriteLine($"{document.Path} [{document.Type}, {catalogState}] - {document.Title}");
        }

        RenderHumanMessages("Warning", result.Warnings);
        RenderHumanMessages("Error", result.Errors);
    }

    private static void RenderValidation(DocumentationValidationResult result, string format)
    {
        if (format == "json")
        {
            RenderJson(result);
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};valid={result.IsValid.ToString().ToLowerInvariant()};" +
                $"catalogEntries={result.CatalogEntries};markdownDocuments={result.MarkdownDocuments};" +
                $"warnings={result.Warnings.Count};errors={result.Errors.Count};" +
                $"strict={result.Strict.ToString().ToLowerInvariant()}");
            Console.WriteLine($"repository={result.RepositoryPath ?? string.Empty}");
            Console.WriteLine($"documentationRoot={result.DocumentationRoot ?? string.Empty}");
            RenderAgentMessages("warning", result.Warnings);
            RenderAgentMessages("error", result.Errors);
            return;
        }

        Console.WriteLine($"Documentation validation: {result.Status}");
        Console.WriteLine($"Catalog entries: {result.CatalogEntries}");
        Console.WriteLine($"Markdown documents: {result.MarkdownDocuments}");
        RenderHumanMessages("Warning", result.Warnings);
        RenderHumanMessages("Error", result.Errors);
    }

    private static void RenderJson<T>(T result)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
    }

    private static void RenderAgentMessages(string name, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Console.WriteLine($"{name}={message}");
        }
    }

    private static void RenderHumanMessages(string label, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Console.WriteLine($"{label}: {message}");
        }
    }
}
