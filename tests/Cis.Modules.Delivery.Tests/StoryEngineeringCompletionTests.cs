using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void ConcurrentParticipantEditIsReportedOnItsExistingContext()
    {
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        foreach (var repository in new[] { first, second })
            repository.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var participants = new[] { new CisWorkspaceRepository("first", first.Path, "docs/cis"), new CisWorkspaceRepository("second", second.Path, "docs/cis") };
        var work = new CisStoryTaskWork(first.Path, "synthetic", "1234567890abcdef", "fixture", "Protocol",
            new("T1", "Task", "Fixture", ["first", "second"], [1], ["Criterion"], []), ["Criterion"], participants, "fixture");
        work = ClassifiedStoryWork(work);
        var graph = new ClosingGraph(path => { if (path == second.Path) first.Write("Changed.cs", "class ConcurrentChange;"); });
        var contexts = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new ReviewOnlyAssessment(), graph, [new ClosingAlignment()], CreateServices().Plans).Assess(work, false);
        Assert.Equal(2, contexts.Count);
        Assert.Contains(contexts[0].Errors, error => error.Contains("while other repositories", StringComparison.Ordinal));
    }

    [Fact]
    public void StoryClosureReportsNonAdoptionAndRejectsDependencyParticipants()
    {
        using var repo = TemporaryRepository.Create();
        repo.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":false}""");
        var participant = new CisWorkspaceRepository("example", repo.Path, "docs/cis");
        var work = new CisStoryTaskWork(repo.Path, "synthetic", "1234567890abcdef", "fixture", "Protocol",
            new("T1", "Task", "Fixture", ["example"], [1], ["Criterion"], []), ["Criterion"], [participant], "fixture");
        var service = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new EngineeringAssessmentService(), new ClosingGraph(), []);
        var context = Assert.Single(service.Assess(work, false));
        Assert.Equal("not-adopted", context.AdoptionStatus);
        Assert.Empty(context.Errors);
        var invalid = Assert.Single(service.Assess(work with { Repositories = [participant with { Participation = "dependency" }] }, false));
        Assert.Contains(invalid.Errors, error => error.Contains("not product-owned", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StoryClosureRequiresEveryParticipantAndRejectsContractOrParticipantChanges(bool nativeImplementation, bool codeTask)
    {
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        first.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        second.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var repositories = new[] { new CisWorkspaceRepository("first", first.Path, "docs/cis"), new CisWorkspaceRepository("second", second.Path, "docs/cis") };
        var work = new CisStoryTaskWork(first.Path, "synthetic", "1234567890abcdef", "approved-synthetic-contract", "Protocol qualification",
            new("T1", "Verify the shared contract", "Synthetic fixture", ["first", "second"], [1],
                ["Given an active registration\nWhen it is disposed\nThen it stops contributing", "Other registrations remain active"], []),
            ["Both participants agree"], repositories, "fixture");
        work = ClassifiedStoryWork(work);
        if (codeTask) work = work with
        {
            Task = work.Task with
            {
                Category = "security",
                TaskTypeKey = "core.security.permissions",
                VerificationObligations = work.Task.VerificationObligations.Select(obligation => obligation with { EvidenceRoute = "native", SharedGateIds = ["unit"] }).ToArray()
            }
        };
        var service = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new ReviewOnlyAssessment(codeTask), new ClosingGraph(), [new ClosingAlignment()], CreateServices().Plans);
        var prepared = service.Assess(work, true);
        Assert.Equal(2, prepared.Count);
        Assert.All(prepared, context => Assert.Empty(context.Errors));
        Assert.All(service.Assess(work, false), context => Assert.Contains(context.Errors, error => error.Contains("missing", StringComparison.Ordinal)));
        foreach (var (context, repository) in prepared.Zip(repositories))
        {
            var receipt = JsonSerializer.Deserialize<EngineeringCompletionReceipt>(context.TemplateJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var result = Write("result.json", new
            {
                schemaVersion = 2,
                envelopeId = "story-envelope",
                status = CisAgentRunStates.Succeeded,
                review = new { recommendation = "ready", findings = Array.Empty<object>() }
            }, review: true);
            var run = Write("manifest.json", new
            {
                schemaVersion = 1,
                runId = "review-" + repository.Id,
                startedAtUtc = "2000-01-02T00:00:00Z",
                inputDigest = receipt.InputDigest,
                taskContractDigest = receipt.TaskDigest,
                taskId = "T1-REVIEW",
                status = CisAgentRunStates.Succeeded,
                mode = "review",
                permission = "read-only",
                provider = "protocol-fixture",
                providerSessionId = "independent-review-session",
                envelopeId = "story-envelope",
                resultDigest = result.Sha256
            }, review: true);
            var nativeResult = Write("result.json", new { schemaVersion = 1, envelopeId = "implementation-envelope", status = CisAgentRunStates.Succeeded }, native: true);
            var nativeRun = Write("manifest.json", new
            {
                schemaVersion = 1,
                runId = "implementation-" + repository.Id,
                taskId = "T1-IMPLEMENT",
                taskContractDigest = receipt.TaskDigest,
                mode = "implement",
                status = CisAgentRunStates.Succeeded,
                outputDigest = receipt.InputDigest,
                targetRepositoryPath = repository.RepositoryPath,
                provider = "protocol-implementer",
                providerSessionId = "implementation-session",
                completedAtUtc = "2000-01-01T00:00:00Z",
                envelopeId = "implementation-envelope",
                resultDigest = nativeResult.Sha256[7..]
            }, native: true);
            var contractArtifact = Write("contract-artifact.json", new { criteria = work.Task.AcceptanceCriteria, conclusion = "The bounded documentation contract satisfies these criteria." });
            var unitEvidence = run;
            if (codeTask)
            {
                var nativeReport = "<TestRun><Results><UnitTestResult testName=\"shared-contract\" outcome=\"Passed\" /></Results></TestRun>";
                var reportPath = ".cis/local/story-fixture/native.trx";
                File.WriteAllText(Path.Combine(repository.RepositoryPath, reportPath), nativeReport);
                var report = new
                {
                    kind = "test-result",
                    path = reportPath,
                    digest = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nativeReport))),
                    bytes = System.Text.Encoding.UTF8.GetByteCount(nativeReport)
                };
                unitEvidence = Write("unit.json", new
                {
                    schemaVersion = 1,
                    inputDigest = receipt.InputDigest,
                    status = "passed",
                    suites = new[] { new { layer = "unit", status = "passed", total = 1, passed = 1, failed = 0, skipped = 0, artifacts = new[] { report } } },
                    artifacts = new[] { report }
                });
            }
            receipt = receipt with
            {
                ImplementerRunId = "implementation-" + repository.Id,
                Implementation = nativeImplementation ? nativeRun : Write("human-implementation.json", new
                {
                    schemaVersion = 1,
                    runId = "implementation-" + repository.Id,
                    kind = "human-implemented",
                    actor = "Synthetic protocol author",
                    taskId = receipt.TaskId,
                    taskContractDigest = receipt.TaskDigest,
                    inputDigest = receipt.InputDigest,
                    completedAtUtc = "2000-01-01T00:00:00Z"
                }),
                ReviewerRunId = "review-" + repository.Id,
                Gates = [new("review", EngineeringGateState.Passed, "Synthetic protocol evidence, not a real provider approval.", [run, result]),
                    .. codeTask ? new[] { new EngineeringGateEvidence("unit", EngineeringGateState.Passed, "Native protocol report covers the bounded criteria.", [unitEvidence]) } : []],
                VerificationObligations = work.Task.VerificationObligations.Select(obligation => new EngineeringObligationEvidence(
                    obligation.Id, obligation.Criteria, EngineeringGateState.Passed, "The independent protocol evidence covers these exact criteria.",
                    codeTask ? [unitEvidence] : obligation.EvidenceRoute == "artifact" ? [contractArtifact] : [run, result])).ToArray()
            };
            var path = Path.Combine(repository.RepositoryPath, context.ReceiptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, EngineeringCompletionReview.Serialize(receipt));

            EngineeringEvidenceArtifact Write(string name, object value, bool native = false, bool review = false)
            {
                var relative = native ? $".cis/local/agents/runs/implementation-{repository.Id}/{name}"
                    : review ? $".cis/local/agents/runs/review-{repository.Id}/{name}" : ".cis/local/story-fixture/" + name;
                var full = Path.Combine(native || review ? first.Path : repository.RepositoryPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, JsonSerializer.Serialize(value));
                if (review && repository.RepositoryPath != first.Path)
                {
                    // The authority retains the native original; this story route consumes a hash-verified participant copy.
                    var copy = Path.Combine(repository.RepositoryPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(full, copy);
                    Assert.Equal(File.ReadAllBytes(full), File.ReadAllBytes(copy));
                }
                return new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full))));
            }
        }
        Assert.All(service.Assess(work, false), context => Assert.Empty(context.Errors));
        var firstCriterionOnly = work with { Task = work.Task with { VerificationObligations = [work.Task.VerificationObligations[1]] } };
        Assert.Equal(work.Task.AcceptanceCriteria, firstCriterionOnly.Task.AcceptanceCriteria);
        Assert.Contains(service.Assess(firstCriterionOnly, false).SelectMany(context => context.Errors),
            error => error.Contains("no verification obligation: Other registrations remain active", StringComparison.Ordinal));
        const string participantReview = ".cis/local/agents/runs/review-second/manifest.json";
        var originalReview = File.ReadAllBytes(Path.Combine(first.Path, participantReview));
        File.Delete(Path.Combine(second.Path, participantReview));
        Assert.NotEmpty(service.Assess(work, false)[1].Errors);
        File.WriteAllBytes(Path.Combine(second.Path, participantReview), originalReview);
        Assert.All(service.Assess(work, false), context => Assert.Empty(context.Errors));
        var firstReceiptPath = Path.Combine(first.Path, prepared[0].ReceiptPath);
        var firstReceiptText = File.ReadAllText(firstReceiptPath);
        var firstReceipt = JsonSerializer.Deserialize<EngineeringCompletionReceipt>(firstReceiptText, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (codeTask)
        {
            var unitArtifact = firstReceipt.Gates.Single(gate => gate.Id == "unit").Artifacts.Single();
            var unitPath = Path.Combine(first.Path, unitArtifact.Path);
            var originalUnit = File.ReadAllText(unitPath);
            File.WriteAllText(unitPath, JsonSerializer.Serialize(new { schemaVersion = 1, inputDigest = firstReceipt.InputDigest, status = "passed", summary = "An unsupported model success claim" }));
            var rehashedClaim = unitArtifact with { Sha256 = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(unitPath))) };
            File.WriteAllText(firstReceiptPath, EngineeringCompletionReview.Serialize(firstReceipt with
            {
                Gates = firstReceipt.Gates.Select(gate => gate.Id == "unit" ? gate with { Artifacts = [rehashedClaim] } : gate).ToArray(),
                VerificationObligations = firstReceipt.VerificationObligations.Select(item => item with { Artifacts = [rehashedClaim] }).ToArray()
            }));
            Assert.Contains(service.Assess(work, false)[0].Errors, error => error.Contains("native test suites are missing", StringComparison.Ordinal));
            File.WriteAllText(unitPath, originalUnit);
        }
        File.WriteAllText(firstReceiptPath, EngineeringCompletionReview.Serialize(firstReceipt with
        { VerificationObligations = firstReceipt.VerificationObligations.Skip(1).ToArray() }));
        Assert.Contains(service.Assess(work, false)[0].Errors, error => error.Contains("no completion evidence", StringComparison.Ordinal));
        File.WriteAllText(firstReceiptPath, firstReceiptText);
        Assert.All(new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new ReviewOnlyAssessment(codeTask), new ClosingGraph(),
            [new ClosingAlignment()], CreateServices().Plans).Assess(work with { PlanHash = "unrelated-story-plan-revision" }, false), context => Assert.Empty(context.Errors));
        if (nativeImplementation)
        {
            const string relative = ".cis/local/agents/runs/implementation-second/manifest.json";
            var authorityRecord = Path.Combine(first.Path, relative);
            var original = File.ReadAllText(authorityRecord);
            foreach (var scenario in new[] { "wrong-target", "wrong-task", "wrong-location" })
            {
                var changedRecord = System.Text.Json.Nodes.JsonNode.Parse(original)!;
                if (scenario == "wrong-target") changedRecord["targetRepositoryPath"] = first.Path;
                if (scenario == "wrong-task") changedRecord["taskId"] = "T1";
                File.WriteAllText(authorityRecord, changedRecord.ToJsonString());
                // Keep the receipt hash current to exercise identity and location, not stale-hash rejection.
                var receiptPath = Path.Combine(second.Path, prepared[1].ReceiptPath);
                var receiptText = File.ReadAllText(receiptPath);
                var receiptNode = System.Text.Json.Nodes.JsonNode.Parse(receiptText)!;
                receiptNode["implementation"]!["sha256"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(authorityRecord)));
                File.WriteAllText(receiptPath, receiptNode.ToJsonString());
                if (scenario == "wrong-location")
                {
                    second.Write(relative, File.ReadAllText(authorityRecord));
                    File.Delete(authorityRecord);
                }
                var rejected = service.Assess(work, false);
                Assert.Empty(rejected[0].Errors);
                var expectedError = scenario switch
                {
                    "wrong-target" => "targets a different participant",
                    "wrong-task" => "does not identify the completed current task contract",
                    _ => "bounded regular repository artifacts"
                };
                Assert.Contains(rejected[1].Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
                if (scenario == "wrong-location") File.Delete(Path.Combine(second.Path, relative));
                File.WriteAllText(authorityRecord, original);
                File.WriteAllText(receiptPath, receiptText);
                Assert.All(service.Assess(work, false), context => Assert.Empty(context.Errors));
            }
        }
        Assert.All(service.Assess(work with { Constraints = "An additional contract obligation" }, false), context => Assert.NotEmpty(context.Errors));
        second.Write("Changed.cs", "class Changed;");
        var changed = service.Assess(work, false);
        Assert.Empty(changed[0].Errors);
        Assert.Contains(changed[1].Errors, error => error.Contains("stale", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("security", "core.security.permissions", true)]
    [InlineData("documentation", "core.documentation.contracts", false)]
    public void StoryCompletionUsesActualCategoryAndRejectsUnresolvedAdoptedContracts(string category, string type, bool requiresBuild)
    {
        using var repo = TemporaryRepository.Create();
        repo.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        repo.Write("package.json", """{"name":"fixture","scripts":{"build":"tsc"},"devDependencies":{"typescript":"5.0.0"}}""");
        repo.Write("app.ts", "export const value = 1;");
        var work = ClassifiedStoryWork(new(repo.Path, "synthetic", "1234567890abcdef", "fixture", "Protocol",
            new("T1", "Task", "Fixture", ["example"], [1], ["Criterion"], []), ["Criterion"],
            [new("example", repo.Path, "docs/cis")], "fixture"));
        work = work with { Task = work.Task with { Category = category, TaskTypeKey = type } };
        var service = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new EngineeringAssessmentService(), new ClosingGraph(), [], CreateServices().Plans);
        var prepared = Assert.Single(service.Assess(work, true));
        Assert.Empty(prepared.Errors);
        var receipt = JsonSerializer.Deserialize<EngineeringCompletionReceipt>(prepared.TemplateJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(requiresBuild, receipt.Gates.Any(gate => gate.Id == "build"));
        Assert.All(receipt.VerificationObligations, obligation => Assert.Equal(EngineeringGateState.Missing, obligation.State));
        var resolvedLegacy = Assert.Single(service.Assess(work with { Task = work.Task with { Category = null, ApprovalGate = null } }, true));
        Assert.Empty(resolvedLegacy.Errors);
        Assert.NotNull(resolvedLegacy.TemplateJson);
        var unassessed = Assert.Single(service.Assess(work with { Task = work.Task with { Complexity = null } }, true));
        Assert.Contains(unassessed.Errors, error => error.Contains("unresolved complexity", StringComparison.Ordinal));
        var unresolved = Assert.Single(service.Assess(work with { Task = work.Task with { Category = null, TaskTypeKey = null } }, true));
        Assert.Contains(unresolved.Errors, error => error.Contains("unresolved category", StringComparison.Ordinal));
        var oversized = Assert.Single(service.Assess(work with { Task = work.Task with { Complexity = "high" } }, true));
        Assert.Contains(oversized.Errors, error => error.Contains("decompose", StringComparison.Ordinal));
        var malformedTask = JsonSerializer.Deserialize<CisFeatureStoryTask>(JsonSerializer.Serialize(work.Task with
        { RequiredOutputs = null! }))!;
        var malformed = Assert.Single(service.Assess(work with { Task = malformedTask }, true));
        Assert.Contains(malformed.Errors, error => error.Contains("malformed required outputs", StringComparison.Ordinal));
        var malformedVerification = Assert.Single(service.Assess(work with
        { Task = work.Task with { VerificationObligations = null! } }, true));
        Assert.Contains(malformedVerification.Errors, error => error.Contains("verification obligations", StringComparison.Ordinal));
    }

    private static CisStoryTaskWork ClassifiedStoryWork(CisStoryTaskWork work) => work with
    {
        Task = work.Task with
        {
            Category = "documentation",
            Complexity = "medium",
            TaskTypeKey = "core.documentation.contracts",
            TaskTypeVersion = "1.0",
            RequiredOutputs = ["A reviewed contract artifact"],
            Validation = "Review the exact criteria against the resulting artifact.",
            VerificationObligations = [new("V1", work.Task.AcceptanceCriteria, "Validate the complete contract artifact", "artifact", ["Current bounded contract conformance artifact"]),
                new("V2", [work.Task.AcceptanceCriteria[0]], "Confirm the primary criterion", "review", ["The same review may cover this criterion"])]
        }
    };

    private sealed class ReviewOnlyAssessment(bool includeUnit = false) : ICisEngineeringAssessment
    {
        public EngineeringAssessment Assess(CisRepositoryContext context, string taskCategory)
            => new(1, true, [new("review", "Protocol fixture", false), .. includeUnit ? new[] { new EngineeringGateRequirement("unit", "Native protocol fixture", false) } : []], []);
    }
    private sealed class ClosingAlignment : ICisRepositoryDoctorCheck
    {
        public string Name => "engineering-alignment";
        public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context) => [];
    }
    private sealed class ClosingGraph(Action<string>? onRead = null) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath) => throw new NotSupportedException();
        public CisGraphMetadataReadResult ReadMetadata(string repositoryPath)
        {
            onRead?.Invoke(repositoryPath);
            return new("ready", 0, repositoryPath, "fresh", new("graph-1", "synthetic", null, true, "complete"), null, [], []);
        }
    }
}
