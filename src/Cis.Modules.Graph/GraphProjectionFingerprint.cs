using System.Text;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Docs;

namespace Cis.Modules.Graph;

internal static class GraphProjectionFingerprint
{
    internal static string? Read(string repository, string relativePath, bool current = false)
        => current ? ReadCore(repository, relativePath)
            : CisReadScope.Read(typeof(GraphProjectionFingerprint), relativePath, repository,
                () => new ProjectionHash(ReadCore(repository, relativePath))).Value;

    private static string? ReadCore(string repository, string relativePath)
    {
        if (!relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && !relativePath.EndsWith("catalog.yml", StringComparison.OrdinalIgnoreCase)
            && !relativePath.EndsWith("catalog.yaml", StringComparison.OrdinalIgnoreCase)) return null;
        if (!GraphEvidenceReceipt.TrySafePath(repository, relativePath, out var absolute)) throw new IOException("Projection input is not a regular repository path.");
        if (relativePath.EndsWith("catalog.yml", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith("catalog.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var catalogue = new DocumentationCatalogReader().Read(absolute);
            if (!catalogue.IsSuccess) throw new IOException("Projection catalogue could not be read.");
            var managed = catalogue.Catalog!.Documents.Where(entry => GraphBuilder.IsManagedChangeDossierPath(entry.Path))
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .Select(entry => new { entry.Id, entry.Path, entry.Type, entry.Authority });
            return GraphEvidenceReceipt.Digest(JsonSerializer.Serialize(managed));
        }
        var content = File.ReadAllText(absolute);
        var semantic = VerificationMappingExtractor.SemanticText(content);
        var task = TaskDocumentProjection.Read(content);
        var mappings = VerificationMappingExtractor.Read(content).Mappings;
        if (semantic.Length == 0 && mappings.Count == 0 && task is null) return null;
        var builder = new StringBuilder(semantic);
        if (task is not null) builder.AppendLine("task:" + task);
        foreach (var reference in mappings.Select(mapping => mapping.ResultReference).Where(IsReference)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var receipt = GraphEvidenceReceipt.Read(repository, reference);
            builder.AppendLine("result:" + reference + "=" + receipt.State + ":" + receipt.SemanticHash);
        }
        return GraphEvidenceReceipt.Digest(builder.ToString());
    }

    internal static bool IsReference(string value) => !string.IsNullOrWhiteSpace(value) && value is not ("-" or "none" or "unknown");
    private sealed record ProjectionHash(string? Value);
}
