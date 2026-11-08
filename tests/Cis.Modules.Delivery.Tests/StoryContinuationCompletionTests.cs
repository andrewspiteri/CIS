using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void StoryContinuation_GovernedParticipantsCompleteWithNativeOptionalMinorReviewAndOneOwner()
    {
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        var repositories = new[] { new CisWorkspaceRepository("first", first.Path, "docs/cis"), new CisWorkspaceRepository("second", second.Path, "docs/cis") };
        foreach (var (repository, id) in new[] { (first, "first"), (second, "second") })
        {
            repository.Write(".cis/repository.yml", $"schema_version: 1\nrepository:\n  id: {id}\ndocumentation_root: docs/cis\n");
            repository.Write(CisEngineeringPolicy.RelativePath, """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
            repository.Write(".gitignore", ".cis/local/\n");
            repository.Write("app.txt", "original");
        }
        first.Write("docs/cis/changes/CIS-0001/proposal.md", "---\nchange_id: CIS-0001\ntitle: Preserve the bounded contract\noutcome: Both participants retain the exact accepted behavior.\nstatus: Approved\nbaseline_kind: graph\nbaseline: synthetic\ngraph_build_id: synthetic\n---\n# Synthetic governed proposal\n");
        first.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\n---\n| WORK-001 | documentation | medium | | | Preserve the bounded contract | CONTRACT-001 | | | Both participants retain the accepted behavior. | Independent review of app.txt. | Approved | core.documentation.contracts | 1.0 | none | | |\n");
        first.Write("docs/cis/changes/CIS-0001/impact.md", "---\nstatus: Accepted\n---\n| Finding | Status |\n|---|---|\n| IMPACT-1 | accepted |\n");
        first.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", "---\ntask_status: Ready\ntargets: [\"first\", \"second\"]\n---\n# Preserve the bounded contract\nBoth participants retain the accepted behavior.\n");
        foreach (var repository in new[] { first, second })
        {
            Git(repository.Path, "init"); Git(repository.Path, "add", ".");
            Git(repository.Path, "-c", "user.name=CIS Tests", "-c", "user.email=tests@example.test", "commit", "-m", "Synthetic governed baseline");
        }
        var plans = CreateServices().Plans;
        var ticks = DateTimeOffset.UtcNow.UtcTicks;
        var provider = new StoryCompletionProvider(() => ticks += TimeSpan.TicksPerSecond);
        var agent = new AgentService(new CisRepositoryContextResolver(), [provider], clock: () => new(ticks, TimeSpan.Zero),
            textGeneration: new StoryCompletionModels(), taskContracts: plans);
        var allocation = new CisAgentWorkAllocationRequest(180, 3, 5, [new("WORK-001", 180, 3, 5)],
            "fixture:approved-canonical-contract", "fixture:existing-local-storage-owner", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Fixture controller"] };
        first.Write(".cis/local/story-fixture/allocation.json", JsonSerializer.Serialize(allocation, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var allocated = agent.AllocateWork(first.Path, "CIS-0001", ".cis/local/story-fixture/allocation.json", "Fixture controller", "Exercise the existing approved owner across both participants.");
        Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
        var work = ClassifiedStoryWork(new(first.Path, "synthetic", "1234567890abcdef", "approved-contract", "Retain the bounded contract",
            new("T1", "Preserve the bounded contract", "The approved cross-repository outcome.", ["first", "second"], [1], ["Both participants retain the accepted behavior."], []),
            ["Both participants retain the accepted behavior."], repositories, "Fixture controller"));
        work = work with
        {
            Task = work.Task with
            {
                ChangeId = "CIS-0001",
                WorkOwnerTaskId = "WORK-001",
                InstanceKey = "synthetic/story/T1",
                ApprovalGate = "none",
                Targets = ["first", "second"],
                RequiredOutputs = ["app.txt"],
                VerificationObligations = [new("V1", work.Task.AcceptanceCriteria, "Verify the exact accepted cross-repository criterion", "review", ["Current independent review of both app.txt outputs"]),
                    new("V2", [work.Task.AcceptanceCriteria[0]], "Confirm the required output against the same accepted criterion", "review", ["The same independent review of both app.txt outputs"])]
            }
        };
        var selection = agent.SelectModels(work);
        Assert.Empty(selection.Errors);
        var executed = agent.Execute(work, selection, true, () => true, TestContext.Current.CancellationToken, null);
        Assert.True(executed.Applied, string.Join("; ", executed.Errors));
        Assert.Equal(4, provider.Calls);
        Assert.Equal(2, provider.Requests.Count(request => request.Mode == "implement"));
        Assert.Equal(2, provider.Requests.Count(request => request.Mode == "review"));
        Assert.All(provider.Requests, request =>
        {
            using var contract = JsonDocument.Parse(request.Prompt.Split("<approved-story-task>")[1].Split("</approved-story-task>")[0]);
            var task = contract.RootElement.GetProperty("task");
            Assert.Equal("documentation", task.GetProperty("category").GetString());
            Assert.Equal("medium", task.GetProperty("complexity").GetString());
            Assert.Equal("synthetic/story/T1", task.GetProperty("instanceKey").GetString());
            Assert.Equal("WORK-001", task.GetProperty("workOwnerTaskId").GetString());
            Assert.Equal("app.txt", Assert.Single(task.GetProperty("requiredOutputs").EnumerateArray()).GetString());
            Assert.Equal(work.Task.AcceptanceCriteria, task.GetProperty("acceptanceCriteria").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(2, task.GetProperty("verificationObligations").GetArrayLength());
            Assert.Equal("CIS-0001", request.Environment["CIS_WORK_CHANGE"]);
            Assert.Equal("WORK-001", request.Environment["CIS_WORK_TASK"]);
            Assert.False(request.ApproveWithinCeiling);
            if (request.Mode == "review")
            {
                Assert.True(request.TaskReview);
                Assert.Contains("Optional advice must not create compulsory corrections or questions", request.Prompt);
            }
        });
        Assert.All(executed.StructuredFindings!, finding => { Assert.Equal("minor", finding.Severity); Assert.Equal("optional", finding.Disposition); Assert.False(finding.RequiresDecision); });
        Assert.Empty(work.ReviewResponses);
        var control = agent.WorkStatus(first.Path, "CIS-0001", "WORK-001").Work!;
        Assert.Equal(4_000, control.Task!.ConsumedMilliseconds); Assert.Equal(control.Task.ConsumedMilliseconds, control.Change.ConsumedMilliseconds);
        Assert.Equal(0, control.Task.ReservedMilliseconds); Assert.Equal(1, control.Task.UsedAttempts); Assert.Empty(control.Corrections);
        Assert.All(control.Executions, execution => Assert.Equal("WORK-001", execution.OwnerTaskId));
        var completion = new StoryEngineeringCompletion(new CisRepositoryContextResolver(), new EngineeringAssessmentService(), new ClosingGraph(), [new ClosingAlignment()], plans);
        var prepared = completion.Assess(work, true);
        Assert.Equal(2, prepared.Count); Assert.All(prepared, context => Assert.Empty(context.Errors));
        foreach (var (context, repository) in prepared.Zip(repositories))
        {
            var receipt = JsonSerializer.Deserialize<EngineeringCompletionReceipt>(context.TemplateJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var implementation = executed.Runs.Single(run => run.RepositoryId == repository.Id && run.Stage == "Implementation");
            var review = executed.Runs.Single(run => run.RepositoryId == repository.Id && run.Stage == "Review");
            var implementationArtifact = Artifact(first.Path, $".cis/local/agents/runs/{implementation.RunId}/manifest.json");
            var reviewArtifacts = new[] { "manifest.json", "result.json" }.Select(name =>
            {
                var relative = $".cis/local/agents/runs/{review.RunId}/{name}";
                if (repository.RepositoryPath != first.Path)
                {
                    var copy = Path.Combine(repository.RepositoryPath, relative); Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(Path.Combine(first.Path, relative), copy);
                    Assert.Equal(File.ReadAllBytes(Path.Combine(first.Path, relative)), File.ReadAllBytes(copy));
                }
                return Artifact(repository.RepositoryPath, relative);
            }).ToArray();
            var reportPath = ".cis/local/story-fixture/context.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repository.RepositoryPath, reportPath))!);
            File.WriteAllText(Path.Combine(repository.RepositoryPath, reportPath), JsonSerializer.Serialize(new
            {
                receipt.InputDigest,
                receipt.GraphBuildId,
                receipt.RequirementIds,
                criteria = work.Task.AcceptanceCriteria,
                output = File.ReadAllText(Path.Combine(repository.RepositoryPath, "app.txt")),
                conclusion = "The existing adopted documentation gate inventory and exact participant output were assessed against the accepted contract."
            }));
            var report = Artifact(repository.RepositoryPath, reportPath);
            receipt = receipt with
            {
                ImplementerRunId = implementation.RunId,
                Implementation = implementationArtifact,
                ReviewerRunId = review.RunId,
                Gates = receipt.Gates.Select(gate => new EngineeringGateEvidence(gate.Id, EngineeringGateState.Passed,
                    "Synthetic protocol evidence verifies the current scoped documentation contract.", gate.Id == "review" ? reviewArtifacts : [report])).ToArray(),
                VerificationObligations = work.Task.VerificationObligations.Select(obligation => new EngineeringObligationEvidence(obligation.Id,
                    obligation.Criteria, EngineeringGateState.Passed, "The native independent review covers this exact accepted criterion.", reviewArtifacts)).ToArray()
            };
            var path = Path.Combine(repository.RepositoryPath, context.ReceiptPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, EngineeringCompletionReview.Serialize(receipt));
        }
        var completed = completion.Assess(work, false);
        Assert.All(completed, context => { Assert.Equal("required", context.AdoptionStatus); Assert.Empty(context.Errors); });
        var firstReceiptPath = Path.Combine(first.Path, prepared[0].ReceiptPath);
        var firstReceiptBytes = File.ReadAllBytes(firstReceiptPath);
        var firstReceipt = JsonSerializer.Deserialize<EngineeringCompletionReceipt>(firstReceiptBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        File.WriteAllText(firstReceiptPath, EngineeringCompletionReview.Serialize(firstReceipt with
        { Gates = firstReceipt.Gates.Where(gate => gate.Id != "review").ToArray() }));
        Assert.Contains(completion.Assess(work, false)[0].Errors, error => error.Contains("review", StringComparison.OrdinalIgnoreCase)
            && error.Contains("missing", StringComparison.OrdinalIgnoreCase));
        File.WriteAllBytes(firstReceiptPath, firstReceiptBytes);
        first.Write(".cis/local/story-fixture/progress.json", JsonSerializer.Serialize(new CisAgentWorkProgressRequest(
            new("Both scoped participant outputs and the independent optional-only review are current.", executed.Runs.Select(run => "agent-run:" + run.RunId).ToArray(),
                "Retain the accepted documentation contract and reuse its current review for both verification obligations.",
                "Accept the bounded result without a correction for optional future label advice.", "Complete the existing task assessment.")
            { CloseAttempt = false }, control.Revision),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var progress = agent.RecordWorkProgress(first.Path, "CIS-0001", "WORK-001", ".cis/local/story-fixture/progress.json", "Fixture controller");
        Assert.True(progress.Applied, string.Join("; ", progress.Errors));
        Assert.All(completion.Assess(work, false), context => Assert.Empty(context.Errors));
        Assert.Equal(4, provider.Calls);
        Assert.Equal(control.Task.ConsumedMilliseconds, agent.WorkStatus(first.Path, "CIS-0001", "WORK-001").Work!.Task!.ConsumedMilliseconds);

        static EngineeringEvidenceArtifact Artifact(string root, string relative)
            => new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relative)))));
        static void Git(string root, params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = root };
            start.ArgumentList.Add("-c"); start.ArgumentList.Add("safe.directory=" + root);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(20), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.ExitCode == 0, result.StandardError);
        }
    }

    private sealed class StoryCompletionModels : ICisTextGenerationService
    {
        public CisAiStatus GetStatus() => new([new("codex", "available", "fixture", true, [new("gpt-6-astra"), new("gpt-6-sol")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request) => throw new InvalidOperationException("Model discovery must not generate text.");
    }

    private sealed class StoryCompletionProvider(Action elapsed) : ICisAgentProvider
    {
        public int Calls { get; private set; }
        public List<CisAgentExecutionRequest> Requests { get; } = [];
        public CisAgentProviderDescriptor Descriptor => new("codex", "Synthetic story provider", "test", true, ["exec-json"], ["implement", "review"], ["read-only", "workspace-write"], false, false, "Protocol fixture only");
        public CisAgentProviderDiagnosis Diagnose(string repositoryPath) => new("codex", "ready", true, null, "test", true, [], []);
        public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
        {
            Calls++; Requests.Add(request); elapsed();
            if (request.Mode == "implement") File.WriteAllText(Path.Combine(request.WorkingDirectory, "app.txt"), "implemented");
            else Assert.Equal("implemented", File.ReadAllText(Path.Combine(request.WorkingDirectory, "app.txt")));
            var response = new
            {
                summary = "The exact accepted contract was checked.",
                changedFiles = request.Mode == "implement" ? new[] { "app.txt" } : [],
                validations = new[] { "The scoped app.txt criterion was verified." },
                evidence = new[] { "app.txt" },
                review = request.Mode == "review" ? new AgentBrdReview("ready", ["The accepted criterion is satisfied."],
                    [new("TASK-REV-001", "minor", "maintainability", "app.txt", "A future label could be shorter.", "Consider it in a separately authorized cleanup.")
                    { Disposition = "optional", BasisReferences = ["Optional preference beyond the accepted criterion"] }]) : null
            };
            return new(CisAgentRunStates.Succeeded, 0, "session-" + request.RunId, JsonSerializer.Serialize(response), [], [], [], null, null, null, null, []);
        }
    }
}
