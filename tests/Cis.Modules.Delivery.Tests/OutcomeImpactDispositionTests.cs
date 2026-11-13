using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Change;
using Cis.Modules.Context;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomeImpactDisposition_RetainsOriginalHumanAndActualControllerWithExactScope()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Bounded existing component", "Deliver the accepted component behavior.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var request = new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false);
        var analysed = services.Impacts.Analyse(request);
        Assert.Equal(0, analysed.ExitCode);
        var finding = Assert.Single(analysed.Findings, item => item.Rationale == "Explicit analysis root.");
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with { VisualUi = false }, "Original human", "Authorize the bounded outcome once."));
        Assert.Equal(0, authorized.ExitCode);

        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new DocsModule())
            .AddModule(new GraphModule()).AddModule(new ContextModule()).AddModule(new ChangeModule())
            .AddModule(new ImpactModule()).Build();
        Assert.Equal(0, application.Invoke(["impact", "accept", change.Id, finding.Id, "--actor", "Controller",
            "--scope", "implementation", "--reason", "The exact component belongs to the accepted implementation scope.",
            "--repo", repository.Path, "--format", "json"]));
        var derived = services.Impacts.Findings(repository.Path, change.Id);
        Assert.Equal(0, derived.ExitCode);
        var provenance = Assert.IsType<ImpactOutcomeDisposition>(Assert.Single(derived.Findings,
            item => item.Id == finding.Id).OutcomeDisposition);
        Assert.Equal("Original human", provenance.Reviewer);
        Assert.Equal("Controller", provenance.Actor);
        Assert.Equal(authorized.Authorization!.Id, provenance.AuthorizationId);
        Assert.Equal(authorized.Authorization.Request.DecisionReference, provenance.DecisionReference);
        Assert.NotEmpty(provenance.Paths);
        var reloaded = CreateServices().Impacts.ReadImpact(change);
        Assert.Empty(reloaded.Errors);
        Assert.Equal(JsonSerializer.Serialize(provenance), JsonSerializer.Serialize(
            Assert.Single(reloaded.Findings, item => item.Id == finding.Id).OutcomeDisposition));
        Assert.Equal(0, services.Impacts.Analyse(request).ExitCode);
        Assert.Equal(JsonSerializer.Serialize(provenance), JsonSerializer.Serialize(
            Assert.Single(services.Impacts.ReadImpact(change).Findings, item => item.Id == finding.Id).OutcomeDisposition));

        var path = services.Changes.DossierFile(change, "impact.md");
        var retained = File.ReadAllBytes(path);
        Assert.NotEqual(0, services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Unassigned actor", ["implementation"], "No delegated authority.").ExitCode);
        Assert.NotEqual(0, services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["documentation"], "The component does not belong to this scope.").ExitCode);
        Assert.Equal(retained, File.ReadAllBytes(path));
        var original = File.ReadAllText(path);
        var forged = original.Replace("\"Reviewer\":\"Original human\"", "\"Reviewer\":\"Invented human\"", StringComparison.Ordinal);
        Assert.NotEqual(original, forged);
        File.WriteAllText(path, forged);
        Assert.NotEmpty(services.Impacts.ReadImpact(change).Errors);
        File.WriteAllBytes(path, retained);
        Assert.Equal(0, services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "rejected", "An explicit human excludes this finding.").ExitCode);
        Assert.NotEqual(0, services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "Do not replace that specific decision.").ExitCode);
        Assert.Equal("rejected", Assert.Single(services.Impacts.ReadImpact(change).Findings, item => item.Id == finding.Id).State);
    }
}
