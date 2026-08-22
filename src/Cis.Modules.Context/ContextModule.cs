using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Context;

public sealed class ContextModule : ICisModule
{
    private static readonly string[] SupportedFormats = ["human", "json", "agent"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Name => "context";

    public string Description => "Retrieve bounded, evidence-backed engineering context from the local graph.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ContextPackService>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var queryService = services.GetRequiredService<ICisGraphQueryService>();
        var context = new Command(Name, Description);
        context.Subcommands.Add(CreateSearchCommand(queryService));
        context.Subcommands.Add(CreateFocusedCommand(
            "contract",
            "Retrieve a governed contract and its immediate ownership, implementation, and test evidence.",
            "reference-item",
            ["declares", "owns", "implemented-by", "consumed-by", "produced-by", "verified-by"],
            queryService));
        context.Subcommands.Add(CreateFocusedCommand(
            "symbol",
            "Retrieve a symbol and its immediate source, component, dependency, implementation, and test evidence.",
            "symbol",
            ["contains", "belongs-to", "owns", "depends-on", "implemented-by", "verified-by"],
            queryService));
        context.Subcommands.Add(CreateFocusedCommand(
            "references",
            "Retrieve canonical and discovered documents or reference items connected to an exact target.",
            null,
            ["contains", "declares", "references", "governed-by", "describes"],
            queryService));
        context.Subcommands.Add(CreateFocusedCommand(
            "callers",
            "Retrieve explicit dependency, consumer, subscriber, and reference evidence connected to an exact target.",
            null,
            ["calls", "depends-on", "consumed-by", "subscribes-to", "references"],
            queryService));
        context.Subcommands.Add(CreateFocusedCommand(
            "tests-for",
            "Retrieve tests connected to an exact graph target by explicit verification evidence.",
            null,
            ["verified-by"],
            queryService));
        context.Subcommands.Add(CreatePackCommand(
            services.GetRequiredService<ContextPackService>(),
            services.GetRequiredService<ICisWorkspaceRegistry>(),
            services.GetService<ICisTokenSavingsCollector>()));
        commands.Add(context);
    }

    private static Command CreateSearchCommand(ICisGraphQueryService service)
    {
        var command = new Command("search", "Search graph-backed engineering context by identity, type, facet, or bounded text.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var id = new Option<string?>("--id")
        {
            Description = "Exact full graph key or node-local ID.",
        };
        var kind = new Option<string?>("--kind")
        {
            Description = "Exact node kind, such as component, document, reference-item, symbol, or test.",
        };
        var subtype = new Option<string?>("--subtype")
        {
            Description = "Exact node subtype, such as api-operation, class, or xunit.",
        };
        var facet = new Option<string?>("--facet")
        {
            Description = "Required node facet, such as contract, test, or dependency.",
        };
        var text = new Option<string?>("--text")
        {
            Description = "Case-insensitive bounded text match over labels, IDs, and properties.",
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum result nodes, from 1 to 1000.",
            DefaultValueFactory = _ => 20,
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(id);
        command.Options.Add(kind);
        command.Options.Add(subtype);
        command.Options.Add(facet);
        command.Options.Add(text);
        command.Options.Add(limit);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Find(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                new CisGraphFindRequest(
                    parseResult.GetValue(id),
                    parseResult.GetValue(kind),
                    parseResult.GetValue(subtype),
                    parseResult.GetValue(facet),
                    parseResult.GetValue(text),
                    parseResult.GetValue(limit)));
            GraphModule.RenderQuery(Annotate(result, "search"), selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateFocusedCommand(
        string name,
        string description,
        string? kind,
        IReadOnlyList<string> edgeTypes,
        ICisGraphQueryService service)
    {
        var command = new Command(name, description);
        var id = new Option<string>("--id")
        {
            Description = "Required full graph key or exact node-local ID.",
            Required = true,
        };
        var repo = CreateRepositoryOption();
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum nodes including the target, from 1 to 1000.",
            DefaultValueFactory = _ => 100,
        };
        var includeProposed = new Option<bool>("--include-proposed")
        {
            Description = "Include proposed relationships. Rejected relationships remain excluded.",
        };
        var format = CreateFormatOption();
        command.Options.Add(id);
        command.Options.Add(repo);
        command.Options.Add(limit);
        command.Options.Add(includeProposed);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = service.Related(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                new CisGraphRelatedRequest(
                    parseResult.GetValue(id) ?? string.Empty,
                    kind,
                    edgeTypes,
                    "both",
                    Depth: 1,
                    parseResult.GetValue(limit),
                    parseResult.GetValue(includeProposed)));
            GraphModule.RenderQuery(Annotate(result, name), selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreatePackCommand(
        ContextPackService service,
        ICisWorkspaceRegistry workspaceRegistry,
        ICisTokenSavingsCollector? savingsCollector)
    {
        var command = new Command("pack", "Create a deterministic, budget-bounded Markdown context pack from an exact graph target.");
        var id = new Option<string>("--id")
        {
            Description = "Required full graph key or exact node-local ID.",
            Required = true,
        };
        var kind = new Option<string?>("--kind")
        {
            Description = "Optional start-node kind used to disambiguate a local ID.",
        };
        var purpose = new Option<string>("--purpose")
        {
            Description = "Task or change for which the context is being assembled.",
            DefaultValueFactory = _ => "engineering change",
        };
        var verification = new Option<string>("--verification")
        {
            Description = "Verification evidence the pack should help locate.",
            DefaultValueFactory = _ => "connected tests and deterministic checks",
        };
        var depth = new Option<int>("--depth")
        {
            Description = "Maximum graph traversal depth, from 1 to 10.",
            DefaultValueFactory = _ => 2,
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum selected graph nodes, from 1 to 1000.",
            DefaultValueFactory = _ => 100,
        };
        var maxCharacters = new Option<int>("--max-chars")
        {
            Description = "Maximum canonical source characters, from 1000 to 1000000.",
            DefaultValueFactory = _ => 50_000,
        };
        var includeProposed = new Option<bool>("--include-proposed")
        {
            Description = "Include proposed relationships. Rejected relationships remain excluded.",
        };
        var output = new Option<string?>("--output")
        {
            Description = "Optional Markdown path beneath .cis/local/context/, or a file name within that directory.",
        };
        var force = new Option<bool>("--force")
        {
            Description = "Replace a different existing pack at the selected output path.",
        };
        var additionalRoot = new Option<string[]>("--root")
        {
            Description = "Additional root as <repository-path-or-registered-id>#<node-id>[#<kind>]. Repeat for multi-root or cross-repository packs.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var repo = new Option<string?>("--repo")
        {
            Description = "Primary repository path, or registered repository ID when --workspace is used. Defaults to the current directory without --workspace.",
        };
        var workspace = new Option<string?>("--workspace")
        {
            Description = "Optional CIS workspace used to resolve --repo and --root repository IDs.",
        };
        var format = CreateFormatOption();
        command.Options.Add(id);
        command.Options.Add(kind);
        command.Options.Add(purpose);
        command.Options.Add(verification);
        command.Options.Add(depth);
        command.Options.Add(limit);
        command.Options.Add(maxCharacters);
        command.Options.Add(includeProposed);
        command.Options.Add(output);
        command.Options.Add(force);
        command.Options.Add(additionalRoot);
        command.Options.Add(repo);
        command.Options.Add(workspace);
        command.Options.Add(format);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var repositorySpecifier = parseResult.GetValue(repo);
            var workspacePath = parseResult.GetValue(workspace);
            CisWorkspace? resolvedWorkspace = null;
            string repositoryPath;
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                var workspaceResolution = workspaceRegistry.Resolve(workspacePath);
                if (!workspaceResolution.IsSuccess || workspaceResolution.Workspace is null)
                {
                    foreach (var error in workspaceResolution.Errors)
                    {
                        Console.Error.WriteLine(error);
                    }

                    return 2;
                }

                if (string.IsNullOrWhiteSpace(repositorySpecifier))
                {
                    Console.Error.WriteLine("--repo <registered-id> is required when --workspace is used.");
                    return 2;
                }

                resolvedWorkspace = workspaceResolution.Workspace;
                var primary = resolvedWorkspace.Repositories.FirstOrDefault(repository =>
                    string.Equals(repository.Id, repositorySpecifier, StringComparison.Ordinal));
                if (primary is null)
                {
                    Console.Error.WriteLine(
                        $"Repository '{repositorySpecifier}' is not registered in workspace '{resolvedWorkspace.WorkspacePath}'.");
                    return 2;
                }

                repositoryPath = primary.RepositoryPath;
            }
            else
            {
                repositoryPath = repositorySpecifier ?? Directory.GetCurrentDirectory();
            }

            if (!TryParseRoots(
                    parseResult.GetValue(additionalRoot),
                    repositoryPath,
                    resolvedWorkspace,
                    out var roots,
                    out var rootError))
            {
                Console.Error.WriteLine(rootError);
                return 2;
            }

            var result = service.Create(new ContextPackRequest(
                repositoryPath,
                parseResult.GetValue(id) ?? string.Empty,
                parseResult.GetValue(kind),
                parseResult.GetValue(purpose) ?? "engineering change",
                parseResult.GetValue(verification) ?? "connected tests and deterministic checks",
                parseResult.GetValue(depth),
                parseResult.GetValue(limit),
                parseResult.GetValue(maxCharacters),
                parseResult.GetValue(includeProposed),
                parseResult.GetValue(output),
                parseResult.GetValue(force),
                roots));
            RenderPack(result, selectedFormat);
            if (result.ExitCode == 0 && result.Sources.Count > 0)
            {
                var baselineTokens = result.Sources.Sum(source =>
                    source.TotalCharacters == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(source.TotalCharacters / 4d)));
                savingsCollector?.Add(new CisTokenSavingsCandidate(
                    baselineTokens,
                    result.EstimatedTokens,
                    "estimated complete selected source documents versus included context-pack excerpts",
                    "high"));
            }
            return result.ExitCode;
        });
        return command;
    }

    private static Option<string> CreateRepositoryOption()
        => new("--repo")
        {
            Description = "Repository path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };

    private static Option<string> CreateFormatOption()
        => new("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };

    private static string? GetFormat(string? value)
    {
        var format = (value ?? "human").ToLowerInvariant();
        if (SupportedFormats.Contains(format, StringComparer.Ordinal))
        {
            return format;
        }

        Console.Error.WriteLine($"Unsupported format '{format}'. Expected human, json, or agent.");
        return null;
    }

    private static bool TryParseRoots(
        IReadOnlyList<string>? values,
        string primaryRepository,
        CisWorkspace? workspace,
        out IReadOnlyList<ContextPackRoot> roots,
        out string? error)
    {
        var parsed = new List<ContextPackRoot>();
        var primaryPath = Path.GetFullPath(primaryRepository);
        foreach (var value in values ?? [])
        {
            var parts = value.Split('#', 3, StringSplitOptions.None);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                roots = [];
                error = $"Invalid --root '{value}'. Expected <repository-path>#<node-id>[#<kind>].";
                return false;
            }

            string repositoryPath;
            try
            {
                var registered = workspace?.Repositories.FirstOrDefault(repository =>
                    string.Equals(repository.Id, parts[0], StringComparison.Ordinal));
                repositoryPath = registered?.RepositoryPath
                    ?? (parts[0] == "."
                        ? primaryPath
                        : Path.IsPathRooted(parts[0])
                            ? Path.GetFullPath(parts[0])
                            : Path.GetFullPath(Path.Combine(primaryPath, parts[0])));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                roots = [];
                error = $"Invalid repository path in --root '{value}': {exception.Message}";
                return false;
            }

            parsed.Add(new ContextPackRoot(
                repositoryPath,
                parts[1],
                parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null));
        }

        roots = parsed;
        error = null;
        return true;
    }

    private static CisGraphQueryResult Annotate(CisGraphQueryResult result, string operation)
    {
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in result.Query)
        {
            query[pair.Key] = pair.Value;
        }

        query["context"] = operation;
        return result with { Query = query };
    }

    private static void RenderPack(ContextPackResult result, string format)
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
                $"freshness={Clean(result.Freshness)};sources={result.Sources.Count};omissions={result.Omissions.Count};" +
                $"estimatedTokens={result.EstimatedTokens};graphTruncated={result.GraphTruncated.ToString().ToLowerInvariant()};" +
                $"contentTruncated={result.ContentTruncated.ToString().ToLowerInvariant()}");
            if (result.Build is not null)
            {
                Console.WriteLine($"buildId={Clean(result.Build.Id)}");
            }

            if (result.OutputPath is not null)
            {
                Console.WriteLine($"output={Clean(result.OutputPath)}");
            }

            if (result.StartNode is not null)
            {
                Console.WriteLine($"start={Clean(result.StartNode.Key)}");
            }

            foreach (var root in result.Roots)
            {
                Console.WriteLine(
                    $"root={Clean(root.StartNode.Key)};repositoryId={Clean(root.RepositoryId)};repository={Clean(root.RepositoryPath)};" +
                    $"buildId={Clean(root.Build.Id)};freshness={Clean(root.Freshness)}");
            }

            foreach (var source in result.Sources)
            {
                Console.WriteLine(
                    $"source={Clean(source.Path)};repositoryId={Clean(source.RepositoryId)};repository={Clean(source.RepositoryPath)};" +
                    $"factClass={source.FactClass};selected={source.SelectedCharacters};" +
                    $"included={source.IncludedCharacters};total={source.TotalCharacters};estimatedTokens={source.EstimatedTokens};" +
                    $"truncated={source.Truncated.ToString().ToLowerInvariant()};evidence={source.TotalEvidence};" +
                    $"evidenceCompacted={source.OmittedEvidence};reason={Clean(string.Join(",", source.Reasons))}");
                const int agentEvidenceLimit = 3;
                foreach (var evidence in source.Evidence.Take(agentEvidenceLimit))
                {
                    Console.WriteLine($"evidence.{Clean(source.Path)}={FormatAgentEvidence(evidence)}");
                }
                if (source.Evidence.Count > agentEvidenceLimit || source.OmittedEvidence > 0)
                {
                    Console.WriteLine($"evidenceOmitted.{Clean(source.Path)}={Math.Max(0, source.Evidence.Count - agentEvidenceLimit) + source.OmittedEvidence}");
                }
            }

            foreach (var omission in result.Omissions)
            {
                Console.WriteLine(
                    $"omission={Clean(omission.Path)};repositoryId={Clean(omission.RepositoryId ?? string.Empty)};" +
                    $"repository={Clean(omission.RepositoryPath ?? string.Empty)};reason={Clean(omission.Reason)}");
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                Console.WriteLine($"diagnostic={Clean(diagnostic.Code)};severity={diagnostic.Severity};message={Clean(diagnostic.Message)}");
            }

            return;
        }

        Console.WriteLine($"Context pack: {result.Status}");
        if (result.OutputPath is not null)
        {
            Console.WriteLine($"Output: {result.OutputPath}");
        }

        Console.WriteLine($"Roots: {result.Roots.Count}; sources: {result.Sources.Count}; omissions: {result.Omissions.Count}; estimated tokens: {result.EstimatedTokens}");
        Console.WriteLine($"Freshness: {result.Freshness}; graph truncated: {result.GraphTruncated}; content truncated: {result.ContentTruncated}");
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static string Clean(string value)
        => value.Replace(";", ",", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string FormatAgentEvidence(ContextPackEvidence evidence)
    {
        var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(evidence.NodeKey))
        {
            fields.Add("node:" + Clean(evidence.NodeKey));
        }
        if (!string.IsNullOrWhiteSpace(evidence.EdgeType))
        {
            fields.Add("edge:" + Clean(evidence.EdgeType));
        }
        if (!string.IsNullOrWhiteSpace(evidence.RelationshipState))
        {
            fields.Add("state:" + Clean(evidence.RelationshipState));
        }
        if (!string.IsNullOrWhiteSpace(evidence.Confidence))
        {
            fields.Add("confidence:" + Clean(evidence.Confidence));
        }
        fields.Add($"locator:{Clean(evidence.LocatorKind)}:{Clean(evidence.LocatorValue)}");
        if (!string.IsNullOrWhiteSpace(evidence.Method))
        {
            fields.Add("method:" + Clean(evidence.Method));
        }
        return string.Join(',', fields);
    }
}
