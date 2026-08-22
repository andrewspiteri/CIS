using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Modules.Docs;

public sealed class DocumentationCatalogReader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public DocumentationCatalogReadResult Read(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            return Failure($"Documentation catalog was not found: {catalogPath}");
        }

        CatalogFile? source;
        try
        {
            source = _deserializer.Deserialize<CatalogFile>(File.ReadAllText(catalogPath));
        }
        catch (Exception exception) when (exception is YamlException or IOException or UnauthorizedAccessException)
        {
            return Failure($"Documentation catalog is invalid YAML: {exception.Message}");
        }

        if (source is null)
        {
            return Failure("Documentation catalog is empty.");
        }

        var documents = (source.Documents ?? [])
            .Select(document => new DocumentationCatalogEntry(
                document.Id?.Trim() ?? string.Empty,
                NormalizePath(document.Path),
                document.Type?.Trim() ?? string.Empty,
                document.Status?.Trim() ?? string.Empty,
                document.Authority?.Trim() ?? string.Empty))
            .ToArray();

        return new DocumentationCatalogReadResult(
            new DocumentationCatalog(source.SchemaVersion, documents),
            []);
    }

    private static string NormalizePath(string? path)
        => path?.Trim().Replace('\\', '/') ?? string.Empty;

    private static DocumentationCatalogReadResult Failure(string error)
        => new(null, [error]);

    private sealed class CatalogFile
    {
        public int SchemaVersion { get; init; }

        public List<CatalogDocument>? Documents { get; init; }
    }

    private sealed class CatalogDocument
    {
        public string? Id { get; init; }

        public string? Path { get; init; }

        public string? Type { get; init; }

        public string? Status { get; init; }

        public string? Authority { get; init; }
    }
}
