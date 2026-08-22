using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Modules.Repository;

public sealed record DocumentationCatalogMergeResult(
    string Content,
    bool Changed,
    IReadOnlyList<string> Collisions);

public sealed partial class DocumentationCatalogMerger
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public DocumentationCatalogMergeResult Merge(
        string repositoryId,
        string? existingContent,
        IReadOnlyList<CatalogArtifactEntry> requiredEntries)
    {
        if (existingContent is null)
        {
            return new DocumentationCatalogMergeResult(
                CreateCatalog(repositoryId, requiredEntries),
                Changed: true,
                []);
        }

        CatalogFile? catalog;
        try
        {
            catalog = _deserializer.Deserialize<CatalogFile>(existingContent);
        }
        catch (YamlException exception)
        {
            return Collision(existingContent, $"catalog.yml is invalid YAML: {exception.Message}");
        }

        if (catalog is null)
        {
            return Collision(existingContent, "catalog.yml is empty.");
        }

        var existingEntries = catalog.Documents ?? [];
        var collisions = new List<string>();
        foreach (var required in requiredEntries)
        {
            var byId = existingEntries.FirstOrDefault(entry =>
                string.Equals(entry.Id, required.Id, StringComparison.OrdinalIgnoreCase));
            if (byId is not null && !string.Equals(NormalizePath(byId.Path), required.Path, StringComparison.OrdinalIgnoreCase))
            {
                collisions.Add(
                    $"Catalog id '{required.Id}' already points to '{byId.Path}', not '{required.Path}'.");
            }

            var byPath = existingEntries.FirstOrDefault(entry =>
                string.Equals(NormalizePath(entry.Path), required.Path, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null && !string.Equals(byPath.Id, required.Id, StringComparison.OrdinalIgnoreCase))
            {
                collisions.Add(
                    $"Catalog path '{required.Path}' already uses id '{byPath.Id}', not '{required.Id}'.");
            }
        }

        if (collisions.Count > 0)
        {
            return new DocumentationCatalogMergeResult(existingContent, Changed: false, collisions);
        }

        var missing = requiredEntries
            .Where(required => !existingEntries.Any(existing =>
                string.Equals(existing.Id, required.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length == 0)
        {
            return new DocumentationCatalogMergeResult(existingContent, Changed: false, []);
        }

        var normalized = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var renderedEntries = RenderEntries(missing);
        var emptyDocuments = EmptyDocumentsPattern().Match(normalized);
        if (emptyDocuments.Success)
        {
            var replacement = "documents:\n" + renderedEntries;
            return new DocumentationCatalogMergeResult(
                normalized[..emptyDocuments.Index] + replacement + normalized[(emptyDocuments.Index + emptyDocuments.Length)..],
                Changed: true,
                []);
        }

        var lines = normalized.Split('\n').ToList();
        var documentsIndex = lines.FindIndex(line => DocumentsPattern().IsMatch(line));
        if (documentsIndex < 0)
        {
            var separator = normalized.EndsWith('\n') ? string.Empty : "\n";
            return new DocumentationCatalogMergeResult(
                normalized + separator + "documents:\n" + renderedEntries,
                Changed: true,
                []);
        }

        var insertionIndex = lines.Count;
        for (var index = documentsIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!string.IsNullOrWhiteSpace(line)
                && !char.IsWhiteSpace(line[0])
                && !line.StartsWith('#'))
            {
                insertionIndex = index;
                break;
            }
        }

        lines.Insert(insertionIndex, renderedEntries.TrimEnd('\n'));
        return new DocumentationCatalogMergeResult(
            string.Join('\n', lines),
            Changed: true,
            []);
    }

    private static string CreateCatalog(
        string repositoryId,
        IReadOnlyList<CatalogArtifactEntry> entries)
        => "schema_version: 1\n" +
           $"repository: {repositoryId}\n" +
           "documents:\n" +
           RenderEntries(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal));

    private static string RenderEntries(IEnumerable<CatalogArtifactEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.AppendLine($"  - id: {entry.Id}");
            builder.AppendLine($"    path: {entry.Path}");
            builder.AppendLine($"    type: {entry.Type}");
            builder.AppendLine($"    status: {entry.Status}");
            builder.AppendLine($"    authority: {entry.Authority}");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string NormalizePath(string? path)
        => path?.Replace('\\', '/') ?? string.Empty;

    private static DocumentationCatalogMergeResult Collision(string content, string collision)
        => new(content, Changed: false, [collision]);

    [GeneratedRegex(@"(?m)^documents:\s*\[\s*\]\s*(?:#.*)?$")]
    private static partial Regex EmptyDocumentsPattern();

    [GeneratedRegex(@"^documents:\s*(?:#.*)?$")]
    private static partial Regex DocumentsPattern();

    private sealed class CatalogFile
    {
        public List<CatalogDocument>? Documents { get; set; }
    }

    private sealed class CatalogDocument
    {
        public string? Id { get; set; }

        public string? Path { get; set; }
    }
}
