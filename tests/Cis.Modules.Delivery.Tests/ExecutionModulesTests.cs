using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Ai;
using Cis.Modules.Change;
using Cis.Modules.Diagnostics;
using Cis.Modules.Generate;
using Cis.Modules.Learn;
using Cis.Modules.Repository;
using Cis.Modules.Verify;
using Cis.Modules.Workflow;

namespace Cis.Modules.Delivery.Tests;

public sealed class ExecutionModulesTests
{
    [Fact]
    public void AiRoutes_AreLocalFirstCachedAndSanitized()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/references/ai-routing-profile.md", """
            | Capability | Provider | Model | Allow remote | Cache |
            |---|---|---|---|---|
            | summary | fake | small | no | yes |
            """);
        var generation = new FakeGeneration();
        var service = new AiGovernanceService(generation, new CisRepositoryContextResolver(), Clock);

        var first = service.Evaluate(repository.Path, "summary", "sensitive prompt", false, true);
        var second = service.Evaluate(repository.Path, "summary", "sensitive prompt", false, true);

        Assert.Equal("generated", first.Status);
        Assert.Equal("cached", second.Status);
        Assert.Equal(1, generation.Calls);
        var usage = File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "local", "ai", "usage.jsonl"));
        Assert.DoesNotContain("sensitive prompt", usage, StringComparison.Ordinal);
        Assert.Contains("promptHash", usage, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateRender_IsIdempotentAndRejectsRepositoryEscape()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/templates/greeting.md", "Hello {{name}}.\n");
        var service = new GenerateService(new CisRepositoryContextResolver(), Clock);

        var first = service.Render(repository.Path, "greeting.md", "generated/hello.md", ["name=Andrew"]);
        var second = service.Render(repository.Path, "greeting.md", "generated/hello.md", ["name=Andrew"]);
        var escaped = service.Render(repository.Path, "greeting.md", "../outside.md", ["name=Andrew"]);

        Assert.True(first.Applied);
        Assert.Equal("unchanged", second.Status);
        Assert.False(second.Applied);
        Assert.NotEqual(0, escaped.ExitCode);
        Assert.Equal("Hello Andrew.\n", File.ReadAllText(System.IO.Path.Combine(repository.Path, "generated", "hello.md")));
    }

    [Fact]
    public void WorkflowRun_CheckpointsAndResumesSuccessfulSteps()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/workflows/check.md", """
            | Step | Command | Depends on | Continue on failure | Timeout seconds |
            |---|---|---|---|---:|
            | version | dotnet --version | - | no | 30 |
            """);
        var service = new WorkflowService(new CisRepositoryContextResolver(), Clock);

        var first = service.Run(repository.Path, "check", "RUN-1");
        var second = service.Run(repository.Path, "check", "RUN-1");

        Assert.Equal("succeeded", first.Status);
        Assert.True(first.Applied);
        Assert.Equal("succeeded", second.Status);
        Assert.False(second.Applied);
        Assert.Equal("succeeded", Assert.Single(second.Run!.Steps).Status);
    }

    [Fact]
    public void AgentImport_IsDigestBoundAndAddsEvidenceWithoutCompletion()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", """
            ---
            task_status: Ready
            ---
            # Work
            Implement the bounded change.
            """);
        var service = new AgentService(new CisRepositoryContextResolver(), Clock);
        var prepared = service.Prepare(repository.Path, "CIS-0001", "WORK-001", "portable");
        var envelope = System.IO.Path.Combine(repository.Path, ".cis", "local", "agents", "envelopes", "CIS-0001", "WORK-001.json");
        var resultPath = System.IO.Path.Combine(repository.Path, "result.json");
        File.WriteAllText(resultPath, JsonSerializer.Serialize(new AgentResultDocument(1, prepared.Envelope!.Id, "completed",
            "Implemented and tested.", ["src/example.cs"], ["dotnet test: passed"], ["test log"]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var imported = service.ImportResult(repository.Path, envelope, resultPath);

        Assert.Equal("imported", imported.Status);
        var task = File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "changes", "CIS-0001", "agent-tasks", "WORK-001.md"));
        Assert.Contains("## Agent result imports", task, StringComparison.Ordinal);
        Assert.DoesNotContain("task_status: Complete", task, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentImport_AcceptsOnlyRegisteredWorkspaceQualifiedChangedFiles()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write(".cis/workspace.yml", "schema_version: 1\nrepositories:\n- id: api\n  path: ../api\n  documentation_root: docs/cis\n  role: participant\n");
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", "---\ntask_status: Ready\n---\n# Work\n");
        var service = new AgentService(new CisRepositoryContextResolver(), Clock);
        var prepared = service.Prepare(repository.Path, "CIS-0001", "WORK-001", "portable");
        var envelope = System.IO.Path.Combine(repository.Path, ".cis", "local", "agents", "envelopes", "CIS-0001", "WORK-001.json");
        var validPath = System.IO.Path.Combine(repository.Path, "valid-result.json");
        repository.Write("valid-result.json", JsonSerializer.Serialize(new AgentResultDocument(1, prepared.Envelope!.Id, "completed",
            "Changed a participant.", ["api::src/app.ts"], ["tests passed"], ["remote log"]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var imported = service.ImportResult(repository.Path, envelope, validPath);

        Assert.Equal("imported", imported.Status);

        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-002.md", "---\ntask_status: Ready\n---\n# Work\n");
        var second = service.Prepare(repository.Path, "CIS-0001", "WORK-002", "portable");
        repository.Write("invalid-result.json", JsonSerializer.Serialize(new AgentResultDocument(1, second.Envelope!.Id, "completed",
            "Unsafe participant.", ["unknown::src/app.ts"], [], []), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var rejected = service.ImportResult(repository.Path,
            System.IO.Path.Combine(repository.Path, ".cis", "local", "agents", "envelopes", "CIS-0001", "WORK-002.json"),
            System.IO.Path.Combine(repository.Path, "invalid-result.json"));

        Assert.Equal("rejected", rejected.Status);
        Assert.Contains(rejected.Diagnostics, item => item.Contains("workspace-qualified", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticsAndLearning_RequireProfilesAndHumanReview()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("logs/app.log", "INFO started\nERROR request timeout token=secret\n");
        repository.Write("docs/cis/references/diagnostics-profile.md", """
            | Source | Kind | Location | Enabled | Sensitive |
            |---|---|---|---|---|
            | app | text-log | logs/app.log | yes | no |
            """);
        var diagnostics = new DiagnosticsService(new CisRepositoryContextResolver(), Clock);
        var analysed = diagnostics.Analyse(repository.Path);
        Assert.Contains(analysed.Events, item => item.Severity == "error");
        Assert.DoesNotContain(analysed.Events, item => item.Message.Contains("secret", StringComparison.Ordinal));

        repository.Write(".cis/local/feedback/tool-usage.jsonl", "{\"status\":\"failed\"}\n");
        var learn = new LearnService(new CisRepositoryContextResolver(), Clock);
        Assert.Equal("collected", learn.Collect(repository.Path).Status);
        var proposed = learn.Propose(repository.Path);
        var proposal = proposed.Proposals.First();
        Assert.Equal("blocked", learn.Apply(repository.Path, proposal.Id).Status);
        Assert.Equal("reviewed", learn.Review(repository.Path, proposal.Id, "approve", "andrew", "Evidence supports the bounded improvement.").Status);
        Assert.Equal("applied", learn.Apply(repository.Path, proposal.Id).Status);
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "learning-history.md")));
    }

    [Fact]
    public void Verify_CapturesDiffValidatesAndRequiresHumanAcceptance()
    {
        using var repository = ExecutionRepository.Create(git: true);
        var baseline = repository.Head();
        repository.Write("docs/cis/changes/CIS-0001/proposal.md", $"""
            ---
            change_id: CIS-0001
            title: Verification fixture
            outcome: Verified delivery
            status: Active
            baseline_kind: git
            baseline: {baseline}
            graph_build_id: graph-1
            impact_roots: []
            ---
            # Proposal
            """);
        repository.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\n---\n# Plan\n");
        repository.Write("docs/cis/changes/CIS-0001/design.md", "---\napproval_status: Approved\n---\n# Design\n");
        repository.Write("docs/cis/changes/CIS-0001/verification.md", "---\nstatus: Draft\n---\n# Verification\n| Task ID | Check | Command or artifact | Result | Notes |\n|---|---|---|---|---|\n| WORK-001 | tests | `dotnet test` | Passed | exact fixture |\n");
        repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", "---\ntask_status: Complete\n---\n# Work\nTarget `src/example.txt`\n");
        repository.Write("src/example.txt", "changed\n");
        var changes = new ChangeDossierStore(new CisRepositoryContextResolver(), Clock);
        var service = new VerifyService(new CisRepositoryContextResolver(), changes, Clock);

        Assert.Equal("captured", service.Diff(repository.Path, "CIS-0001").Status);
        Assert.Equal(0, service.Validate(repository.Path, "CIS-0001").ExitCode);
        var accepted = service.Accept(repository.Path, "CIS-0001", "andrew", "All deterministic evidence passed.");
        Assert.Equal("accepted", accepted.Status);
        Assert.Contains("status: Accepted", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "changes", "CIS-0001", "verification.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_SkipsDesignGateForNonFrontendWorkAndDoesNotTreatContextAsTargets()
    {
        using var repository = ExecutionRepository.Create(git: true);
        var baseline = repository.Head();
        repository.Write("docs/cis/changes/CIS-0002/proposal.md", $"""
            ---
            change_id: CIS-0002
            title: Non-frontend verification fixture
            outcome: Verified delivery
            status: Active
            baseline_kind: git
            baseline: {baseline}
            graph_build_id: graph-1
            impact_roots: []
            ---
            # Proposal
            """);
        repository.Write("docs/cis/changes/CIS-0002/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: false\n---\n# Plan\n");
        repository.Write("docs/cis/changes/CIS-0002/design.md", "---\napproval_status: NotRequired\n---\n# Design\n");
        repository.Write("docs/cis/changes/CIS-0002/verification.md", "---\nstatus: Draft\n---\n# Verification\n| Task ID | Check | Command or artifact | Result | Notes |\n|---|---|---|---|---|\n| WORK-001 | tests | `dotnet test` | Passed | exact fixture |\n");
        repository.Write("docs/cis/changes/CIS-0002/agent-tasks/WORK-001.md", "---\ntask_status: Complete\n---\n# Work\nTarget `src/example.txt`\n\n## Context and evidence\n\n- Governed reference: `docs/specs/business-requirements.md`.\n- Governed reference: `https://example.test/reference`.\n");
        repository.Write("src/example.txt", "changed\n");
        var changes = new ChangeDossierStore(new CisRepositoryContextResolver(), Clock);
        var service = new VerifyService(new CisRepositoryContextResolver(), changes, Clock);

        Assert.Equal("captured", service.Diff(repository.Path, "CIS-0002").Status);
        var compared = service.Compare(repository.Path, "CIS-0002");
        Assert.DoesNotContain(compared.Findings, item => item.Code == "CIS-VERIFY-PLANNED-MISSING" && item.Path is not null && (item.Path.Contains("business-requirements") || item.Path.StartsWith("https://", StringComparison.Ordinal)));
        Assert.Equal(0, service.Validate(repository.Path, "CIS-0002").ExitCode);

        repository.Write("docs/cis/changes/CIS-0002/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: true\n---\n# Plan\n");
        Assert.Contains(service.Validate(repository.Path, "CIS-0002").Findings, item => item.Code == "CIS-VERIFY-DESIGN");
    }

    [Fact]
    public void Verify_AcceptsApprovedGatesDecomposedCoordinationAndReadyFinalSweep()
    {
        using var repository = ExecutionRepository.Create(git: true);
        var baseline = repository.Head();
        repository.Write("docs/cis/changes/CIS-0003/proposal.md", $"""
            ---
            change_id: CIS-0003
            title: Lifecycle verification fixture
            outcome: Verified delivery
            status: Active
            baseline_kind: git
            baseline: {baseline}
            graph_build_id: graph-1
            impact_roots: []
            ---
            # Proposal
            """);
        repository.Write("docs/cis/changes/CIS-0003/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: true\n---\n# Plan\n");
        repository.Write("docs/cis/changes/CIS-0003/design.md", "---\napproval_status: Approved\n---\n# Design\n");
        repository.Write("docs/cis/changes/CIS-0003/verification.md", "---\nstatus: Draft\n---\n# Verification\n| Task ID | Check | Command or artifact | Result | Notes |\n|---|---|---|---|---|\n| WORK-180 | sweep | `cis verify validate` | Passed | exact fixture |\n");
        repository.Write("docs/cis/changes/CIS-0003/agent-tasks/WORK-000.md", "---\r\ntask_status: Decomposed\r\ncategory: coordination\r\n---\r\n# Coordination\r\n");
        repository.Write("docs/cis/changes/CIS-0003/agent-tasks/WORK-010.md", "---\r\ntask_status: Approved\r\ncategory: wireframe\r\n---\r\n# Wireframe\r\n");
        repository.Write("docs/cis/changes/CIS-0003/agent-tasks/WORK-020.md", "---\r\ntask_status: Approved\r\ncategory: design\r\n---\r\n# Design\r\n");
        repository.Write("docs/cis/changes/CIS-0003/agent-tasks/WORK-160.md", "---\r\ntask_status: Complete\r\ncategory: verification\r\n---\r\n# Verification\r\n");
        repository.Write("docs/cis/changes/CIS-0003/agent-tasks/WORK-180.md", "---\r\ntask_status: InProgress\r\ntask_type: core.delivery.final-sweep\r\ncategory: delivery\r\n---\r\n# Final sweep\r\n- [x] Reconciled.\r\n| Check | Result |\r\n|---|---|\r\n| completion | Passed |\r\n");
        var changes = new ChangeDossierStore(new CisRepositoryContextResolver(), Clock);
        var service = new VerifyService(new CisRepositoryContextResolver(), changes, Clock);

        Assert.Equal("captured", service.Diff(repository.Path, "CIS-0003").Status);
        Assert.Equal(0, service.Validate(repository.Path, "CIS-0003").ExitCode);

        repository.Write("docs/cis/changes/CIS-0003/agent-tasks/WORK-180.md", "---\ntask_status: InProgress\ntask_type: core.delivery.final-sweep\ncategory: delivery\n---\n# Final sweep\n- [ ] Reconcile.\n| Check | Result |\n|---|---|\n| completion | Passed |\n");
        Assert.Contains(service.Validate(repository.Path, "CIS-0003").Findings, item => item.Code == "CIS-VERIFY-TASK" && item.Path!.EndsWith("WORK-180.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_CapturesWorkspaceRepositoriesAndUntrackedFiles()
    {
        using var authority = ExecutionRepository.Create(git: true, id: "authority");
        using var participant = ExecutionRepository.Create(git: true, id: "participant");
        var authorityBaseline = authority.Head();
        var participantBaseline = participant.Head();
        authority.Write(".cis/workspace.yml", $"""
            schema_version: 1
            repositories:
            - id: authority
              path: .
              documentation_root: docs/cis
              role: authority
            - id: participant
              path: "{participant.Path.Replace('\\', '/')}"
              documentation_root: docs/cis
              role: participant
            """);
        var baselines = JsonSerializer.Serialize(new[]
        {
            new ChangeRepositoryBaseline("authority", "git", authorityBaseline),
            new ChangeRepositoryBaseline("participant", "git", participantBaseline),
        });
        var baselineMetadata = JsonSerializer.Serialize(baselines);
        authority.Write("docs/cis/changes/CIS-0004/proposal.md", $"""
            ---
            change_id: CIS-0004
            title: Workspace verification fixture
            outcome: Verified workspace delivery
            status: Active
            baseline_kind: git
            baseline: {authorityBaseline}
            graph_build_id: graph-1
            impact_roots: []
            repository_baselines: {baselineMetadata}
            ---
            # Proposal
            """);
        authority.Write("docs/cis/changes/CIS-0004/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: false\n---\n# Plan\n");
        authority.Write("docs/cis/changes/CIS-0004/verification.md", "---\nstatus: Draft\n---\n| Task | Check | Artifact | Result | Notes |\n|---|---|---|---|---|\n| WORK-001 | tests | `test` | Passed | exact |\n");
        authority.Write("docs/cis/changes/CIS-0004/agent-tasks/WORK-001.md", "---\ntask_status: Complete\ntargets: [\"participant\"]\n---\n# Task\n");
        participant.Write("src/untracked.cs", "namespace Example;\n");
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        var changes = new ChangeDossierStore(resolver, Clock, workspaceRegistry: registry);
        var service = new VerifyService(resolver, changes, Clock, registry);

        var result = service.Diff(authority.Path, "CIS-0004");

        Assert.True(result.ExitCode == 0, string.Join(" | ", result.Findings.Select(item => $"{item.Code}:{item.Message}")));
        Assert.Equal(2, result.Snapshot!.Repositories!.Count);
        Assert.Contains(result.Snapshot.Files, item => item.RepositoryId == "participant" && item.Status == "??" && item.Path == "src/untracked.cs");
        Assert.DoesNotContain(result.Findings, item => item.Code == "CIS-VERIFY-BASELINE-FALLBACK");
    }

    [Fact]
    public void Verify_RejectsEmptySnapshotAcceptance()
    {
        using var repository = ExecutionRepository.Create(git: true);
        var baseline = repository.Head();
        repository.Write("docs/cis/changes/CIS-0005/proposal.md", $"---\nchange_id: CIS-0005\ntitle: Empty\noutcome: Empty\nstatus: Active\nbaseline_kind: git\nbaseline: {baseline}\ngraph_build_id: graph-1\nimpact_roots: []\n---\n");
        repository.Write("docs/cis/changes/CIS-0005/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: false\n---\n");
        repository.Write("docs/cis/changes/CIS-0005/verification.md", "---\nstatus: Draft\n---\n| Task | Check | Artifact | Result | Notes |\n|---|---|---|---|---|\n| W | test | `x` | Passed | x |\n");
        repository.Write("docs/cis/changes/CIS-0005/agent-tasks/WORK-001.md", "---\ntask_status: Complete\n---\n");
        var changes = new ChangeDossierStore(new CisRepositoryContextResolver(), Clock);
        var service = new VerifyService(new CisRepositoryContextResolver(), changes, Clock);
        Assert.Equal(0, service.Diff(repository.Path, "CIS-0005").ExitCode);
        var snapshotPath = System.IO.Path.Combine(repository.Path, ".cis", "local", "verify", "CIS-0005", "snapshot.json");
        File.WriteAllText(snapshotPath, JsonSerializer.Serialize(new VerifySnapshot(2, "CIS-0005", baseline, Clock().ToString("O"), [],
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", [])));

        var result = service.Accept(repository.Path, "CIS-0005", "Andrew", "accepted");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Findings, item => item.Code == "CIS-VERIFY-EMPTY");
        Assert.Contains("status: Draft", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "changes", "CIS-0005", "verification.md")));
    }

    private static DateTimeOffset Clock() => DateTimeOffset.Parse("2026-08-14T12:00:00Z");

    private sealed class FakeGeneration : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public CisAiStatus GetStatus() => new([new("fake", "available", "local", true, [new("small")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request) { Calls++; return new("generated", "fake", "small", "short result", null, true); }
    }

    private sealed class ExecutionRepository : IDisposable
    {
        public string Path { get; }
        private ExecutionRepository(string path) => Path = path;
        public static ExecutionRepository Create(bool git = false, string id = "execution-fixture")
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-execution-tests", Guid.NewGuid().ToString("N"));
            var repository = new ExecutionRepository(path);
            repository.Write(".cis/repository.yml", $"schema_version: 1\nrepository:\n  id: {id}\ndocumentation_root: docs/cis\n");
            repository.Write("docs/cis/catalog.yml", $"schema_version: 1\nrepository: {id}\ndocuments: []\n");
            if (git)
            {
                repository.Write("src/example.txt", "baseline\n");
                repository.Git("init"); repository.Git("config", "user.email", "test@example.test"); repository.Git("config", "user.name", "CIS Tests");
                repository.Git("add", "."); repository.Git("commit", "-m", "baseline");
            }
            return repository;
        }
        public void Write(string relative, string content)
        { var file = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!); File.WriteAllText(file, content); }
        public string Head() => Git("rev-parse", "HEAD").Trim();
        private string Git(params string[] arguments)
        {
            using var process = new Process { StartInfo = new("git") { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument); process.Start(); var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error); return output;
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
