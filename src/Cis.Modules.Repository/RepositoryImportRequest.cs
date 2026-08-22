namespace Cis.Modules.Repository;

public sealed record RepositoryImportRequest(
    string WorkspacePath,
    string DocumentationRoot,
    IReadOnlyList<string> SourcePaths,
    bool DryRun,
    bool Confirmed);
