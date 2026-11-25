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
        var previousOutput = Console.Out;
        using var output = new StringWriter();
        int accepted;
        try
        {
            Console.SetOut(output);
            accepted = application.Invoke(["impact", "accept", change.Id, finding.Id, "--actor", "Controller",
                "--scope", "implementation", "--reason", "The exact component belongs to the accepted implementation scope.",
                "--repo", repository.Path, "--format", "json"]);
        }
        finally { Console.SetOut(previousOutput); }
        Assert.True(accepted == 0, output.ToString());
        Assert.Equal(change.GraphBuildId, new SqliteGraphStore().Read(repository.Path).Graph!.Build.Id);
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

    [Fact]
    public void OutcomeImpactDispositionRefreshCannotAcceptAChangedMaterialBaseline()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Bounded existing component", "Deliver the accepted component behavior.", [new ChangeRoot("orders-api", "component")])).Change);
        var analysed = services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false));
        Assert.Equal(0, analysed.ExitCode);
        var finding = Assert.Single(analysed.Findings, item => item.Rationale == "Explicit analysis root.");
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with { VisualUi = false }, "Original human", "Authorize the exact material baseline."));
        Assert.Equal(0, authorized.ExitCode);
        var impact = File.ReadAllBytes(services.Changes.DossierFile(change, "impact.md"));
        var proposal = File.ReadAllBytes(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("src/Orders.Api/Changed.cs", "public class ChangedImplementation {}");
        var resolver = new CisRepositoryContextResolver();
        var currentService = new ImpactAnalysisService(services.Changes, new GraphQueryService(resolver),
            new GraphSnapshotReader(resolver, builder: services.Graph));

        var result = currentService.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "A refresh cannot enlarge the original authority.");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("exact retained graph baseline", StringComparison.Ordinal));
        Assert.NotEqual(change.GraphBuildId, new SqliteGraphStore().Read(repository.Path).Graph!.Build.Id);
        Assert.Equal(impact, File.ReadAllBytes(services.Changes.DossierFile(change, "impact.md")));
        Assert.Equal(proposal, File.ReadAllBytes(services.Changes.DossierFile(change, "proposal.md")));
        Assert.Equal(authorized.Authorization!.Digest, services.Changes.ReadAuthorization(repository.Path, change.Id)!.Digest);
    }

    [Fact]
    public void OutcomeImpactDispositionAcceptsAuthorizedCanonicalComponentOwnership()
    {
        using var repository = TemporaryRepository.Create(buildGraph: false);
        var (services, change, finding) = CreateComponentOwnershipFinding(repository, canonicalOnly: true);
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with
            {
                VisualUi = false,
                Scope = [new("implementation", "Implement the canonical component.", ["src/Orders.Canonical"])]
            }, "Original human", "Authorize the declared canonical ownership."));
        Assert.Equal(0, authorized.ExitCode);

        var result = services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "The canonical ownership is covered by the accepted scope.");

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("derived", result.Status);
        var disposition = Assert.IsType<ImpactOutcomeDisposition>(Assert.Single(result.Findings,
            item => item.Id == finding.Id).OutcomeDisposition);
        Assert.Equal(["src/Orders.Canonical"], disposition.Paths);
        Assert.Equal("Original human", disposition.Reviewer);
    }

    [Fact]
    public void OutcomeImpactDispositionRefusesPartiallyAuthorizedMergedComponentOwnership()
    {
        using var repository = TemporaryRepository.Create(buildGraph: false);
        var (services, change, finding) = CreateComponentOwnershipFinding(repository, canonicalOnly: false);
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with
            {
                VisualUi = false,
                Scope = [new("implementation", "Implement only the legacy location.", ["src/Orders.Api"])]
            }, "Original human", "Authorize the original component location only."));
        Assert.Equal(0, authorized.ExitCode);
        var impactPath = services.Changes.DossierFile(change, "impact.md");
        var retainedImpact = File.ReadAllBytes(impactPath);

        var result = services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "The second declared location has not been authorized.");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("out-of-scope concrete paths", StringComparison.Ordinal));
        Assert.Equal(retainedImpact, File.ReadAllBytes(impactPath));
        Assert.Equal(authorized.Authorization!.Digest, services.Changes.ReadAuthorization(repository.Path, change.Id)!.Digest);
    }

    [Fact]
    public void OutcomeImpactDispositionRetainsEveryAuthorizedMergedComponentPath()
    {
        using var repository = TemporaryRepository.Create(buildGraph: false);
        var (services, change, finding) = CreateComponentOwnershipFinding(repository, canonicalOnly: false);
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with { VisualUi = false }, "Original human", "Authorize both declared locations."));
        Assert.Equal(0, authorized.ExitCode);

        var result = services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "Both the legacy and canonical ownership are covered.");

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Diagnostics));
        var disposition = Assert.IsType<ImpactOutcomeDisposition>(Assert.Single(result.Findings,
            item => item.Id == finding.Id).OutcomeDisposition);
        Assert.Equal(["src/Orders.Api", "src/Orders.Secondary"], disposition.Paths);
        var reloaded = services.Impacts.ReadImpact(change);
        Assert.Empty(reloaded.Errors);
        Assert.Equal(disposition.Paths, Assert.Single(reloaded.Findings, item => item.Id == finding.Id).OutcomeDisposition!.Paths);

        // Model a retained disposition written before canonical ownership entered scope validation.
        var impactPath = services.Changes.DossierFile(change, "impact.md");
        var complete = File.ReadAllText(impactPath);
        var incompleteDisposition = disposition with { Paths = ["src/Orders.Api"] };
        Assert.Contains(JsonSerializer.Serialize(disposition), complete, StringComparison.Ordinal);
        File.WriteAllText(impactPath, complete.Replace(JsonSerializer.Serialize(disposition),
            JsonSerializer.Serialize(incompleteDisposition), StringComparison.Ordinal));
        Assert.Empty(services.Impacts.ReadImpact(change).Errors);
        var refreshed = services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "Retain the complete declared paths under the same accepted authority.");
        Assert.True(refreshed.ExitCode == 0, string.Join(Environment.NewLine, refreshed.Diagnostics));
        Assert.Equal("derived", refreshed.Status);
        Assert.Equal(disposition.Paths, Assert.Single(refreshed.Findings, item => item.Id == finding.Id).OutcomeDisposition!.Paths);

        var retained = File.ReadAllBytes(services.Changes.DossierFile(change, "impact.md"));
        var repeated = services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "Reuse the complete accepted ownership scope.");
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
        Assert.Equal(retained, File.ReadAllBytes(services.Changes.DossierFile(change, "impact.md")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutcomeImpactDispositionRequiresExplicitRepositoryScopeForRootOwnership(bool wholeRepositoryAuthorized)
    {
        using var repository = TemporaryRepository.Create(buildGraph: false);
        var (services, change, finding) = CreateComponentOwnershipFinding(repository, canonicalOnly: true, ownershipPath: ".");
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with
            {
                VisualUi = false,
                Scope = [new("implementation", "Implement the declared ownership.", [wholeRepositoryAuthorized ? "." : "src"])]
            }, "Original human", "Authorize only the explicit scope."));
        Assert.Equal(0, authorized.ExitCode);
        var impactPath = services.Changes.DossierFile(change, "impact.md");
        var retained = File.ReadAllBytes(impactPath);

        var result = services.Impacts.DispositionUnderOutcome(repository.Path, change.Id, finding.Id,
            "Controller", ["implementation"], "Check the complete repository-root ownership.");

        if (wholeRepositoryAuthorized)
        {
            Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Diagnostics));
            var disposition = Assert.IsType<ImpactOutcomeDisposition>(Assert.Single(result.Findings,
                item => item.Id == finding.Id).OutcomeDisposition);
            Assert.Equal(["."], disposition.Paths);
        }
        else
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("out-of-scope concrete paths", StringComparison.Ordinal));
            Assert.Equal(retained, File.ReadAllBytes(impactPath));
        }
    }

    private static (Services Services, ChangeDossier Change, ImpactFinding Finding) CreateComponentOwnershipFinding(
        TemporaryRepository repository, bool canonicalOnly, string? ownershipPath = null)
    {
        var componentId = canonicalOnly ? "canonical-orders" : "orders-api";
        var ownedPath = ownershipPath ?? (canonicalOnly ? "src/Orders.Canonical" : "src/Orders.Secondary");
        repository.Write(ownedPath + "/Owned.cs", "public class OwnedImplementation {}");
        repository.Write("docs/cis/references/component-sheet.md", $$"""
            ---
            title: Components
            type: component-sheet
            status: Draft
            cis:
              stable_id: fixture:component-sheet
            ---

            # Components

            ## Component catalogue

            | Component ID | Name | Classification | Responsibility | Owns | Must not own | BRD authority |
            | --- | --- | --- | --- | --- | --- | --- |
            | {{componentId}} | Orders | module | Process orders | Order behavior | Other behavior | BRD-ORDERS-001 |

            ## Implementation and verification locations

            | Component ID | Relationship | Path |
            | --- | --- | --- |
            | {{componentId}} | owns | {{ownedPath}} |
            """);
        File.AppendAllText(Path.Combine(repository.Path, "docs/cis/catalog.yml"), """

              - id: fixture:component-sheet
                path: docs/cis/references/component-sheet.md
                type: component-sheet
                status: draft
                authority: canonical
            """);
        var services = CreateServices();
        var resolver = new CisRepositoryContextResolver();
        // Use the host's current-reader path: creating/authorizing a dossier changes
        // advisory graph projections while preserving the material implementation baseline.
        services = services with
        {
            Impacts = new ImpactAnalysisService(services.Changes, new GraphQueryService(resolver),
                new GraphSnapshotReader(resolver, builder: services.Graph))
        };
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Complete component ownership", "Authorize the complete declared component.", [new(componentId, "component")])).Change);
        var analysed = services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false));
        Assert.Equal(0, analysed.ExitCode);
        var finding = Assert.Single(analysed.Findings, item => item.Rationale == "Explicit analysis root.");
        return (services, change, finding);
    }
}
