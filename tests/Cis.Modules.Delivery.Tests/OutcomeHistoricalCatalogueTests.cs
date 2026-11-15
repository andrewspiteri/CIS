using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomeHistoricalCatalogue_ImportedUiCompletionRetainsCatalogueAfterNoUiAmendmentAndSupportingWork()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Accepted customer display", "Deliver the accepted customer status display.", [])).Change);
        var request = OutcomeRequest(repository.Path) with { Outcome = change.Outcome, VisualUi = true };
        Assert.Equal(0, services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize the accepted visual work and its applicable catalogue.")).ExitCode);
        const string source = "docs/cis/specs/historical-customer-display.md";
        repository.Write(source, """
            ---
            title: Accepted customer display
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Frontend type | Requirement | Acceptance criteria |
            | --- | --- | --- | --- | --- |
            | VIEW-001 | frontend | customer | Display the current status. | The customer sees the accepted current status. |
            """);
        repository.Write("tests/historical-display.test.ts", "test('TC-VIEW-001-001 displays the accepted current status', () => {});\n");
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, source)
        {
            AuthorizationScopeIds = ["implementation"], Actor = "Controller",
            Reason = "Derive the exact accepted display tasks within their explicit implementation scope."
        });
        AssertApprovedOutcomePlan(imported);
        Assert.True(imported.Source!.FrontendChanges);
        Assert.Single(imported.WorkItems, item => item.Category == "frontend");
        var designPath = services.Changes.DossierFile(change, "design.md");
        // Design rendering has its own lifecycle fixtures; this case retains the
        // original approved UI gate while exercising import and task completion.
        File.WriteAllText(designPath, "---\napproval_status: Approved\ngate_status: Approved\n---\n# Accepted display fixture\n");
        foreach (var task in imported.WorkItems.OrderBy(item => item.Category switch { "wireframe" => 0, "design" => 1, _ => 2 }))
        {
            AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id,
                "InProgress", "Controller", "Begin the originally authorized imported display task.")));
            CompleteProportionateProtocolEvidence(services, change, task);
            AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id,
                "Complete", "Controller", "Complete the original imported task's checked fixture contract.")));
        }
        var completed = services.Plans.Show(repository.Path, change.Id).WorkItems;
        Assert.All(completed, task => Assert.Equal("Complete", task.Status));
        var dossier = Path.Combine(repository.Path, change.RelativePath);
        var taskContracts = JsonSerializer.Serialize(completed);
        var taskBytes = completed.ToDictionary(task => task.Id, task => File.ReadAllBytes(Path.Combine(dossier, task.TaskPath!)));
        var cataloguePath = services.Changes.DossierFile(change, "test-cases.md");
        var csvPath = services.Changes.DossierFile(change, "test-cases.csv");
        var catalogueBytes = File.ReadAllBytes(cataloguePath);
        var csvBytes = File.ReadAllBytes(csvPath);
        var designBytes = File.ReadAllBytes(designPath);
        var sourceBytes = File.ReadAllBytes(Path.Combine(repository.Path, source));
        Assert.Contains("### TC-VIEW-001-001:", File.ReadAllText(cataloguePath), StringComparison.Ordinal);
        Assert.Contains("- Automation status: Automated", File.ReadAllText(cataloguePath), StringComparison.Ordinal);

        var amended = services.Changes.Authorize(new(repository.Path, change.Id, request with
        {
            VisualUi = false, DecisionReference = "fixture:completed-import-future-no-ui"
        }, "Original human", "Preserve completed imported display evidence while excluding future visual work."));
        Assert.Equal(0, amended.ExitCode);
        AssertApprovedOutcomePlan(services.Plans.Validate(repository.Path, change.Id));
        var support = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Independent supporting note", ["documentation"], "The note describes existing completed behavior.", "Review the supporting note.")
                { Paths = ["docs/supporting-note.md"], Category = "documentation" }],
            "Controller", "Organize independent current documentation without changing the completed source or its catalogue."));
        AssertApprovedOutcomePlan(support);
        var supporting = Assert.Single(support.WorkItems, task => task.Title == "Independent supporting note");
        Assert.Empty(supporting.RequirementIds);
        var linkedSupport = new CisFeatureStoryTask("SUPPORT-001", supporting.Title, supporting.AcceptanceCriteria,
            [], [], [supporting.AcceptanceCriteria], [])
        { Category = "documentation", ApprovalGate = "none", ChangeId = change.Id, WorkOwnerTaskId = supporting.Id };
        Assert.Empty(services.Plans.ValidateStoryTaskGate(repository.Path, linkedSupport));
        Assert.Contains(services.Plans.ValidateStoryTaskGate(repository.Path, linkedSupport with
        { Category = "frontend", FrontendType = "customer" }), error => error.Contains("no-UI scope", StringComparison.Ordinal));
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, supporting.Id,
            "InProgress", "Controller", "Continue current non-UI support while retaining historical imported evidence.")));
        CompleteProportionateProtocolEvidence(services, change, supporting);
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, supporting.Id,
            "Complete", "Controller", "Complete current support without reopening historical imported work.")));
        AssertApprovedOutcomePlan(services.Plans.Validate(repository.Path, change.Id));
        Assert.Empty(services.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id));
        AssertApprovedOutcomePlan(CreateServices().Plans.Build(repository.Path, change.Id));
        var retained = services.Plans.Show(repository.Path, change.Id).WorkItems.Where(task => task.RequirementIds.Count > 0).ToArray();
        Assert.Equal(taskContracts, JsonSerializer.Serialize(retained));
        Assert.All(retained, task => Assert.Equal(taskBytes[task.Id], File.ReadAllBytes(Path.Combine(dossier, task.TaskPath!))));
        Assert.Equal(catalogueBytes, File.ReadAllBytes(cataloguePath));
        Assert.Equal(csvBytes, File.ReadAllBytes(csvPath));
        Assert.Equal(designBytes, File.ReadAllBytes(designPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(Path.Combine(repository.Path, source)));

        var before = SnapshotOutcomeDossier(dossier);
        var newUi = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Excluded new display", ["implementation"], "Introduce another display.", "Review another display.")
                { Category = "frontend", Paths = ["src/new-display.cs"] }],
            "Controller", "Historical display evidence cannot authorize new UI under the current no-UI grant."));
        Assert.NotEqual(0, newUi.ExitCode);
        Assert.False(newUi.Applied);
        AssertOutcomeDossierUnchanged(before, dossier);
    }

    [Fact]
    public void OutcomeHistoricalCatalogue_CompletedBackendIgnoresInactiveZeroCaseLegacyCatalogue()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Accepted local value", "Keep the existing bounded local value behavior.", [])).Change);
        var request = OutcomeRequest(repository.Path) with { Outcome = change.Outcome, VisualUi = false };
        Assert.Equal(0, services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize bounded backend work without a manual catalogue obligation.")).ExitCode);
        const string source = "docs/cis/specs/historical-local-value.md";
        WriteOutcomeImportSpec(repository, source);
        var cataloguePath = services.Changes.DossierFile(change, "test-cases.md");
        var csvPath = services.Changes.DossierFile(change, "test-cases.csv");
        File.WriteAllText(cataloguePath, $"---\ntitle: Legacy inactive catalogue\ntype: manual-test-cases\nstatus: NotGenerated\nchange_id: {change.Id}\ntest_case_count: 0\nautomated_test_case_count: 0\nautomation_pending_count: 0\ncsv_path: test-cases.csv\ngeneration: deterministic\nauthority: derived\n---\n\n# Manual test cases\n\nInactive legacy template; this accepted backend work requires no catalogue.\n");
        File.WriteAllText(csvPath, "\"ID\",\"Title\",\"Section\",\"Priority\",\"Type\",\"Preconditions\",\"Steps\",\"Expected Result\",\"References\",\"Frontend Type\",\"Automation Status\",\"Automated Test References\"\r\n");
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, source)
        {
            AuthorizationScopeIds = ["implementation"], Actor = "Controller",
            Reason = "Derive only the accepted local backend behavior."
        });
        AssertApprovedOutcomePlan(imported);
        Assert.False(imported.Source!.FrontendChanges);
        var task = Assert.Single(imported.WorkItems);
        Assert.Equal("backend", task.Category);
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id,
            "InProgress", "Controller", "Begin the original backend fixture contract.")));
        CompleteProportionateProtocolEvidence(services, change, task);
        var finished = services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id,
            "Complete", "Controller", "Complete the original checked backend fixture contract."));
        AssertApprovedOutcomePlan(finished);
        var completed = Assert.Single(finished.WorkItems);
        var taskPath = Path.Combine(repository.Path, change.RelativePath, task.TaskPath!);
        var taskBytes = File.ReadAllBytes(taskPath);
        var taskContract = JsonSerializer.Serialize(completed);
        var catalogueBytes = File.ReadAllBytes(cataloguePath);
        var csvBytes = File.ReadAllBytes(csvPath);
        var sourceBytes = File.ReadAllBytes(Path.Combine(repository.Path, source));
        Assert.Contains("status: NotGenerated", File.ReadAllText(cataloguePath), StringComparison.Ordinal);
        Assert.DoesNotContain("### TC-", File.ReadAllText(cataloguePath), StringComparison.Ordinal);

        var amended = services.Changes.Authorize(new(repository.Path, change.Id, request with
        {
            Scope = request.Scope.Select(scope => scope.Id == "implementation"
                ? scope with { Description = "Implement the specifically revised bounded backend behavior." } : scope).ToArray(),
            DecisionReference = "fixture:completed-backend-scope-amendment"
        }, "Original human", "Revise future implementation scope without inventing a historical catalogue obligation."));
        Assert.Equal(0, amended.ExitCode);
        Assert.False(amended.Authorization!.Request.ManualTestCatalogue);
        AssertApprovedOutcomePlan(services.Plans.Validate(repository.Path, change.Id));
        Assert.Empty(services.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id));
        AssertApprovedOutcomePlan(CreateServices().Plans.Build(repository.Path, change.Id));
        var retained = Assert.Single(services.Plans.Show(repository.Path, change.Id).WorkItems);
        Assert.Equal("Complete", retained.Status);
        Assert.Equal(taskContract, JsonSerializer.Serialize(retained));
        Assert.Equal(taskBytes, File.ReadAllBytes(taskPath));
        Assert.Equal(catalogueBytes, File.ReadAllBytes(cataloguePath));
        Assert.Equal(csvBytes, File.ReadAllBytes(csvPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(Path.Combine(repository.Path, source)));
    }
}
