using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class TestingService
{
    private IReadOnlyList<TestSuiteProfile> ReadTraceProfiles(CisRepositoryContext context,
        IReadOnlyList<TestRunManifest> manifests, ICollection<string> diagnostics)
    {
        var resolution = _workspaceRegistry?.Resolve(context.RepositoryPath);
        var workspace = resolution is { IsSuccess: true } ? resolution.Workspace : null;
        if (workspace?.AuthorityRepository?.Id != context.RepositoryId || manifests.Count == 0)
            return ReadProfile(context, diagnostics);

        // A documentation authority owns the catalogue, not its participants' native suites.
        // Validate profiles where execution was recorded, including the authority if it ran tests.
        var profiles = new List<TestSuiteProfile>();
        foreach (var repositoryId in manifests.Select(manifest => manifest.RepositoryId).Distinct(StringComparer.Ordinal))
        {
            var repository = workspace.Repositories.SingleOrDefault(item => item.Id == repositoryId);
            if (repository is null)
            {
                diagnostics.Add($"ERROR: Test manifest names an unregistered repository: {repositoryId}.");
                continue;
            }
            var participant = Resolve(repository.RepositoryPath, out var errors);
            foreach (var error in errors) diagnostics.Add(error);
            if (participant is not null) profiles.AddRange(ReadProfile(participant, diagnostics));
        }
        return profiles;
    }
}
