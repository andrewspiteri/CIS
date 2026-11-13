using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomeImport_DraftBuildThenLegacyImportsKeepOnlyTheLegacyCanonicalPlan()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Bounded value change", "Keep the current value within the existing component.",
            [new ChangeRoot("orders-api", "component")])).Change);
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/outcome-value.md";
        WriteOutcomeImportSpec(repository, source);
        var draft = services.Plans.Build(repository.Path, change.Id);
        Assert.Equal(0, draft.ExitCode);
        Assert.Equal("Draft", draft.PlanStatus);
        var seed = Assert.Single(draft.WorkItems);
        WriteAdHocImpact(services, change, AdHocFinding("IMPACT-VALUE", "orders-api"));
        var canonicalPath = services.Changes.DossierFile(change, "plan.json");
        Assert.True(File.Exists(canonicalPath));
        var dossier = Path.GetDirectoryName(canonicalPath)!;
        var before = SnapshotOutcomeDossier(dossier);
        var catalogPath = Path.Combine(repository.Path, "docs/cis/catalog.yml");
        var catalog = File.ReadAllBytes(catalogPath);

        var failed = services.Plans.ImportSpec(new(repository.Path, change.Id, "docs/cis/specs/missing.md"));
        Assert.NotEqual(0, failed.ExitCode);
        Assert.False(failed.Applied);
        AssertOutcomeDossierUnchanged(before, dossier);
        Assert.Equal(catalog, File.ReadAllBytes(catalogPath));

        var first = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(first.ExitCode == 0, string.Join("; ", first.Errors.Concat(first.Validation?.Errors ?? [])));
        Assert.Equal("Draft", first.PlanStatus);
        Assert.False(File.Exists(canonicalPath));
        Assert.DoesNotContain(first.WorkItems, item => item.Id == seed.Id);
        Assert.Contains(first.WorkItems, item => item.RequirementIds.Contains("VALUE-001", StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(dossier, "agent-tasks/retired", Path.GetFileName(seed.TaskPath!))));
        var second = CreateServices().Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(second.ExitCode == 0, string.Join("; ", second.Errors.Concat(second.Validation?.Errors ?? [])));
        Assert.False(File.Exists(canonicalPath));
        Assert.Equal(first.WorkItems.Select(item => item.Id), second.WorkItems.Select(item => item.Id));
        var shown = CreateServices().Plans.Show(repository.Path, change.Id);
        Assert.Equal(source, shown.Source!.Path);
        Assert.Equal(first.WorkItems.Select(item => item.Id), shown.WorkItems.Select(item => item.Id));

        var approved = services.Plans.Approve(repository.Path, change.Id,
            "Exact plan human", "Approve only this unchanged legacy feature plan.");
        Assert.Equal(0, approved.ExitCode);
        Assert.Equal("Approved", CreateServices().Plans.Show(repository.Path, change.Id).PlanStatus);
        Assert.False(File.Exists(canonicalPath));
        Assert.Null(services.Changes.ReadAuthorization(repository.Path, change.Id));
    }

    [Fact]
    public void OutcomeImport_ExplicitScopeBindingsKeepCanonicalTasksAndUnchangedSourceContracts()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Authorized value change", "Keep the current value and document its accepted behavior.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var request = OutcomeRequest(repository.Path) with { Outcome = change.Outcome, VisualUi = false };
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize the bounded outcome, implementation and documentation scopes once."));
        Assert.Equal(0, authorized.ExitCode);
        var authority = Assert.IsType<CisOutcomeAuthorization>(authorized.Authorization);
        AssertApprovedOutcomePlan(services.Plans.Build(repository.Path, change.Id));
        var supporting = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Write the independent usage note", ["documentation"], "Describe existing value replacement.", "Review the note against the accepted contract.")
                { Paths = ["docs/value-guide.md"], Category = "documentation" }],
            "Controller", "Retain supporting documentation separately from feature-derived implementation."));
        AssertApprovedOutcomePlan(supporting);
        var support = Assert.Single(supporting.WorkItems, item => item.Title == "Write the independent usage note");
        var canonicalPath = services.Changes.DossierFile(change, "plan.json");
        var dossier = Path.GetDirectoryName(canonicalPath)!;
        var supportPath = Path.Combine(dossier, support.TaskPath!);
        File.AppendAllText(supportPath, "\n## Work notes\n\nRetain the documentation agent's authored note.\n");
        var supportBytes = File.ReadAllBytes(supportPath);
        var authorizationBytes = File.ReadAllBytes(services.Changes.DossierFile(change, "authorization.json"));
        const string source = "docs/cis/specs/outcome-value.md";
        WriteOutcomeImportSpec(repository, source);
        var import = new FeatureSpecImportRequest(repository.Path, change.Id, source)
        {
            Actor = "Controller", Reason = "Derive implementation tasks within the explicitly accepted implementation scope."
        };
        var before = SnapshotOutcomeDossier(dossier);
        var missingScope = services.Plans.ImportSpec(import);
        Assert.NotEqual(0, missingScope.ExitCode);
        Assert.False(missingScope.Applied);
        AssertOutcomeDossierUnchanged(before, dossier);

        import = import with { AuthorizationScopeIds = ["implementation"] };
        var first = services.Plans.ImportSpec(import);
        AssertApprovedOutcomePlan(first);
        Assert.True(File.Exists(canonicalPath));
        var feature = Assert.Single(first.WorkItems, item => item.RequirementIds.Contains("VALUE-001", StringComparer.Ordinal));
        Assert.Equal(new[] { "implementation" }, feature.AuthorizationScopeIds);
        Assert.Equal(CisOutcomeAuthority.ScopeBasis(authority, ["implementation"]), feature.AuthorizationScopeBasis);
        Assert.Equal(support.InstanceKey, Assert.Single(first.WorkItems, item => item.Id == support.Id).InstanceKey);
        Assert.Equal(supportBytes, File.ReadAllBytes(supportPath));
        var featurePath = Path.Combine(dossier, feature.TaskPath!);
        var text = File.ReadAllText(featurePath);
        Assert.Contains("authority: derived-outcome-authorization", text, StringComparison.Ordinal);
        Assert.Contains("outcome_authorization_id: " + JsonSerializer.Serialize(authority.Id), text, StringComparison.Ordinal);
        Assert.Contains("authorization_scope_ids: [\"implementation\"]", text, StringComparison.Ordinal);
        Assert.Contains("authorization_scope_basis: " + JsonSerializer.Serialize(feature.AuthorizationScopeBasis), text, StringComparison.Ordinal);
        var started = services.Plans.TransitionTask(new(repository.Path, change.Id, feature.Id,
            "InProgress", "Controller", "Begin the feature task under the same outcome authority."));
        AssertApprovedOutcomePlan(started);
        File.AppendAllText(featurePath, "\n## Work notes\n\nRetain the implementation agent's authored observation.\n");
        var taskBytes = File.ReadAllBytes(featurePath);
        var retainedItems = JsonSerializer.Serialize(started.WorkItems);
        var eventsPath = services.Changes.DossierFile(change, "events.jsonl");
        var events = File.ReadAllBytes(eventsPath);

        var second = CreateServices().Plans.ImportSpec(import);
        AssertApprovedOutcomePlan(second);
        Assert.Equal("unchanged", second.Status);
        Assert.False(second.Applied);
        Assert.Equal(retainedItems, JsonSerializer.Serialize(second.WorkItems));
        Assert.Equal(taskBytes, File.ReadAllBytes(featurePath));
        Assert.Equal(supportBytes, File.ReadAllBytes(supportPath));
        Assert.Equal(authorizationBytes, File.ReadAllBytes(services.Changes.DossierFile(change, "authorization.json")));
        Assert.Equal(events, File.ReadAllBytes(eventsPath));
        Assert.Equal(source, CreateServices().Plans.Show(repository.Path, change.Id).Source!.Path);
        Assert.Equal(1, OutcomeEventTypes(eventsPath).Count(type => type == "outcome-authorized"));
        Assert.DoesNotContain("plan-approved", OutcomeEventTypes(eventsPath));

        var sourcePath = Path.Combine(repository.Path, source);
        File.WriteAllText(sourcePath, File.ReadAllText(sourcePath).Replace("Keep the current value", "Keep a specifically revised current value", StringComparison.Ordinal));
        var retained = SnapshotOutcomeDossier(dossier);
        var changed = services.Plans.ImportSpec(import);
        Assert.NotEqual(0, changed.ExitCode);
        Assert.False(changed.Applied);
        AssertOutcomeDossierUnchanged(retained, dossier);
        Assert.Contains(services.Plans.Validate(repository.Path, change.Id).Validation!.Errors,
            error => error.Contains("specification changed", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => services.Plans.ReadTaskDigest(repository.Path, change.Id, feature.Id));
        Assert.False(string.IsNullOrWhiteSpace(services.Plans.ReadTaskDigest(repository.Path, change.Id, support.Id)));
    }

    private static void WriteOutcomeImportSpec(TemporaryRepository repository, string path)
        => repository.Write(path, """
            ---
            title: Bounded current value
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Keep the current value without network access, persistence or an integration provider. | Replacing a value returns its previous value. |
            """);
}
