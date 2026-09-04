namespace Cis.Abstractions;

public sealed record CisFrontendDiscoveryContext(
    string RepositoryPath,
    string RepositoryId,
    string DocumentationPath);

public sealed record CisFrontendSourceFile(
    string Path,
    string Content);

public sealed record CisFrontendObservation(
    string Provider,
    string Framework,
    string Kind,
    string Id,
    string Name,
    string SourcePath,
    int Line,
    string? Route,
    string? Target,
    IReadOnlyDictionary<string, string> Properties);

public interface ICisFrontendContextProvider
{
    string Name { get; }

    bool CanInspect(CisFrontendSourceFile source);

    IReadOnlyList<CisFrontendObservation> Discover(
        CisFrontendDiscoveryContext context,
        CisFrontendSourceFile source);
}
