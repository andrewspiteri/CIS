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
        var graph = new ClosingGraph(path => { if (path == second.Path) first.Write("Changed.cs", "class ConcurrentChange;"); });
        var contexts = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new ReviewOnlyAssessment(), graph, [new ClosingAlignment()]).Assess(work, false);
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
    [InlineData(false)]
    [InlineData(true)]
    public void StoryClosureRequiresEveryParticipantAndRejectsContractOrParticipantChanges(bool nativeImplementation)
    {
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        first.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        second.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var repositories = new[] { new CisWorkspaceRepository("first", first.Path, "docs/cis"), new CisWorkspaceRepository("second", second.Path, "docs/cis") };
        var work = new CisStoryTaskWork(first.Path, "synthetic", "1234567890abcdef", "approved-synthetic-contract", "Protocol qualification",
            new("T1", "Verify the shared contract", "Synthetic fixture", ["first", "second"], [1], ["Both participants agree"], []),
            ["Both participants agree"], repositories, "fixture");
        var service = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new ReviewOnlyAssessment(), new ClosingGraph(), [new ClosingAlignment()]);
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
            });
            var run = Write("run.json", new
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
                envelopeId = "story-envelope",
                resultDigest = result.Sha256
            });
            var nativeResult = Write("result.json", new { schemaVersion = 1, envelopeId = "implementation-envelope", status = CisAgentRunStates.Succeeded }, native: true);
            var nativeRun = Write("manifest.json", new
            {
                schemaVersion = 1, runId = "implementation-" + repository.Id, taskId = "T1-IMPLEMENT", taskContractDigest = receipt.TaskDigest,
                mode = "implement", status = CisAgentRunStates.Succeeded, outputDigest = receipt.InputDigest, targetRepositoryPath = repository.RepositoryPath,
                provider = "protocol-implementer", providerSessionId = "implementation-session", completedAtUtc = "2000-01-01T00:00:00Z",
                envelopeId = "implementation-envelope", resultDigest = nativeResult.Sha256[7..]
            }, native: true);
            receipt = receipt with
            {
                ImplementerRunId = "implementation-" + repository.Id,
                Implementation = nativeImplementation ? nativeRun : Write("human-implementation.json", new
                {
                    schemaVersion = 1, runId = "implementation-" + repository.Id, kind = "human-implemented", actor = "Synthetic protocol author",
                    taskId = receipt.TaskId, taskContractDigest = receipt.TaskDigest, inputDigest = receipt.InputDigest, completedAtUtc = "2000-01-01T00:00:00Z"
                }),
                ReviewerRunId = "review-" + repository.Id,
                Gates = [new("review", EngineeringGateState.Passed, "Synthetic protocol evidence, not a real provider approval.", [run, result])]
            };
            var path = Path.Combine(repository.RepositoryPath, context.ReceiptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, EngineeringCompletionReview.Serialize(receipt));

            EngineeringEvidenceArtifact Write(string name, object value, bool native = false)
            {
                var relative = native ? $".cis/local/agents/runs/implementation-{repository.Id}/{name}" : ".cis/local/story-fixture/" + name;
                var full = Path.Combine(native ? first.Path : repository.RepositoryPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, JsonSerializer.Serialize(value));
                return new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full))));
            }
        }
        Assert.All(service.Assess(work, false), context => Assert.Empty(context.Errors));
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

    private sealed class ReviewOnlyAssessment : ICisEngineeringAssessment
    {
        public EngineeringAssessment Assess(CisRepositoryContext context, string taskCategory)
            => new(1, true, [new("review", "Protocol fixture", false)], []);
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
