using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>A maintainer's intended stack selects guidance; it is never evidence of installed tools or source.</summary>
internal static class DeclaredEngineeringStack
{
    internal const string Path = ".cis/engineering-stack.json";
    internal static string? Read(string repository, ICollection<string> warnings)
    {
        var path = System.IO.Path.Combine(repository, Path);
        if (!File.Exists(path)) return null;
        try
        {
            if (CisPathSafety.ContainsReparsePoint(repository, path) || new FileInfo(path).Length > 4096)
                throw new InvalidDataException("Stack declaration must be a regular file of at most 4 KiB.");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema)
                || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1
                || !root.TryGetProperty("stack", out var stack) || stack.ValueKind != JsonValueKind.String || stack.GetString() != "csharp")
                throw new InvalidDataException("Expected schemaVersion=1 and stack=csharp; other declared-stack recipes are not qualified yet.");
            return "csharp";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { warnings.Add($"Invalid declared engineering stack: {exception.Message}"); return null; }
    }
    internal static string Render(string stack) => JsonSerializer.Serialize(new { schemaVersion = 1, stack }, new JsonSerializerOptions { WriteIndented = true });
}
