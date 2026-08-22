using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed class GraphExporter
{
    private const string GraphRelativePath = SqliteGraphStore.DatabaseRelativePath;

    private static readonly IReadOnlyDictionary<string, string> Extensions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["markdown"] = "md",
            ["json"] = "json",
            ["jsonl"] = "jsonl",
            ["dot"] = "dot",
        };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly GraphValidator _validator;
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly SqliteGraphStore _store;

    public GraphExporter(
        ICisRepositoryContextResolver repositoryContextResolver,
        GraphValidator validator,
        SqliteGraphStore? store = null)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _validator = validator;
        _store = store ?? new SqliteGraphStore();
    }

    public GraphExportResult Export(GraphExportRequest request)
    {
        var type = request.Type.ToLowerInvariant();
        if (!Extensions.TryGetValue(type, out var extension))
        {
            return InvalidRequest(
                request,
                type,
                $"Unsupported export type '{request.Type}'. Expected markdown, json, jsonl, or dot.");
        }

        var resolution = _repositoryContextResolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess)
        {
            return new GraphExportResult(
                "invalid-repository",
                null,
                null,
                null,
                type,
                null,
                "unknown",
                0,
                0,
                resolution.Errors.Select(error => Diagnostic(
                    "CIS-GRAPH-EXPORT-REPO-001",
                    "error",
                    error)).ToArray(),
                Applied: false,
                RepositoryConfigurationValid: false,
                GraphAvailable: false,
                RequestValid: true,
                Collision: false);
        }

        var context = resolution.Context!;
        var defaultOutput = $".cis/local/graph/export.{extension}";
        var relativeOutput = string.IsNullOrWhiteSpace(request.OutputPath)
            ? defaultOutput
            : request.OutputPath.Replace('\\', '/');
        if (!TryResolveOutput(context.RepositoryPath, relativeOutput, out var outputPath))
        {
            return InvalidRequest(
                request,
                type,
                "Export output must be a repository-relative file path inside the repository.",
                relativeOutput,
                context.RepositoryPath);
        }

        if (IsReservedOutput(relativeOutput))
        {
            return InvalidRequest(
                request,
                type,
                "Export output cannot replace repository inputs or graph generation files.",
                relativeOutput,
                context.RepositoryPath);
        }

        var validation = _validator.Validate(context.RepositoryPath, strict: false);
        if (!validation.GraphAvailable)
        {
            return FromValidation(validation, type, relativeOutput, Applied: false, Collision: false);
        }

        if (validation.ErrorCount > 0)
        {
            return FromValidation(
                validation with { Status = "invalid" },
                type,
                relativeOutput,
                Applied: false,
                Collision: false);
        }

        if (IsManifestInput(context, relativeOutput))
        {
            return InvalidRequest(
                request,
                type,
                "Export output cannot replace a canonical or implementation graph input.",
                relativeOutput,
                context.RepositoryPath);
        }

        var stored = _store.Read(context.RepositoryPath);
        if (!stored.Success || stored.Graph is null)
        {
            return Unavailable(
                context.RepositoryPath,
                relativeOutput,
                type,
                $"SQLite graph is empty or incompatible: {stored.Error}");
        }
        var graph = stored.Graph;

        var content = type switch
        {
            "json" => JsonSerializer.Serialize(graph, WriteOptions) + "\n",
            "jsonl" => CreateJsonLines(graph),
            "dot" => CreateDot(graph),
            _ => CreateMarkdown(graph, validation.Freshness),
        };
        if (File.Exists(outputPath))
        {
            try
            {
                if (string.Equals(File.ReadAllText(outputPath), content, StringComparison.Ordinal))
                {
                    return Success(
                        validation,
                        type,
                        relativeOutput,
                        graph,
                        "unchanged",
                        Applied: false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return InvalidRequest(
                    request,
                    type,
                    $"Unable to inspect existing export output: {exception.Message}",
                    relativeOutput,
                    context.RepositoryPath);
            }

            var isDefaultOutput = string.Equals(relativeOutput, defaultOutput, StringComparison.OrdinalIgnoreCase);
            if (!isDefaultOutput && !request.Force)
            {
                return new GraphExportResult(
                    "collision",
                    context.RepositoryPath,
                    GraphRelativePath,
                    relativeOutput,
                    type,
                    graph.Build.Id,
                    validation.Freshness,
                    graph.Nodes.Count,
                    graph.Edges.Count,
                    [Diagnostic(
                        "CIS-GRAPH-EXPORT-COLLISION-001",
                        "error",
                        "Export output already exists with different content. Use --force to replace it.",
                        relativeOutput)],
                    Applied: false,
                    RepositoryConfigurationValid: true,
                    GraphAvailable: true,
                    RequestValid: true,
                    Collision: true);
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            WriteAtomic(outputPath, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return InvalidRequest(
                request,
                type,
                $"Unable to write export output: {exception.Message}",
                relativeOutput,
                context.RepositoryPath);
        }

        return Success(validation, type, relativeOutput, graph, "exported", Applied: true);
    }

    private static bool IsReservedOutput(string relativeOutput)
    {
        var normalized = relativeOutput.Replace('\\', '/');
        return normalized is ".cis/repository.yml"
            or ".cis/local/graph/graph.json"
            or ".cis/local/graph/context.db"
            or ".cis/local/graph/manifest.json"
            or ".cis/local/graph/diagnostics.json"
            || normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsManifestInput(CisRepositoryContext context, string relativeOutput)
    {
        var normalized = relativeOutput.Replace('\\', '/');
        var header = _store.ReadHeader(context.RepositoryPath);
        return !header.Success || header.Manifest is null || header.Manifest.Inputs.Any(input => string.Equals(
            input.Path,
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateMarkdown(CisGraphDocument graph, string freshness)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CIS context graph export");
        builder.AppendLine();
        builder.AppendLine($"- Build: `{EscapeMarkdown(graph.Build.Id)}`");
        builder.AppendLine($"- Repository: `{EscapeMarkdown(graph.Build.RepositoryId)}`");
        builder.AppendLine($"- Graph status: `{EscapeMarkdown(graph.Build.Status)}`");
        builder.AppendLine($"- Freshness at export: `{EscapeMarkdown(freshness)}`");
        builder.AppendLine($"- Nodes: {graph.Nodes.Count}");
        builder.AppendLine($"- Edges: {graph.Edges.Count}");
        builder.AppendLine();
        builder.AppendLine("## Nodes");
        builder.AppendLine();
        builder.AppendLine("| Key | Kind | Subtype | Label | Authority | Lifecycle | Locations |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var node in graph.Nodes.OrderBy(node => node.Key, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"| `{EscapeTable(node.Key)}` | {EscapeTable(node.Kind)} | {EscapeTable(node.Subtype)} | " +
                $"{EscapeTable(node.Label)} | {EscapeTable(node.Authority)} | {EscapeTable(node.Lifecycle)} | " +
                $"{EscapeTable(string.Join("; ", node.Locations.Select(location => location.Path)))} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Edges");
        builder.AppendLine();
        builder.AppendLine("| Type | From | To | State | Confidence | Evidence |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var edge in graph.Edges.OrderBy(edge => edge.Key, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"| {EscapeTable(edge.Type)} | `{EscapeTable(edge.From)}` | `{EscapeTable(edge.To)}` | " +
                $"{EscapeTable(edge.State)} | {EscapeTable(edge.Confidence)} | " +
                $"{EscapeTable(string.Join("; ", edge.Observations.Select(observation => observation.Path).Distinct()))} |");
        }

        return builder.ToString();
    }

    private static string CreateJsonLines(CisGraphDocument graph)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var lines = new List<string>
        {
            JsonSerializer.Serialize(new
            {
                RecordType = "metadata",
                graph.SchemaVersion,
                graph.Build,
                graph.DiagnosticSummary,
            }, options),
        };
        lines.AddRange(graph.Nodes
            .OrderBy(node => node.Key, StringComparer.Ordinal)
            .Select(node => JsonSerializer.Serialize(new { RecordType = "node", Node = node }, options)));
        lines.AddRange(graph.Edges
            .OrderBy(edge => edge.Key, StringComparer.Ordinal)
            .Select(edge => JsonSerializer.Serialize(new { RecordType = "edge", Edge = edge }, options)));
        return string.Join('\n', lines) + "\n";
    }

    private static string CreateDot(CisGraphDocument graph)
    {
        var nodes = graph.Nodes.OrderBy(node => node.Key, StringComparer.Ordinal).ToArray();
        var identifiers = nodes
            .Select((node, index) => (node.Key, Id: $"n{index}"))
            .ToDictionary(item => item.Key, item => item.Id, StringComparer.Ordinal);
        var builder = new StringBuilder();
        builder.AppendLine("digraph cis_context_graph {");
        builder.AppendLine("  rankdir=LR;");
        builder.AppendLine($"  // build: {EscapeDot(graph.Build.Id)}");
        foreach (var node in nodes)
        {
            builder.AppendLine(
                $"  {identifiers[node.Key]} [label=\"{EscapeDot(node.Label)}\\n{EscapeDot(node.Kind)}/{EscapeDot(node.Subtype)}\"];");
        }

        foreach (var edge in graph.Edges.OrderBy(edge => edge.Key, StringComparer.Ordinal))
        {
            if (identifiers.TryGetValue(edge.From, out var from)
                && identifiers.TryGetValue(edge.To, out var to))
            {
                builder.AppendLine(
                    $"  {from} -> {to} [label=\"{EscapeDot(edge.Type)}\\n{EscapeDot(edge.State)}/{EscapeDot(edge.Confidence)}\"];");
            }
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static GraphExportResult Success(
        GraphValidationResult validation,
        string type,
        string relativeOutput,
        CisGraphDocument graph,
        string status,
        bool Applied)
        => new(
            status,
            validation.RepositoryPath,
            GraphRelativePath,
            relativeOutput,
            type,
            graph.Build.Id,
            validation.Freshness,
            graph.Nodes.Count,
            graph.Edges.Count,
            validation.Diagnostics,
            Applied,
            RepositoryConfigurationValid: true,
            GraphAvailable: true,
            RequestValid: true,
            Collision: false);

    private static GraphExportResult FromValidation(
        GraphValidationResult validation,
        string type,
        string relativeOutput,
        bool Applied,
        bool Collision)
        => new(
            validation.GraphAvailable ? "invalid-graph" : validation.Status,
            validation.RepositoryPath,
            validation.GraphPath,
            relativeOutput,
            type,
            validation.BuildId,
            validation.Freshness,
            validation.NodeCount,
            validation.EdgeCount,
            validation.Diagnostics,
            Applied,
            validation.RepositoryConfigurationValid,
            validation.GraphAvailable,
            RequestValid: true,
            Collision);

    private static GraphExportResult InvalidRequest(
        GraphExportRequest request,
        string type,
        string message,
        string? outputPath = null,
        string? repositoryPath = null)
        => new(
            "invalid-request",
            repositoryPath ?? request.RepositoryPath,
            null,
            outputPath ?? request.OutputPath,
            type,
            null,
            "unknown",
            0,
            0,
            [Diagnostic("CIS-GRAPH-EXPORT-INPUT-001", "error", message, outputPath ?? string.Empty)],
            Applied: false,
            RepositoryConfigurationValid: true,
            GraphAvailable: false,
            RequestValid: false,
            Collision: false);

    private static GraphExportResult Unavailable(
        string repositoryPath,
        string outputPath,
        string type,
        string message)
        => new(
            "graph-unavailable",
            repositoryPath,
            GraphRelativePath,
            outputPath,
            type,
            null,
            "unknown",
            0,
            0,
            [Diagnostic("CIS-GRAPH-EXPORT-LOAD-001", "error", message, GraphRelativePath)],
            Applied: false,
            RepositoryConfigurationValid: true,
            GraphAvailable: false,
            RequestValid: true,
            Collision: false);

    private static bool TryResolveOutput(
        string repositoryPath,
        string relativePath,
        out string outputPath)
    {
        outputPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            outputPath = Path.GetFullPath(Path.Combine(
                repository,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return outputPath.StartsWith(
                repository + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && !Directory.Exists(outputPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string EscapeTable(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeMarkdown(string value)
        => value.Replace("`", "\\`", StringComparison.Ordinal);

    private static string EscapeDot(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static CisGraphDiagnostic Diagnostic(
        string code,
        string severity,
        string message,
        params string[] evidence)
        => new(code, severity, message, evidence.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
}
