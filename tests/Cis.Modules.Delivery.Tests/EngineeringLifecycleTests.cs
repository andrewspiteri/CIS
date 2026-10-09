using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void AdoptedCompletionBlocksMissingEvidenceThenAcceptsCurrentReceiptAndRejectsLaterTaskEdit()
    {
        using var repository = TemporaryRepository.Create();
        Assert.Equal(0, new Cis.Modules.Repository.RepositoryInitializer().Initialize(new(repository.Path, "docs/cis", false, true, AdoptEngineeringDefaults: true)).ExitCode);
        var services = CreateServices(engineering: true);
        var change = PrepareSearchChange(repository, services);
        var spec = Path.Combine(repository.Path, "docs/cis/specs/search-feature-spec.md");
        File.WriteAllText(spec, File.ReadAllText(spec).Replace("| backend |", "| documentation |", StringComparison.Ordinal));
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, "docs/cis/specs/search-feature-spec.md"));
        var task = Assert.Single(imported.WorkItems, item => item.Category == "documentation");
        var transition = new PlanTaskTransitionRequest(repository.Path, change.Id, task.Id, "Complete", "fixture-actor", "Exercise completion contract.");
        Assert.Equal(0, services.Plans.TransitionTask(transition with { Status = "InProgress" }).ExitCode);
        var taskPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, task.TaskPath!);
        File.WriteAllText(taskPath, File.ReadAllText(taskPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |", "| Fixture | Native assertions | Passed | Scoped test evidence. |", StringComparison.Ordinal));
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var missing = services.Plans.TransitionTask(transition);
        Assert.Contains(missing.Errors, error => error.Contains("closing evidence is missing", StringComparison.Ordinal));
        var context = services.Plans.CompletionContext(repository.Path, change.Id, task.Id);
        Assert.Empty(context.Errors);
        var template = Assert.IsType<EngineeringCompletionReceipt>(context.Template);
        Assert.All(template.Gates, gate => Assert.Equal(EngineeringGateState.Missing, gate.State));

        // Protocol fixtures validate enforcement; they are not claims of a live external review.
        var evidence = WriteEvidence("scope.json", new { scope = "Documentation contract fixture; source and scope reviewed." });
        var result = WriteEvidence("review-result.json", new
        {
            schemaVersion = 2,
            envelopeId = "envelope-1",
            status = CisAgentRunStates.Succeeded,
            review = new { recommendation = "ready", findings = Array.Empty<object>() }
        });
        var run = WriteEvidence("review-run.json", new
        {
            schemaVersion = 1,
            runId = "review-1",
            startedAtUtc = "2000-01-02T00:00:00Z",
            inputDigest = template.InputDigest,
            status = CisAgentRunStates.Succeeded,
            mode = "review",
            permission = "read-only",
            taskId = task.Id,
            provider = "fixture-provider",
            envelopeId = "envelope-1",
            resultDigest = result.Sha256,
            taskContractDigest = template.TaskDigest
        });
        var receipt = template with
        {
            ImplementerRunId = "implementation-1",
            Implementation = WriteEvidence("human-implementation.json", new
            {
                schemaVersion = 1, runId = "implementation-1", kind = "human-implemented", actor = "Synthetic protocol author",
                taskId = task.Id, taskContractDigest = template.TaskDigest, inputDigest = template.InputDigest, completedAtUtc = "2000-01-01T00:00:00Z"
            }),
            ReviewerRunId = "review-1",
            Gates = template.Gates.Select(gate => gate with
            {
                State = EngineeringGateState.Passed,
                Rationale = "Scoped protocol fixture verifies current closing evidence.",
                Artifacts = gate.Id == "review" ? [run, result] : [evidence]
            }).ToArray()
        };
        var receiptPath = services.Changes.DossierFile(change, $"verification/{task.Id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        File.WriteAllText(receiptPath, EngineeringCompletionReview.Serialize(receipt));
        var completed = services.Plans.TransitionTask(transition);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        Assert.Empty(services.Plans.TransitionTask(transition).Errors);
        File.AppendAllText(taskPath, "\nA new acceptance obligation requires fresh review.\n");
        Assert.Contains(services.Plans.TransitionTask(transition).Errors, error => error.Contains("stale", StringComparison.Ordinal));
        var refreshed = services.Plans.CompletionContext(repository.Path, change.Id, task.Id);
        Assert.Empty(refreshed.Errors);
        File.WriteAllText(receiptPath, EngineeringCompletionReview.Serialize(refreshed.Template! with
        {
            ImplementerRunId = receipt.ImplementerRunId,
            ReviewerRunId = receipt.ReviewerRunId,
            Gates = receipt.Gates
        }));
        Assert.Contains(services.Plans.TransitionTask(transition).Errors, error => error.Contains("Gate 'review'", StringComparison.Ordinal));

        EngineeringEvidenceArtifact WriteEvidence(string name, object content)
        {
            var relative = ".cis/local/engineering/" + name;
            repository.Write(relative, JsonSerializer.Serialize(content));
            return new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository.Path, relative)))));
        }
    }
}
