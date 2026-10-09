using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Cis.Modules.Plan;
using System.Security.Cryptography;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void NativeImplementationRetainsFinalSourceIdentityAndResultBinding()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        repository.CommitAll("Synthetic adopted implementation fixture");
        var task = new PlanWorkItem("WORK-090", "documentation", "low", null, null, "Native fixture", [], [], [], "Contract", "Native tests", "InProgress");
        var service = new AgentService(new CisRepositoryContextResolver(), [new FakeProvider(writeFile: true)], clock: Clock,
            taskContracts: new MutableTaskContract { Digest = EngineeringCompletionReview.TaskDigest(task) });
        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Implement,
            CisAgentPermissions.WorkspaceWrite, null, "fake-json", 60, false, "Fixture author", TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        var manifest = result.Run!.Manifest;
        Assert.NotEqual(manifest.InputDigest, manifest.OutputDigest);
        Assert.Equal(CisExecutionIdentity.Capture(new CisRepositoryContextResolver().Resolve(manifest.WorkingDirectory).Context!), manifest.OutputDigest);
        var resultPath = Path.Combine(repository.Path, AgentService.RootPath, "runs", manifest.RunId, "result.json");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(resultPath))), manifest.ResultDigest);
        Assert.Equal(EngineeringCompletionReview.TaskDigest(task), manifest.TaskContractDigest);
        // Isolate the real native producer/consumer contract; required gate enforcement is tested separately.
        var graph = new CisGraphMetadataReadResult("ready", 0, null, "fresh", new("graph-1", "fixture", null, true, "complete"), null, [], []);
        var assessment = new EngineeringAssessment(1, true, [], []);
        var policy = Path.Combine(repository.Path, ".cis/engineering-defaults.json");
        var relative = $"{AgentService.RootPath}/runs/{manifest.RunId}/manifest.json";
        var receipt = EngineeringCompletionReview.Template("CIS-0001", task, policy, graph, assessment, manifest.OutputDigest!) with
        {
            ImplementerRunId = manifest.RunId, ReviewerRunId = "separate-protocol-review",
            Implementation = new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository.Path, relative)))))
        };
        var receiptPath = Path.Combine(repository.Path, ".cis/local/native-contract.json");
        File.WriteAllText(receiptPath, EngineeringCompletionReview.Serialize(receipt));
        Assert.Empty(EngineeringCompletionReview.Review(repository.Path, receiptPath, "CIS-0001", task, policy, graph, assessment, manifest.OutputDigest!, "docs/cis"));
    }

    [Fact]
    public void AdoptedImplementationRequiresTaskContractBeforeProviderExecution()
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        repository.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);
        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Implement,
            CisAgentPermissions.ReadOnly, null, "fake-json", 60, false, "Fixture author", TestContext.Current.CancellationToken);
        Assert.Equal("blocked", result.Status);
        Assert.Equal(0, provider.ExecuteCalls);
    }

    [Fact]
    public void ReviewRejectsContentChangesToAnAlreadyDirtyFile()
    {
        using var repository = AgentRepository.Create(git: true);
        repository.AddEligibleChange();
        repository.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        repository.Write("Tracked.cs", "class Original;");
        repository.CommitAll("Synthetic review identity fixture");
        repository.Write("Tracked.cs", "class BeforeReview;");
        var before = repository.Git("status", "--porcelain");
        var provider = new FakeProvider(duringExecution: _ => repository.Write("Tracked.cs", "class DuringReview;"));
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock, taskContracts: new MutableTaskContract());
        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, "fake-json", 60, false, "Fixture reviewer", TestContext.Current.CancellationToken);
        Assert.Equal(before, repository.Git("status", "--porcelain"));
        Assert.Equal(CisAgentRunStates.InvalidEvidence, result.Run!.Manifest.Status);
        Assert.NotEqual(result.Run.Manifest.InputDigest, result.Run.Manifest.OutputDigest);
    }

    [Fact]
    public void ReviewRunRetainsPlanOwnedContractAndDoesNotRefreshItOnResume()
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        repository.Write(".cis/engineering-defaults.json", "{\"schemaVersion\":1,\"requireTaskCompletion\":true,\"additionalGates\":[]}");
        var task = new PlanWorkItem("WORK-090", "documentation", "low", null, null, "Review the task", [], [], [], "Verified behavior", "Native tests", "InProgress");
        var contracts = new MutableTaskContract { Digest = EngineeringCompletionReview.TaskDigest(task) };
        var service = new AgentService(new CisRepositoryContextResolver(), [new FakeProvider()],
            clock: Clock, taskContracts: contracts);
        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, "fake-json", 60, false, "Fixture reviewer",
            TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(EngineeringCompletionReview.TaskDigest(task), result.Run!.Manifest.TaskContractDigest);
        Assert.Equal(2, result.Run.Result!.SchemaVersion);
        Assert.Equal(CisAgentRunStates.Succeeded, result.Run.Result.Status);
        Assert.Equal("ready", result.Run.Result.Review!.Recommendation);
        var graph = new CisGraphMetadataReadResult("ready", 0, null, "fresh", new("graph-1", "fixture", null, true, "complete"), null, [], []);
        var policy = Path.Combine(repository.Path, ".cis/engineering-defaults.json");
        var assessment = new EngineeringAssessment(1, true, [new("review", "Independent task review", false)], []);
        var template = EngineeringCompletionReview.Template("CIS-0001", task, policy, graph, assessment, result.Run.Manifest.InputDigest!);
        EngineeringEvidenceArtifact Artifact(string name)
        {
            var relative = $"{AgentService.RootPath}/runs/{result.Run.Manifest.RunId}/{name}";
            return new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository.Path, relative)))));
        }
        var receiptPath = Path.Combine(repository.Path, ".cis/local/completion.json");
        repository.Write($"{AgentService.RootPath}/runs/{result.Run.Manifest.RunId}/human.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1, runId = "implementation-fixture", kind = "human-implemented", actor = "Synthetic protocol author",
            taskId = task.Id, taskContractDigest = template.TaskDigest, inputDigest = template.InputDigest, completedAtUtc = "2000-01-01T00:00:00Z"
        }));
        File.WriteAllText(receiptPath, EngineeringCompletionReview.Serialize(template with
        {
            ImplementerRunId = "implementation-fixture",
            Implementation = Artifact("human.json"),
            ReviewerRunId = result.Run.Manifest.RunId,
            Gates = [new("review", EngineeringGateState.Passed, "Native agent result and manifest verified.", [Artifact("manifest.json"), Artifact("result.json")])]
        }));
        Assert.Empty(EngineeringCompletionReview.Review(repository.Path, receiptPath, "CIS-0001", task, policy, graph, assessment, result.Run.Manifest.InputDigest!, "docs"));
        contracts.Digest = "sha256:changed-contract";
        var resumed = service.Resume(repository.Path, result.Run.Manifest.RunId, "Continue review", "Fixture reviewer",
            "Verify original run provenance", false, TestContext.Current.CancellationToken);
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(EngineeringCompletionReview.TaskDigest(task), resumed.Run!.Manifest.TaskContractDigest);
    }

    private sealed class MutableTaskContract : ICisEngineeringTaskContractReader
    {
        public string Digest { get; set; } = "sha256:original-contract";
        public string ReadTaskDigest(string repositoryPath, string changeId, string taskId) => Digest;
    }
}
