using System.Text.Json;

namespace Cis.Modules.Mcp;

public sealed record McpToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema,
    bool Mutating);

public sealed record McpToolOutcome(
    object StructuredContent,
    bool IsError = false);

public interface ICisMcpToolService
{
    IReadOnlyList<McpToolDefinition> ListTools(bool allowMutations);

    McpToolOutcome Call(string repositoryPath, bool allowMutations, string name, JsonElement arguments);
}
