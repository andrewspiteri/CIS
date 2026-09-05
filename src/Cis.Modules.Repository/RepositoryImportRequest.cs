namespace Cis.Modules.Repository;

public sealed record RepositoryImportRequest(
    string WorkspacePath,
    string DocumentationRoot,
    IReadOnlyList<string> SourcePaths,
    bool DryRun,
    bool Confirmed,
    string Participation,
    string Relationship,
    IReadOnlyList<string>? ComponentScope = null,
    string? EcosystemId = null,
    string? ProductId = null,
    string? EcosystemName = null,
    string? ProductName = null);
