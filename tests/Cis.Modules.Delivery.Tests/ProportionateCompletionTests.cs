using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void ProportionateCompletion_OneBackendTaskCompletesItsOwnChecksWithoutOverheadTasks()
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, imported) = ImportProportionateCompletion(repository,
            "| VALUE-001 | backend | Replace the direct in-memory value. | Replacement returns the previous value. |");
        var task = Assert.Single(imported.WorkItems);
        Assert.Equal("backend", task.Category);
        Assert.Contains(task.VerificationObligations, obligation => obligation.Id == "task-contract");
        Assert.Contains(task.VerificationObligations, obligation => obligation.Id == "task-independent-review");
        Assert.Contains(task.VerificationObligations, obligation => obligation.Id == "task-documentation");
        Assert.Contains(task.VerificationObligations, obligation => obligation.Id == "task-completion");
        Assert.True(imported.Validation!.Valid);
        var transition = new PlanTaskTransitionRequest(repository.Path, change.Id, task.Id, "InProgress",
            "Synthetic implementer", "Exercise the scoped completion protocol.");
        Assert.Equal(0, services.Plans.TransitionTask(transition).ExitCode);
        var incomplete = services.Plans.TransitionTask(transition with { Status = "Complete" });
        Assert.Equal(2, incomplete.ExitCode);
        Assert.Contains(incomplete.Errors, error => error.Contains("checklists", StringComparison.Ordinal));
        CompleteProportionateProtocolEvidence(services, change, task);

        var completed = services.Plans.TransitionTask(transition with { Status = "Complete" });

        Assert.Equal(0, completed.ExitCode);
        Assert.True(completed.Validation!.Valid);
        Assert.Equal("Complete", Assert.Single(completed.WorkItems).Status);
        Assert.DoesNotContain(completed.WorkItems, item => item.Category is "coordination" or "verification" or "assurance" or "delivery");
    }

    [Fact]
    public void ProportionateCompletion_AdoptedBackendKeepsNativeAndIndependentReviewGatesWithinTheTask()
    {
        using var repository = TemporaryRepository.Create();
        Assert.Equal(0, new Cis.Modules.Repository.RepositoryInitializer().Initialize(
            new(repository.Path, "docs/cis", false, true, AdoptEngineeringDefaults: true)).ExitCode);
        var (services, change, imported) = ImportProportionateCompletion(repository,
            "| VALUE-001 | backend | Replace the direct in-memory value. | Replacement returns the previous value. |", engineering: true);
        var task = Assert.Single(imported.WorkItems);
        Assert.Equal("backend", task.Category);
        var context = services.Plans.CompletionContext(repository.Path, change.Id, task.Id);
        Assert.Empty(context.Errors);
        var receipt = Assert.IsType<EngineeringCompletionReceipt>(context.Template);
        Assert.Contains(receipt.Gates, gate => gate.Id == "unit" && gate.State == EngineeringGateState.Missing);
        Assert.Contains(receipt.Gates, gate => gate.Id == "review" && gate.State == EngineeringGateState.Missing);
        Assert.Contains(receipt.VerificationObligations, obligation => obligation.Id == "task-independent-review");
        CompleteProportionateProtocolEvidence(services, change, task);
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id, "InProgress",
            "Synthetic implementer", "Begin the adopted implementation protocol.")).ExitCode);
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);

        var result = services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id, "Complete",
            "Synthetic implementer", "Checked prose cannot substitute for native and assigned-reviewer evidence."));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("closing evidence is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void ProportionateCompletion_ExplicitWorkloadExclusionsDoNotRequireALoadExperiment()
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, imported) = ImportProportionateCompletion(repository,
            "| VALUE-001 | backend | Replace the direct in-memory value. No bulk operations, load tests or datasets are introduced. | Replacement returns the previous value. |");
        var task = Assert.Single(imported.WorkItems);
        CompleteProportionateProtocolEvidence(services, change, task);
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id, "InProgress", "Fixture", "Begin bounded local behavior.")).ExitCode);

        var result = services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id, "Complete", "Fixture", "The accepted scope excludes volume-sensitive work."));

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(services.Changes.DossierFile(change, "workload-evidence.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProportionateCompletion_WorkloadOwnerKeepsItsGateBeforeAnUnrelatedTaskFinishes(bool authoredReplacement)
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, imported) = ImportProportionateCompletion(repository,
            "| DATA-001 | data | Persist a bulk dataset through the existing record store. | All 100 input records remain available. |\n"
            + "| VALUE-001 | backend | Replace the direct in-memory value. | Replacement returns the previous value. |");
        var owner = Assert.Single(imported.WorkItems, item => item.Category == "data");
        var unrelated = Assert.Single(imported.WorkItems, item => item.Category == "backend");
        Assert.Contains(owner.VerificationObligations, obligation => obligation.EvidenceExpectations.Any(
            expectation => expectation.Contains("workload-evidence.json", StringComparison.Ordinal)));
        if (authoredReplacement)
        {
            var authored = new CisTaskVerificationObligation("authored-volume-review", [owner.AcceptanceCriteria],
                "Review all accepted scope through this authored route.", "review", ["Current scoped review findings."])
            { BasisReferences = owner.RequirementIds };
            var path = ProportionateTaskPath(services, change, owner);
            File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), "(?m)^verification_obligations:.*$",
                _ => "verification_obligations: " + JsonSerializer.Serialize(new[] { authored }), RegexOptions.CultureInvariant));
            Assert.DoesNotContain(services.Plans.Status(repository.Path, change.Id).WorkItems.Single(task => task.Id == owner.Id)
                .VerificationObligations, obligation => obligation.EvidenceExpectations.Any(
                    expectation => expectation.Contains("workload-evidence.json", StringComparison.Ordinal)));
        }
        CompleteProportionateProtocolEvidence(services, change, owner);
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, owner.Id, "InProgress", "Fixture", "Begin the volume-sensitive deliverable.")).ExitCode);

        var result = services.Plans.TransitionTask(new(repository.Path, change.Id, owner.Id, "Complete", "Fixture", "A component pass cannot replace applicable workload evidence."));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("workload evidence is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("draft", services.Plans.Status(repository.Path, change.Id).WorkItems.Single(item => item.Id == unrelated.Id).Status,
            ignoreCase: true);
    }

    [Fact]
    public void ProportionateCompletion_LastActualTaskChecksReadinessWithoutBlockingAnUnrelatedEarlyLeaf()
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, imported) = ImportProportionateCompletion(repository,
            "| VALUE-001 | backend | Replace the direct in-memory value. | Replacement returns the previous value. |\n"
            + "| RECORD-001 | data | Persist the record using the existing repository. | The written record remains available. |");
        var data = Assert.Single(imported.WorkItems, item => item.Category == "data");
        var backend = Assert.Single(imported.WorkItems, item => item.Category == "backend");
        Assert.Equal(2, imported.WorkItems.Count);
        File.WriteAllText(services.Changes.DossierFile(change, "workload-evidence.json"),
            "{\"schemaVersion\":1,\"applicable\":true,\"endToEndResult\":\"timeout\"}");
        CompleteProportionateProtocolEvidence(services, change, data);
        CompleteProportionateProtocolEvidence(services, change, backend);
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, data.Id, "InProgress", "Fixture", "Begin data deliverable.")).ExitCode);

        var early = services.Plans.TransitionTask(new(repository.Path, change.Id, data.Id, "Complete", "Fixture", "Complete the independent data boundary."));

        Assert.Equal(0, early.ExitCode);
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, backend.Id, "InProgress", "Fixture", "Begin final deliverable.")).ExitCode);
        var final = services.Plans.TransitionTask(new(repository.Path, change.Id, backend.Id, "Complete", "Fixture", "Reconcile overall readiness within actual work."));
        Assert.NotEqual(0, final.ExitCode);
        Assert.Contains(final.Errors, error => error.Contains("has not passed", StringComparison.Ordinal));
        File.WriteAllText(services.Changes.DossierFile(change, "workload-evidence.json"),
            "{\"schemaVersion\":1,\"applicable\":false,\"rationale\":\"This isolated protocol fixture changes ordinary record metadata and introduces no volume-sensitive runtime behavior.\"}");
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, backend.Id, "Complete", "Fixture", "The applicability record is concrete.")).ExitCode);
    }

    [Theory]
    [InlineData("review", true)]
    [InlineData("artifact", false)]
    public void ProportionateCompletion_PublicCacheAcceptsAuthoredReviewCoverageWithoutSeparateAssuranceTasks(string route, bool valid)
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, imported) = ImportProportionateCompletion(repository,
            "| PUBLIC-001 | backend | Expose an unauthenticated public API endpoint returning the cached value. | The route uses the governed cache and never queries the repository directly. |");
        Assert.True(imported.Source!.PublicEndpoints);
        Assert.DoesNotContain(imported.WorkItems, item => item.Category is "verification" or "assurance");
        var backend = Assert.Single(imported.WorkItems, item => item.Category == "backend");
        var path = ProportionateTaskPath(services, change, backend);
        var authored = new CisTaskVerificationObligation("authored-complete-cache-review", [backend.AcceptanceCriteria],
            "Review the complete accepted cache boundary and its native checks.", route, ["Current scoped evidence and independent findings."])
        { BasisReferences = backend.RequirementIds };
        File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), "(?m)^verification_obligations:.*$",
            _ => "verification_obligations: " + JsonSerializer.Serialize(new[] { authored }), RegexOptions.CultureInvariant));

        var validation = services.Plans.Validate(repository.Path, change.Id);

        Assert.Equal(route, Assert.Single(validation.WorkItems.Single(item => item.Id == backend.Id).VerificationObligations!).EvidenceRoute);
        Assert.Equal(valid, validation.Validation!.Valid);
        if (!valid) Assert.Contains(validation.Validation.Errors, error => error.Contains("independent review", StringComparison.Ordinal));
    }

    private static (Services Services, ChangeDossier Change, PlanResult Imported) ImportProportionateCompletion(
        TemporaryRepository repository, string requirements, bool engineering = false)
    {
        if (!engineering) repository.Write(".cis/engineering-defaults.json", "{\"schemaVersion\":1,\"requireTaskCompletion\":false,\"additionalGates\":[]}");
        var services = CreateServices(engineering: engineering);
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Bounded local behavior", "Complete only the accepted local behavior and its required checks.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed this isolated task-planning fixture.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        repository.Write("docs/cis/specs/proportionate-completion.md", "---\ntitle: Bounded local behavior\ntype: feature-specification\n---\n\n"
            + "## Functional requirements\n\n| ID | Surface | Requirement | Acceptance criteria |\n| --- | --- | --- | --- |\n" + requirements + "\n");
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, "docs/cis/specs/proportionate-completion.md"));
        Assert.True(imported.ExitCode == 0, string.Join("; ", imported.Errors.Concat(imported.Validation?.Errors ?? [])));
        return (services, change, imported);
    }

    private static string ProportionateTaskPath(Services services, ChangeDossier change, PlanWorkItem task)
        => Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, task.TaskPath!.Replace('/', Path.DirectorySeparatorChar));

    private static void CompleteProportionateProtocolEvidence(Services services, ChangeDossier change, PlanWorkItem task)
    {
        var path = ProportionateTaskPath(services, change, task);
        File.WriteAllText(path, File.ReadAllText(path).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |",
                "| Scoped protocol | Isolated completion fixture | Passed | Product and release qualification are separate. |", StringComparison.Ordinal));
    }
}
