namespace Cis.Modules.Repository;

public sealed record RepositoryClassification(
    string Shape,
    IReadOnlyList<RepositoryComponentClassification> Components,
    IReadOnlyList<string> Warnings);

public sealed record RepositoryComponentClassification(
    string Id,
    string Root,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities,
    string Confidence,
    IReadOnlyList<string> Evidence);
