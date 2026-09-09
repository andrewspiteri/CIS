using Cis.Abstractions;
using Cis.Modules.Graph;
using Cis.Modules.Repository;

namespace Cis.Modules.Definition;

/// <summary>Refresh dictionaries and their graphs before authoring freezes implementation evidence.</summary>
public sealed class ObservedReferenceWorkspacePreparer(RepositoryInitializer initializer, GraphBuilder graph)
    : ICisObservedReferencePreparer
{
    public IReadOnlyList<CisReferencePreparationResult> PrepareWorkspaceObservedReferences(string workspacePath, bool apply = true)
    {
        var results = initializer.PrepareWorkspaceObservedReferences(workspacePath, apply).ToArray();
        if (!apply || results.Any(item => item.ExitCode != 0)) return results;
        for (var index = 0; index < results.Length; index++)
        {
            var built = graph.Build(results[index].RepositoryPath);
            if (built.ExitCode == 0) continue;
            results[index] = results[index] with { Errors = built.Diagnostics.Where(item => item.Severity == "error")
                .Select(item => item.Message).DefaultIfEmpty("Dictionary graph refresh failed.").ToArray() };
            break;
        }
        return results;
    }
}
