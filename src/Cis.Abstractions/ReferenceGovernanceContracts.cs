namespace Cis.Abstractions;

public sealed record CisReferenceDiscoveryContext(
    string RepositoryPath,
    string DocumentationPath,
    string RepositoryId);

public sealed record CisReferenceSourceFile(
    string RelativePath,
    string Content);

public sealed record CisReferenceObservation(
    string Kind,
    string Identity,
    string DisplayName,
    string SourcePath,
    int? Line,
    IReadOnlyList<string> Aliases,
    IReadOnlyDictionary<string, string>? Properties = null);

/// <summary>
/// Discovers one governed reference family from repository source. Providers are
/// deterministic and may not mutate canonical documentation.
/// </summary>
public interface ICisReferenceProvider
{
    string Kind { get; }

    string CanonicalFileName { get; }

    string Description { get; }

    bool Supports(string relativePath);

    IReadOnlyList<CisReferenceObservation> Discover(
        CisReferenceDiscoveryContext context,
        CisReferenceSourceFile source);
}
