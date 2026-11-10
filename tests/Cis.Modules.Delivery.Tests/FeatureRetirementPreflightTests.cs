using Cis.Modules.Change;
using Cis.Modules.Impact;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void PlanReimport_UnsafeObsoleteTaskRouteHoldsBeforeAnyDossierOrCatalogWrite()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Revise operator experience", "Operators receive the approved delivery foundation.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string featurePath = "docs/cis/specs/retirement-preflight-feature.md";
        repository.Write(featurePath,
            "---\ntitle: Operator experience\ntype: feature-specification\n---\n\n" +
            "## Functional requirements\n\n| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n" +
            "|---|---|---|---|---|\n" +
            "| OPS-001 | full-stack | backoffice | Add an operator dashboard backed by an API. | Operators can review current orders. |\n");
        var initial = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, featurePath));
        Assert.Equal(0, initial.ExitCode);
        var obsolete = Assert.Single(initial.WorkItems, item => item.Category == "design");
        var planPath = services.Changes.DossierFile(change, "plan.md");
        var dossier = Path.GetDirectoryName(planPath)!;
        var originalTaskPath = Path.Combine(dossier, obsolete.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        // Retain the document within the dossier, but outside the supported task route.
        // The source revision removes this task, so it must be checked before any writes.
        const string unsafeRoute = "review/retained-design.md";
        Directory.CreateDirectory(Path.Combine(dossier, "review"));
        File.Copy(originalTaskPath, Path.Combine(dossier, unsafeRoute.Replace('/', Path.DirectorySeparatorChar)));
        File.WriteAllText(planPath, File.ReadAllText(planPath).Replace(obsolete.TaskPath!, unsafeRoute, StringComparison.Ordinal));
        repository.Write(featurePath,
            "---\ntitle: Operator foundation\ntype: feature-specification\n---\n\n" +
            "## Functional requirements\n\n| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n" +
            "|---|---|---|---|---|\n" +
            "| OPS-001 | delivery | not-applicable | Provide an API-only operational foundation. | The service starts and reports readiness. |\n\n" +
            "## UX, screens, and accessibility\n\nNo product screen, wireframe, or visual-design approval is required.\n");
        var root = Path.Combine(repository.Path, "docs", "cis");
        var before = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

        var result = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, featurePath));

        Assert.Equal("invalid", result.Status);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.Contains(obsolete.Id, StringComparison.Ordinal)
            && error.Contains("unsafe retained task path", StringComparison.Ordinal));
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, snapshot) in before)
        {
            Assert.Equal(snapshot.Bytes, File.ReadAllBytes(path));
            Assert.Equal(snapshot.Modified, File.GetLastWriteTimeUtc(path));
        }
        Assert.True(File.Exists(originalTaskPath));
        Assert.False(Directory.Exists(Path.Combine(dossier, "agent-tasks", "retired")));
    }
}
