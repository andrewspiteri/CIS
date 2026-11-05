using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Plan;
using Cis.Modules.Repository;
using Cis.Providers.Agent.Claude;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("ready", "optional", true, true, null)]
    [InlineData("revise", "required", true, false, null)]
    [InlineData("ready", "required", false, false, null)]
    [InlineData("revise", "optional", false, false, null)]
    [InlineData("ready", "unknown", false, false, null)]
    [InlineData("ready", null, false, false, null)]
    [InlineData("blocked", "required", true, false, null)]
    [InlineData("blocked", "required", true, false, true)]
    [InlineData("ready", "optional", false, false, true)]
    [InlineData("revise", "required", false, false, true)]
    public void TaskReviewContract_PreservesMinorDispositionThroughNativeReviewAndCompletion(string verdict, string? disposition,
        bool valid, bool canComplete, bool? requiresDecision)
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        repository.CommitAll("Synthetic task review contract fixture");
        var task = new PlanWorkItem("WORK-090", "documentation", "low", null, null, "Review the scoped task", [], [], [],
            "Preserve the accepted behavior", "Inspect current native evidence", "InProgress");
        var provider = new TaskContractReviewProvider(verdict, disposition, necessity: canComplete, requiresDecision: requiresDecision);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock,
            taskContracts: new MutableTaskContract { Digest = EngineeringCompletionReview.TaskDigest(task) });
        AllocateWorkFixture(repository, service);
        var reviewed = service.Run(repository.Path, "CIS-0001", task.Id, "fake", CisAgentRunModes.Review, CisAgentPermissions.ReadOnly,
            null, "fake-json", 60, false, "Fixture independent reviewer", TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.Calls);
        Assert.Contains("disposition", provider.Request!.Prompt);
        Assert.Contains("smallest capable alternative", provider.Request.Prompt);
        Assert.Equal(valid ? CisAgentRunStates.Succeeded : CisAgentRunStates.InvalidEvidence, reviewed.Run!.Manifest.Status);
        if (!valid)
        {
            Assert.Contains(reviewed.Diagnostics, message => message.Contains("Task review evidence is invalid or inconsistent", StringComparison.Ordinal));
            return;
        }
        var review = reviewed.Run.Result!.Review!;
        Assert.Equal(2, review.ContractVersion);
        Assert.Equal(disposition, Assert.Single(review.Findings).Disposition);
        Assert.Equal(requiresDecision, Assert.Single(review.Findings).RequiresDecision);
        Assert.Equal(canComplete, review.NecessityAssessment is not null);
        var manifest = reviewed.Run.Manifest;
        var graph = new CisGraphMetadataReadResult("ready", 0, null, "fresh", new("graph-1", "fixture", null, true, "complete"), null, [], []);
        var policy = Path.Combine(repository.Path, ".cis/engineering-defaults.json");
        var assessment = new EngineeringAssessment(1, true, [new("review", "Current independent task review", false)], []);
        var receipt = EngineeringCompletionReview.Template("CIS-0001", task, policy, graph, assessment, manifest.InputDigest!);
        var runRoot = $"{AgentService.RootPath}/runs/{manifest.RunId}/";
        repository.Write(runRoot + "human.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runId = "implementation-fixture",
            kind = "human-implemented",
            actor = "Synthetic protocol author",
            taskId = task.Id,
            taskContractDigest = receipt.TaskDigest,
            inputDigest = receipt.InputDigest,
            completedAtUtc = "2000-01-01T00:00:00Z"
        }));
        receipt = receipt with
        {
            ImplementerRunId = "implementation-fixture",
            Implementation = Artifact("human.json"),
            ReviewerRunId = manifest.RunId,
            Gates = [new("review", EngineeringGateState.Passed, "Current native review result is independently bound.", [Artifact("manifest.json"), Artifact("result.json")])]
        };
        var receiptPath = Path.Combine(repository.Path, ".cis/local/task-review-completion.json");
        File.WriteAllText(receiptPath, EngineeringCompletionReview.Serialize(receipt));
        var errors = EngineeringCompletionReview.Review(repository.Path, receiptPath, "CIS-0001", task, policy, graph, assessment, manifest.InputDigest!, "docs/cis");
        Assert.Equal(canComplete, errors.Count == 0);
        EngineeringEvidenceArtifact Artifact(string name)
        {
            var path = runRoot + name;
            return new(path, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository.Path, path)))));
        }
    }

    [Fact]
    public void TaskReviewContract_ClaudeSchemaIsExplicitAndKeepsBrdAndOrdinaryReviewCompatible()
    {
        JsonElement Schema(bool task)
        {
            var request = Request("C:/bounded-review", approve: false) with { Mode = "review", Permission = "read-only", TaskReview = task };
            var start = new ProcessStartInfo("claude");
            ClaudeAgentProvider.ConfigureExecutionArguments(start, request);
            using var document = JsonDocument.Parse(start.ArgumentList[start.ArgumentList.IndexOf("--json-schema") + 1]);
            return document.RootElement.Clone();
        }
        var task = Schema(true).GetProperty("properties").GetProperty("review");
        var finding = task.GetProperty("properties").GetProperty("findings").GetProperty("items");
        Assert.Contains(finding.GetProperty("required").EnumerateArray(), value => value.GetString() == "disposition");
        Assert.Contains(finding.GetProperty("required").EnumerateArray(), value => value.GetString() == "basisReferences");
        Assert.Equal("boolean", finding.GetProperty("properties").GetProperty("requiresDecision").GetProperty("type").GetString());
        Assert.DoesNotContain(finding.GetProperty("required").EnumerateArray(), value => value.GetString() == "requiresDecision");
        Assert.True(task.GetProperty("properties").TryGetProperty("necessityAssessment", out _));
        Assert.DoesNotContain(task.GetProperty("required").EnumerateArray(), value => value.GetString() == "necessityAssessment");
        var brdFinding = Schema(false).GetProperty("properties").GetProperty("review").GetProperty("properties").GetProperty("findings").GetProperty("items");
        Assert.DoesNotContain(brdFinding.GetProperty("required").EnumerateArray(), value => value.GetString() == "disposition");
        Assert.False(brdFinding.GetProperty("properties").TryGetProperty("requiresDecision", out _));
        Assert.Equal("^BRD-REV-[0-9]{3}$", brdFinding.GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString());
        Assert.True(CisTaskReviewSemantics.TryDisposition("minor", null, false, out var historicalRequired));
        Assert.Equal("required", historicalRequired);
        Assert.True(CisTaskReviewSemantics.TryDisposition("observation", null, false, out var historicalOptional));
        Assert.Equal("optional", historicalOptional);
        Assert.DoesNotContain("requiresDecision", JsonSerializer.Serialize(new AgentBrdReviewFinding("BRD-REV-001", "minor", "scope", "section", "Observation", "Recommendation"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
    }

    private sealed class TaskContractReviewProvider(string verdict, string? disposition, bool necessity, bool? requiresDecision) : ICisAgentProvider
    {
        public int Calls { get; private set; }
        public CisAgentExecutionRequest? Request { get; private set; }
        public CisAgentProviderDescriptor Descriptor { get; } = new("fake", "Task review fixture", "test", true, ["fake-json"],
            [CisAgentRunModes.Review], [CisAgentPermissions.ReadOnly], true, false, "Synthetic protocol fixture");
        public CisAgentProviderDiagnosis Diagnose(string repositoryPath) => new("fake", "ready", true, "fake", "1.0", true, [], []);
        public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
        {
            Calls++; Request = request;
            onEvent(new("provider-event", "Reviewed scoped task", "fixture/review", "review-session"));
            var finding = new Dictionary<string, object?>
            {
                ["id"] = "TASK-REV-001",
                ["severity"] = "minor",
                ["category"] = "scope",
                ["location"] = "task artifact",
                ["observation"] = "A bounded review observation.",
                ["recommendation"] = "Retain the accepted behavior.",
                ["basisReferences"] = new[] { "Accepted task completion criterion" }
            };
            if (disposition is not null) finding["disposition"] = disposition;
            if (requiresDecision is not null)
            {
                finding["requiresDecision"] = requiresDecision;
                finding["observation"] = "The accepted task retains an explicit human choice that must be resolved.";
                finding["recommendation"] = "Resolve that existing human decision without inventing a product default.";
            }
            var review = new Dictionary<string, object?> { ["recommendation"] = verdict, ["strengths"] = new[] { "Bounded task scope." }, ["findings"] = new[] { finding } };
            if (necessity) review["necessityAssessment"] = new CisReviewNecessityAssessment("Preserve the concrete acceptance invariant.",
                "Existing native assertions suffice; prefer the smallest adapter.", "Runtime is unknown; no benchmark was added.",
                "Keep one maintained adapter without changing pass semantics.", "Avoid a second comparator and duplicate verification.",
                "Simplify within the accepted scope using existing native evidence.");
            var json = JsonSerializer.Serialize(new
            {
                summary = "Independent task review completed.",
                changedFiles = Array.Empty<string>(),
                validations = new[] { "Inspected the accepted task and current evidence." },
                evidence = Array.Empty<string>(),
                review
            },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new(CisAgentRunStates.Succeeded, 0, "review-session", json, [], [], [], null, null, null, null, []);
        }
    }
}
