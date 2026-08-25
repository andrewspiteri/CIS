using Cis.Host;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Context;
using Cis.Modules.Decision;
using Cis.Modules.Design;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Feedback;
using Cis.Modules.Plan;
using Cis.Modules.Repository;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Cis.Modules.Delivery.Tests;

public sealed class DeliveryWorkflowTests
{
    [Theory]
    [InlineData("Approved", "wireframe", true)]
    [InlineData("Approved", "design", true)]
    [InlineData("Decomposed", "coordination", true)]
    [InlineData("Complete", "backend", true)]
    [InlineData("Deferred", "integration", true)]
    [InlineData("Cancelled", "testing", true)]
    [InlineData("Approved", "backend", false)]
    [InlineData("InProgress", "delivery", false)]
    public void TaskDispositionPolicy_UsesRoleAwareTerminalStates(string status, string category, bool expected)
        => Assert.Equal(expected, PlanTaskDispositionPolicy.IsTerminal(status, category));

    [Fact]
    public void ChangeAndPlanBuild_BlockWhenApplicableReadinessCheckFails()
    {
        using var repository = TemporaryRepository.Create();
        var resolver = new CisRepositoryContextResolver();
        var check = new BlockingReadinessCheck();
        var changes = new ChangeDossierStore(resolver, readinessChecks: [check]);

        var create = changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Blocked change",
            "The governed outcome is delivered.",
            []));

        Assert.NotEqual(0, create.ExitCode);
        Assert.Contains(create.Errors, error => error.Contains("technical-intent", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(repository.Path, "docs", "cis", "changes", "CIS-0001")));

        var services = CreateServices();
        var plans = new PlanningService(
            services.Changes,
            services.Impacts,
            services.Decisions,
            resolver,
            readinessChecks: [check]);
        var plan = plans.Build(repository.Path, "CIS-9999");
        Assert.NotEqual(0, plan.ExitCode);
        Assert.Contains(plan.Errors, error => error.Contains("technical-intent", StringComparison.Ordinal));
    }

    [Fact]
    public void ChangeCreate_WritesCompleteCatalogedDossierAgainstExactGraphBaseline()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();

        var result = services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")]));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        var change = Assert.IsType<ChangeDossier>(result.Change);
        Assert.Equal("CIS-0001", change.Id);
        Assert.StartsWith("sha256:", change.GraphBuildId, StringComparison.Ordinal);
        foreach (var file in new[] { "proposal.md", "impact.md", "decisions.md", "plan.md", "wireframes.md", "design.md", "test-cases.md", "test-cases.csv", "verification.md", "events.jsonl" })
        {
            Assert.True(File.Exists(services.Changes.DossierFile(change, file)));
        }

        var catalog = File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "catalog.yml"));
        Assert.Contains("changes/CIS-0001/proposal.md", catalog, StringComparison.Ordinal);
        Assert.Contains("changes/CIS-0001/plan.md", catalog, StringComparison.Ordinal);
        Assert.Contains("changes/CIS-0001/verification.md", catalog, StringComparison.Ordinal);
        Assert.Contains("changes/CIS-0001/design.md", catalog, StringComparison.Ordinal);
        Assert.Contains("changes/CIS-0001/wireframes.md", catalog, StringComparison.Ordinal);
        Assert.Contains("changes/CIS-0001/test-cases.md", catalog, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(
            services.Changes.DossierFile(change, "plan.md"))!, "agent-tasks")));
        Assert.Equal(0, services.DocsValidation.Validate(repository.Path, strict: false).ExitCode);
    }

    [Fact]
    public void PlanDerive_CarriesApprovedFeatureAuthorityAcrossImpactsAndPlanAtomically()
    {
        using var repository = TemporaryRepository.Create();
        const string featurePath = "docs/cis/specs/approved-order-feature.md";
        var authority = new StaticFeatureApprovalAuthority(featurePath);
        var services = CreateServices(featureAuthorities: [authority]);
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Approved order lookup",
            "Customers can retrieve the approved order representation.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(
            repository.Path, change.Id, [], 2, 200, false));
        Assert.NotEmpty(analysis.Findings);
        Assert.All(analysis.Findings, finding => Assert.Equal("proposed", finding.State));
        repository.Write(featurePath, """
---
title: Approved order lookup
type: feature-specification
status: Active
---

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
| ORDER-001 | backend | not-applicable | Return the approved order representation. | The API returns the governed representation for an existing order. |
""");

        var derived = services.Plans.DeriveFromApprovedFeature(new FeatureSpecImportRequest(
            repository.Path, change.Id, featurePath));

        Assert.Equal(0, derived.ExitCode);
        Assert.Equal("derived-and-approved", derived.Status);
        Assert.Equal("Approved", derived.PlanStatus);
        Assert.True(derived.Validation!.Valid);
        Assert.All(services.Impacts.ReadImpact(change).Findings, finding => Assert.NotEqual("proposed", finding.State));
        var plan = File.ReadAllText(services.Changes.DossierFile(change, "plan.md"));
        Assert.Contains("approval_basis: \"approved-feature:HLT-FR-001:sha256:approved-feature\"", plan, StringComparison.Ordinal);
        Assert.Contains("approved_by: \"Product owner\"", plan, StringComparison.Ordinal);
        var proposal = File.ReadAllText(services.Changes.DossierFile(change, "proposal.md"));
        Assert.Contains("- `ORDER-001`: The API returns the governed representation for an existing order.", proposal, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO: define outcome-level acceptance criteria", proposal, StringComparison.Ordinal);
        var events = File.ReadAllText(services.Changes.DossierFile(change, "events.jsonl"));
        Assert.Contains("impact-authority-carried-forward", events, StringComparison.Ordinal);
        Assert.Contains("proposal-acceptance-authority-carried-forward", events, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeManagedDocuments_DoNotSelfInvalidateTheProductGraphBaseline()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")])).Change);

        var rebuilt = services.Graph.Build(repository.Path);

        Assert.Equal(0, rebuilt.ExitCode);
        Assert.Equal(change.GraphBuildId, rebuilt.BuildId);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(
            repository.Path, change.Id, [], 1, 50, false));
        Assert.Equal(0, analysis.ExitCode);
        Assert.NotEmpty(analysis.Findings);
    }

    [Fact]
    public void ImpactAnalyse_EmitsDeclaredFeatureTargetRepositoriesForHumanReview()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var context = Assert.IsType<CisRepositoryContext>(new CisRepositoryContextResolver()
            .Resolve(repository.Path).Context);
        var featureId = $"{context.RepositoryId}:feature:cross-repository";
        const string featurePath = "docs/cis/specs/cross-repository-feature.md";
        repository.Write(featurePath, $$"""
            ---
            title: Cross-repository feature
            type: feature-specification
            status: Active
            targets:
              - todo-api
              - "todo-web"
              - todo-infra
            cis:
              stable_id: {{featureId}}
            ---

            # Cross-repository feature

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | FEAT-001 | delivery | Change three repositories. | All declared targets participate. |
            """);
        File.AppendAllText(context.CatalogPath, $$"""

              - id: {{featureId}}
                path: {{featurePath}}
                type: feature-specification
                status: active
                authority: canonical
            """);
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Cross-repository change",
            "All declared repository targets are reviewed before planning.",
            [new ChangeRoot(featureId, "document")])).Change);

        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(
            repository.Path, change.Id, [], 1, 100, false));

        Assert.Equal(0, analysis.ExitCode);
        Assert.Single(analysis.Findings, item => item.Category == "documentation");
        var repositories = analysis.Findings.Where(item => item.Category == "repository").ToArray();
        Assert.Equal(3, repositories.Length);
        Assert.Equal(["todo-api", "todo-infra", "todo-web"], repositories.Select(item => item.Label));
        Assert.All(repositories, item =>
        {
            Assert.Equal("proposed", item.State);
            Assert.Equal("high", item.Confidence);
            Assert.Contains("frontmatter=targets/", item.Evidence, StringComparison.Ordinal);
            Assert.Contains("Declared target repository", item.Rationale, StringComparison.Ordinal);
        });
        Assert.False(analysis.Completeness!.PlanReady);
    }

    [Fact]
    public void ChangeRebaseline_IsAuditedAndLimitedToPreImpactDossiers()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")])).Change);
        repository.Write("src/Orders.Api/BeforeImpact.cs", "namespace Orders.Api; public sealed class BeforeImpact { }");
        var rebuilt = services.Graph.Build(repository.Path);
        Assert.NotEqual(change.GraphBuildId, rebuilt.BuildId);

        var result = services.Changes.Rebaseline(new ChangeRebaselineRequest(
            repository.Path, change.Id, "review-agent", "Adopt reviewed pre-impact source additions."));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("rebaselined", result.Status);
        Assert.Equal(rebuilt.BuildId, result.Change!.GraphBuildId);
        var events = File.ReadAllText(services.Changes.DossierFile(result.Change, "events.jsonl"));
        Assert.Contains("change-rebaselined", events, StringComparison.Ordinal);
        Assert.Contains("review-agent", events, StringComparison.Ordinal);
        var postRebaseline = services.Impacts.Analyse(new ImpactAnalyseRequest(
            repository.Path, change.Id, [], 1, 50, false));
        Assert.True(postRebaseline.ExitCode == 0, string.Join(Environment.NewLine, postRebaseline.Diagnostics));

        repository.Write("src/Orders.Api/AfterImpact.cs", "namespace Orders.Api; public sealed class AfterImpact { }");
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var blocked = services.Changes.Rebaseline(new ChangeRebaselineRequest(
            repository.Path, change.Id, "review-agent", "Attempt to replace reviewed impact evidence."));
        Assert.Equal(2, blocked.ExitCode);
        Assert.Contains(blocked.Errors, error => error.Contains("no impact findings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImpactAndPlan_RequireHumanReviewAndProduceValidApprovedBoundedWork()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")])).Change);

        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(
            repository.Path,
            change.Id,
            [],
            Depth: 2,
            Limit: 200,
            IncludeProposed: false));

        Assert.Equal(0, analysis.ExitCode);
        Assert.NotEmpty(analysis.Findings);
        Assert.True(analysis.Completeness!.Proposed > 0);
        Assert.False(analysis.Completeness.PlanReady);
        Assert.NotEqual(0, services.Plans.Build(repository.Path, change.Id).ExitCode);

        foreach (var finding in analysis.Findings)
        {
            var disposition = services.Impacts.Disposition(
                repository.Path,
                change.Id,
                finding.Id,
                "accepted",
                "Required by the reviewed change scope.");
            Assert.Equal(0, disposition.ExitCode);
        }

        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        var built = services.Plans.Build(repository.Path, change.Id);
        Assert.Equal(0, built.ExitCode);
        Assert.NotEmpty(built.WorkItems);
        Assert.True(built.Validation!.Valid);
        Assert.All(built.WorkItems, item =>
        {
            Assert.NotEmpty(item.ImpactIds);
            Assert.False(string.IsNullOrWhiteSpace(item.AcceptanceCriteria));
            Assert.False(string.IsNullOrWhiteSpace(item.Validation));
        });

        var approved = services.Plans.Approve(repository.Path, change.Id);
        Assert.Equal(0, approved.ExitCode);
        Assert.Equal("Approved", approved.PlanStatus);
        Assert.Equal("unchanged", services.Plans.Approve(repository.Path, change.Id).Status);
    }

    [Fact]
    public void PlanImportSpec_CreatesFrontendBackendDesignAndVerificationTasksAndDecomposesHighWork()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Add customer checkout",
            "Customers can complete checkout through the web application.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
        {
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        }

        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write(
            "docs/cis/specs/customer-checkout-feature-spec.md",
            "---\ntitle: Customer checkout\ntype: feature-specification\nstatus: Draft\n---\n\n" +
            "# Customer checkout\n\n## Functional requirements\n\n" +
            "| ID | Surface | Requirement | Acceptance criteria |\n| --- | --- | --- | --- |\n" +
            "| CHECKOUT-001 | full-stack | Display a responsive checkout page that calls an authenticated API, persists payment state, emits an order event, and enforces customer permissions. | An authorized customer completes checkout and sees confirmed state without exposing another customer's data. |\n\n" +
            "## UX and accessibility\n\nSupport keyboard navigation, responsive layouts, loading, error, and permission-denied states.\n");

        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path,
            change.Id,
            "docs/cis/specs/customer-checkout-feature-spec.md"));

        Assert.Equal(0, imported.ExitCode);
        Assert.Equal("imported", imported.Status);
        Assert.True(imported.Source!.FrontendChanges);
        Assert.Equal(1, imported.Source.Requirements);
        Assert.Contains(imported.WorkItems, item => item.Category == "wireframe");
        Assert.Contains(imported.WorkItems, item => item.Category == "design");
        Assert.Contains(imported.WorkItems, item => item.Category == "frontend");
        Assert.Contains(imported.WorkItems, item => item.Category == "backend");
        Assert.Contains(imported.WorkItems, item => item.Category == "contract");
        Assert.Contains(imported.WorkItems, item => item.Category == "verification");
        var high = Assert.Single(imported.WorkItems, item => item.Complexity == "high");
        Assert.Equal("decomposed", high.Status);
        Assert.True(imported.WorkItems.Count(item => item.ParentId == high.Id) >= 2);
        Assert.All(imported.WorkItems, item =>
        {
            Assert.Contains(item.Complexity, new[] { "low", "medium", "high" });
            Assert.False(string.IsNullOrWhiteSpace(item.AcceptanceCriteria));
            Assert.False(string.IsNullOrWhiteSpace(item.Validation));
            Assert.False(string.IsNullOrWhiteSpace(item.TaskPath));
            var taskPath = Path.Combine(
                Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
                item.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(taskPath));
            var task = File.ReadAllText(taskPath);
            Assert.Contains("## Required changes", task, StringComparison.Ordinal);
            Assert.Contains("## Constraints and exclusions", task, StringComparison.Ordinal);
            Assert.Contains("## Completion evidence", task, StringComparison.Ordinal);
            Assert.Contains("## Deferrals and residual risk", task, StringComparison.Ordinal);
        });
        Assert.True(imported.Validation!.Valid);
        var planContent = File.ReadAllText(services.Changes.DossierFile(change, "plan.md"));
        Assert.Contains("feature_spec_sha256: \"sha256:", planContent, StringComparison.Ordinal);
        Assert.Contains("## Approval summary", planContent, StringComparison.Ordinal);
        Assert.Contains("## Tasks for approval", planContent, StringComparison.Ordinal);
        Assert.Contains("| Task | Area | Complexity | Depends on | Requirements | Repositories | Status |", planContent, StringComparison.Ordinal);
        Assert.Contains("[WORK-", planContent, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:execution-manifest", planContent, StringComparison.Ordinal);
        Assert.Contains("| ID | Category | Complexity | Parent |", planContent, StringComparison.Ordinal);
        Assert.Contains("cis:execution-manifest -->", planContent, StringComparison.Ordinal);

        var testCasesMarkdownPath = services.Changes.DossierFile(change, "test-cases.md");
        var testCasesCsvPath = services.Changes.DossierFile(change, "test-cases.csv");
        var testCasesMarkdown = File.ReadAllText(testCasesMarkdownPath);
        var testCasesCsv = File.ReadAllText(testCasesCsvPath);
        Assert.Contains("type: manual-test-cases", testCasesMarkdown, StringComparison.Ordinal);
        Assert.Contains("test_case_count: 1", testCasesMarkdown, StringComparison.Ordinal);
        Assert.Contains("### TC-CHECKOUT-001-001:", testCasesMarkdown, StringComparison.Ordinal);
        Assert.Contains("`CHECKOUT-001`", testCasesMarkdown, StringComparison.Ordinal);
        Assert.Contains("An authorized customer completes checkout", testCasesMarkdown, StringComparison.Ordinal);
        Assert.Contains("automation_pending_count: 1", testCasesMarkdown, StringComparison.Ordinal);
        Assert.Contains("- Automation status: Pending", testCasesMarkdown, StringComparison.Ordinal);
        Assert.StartsWith("\"ID\",\"Title\",\"Section\",\"Priority\",\"Type\",\"Preconditions\",\"Steps\",\"Expected Result\",\"References\",\"Frontend Type\",\"Automation Status\",\"Automated Test References\"\r\n", testCasesCsv, StringComparison.Ordinal);
        Assert.Contains("\"CHECKOUT-001\"", testCasesCsv, StringComparison.Ordinal);
        Assert.Contains("\"customer\"", testCasesCsv, StringComparison.Ordinal);
        var csvSha = "sha256:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(testCasesCsv))).ToLowerInvariant();
        Assert.Contains($"csv_sha256: \"{csvSha}\"", testCasesMarkdown, StringComparison.Ordinal);
        var verificationTask = Assert.Single(imported.WorkItems, item => item.Category == "verification");
        Assert.Contains("test-cases.md manual test catalogue with automated-test traceability", verificationTask.RequiredOutputs!);
        Assert.Contains("test-cases.csv test-management import", verificationTask.RequiredOutputs!);

        var repeated = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path,
            change.Id,
            "docs/cis/specs/customer-checkout-feature-spec.md"));
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);

        var backend = Assert.Single(imported.WorkItems, item => item.Category == "backend");
        var blocked = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, backend.Id, "InProgress", "delivery-agent", "Begin approved backend scope."));
        Assert.Equal(2, blocked.ExitCode);
        Assert.Contains(blocked.Errors, error => error.Contains("design gate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanImportSpec_RepairsDriftedManualTestCsvAndNeutralizesFormulaCells()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Export reviewed orders", "Operators can export the reviewed order list safely.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/order-export-feature-spec.md", """
---
title: Order export
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
| EXPORT-001 | frontend | backoffice | =Export the reviewed order list, with the selected filters. | The downloaded file contains only the selected, authorized records. |
""");
        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/order-export-feature-spec.md"));
        Assert.Equal(0, imported.ExitCode);
        var csvPath = services.Changes.DossierFile(change, "test-cases.csv");
        var generated = File.ReadAllText(csvPath);
        Assert.Contains("\"'=Export the reviewed order list, with the selected filters.\"", generated, StringComparison.Ordinal);

        File.AppendAllText(csvPath, "\"UNREVIEWED\"\r\n");
        var invalid = services.Plans.Validate(repository.Path, change.Id);
        Assert.False(invalid.Validation!.Valid);
        Assert.Contains(invalid.Validation.Errors, error => error.Contains("differs from the projection", StringComparison.Ordinal));

        var repaired = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/order-export-feature-spec.md"));
        Assert.Equal(0, repaired.ExitCode);
        Assert.True(repaired.Applied);
        Assert.Equal(generated, File.ReadAllText(csvPath));
        Assert.True(repaired.Validation!.Valid);
    }

    [Fact]
    public void PlanImportSpec_RefreshesManualCasesFromStableAutomatedTestReferences()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Automated order review", "Reviewers can approve an eligible order.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/order-review-feature-spec.md", """
---
title: Order review
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| REVIEW-001 | backend | An eligible order can be approved once. | A repeated approval is idempotent and preserves the approved state. |
""");
        repository.Write("node_modules/example/order.test.ts", "test('TC-REVIEW-001-001 vendor copy', () => {});");

        var pending = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/order-review-feature-spec.md"));
        Assert.Equal(0, pending.ExitCode);
        var markdownPath = services.Changes.DossierFile(change, "test-cases.md");
        Assert.Contains("- Automation status: Pending", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        Assert.Contains(services.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id),
            error => error.Contains("TC-REVIEW-001-001", StringComparison.Ordinal));

        repository.Write("tests/order-review.test.ts", """
describe('order review', () => {
  test('TC-REVIEW-001-001 approves an eligible order idempotently', () => {});
});
""");
        var synchronized = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/order-review-feature-spec.md"));

        Assert.Equal(0, synchronized.ExitCode);
        Assert.True(synchronized.Applied);
        var markdown = File.ReadAllText(markdownPath);
        Assert.Contains("automated_test_case_count: 1", markdown, StringComparison.Ordinal);
        Assert.Contains("automation_pending_count: 0", markdown, StringComparison.Ordinal);
        Assert.Contains("- Automation status: Automated", markdown, StringComparison.Ordinal);
        Assert.Contains("::tests/order-review.test.ts:2`", markdown, StringComparison.Ordinal);
        Assert.Empty(services.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id));
        Assert.Contains("\"Automated\",\"", File.ReadAllText(services.Changes.DossierFile(change, "test-cases.csv")), StringComparison.Ordinal);
        Assert.Contains("::tests/order-review.test.ts:2\"",
            File.ReadAllText(services.Changes.DossierFile(change, "test-cases.csv")), StringComparison.Ordinal);
    }

    [Fact]
    public void PlanImportSpec_DecomposesUiWorkByPublicCustomerAndBackofficeFrontendType()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Add cross-surface order journeys", "Serve visitors, customers, and operators.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/order-journeys-feature-spec.md", """
---
title: Cross-surface order journeys
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
|---|---|---|---|---|
| JOURNEY-001 | frontend | public | Show a public order-information landing page. | A visitor can understand the service without signing in. |
| JOURNEY-002 | frontend | customer | Show the authenticated customer's order history. | A customer sees only their own orders. |
| JOURNEY-003 | frontend | backoffice | Show the internal operator order queue. | An authorized operator can review the queue. |
""");

        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/order-journeys-feature-spec.md"));

        Assert.Equal(0, imported.ExitCode);
        Assert.Equal(new[] { "public", "customer", "backoffice" }, imported.Source!.FrontendTypes);
        foreach (var frontendType in imported.Source.FrontendTypes!)
        {
            var wireframe = Assert.Single(imported.WorkItems, item => item.Category == "wireframe" && item.FrontendType == frontendType);
            var design = Assert.Single(imported.WorkItems, item => item.Category == "design" && item.FrontendType == frontendType);
            var frontend = Assert.Single(imported.WorkItems, item => item.Category == "frontend" && item.FrontendType == frontendType);
            Assert.Contains(wireframe.Id, design.DependsOn);
            Assert.Contains(design.Id, frontend.DependsOn);
            Assert.Single(frontend.RequirementIds);
            var task = File.ReadAllText(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
                frontend.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains($"frontend_type: {frontendType}", task, StringComparison.Ordinal);
        }
        Assert.True(imported.Validation!.Valid);
    }

    [Fact]
    public void PlanImportSpec_EnforcesCacheAndDatabaseIsolationForUnauthenticatedEndpoints()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Expose public catalogue", "Visitors can retrieve the public catalogue.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed public endpoint scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/public-catalogue-feature-spec.md", """
---
title: Public catalogue endpoint
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
|---|---|---|---|---|
| PUBLIC-API-001 | full-stack | public | Expose an unauthenticated public catalogue API endpoint used by the visitor page. | Visitors receive current public catalogue entries without restricted data. |
""");

        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/public-catalogue-feature-spec.md"));

        Assert.Equal(0, imported.ExitCode);
        Assert.True(imported.Source!.PublicEndpoints);
        foreach (var category in new[] { "security", "contract", "backend", "observability", "verification", "assurance" })
        {
            var item = Assert.Single(imported.WorkItems, candidate => candidate.Category == category);
            Assert.Contains("PUBLIC-ENDPOINT-CACHE", item.AcceptanceCriteria, StringComparison.Ordinal);
            Assert.Contains("cache", item.Validation, StringComparison.OrdinalIgnoreCase);
            var taskPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
                item.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.Contains("PUBLIC-ENDPOINT-CACHE", File.ReadAllText(taskPath), StringComparison.Ordinal);
        }

        var backend = Assert.Single(imported.WorkItems, item => item.Category == "backend");
        var backendPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            backend.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains("including on cache miss", File.ReadAllText(backendPath), StringComparison.OrdinalIgnoreCase);
        Assert.True(imported.Validation!.Valid);

        File.WriteAllText(backendPath, File.ReadAllText(backendPath).Replace("PUBLIC-ENDPOINT-CACHE", "REMOVED-POLICY", StringComparison.Ordinal));
        var invalid = services.Plans.Validate(repository.Path, change.Id);
        Assert.False(invalid.Validation!.Valid);
        Assert.Contains(invalid.Validation.Errors, error => error.Contains("PUBLIC-ENDPOINT-CACHE", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanImportSpec_DoesNotTreatAnExplicitlyInapplicablePublicEndpointPolicyAsPublicScope()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Manage access link", "Owners manage one customer-only access-request link.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed customer endpoint scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/customer-access-link-feature-spec.md", """
---
title: Customer access link
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
|---|---|---|---|---|
| LINK-001 | api | customer | The verified owner can retrieve the current access-request link. | The private no-store customer endpoint returns bounded status. |

## API policy

These are customer endpoints, so `PUBLIC-ENDPOINT-CACHE` is not applicable. A future
unauthenticated join route belongs to another feature and is not included here.
""");

        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/customer-access-link-feature-spec.md"));

        Assert.Equal(0, imported.ExitCode);
        Assert.False(imported.Source!.PublicEndpoints);
        Assert.DoesNotContain(imported.WorkItems, item =>
            item.AcceptanceCriteria.Contains("PUBLIC-ENDPOINT-CACHE", StringComparison.Ordinal));
        Assert.True(imported.Validation!.Valid);
    }

    [Fact]
    public void PlanImportSpec_KeepsFrontendStateCoverageOutOfFullStackCapabilities()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Application state coverage", "Public and customer UI states are consistent.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed web-only scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/application-state-coverage.md", """
---
title: Application state coverage
type: feature-specification
targets:
  - todo-web
---

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
|---|---|---|---|---|
| STATE-001 | frontend | public | Public routes shall cover startup, entry, Google callback, validation, progress, unavailable, and not-found states. | No public state renders private data, and callback tokens never appear in telemetry. |
| STATE-002 | frontend | customer | Customer routes shall cover session, list lifecycle, loading, empty, populated, forbidden, stale-write, and recovery states. | Refresh retains only actor-authorized content, and write progress cannot duplicate a mutation. |
| STATE-003 | security | customer | Hidden resources shall use one non-disclosing unavailable presentation. | A visible resource may explain a disallowed action without exposing owner-only controls. |
| STATE-004 | delivery | not-applicable | State coverage shall be traceable through manual cases and automated tests. | Public and customer browser journeys, compact layouts, and error privacy pass. |

## Non-goals and explicit exclusions

No new API, database migration, application service, infrastructure service, event,
search index, or telemetry backend is expected.

## Lifecycle, conversion, and carry-forward

Existing approved state designs are carried forward by provenance. Re-import preserves
approval evidence, rejected revision hashes and history, renderer hashes, test identities,
and automation mappings.
""");

        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/application-state-coverage.md"));

        Assert.True(imported.ExitCode == 0, string.Join(Environment.NewLine, imported.Errors));
        Assert.True(imported.Source!.FrontendChanges);
        Assert.False(imported.Source.PublicEndpoints);
        Assert.DoesNotContain(imported.WorkItems, item => item.Category is
            "data" or "database-migration" or "contract" or "backend" or "integration" or
            "infrastructure" or "observability" or "lifecycle" or "rollout");
        Assert.Contains(imported.WorkItems, item => item.Category == "security");
        foreach (var frontendType in new[] { "public", "customer" })
        {
            Assert.Single(imported.WorkItems, item => item.Category == "wireframe" && item.FrontendType == frontendType);
            Assert.Single(imported.WorkItems, item => item.Category == "design" && item.FrontendType == frontendType);
            Assert.Single(imported.WorkItems, item => item.Category == "frontend" && item.FrontendType == frontendType);
        }
        Assert.DoesNotContain(imported.WorkItems, item =>
            item.AcceptanceCriteria.Contains("PUBLIC-ENDPOINT-CACHE", StringComparison.Ordinal));
        var security = Assert.Single(imported.WorkItems, item => item.Category == "security");
        Assert.Contains("STATE-003", security.RequirementIds);
        Assert.True(imported.Validation!.Valid);
    }

    [Fact]
    public void DesignApproval_CombinesValidWireframesAndRenderedPackWhileReusingTemplates()
    {
        using var repository = TemporaryRepository.Create();
        const string featurePath = "docs/cis/specs/order-list-feature.md";
        var services = CreateServices(new FakeDesignProcessRunner(),
            featureAuthorities: [new StaticFeatureApprovalAuthority(featurePath)]);
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Add order list",
            "Backoffice users can inspect orders in a consistent application shell.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var wireframePath = services.Changes.DossierFile(change, "wireframes.md");
        File.WriteAllText(wireframePath, """
---
title: Friends Todo order list wireframes
type: textual-wireframes
status: Draft
change_id: CIS-0001
approval_status: NotReviewed
authority: human-reviewed
---

# Textual wireframes

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CUS-TODO-BOARD | customer | Friends Todo board | /app/lists/{listId} | Web | Current list actor | List detail route | Find and maintain todos. |

## Screen definitions

### CUS-TODO-BOARD: Friends Todo board

#### Description

The application rail and top bar surround a page header, filters, and the standard orders table.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| OPEN-TODO | Todo card | The todo is visible | Select the card | No mutation | /app/lists/{listId} | CUS-TODO-BOARD | Keep the board visible and show an access error. |

#### States

- Primary, empty, loading, error, denied, responsive, and keyboard-focus states are required.

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States | Notes |
| --- | --- | --- | --- | --- |
| Customer todo lifecycle | CUS-TODO-BOARD | OPEN-TODO | All required states | Covered. |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Not reviewed | Pending | Pending | Pending | Pending |
""");

        var wireframeValidation = services.Designs.ValidateWireframes(repository.Path, change.Id);
        Assert.Equal(0, wireframeValidation.ExitCode);
        Assert.Equal("wireframes-valid", wireframeValidation.Status);
        Assert.Contains(wireframeValidation.Diagnostics,
            item => item.Contains("screens=1", StringComparison.Ordinal) && item.Contains("actions=1", StringComparison.Ordinal));

        var scaffolded = services.Designs.Scaffold(new DesignScaffoldRequest(
            repository.Path, change.Id, "order-list", "shell.standard-app",
            ["component.page-header", "component.filter-bar", "component.table", "component.status-badge"]));

        Assert.Equal(0, scaffolded.ExitCode);
        Assert.True(scaffolded.Applied);
        Assert.Contains(scaffolded.Templates, template => template.Id == "shell.standard-app" && template.EstimatedSavedTokens > 0);
        Assert.Contains(scaffolded.Templates, template => template.Id == "component.table" && template.EstimatedSavedTokens > 0);
        Assert.Contains(scaffolded.Templates, template => template.Id == "component.button");
        var catalog = services.Designs.Templates();
        Assert.Contains(catalog.Templates, template => template.Id == "shell.minimal-app" && template.EstimatedSavedTokens > 0);
        foreach (var component in new[] { "component.button", "component.text-input", "component.select", "component.textarea",
                     "component.checkbox", "component.radio-group", "component.toggle", "component.tabs", "component.pagination",
                     "component.dropdown-menu", "component.dialog", "component.alert", "component.accordion" })
            Assert.Contains(catalog.Templates, template => template.Id == component && template.EstimatedSavedTokens > 0);
        var renderer = File.ReadAllText(Path.Combine(repository.Path,
            scaffolded.RendererPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("function applicationShell", renderer, StringComparison.Ordinal);
        Assert.Contains("function standardTable", renderer, StringComparison.Ordinal);
        Assert.Contains("function standardButton", renderer, StringComparison.Ordinal);
        Assert.Contains("function textInput", renderer, StringComparison.Ordinal);
        Assert.Contains("function selectInput", renderer, StringComparison.Ordinal);
        Assert.Contains("function checkboxControl", renderer, StringComparison.Ordinal);
        Assert.Contains("function radioGroup", renderer, StringComparison.Ordinal);
        Assert.Contains("function modalDialog", renderer, StringComparison.Ordinal);
        Assert.Contains("applicationShell(screen, content)", renderer, StringComparison.Ordinal);
        Assert.Contains("shell.minimal-app@", renderer, StringComparison.Ordinal);
        Assert.Contains("guidelineSha256", renderer, StringComparison.Ordinal);
        var preparedWireframeSha = wireframeValidation.Diagnostics
            .SelectMany(item => item.Split(';'))
            .Select(item => item.Split('=', 2))
            .Where(item => item.Length == 2 && item[0].Equals("wireframeSha256", StringComparison.Ordinal))
            .Select(item => item[1])
            .Single();
        Assert.Contains($"wireframeSha256: \"{preparedWireframeSha}\"", renderer, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Friends Todo board\"", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("â", renderer, StringComparison.Ordinal);
        renderer = renderer.Replace("function radioGroup", "function unusedRadioGroup", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(repository.Path,
            scaffolded.RendererPath.Replace('/', Path.DirectorySeparatorChar)), renderer);
        var validation = services.Designs.Validate(repository.Path, change.Id);
        Assert.True(validation.ExitCode == 0, string.Join(Environment.NewLine, validation.Diagnostics));
        var protectedReplacement = services.Designs.Scaffold(new DesignScaffoldRequest(
            repository.Path, change.Id, "order-list", "shell.standard-app", ["component.table"]));
        Assert.Equal(2, protectedReplacement.ExitCode);
        Assert.Equal(renderer, File.ReadAllText(Path.Combine(repository.Path,
            scaffolded.RendererPath.Replace('/', Path.DirectorySeparatorChar))));

        var rendererPath = Path.Combine(repository.Path, scaffolded.RendererPath.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(rendererPath, Environment.NewLine + "// Reviewed feature customization." + Environment.NewLine);
        var customizedRendererSha = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rendererPath))).ToLowerInvariant();
        var rendered = services.Designs.Render(repository.Path, change.Id);
        Assert.Equal(0, rendered.ExitCode);
        Assert.Equal("PausedForReview", rendered.GateStatus);
        Assert.Single(rendered.Artifacts);
        var renderedDesign = File.ReadAllText(services.Changes.DossierFile(change, "design.md"));
        Assert.Contains($"`{customizedRendererSha}`", renderedDesign, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending render", renderedDesign, StringComparison.Ordinal);
        var rejectedPath = Path.Combine(repository.Path, rendered.Artifacts[0].Path.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(0, services.Designs.Reject(new DesignReviewRequest(
            repository.Path, change.Id, "product-owner", "Increase information density.")).ExitCode);
        Assert.False(File.Exists(rejectedPath));
        Assert.Contains("sha256:", File.ReadAllText(services.Changes.DossierFile(change, "design.md")), StringComparison.Ordinal);

        Assert.Equal(0, services.Designs.Render(repository.Path, change.Id).ExitCode);
        var approved = services.Designs.Approve(new DesignReviewRequest(
            repository.Path, change.Id, "product-owner", "The revised shell and standard table are approved."));
        Assert.Equal(0, approved.ExitCode);
        Assert.Equal("Approved", approved.GateStatus);
        var approvedWireframes = File.ReadAllText(wireframePath);
        Assert.Contains("approval_status: Approved", approvedWireframes, StringComparison.Ordinal);
        Assert.Contains($"`{preparedWireframeSha}`", approvedWireframes, StringComparison.Ordinal);
        var approvedDesign = File.ReadAllText(services.Changes.DossierFile(change, "design.md"));
        Assert.Contains("| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |", approvedDesign, StringComparison.Ordinal);
        Assert.Contains($"`{preparedWireframeSha}`", approvedDesign, StringComparison.Ordinal);
        var approvedValidation = services.Designs.Validate(repository.Path, change.Id);
        Assert.Equal(0, approvedValidation.ExitCode);
        Assert.Equal("valid", approvedValidation.Status);
        Assert.DoesNotContain(approvedValidation.Diagnostics,
            item => item.Contains("wireframe provenance", StringComparison.OrdinalIgnoreCase));

        repository.Write(featurePath, "# Approved order-list feature\n\nThe list remains visually unchanged.\n");
        var featureSha = "sha256:" + Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(repository.Path, featurePath.Replace('/', Path.DirectorySeparatorChar))))).ToLowerInvariant();
        File.WriteAllText(services.Changes.DossierFile(change, "plan.md"), $"""
---
title: Order list delivery plan
type: delivery-plan
status: Approved
change_id: {change.Id}
feature_spec_path: "{featurePath}"
feature_spec_sha256: "{featureSha}"
---

# Delivery plan
""");
        var revisedWireframes = File.ReadAllText(wireframePath)
            .Replace("Find and maintain todos.", "Find and maintain todos. Return focus to the entry field after submission.", StringComparison.Ordinal);
        File.WriteAllText(wireframePath, revisedWireframes);
        var revisedWireframeSha = services.Designs.ValidateWireframes(repository.Path, change.Id).Diagnostics
            .SelectMany(item => item.Split(';'))
            .Select(item => item.Split('=', 2))
            .Where(item => item.Length == 2 && item[0].Equals("wireframeSha256", StringComparison.Ordinal))
            .Select(item => item[1])
            .Single();
        File.WriteAllText(rendererPath, File.ReadAllText(rendererPath)
            .Replace(preparedWireframeSha, revisedWireframeSha, StringComparison.Ordinal));
        var designPath = services.Changes.DossierFile(change, "design.md");
        File.WriteAllText(designPath, File.ReadAllText(designPath)
            .Replace(preparedWireframeSha, revisedWireframeSha, StringComparison.Ordinal));
        Assert.Equal(0, services.Designs.Render(repository.Path, change.Id).ExitCode);

        var reconciled = services.Designs.Reconcile(repository.Path, change.Id);
        Assert.Equal(0, reconciled.ExitCode);
        Assert.Equal("authority-carried-forward", reconciled.Status);
        Assert.Equal("Approved", reconciled.GateStatus);
        Assert.Contains("| Authority carried forward | Product owner |", File.ReadAllText(wireframePath), StringComparison.Ordinal);
        Assert.Contains("| Authority carried forward | Product owner |", File.ReadAllText(designPath), StringComparison.Ordinal);
        Assert.Equal(0, services.Designs.Validate(repository.Path, change.Id).ExitCode);

        Assert.Equal(0, services.Designs.Render(repository.Path, change.Id).ExitCode);
        var changedPng = services.Designs.Status(repository.Path, change.Id).Artifacts.Single().Path;
        using (var stream = File.OpenWrite(Path.Combine(repository.Path, changedPng.Replace('/', Path.DirectorySeparatorChar))))
        {
            stream.Seek(0, SeekOrigin.End);
            stream.WriteByte(0);
        }
        var changedPixels = services.Designs.Reconcile(repository.Path, change.Id);
        Assert.Equal(2, changedPixels.ExitCode);
        Assert.Contains(changedPixels.Diagnostics,
            item => item.Contains("Rendered pixels changed", StringComparison.Ordinal));
    }

    [Fact]
    public void DesignReuse_CarriesApprovedArtifacts_RendersOnlyGaps_AndDetectsSourceDrift()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices(new FakeDesignProcessRunner());
        var source = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Source design", "The approved source state is available.",
            [new ChangeRoot("todo-web", "component")])).Change);
        WriteDesignWireframes(services.Changes.DossierFile(source, "wireframes.md"), source.Id,
            ("CUS-SOURCE", "customer"));
        Assert.Equal(0, services.Designs.Scaffold(new DesignScaffoldRequest(
            repository.Path, source.Id, "source-design", "shell.minimal-app", ["component.alert"])).ExitCode);
        Assert.Equal(0, services.Designs.Render(repository.Path, source.Id).ExitCode);
        Assert.Equal(0, services.Designs.Approve(new DesignReviewRequest(
            repository.Path, source.Id, "product-owner", "The source state is approved.")).ExitCode);

        var target = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Coverage design", "Existing visuals are reused and only the gap is rendered.",
            [new ChangeRoot("todo-web", "component")])).Change);
        WriteDesignWireframes(services.Changes.DossierFile(target, "wireframes.md"), target.Id,
            ("CUS-REUSED", "customer"), ("PUB-NEW", "public"));
        var reuseRequest = new DesignReuseRequest(repository.Path, target.Id, source.Id,
            "CUS-SOURCE", "CUS-REUSED", "The target state is visually identical to the approved source state.");
        Assert.Equal(0, services.Designs.Reuse(reuseRequest).ExitCode);
        Assert.Equal(0, services.Designs.Reuse(reuseRequest).ExitCode);
        var targetDesignPath = services.Changes.DossierFile(target, "design.md");
        Assert.Single(File.ReadAllLines(targetDesignPath), line => line.Contains("`cus-reused`", StringComparison.Ordinal));

        var scaffolded = services.Designs.Scaffold(new DesignScaffoldRequest(
            repository.Path, target.Id, "coverage-design", "shell.minimal-app", ["component.alert"]));
        Assert.Equal(0, scaffolded.ExitCode);
        var renderer = File.ReadAllText(Path.Combine(repository.Path,
            scaffolded.RendererPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.DoesNotContain("\"id\": \"cus-reused\"", renderer, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"pub-new\"", renderer, StringComparison.Ordinal);

        var rendered = services.Designs.Render(repository.Path, target.Id);
        Assert.Equal(0, rendered.ExitCode);
        Assert.Equal(2, rendered.Artifacts.Count);
        Assert.Contains(rendered.Artifacts, item => item.ScreenId == "cus-reused" && item.Path.Contains(source.Id, StringComparison.Ordinal));
        Assert.Contains(rendered.Artifacts, item => item.ScreenId == "pub-new" && item.Path.Contains(target.Id, StringComparison.Ordinal));
        Assert.Equal(0, services.Designs.Approve(new DesignReviewRequest(
            repository.Path, target.Id, "product-owner", "The reused state and new state are approved together.")).ExitCode);

        var approvedTargetDesign = File.ReadAllText(targetDesignPath);
        File.WriteAllText(targetDesignPath, approvedTargetDesign.Replace(
            "\n## PNG manifest", "\n| malformed |\n\n## PNG manifest", StringComparison.Ordinal));
        var malformed = services.Designs.Validate(repository.Path, target.Id);
        Assert.Equal(2, malformed.ExitCode);
        Assert.Contains(malformed.Diagnostics, item => item.Contains("malformed row", StringComparison.Ordinal));
        File.WriteAllText(targetDesignPath, approvedTargetDesign);

        var sourcePng = services.Designs.Status(repository.Path, source.Id).Artifacts.Single().Path;
        File.AppendAllText(Path.Combine(repository.Path, sourcePng.Replace('/', Path.DirectorySeparatorChar)), "drift");
        var drifted = services.Designs.Validate(repository.Path, target.Id);
        Assert.Equal(2, drifted.ExitCode);
        Assert.Contains(drifted.Diagnostics, item => item.Contains("Reused PNG bytes", StringComparison.Ordinal)
            || item.Contains("source PNG manifest is stale", StringComparison.Ordinal));
    }

    [Fact]
    public void NodeDesignProcessRunner_FindsSharpInRegisteredWorkspaceParticipant()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(".cis/workspace.yml", """
            schema_version: 1
            repositories:
            - id: docs
              path: .
              role: authority
            - id: web
              path: targets/web
              role: participant
            """);
        repository.Write("targets/web/node_modules/sharp/package.json", "{\"name\":\"sharp\"}");
        repository.Write("docs/changes/CIS-0001/assets/render.mjs", "// renderer");

        var modules = NodeDesignProcessRunner.FindSharpNodeModules(repository.Path,
            Path.Combine(repository.Path, "docs", "changes", "CIS-0001", "assets", "render.mjs"));

        Assert.Equal(Path.GetFullPath(Path.Combine(repository.Path, "targets", "web", "node_modules")), modules);
    }

    [Fact]
    public void TaskCompletion_SnapshotsSanitizedToolUsageAndPossibleSavingsIntoEvidence()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Document order policy", "The governed order policy is current.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/order-policy-feature-spec.md", """
---
title: Order policy
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| POLICY-001 | documentation | Document the order policy and its backend rule. | The canonical policy and implementation rule agree. |
""");
        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/order-policy-feature-spec.md"));
        var task = Assert.Single(imported.WorkItems, item => item.Category == "documentation");
        var finalSweep = Assert.Single(imported.WorkItems, item => item.TaskTypeKey == "core.delivery.final-sweep");
        Assert.All(imported.WorkItems.Where(item =>
                item.Id != finalSweep.Id && item.TaskTypeKey != "core.coordination.scope-guard"),
            item => Assert.Contains(item.Id, finalSweep.DependsOn));

        var coordination = Assert.Single(imported.WorkItems, item => item.TaskTypeKey == "core.coordination.scope-guard");
        var coordinationPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            coordination.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(coordinationPath, File.ReadAllText(coordinationPath)
            .Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |",
                "| Coordination review | `cis plan validate` | Passed | Scope is reconciled. |", StringComparison.Ordinal));
        var prematureAcceptance = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, coordination.Id, "Complete", "product-owner", "Accept completed delivery."));
        Assert.Equal(2, prematureAcceptance.ExitCode);
        Assert.Contains(prematureAcceptance.Errors, error => error.Contains("Final Delivery Sweep", StringComparison.Ordinal));

        var finalSweepPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            finalSweep.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(finalSweepPath, File.ReadAllText(finalSweepPath)
            .Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |",
                "| Completion audit | `cis plan validate` | Passed | Evidence index recorded. |", StringComparison.Ordinal));
        Assert.Equal(0, services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, finalSweep.Id, "InProgress", "delivery-agent", "Begin completion audit.")).ExitCode);
        var prematureSweep = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, finalSweep.Id, "Complete", "delivery-agent", "Audit is ready."));
        Assert.Equal(2, prematureSweep.ExitCode);
        Assert.Contains(prematureSweep.Errors, error => error.Contains("terminal disposition", StringComparison.OrdinalIgnoreCase));

        services.Usage.Record(new CisToolUsageCapture(
            ["context", "pack", "--repo", repository.Path], repository.Path,
            DateTimeOffset.Parse("2026-08-09T09:00:00Z"), DateTimeOffset.Parse("2026-08-09T09:00:01Z"),
            1000, 0, 80, 0, [new CisTokenSavingsCandidate(100, 20, "focused context pack", "high")]));
        Assert.Equal(0, services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, task.Id, "InProgress", "delivery-agent", "Begin bounded work.")).ExitCode);
        var taskPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            task.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(taskPath)
            .Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |",
                "| Documentation drift | `cis docs validate --strict` | Passed | Canonical policy agrees. |", StringComparison.Ordinal);
        File.WriteAllText(taskPath, content);

        var completed = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, task.Id, "Complete", "delivery-agent", "Acceptance and validation evidence pass."));

        Assert.Equal(0, completed.ExitCode);
        Assert.Contains("possibleTokenSavings=80", File.ReadAllText(taskPath), StringComparison.Ordinal);
        var verification = File.ReadAllText(services.Changes.DossierFile(change, "verification.md"));
        Assert.Contains("CIS tool-usage snapshot", verification, StringComparison.Ordinal);
        Assert.Contains("ledgerDigest=sha256:", verification, StringComparison.Ordinal);
        Assert.DoesNotContain(repository.Path, verification, StringComparison.OrdinalIgnoreCase);

        foreach (var remaining in imported.WorkItems.Where(item =>
                     item.Id != coordination.Id
                     && item.Id != finalSweep.Id
                     && item.Id != task.Id))
        {
            var remainingPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
                remaining.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(remainingPath, File.ReadAllText(remainingPath)
                .Replace("- [ ]", "- [x]", StringComparison.Ordinal)
                .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |",
                    "| Completion check | `cis plan validate` | Passed | Evidence recorded. |", StringComparison.Ordinal));
            Assert.Equal(0, services.Plans.TransitionTask(new PlanTaskTransitionRequest(
                repository.Path, change.Id, remaining.Id, "InProgress", "delivery-agent", "Begin bounded work.")).ExitCode);
            Assert.Equal(0, services.Plans.TransitionTask(new PlanTaskTransitionRequest(
                repository.Path, change.Id, remaining.Id, "Complete", "delivery-agent", "Acceptance evidence passes.")).ExitCode);
        }

        var swept = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, finalSweep.Id, "Complete", "delivery-agent", "Final delivery is reproducible."));
        Assert.Equal(0, swept.ExitCode);

        var accepted = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, coordination.Id, "Complete", "product-owner", "Accept completed delivery."));
        Assert.Equal(0, accepted.ExitCode);
        Assert.True(accepted.Validation!.Valid);
    }

    [Fact]
    public void PlanImportSpec_DecomposesParrShapedNarrativeSpecificationIntoExecutableIssuePack()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Lead research",
            "Internal users can manage source-backed lead research safely.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
        {
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed feature scope.");
        }
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write(
            "docs/cis/specs/lead-research-feature-spec.md",
            "---\ntitle: Lead research feature specification\ntype: specification\nstatus: Draft\n" +
            "targets:\n  - documentation\n  - backend\n  - frontend\nstack:\n  - csharp\n  - nextjs\n" +
            "references:\n  - docs/cis/references/api-dictionary.md\n  - docs/cis/references/permissions-dictionary.md\n---\n\n" +
            "# Lead research feature specification\n\n## 1. Feature summary\n\n" +
            "Internal users manage source-backed Research Activities from a dedicated Research tab.\n\n" +
            "## 2. Goals\n\n" +
            "1. Add a Research tab with a task-like list and full-page detail view.\n" +
            "2. Persist one audited generic research model with lifecycle states through an additive schema migration.\n" +
            "3. Expose secured API endpoints and enforce internal permissions.\n" +
            "4. Integrate with WorkManagement to create review requests through linked tasks.\n" +
            "5. Project research into internal search and exclude customer-facing retrieval.\n" +
            "6. Preserve inherited research after lead conversion without copying records.\n\n" +
            "## 3. Non-goals\n\n1. Do not add upload or attachment storage.\n2. Do not create a parallel review subsystem.\n\n" +
            "## 4. Data model and statuses\n\nPersist Draft, Complete, PendingReview, Approved, and Archived state with provenance and audit metadata.\n\n" +
            "## 5. Research tab UI\n\nProvide list, empty, loading, error, denied, responsive, create, edit, review, and archive states.\n\n" +
            "## 6. WorkManagement handoff\n\nRequest review through a linked task and preserve links to the lead and research activity.\n\n" +
            "## 7. Search projection and retrieval\n\nIndex an internal-only projection and prove customer-facing search and AI cannot retrieve it.\n\n" +
            "## 8. Carry-forward after conversion\n\nExpose inherited research from customer context without copying canonical rows.\n\n" +
            "## 9. Permissions and API\n\nImplement CRUD, archive, review, permission, validation, and unauthorized cases.\n\n" +
            "## 10. Testing requirements\n\nInclude domain, migration, API, permission, search, frontend component, browser, drift, and coverage checks.\n");

        var result = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/lead-research-feature-spec.md"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "documentation", "backend", "frontend" }, result.Source!.Targets);
        Assert.Equal(new[] { "csharp", "nextjs" }, result.Source.Stack);
        Assert.Equal(6, result.Source.Requirements);
        foreach (var category in new[]
                 {
                     "documentation", "security", "data", "database-migration", "contract", "backend",
                     "wireframe", "design", "frontend", "integration", "lifecycle", "verification",
                     "assurance", "delivery",
                 })
        {
            Assert.True(result.WorkItems.Any(item => item.Category == category),
                $"Missing category '{category}'. Actual: {string.Join(", ", result.WorkItems.Select(item => item.Category).Distinct())}");
        }
        Assert.DoesNotContain(result.WorkItems, item => item.Category == "search");
        var wireframe = Assert.Single(result.WorkItems, item => item.Category == "wireframe");
        var design = Assert.Single(result.WorkItems, item => item.Category == "design");
        var frontend = Assert.Single(result.WorkItems, item => item.Category == "frontend");
        Assert.Contains(wireframe.Id, design.DependsOn);
        Assert.Contains(design.Id, frontend.DependsOn);
        Assert.All(result.WorkItems.Where(item => item.Category is not ("coordination" or "wireframe" or "design")),
            item => Assert.Contains(design.Id, item.DependsOn));
        var finalSweep = Assert.Single(result.WorkItems, item => item.TaskTypeKey == "core.delivery.final-sweep");
        Assert.All(result.WorkItems.Where(item =>
                item.Id != finalSweep.Id && item.TaskTypeKey != "core.coordination.scope-guard"),
            item => Assert.Contains(item.Id, finalSweep.DependsOn));
        var designTask = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            design.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("Human approval gate", designTask, StringComparison.Ordinal);
        Assert.Contains("Do not add upload or attachment storage", designTask, StringComparison.Ordinal);
        Assert.Contains("Governed reference", designTask, StringComparison.Ordinal);
        Assert.Contains("| Check | Command or artifact | Result | Notes |", designTask, StringComparison.Ordinal);
        var catalog = File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "catalog.yml"));
        Assert.All(result.WorkItems, item => Assert.Contains(
            $"changes/{change.Id}/{item.TaskPath}", catalog, StringComparison.Ordinal));
        Assert.True(result.Validation!.Valid);
    }

    [Fact]
    public void PlanImportSpec_RejectsPlaceholderRequirements()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Incomplete feature",
            "The feature is specified before delivery.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
        {
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        }

        repository.Write(
            "docs/cis/specs/incomplete-feature-spec.md",
            "---\ntype: feature-specification\n---\n\n## Functional requirements\n\n" +
            "| ID | Requirement | Acceptance criteria |\n| --- | --- | --- |\n| FEAT-TODO-001 | TODO | TODO |\n");

        var result = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path,
            change.Id,
            "docs/cis/specs/incomplete-feature-spec.md"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("placeholder", StringComparison.OrdinalIgnoreCase));

        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write(
            "docs/cis/specs/incomplete-feature-spec.md",
            "---\ntitle: Friends Todo storage\ntype: feature-specification\n---\n\n## Functional requirements\n\n" +
            "| ID | Requirement | Acceptance criteria |\n| --- | --- | --- |\n" +
            "| FEAT-FR-001 | `todo-api` shall persist Friends Todo lists in SQLite. | Restart preserves Friends Todo records without PostgreSQL. |\n");

        var todoProduct = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path,
            change.Id,
            "docs/cis/specs/incomplete-feature-spec.md"));

        Assert.True(todoProduct.ExitCode == 0, string.Join(Environment.NewLine, todoProduct.Errors));
        Assert.DoesNotContain(todoProduct.Errors, error =>
            error.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanImportSpec_RespectsExplicitNoUiAndDoesNotCreateNegativeBackfillOrRolloutWork()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Local development foundation", "Developers can start the local stack.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/local-foundation.md",
            "---\ntitle: Local foundation\ntype: feature-specification\n---\n\n" +
            "## Functional requirements\n\n| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n" +
            "| --- | --- | --- | --- | --- |\n" +
            "| FEAT-001 | delivery | not-applicable | Compose shall start the API with a suitable health check and SQLite database migrations. | Repeated startup is idempotent. |\n" +
            "| FEAT-002 | security | not-applicable | Runtime configuration shall keep secrets out of browser artifacts. | Secret scanning passes. |\n" +
            "| FEAT-003 | delivery | not-applicable | The web shall expose a health endpoint without requiring authentication. | The route returns static health and never accesses a database. |\n\n" +
            "## Non-goals and explicit exclusions\n\nNo existing-data backfill or production rollout is included.\n\n" +
            "## UX, screens, and accessibility\n\nNo product screen, wireframe, or visual-design approval is required.\n");

        var result = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/local-foundation.md"));

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.False(result.Source!.FrontendChanges);
        Assert.True(result.Source.PublicEndpoints);
        Assert.DoesNotContain(result.WorkItems, item => item.Category is "wireframe" or "design" or "frontend");
        Assert.DoesNotContain(result.WorkItems, item => item.Category is "data-backfill" or "rollout");
        Assert.Contains(result.WorkItems, item => item.Category == "database-migration");
        var verification = Assert.Single(result.WorkItems, item => item.Category == "verification");
        var assurance = Assert.Single(result.WorkItems, item => item.Category == "assurance");
        Assert.Contains("PUBLIC-ENDPOINT-CACHE", verification.AcceptanceCriteria, StringComparison.Ordinal);
        Assert.DoesNotContain("browser journeys", verification.Validation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("independent", verification.Validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("independent", assurance.Validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(verification.Id, assurance.DependsOn);
        var verificationTask = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            verification.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.DoesNotContain("independent assurance results", verificationTask, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanImportSpec_DistinguishesSchemaMigrationAndProductLifecycleFromRestoreEvidence()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "List lifecycle", "Owners manage the lifecycle of a todo list.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/list-lifecycle.md", """
            ---
            title: List lifecycle
            type: feature-specification
            targets:
              - todo-api
              - todo-infra
              - todo-web
            ---

            ## Functional requirements

            | ID | Surface | Frontend type | Requirement | Acceptance criteria |
            |---|---|---|---|---|
            | LIST-001 | data | not-applicable | A list shall retain lifecycle timestamps and one immutable owner. | Constraints preserve the initial state. |
            | LIST-005 | backend | customer | Only the owner shall permanently delete the list. | Permanent deletion removes the aggregate atomically. |
            | LIST-007 | security | not-applicable | List lifecycle authorization shall fail closed. | Non-owners receive no list data. |
            | LIST-009 | frontend | customer | Deletion shall use an accessible confirmation dialog. | Cancel restores focus without sending a request. |
            | LIST-010 | delivery | not-applicable | Migration, recovery, diagnostics, and runtime topology shall remain repeatable. | One ordered immutable SQLite migration supplies the list table and constraints; backup and restore preserve lists; Compose remains API/web only. |

            ## Lifecycle, conversion, and carry-forward

            A list moves directly from existing to permanently deleted. Ownership remains immutable.
            Archive, soft delete, restoration, and ownership transfer are not supported.
            """);

        var result = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/list-lifecycle.md"));

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Errors));
        var migration = Assert.Single(result.WorkItems, item => item.Category == "database-migration");
        Assert.Equal(["LIST-010"], migration.RequirementIds);
        var lifecycle = Assert.Single(result.WorkItems, item => item.Category == "lifecycle");
        Assert.Equal(["LIST-001", "LIST-005"], lifecycle.RequirementIds);
        Assert.DoesNotContain("LIST-007", lifecycle.RequirementIds);
        Assert.DoesNotContain("LIST-009", lifecycle.RequirementIds);
        Assert.DoesNotContain("LIST-010", lifecycle.RequirementIds);
        Assert.Contains(result.WorkItems, item => item.Category == "infrastructure");
        Assert.Contains(result.WorkItems, item => item.Category == "observability");
    }

    [Fact]
    public void PlanImportSpec_UsesPositiveTaskSignalsAndClassificationBasedRepositoryRouting()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Authentication modes", "Users establish a mode-qualified session.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));

        repository.Write(".cis/workspace.yml", """
            schema_version: 1
            repositories:
            - id: api
              path: targets/api
              documentation_root: docs
              role: participant
            - id: web
              path: targets/web
              documentation_root: docs
              role: participant
            - id: infra
              path: targets/infra
              documentation_root: docs
              role: participant
            """);
        repository.Write("targets/api/.cis/starter-manifest.yml", """
            schema_version: 1
            components:
            - id: api
              roles:
              - backend-api-producer
              - database
            """);
        repository.Write("targets/web/.cis/starter-manifest.yml", """
            schema_version: 1
            components:
            - id: web
              roles:
              - frontend-consumer
            """);
        repository.Write("targets/infra/.cis/starter-manifest.yml", """
            schema_version: 1
            components:
            - id: infra
              roles:
              - infrastructure
            """);
        repository.Write("docs/cis/specs/authentication-feature.md", """
            ---
            title: Authentication modes
            type: feature-specification
            targets:
              - api
              - web
              - infra
            ---

            ## Functional requirements

            | ID | Surface | Frontend type | Requirement | Acceptance criteria |
            |---|---|---|---|---|
            | AUTH-001 | backend | customer | A valid user session shall resolve a stable actor ID. | The backend trusts only verified session state. |
            | AUTH-002 | frontend | customer | The customer session page shall show the current mode. | The page is accessible and responsive. |
            | AUTH-003 | backend | public | `GET /auth/mode` shall expose cached public application configuration. | The endpoint never accesses a repository or database. |
            | AUTH-004 | backend | not-applicable | Anonymous mode shall persist an opaque session record. | A hash lookup resolves the actor after restart. |
            | AUTH-005 | delivery | not-applicable | Runtime configuration shall isolate the managed API key and anonymous secrets. | Deployment configuration fails closed for mixed values. |
            | AUTH-006 | delivery | not-applicable | Authentication diagnostics shall emit redacted logs and metrics. | Telemetry excludes identity and credential values. |
            | AUTH-007 | api | public | The backend shall expose the SuperTokens identity protocol routes. | Protocol responses are `no-store`. |
            | AUTH-008 | delivery | not-applicable | Existing runtime topology shall remain repeatable. | No new service or migration is required in this feature; SQLite restart behavior remains unchanged. |

            ## UX, screens, and accessibility

            Public sign-in page presents provider entry and callback states.
            Customer session page presents the current session mode.

            ## Domain model, data, audit, and migrations

            Reuse the existing actor and session tables. No schema migration is required.

            ## Lifecycle, conversion, and carry-forward

            Not applicable. Account conversion and ownership transfer are excluded.
            """);

        var result = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/authentication-feature.md"));

        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.DoesNotContain(result.WorkItems, item => item.Category is "database-migration" or "lifecycle");
        var wireframe = Assert.Single(result.WorkItems, item => item.Category == "wireframe");
        Assert.Equal(["AUTH-002"], wireframe.RequirementIds);
        Assert.Equal(["web"], wireframe.Targets);
        var wireframeDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            wireframe.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("`AUTH-002`", wireframeDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("Public sign-in page", wireframeDocument, StringComparison.Ordinal);
        Assert.Equal(["web"], Assert.Single(result.WorkItems, item => item.Category == "frontend").Targets);
        var backend = Assert.Single(result.WorkItems, item => item.Category == "backend");
        Assert.Equal(["api"], backend.Targets);
        var backendDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            backend.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("`AUTH-001`: A valid user session shall resolve a stable actor ID.", backendDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("`AUTH-001`: The backend trusts only verified session state.", backendDocument, StringComparison.Ordinal);
        var data = Assert.Single(result.WorkItems, item => item.Category == "data");
        Assert.Equal(["api"], data.Targets);
        var dataDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            data.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.DoesNotContain("Forward migration", dataDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("down-script", dataDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`AUTH-004`: A hash lookup resolves the actor after restart.", dataDocument, StringComparison.Ordinal);
        Assert.Contains("`AUTH-004`: Anonymous mode shall persist an opaque session record.", dataDocument, StringComparison.Ordinal);
        Assert.Equal(["infra"], Assert.Single(result.WorkItems, item => item.Category == "infrastructure").Targets);
        var contract = Assert.Single(result.WorkItems, item => item.Category == "contract");
        Assert.Equal(["api", "web"], contract.Targets);
        Assert.DoesNotContain("AUTH-005", contract.RequirementIds);
        var contractDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            contract.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(contract.AcceptanceCriteria, contractDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("`AUTH-002`: The page is accessible and responsive.", contractDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("`AUTH-004`: A hash lookup resolves the actor after restart.", contractDocument, StringComparison.Ordinal);
        var security = Assert.Single(result.WorkItems, item => item.Category == "security");
        var securityDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            security.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(security.AcceptanceCriteria, securityDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("`AUTH-002`: The page is accessible and responsive.", securityDocument, StringComparison.Ordinal);
        Assert.Contains("`AUTH-002`: The customer session page", securityDocument, StringComparison.Ordinal);

        var checkedSecurityDocument = Regex.Replace(
            securityDocument,
            @"(?m)^- \[ \] (?<criterion>.+)$",
            "- [x] ${criterion}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        File.WriteAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            security.TaskPath!.Replace('/', Path.DirectorySeparatorChar)), checkedSecurityDocument);
        var checkedCriterion = Regex.Match(checkedSecurityDocument, @"(?m)^- \[x\] (?<criterion>.+)$").Groups["criterion"].Value;

        var approved = services.Plans.Approve(repository.Path, change.Id);
        Assert.Equal(0, approved.ExitCode);
        var repeatedApproved = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/authentication-feature.md"));
        Assert.Equal(0, repeatedApproved.ExitCode);
        Assert.Equal("Approved", repeatedApproved.PlanStatus);
        Assert.Equal(security.Status, Assert.Single(repeatedApproved.WorkItems, item => item.Id == security.Id).Status);
        var repeatedSecurityDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            security.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains($"- [x] {checkedCriterion}", repeatedSecurityDocument, StringComparison.Ordinal);

        repository.Write("docs/cis/specs/authentication-feature.md",
            File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "specs", "authentication-feature.md"))
                + "\nMaterial scope change.\n");
        var staleValidation = services.Plans.Validate(repository.Path, change.Id);
        Assert.Equal(5, staleValidation.ExitCode);
        Assert.Contains(staleValidation.Validation!.Errors, error =>
            error.Contains("changed after plan generation", StringComparison.OrdinalIgnoreCase));
        var blockedTransition = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, security.Id, "InProgress", "Reviewer", "Begin revised work."));
        Assert.Equal(2, blockedTransition.ExitCode);
        Assert.Contains(blockedTransition.Errors, error =>
            error.Contains("changed after plan generation", StringComparison.OrdinalIgnoreCase));

        var changedApproved = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/authentication-feature.md"));
        Assert.Equal(0, changedApproved.ExitCode);
        Assert.Equal("Draft", changedApproved.PlanStatus);
        Assert.NotEqual(repeatedApproved.Source!.Sha256, changedApproved.Source!.Sha256);
        var revisedSecurityDocument = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            security.TaskPath!.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains($"- [x] {checkedCriterion}", revisedSecurityDocument, StringComparison.Ordinal);
        Assert.Contains("plan-approval-invalidated", File.ReadAllText(
            services.Changes.DossierFile(change, "events.jsonl")), StringComparison.Ordinal);
        Assert.Equal(0, services.Plans.Validate(repository.Path, change.Id).ExitCode);
    }

    [Fact]
    public void PlanReimport_RetiresObsoleteTasksAndPreservesHumanEvidence()
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
        const string featurePath = "docs/cis/specs/reimport-feature.md";
        repository.Write(featurePath,
            "---\ntitle: Operator experience\ntype: feature-specification\n---\n\n" +
            "## Functional requirements\n\n| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n" +
            "|---|---|---|---|---|\n" +
            "| OPS-001 | full-stack | backoffice | Add an operator dashboard backed by an API. | Operators can review current orders. |\n");
        var initial = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, featurePath));
        Assert.Equal(0, initial.ExitCode);
        var obsolete = Assert.Single(initial.WorkItems, item => item.Category == "design");
        var dossier = Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!;
        var activePath = Path.Combine(dossier, obsolete.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        File.AppendAllText(activePath, Environment.NewLine + "| Human design review | `review-record` | Passed | Preserve this evidence. |" + Environment.NewLine);

        repository.Write(featurePath,
            "---\ntitle: Operator foundation\ntype: feature-specification\n---\n\n" +
            "## Functional requirements\n\n| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n" +
            "|---|---|---|---|---|\n" +
            "| OPS-001 | delivery | not-applicable | Provide an API-only operational foundation. | The service starts and reports readiness. |\n\n" +
            "## UX, screens, and accessibility\n\nNo product screen, wireframe, or visual-design approval is required.\n");
        var revised = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, featurePath));

        Assert.Equal(0, revised.ExitCode);
        Assert.DoesNotContain(revised.WorkItems, item => item.Id == obsolete.Id);
        Assert.False(File.Exists(activePath));
        var retiredPath = Path.Combine(dossier, "agent-tasks", "retired", Path.GetFileName(activePath));
        Assert.True(File.Exists(retiredPath));
        var retired = File.ReadAllText(retiredPath);
        Assert.Contains("task_status: Retired", retired, StringComparison.Ordinal);
        Assert.Contains("Preserve this evidence", retired, StringComparison.Ordinal);
        Assert.Contains("## Retirement history", retired, StringComparison.Ordinal);
        var catalog = File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "catalog.yml"));
        Assert.Contains($"changes/{change.Id}/agent-tasks/retired/{Path.GetFileName(activePath)}", catalog, StringComparison.Ordinal);
        Assert.Equal(0, services.DocsValidation.Validate(repository.Path, strict: true).ExitCode);

        var repeated = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, featurePath));
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);

        repository.Write(featurePath,
            "---\ntitle: Operator experience\ntype: feature-specification\n---\n\n" +
            "## Functional requirements\n\n| ID | Surface | Frontend type | Requirement | Acceptance criteria |\n" +
            "|---|---|---|---|---|\n" +
            "| OPS-001 | full-stack | backoffice | Add an operator dashboard backed by an API. | Operators can review current orders. |\n");
        var restored = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, featurePath));
        var restoredDesign = Assert.Single(restored.WorkItems, item => item.Category == "design");
        Assert.Equal(obsolete.Id, restoredDesign.Id);
        Assert.True(File.Exists(activePath));
        var restoredContent = File.ReadAllText(activePath);
        Assert.Contains("Preserve this evidence", restoredContent, StringComparison.Ordinal);
        Assert.Contains("## Retirement history", restoredContent, StringComparison.Ordinal);
        Assert.DoesNotContain("task_status: Retired", restoredContent, StringComparison.Ordinal);
        Assert.False(File.Exists(retiredPath));
    }

    [Fact]
    public void ImpactAnalyse_RejectsAChangedGraphBaseline()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")])).Change);
        repository.Write("src/Orders.Api/Changed.cs", "namespace Orders.Api; public sealed class Changed { }");
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);

        var result = services.Impacts.Analyse(new ImpactAnalyseRequest(
            repository.Path,
            change.Id,
            [],
            1,
            50,
            false));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("baseline graph", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanApproval_IsBlockedByAnOpenDecision()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
        {
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        }

        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        var decision = services.Decisions.Create(new DecisionCreateRequest(
            repository.Path,
            change.Id,
            "Select compatibility policy",
            "contract",
            ["preserve compatibility", "introduce a new version"],
            Blocking: true,
            "plan approval",
            "contract evidence"));
        Assert.Equal(0, decision.ExitCode);
        var built = services.Plans.Build(repository.Path, change.Id);

        Assert.Equal(5, built.ExitCode);
        Assert.False(built.Validation!.Valid);
        Assert.Equal(1, built.Validation.OpenDecisions);
        Assert.Equal("blocked", services.Plans.Approve(repository.Path, change.Id).Status);

        var resolved = services.Decisions.Resolve(
            repository.Path,
            change.Id,
            decision.Decision!.Id,
            "preserve compatibility",
            "Existing consumers require a compatible contract.",
            "consumer evidence");
        Assert.Equal(0, resolved.ExitCode);
        Assert.True(services.Plans.Validate(repository.Path, change.Id).Validation!.Valid);
        Assert.Equal("approved", services.Plans.Approve(repository.Path, change.Id).Status);
    }

    [Fact]
    public void DecisionLifecycle_PreservesAuthorityAndPromotesResolvedDecisionToCatalogedAdr()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Choose contract strategy",
            "The API evolves under an explicit compatibility strategy.",
            [new ChangeRoot("orders-api", "component")])).Change);

        var created = services.Decisions.Create(new DecisionCreateRequest(
            repository.Path,
            change.Id,
            "How will clients remain compatible?",
            "architecture",
            ["preserve the existing contract", "publish a versioned contract"],
            Blocking: true,
            "plan approval",
            "API consumers and the governed API dictionary"));

        Assert.Equal(0, created.ExitCode);
        Assert.Equal("DEC-001", created.Decision!.Id);
        Assert.Equal("unchanged", services.Decisions.Create(new DecisionCreateRequest(
            repository.Path,
            change.Id,
            "How will clients remain compatible?",
            "architecture",
            ["preserve the existing contract", "publish a versioned contract"],
            true,
            "plan approval",
            "same evidence")).Status);
        Assert.Equal(2, services.Decisions.Resolve(
            repository.Path,
            change.Id,
            "DEC-001",
            "an unrecorded option",
            "Not valid.",
            null).ExitCode);
        Assert.Equal(2, services.Decisions.Promote(repository.Path, change.Id, "DEC-001", null).ExitCode);

        var resolved = services.Decisions.Resolve(
            repository.Path,
            change.Id,
            "DEC-001",
            "publish a versioned contract",
            "Versioning preserves existing integrations while allowing the new behavior.",
            "consumer inventory");
        Assert.Equal(0, resolved.ExitCode);
        Assert.Equal("resolved", resolved.Decision!.Status);

        var promoted = services.Decisions.Promote(
            repository.Path,
            change.Id,
            "DEC-001",
            "Version externally consumed API contracts");
        Assert.Equal(0, promoted.ExitCode);
        Assert.Equal("promoted", promoted.Status);
        Assert.EndsWith(".md", promoted.Decision!.PromotedAdr, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repository.Path, promoted.Decision.PromotedAdr.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains(promoted.Decision.PromotedAdr, File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "catalog.yml")), StringComparison.Ordinal);
        Assert.Equal("unchanged", services.Decisions.Promote(repository.Path, change.Id, "DEC-001", null).Status);
        Assert.Equal(0, services.DocsValidation.Validate(repository.Path, strict: false).ExitCode);

        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var graph = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path).Graph!;
        Assert.Contains(graph.Nodes, node => node.Kind == "decision"
            && node.Properties.GetValueOrDefault("decisionId") == "DEC-001");
        Assert.Contains(graph.Nodes, node => node.Kind == "decision" && node.Facets.Contains("promoted"));
        Assert.Contains(graph.Edges, edge => edge.Type == "generated-from");
    }

    [Fact]
    public void AdvisoryDeferredDecision_RemainsVisibleWithoutBlockingPlanApproval()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path,
            "Change order lookup",
            "Customers can retrieve an order with the revised contract.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 1, 100, false));
        foreach (var finding in analysis.Findings)
        {
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed scope.");
        }

        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        var advisory = services.Decisions.Create(new DecisionCreateRequest(
            repository.Path,
            change.Id,
            "Which optional dashboard should display adoption?",
            "operations",
            ["existing dashboard", "new dashboard"],
            Blocking: false,
            "post-delivery review",
            "observability inventory"));
        services.Decisions.Defer(repository.Path, change.Id, advisory.Decision!.Id, "Does not affect safe delivery.");

        var plan = services.Plans.Build(repository.Path, change.Id);
        Assert.Equal(0, plan.ExitCode);
        Assert.True(plan.Validation!.Valid);
        Assert.Contains(plan.Validation.Warnings, warning => warning.Contains("advisory", StringComparison.Ordinal));
        Assert.Equal("approved", services.Plans.Approve(repository.Path, change.Id).Status);
    }

    [Fact]
    public void DeliveryCommands_AreRegisteredByExplicitModules()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new ContextModule())
            .AddModule(new DecisionModule())
            .AddModule(new ChangeModule())
            .AddModule(new ImpactModule())
            .AddModule(new PlanModule())
            .AddModule(new DesignModule())
            .Build();

        Assert.Equal(0, application.Invoke(["change", "create", "--help"]));
        Assert.Equal(0, application.Invoke(["impact", "analyse", "--help"]));
        Assert.Equal(0, application.Invoke(["impact", "accept", "--help"]));
        Assert.Equal(0, application.Invoke(["impact", "completeness", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "build", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "import-spec", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "derive", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "approve", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "capability", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "capability", "select", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "task", "transition", "--help"]));
        Assert.Equal(0, application.Invoke(["plan", "task", "migrate-type", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "templates", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "scaffold", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "wireframe-validate", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "reuse", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "render", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "reconcile", "--help"]));
        Assert.Equal(0, application.Invoke(["design", "approve", "--help"]));
        Assert.Equal(0, application.Invoke(["decision", "create", "--help"]));
        Assert.Equal(0, application.Invoke(["decision", "resolve", "--help"]));
        Assert.Equal(0, application.Invoke(["decision", "promote", "--help"]));
    }

    [Fact]
    public void CoreTaskRegistry_HasOneDetailedCanonicalDefinitionPerType()
    {
        var root = FindRepositoryRoot();
        var definitions = TaskTypeRegistry.CreateDefault().Definitions;
        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["core.coordination.scope-guard"] = "coordination-scope-guard-task-type.md",
            ["core.design.wireframe"] = "wireframe-task-type.md",
            ["core.design.visual"] = "visual-design-task-type.md",
            ["core.documentation.contracts"] = "documentation-contracts-task-type.md",
            ["core.security.permissions"] = "security-permissions-task-type.md",
            ["core.data.persistence"] = "data-persistence-task-type.md",
            ["core.data.database-migration"] = "database-migration-task-type.md",
            ["core.data.backfill"] = "data-backfill-task-type.md",
            ["core.api.contract"] = "api-contract-task-type.md",
            ["core.backend.behavior"] = "backend-behavior-task-type.md",
            ["core.frontend.implementation"] = "frontend-implementation-task-type.md",
            ["core.integration.handoff"] = "integration-handoff-task-type.md",
            ["core.infrastructure.deployment"] = "infrastructure-deployment-task-type.md",
            ["core.operations.observability"] = "observability-operations-task-type.md",
            ["core.lifecycle.carry-forward"] = "lifecycle-carry-forward-task-type.md",
            ["core.release.rollout"] = "rollout-release-task-type.md",
            ["core.verification"] = "verification-task-type.md",
            ["core.assurance.independent"] = "independent-assurance-task-type.md",
            ["core.delivery.final-sweep"] = "final-delivery-sweep-task-type.md",
        };

        Assert.Equal(19, definitions.Count);
        Assert.Equal(definitions.Select(definition => definition.Key).Order(StringComparer.Ordinal),
            paths.Keys.Order(StringComparer.Ordinal));
        foreach (var definition in definitions)
        {
            var content = File.ReadAllText(Path.Combine(root, "docs", "specs", paths[definition.Key]));
            Assert.Contains($"stable_id: change-impact-studio:task-type:{definition.Key}", content, StringComparison.Ordinal);
            foreach (var section in new[]
                     {
                         "Acceptance criteria", "Negative criteria", "Validation",
                         "Deferral", "External issue hints",
                     })
            {
                Assert.Contains(section, content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ExtensionCapabilityConflict_RequiresCanonicalSelectionAndFiltersPlanning()
    {
        using var repository = TemporaryRepository.Create();
        var registry = CreateSearchRegistry();
        var services = CreateServices(taskTypes: registry);
        var change = PrepareSearchChange(repository, services);

        var conflicted = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));

        Assert.Equal(2, conflicted.ExitCode);
        Assert.Contains(conflicted.Errors, error => error.Contains("capability conflict", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(conflicted.Errors, error => error.Contains("parr.search.legacy", StringComparison.Ordinal));
        Assert.Contains(conflicted.Errors, error => error.Contains("parr.search.projection", StringComparison.Ordinal));
        var conflictEvents = File.ReadAllText(services.Changes.DossierFile(change, "events.jsonl"));
        Assert.Contains("task-capability-conflict-detected", conflictEvents, StringComparison.Ordinal);
        Assert.Contains("sha256:", conflictEvents, StringComparison.Ordinal);
        services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));
        Assert.Equal(1, File.ReadAllLines(services.Changes.DossierFile(change, "events.jsonl"))
            .Count(line => line.Contains("task-capability-conflict-detected", StringComparison.Ordinal)));

        var selected = services.Plans.SelectCapability(new TaskTypeCapabilitySelectRequest(
            repository.Path, "parr.search", "parr.search.legacy", [], "architect", "Use the current PARR search provider."));
        Assert.Equal(0, selected.ExitCode);
        Assert.True(selected.Applied);
        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));
        Assert.Equal(0, imported.ExitCode);
        Assert.Contains(imported.WorkItems, item => item.TaskTypeKey == "parr.search.legacy");
        Assert.DoesNotContain(imported.WorkItems, item => item.TaskTypeKey == "parr.search.projection");
        var ledger = File.ReadAllText(Path.Combine(repository.Path, "docs", "cis", "references",
            "task-type-capability-selections.md"));
        Assert.Contains("| parr.search | parr.search.legacy | None | architect |", ledger, StringComparison.Ordinal);
        var providerRemoved = CreateServices().Plans.CapabilityStatus(repository.Path);
        Assert.Equal(2, providerRemoved.ExitCode);
        Assert.Contains(providerRemoved.Errors, error => error.Contains("unavailable task type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtensionTaskMigration_PreservesHumanEvidenceAndSurvivesReimport()
    {
        using var repository = TemporaryRepository.Create();
        var registry = CreateSearchRegistry();
        var services = CreateServices(taskTypes: registry);
        var change = PrepareSearchChange(repository, services);
        Assert.Equal(0, services.Plans.SelectCapability(new TaskTypeCapabilitySelectRequest(
            repository.Path, "parr.search", "parr.search.legacy", [], "architect", "Start on the current provider.")).ExitCode);
        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));
        var legacy = Assert.Single(imported.WorkItems, item => item.TaskTypeKey == "parr.search.legacy");
        var taskPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            legacy.TaskPath!.Replace('/', Path.DirectorySeparatorChar));
        var humanEvidence = "| Human verification | `search-contract-test` | Passed | Preserve this evidence across migration. |";
        File.WriteAllText(taskPath, File.ReadAllText(taskPath)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |", humanEvidence, StringComparison.Ordinal)
            .Replace("|---|---|---|---|---|---|---|",
                "|---|---|---|---|---|---|---|\n| github | 42 | https://example.test/issues/42 | canonical | remote | 2026-08-14T12:00:00Z | linked |",
                StringComparison.Ordinal)
            .Replace("|---|---|---|---|\n",
                "|---|---|---|---|\n| github | cis | architect | 2026-08-14T12:01:00Z | Preserve reviewed tracker authority. |\n",
                StringComparison.Ordinal));

        Assert.Equal(0, services.Plans.SelectCapability(new TaskTypeCapabilitySelectRequest(
            repository.Path, "parr.search", "parr.search.projection", ["parr.search.legacy"],
            "architect", "Adopt the compatible projection provider.")).ExitCode);
        var implicitMigration = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));
        Assert.Equal(2, implicitMigration.ExitCode);
        Assert.Equal("migration-required", implicitMigration.Status);
        Assert.Contains(implicitMigration.Errors, error => error.Contains("migrate-type", StringComparison.Ordinal));
        var migrated = services.Plans.MigrateTaskType(new TaskTypeMigrationRequest(
            repository.Path, change.Id, legacy.Id, "parr.search.projection", "architect",
            "Move the existing task without losing review evidence."));

        Assert.Equal(0, migrated.ExitCode);
        Assert.True(migrated.Applied);
        var current = Assert.Single(migrated.WorkItems, item => item.Id == legacy.Id);
        Assert.Equal("parr.search.projection", current.TaskTypeKey);
        Assert.Equal("2.0", current.TaskTypeVersion);
        Assert.Equal("Draft", current.Status);
        var content = File.ReadAllText(taskPath);
        Assert.Contains(humanEvidence, content, StringComparison.Ordinal);
        Assert.Contains("example.test/issues/42", content, StringComparison.Ordinal);
        Assert.Contains("Preserve reviewed tracker authority", content, StringComparison.Ordinal);
        Assert.Contains("## Task-type migration history", content, StringComparison.Ordinal);
        Assert.Contains("parr.search.legacy@1.0", content, StringComparison.Ordinal);
        Assert.Contains("parr.search.projection@2.0", content, StringComparison.Ordinal);
        Assert.Contains("## Current task-type contract", content, StringComparison.Ordinal);
        Assert.Contains("- [ ] Acceptance:", content, StringComparison.Ordinal);

        var repeated = services.Plans.ImportSpec(new FeatureSpecImportRequest(
            repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));
        Assert.Equal(0, repeated.ExitCode);
        Assert.Single(repeated.WorkItems, item => item.TaskTypeKey == "parr.search.projection");
        var reconciled = File.ReadAllText(taskPath);
        Assert.Contains(humanEvidence, reconciled, StringComparison.Ordinal);
        Assert.Contains("example.test/issues/42", reconciled, StringComparison.Ordinal);
        Assert.Contains("Preserve reviewed tracker authority", reconciled, StringComparison.Ordinal);
        Assert.Contains("## Task-type migration history", reconciled, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskTypeRegistry_RejectsDuplicateProvidersAndReservedCoreReplacement()
    {
        var definition = new CisTaskTypeDefinition(
            "example.capability", "1.0", "example", "Example", 200, "conditional", ["example"], [],
            "low", "Example purpose.", "Example acceptance.", "Example validation.", CapabilityKey: "example");
        var duplicate = Assert.Throws<InvalidOperationException>(() => new TaskTypeRegistry([
            new TestTaskTypeProvider("example.provider", definition),
            new TestTaskTypeProvider("EXAMPLE.PROVIDER", definition with { Key = "example.other" }),
        ]));
        Assert.Contains("provider key", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        var reserved = Assert.Throws<InvalidOperationException>(() => new TaskTypeRegistry([
            new CoreTaskTypeProvider(),
            new TestTaskTypeProvider("example.provider", definition with { Key = "core.backend.replacement" }),
        ]));
        Assert.Contains("reserved core", reserved.Message, StringComparison.OrdinalIgnoreCase);

        var replacesCore = Assert.Throws<InvalidOperationException>(() => new TaskTypeRegistry([
            new CoreTaskTypeProvider(),
            new TestTaskTypeProvider("example.provider", definition with
            {
                ReplacesTypeKeys = ["core.backend.behavior"],
            }),
        ]));
        Assert.Contains("non-replaceable", replacesCore.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ChangeImpactStudio.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static Services CreateServices(
        IDesignProcessRunner? designRunner = null,
        TaskTypeRegistry? taskTypes = null,
        IEnumerable<ICisFeatureApprovalAuthority>? featureAuthorities = null)
    {
        var resolver = new CisRepositoryContextResolver();
        var reader = new DocumentationCatalogReader();
        var inspector = new MarkdownDocumentInspector();
        var inventory = new DocumentationInventoryService(resolver, reader, inspector);
        var docsValidation = new DocumentationValidationService(resolver, reader, inventory, inspector);
        var graph = new GraphBuilder(resolver, reader, inventory, docsValidation);
        var changes = new ChangeDossierStore(resolver, () => DateTimeOffset.Parse("2026-08-09T10:00:00Z"));
        var impacts = new ImpactAnalysisService(changes, new GraphQueryService(resolver));
        var decisions = new DecisionService(changes, resolver);
        var usage = new ToolUsageStore();
        var capabilityStore = new TaskTypeCapabilityStore(resolver,
            () => DateTimeOffset.Parse("2026-08-09T10:00:00Z"));
        var plans = new PlanningService(changes, impacts, decisions, resolver,
            taskTypes: taskTypes, toolUsage: usage, capabilities: capabilityStore,
            featureAuthorities: featureAuthorities);
        var designs = new DesignService(changes, new DesignTemplateCatalog(), featureAuthorities ?? [],
            () => DateTimeOffset.Parse("2026-08-09T10:00:00Z"), designRunner);
        return new Services(changes, decisions, impacts, plans, designs, usage, graph, docsValidation);
    }

    private static TaskTypeRegistry CreateSearchRegistry()
        => new(new ICisTaskTypeProvider[]
        {
            new CoreTaskTypeProvider(),
            new TestTaskTypeProvider("parr.search.legacy-provider",
                new CisTaskTypeDefinition(
                    "parr.search.legacy", "1.0", "search", "Build legacy search projection", 95,
                    "conditional", ["search"], [], "medium", "Build the selected search projection.",
                    "The projection is queryable and respects visibility.", "Run legacy projection and retrieval tests.",
                    CapabilityKey: "parr.search", ConflictsWithTypeKeys: ["parr.search.projection"])),
            new TestTaskTypeProvider("parr.search.projection-provider",
                new CisTaskTypeDefinition(
                    "parr.search.projection", "2.0", "search", "Build governed search projection", 96,
                    "conditional", ["search"], [], "medium", "Build the governed search projection.",
                    "The governed projection is queryable, current, and permission-filtered.",
                    "Run projection drift, permission, and retrieval tests.", CapabilityKey: "parr.search",
                    ConflictsWithTypeKeys: ["parr.search.legacy"], ReplacesTypeKeys: ["parr.search.legacy"])),
        });

    private static ChangeDossier PrepareSearchChange(TemporaryRepository repository, Services services)
    {
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Search projection", "Internal users can retrieve permission-filtered records.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var analysis = services.Impacts.Analyse(new ImpactAnalyseRequest(repository.Path, change.Id, [], 2, 200, false));
        foreach (var finding in analysis.Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed search scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/search-feature-spec.md", """
---
title: Search projection
type: feature-specification
status: Draft
---

## Functional requirements

| ID | Surface | Requirement | Acceptance criteria |
|---|---|---|---|
| SEARCH-001 | backend | Build an internal search projection and permission-filtered retrieval. | Internal results are current and restricted records are excluded. |
""");
        return change;
    }

    private static void DefineAcceptanceCriteria(string proposalPath)
    {
        var content = File.ReadAllText(proposalPath);
        File.WriteAllText(
            proposalPath,
            content.Replace(
                "- TODO: define outcome-level acceptance criteria before plan approval.",
                "- The revised order lookup is implemented and its contract tests pass.",
                StringComparison.Ordinal));
    }

    private static void WriteDesignWireframes(string path, string changeId, params (string Id, string Type)[] screens)
    {
        var inventory = string.Join(Environment.NewLine, screens.Select(screen =>
            $"| {screen.Id} | {screen.Type} | {screen.Id} | /{screen.Id.ToLowerInvariant()} | Web | Actor | Direct route | Verify state coverage. |"));
        var definitions = string.Join(Environment.NewLine + Environment.NewLine, screens.Select(screen => $"""
### {screen.Id}: {screen.Id}

#### Description

The governed screen presents its documented state without disclosing private data.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| OPEN-{screen.Id} | Screen | Always | Open | No mutation | /{screen.Id.ToLowerInvariant()} | {screen.Id} | Show a recoverable unavailable state. |

#### States

- Primary, loading, empty, unavailable, responsive, and keyboard-focus states are covered.
"""));
        File.WriteAllText(path, $"""
---
title: Design reuse test wireframes
type: textual-wireframes
status: Draft
change_id: {changeId}
approval_status: NotReviewed
authority: human-reviewed
---

# Textual wireframes

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
{inventory}

## Screen definitions

{definitions}

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States | Notes |
| --- | --- | --- | --- | --- |
| Governed coverage | {string.Join(", ", screens.Select(screen => screen.Id))} | {string.Join(", ", screens.Select(screen => $"OPEN-{screen.Id}"))} | All required states | Covered. |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Not reviewed | Pending | Pending | Pending | Pending |
""");
    }

    private sealed record Services(
        ChangeDossierStore Changes,
        DecisionService Decisions,
        ImpactAnalysisService Impacts,
        PlanningService Plans,
        DesignService Designs,
        ToolUsageStore Usage,
        GraphBuilder Graph,
        DocumentationValidationService DocsValidation);

    private sealed class BlockingReadinessCheck : IChangeReadinessCheck
    {
        public ChangeReadinessResult Evaluate(string repositoryPath)
            => new("technical-intent", Applicable: true, Ready: false,
                ["An Active, current workspace technical intent is required."]);
    }

    private sealed class StaticFeatureApprovalAuthority(string featurePath) : ICisFeatureApprovalAuthority
    {
        public CisFeatureApproval Evaluate(string repositoryPath, string featureSpecificationPath)
            => featureSpecificationPath.Equals(featurePath, StringComparison.OrdinalIgnoreCase)
                ? new(true, true, "HLT-FR-001", featurePath, "Product owner", "Approved bounded behavior.",
                    "sha256:approved-feature", [])
                : new(false, false, null, null, null, null, null, []);
    }

    private sealed class TestTaskTypeProvider(
        string providerKey,
        params CisTaskTypeDefinition[] definitions) : ICisTaskTypeProvider
    {
        public string ProviderKey => providerKey;

        public IReadOnlyList<CisTaskTypeDefinition> GetTaskTypes() => definitions;
    }

    private sealed class FakeDesignProcessRunner : IDesignProcessRunner
    {
        public DesignProcessResult Run(string repositoryPath, string rendererPath)
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("node")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("--check");
                start.ArgumentList.Add(rendererPath);
                using var syntax = System.Diagnostics.Process.Start(start);
                if (syntax is not null)
                {
                    var error = syntax.StandardError.ReadToEnd();
                    syntax.WaitForExit();
                    if (syntax.ExitCode != 0) throw new InvalidOperationException("Generated renderer syntax is invalid: " + error);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The lifecycle test remains deterministic on hosts without Node; production render reports its absence.
            }
            var files = System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(rendererPath), "\\\"file\\\":\\s*\\\"(?<file>[^\\\"]+\\.png)\\\"")
                .Select(match => match.Groups["file"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var file in files)
            {
                var png = new byte[24];
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
                WriteBigEndian(png, 16, 1600);
                WriteBigEndian(png, 20, 1000);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(rendererPath)!, file), png);
            }
            return new(0, "Runtime node=v22.0.0 sharp=0.34.0 libvips=8.16.0", string.Empty);
        }

        private static void WriteBigEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cis-delivery-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var repository = new TemporaryRepository(path);
            repository.Write(
                "src/Orders.Api/Orders.Api.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            repository.Write(
                "src/Orders.Api/Program.cs",
                "var builder = WebApplication.CreateBuilder(args); var app = builder.Build(); app.MapGet(\"/orders/{id}\", () => Results.Ok()); app.Run();");
            var init = new RepositoryInitializer().Initialize(new RepositoryInitRequest(path, "docs/cis", false, true));
            Assert.Equal(0, init.ExitCode);
            var services = CreateServices();
            Assert.Equal(0, services.Graph.Build(path).ExitCode);
            return repository;
        }

        public void Write(string relativePath, string content)
        {
            var absolute = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, content);
        }

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-delivery-tests"))
                + System.IO.Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
