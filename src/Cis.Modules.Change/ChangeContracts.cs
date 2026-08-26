namespace Cis.Modules.Change;

public sealed record ChangeRoot(string Id, string? Kind);

public sealed record ChangeCreateRequest(
    string RepositoryPath,
    string Title,
    string Outcome,
    IReadOnlyList<ChangeRoot> Roots,
    string? RequestedId = null);

public sealed record ChangeRebaselineRequest(
    string RepositoryPath,
    string ChangeId,
    string Actor,
    string Reason);

public sealed record ChangeRepositoryBaseline(
    string RepositoryId,
    string BaselineKind,
    string Baseline,
    IReadOnlyList<ChangeRepositoryWorkingFile>? WorkingTree = null);

public sealed record ChangeRepositoryWorkingFile(
    string Status,
    string Path,
    string Digest);

public sealed record ChangeDossier(
    string Id,
    string Title,
    string Outcome,
    string Status,
    string BaselineKind,
    string Baseline,
    string GraphBuildId,
    string RepositoryPath,
    string DocumentationRoot,
    string RelativePath,
    IReadOnlyList<ChangeRoot> Roots,
    IReadOnlyList<ChangeRepositoryBaseline>? RepositoryBaselines = null);

public sealed record ChangeResult(
    string Status,
    ChangeDossier? Change,
    IReadOnlyList<ChangeDossier> Changes,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 2 : 0;
}
