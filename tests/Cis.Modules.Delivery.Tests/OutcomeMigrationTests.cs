using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomeMigration_PreservesCanonicalApprovalAndRejectsStaleScopeBeforeNoOp()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices(taskTypes: CreateSearchRegistry());
        var change = PrepareSearchChange(repository, services);
        Assert.Equal(0, services.Plans.SelectCapability(new(repository.Path, "parr.search", "parr.search.legacy", [],
            "architect", "Retain the original product capability selection.")).ExitCode);
        var request = OutcomeRequest(repository.Path) with
        {
            Outcome = change.Outcome, Controllers = ["Controller"], VisualUi = false
        };
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize this bounded outcome and controller-led task organization."));
        Assert.Equal(0, authorized.ExitCode);
        var authority = Assert.IsType<CisOutcomeAuthorization>(authorized.Authorization);
        var authorizationPath = services.Changes.DossierFile(change, "authorization.json");
        var authorityBytes = File.ReadAllBytes(authorizationPath);
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md")
        {
            AuthorizationScopeIds = ["implementation", "documentation"],
            Actor = "Controller", Reason = "Derive the accepted search work under its explicit implementation and documentation scopes."
        });
        AssertApprovedOutcomePlan(imported);
        var original = Assert.Single(imported.WorkItems, item => item.TaskTypeKey == "parr.search.legacy");
        Assert.Equal(0, services.Plans.SelectCapability(new(repository.Path, "parr.search", "parr.search.projection",
            ["parr.search.legacy"], "architect", "Select the compatible replacement product capability.")).ExitCode);

        var migration = new TaskTypeMigrationRequest(repository.Path, change.Id, original.Id, "parr.search.projection",
            "Controller", "Organize this retained task under the selected compatible provider and original outcome authority.");
        var migrated = services.Plans.MigrateTaskType(migration);
        AssertApprovedOutcomePlan(migrated);
        Assert.True(migrated.Applied);
        var current = Assert.Single(migrated.WorkItems, item => item.Id == original.Id);
        Assert.Equal("Draft", current.Status);
        Assert.Equal("parr.search.projection", current.TaskTypeKey);
        Assert.Equal("2.0", current.TaskTypeVersion);
        Assert.Equal(original.InstanceKey, current.InstanceKey);
        Assert.Equal(original.WorkOwnerTaskId, current.WorkOwnerTaskId);
        Assert.Equal(original.AuthorizationScopeIds, current.AuthorizationScopeIds);
        Assert.Equal(original.AuthorizationScopeBasis, current.AuthorizationScopeBasis);
        AssertEmbeddedTaskProtocol(current);
        var shown = CreateServices(taskTypes: CreateSearchRegistry()).Plans.Show(repository.Path, change.Id);
        Assert.Equal(0, shown.ExitCode);
        Assert.Equal("Approved", shown.PlanStatus);
        Assert.Equal("Draft", Assert.Single(shown.WorkItems, item => item.Id == original.Id).Status);
        Assert.Equal(authorityBytes, File.ReadAllBytes(authorizationPath));
        var eventsPath = services.Changes.DossierFile(change, "events.jsonl");
        var events = OutcomeEventTypes(eventsPath);
        Assert.Equal(1, events.Count(type => type == "outcome-authorized"));
        Assert.Equal(1, events.Count(type => type == "task-type-migrated"));
        Assert.DoesNotContain("outcome-authorization-amended", events);
        Assert.DoesNotContain("plan-approved", events);

        var amended = services.Changes.Authorize(new(repository.Path, change.Id, request with
        {
            Scope = request.Scope.Select(scope => scope.Id == "implementation"
                ? scope with { Description = "Implement the specifically revised accepted search behavior." } : scope).ToArray(),
            DecisionReference = "fixture:targeted-migration-scope-amendment"
        }, "Original human", "Authorize only the specific implementation scope difference."));
        Assert.Equal(0, amended.ExitCode);
        Assert.Equal(authority.Id, amended.Authorization!.Id);
        Assert.Equal(authority.Digest, Assert.Single(amended.Authorization.History).Digest);
        var dossier = Path.GetDirectoryName(authorizationPath)!;
        var before = SnapshotOutcomeDossier(dossier);
        var catalogPath = Path.Combine(repository.Path, "docs/cis/catalog.yml");
        var catalog = File.ReadAllBytes(catalogPath);

        var stale = services.Plans.MigrateTaskType(migration);
        Assert.NotEqual(0, stale.ExitCode);
        Assert.False(stale.Applied);
        Assert.Contains(stale.Errors, error => error.Contains("scope", StringComparison.OrdinalIgnoreCase));
        AssertOutcomeDossierUnchanged(before, dossier);
        Assert.Equal(catalog, File.ReadAllBytes(catalogPath));
        Assert.Equal(1, OutcomeEventTypes(eventsPath).Count(type => type == "task-type-migrated"));
    }
}
