using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Cis.Modules.Docs;

public sealed class MarkdownDocumentInspector
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public MarkdownDocumentMetadata Inspect(string path, string documentationRelativePath)
    {
        var content = File.ReadAllText(path);
        var body = content;
        var hasFrontMatter = false;
        var malformedFrontMatter = false;
        var title = string.Empty;
        var type = string.Empty;
        var status = string.Empty;
        var stableId = string.Empty;

        if (TryExtractFrontMatter(content, out var yaml, out body, out var terminated))
        {
            hasFrontMatter = true;
            malformedFrontMatter = !terminated;
            try
            {
                if (terminated)
                {
                    var metadata = _deserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? [];
                    title = GetScalar(metadata, "title");
                    type = GetScalar(metadata, "type");
                    status = GetScalar(metadata, "status");
                    stableId = GetNestedScalar(metadata, "cis", "stable_id");
                }
            }
            catch (YamlException)
            {
                malformedFrontMatter = true;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = FindFirstHeading(body) ?? CreateTitleFromPath(documentationRelativePath);
        }

        return new MarkdownDocumentMetadata(
            title,
            type,
            status,
            stableId,
            hasFrontMatter,
            malformedFrontMatter);
    }

    private static bool TryExtractFrontMatter(
        string content,
        out string yaml,
        out string body,
        out bool terminated)
    {
        yaml = string.Empty;
        body = content;
        terminated = false;
        using var reader = new StringReader(content);
        if (!string.Equals(reader.ReadLine()?.TrimStart('\uFEFF'), "---", StringComparison.Ordinal))
        {
            return false;
        }

        var yamlLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                yaml = string.Join(Environment.NewLine, yamlLines);
                body = reader.ReadToEnd();
                terminated = true;
                return true;
            }

            yamlLines.Add(line);
        }

        yaml = string.Join(Environment.NewLine, yamlLines);
        body = string.Empty;
        return true;
    }

    private static string GetScalar(IReadOnlyDictionary<object, object?> metadata, string key)
    {
        var pair = metadata.FirstOrDefault(item =>
            string.Equals(item.Key.ToString(), key, StringComparison.OrdinalIgnoreCase));
        return pair.Value?.ToString()?.Trim() ?? string.Empty;
    }

    private static string GetNestedScalar(
        IReadOnlyDictionary<object, object?> metadata,
        string parentKey,
        string childKey)
    {
        var pair = metadata.FirstOrDefault(item =>
            string.Equals(item.Key.ToString(), parentKey, StringComparison.OrdinalIgnoreCase));
        if (pair.Value is not IReadOnlyDictionary<object, object?> nested)
        {
            return string.Empty;
        }

        return GetScalar(nested, childKey);
    }

    private static string? FindFirstHeading(string body)
    {
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return null;
    }

    private static string CreateTitleFromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, "README", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileName(Path.GetDirectoryName(path)) ?? "Documentation";
        }

        return name.Replace('-', ' ').Replace('_', ' ');
    }
}

public sealed record MarkdownDocumentMetadata(
    string Title,
    string Type,
    string Status,
    string StableId,
    bool HasFrontMatter,
    bool MalformedFrontMatter);
