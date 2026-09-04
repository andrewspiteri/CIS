namespace Cis.Abstractions;

public interface ICisGraphAugmenter
{
    string Name { get; }

    CisGraphAugmentation Augment(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, string> inputHashes);
}

public sealed record CisGraphAugmentation(
    IReadOnlyList<CisGraphNode> Nodes,
    IReadOnlyList<CisGraphEdge> Edges,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics);
