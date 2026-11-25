using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void CurrentImpactRefreshesDirtyInputsAndPreservesApprovedBaselineAndFindings()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.Api/Old.cs", "public class Old {}");
        var services = CreateServices();
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Current order implementation", "Inspect current order dependencies.", [new("orders-api", "component")])).Change);
        var request = new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false);
        Assert.Equal(0, services.Impacts.Analyse(request).ExitCode);
        var baseline = File.ReadAllBytes(services.Changes.DossierFile(change, "proposal.md"));
        var historicalFindings = File.ReadAllBytes(services.Changes.DossierFile(change, "impact.md"));

        File.Delete(Path.Combine(repository.Path, "src/Orders.Api/Old.cs"));
        repository.Write("src/Orders.Api/New.cs", "public class New {}");
        repository.Write("src/Orders.Api/Program.cs", "public class ChangedProgram {}");
        var resolver = new CisRepositoryContextResolver();
        var currentService = new ImpactAnalysisService(services.Changes, new GraphQueryService(resolver),
            new GraphSnapshotReader(resolver, builder: services.Graph));
        var result = currentService.Analyse(request with { CurrentImplementation = true });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("current-implementation", result.AnalysisBasis);
        Assert.Contains(result.DecisionSuitability, new[] { "bounded-current", "incomplete-current" });
        Assert.NotEqual(change.GraphBuildId, result.GraphBuildId);
        Assert.Contains(result.Findings, item => item.Evidence.Contains("src/Orders.Api/New.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, item => item.Evidence.Contains("src/Orders.Api/Old.cs", StringComparison.Ordinal));
        Assert.False(result.Applied);
        Assert.Equal(baseline, File.ReadAllBytes(services.Changes.DossierFile(change, "proposal.md")));
        Assert.Equal(historicalFindings, File.ReadAllBytes(services.Changes.DossierFile(change, "impact.md")));
        Assert.NotEqual(0, services.Impacts.Analyse(request).ExitCode);
    }

    [Fact]
    public void CurrentImpactDisclosesExtractionLimitationsWithoutClaimingCompleteCurrentImpact()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Bounded impact", "Inspect the existing component.", [new("orders-api", "component")])).Change);
        repository.Write("docs/cis/unlisted.md", "An unrelated uncatalogued document.");
        var resolver = new CisRepositoryContextResolver();
        var service = new ImpactAnalysisService(services.Changes, new GraphQueryService(resolver),
            new GraphSnapshotReader(resolver, builder: services.Graph));

        var result = service.Analyse(new(repository.Path, change.Id, [], 2, 200, false) { CurrentImplementation = true });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("current-partial", result.Status);
        Assert.Equal("incomplete-current", result.DecisionSuitability);
        Assert.Contains(result.Diagnostics, item => item.Contains("CIS-GRAPH-DOC-002", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Contains("do not establish complete current impact", StringComparison.Ordinal));
        Assert.False(File.Exists(services.Changes.DossierFile(change, "impact.md")));
    }
}
