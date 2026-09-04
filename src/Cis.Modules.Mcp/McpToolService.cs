using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Graph;
using Cis.Modules.References;

namespace Cis.Modules.Mcp;

public sealed class McpToolService(
    ICisRepositoryContextResolver resolver,
    ICisGraphQueryService graphQueries,
    GraphBuilder graphBuilder,
    ChangeDossierStore changes,
    ReferenceGovernanceService references) : ICisMcpToolService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<McpToolDefinition> ListTools(bool allowMutations)
    {
        var tools = new List<McpToolDefinition>
        {
            Tool("cis_repository_status", "Resolve the fixed CIS repository scope and documentation root.", Schema([]), false),
            Tool("cis_graph_find", "Find bounded context-graph nodes by identity, kind, subtype, facet, or text.", Schema([
                Prop("id", "string"), Prop("kind", "string"), Prop("subtype", "string"), Prop("facet", "string"),
                Prop("text", "string"), Prop("limit", "integer", 1, 1000)]), false),
            Tool("cis_graph_related", "Traverse a bounded graph neighbourhood from one exact node.", Schema([
                Prop("id", "string", required: true), Prop("kind", "string"), Prop("edgeTypes", "array", itemType: "string"),
                Prop("direction", "string"), Prop("depth", "integer", 1, 10), Prop("limit", "integer", 1, 1000),
                Prop("includeProposed", "boolean")]), false),
            Tool("cis_graph_trace", "Trace bounded paths between two exact graph nodes.", Schema([
                Prop("from", "string", required: true), Prop("to", "string", required: true),
                Prop("fromKind", "string"), Prop("toKind", "string"), Prop("edgeTypes", "array", itemType: "string"),
                Prop("direction", "string"), Prop("maxDepth", "integer", 1, 10), Prop("maxPaths", "integer", 1, 100),
                Prop("includeProposed", "boolean")]), false),
            Tool("cis_references_inventory", "Read normalized provider-based reference families and source correlations.", Schema([
                Prop("kind", "string")]), false),
            Tool("cis_changes_list", "List canonical CIS change dossiers in the fixed repository.", Schema([]), false),
        };
        if (allowMutations)
        {
            tools.Add(Tool("cis_graph_build", "Rebuild disposable graph state after explicit confirmation.", Schema([
                Prop("confirm", "boolean", required: true)]), true));
            tools.Add(Tool("cis_change_create", "Create a canonical change dossier after explicit confirmation.", Schema([
                Prop("title", "string", required: true), Prop("outcome", "string", required: true),
                Prop("requestedId", "string"), Prop("roots", "array", required: true, itemType: "object"), Prop("confirm", "boolean", required: true)]), true));
        }
        return tools;
    }

    public McpToolOutcome Call(string repositoryPath, bool allowMutations, string name, JsonElement arguments)
    {
        var resolution = resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return Error(new { status = "invalid-repository", errors = resolution.Errors });
        var repository = resolution.Context.RepositoryPath;
        return name switch
        {
            "cis_repository_status" => Ok(new
            {
                status = "resolved",
                repository = resolution.Context.RepositoryPath,
                repositoryId = resolution.Context.RepositoryId,
                documentationRoot = resolution.Context.DocumentationRoot,
                documentationPath = resolution.Context.DocumentationPath,
            }),
            "cis_graph_find" => From(graphQueries.Find(repository, new(
                String(arguments, "id"), String(arguments, "kind"), String(arguments, "subtype"), String(arguments, "facet"),
                String(arguments, "text"), Integer(arguments, "limit", 20, 1, 1000))), result => result.ExitCode),
            "cis_graph_related" => Require(arguments, ["id"], () => From(graphQueries.Related(repository, new(
                String(arguments, "id")!, String(arguments, "kind"), Strings(arguments, "edgeTypes"),
                Direction(arguments), Integer(arguments, "depth", 1, 1, 10), Integer(arguments, "limit", 100, 1, 1000),
                Boolean(arguments, "includeProposed"))), result => result.ExitCode)),
            "cis_graph_trace" => Require(arguments, ["from", "to"], () => From(graphQueries.Trace(repository, new(
                String(arguments, "from")!, String(arguments, "to")!, String(arguments, "fromKind"), String(arguments, "toKind"),
                Strings(arguments, "edgeTypes"), Direction(arguments), Integer(arguments, "maxDepth", 5, 1, 10),
                Integer(arguments, "maxPaths", 10, 1, 100), Boolean(arguments, "includeProposed"))), result => result.ExitCode)),
            "cis_references_inventory" => From(references.Inventory(repository, String(arguments, "kind")), result => result.ExitCode),
            "cis_changes_list" => From(changes.List(repository), result => result.ExitCode),
            "cis_graph_build" => Mutation(allowMutations, arguments,
                () => From(graphBuilder.Build(repository), result => result.ExitCode)),
            "cis_change_create" => Mutation(allowMutations, arguments,
                () => Require(arguments, ["title", "outcome", "roots"], () =>
                {
                    var roots = ReadRoots(arguments, out var rootError);
                    return rootError is null
                        ? From(changes.Create(new(repository, String(arguments, "title")!, String(arguments, "outcome")!, roots,
                            String(arguments, "requestedId"))), result => result.ExitCode)
                        : Error(new { status = "invalid-request", error = rootError });
                })),
            _ => Error(new { status = "unknown-tool", error = $"Unknown or unavailable MCP tool '{name}'." }),
        };
    }

    private static McpToolOutcome Mutation(bool allowed, JsonElement arguments, Func<McpToolOutcome> action)
    {
        if (!allowed) return Error(new { status = "mutation-disabled", error = "Start the server with --allow-mutations to expose mutation tools." });
        if (!Boolean(arguments, "confirm")) return Error(new { status = "confirmation-required", error = "Set confirm=true for this exact mutation." });
        return action();
    }

    private static McpToolOutcome Require(JsonElement arguments, IReadOnlyList<string> required, Func<McpToolOutcome> action)
    {
        var missing = required.Where(name => !arguments.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))).ToArray();
        return missing.Length == 0 ? action() : Error(new { status = "invalid-request", error = "Missing required arguments: " + string.Join(", ", missing) });
    }

    private static IReadOnlyList<ChangeRoot> ReadRoots(JsonElement arguments, out string? error)
    {
        error = null;
        if (!arguments.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Array)
        { error = "roots must be an array of objects with id and optional kind."; return []; }
        var results = new List<ChangeRoot>();
        foreach (var root in roots.EnumerateArray())
        {
            if (root.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(String(root, "id")))
            { error = "Each root requires a non-empty id."; return []; }
            results.Add(new(String(root, "id")!, String(root, "kind")));
        }
        return results;
    }

    private static McpToolOutcome From<T>(T result, Func<T, int> exitCode) => new(result!, exitCode(result) != 0);
    private static McpToolOutcome Ok(object value) => new(value);
    private static McpToolOutcome Error(object value) => new(value, true);
    private static string Direction(JsonElement element)
    {
        var value = String(element, "direction")?.ToLowerInvariant() ?? "both";
        return value is "in" or "out" or "both" ? value : "both";
    }
    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Boolean(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static int Integer(JsonElement element, string name, int fallback, int minimum, int maximum) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() : [];

    private static McpToolDefinition Tool(string name, string description, JsonElement schema, bool mutating) => new(name, description, schema, mutating);
    private static (string Name, string Type, int? Minimum, int? Maximum, bool Required, string? ItemType) Prop(string name, string type, int? minimum = null, int? maximum = null, bool required = false, string? itemType = null) =>
        (name, type, minimum, maximum, required, itemType);
    private static JsonElement Schema(IReadOnlyList<(string Name, string Type, int? Minimum, int? Maximum, bool Required, string? ItemType)> properties)
    {
        var definitions = properties.ToDictionary(item => item.Name, item =>
        {
            var schema = new Dictionary<string, object?> { ["type"] = item.Type };
            if (item.Type == "array") schema["items"] = new { type = item.ItemType ?? "string" };
            if (item.Minimum is not null) schema["minimum"] = item.Minimum;
            if (item.Maximum is not null) schema["maximum"] = item.Maximum;
            return (object)schema;
        }, StringComparer.Ordinal);
        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = definitions,
            required = properties.Where(item => item.Required).Select(item => item.Name).ToArray(),
            additionalProperties = false,
        }, JsonOptions);
    }
}
