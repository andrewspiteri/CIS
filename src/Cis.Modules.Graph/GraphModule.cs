using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Graph;

public sealed class GraphModule : ICisModule
{
    private static readonly string[] SupportedFormats = ["human", "json", "agent"];

    public string Name => "graph";

    public string Description => "Build, validate, and query the derived CIS context graph.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<SqliteGraphStore>();
        services.AddSingleton<GraphBuilder>();
        services.AddSingleton<GraphValidator>();
        services.AddSingleton<GraphExporter>();
        services.AddSingleton<WorkspaceGraphService>();
        services.AddSingleton<ICisRepositoryDoctorCheck, GraphDoctorCheck>();
        services.AddSingleton<ICisGraphQueryService, GraphQueryService>();
        services.AddSingleton<ICisGraphSnapshotReader, GraphSnapshotReader>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var graph = new Command(Name, Description);
        var workspaceGraphs = services.GetRequiredService<WorkspaceGraphService>();
        graph.Subcommands.Add(CreateBuildCommand(
            services.GetRequiredService<GraphBuilder>(),
            workspaceGraphs));
        graph.Subcommands.Add(CreateValidateCommand(
            services.GetRequiredService<GraphValidator>(),
            workspaceGraphs));
        var queryService = services.GetRequiredService<ICisGraphQueryService>();
        graph.Subcommands.Add(CreateFindCommand(queryService));
        graph.Subcommands.Add(CreateRelatedCommand(queryService));
        graph.Subcommands.Add(CreateTraceCommand(queryService));
        graph.Subcommands.Add(CreateExportCommand(services.GetRequiredService<GraphExporter>()));
        commands.Add(graph);
    }

    private static Command CreateFindCommand(ICisGraphQueryService service)
    {
        var command = new Command("find", "Find graph nodes by exact identity, type, facet, or bounded text.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var id = new Option<string?>("--id")
        {
            Description = "Exact full graph key or node-local ID.",
        };
        var kind = new Option<string?>("--kind")
        {
            Description = "Exact node kind, such as component, reference-item, symbol, or test.",
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
            RenderQuery(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateValidateCommand(
        GraphValidator validator,
        WorkspaceGraphService workspaceGraphs)
    {
        var command = new Command("validate", "Validate the stored graph generation without rebuilding it.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var strict = new Option<bool>("--strict")
        {
            Description = "Treat graph validation warnings as failures.",
        };
        var workspace = new Option<string?>("--workspace")
        {
            Description = "Validate every registered repository graph in this CIS workspace instead of one --repo.",
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(strict);
        command.Options.Add(workspace);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var workspacePath = parseResult.GetValue(workspace);
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                var workspaceResult = workspaceGraphs.Validate(
                    workspacePath,
                    parseResult.GetValue(strict));
                RenderWorkspaceValidation(workspaceResult, selectedFormat);
                return workspaceResult.ExitCode;
            }

            var repositoryResult = validator.Validate(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(strict));
            RenderValidation(repositoryResult, selectedFormat);
            return repositoryResult.ExitCode;
        });
        return command;
    }

    private static Command CreateRelatedCommand(ICisGraphQueryService service)
    {
        var command = new Command("related", "Traverse a deterministic bounded neighbourhood around one graph node.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var id = new Option<string>("--id")
        {
            Description = "Required full graph key or node-local ID for the start node.",
            Required = true,
        };
        var kind = new Option<string?>("--kind")
        {
            Description = "Optional start-node kind used to disambiguate a local ID.",
        };
        var edge = new Option<string?>("--edge")
        {
            Description = "Comma-separated edge types to traverse. Defaults to every type.",
        };
        var direction = new Option<string>("--direction")
        {
            Description = "Traversal direction: in, out, or both.",
            DefaultValueFactory = _ => "both",
        };
        var depth = new Option<int>("--depth")
        {
            Description = "Maximum traversal depth, from 1 to 10.",
            DefaultValueFactory = _ => 1,
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum nodes including the start node, from 1 to 1000.",
            DefaultValueFactory = _ => 100,
        };
        var includeProposed = new Option<bool>("--include-proposed")
        {
            Description = "Include proposed edges. Rejected edges remain excluded.",
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(id);
        command.Options.Add(kind);
        command.Options.Add(edge);
        command.Options.Add(direction);
        command.Options.Add(depth);
        command.Options.Add(limit);
        command.Options.Add(includeProposed);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var edgeTypes = (parseResult.GetValue(edge) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = service.Related(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                new CisGraphRelatedRequest(
                    parseResult.GetValue(id) ?? string.Empty,
                    parseResult.GetValue(kind),
                    edgeTypes,
                    (parseResult.GetValue(direction) ?? "both").ToLowerInvariant(),
                    parseResult.GetValue(depth),
                    parseResult.GetValue(limit),
                    parseResult.GetValue(includeProposed)));
            RenderQuery(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateTraceCommand(ICisGraphQueryService service)
    {
        var command = new Command("trace", "Find deterministic shortest graph paths between two exact nodes.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var from = new Option<string>("--from")
        {
            Description = "Required source full graph key or node-local ID.",
            Required = true,
        };
        var to = new Option<string>("--to")
        {
            Description = "Required target full graph key or node-local ID.",
            Required = true,
        };
        var fromKind = new Option<string?>("--from-kind")
        {
            Description = "Optional source-node kind used to disambiguate a local ID.",
        };
        var toKind = new Option<string?>("--to-kind")
        {
            Description = "Optional target-node kind used to disambiguate a local ID.",
        };
        var edge = new Option<string?>("--edge")
        {
            Description = "Comma-separated edge types to traverse. Defaults to every type.",
        };
        var direction = new Option<string>("--direction")
        {
            Description = "Traversal direction: in, out, or both.",
            DefaultValueFactory = _ => "both",
        };
        var maxDepth = new Option<int>("--max-depth")
        {
            Description = "Maximum path depth, from 1 to 10.",
            DefaultValueFactory = _ => 5,
        };
        var maxPaths = new Option<int>("--max-paths")
        {
            Description = "Maximum equal-length shortest paths, from 1 to 100.",
            DefaultValueFactory = _ => 10,
        };
        var includeProposed = new Option<bool>("--include-proposed")
        {
            Description = "Include proposed edges. Rejected edges remain excluded.",
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(from);
        command.Options.Add(to);
        command.Options.Add(fromKind);
        command.Options.Add(toKind);
        command.Options.Add(edge);
        command.Options.Add(direction);
        command.Options.Add(maxDepth);
        command.Options.Add(maxPaths);
        command.Options.Add(includeProposed);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var edgeTypes = (parseResult.GetValue(edge) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = service.Trace(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                new CisGraphTraceRequest(
                    parseResult.GetValue(from) ?? string.Empty,
                    parseResult.GetValue(to) ?? string.Empty,
                    parseResult.GetValue(fromKind),
                    parseResult.GetValue(toKind),
                    edgeTypes,
                    (parseResult.GetValue(direction) ?? "both").ToLowerInvariant(),
                    parseResult.GetValue(maxDepth),
                    parseResult.GetValue(maxPaths),
                    parseResult.GetValue(includeProposed)));
            RenderTrace(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateExportCommand(GraphExporter exporter)
    {
        var command = new Command("export", "Export the validated local graph to a portable representation.");
        var repo = CreateRepositoryOption();
        var format = CreateFormatOption();
        var type = new Option<string>("--type")
        {
            Description = "Export type: markdown, json, jsonl, or dot.",
            DefaultValueFactory = _ => "markdown",
        };
        var output = new Option<string?>("--output")
        {
            Description = "Repository-relative output file. Defaults beneath .cis/local/graph/.",
        };
        var force = new Option<bool>("--force")
        {
            Description = "Replace a differing custom output file. Repository and graph inputs remain protected.",
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(type);
        command.Options.Add(output);
        command.Options.Add(force);
        command.SetAction(parseResult =>
        {
            var selectedFormat = GetFormat(parseResult.GetValue(format));
            if (selectedFormat is null)
            {
                return 2;
            }

            var result = exporter.Export(new GraphExportRequest(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(type) ?? "markdown",
                parseResult.GetValue(output),
                parseResult.GetValue(force)));
            RenderExport(result, selectedFormat);
            return result.ExitCode;
        });
        return command;
    }

    private static Command CreateBuildCommand(
        GraphBuilder builder,
        WorkspaceGraphService workspaceGraphs)
    {
        var command = new Command(
            "build",
            "Build the deterministic local context graph from canonical repository sources.");
        var repo = new Option<string>("--repo")
        {
            Description = "Repository path. Defaults to the current directory.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, or agent.",
            DefaultValueFactory = _ => "human",
        };
        var workspace = new Option<string?>("--workspace")
        {
            Description = "Build every registered repository graph in this CIS workspace instead of one --repo.",
        };
        command.Options.Add(repo);
        command.Options.Add(format);
        command.Options.Add(workspace);
        command.SetAction(parseResult =>
        {
            var selectedFormat = (parseResult.GetValue(format) ?? "human").ToLowerInvariant();
            if (!SupportedFormats.Contains(selectedFormat, StringComparer.Ordinal))
            {
                Console.Error.WriteLine(
                    $"Unsupported format '{selectedFormat}'. Expected human, json, or agent.");
                return 2;
            }

            var workspacePath = parseResult.GetValue(workspace);
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                var workspaceResult = workspaceGraphs.Build(workspacePath);
                RenderWorkspaceBuild(workspaceResult, selectedFormat);
                return workspaceResult.ExitCode;
            }

            var repositoryResult = builder.Build(
                parseResult.GetValue(repo) ?? Directory.GetCurrentDirectory());
            Render(repositoryResult, selectedFormat);
            return repositoryResult.ExitCode;
        });
        return command;
    }

    private static void RenderWorkspaceBuild(WorkspaceGraphBuildResult result, string format)
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
                $"status={result.Status};exitCode={result.ExitCode};repositories={result.Repositories.Count};" +
                $"nodes={result.NodeCount};edges={result.EdgeCount}");
            Console.WriteLine($"workspace={Normalize(result.WorkspacePath)}");
            foreach (var repository in result.Repositories)
            {
                Console.WriteLine(
                    $"repository={Normalize(repository.Id)};path={Normalize(repository.RepositoryPath)};role={repository.Role};" +
                    $"status={repository.Result.Status};exitCode={repository.Result.ExitCode};" +
                    $"nodes={repository.Result.NodeCount};edges={repository.Result.EdgeCount};" +
                    $"warnings={repository.Result.WarningCount};errors={repository.Result.ErrorCount};" +
                    $"buildId={Normalize(repository.Result.BuildId)}");
            }

            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={Normalize(error)}");
            }

            return;
        }

        Console.WriteLine($"Workspace graph build: {result.Status}");
        Console.WriteLine($"Workspace: {result.WorkspacePath ?? string.Empty}");
        foreach (var repository in result.Repositories)
        {
            Console.WriteLine(
                $"Repository: {repository.Id} [{repository.Result.Status}] " +
                $"{repository.Result.NodeCount} nodes, {repository.Result.EdgeCount} edges");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }
    }

    private static void RenderWorkspaceValidation(
        WorkspaceGraphValidationResult result,
        string format)
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
                $"status={result.Status};exitCode={result.ExitCode};repositories={result.Repositories.Count};" +
                $"nodes={result.NodeCount};edges={result.EdgeCount}");
            Console.WriteLine($"workspace={Normalize(result.WorkspacePath)}");
            foreach (var repository in result.Repositories)
            {
                Console.WriteLine(
                    $"repository={Normalize(repository.Id)};path={Normalize(repository.RepositoryPath)};role={repository.Role};" +
                    $"status={repository.Result.Status};exitCode={repository.Result.ExitCode};" +
                    $"freshness={repository.Result.Freshness};nodes={repository.Result.NodeCount};" +
                    $"edges={repository.Result.EdgeCount};warnings={repository.Result.WarningCount};" +
                    $"errors={repository.Result.ErrorCount};buildId={Normalize(repository.Result.BuildId)}");
            }

            foreach (var error in result.Errors)
            {
                Console.WriteLine($"error={Normalize(error)}");
            }

            return;
        }

        Console.WriteLine($"Workspace graph validation: {result.Status}");
        Console.WriteLine($"Workspace: {result.WorkspacePath ?? string.Empty}");
        foreach (var repository in result.Repositories)
        {
            Console.WriteLine(
                $"Repository: {repository.Id} [{repository.Result.Status}] " +
                $"freshness={repository.Result.Freshness}; " +
                $"{repository.Result.NodeCount} nodes, {repository.Result.EdgeCount} edges");
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }
    }

    private static void Render(GraphBuildResult result, string format)
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
                $"nodes={result.NodeCount};edges={result.EdgeCount};errors={result.ErrorCount};" +
                $"warnings={result.WarningCount};information={result.InformationCount}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            Console.WriteLine($"documentationRoot={Normalize(result.DocumentationRoot)}");
            Console.WriteLine($"buildId={Normalize(result.BuildId)}");
            Console.WriteLine($"graphPath={Normalize(result.GraphPath)}");
            Console.WriteLine($"manifestPath={Normalize(result.ManifestPath)}");
            Console.WriteLine($"diagnosticsPath={Normalize(result.DiagnosticsPath)}");
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.WriteLine(
                    $"diagnostic={diagnostic.Code};severity={diagnostic.Severity};" +
                    $"message={Normalize(diagnostic.Message)}");
                foreach (var evidence in diagnostic.Evidence)
                {
                    Console.WriteLine($"evidence.{diagnostic.Code}={Normalize(evidence)}");
                }
            }

            return;
        }

        Console.WriteLine($"Graph build: {result.Status}");
        if (result.RepositoryPath is not null)
        {
            Console.WriteLine($"Repository: {result.RepositoryPath}");
        }

        if (result.BuildId is not null)
        {
            Console.WriteLine($"Build ID: {result.BuildId}");
        }

        Console.WriteLine($"Nodes: {result.NodeCount}");
        Console.WriteLine($"Edges: {result.EdgeCount}");
        if (result.GraphPath is not null)
        {
            Console.WriteLine($"Graph: {result.GraphPath}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine(
                $"[{diagnostic.Severity.ToUpperInvariant()}] {diagnostic.Code}: {diagnostic.Message}");
            foreach (var evidence in diagnostic.Evidence)
            {
                Console.WriteLine($"  Evidence: {evidence}");
            }
        }
    }

    public static void RenderQuery(CisGraphQueryResult result, string format)
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
                $"status={result.Status};exitCode={result.ExitCode};freshness={result.Freshness};" +
                $"nodes={result.Nodes.Count};traversals={result.Traversals.Count};" +
                $"truncated={result.Truncated.ToString().ToLowerInvariant()}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            Console.WriteLine($"graphPath={Normalize(result.GraphPath)}");
            Console.WriteLine($"buildId={Normalize(result.Build?.Id)}");
            foreach (var pair in result.Query)
            {
                Console.WriteLine($"query.{pair.Key}={Normalize(pair.Value)}");
            }

            foreach (var node in result.Nodes)
            {
                Console.WriteLine(
                    $"node={Normalize(node.Key)};kind={Normalize(node.Kind)};subtype={Normalize(node.Subtype)};" +
                    $"localId={Normalize(node.LocalId)};label={Normalize(node.Label)};" +
                    $"authority={Normalize(node.Authority)};lifecycle={Normalize(node.Lifecycle)}");
                foreach (var location in node.Locations)
                {
                    Console.WriteLine(
                        $"location.{Normalize(node.Key)}={Normalize(location.Path)};" +
                        $"kind={Normalize(location.LocatorKind)};value={Normalize(location.LocatorValue)}");
                }
            }

            foreach (var traversal in result.Traversals)
            {
                Console.WriteLine(
                    $"traversal={Normalize(traversal.Edge.Key)};depth={traversal.Depth};" +
                    $"direction={traversal.Direction};type={Normalize(traversal.Edge.Type)};" +
                    $"from={Normalize(traversal.Edge.From)};to={Normalize(traversal.Edge.To)};" +
                    $"state={Normalize(traversal.Edge.State)};confidence={Normalize(traversal.Edge.Confidence)}");
                RenderAgentObservations(traversal.Edge);
            }

            RenderAgentDiagnostics(result.Diagnostics);
            return;
        }

        Console.WriteLine($"Graph query: {result.Status}");
        Console.WriteLine($"Freshness: {result.Freshness}");
        Console.WriteLine($"Nodes: {result.Nodes.Count}");
        Console.WriteLine($"Traversals: {result.Traversals.Count}");
        Console.WriteLine($"Truncated: {result.Truncated}");
        foreach (var node in result.Nodes)
        {
            Console.WriteLine($"{node.Key}");
            Console.WriteLine($"  {node.Kind}/{node.Subtype}: {node.Label}");
            Console.WriteLine($"  Authority: {node.Authority}; lifecycle: {node.Lifecycle}");
            foreach (var location in node.Locations)
            {
                Console.WriteLine($"  Location: {location.Path} ({location.LocatorKind}: {location.LocatorValue})");
            }
        }

        foreach (var traversal in result.Traversals)
        {
            Console.WriteLine(
                $"depth {traversal.Depth} {traversal.Direction} {traversal.Edge.Type}: " +
                $"{traversal.Edge.From} -> {traversal.Edge.To} " +
                $"[{traversal.Edge.State}, {traversal.Edge.Confidence}]");
            foreach (var observation in traversal.Edge.Observations)
            {
                Console.WriteLine(
                    $"  Evidence: {observation.Path} " +
                    $"({observation.LocatorKind}: {observation.LocatorValue})");
            }
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine($"[{diagnostic.Severity.ToUpperInvariant()}] {diagnostic.Code}: {diagnostic.Message}");
            foreach (var evidence in diagnostic.Evidence)
            {
                Console.WriteLine($"  Evidence: {evidence}");
            }
        }
    }

    private static void RenderValidation(GraphValidationResult result, string format)
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
                $"status={result.Status};exitCode={result.ExitCode};freshness={result.Freshness};" +
                $"nodes={result.NodeCount};edges={result.EdgeCount};errors={result.ErrorCount};" +
                $"warnings={result.WarningCount};information={result.InformationCount};" +
                $"strict={result.Strict.ToString().ToLowerInvariant()}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            Console.WriteLine($"graphPath={Normalize(result.GraphPath)}");
            Console.WriteLine($"buildId={Normalize(result.BuildId)}");
            RenderAgentDiagnostics(result.Diagnostics);
            return;
        }

        Console.WriteLine($"Graph validation: {result.Status}");
        Console.WriteLine($"Freshness: {result.Freshness}");
        Console.WriteLine($"Nodes: {result.NodeCount}");
        Console.WriteLine($"Edges: {result.EdgeCount}");
        Console.WriteLine($"Errors: {result.ErrorCount}");
        Console.WriteLine($"Warnings: {result.WarningCount}");
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine($"[{diagnostic.Severity.ToUpperInvariant()}] {diagnostic.Code}: {diagnostic.Message}");
            foreach (var evidence in diagnostic.Evidence)
            {
                Console.WriteLine($"  Evidence: {evidence}");
            }
        }
    }

    private static void RenderTrace(CisGraphTraceResult result, string format)
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
                $"status={result.Status};exitCode={result.ExitCode};freshness={result.Freshness};" +
                $"paths={result.Paths.Count};truncated={result.Truncated.ToString().ToLowerInvariant()}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            Console.WriteLine($"graphPath={Normalize(result.GraphPath)}");
            Console.WriteLine($"buildId={Normalize(result.Build?.Id)}");
            foreach (var pair in result.Query)
            {
                Console.WriteLine($"query.{pair.Key}={Normalize(pair.Value)}");
            }

            for (var pathIndex = 0; pathIndex < result.Paths.Count; pathIndex++)
            {
                var path = result.Paths[pathIndex];
                Console.WriteLine($"path.{pathIndex + 1}.nodes={path.Nodes.Count};edges={path.Traversals.Count}");
                for (var nodeIndex = 0; nodeIndex < path.Nodes.Count; nodeIndex++)
                {
                    Console.WriteLine(
                        $"path.{pathIndex + 1}.node.{nodeIndex + 1}={Normalize(path.Nodes[nodeIndex].Key)}");
                }

                foreach (var traversal in path.Traversals)
                {
                    Console.WriteLine(
                        $"path.{pathIndex + 1}.traversal={Normalize(traversal.Edge.Key)};" +
                        $"depth={traversal.Depth};direction={traversal.Direction};" +
                        $"type={Normalize(traversal.Edge.Type)};state={Normalize(traversal.Edge.State)};" +
                        $"confidence={Normalize(traversal.Edge.Confidence)}");
                    RenderAgentObservations(traversal.Edge);
                }
            }

            RenderAgentDiagnostics(result.Diagnostics);
            return;
        }

        Console.WriteLine($"Graph trace: {result.Status}");
        Console.WriteLine($"Freshness: {result.Freshness}");
        Console.WriteLine($"Paths: {result.Paths.Count}");
        Console.WriteLine($"Truncated: {result.Truncated}");
        for (var index = 0; index < result.Paths.Count; index++)
        {
            var path = result.Paths[index];
            Console.WriteLine($"Path {index + 1} ({path.Traversals.Count} edge(s)):");
            Console.WriteLine($"  {path.Nodes[0].Key}");
            foreach (var traversal in path.Traversals)
            {
                Console.WriteLine(
                    $"  --{traversal.Edge.Type} ({traversal.Direction}, {traversal.Edge.State}, " +
                    $"{traversal.Edge.Confidence})--> " +
                    $"{(traversal.Direction == "out" ? traversal.Edge.To : traversal.Edge.From)}");
                foreach (var observation in traversal.Edge.Observations)
                {
                    Console.WriteLine(
                        $"    Evidence: {observation.Path} " +
                        $"({observation.LocatorKind}: {observation.LocatorValue})");
                }
            }
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine($"[{diagnostic.Severity.ToUpperInvariant()}] {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static void RenderExport(GraphExportResult result, string format)
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
                $"type={result.Type};freshness={result.Freshness};nodes={result.NodeCount};edges={result.EdgeCount}");
            Console.WriteLine($"repository={Normalize(result.RepositoryPath)}");
            Console.WriteLine($"graphPath={Normalize(result.GraphPath)}");
            Console.WriteLine($"outputPath={Normalize(result.OutputPath)}");
            Console.WriteLine($"buildId={Normalize(result.BuildId)}");
            RenderAgentDiagnostics(result.Diagnostics);
            return;
        }

        Console.WriteLine($"Graph export: {result.Status}");
        Console.WriteLine($"Type: {result.Type}");
        Console.WriteLine($"Freshness: {result.Freshness}");
        Console.WriteLine($"Nodes: {result.NodeCount}");
        Console.WriteLine($"Edges: {result.EdgeCount}");
        if (result.OutputPath is not null)
        {
            Console.WriteLine($"Output: {result.OutputPath}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine($"[{diagnostic.Severity.ToUpperInvariant()}] {diagnostic.Code}: {diagnostic.Message}");
            foreach (var evidence in diagnostic.Evidence)
            {
                Console.WriteLine($"  Evidence: {evidence}");
            }
        }
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

    private static void RenderAgentDiagnostics(IEnumerable<CisGraphDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Console.WriteLine(
                $"diagnostic={diagnostic.Code};severity={diagnostic.Severity};" +
                $"message={Normalize(diagnostic.Message)}");
            foreach (var evidence in diagnostic.Evidence.Take(10))
            {
                Console.WriteLine($"evidence.{diagnostic.Code}={Normalize(evidence)}");
            }
            if (diagnostic.Evidence.Count > 10)
            {
                Console.WriteLine($"evidence.{diagnostic.Code}.omitted={diagnostic.Evidence.Count - 10}");
            }
        }
    }

    private static void RenderAgentObservations(CisGraphEdge edge)
    {
        foreach (var observation in edge.Observations)
        {
            Console.WriteLine(
                $"observation.{Normalize(edge.Key)}={Normalize(observation.Path)};" +
                $"kind={Normalize(observation.LocatorKind)};value={Normalize(observation.LocatorValue)};" +
                $"method={Normalize(observation.Method)};extractor={Normalize(observation.Extractor)}");
        }
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Trim();
}
