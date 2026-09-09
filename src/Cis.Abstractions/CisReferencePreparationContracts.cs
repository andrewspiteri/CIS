namespace Cis.Abstractions;

public interface ICisObservedReferencePreparer
{
    IReadOnlyList<CisReferencePreparationResult> PrepareWorkspaceObservedReferences(string workspacePath, bool apply = true);
}

public sealed record CisReferencePreparationItem(string Kind, string Path, int DiscoveredRows, int AddedRows,
    string Action);

public sealed record CisReferencePreparationResult(string RepositoryPath,
    IReadOnlyList<CisReferencePreparationItem> Inventories, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors, bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 5 : 0;
}
