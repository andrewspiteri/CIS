using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Change;
using Cis.Modules.Decision;
using Cis.Modules.Plan;
using Cis.Modules.Repository;
using Cis.Modules.Verify;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void ProportionateDraftCreation_DoesNotRequirePreliminaryCanonicalApproval()
    {
        using var repository = TemporaryRepository.Create();
        var resolver = new CisRepositoryContextResolver();
        var check = new BlockingReadinessCheck();
        var changes = new ChangeDossierStore(resolver, readinessChecks: [check]);
        var prepared = changes.Create(new(repository.Path, "Bounded draft", "Prepare the agreed outcome.", []));
        Assert.Equal(0, prepared.ExitCode);
        var change = Assert.IsType<ChangeDossier>(prepared.Change);
        Assert.Equal("Proposed", change.Status);
        var services = CreateServices();
        var planning = new PlanningService(changes, services.Impacts, services.Decisions, resolver, readinessChecks: [check]);
        var draft = planning.Build(repository.Path, change.Id);
        Assert.Equal(0, draft.ExitCode);
        Assert.Equal("Draft", draft.PlanStatus);
        Assert.Equal(0, changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with { VisualUi = false }, "Original human", "Authorize the bounded outcome.")).ExitCode);
        Assert.Equal(0, planning.Build(repository.Path, change.Id).ExitCode);
        Assert.Contains(planning.TransitionTask(new(repository.Path, change.Id, "WORK-001", "InProgress", "Controller",
            "Begin authorized work after checking canonical readiness.")).Errors,
            error => error.Contains("technical-intent", StringComparison.Ordinal));
    }

    [Fact]
    public void ProportionateDocuments_CreateOnlyActualSubjectsAndRegisterTheFirstDecision()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Bounded component update", "Replace the current value.", [new ChangeRoot("orders-api", "component")])).Change);
        var directory = Path.GetDirectoryName(services.Changes.DossierFile(change, "proposal.md"))!;
        Assert.Equal(new[] { "creation-inputs.json", "events.jsonl", "proposal.md" }, Directory.GetFiles(directory)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("repository_baselines:", File.ReadAllText(services.Changes.DossierFile(change, "proposal.md")), StringComparison.Ordinal);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(change.RepositoryBaselines), System.Text.Json.JsonSerializer.Serialize(
            services.Changes.Read(repository.Path, change.Id)!.RepositoryBaselines));
        Assert.True(services.Changes.IsProportionate(change));
        Assert.Empty(services.Decisions.List(repository.Path, change.Id).Decisions);
        var catalog = File.ReadAllText(Path.Combine(repository.Path, "docs/cis/catalog.yml"));
        Assert.DoesNotContain($"{change.RelativePath}/design.md", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain($"{change.RelativePath}/test-cases.md", catalog, StringComparison.Ordinal);
        Assert.Equal(0, services.DocsValidation.Validate(repository.Path, strict: true).ExitCode);

        var decision = services.Decisions.Create(new(repository.Path, change.Id,
            "Which existing storage contract should be retained?", "contract", ["Retain", "Replace"], true,
            "implementation", "The existing callers depend on its current behavior."));
        Assert.Equal(0, decision.ExitCode);
        Assert.Single(services.Decisions.List(repository.Path, change.Id).Decisions);
        Assert.Contains($"{change.RelativePath}/decisions.md", File.ReadAllText(Path.Combine(repository.Path, "docs/cis/catalog.yml")), StringComparison.Ordinal);
        Assert.Equal(0, services.DocsValidation.Validate(repository.Path, strict: true).ExitCode);
        File.Delete(services.Changes.DossierFile(change, "decisions.md"));
        Assert.Contains("Decision document is missing.", services.Decisions.Read(change).Errors);
    }

    [Fact]
    public void ProportionateDocuments_ApplicableTemplatesAreLazyAndAuthoredRecordsArePreserved()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Reviewed UI behavior", "Display the existing value.", [new ChangeRoot("orders-api", "component")])).Change);
        Assert.Empty(services.Changes.EnsureApplicableDocuments(change, visualUi: false));
        Assert.False(File.Exists(services.Changes.DossierFile(change, "design.md")));
        Assert.Empty(services.Changes.EnsureApplicableDocuments(change, visualUi: true, manualTestCatalogue: true));
        foreach (var name in new[] { "wireframes.md", "design.md", "test-cases.md", "test-cases.csv" })
            Assert.True(File.Exists(services.Changes.DossierFile(change, name)));
        var design = services.Changes.DossierFile(change, "design.md");
        var authored = File.ReadAllText(design) + "\nAn authored design outcome.\n";
        File.WriteAllText(design, authored);
        Assert.Empty(services.Changes.EnsureApplicableDocuments(change, visualUi: true, manualTestCatalogue: true));
        Assert.Equal(authored, File.ReadAllText(design));
        Assert.Equal(0, services.DocsValidation.Validate(repository.Path, strict: true).ExitCode);
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with { VisualUi = true, ManualTestCatalogue = true },
            "Product owner", "Approve the visual and catalogue obligations for this outcome."));
        Assert.Equal(0, authorized.ExitCode);
        Assert.True(services.Changes.DocumentApplicability(change, manualTestCatalogue: false).ManualTestCatalogue);
        Assert.NotEmpty(services.Changes.DocumentApplicability(change, visualUi: false).Errors);

        var proposal = services.Changes.DossierFile(change, "proposal.md");
        var legacy = File.ReadAllText(proposal).Replace("record_format: proportionate\n", string.Empty, StringComparison.Ordinal)
            .Replace("record_format: proportionate\r\n", string.Empty, StringComparison.Ordinal);
        File.WriteAllText(proposal, legacy);
        Assert.False(services.Changes.IsProportionate(services.Changes.Read(repository.Path, change.Id)!));
        Assert.Contains("Decision document is missing.", services.Decisions.Read(change).Errors);
        Assert.Equal(legacy, File.ReadAllText(proposal));
        Assert.Equal(authored, File.ReadAllText(design));
    }
}

public sealed partial class ExecutionModulesTests
{
    [Fact]
    public void ProportionateExecutionInputs_OmitIgnoredGeneratedTransfersAndPreserveTrackedInputs()
    {
        using var repository = ExecutionRepository.Create(git: true);
        repository.Write(".gitignore", ".codex-tmp/\n.artifacts/\n");
        repository.Write(".codex-tmp/transfer.tar", "Obsolete generated transfer bytes are retained.");
        repository.Write(".artifacts/previous-results/native.trx", "Retained generated result.");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        var inputs = CisExecutionIdentity.CaptureInputs(context);
        Assert.DoesNotContain(inputs, input => input.Path.StartsWith(".codex-tmp/", StringComparison.Ordinal));
        Assert.DoesNotContain(inputs, input => input.Path.StartsWith(".artifacts/", StringComparison.Ordinal));
        repository.Write(".codex-tmp/Source.cs", "class RetainedSource { }");
        repository.Git("add", "-f", ".codex-tmp/Source.cs");
        Assert.Contains(CisExecutionIdentity.CaptureInputs(context), input => input.Path == ".codex-tmp/Source.cs");
        Assert.True(File.Exists(Path.Combine(repository.Path, ".codex-tmp/transfer.tar")));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "CIS-VERIFY-DESIGN")]
    [InlineData(null, "CIS-VERIFY-APPLICABILITY")]
    public void ProportionateVerification_UsesAcceptedApplicabilityWithoutFeatureMetadata(bool? visualUi, string? expectedFinding)
    {
        using var repository = ExecutionRepository.Create(git: true);
        repository.Write("docs/cis/changes/CIS-0001/proposal.md", $$"""
            ---
            change_id: CIS-0001
            title: Scoped component verification
            outcome: Verified delivery
            status: Active
            record_format: proportionate
            baseline_kind: git
            baseline: {{repository.Head()}}
            graph_build_id: graph-1
            impact_roots: []
            ---
            # Proposal
            """);
        repository.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\n---\n# Plan\n");
        repository.Write("docs/cis/changes/CIS-0001/verification.md", "---\nstatus: Draft\n---\n| Task | Check | Artifact | Result | Notes |\n|---|---|---|---|---|\n| WORK-001 | tests | native-result.trx | Passed | current fixture |\n");
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", "---\ntask_status: Complete\n---\n# Work\nTarget `src/example.txt`\n");
        repository.Write("src/example.txt", "changed\n");
        var resolver = new CisRepositoryContextResolver();
        var changes = new ChangeDossierStore(resolver, Clock, documentApplicabilitySources:
            [new RecordedDocumentApplicability(visualUi)]);
        var service = new VerifyService(resolver, changes, [], Clock);
        Assert.Equal(0, service.Diff(repository.Path, "CIS-0001").ExitCode);
        var result = service.Validate(repository.Path, "CIS-0001");
        if (expectedFinding is null) Assert.Equal(0, result.ExitCode);
        else Assert.Contains(result.Findings, finding => finding.Code == expectedFinding);
        Assert.False(File.Exists(changes.DossierFile(changes.Read(repository.Path, "CIS-0001")!, "design.md")));

        if (visualUi == false)
        {
            repository.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: true\n---\n# Plan\n");
            Assert.Contains(service.Validate(repository.Path, "CIS-0001").Findings,
                finding => finding.Code == "CIS-VERIFY-APPLICABILITY" && finding.Message.Contains("conflicts", StringComparison.Ordinal));
        }
        else if (visualUi == true)
        {
            const string designPath = "docs/cis/changes/CIS-0001/design.md";
            repository.Write(designPath, "---\napproval_status: NotReviewed\ngate_status: PausedForReview\n---\n# Design\n\n```text\napproval_status: Approved\ngate_status: Approved\n```\n");
            Assert.Contains(service.Validate(repository.Path, "CIS-0001").Findings, finding => finding.Code == "CIS-VERIFY-DESIGN");
            repository.Write(designPath, "---\napproval_status: Approved\ngate_status: PausedForReview\n---\n# Design\n");
            Assert.Contains(service.Validate(repository.Path, "CIS-0001").Findings, finding => finding.Code == "CIS-VERIFY-DESIGN");
        }
    }

    private sealed class RecordedDocumentApplicability(bool? visualUi) : IChangeDocumentApplicabilitySource
    {
        public ChangeDocumentApplicabilitySource DocumentApplicability(string repositoryPath) => new(true, visualUi, []);
    }
}
