using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void PlanImportSpec_ApprovedCatalogueRefreshPreservesBroaderReviewedWorkflowCoverageAcrossCorePolicyUpdate()
    {
        using var repository = TemporaryRepository.Create();
        var readiness = new ToggleReadinessCheck();
        var formerTypes = new CoreTaskTypeProvider().GetTaskTypes().Select(definition =>
            definition.Category is "documentation" or "assurance"
                ? definition with { CreationPolicy = "always" } : definition).ToArray();
        var former = CreateServices(taskTypes: new TaskTypeRegistry([new TestTaskTypeProvider("cis.core", formerTypes)]),
            readinessChecks: [readiness]);
        var change = Assert.IsType<ChangeDossier>(former.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Retain reviewed value delivery", "The approved value behavior and independently owned handoffs retain their scope.",
            [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in former.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            Assert.Equal(0, former.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted",
                "Reviewed the bounded value behavior and separately owned guide and assurance report.").ExitCode);
        DefineAcceptanceCriteria(former.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/reviewed-value-delivery.md";
        repository.Write(source, """
            ---
            title: Reviewed value delivery
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Replace the current value directly. | The previous value is returned exactly. Its manual test catalogue retains stable case identities and automated coverage references. |
            | GUIDE-001 | documentation | Publish a separately owned operator guide. | The named maintainer accepts the guide. |
            | REVIEW-001 | assurance | Publish a separately owned independent assurance report. | The independent reviewer records findings and dispositions. |
            """);
        var request = new FeatureSpecImportRequest(repository.Path, change.Id, source);
        var imported = former.Plans.ImportSpec(request);
        Assert.True(imported.ExitCode == 0, string.Join("; ", imported.Errors.Concat(imported.Validation?.Errors ?? [])));
        var requirements = new[] { "VALUE-001", "GUIDE-001", "REVIEW-001" };
        var documentation = Assert.Single(imported.WorkItems, task => task.Category == "documentation");
        var assurance = Assert.Single(imported.WorkItems, task => task.Category == "assurance");
        Assert.Equal(requirements, documentation.RequirementIds);
        Assert.Equal(requirements, assurance.RequirementIds);
        var planPath = former.Changes.DossierFile(change, "plan.md");
        var dossier = Path.GetDirectoryName(planPath)!;
        const string authoredOutput = "Retain the reviewed operator handoff artifact.";
        const string authoredBoundary = "Preserve the reviewed compatibility boundary.";
        var proof = new CisTaskVerificationObligation("accepted-guide-proof", [documentation.AcceptanceCriteria],
            "Retain the independently reviewed operator-guide proof.", "artifact", ["accepted/operator-guide-proof.json"])
        { BasisReferences = ["reviewed-operator-guide-decision"] };
        foreach (var task in new[] { documentation, assurance })
        {
            var taskPath = Path.Combine(dossier, task.TaskPath!);
            var document = File.ReadAllText(taskPath)
                .Replace("## Structured test-obligation matrix", "- " + authoredOutput + "\n\n## Structured test-obligation matrix", StringComparison.Ordinal)
                .Replace("## Context and evidence", "- " + authoredBoundary + "\n\n## Context and evidence", StringComparison.Ordinal)
                .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |",
                    "| Reviewed handoff | accepted/handoff-review.json | Passed | Preserve the reviewed handoff evidence. |", StringComparison.Ordinal);
            if (task.Id == documentation.Id)
                document = Regex.Replace(document, @"(?m)^verification_obligations: [^\r\n]*",
                    _ => "verification_obligations: " + JsonSerializer.Serialize(task.VerificationObligations.Append(proof)));
            File.WriteAllText(taskPath, document + "\n## Maintainer handoff\n\nPreserve the reviewed implementation context and evidence.\n");
        }
        var approved = former.Plans.Approve(repository.Path, change.Id, "Synthetic plan reviewer",
            "Approve the exact value behavior and broad workflow coverage retained from the existing policy.");
        Assert.Equal(0, approved.ExitCode);
        Assert.Equal("Approved", approved.PlanStatus);
        var moved = former.Plans.TransitionTask(new(repository.Path, change.Id, documentation.Id, "InProgress",
            "Synthetic controller", "Record the guide owner's actual work state without changing approved scope."));
        Assert.True(moved.ExitCode == 0, string.Join("; ", moved.Errors.Concat(moved.Validation?.Errors ?? [])));
        File.WriteAllText(planPath, File.ReadAllText(planPath).Replace("## Approval summary",
            "## Approval summary\n\nRetain the explicitly reviewed workflow coverage and handoff boundaries.", StringComparison.Ordinal));
        var approvedItems = former.Plans.Show(repository.Path, change.Id).WorkItems;
        Assert.Equal("InProgress", approvedItems.Single(task => task.Id == documentation.Id).Status);
        var approvedTasks = approvedItems.ToDictionary(task => task.Id, task => File.ReadAllBytes(Path.Combine(dossier, task.TaskPath!)));
        var digests = approvedItems.ToDictionary(task => task.Id,
            task => former.Plans.ReadTaskDigest(repository.Path, change.Id, task.Id));
        var planBytes = File.ReadAllBytes(planPath);
        var eventsPath = former.Changes.DossierFile(change, "events.jsonl");
        var approvalEvents = ReadWorkflowApprovalEvents(eventsPath);
        Assert.Single(approvalEvents);
        var cataloguePath = former.Changes.DossierFile(change, "test-cases.md");
        var csvPath = former.Changes.DossierFile(change, "test-cases.csv");
        var previousCatalogue = File.ReadAllBytes(cataloguePath);
        readiness.Ready = false;
        repository.Write("tests/reviewed-value-delivery.test.ts", """
            test('TC-VALUE-001-001 returns the previous value', () => {});
            test('TC-GUIDE-001-001 records operator guide acceptance', () => {});
            test('TC-REVIEW-001-001 records independent assurance dispositions', () => {});
            """);

        var current = CreateServices(readinessChecks: [readiness]);
        var refreshed = current.Plans.ImportSpec(request);

        Assert.True(refreshed.ExitCode == 0, string.Join("; ", refreshed.Errors.Concat(refreshed.Validation?.Errors ?? [])));
        Assert.True(refreshed.Applied);
        Assert.Equal("Approved", refreshed.PlanStatus);
        Assert.Equal(imported.Source!.Sha256, refreshed.Source!.Sha256);
        var reloaded = current.Plans.Show(repository.Path, change.Id);
        Assert.Equal("Approved", reloaded.PlanStatus);
        Assert.Equal(approvedItems.Select(task => (task.Id, task.InstanceKey, task.Status)),
            reloaded.WorkItems.Select(task => (task.Id, task.InstanceKey, task.Status)));
        foreach (var task in approvedItems)
        {
            var retained = Assert.Single(reloaded.WorkItems, item => item.Id == task.Id);
            Assert.Equal(task.RequirementIds, retained.RequirementIds);
            Assert.Equal(task.RequiredOutputs, retained.RequiredOutputs);
            Assert.Equal(task.NegativeCriteria, retained.NegativeCriteria);
            Assert.Equal(JsonSerializer.Serialize(task.VerificationObligations), JsonSerializer.Serialize(retained.VerificationObligations));
            Assert.Equal(digests[task.Id], current.Plans.ReadTaskDigest(repository.Path, change.Id, task.Id));
            Assert.Equal(approvedTasks[task.Id], File.ReadAllBytes(Path.Combine(dossier, task.TaskPath!)));
        }
        foreach (var task in reloaded.WorkItems.Where(task => task.Category is "documentation" or "assurance"))
        {
            Assert.Equal(requirements, task.RequirementIds);
            Assert.Contains(authoredOutput, task.RequiredOutputs!);
            Assert.Contains(authoredBoundary, task.NegativeCriteria!);
            Assert.Contains("Preserve the reviewed handoff evidence.", File.ReadAllText(Path.Combine(dossier, task.TaskPath!)), StringComparison.Ordinal);
        }
        Assert.Equal(JsonSerializer.Serialize(proof), JsonSerializer.Serialize(Assert.Single(reloaded.WorkItems
            .Single(task => task.Id == documentation.Id).VerificationObligations, obligation => obligation.Id == proof.Id)));
        Assert.Equal(planBytes, File.ReadAllBytes(planPath));
        Assert.Equal(approvalEvents, ReadWorkflowApprovalEvents(eventsPath));
        Assert.False(previousCatalogue.SequenceEqual(File.ReadAllBytes(cataloguePath)));
        var catalogue = File.ReadAllText(cataloguePath);
        Assert.Contains("automated_test_case_count: 3", catalogue, StringComparison.Ordinal);
        Assert.Contains("automation_pending_count: 0", catalogue, StringComparison.Ordinal);
        Assert.Contains("::tests/reviewed-value-delivery.test.ts:1", catalogue, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(File.ReadAllText(csvPath), "\"Automated\"").Count);
        Assert.Empty(current.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id));
        var derivedBytes = new[] { cataloguePath, csvPath, eventsPath }.ToDictionary(path => path, File.ReadAllBytes);

        var repeated = CreateServices(readinessChecks: [readiness]).Plans.ImportSpec(request);

        Assert.Equal(0, repeated.ExitCode);
        Assert.False(repeated.Applied);
        Assert.Equal("unchanged", repeated.Status);
        Assert.Equal(planBytes, File.ReadAllBytes(planPath));
        foreach (var task in approvedItems) Assert.Equal(approvedTasks[task.Id], File.ReadAllBytes(Path.Combine(dossier, task.TaskPath!)));
        foreach (var artifact in derivedBytes) Assert.Equal(artifact.Value, File.ReadAllBytes(artifact.Key));
        repository.Write(source, File.ReadAllText(Path.Combine(repository.Path, source)).Replace(
            "The previous value is returned exactly.", "The previous value is returned with the revised marker.", StringComparison.Ordinal));

        var blocked = current.Plans.ImportSpec(request);

        Assert.NotEqual(0, blocked.ExitCode);
        Assert.False(blocked.Applied);
        Assert.Contains(blocked.Errors, error => error.Contains("technical-intent", StringComparison.Ordinal));
        Assert.Equal(planBytes, File.ReadAllBytes(planPath));
        foreach (var task in approvedItems) Assert.Equal(approvedTasks[task.Id], File.ReadAllBytes(Path.Combine(dossier, task.TaskPath!)));
        foreach (var artifact in derivedBytes) Assert.Equal(artifact.Value, File.ReadAllBytes(artifact.Key));
        readiness.Ready = true;

        var revised = current.Plans.ImportSpec(request);

        Assert.True(revised.ExitCode == 0, string.Join("; ", revised.Errors.Concat(revised.Validation?.Errors ?? [])));
        Assert.Equal("Draft", revised.PlanStatus);
        Assert.NotEqual(imported.Source!.Sha256, revised.Source!.Sha256);
        Assert.Equal(new[] { "GUIDE-001" }, revised.WorkItems.Single(task => task.Id == documentation.Id).RequirementIds);
        Assert.Equal(new[] { "REVIEW-001" }, revised.WorkItems.Single(task => task.Id == assurance.Id).RequirementIds);
        Assert.Equal(approvalEvents, ReadWorkflowApprovalEvents(eventsPath));
        Assert.Contains("plan-approval-invalidated", File.ReadAllText(eventsPath), StringComparison.Ordinal);
    }

    private static string[] ReadWorkflowApprovalEvents(string path)
        => File.ReadAllLines(path).Where(line =>
        {
            using var entry = JsonDocument.Parse(line);
            return entry.RootElement.GetProperty("type").GetString() == "plan-approved";
        }).ToArray();
}
