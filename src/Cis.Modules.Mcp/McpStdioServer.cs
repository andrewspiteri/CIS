using System.Text;
using System.Text.Json;

namespace Cis.Modules.Mcp;

public sealed class McpStdioServer(ICisMcpToolService tools)
{
    public const string ProtocolVersion = "2025-11-25";
    public const int MaximumMessageCharacters = 1024 * 1024;
    private static readonly string[] SupportedProtocolVersions = [ProtocolVersion, "2024-11-05"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int Run(string repositoryPath, bool allowMutations, TextReader input, TextWriter output)
    {
        while (ReadMessage(input) is { EndOfStream: false } message)
        {
            if (message.TooLong) { Write(output, Error(null, -32600, $"Request exceeds the {MaximumMessageCharacters} character limit.")); continue; }
            var line = message.Value!;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("jsonrpc", out var version)
                    || version.ValueKind != JsonValueKind.String
                    || version.GetString() != "2.0")
                { Write(output, Error(null, -32600, "Invalid Request")); continue; }
                var id = root.TryGetProperty("id", out var requestId) ? requestId.Clone() : (JsonElement?)null;
                if (id is { ValueKind: not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null) })
                { Write(output, Error(null, -32600, "Invalid Request")); continue; }
                var method = root.TryGetProperty("method", out var methodValue) ? methodValue.GetString() : null;
                if (string.IsNullOrWhiteSpace(method))
                {
                    Write(output, Error(id, -32600, "Invalid Request"));
                    continue;
                }
                if (method.StartsWith("notifications/", StringComparison.Ordinal)) continue;
                var response = Handle(repositoryPath, allowMutations, id, method, root);
                if (response is not null) Write(output, response);
            }
            catch (JsonException)
            {
                Write(output, Error(null, -32700, "Parse error"));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                Write(output, Error(null, -32603, "Internal error"));
            }
            finally
            {
                document?.Dispose();
            }
        }
        return 0;
    }

    private object? Handle(string repositoryPath, bool allowMutations, JsonElement? id, string method, JsonElement root)
    {
        switch (method)
        {
            case "server/discover":
                return Error(id, -32022, "Unsupported protocol version", new { supported = SupportedProtocolVersions });
            case "initialize":
            {
                var requestedVersion = root.TryGetProperty("params", out var initializeParameters)
                    && initializeParameters.ValueKind == JsonValueKind.Object
                    && initializeParameters.TryGetProperty("protocolVersion", out var requested)
                    && requested.ValueKind == JsonValueKind.String ? requested.GetString() : null;
                if (requestedVersion is null || !SupportedProtocolVersions.Contains(requestedVersion, StringComparer.Ordinal))
                    return Error(id, -32022, "Unsupported protocol version", new { supported = SupportedProtocolVersions });
                return Success(id, new
                {
                    protocolVersion = requestedVersion,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "cis", title = "Change Impact Studio", version = "0.3.0" },
                    instructions = "Repository-scoped CIS tools. Mutations are hidden unless enabled and always require confirm=true.",
                });
            }
            case "ping":
                return Success(id, new { });
            case "tools/list":
                return Success(id, new
                {
                    tools = tools.ListTools(allowMutations).Select(tool => new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        inputSchema = tool.InputSchema,
                        annotations = new
                        {
                            readOnlyHint = !tool.Mutating,
                            destructiveHint = tool.Mutating,
                            idempotentHint = tool.Name == "cis_graph_build",
                            openWorldHint = false,
                        },
                    }).ToArray(),
                });
            case "tools/call":
                return CallTool(repositoryPath, allowMutations, id, root);
            default:
                return Error(id, -32601, "Method not found");
        }
    }

    private object CallTool(string repositoryPath, bool allowMutations, JsonElement? id, JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String)
            return Error(id, -32602, "Invalid params");
        var name = nameValue.GetString()!;
        var arguments = parameters.TryGetProperty("arguments", out var argumentValue) && argumentValue.ValueKind == JsonValueKind.Object
            ? argumentValue : JsonSerializer.SerializeToElement(new { }, JsonOptions);
        if (!tools.ListTools(allowMutations).Any(tool => tool.Name == name))
            return Error(id, -32602, $"Unknown or unavailable tool '{name}'.");
        var outcome = tools.Call(repositoryPath, allowMutations, name, arguments);
        var structured = JsonSerializer.SerializeToElement(outcome.StructuredContent, JsonOptions);
        var text = JsonSerializer.Serialize(outcome.StructuredContent, JsonOptions);
        return Success(id, new
        {
            content = new[] { new { type = "text", text } },
            structuredContent = structured,
            isError = outcome.IsError,
        });
    }

    private static object Success(JsonElement? id, object result) => new { jsonrpc = "2.0", id, result };
    private static object Error(JsonElement? id, int code, string message, object? data = null) => new
    {
        jsonrpc = "2.0",
        id,
        error = new { code, message, data },
    };
    private static void Write(TextWriter output, object response)
    {
        output.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        output.Flush();
    }

    private static StdioMessage ReadMessage(TextReader input)
    {
        var builder = new StringBuilder(Math.Min(4096, MaximumMessageCharacters));
        var tooLong = false; var readAny = false;
        while (true)
        {
            var value = input.Read();
            if (value < 0) return readAny ? new(builder.ToString(), tooLong, false) : new(null, false, true);
            readAny = true;
            if (value == '\n') return new(builder.ToString(), tooLong, false);
            if (value == '\r')
            {
                if (input.Peek() == '\n') input.Read();
                return new(builder.ToString(), tooLong, false);
            }
            if (builder.Length < MaximumMessageCharacters) builder.Append((char)value);
            else tooLong = true;
        }
    }

    private sealed record StdioMessage(string? Value, bool TooLong, bool EndOfStream);
}
