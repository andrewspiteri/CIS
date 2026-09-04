namespace Cis.Modules.Index;

public sealed record FileIndexBuildRequest(
    string RepositoryPath,
    string? Path,
    string? Provider,
    string? Model,
    bool AllowRemote,
    int Limit,
    int MaxInputCharacters,
    bool Refresh);

public sealed record FileIndexCard(
    string Path,
    string SourceHash,
    string Summary,
    string Provider,
    string? Model,
    string PromptVersion,
    bool IsLocal,
    bool InputTruncated,
    bool Sensitive,
    string CardPath,
    string GeneratedAt);

public sealed record FileIndexBuildResult(
    string Status,
    string? RepositoryPath,
    string? OutputPath,
    string? Provider,
    string? Model,
    int Candidates,
    int Indexed,
    int Generated,
    int Reused,
    int Pending,
    int Removed,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Errors,
    bool Applied,
    bool RepositoryConfigurationValid,
    bool RequestValid,
    bool ProviderAvailable)
{
    public int ExitCode => !RepositoryConfigurationValid || !RequestValid
        ? 2
        : !ProviderAvailable
            ? 4
            : Errors.Count > 0
                ? 5
                : 0;
}

public sealed record FileIndexStatusResult(
    string Status,
    string? RepositoryPath,
    string? OutputPath,
    int Candidates,
    int Indexed,
    int Fresh,
    int Stale,
    int Missing,
    int Removed,
    IReadOnlyList<string> Errors,
    bool RepositoryConfigurationValid,
    bool IndexAvailable,
    bool Cached = false)
{
    public int ExitCode => !RepositoryConfigurationValid
        ? 2
        : !IndexAvailable
            ? 4
            : Errors.Count > 0
                ? 5
                : 0;
}

public sealed record FileIndexFindResult(
    string Status,
    string? RepositoryPath,
    string Query,
    IReadOnlyList<FileIndexCard> Cards,
    IReadOnlyList<string> Errors,
    bool RepositoryConfigurationValid,
    bool IndexAvailable,
    bool RequestValid)
{
    public int ExitCode => !RepositoryConfigurationValid || !RequestValid
        ? 2
        : !IndexAvailable
            ? 4
            : Errors.Count > 0
                ? 5
                : 0;
}
