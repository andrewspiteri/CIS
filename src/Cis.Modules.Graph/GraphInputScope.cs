using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Graph;

// Prefixes filter implementation extraction only; catalog documents remain authoritative.
internal sealed record GraphInputScope(string[] Include, string[] Exclude)
{
    internal const string RelativePath = ".cis/graph-inputs.json";

    internal static GraphInputScope Read(string repository)
    {
        var path = Path.Combine(repository, RelativePath);
        if (!File.Exists(path)) return new([], []);
        if (CisPathSafety.ContainsReparsePoint(repository, path))
            throw new IOException("Graph input scope must be a regular repository file.");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                throw new IOException("Unsupported graph input scope schema; expected 1.");
            return new(ReadPrefixes("include"), ReadPrefixes("exclude"));

            string[] ReadPrefixes(string name)
            {
                if (!json.RootElement.TryGetProperty(name, out var value)) return [];
                return value.EnumerateArray().Select(item =>
                {
                    var prefix = item.GetString()?.Replace('\\', '/').TrimEnd('/') ?? "";
                    if (prefix.Length == 0 || prefix.StartsWith('/') || prefix.Contains(':')
                        || prefix.Split('/').Any(part => part is "" or "." or "..")
                        || prefix.IndexOfAny(['*', '?']) >= 0)
                        throw new IOException("Graph input scope requires relative file/directory prefixes without wildcards or traversal.");
                    return prefix;
                }).ToArray();
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new IOException("Invalid .cis/graph-inputs.json: " + error.Message, error);
        }
    }

    internal bool Includes(string path) => (Include.Length == 0 || Include.Any(prefix => Matches(path, prefix)))
        && !Exclude.Any(prefix => Matches(path, prefix));

    internal bool ExcludesDirectory(string path) => Exclude.Any(prefix => Matches(path, prefix));

    private static bool Matches(string path, string prefix) => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
