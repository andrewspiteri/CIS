using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomeAuthorization_ReusesOriginalReviewAndRetainsSpecificAmendmentHistory()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = CreateAdHocChange(repository, services, AdHocFinding("IMPACT-OUTCOME", "orders-api"));
        var request = OutcomeRequest(repository.Path);
        var first = services.Changes.Authorize(new(repository.Path, change.Id, request, "Original human", "Approve the explicit outcome and limits."));
        Assert.Equal(0, first.ExitCode);
        var original = Assert.IsType<CisOutcomeAuthorization>(first.Authorization);
        var sharedView = File.ReadAllText(services.Changes.DossierFile(change, "proposal.md"));
        Assert.Contains("[the canonical authorization](authorization.json)", sharedView, StringComparison.Ordinal);
        Assert.Contains(request.DecisionReference, sharedView, StringComparison.Ordinal);
        Assert.Contains("Original human", sharedView, StringComparison.Ordinal);
        Assert.Contains("300 active seconds", sharedView, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO: define outcome-level acceptance", sharedView, StringComparison.Ordinal);
        var path = services.Changes.DossierFile(change, "authorization.json");
        var bytes = File.ReadAllBytes(path);
        var events = File.ReadAllBytes(services.Changes.DossierFile(change, "events.jsonl"));

        var repeated = services.Changes.Authorize(new(repository.Path, change.Id,
            request with { Scope = request.Scope.Reverse().ToArray(), Controllers = ["Controller", "Controller"] },
            "Another caller", "Reuse does not invent another human review."));

        Assert.Equal(0, repeated.ExitCode);
        Assert.False(repeated.Applied);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(repeated.Authorization));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(events, File.ReadAllBytes(services.Changes.DossierFile(change, "events.jsonl")));
        var changed = request with { Seconds = 600 };
        var denied = services.Changes.Authorize(new(repository.Path, change.Id, changed, "Original human", "This is a changed overall limit."));
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var amended = services.Changes.Authorize(new(repository.Path, change.Id,
            changed with { DecisionReference = "fixture:targeted-limit-amendment" }, "Original human", "Authorize the specific additional overall allowance."));
        Assert.Equal(0, amended.ExitCode);
        var current = Assert.IsType<CisOutcomeAuthorization>(services.Changes.ReadAuthorization(repository.Path, change.Id));
        Assert.Equal(original.Id, current.Id);
        Assert.Equal(original.Reviewer, current.Reviewer);
        var history = Assert.Single(current.History);
        Assert.Equal(original.Digest, history.Digest);
        Assert.Equal(original.AuthorizedAtUtc, history.AuthorizedAtUtc);
        Assert.Equal(original.Reason, history.Reason);
    }

    [Fact]
    public void OutcomeScopeBasis_ExcludesLimitsControllersAndUnselectedScopesButIncludesAcceptedBehavior()
    {
        using var repository = TemporaryRepository.Create();
        var request = OutcomeRequest(repository.Path);
        CisOutcomeAuthorization Authorization(CisOutcomeAuthorizationRequest value)
            => new("fixture:outcome", "CIS-0001", value, "Human", "Approve the explicit scope.",
                "2026-11-13T00:00:00Z", CisOutcomeAuthority.Digest(value));
        var first = Authorization(request);
        var basis = CisOutcomeAuthority.ScopeBasis(first, ["implementation"]);
        var otherWork = request with
        {
            Seconds = 600, Controllers = ["Another controller"],
            Scope = [request.Scope[0], new("documentation", "Changed independent supporting documentation", ["docs"])]
        };
        Assert.Equal(basis, CisOutcomeAuthority.ScopeBasis(Authorization(otherWork), ["implementation"]));
        Assert.NotEqual(first.Digest, CisOutcomeAuthority.Digest(otherWork));
        Assert.NotEqual(basis, CisOutcomeAuthority.ScopeBasis(Authorization(request with { Constraints = ["Preserve the revised contract."] }), ["implementation"]));
        Assert.Throws<InvalidDataException>(() => CisOutcomeAuthority.ScopeBasis(first, ["outside-scope"]));
        Assert.True(CisOutcomeAuthority.IsController(first, "Controller"));
        Assert.False(CisOutcomeAuthority.IsController(first, "Unassigned actor"));
    }

    [Fact]
    public void OutcomeAuthorization_DerivesAcceptedApplicabilityAndRepairsViewsWithoutAnotherReview()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = CreateAdHocChange(repository, services, AdHocFinding("IMPACT-APPLICABILITY", "orders-api"));
        var store = new ChangeDossierStore(new CisRepositoryContextResolver(), documentApplicabilitySources: [new AcceptedOutcomeNoUiSource()]);
        var request = OutcomeRequest(repository.Path) with { ManualTestCatalogue = true };
        var first = store.Authorize(new(repository.Path, change.Id, request, "Original human", "Authorize this outcome using the accepted product direction."));
        Assert.Equal(0, first.ExitCode);
        var authorization = Assert.IsType<CisOutcomeAuthorization>(first.Authorization);
        Assert.False(authorization.Request.VisualUi);
        Assert.True(authorization.VisualUiDerivedFromAcceptedSource);
        var path = store.DossierFile(change, "authorization.json");
        var bytes = File.ReadAllBytes(path);
        var catalogue = store.DossierFile(change, "test-cases.md");
        Assert.True(File.Exists(catalogue));
        File.Delete(catalogue);

        var reused = store.Authorize(new(repository.Path, change.Id, request, "Another caller", "Recover a derived view under existing authority."));

        Assert.Equal(0, reused.ExitCode);
        Assert.Equal("authorization-reused", reused.Status);
        Assert.True(reused.Applied);
        Assert.True(File.Exists(catalogue));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("Original human", reused.Authorization!.Reviewer);
        Assert.True(reused.Authorization.VisualUiDerivedFromAcceptedSource);
    }

    [Fact]
    public void OutcomeAuthorization_ReadRejectsChangedPayloadAndUnsafeExplicitPrefixes()
    {
        using var repository = TemporaryRepository.Create();
        var request = OutcomeRequest(repository.Path);
        var docs = Path.Combine(repository.Path, "docs/cis");
        var context = new CisRepositoryContext(repository.Path, "fixture", "docs/cis", docs, Path.Combine(docs, "catalog.yml"));
        var path = CisOutcomeAuthority.Path(context, "CIS-0001");
        Assert.Null(CisOutcomeAuthority.Read(context, "CIS-0001"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var authorization = new CisOutcomeAuthorization("fixture:outcome", "CIS-0001", request, "Human", "Approved scope.",
            "2026-11-13T00:00:00Z", CisOutcomeAuthority.Digest(request));
        File.WriteAllText(path, JsonSerializer.Serialize(authorization with { Request = request with { Seconds = 600 } }));
        Assert.Throws<InvalidDataException>(() => CisOutcomeAuthority.Read(context, "CIS-0001"));
        Assert.Throws<InvalidDataException>(() => CisOutcomeAuthority.Normalize(request with
        { Scope = [new("implementation", "Escaping work", ["../outside"])] }));
        Assert.Throws<InvalidDataException>(() => CisOutcomeAuthority.Normalize(request with
        { Scope = [new("implementation", "Guessed work", ["src/**"])] }));
    }

    [Fact]
    public void OutcomeAuthorization_ContextPreservesLegacyBindingAndTracksOnlyApplicableNewGates()
    {
        using var repository = TemporaryRepository.Create();
        var docs = Path.Combine(repository.Path, "docs/cis");
        var context = new CisRepositoryContext(repository.Path, "fixture", "docs/cis", docs, Path.Combine(docs, "catalog.yml"));
        var authorizationPath = CisOutcomeAuthority.Path(context, "CIS-0001");
        var dossier = Path.GetDirectoryName(authorizationPath)!;
        Directory.CreateDirectory(dossier);
        var proposal = Path.Combine(dossier, "proposal.md");
        var legacy = CisTaskAuthorityContext.Capture(context, "CIS-0001");
        File.WriteAllText(proposal, "An authored legacy contract.");
        Assert.NotEqual(legacy, CisTaskAuthorityContext.Capture(context, "CIS-0001"));
        var request = OutcomeRequest(repository.Path) with { VisualUi = false };
        CisOutcomeAuthorization Authorization(CisOutcomeAuthorizationRequest value)
            => new("fixture:stable-outcome", "CIS-0001", value, "Human", "Authorize this explicit outcome.",
                "2026-11-13T00:00:00Z", CisOutcomeAuthority.Digest(value));
        File.WriteAllText(authorizationPath, JsonSerializer.Serialize(Authorization(request)));
        var authorized = CisTaskAuthorityContext.Capture(context, "CIS-0001");
        File.WriteAllText(proposal, "A refreshed human view with different formatting.");
        File.WriteAllText(Path.Combine(dossier, "impact.md"), "An independent supporting impact.");
        Assert.Equal(authorized, CisTaskAuthorityContext.Capture(context, "CIS-0001"));

        File.WriteAllText(authorizationPath, JsonSerializer.Serialize(Authorization(request with { VisualUi = true })));
        var design = Path.Combine(dossier, "design.md");
        File.WriteAllText(design, "---\napproval_status: Approved\ngate_status: Approved\n---\n## Approval decision\n\n| Approved | Reviewer | Date | wireframe | renderer-one | manifest | Reason |\n");
        var ui = CisTaskAuthorityContext.Capture(context, "CIS-0001");
        File.AppendAllText(design, "\n## Notes\nA generated explanatory paragraph.\n");
        Assert.Equal(ui, CisTaskAuthorityContext.Capture(context, "CIS-0001"));
        File.WriteAllText(design, File.ReadAllText(design).Replace("renderer-one", "renderer-two", StringComparison.Ordinal));
        Assert.NotEqual(ui, CisTaskAuthorityContext.Capture(context, "CIS-0001"));
    }

    private static CisOutcomeAuthorizationRequest OutcomeRequest(string repository)
        => new("Deliver the existing bounded outcome.",
            [new("implementation", "Implement the accepted behavior.", ["src"]), new("documentation", "Document the same outcome.", ["docs"])],
            ["Preserve public contracts."], 300, 3, 30,
            new(1024 * 1024, [new(repository, ".cis/local")]) { RestorationBytes = 1024 },
            ["Controller"], true, true, null, false, "fixture:outcome-decision");

    private sealed class AcceptedOutcomeNoUiSource : IChangeDocumentApplicabilitySource
    {
        public ChangeDocumentApplicabilitySource DocumentApplicability(string repositoryPath) => new(true, false, []);
    }
}
