namespace Cis.Modules.Repository;

internal sealed partial class RepositoryStarterBinder
{
    // Runtime configuration is independent of how often a topic is mentioned in prose.
    // Do not install the full policy library or choose replacement security scanners.
    internal RepositoryStarterBinding BindMinimalImport(string repositoryPath, string repositoryId,
        string documentationRoot, RepositoryClassification classification)
    {
        var selections = new List<RepositoryStarterSelection>();
        var artifacts = new List<RepositoryStarterArtifact>();
        AddExecutionGovernanceProfiles(repositoryPath, repositoryId, documentationRoot, classification, selections, artifacts, runtimeOnly: true);
        var core = new HashSet<string>(StringComparer.Ordinal)
        {
            "reference.ai-routing-profile", "reference.agent-provider-profile", "reference.source-evidence",
            "reference.ai-model-registry", "reference.ai-evaluation-index-card", "reference.local-artifact-retention",
        };
        var selected = artifacts.Where(artifact => core.Contains(artifact.Id)).ToList();
        var tests = RepositoryTestingStarter.CreateImportProfile(repositoryPath, classification);
        if (tests is not null)
        {
            var path = $"{documentationRoot}/references/test-suite-profile.md";
            selected.Add(new("reference.test-suite-profile", "reference.test-suite-profile", path, tests,
                new CatalogArtifactEntry($"{repositoryId}:reference:test-suite-profile", path, "test-suite-profile", "active", "canonical")));
        }
        return new(selected.Select(artifact => new RepositoryStarterSelection(artifact.Definition,
            artifact.Id == "reference.test-suite-profile"
                ? "Connect CIS to detected test projects and declared scripts; execution and report paths still require verification."
                : "Provide scoped CIS runtime configuration without replacing existing project directives.",
            artifact.Id == "reference.test-suite-profile"
                ? classification.Components.SelectMany(component => component.Evidence).Distinct().ToArray()
                : [artifact.RelativePath])).ToArray(), selected);
    }
}
