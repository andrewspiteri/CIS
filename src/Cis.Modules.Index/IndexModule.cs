using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cis.Modules.Index;

public sealed class IndexModule : ICisModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "index";

    public string Description => "Build and query incremental, non-authoritative per-file routing cards.";

    public void RegisterServices(IServiceCollection services)
    {
        services.TryAddSingleton<ICisTextGenerationService, UnavailableTextGenerationService>();
        services.AddSingleton<FileIndexService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, FileIndexDoctorCheck>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var service = services.GetRequiredService<FileIndexService>();
        var index = new Command(Name, Description);
        index.Subcommands.Add(CreateBuildCommand(service));
        index.Subcommands.Add(CreateStatusCommand(service));
        index.Subcommands.Add(CreateFindCommand(
            service,
            services.GetService<ICisTokenSavingsCollector>()));
        commands.Add(index);
    }

    private static Command CreateBuildCommand(FileIndexService service)
    {
        var command = new Command("build", "Generate or refresh content-hashed file routing cards.");
        var repo = RepositoryOption();
        var path = PathOption();
        var provider = new Option<string?>("--provider")
        {
            Description = "Provider name. Auto-selection considers local providers only.",
        };
        var model = new Option<string?>("--model")
        {
            Description = "Exact provider model. Defaults to the smallest available local model.",
        };
        var allowRemote = new Option<bool>("--allow-remote")
        {
            Description = "Explicitly authorize sending selected file content to a remote provider.",
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum new or refreshed model calls. Zero means unlimited.",
            DefaultValueFactory = _ => 0,
        };
        var maxInput = new Option<int>("--max-input-chars")
        {
            Description = "Maximum source characters submitted per file, from 1000 to 100000.",
            DefaultValueFactory = _ => 12_000,
        };
        var refresh = new Option<bool>("--refresh")
        {
            Description = "Regenerate matching cards even when source hashes are unchanged.",
        };
        var format = FormatOption();
        foreach (var option in new Option[] { repo, path, provider, model, allowRemote, limit, maxInput, refresh, format })
        {
            command.Options.Add(option);
        }

        command.SetAction(parseResult =>
        {
            var selectedFormat = ParseFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Build(new FileIndexBuildRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(path),
                parseResult.GetValue(provider),
                parseResult.GetValue(model),
                parseResult.GetValue(allowRemote),
                parseResult.GetValue(limit),
                parseResult.GetValue(maxInput),
                parseResult.GetValue(refresh)));
            RenderBuild(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateStatusCommand(FileIndexService service)
    {
        var command = new Command("status", "Report card coverage and source-hash freshness without invoking a model.");
        var repo = RepositoryOption();
        var path = PathOption();
        var format = FormatOption();
        command.Options.Add(repo);
        command.Options.Add(path);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = ParseFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Status(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(path));
            RenderStatus(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateFindCommand(
        FileIndexService service,
        ICisTokenSavingsCollector? savingsCollector)
    {
        var command = new Command("find", "Find likely source files through cached routing summaries and paths.");
        var repo = RepositoryOption();
        var text = new Option<string>("--text")
        {
            Description = "Required routing search text.",
            Required = true,
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum matching cards, from 1 to 1000.",
            DefaultValueFactory = _ => 20,
        };
        var format = FormatOption();
        command.Options.Add(repo);
        command.Options.Add(text);
        command.Options.Add(limit);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = ParseFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var repositoryPath = parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory();
            var result = service.Find(
                repositoryPath,
                parseResult.GetValue(text) ?? string.Empty,
                parseResult.GetValue(limit));
            RenderFind(result, selectedFormat);
            if (result.ExitCode == 0 && result.Cards.Count > 0)
            {
                var root = Path.GetFullPath(repositoryPath);
                var baselineTokens = result.Cards.Sum(card => EstimateSourceTokens(root, card.Path));
                savingsCollector?.Add(new CisTokenSavingsCandidate(
                    baselineTokens,
                    null,
                    "estimated source-file content for matched cards versus the compact routing result",
                    "medium"));
            }
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> RepositoryOption() => new("--repo")
    {
        Description = "Initialized repository path. Defaults to the current directory.",
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
    };

    private static Option<string?> PathOption() => new("--path")
    {
        Description = "Optional repository-relative file or directory selection.",
    };

    private static Option<string> FormatOption() => new("--format")
    {
        Description = "Output format: human, json, or agent.",
        DefaultValueFactory = _ => "human",
    };

    private static string? ParseFormat(string? value)
    {
        var format = (value ?? "human").Trim().ToLowerInvariant();
        if (format is "human" or "json" or "agent")
        {
            return format;
        }

        Console.Error.WriteLine("Unsupported format. Use human, json, or agent.");
        return null;
    }

    private static void RenderBuild(FileIndexBuildResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()};" +
                $"candidates={result.Candidates};indexed={result.Indexed};generated={result.Generated};" +
                $"reused={result.Reused};pending={result.Pending};removed={result.Removed};" +
                $"provider={Normalize(result.Provider)};model={Normalize(result.Model)}");
            WriteDetails(result.OutputPath, result.Skipped, result.Errors);
            return;
        }

        Console.WriteLine($"File index: {result.Status}");
        Console.WriteLine($"Candidates: {result.Candidates}; indexed: {result.Indexed}; generated: {result.Generated}; reused: {result.Reused}; pending: {result.Pending}");
        Console.WriteLine($"Provider: {result.Provider ?? "none"}; model: {result.Model ?? "none"}");
        WriteHumanDetails(result.OutputPath, result.Skipped, result.Errors);
    }

    private static void RenderStatus(FileIndexStatusResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine(
                $"status={result.Status};exitCode={result.ExitCode};candidates={result.Candidates};indexed={result.Indexed};" +
                $"fresh={result.Fresh};stale={result.Stale};missing={result.Missing};removed={result.Removed};" +
                $"cached={result.Cached.ToString().ToLowerInvariant()}");
            WriteDetails(result.OutputPath, [], result.Errors);
            return;
        }

        Console.WriteLine($"File index: {result.Status}");
        Console.WriteLine($"Candidates: {result.Candidates}; indexed: {result.Indexed}; fresh: {result.Fresh}; stale: {result.Stale}; missing: {result.Missing}; removed: {result.Removed}");
        Console.WriteLine($"Status source: {(result.Cached ? "cache" : "filesystem scan")}");
        WriteHumanDetails(result.OutputPath, [], result.Errors);
    }

    private static void RenderFind(FileIndexFindResult result, string format)
    {
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (format == "agent")
        {
            Console.WriteLine($"status={result.Status};exitCode={result.ExitCode};matches={result.Cards.Count};query={Normalize(result.Query)}");
            foreach (var card in result.Cards)
            {
                Console.WriteLine($"card={Normalize(card.Path)};summary={Normalize(card.Summary)};sourceHash={card.SourceHash}");
            }

            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={Normalize(error)}");
            }

            return;
        }

        Console.WriteLine($"File index search: {result.Status}; matches: {result.Cards.Count}");
        foreach (var card in result.Cards)
        {
            Console.WriteLine($"- {card.Path}: {card.Summary}");
        }
    }

    private static void WriteDetails(string? outputPath, IReadOnlyList<string> skipped, IReadOnlyList<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine($"output={Normalize(outputPath)}");
        }

        foreach (var item in skipped)
        {
            Console.WriteLine($"skipped={Normalize(item)}");
        }

        foreach (var error in errors)
        {
            Console.WriteLine($"error={Normalize(error)}");
        }
    }

    private static void WriteHumanDetails(string? outputPath, IReadOnlyList<string> skipped, IReadOnlyList<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine($"Output: {outputPath}");
        }

        foreach (var item in skipped)
        {
            Console.WriteLine($"Skipped: {item}");
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine($"Error: {error}");
        }
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();

    private static int EstimateSourceTokens(string repositoryPath, string relativePath)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(repositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.TrimEndingDirectorySeparator(repositoryPath) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !File.Exists(path))
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Ceiling(new FileInfo(path).Length / 4d));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
