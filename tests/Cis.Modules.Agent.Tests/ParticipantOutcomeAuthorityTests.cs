using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Change;
using Cis.Modules.Decision;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void ParticipantOutcome_CompletesAfterUnrelatedApprovedAmendmentWithOriginalSnapshots()
    {
        using var fixture = new OutcomeParticipantFixture();
        var originalGrant = fixture.ReadChange("authorization.json");
        var originalProposal = fixture.ReadChange("proposal.md");
        var provider = new FakeProvider(duringExecution: _ =>
        {
            fixture.Amend("unrelated-scope");
            fixture.RefreshTaskView();
        });
        var service = fixture.Service(provider);

        var run = fixture.Run(service);

        Assert.True(run.ExitCode == 0, string.Join("; ", run.Diagnostics));
        Assert.Equal(CisAgentRunStates.Succeeded, run.Run!.Manifest.Status);
        Assert.NotEqual(originalGrant, fixture.ReadChange("authorization.json"));
        Assert.NotEqual(originalProposal, fixture.ReadChange("proposal.md"));
        Assert.Equal(originalGrant, Assert.Single(run.Envelope!.AuthorityArtifacts!, artifact => artifact.Path.EndsWith("/authorization.json", StringComparison.Ordinal)).Content);
        Assert.Equal(originalProposal, Assert.Single(run.Envelope.AuthorityArtifacts!, artifact => artifact.Path.EndsWith("/proposal.md", StringComparison.Ordinal)).Content);
        var originalTask = Assert.Single(run.Envelope.AuthorityArtifacts!, artifact => artifact.Path == run.Envelope.CanonicalTaskPath);
        Assert.Equal(run.Envelope.InstructionMarkdown, originalTask.Content);
        Assert.Equal(run.Envelope.CanonicalTaskDigest, originalTask.Sha256);
        Assert.NotEqual(originalTask.Content, fixture.Authority.Read(run.Envelope.CanonicalTaskPath));
        Assert.All(run.Envelope.AuthorityArtifacts!, artifact => Assert.Equal(ContextSha(artifact.Content), artifact.Sha256));
        Assert.All(run.Run.Artifacts, artifact => Assert.True(artifact.Valid));
        Assert.Single(CisOutcomeAuthority.Read(fixture.Context, OutcomeParticipantFixture.Change)!.History);
        Assert.Equal(0, service.WorkStatus(fixture.Authority.Path, OutcomeParticipantFixture.Change, fixture.TaskId).Work!.Change.ReservedMilliseconds);
    }

    [Fact]
    public void ParticipantOutcome_ResumesInterruptedRunAfterUnrelatedApprovedAmendmentWithoutReplacingContext()
    {
        using var fixture = new OutcomeParticipantFixture();
        var provider = new InterruptedParticipantProvider();
        var service = fixture.Service(provider);
        var first = fixture.Run(service);
        Assert.Equal(CisAgentRunStates.Interrupted, first.Run!.Manifest.Status);
        var envelopePath = fixture.EnvelopePath;
        var originalEnvelope = File.ReadAllBytes(envelopePath);
        var originalManifest = first.Run.Manifest;
        var originalGrant = fixture.ReadChange("authorization.json");
        fixture.Amend("unrelated-scope");

        // A different controller must use the retained authority identity, not capture a new one.
        var resumed = fixture.Service(provider).Resume(fixture.Authority.Path, originalManifest.RunId,
            "Continue the same bounded task.", OutcomeParticipantFixture.Actor, "Continue after an unrelated approved scope addition.",
            false, TestContext.Current.CancellationToken);

        Assert.True(resumed.ExitCode == 0, string.Join("; ", resumed.Diagnostics));
        Assert.Equal(CisAgentRunStates.Succeeded, resumed.Run!.Manifest.Status);
        Assert.Equal(2, resumed.Run.Manifest.Attempt);
        Assert.Equal(2, provider.Inner.ExecuteCalls);
        Assert.Equal(originalEnvelope, File.ReadAllBytes(envelopePath));
        Assert.Equal(originalManifest.EnvelopeDigest, resumed.Run.Manifest.EnvelopeDigest);
        Assert.Equal(originalManifest.AcceptedScopeDigest, resumed.Run.Manifest.AcceptedScopeDigest);
        Assert.Equal(originalManifest.AuthorityDossierDigest, resumed.Run.Manifest.AuthorityDossierDigest);
        Assert.Equal(originalGrant, Assert.Single(resumed.Envelope!.AuthorityArtifacts!, artifact => artifact.Path.EndsWith("/authorization.json", StringComparison.Ordinal)).Content);
        Assert.Contains(JsonSerializer.Serialize(originalGrant), provider.Inner.LastRequest!.Prompt, StringComparison.Ordinal);
        Assert.All(resumed.Run.Artifacts, artifact => Assert.True(artifact.Valid));
        var work = service.WorkStatus(fixture.Authority.Path, OutcomeParticipantFixture.Change, fixture.TaskId).Work!;
        Assert.Equal(300_000, work.Change.AllocatedMilliseconds);
        Assert.Equal(1, work.Task!.UsedAttempts);
        Assert.Single(work.Executions.Select(execution => execution.LogicalAttemptId).Distinct(StringComparer.Ordinal));
        Assert.Equal(0, work.Change.ReservedMilliseconds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParticipantOutcome_LargeUnrelatedAmendmentUsesCanonicalLimitsWithoutReplacingBoundSnapshots(bool duringRun)
    {
        using var fixture = new OutcomeParticipantFixture();
        var amended = false;
        var provider = new FakeProvider(duringExecution: _ =>
        {
            if (duringRun && !amended) { fixture.Amend("large-unrelated-scope"); amended = true; }
        });
        var service = fixture.Service(provider);
        var run = fixture.Run(service);
        Assert.True(run.ExitCode == 0, string.Join("; ", run.Diagnostics));
        var envelopeBytes = File.ReadAllBytes(fixture.EnvelopePath);
        if (!duringRun) fixture.Amend("large-unrelated-scope");
        Assert.True(Encoding.UTF8.GetByteCount(fixture.ReadChange("authorization.json")) > 256 * 1024);
        Assert.True(Encoding.UTF8.GetByteCount(fixture.ReadChange("proposal.md")) > 256 * 1024);
        Assert.All(run.Envelope!.AuthorityArtifacts!, artifact => Assert.True(Encoding.UTF8.GetByteCount(artifact.Content) <= 256 * 1024));

        var resumed = fixture.Service(provider).Resume(fixture.Authority.Path, run.Run!.Manifest.RunId, "Continue the same task.",
            OutcomeParticipantFixture.Actor, "The large amendment added only unrelated scope.", false, TestContext.Current.CancellationToken);

        Assert.True(resumed.ExitCode == 0, string.Join("; ", resumed.Diagnostics));
        Assert.Equal(CisAgentRunStates.Succeeded, resumed.Run!.Manifest.Status);
        Assert.Equal(2, provider.ExecuteCalls);
        Assert.Equal(envelopeBytes, File.ReadAllBytes(fixture.EnvelopePath));
        Assert.Equal(run.Run.Manifest.AuthorityDossierDigest, resumed.Run.Manifest.AuthorityDossierDigest);
    }

    [Theory]
    [InlineData("reviewer", true)]
    [InlineData("reviewer", false)]
    [InlineData("reason", true)]
    [InlineData("reason", false)]
    [InlineData("time", true)]
    [InlineData("time", false)]
    [InlineData("attribution", true)]
    [InlineData("attribution", false)]
    [InlineData("history-reason", true)]
    [InlineData("history-reason", false)]
    [InlineData("dropped-revision", true)]
    [InlineData("dropped-revision", false)]
    public void ParticipantOutcome_HoldsChangedOriginalApprovalProvenanceAndTruncatedLineage(string mutation, bool duringRun)
    {
        using var fixture = new OutcomeParticipantFixture();
        fixture.Amend("unrelated-scope");
        var provider = new FakeProvider(duringExecution: _ => { if (duringRun) fixture.TamperApproval(mutation); });
        var service = fixture.Service(provider);
        var run = fixture.Run(service);
        Assert.Equal(duringRun ? CisAgentRunStates.InvalidEvidence : CisAgentRunStates.Succeeded, run.Run!.Manifest.Status);
        if (!duringRun) fixture.TamperApproval(mutation);
        // These alterations retain a valid normalized request/digest. Scope-only
        // validation therefore cannot detect the lost human approval provenance.
        Assert.NotNull(CisOutcomeAuthority.Read(fixture.Context, OutcomeParticipantFixture.Change));

        var resumed = service.Resume(fixture.Authority.Path, run.Run.Manifest.RunId, "Continue.", OutcomeParticipantFixture.Actor,
            "Validate the original approval provenance.", false, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("Bound authority context", StringComparison.Ordinal));
        Assert.Equal(1, provider.ExecuteCalls);
    }

    [Theory]
    [InlineData("selected-scope", true)]
    [InlineData("selected-scope", false)]
    [InlineData("constraint", true)]
    [InlineData("constraint", false)]
    [InlineData("task-contract", true)]
    [InlineData("task-contract", false)]
    [InlineData("task-view", true)]
    [InlineData("task-view", false)]
    [InlineData("product-authority", true)]
    [InlineData("product-authority", false)]
    [InlineData("blocking-decision", true)]
    [InlineData("blocking-decision", false)]
    public void ParticipantOutcome_HoldsMaterialChangesDuringCompletionAndBeforeResume(string mutation, bool duringRun)
    {
        using var fixture = new OutcomeParticipantFixture();
        var provider = new FakeProvider(duringExecution: _ => { if (duringRun) fixture.Mutate(mutation); });
        var service = fixture.Service(provider);
        var run = fixture.Run(service);
        Assert.Equal(duringRun ? CisAgentRunStates.InvalidEvidence : CisAgentRunStates.Succeeded, run.Run!.Manifest.Status);
        if (!duringRun) fixture.Mutate(mutation);

        var resumed = service.Resume(fixture.Authority.Path, run.Run.Manifest.RunId, "Continue.", OutcomeParticipantFixture.Actor,
            "Check the selected material context.", false, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("authority context", StringComparison.Ordinal));
        Assert.Equal(1, provider.ExecuteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParticipantOutcome_RejectsRehashedTaskSnapshotEvenWithReboundEnvelopeInventory(bool rebindSnapshotDigest)
    {
        using var fixture = new OutcomeParticipantFixture();
        var provider = new FakeProvider();
        var service = fixture.Service(provider);
        var run = fixture.Run(service);
        Assert.Equal(CisAgentRunStates.Succeeded, run.Run!.Manifest.Status);
        var original = run.Envelope!;
        var artifacts = original.AuthorityArtifacts!.Select(artifact => artifact.Path == original.CanonicalTaskPath
            ? new AgentAuthorityArtifact(artifact.Path, ContextSha(artifact.Content + "\nInvented instruction."), artifact.Content + "\nInvented instruction.")
            : artifact).ToArray();
        var aggregate = ContextSha(string.Join("\n", artifacts.Select(artifact => artifact.Path + ":" + artifact.Sha256)));
        var tampered = original with { AuthorityArtifacts = artifacts, AcceptedScopeDigest = aggregate };
        File.WriteAllText(fixture.EnvelopePath, JsonSerializer.Serialize(tampered, JsonOptions));
        var runDirectory = Path.Combine(fixture.Authority.Path, AgentService.RootPath, "runs", run.Run.Manifest.RunId);
        var manifestPath = Path.Combine(runDirectory, "manifest.json");
        var manifest = run.Run.Manifest with { EnvelopeDigest = ContextSha(File.ReadAllText(fixture.EnvelopePath)) };
        if (rebindSnapshotDigest) manifest = manifest with { AcceptedScopeDigest = aggregate, ContextManifestDigest = aggregate };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        // Rebind the mutable inventory too: the rejection must reach the original
        // task/aggregate invariant, rather than only detect a stale outer hash.
        var inventoryPath = Path.Combine(runDirectory, "artifacts.json");
        var inventory = JsonNode.Parse(File.ReadAllText(inventoryPath))!.AsArray();
        foreach (var artifact in inventory)
            if (artifact!["path"]!.GetValue<string>() == "manifest.json") artifact["sha256"] = ContextSha(File.ReadAllText(manifestPath));
        File.WriteAllText(inventoryPath, inventory.ToJsonString(JsonOptions));
        fixture.Amend("unrelated-scope");

        var resumed = fixture.Service(provider).Resume(fixture.Authority.Path, run.Run.Manifest.RunId, "Continue.", OutcomeParticipantFixture.Actor,
            "Verify original participant snapshots.", false, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("Bound authority context", StringComparison.Ordinal));
        Assert.DoesNotContain(resumed.Diagnostics, message => message.Contains("run envelope is missing or changed", StringComparison.Ordinal)
            || message.Contains("inventory entry", StringComparison.Ordinal));
        Assert.Equal(1, provider.ExecuteCalls);
    }

    private static string ContextSha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class InterruptedParticipantProvider : ICisAgentProvider
    {
        public FakeProvider Inner { get; } = new();
        public CisAgentProviderDescriptor Descriptor => Inner.Descriptor;
        public CisAgentProviderDiagnosis Diagnose(string repositoryPath) => Inner.Diagnose(repositoryPath);
        public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
        {
            var result = Inner.Execute(request, onEvent, cancellationToken);
            return Inner.ExecuteCalls == 1 ? result with { Status = CisAgentRunStates.Interrupted } : result;
        }
    }

    private sealed class OutcomeParticipantFixture : IDisposable
    {
        public const string Change = "CIS-0002";
        public const string Actor = "Participant outcome controller";
        public AgentRepository Authority { get; } = AgentRepository.Create();
        private AgentRepository Participant { get; } = AgentRepository.Create();
        private CisRepositoryContextResolver Resolver { get; } = new();
        private ChangeDossierStore Changes { get; }
        private PlanningService Planner { get; }
        public CisRepositoryContext Context { get; }
        public string TaskId { get; }
        public string EnvelopePath => Path.Combine(Authority.Path, AgentService.RootPath, "envelopes", Change, TaskId + ".json");
        private string TaskPath { get; }

        public OutcomeParticipantFixture()
        {
            ConfigureParticipantTask(Authority, Participant);
            Authority.Write("src/X.cs", "class X { }");
            Authority.Write("src/Y.cs", "class Y { }");
            Authority.Write("tests/XTests.cs", "class XTests { }");
            Authority.Write("tests/YTests.cs", "class YTests { }");
            Authority.Write("config.json", "{}");
            var graph = new ProtectedEvidenceGraph(Authority.Path);
            Authority.Write(".cis/local/graph/manifest.json", JsonSerializer.Serialize(graph.Read(Authority.Path).Manifest, JsonOptions));
            Context = Resolver.Resolve(Authority.Path).Context!;
            Changes = new ChangeDossierStore(Resolver);
            var storage = new CisOwnedStoragePolicy(128 * 1024 * 1024, [new(Authority.Path, ".cis/local")]);
            var request = new CisOutcomeAuthorizationRequest("Deliver the neutral participant behavior.",
                [new("main", "The selected behavior.", ["src"])], ["Keep the work within the selected behavior."],
                300, 8, 30, storage, [Actor], true, true, false, false, "fixture:participant-outcome");
            var created = Changes.Create(new(Authority.Path, "Neutral participant behavior", request.Outcome, [], Change) { OutcomeAuthorization = request });
            Assert.True(created.ExitCode == 0, string.Join("; ", created.Errors));
            var authorized = Changes.Authorize(new(Authority.Path, Change, request, "Maintainer", "One synthetic bounded outcome decision."));
            Assert.True(authorized.ExitCode == 0, string.Join("; ", authorized.Errors));
            Planner = new PlanningService(Changes, new ImpactAnalysisService(Changes, new GraphQueryService(Resolver)),
                new DecisionService(Changes, Resolver), Resolver);
            var plan = Planner.Build(Authority.Path, Change);
            Assert.True(plan.ExitCode == 0, string.Join("; ", plan.Errors));
            var task = Assert.Single(plan.WorkItems);
            TaskId = task.Id;
            TaskPath = Path.Combine(Context.DocumentationRoot, "changes", Change, task.TaskPath!);
            var started = Planner.TransitionTask(new(Authority.Path, Change, TaskId, "InProgress", Actor, "Start the selected participant behavior."));
            Assert.True(started.ExitCode == 0, string.Join("; ", started.Errors));
            var allocation = new CisAgentWorkAllocationRequest(300, 8, 30, [new(TaskId, 240, 6, 30)],
                request.DecisionReference, "fixture:participant-storage", AcknowledgeHistoricalUnknown: true) { Storage = storage, Controllers = [Actor] };
            var allocated = Service(new FakeProvider()).AllocateWork(Authority.Path, Change,
                WriteWorkInput(Authority, "participant-allocation.json", allocation), Actor, "Distribute resources under the same bounded outcome.");
            Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
        }

        public AgentService Service(ICisAgentProvider provider, bool withContractReader = true)
            => new(Resolver, [provider], new WorkspaceRegistry(Resolver), clock: Clock, taskContracts: withContractReader ? Planner : null);
        public AgentResult Run(AgentService service) => service.Run(Authority.Path, Change, TaskId, "fake", CisAgentRunModes.Plan,
            CisAgentPermissions.ReadOnly, "participant", "fake-json", 30, false, Actor, TestContext.Current.CancellationToken);
        public string ReadChange(string name) => Authority.Read($"{Context.DocumentationRoot}/changes/{Change}/{name}");
        public void RefreshTaskView() => Authority.Write(TaskPath, Authority.Read(TaskPath) + "\n<!-- Refreshed derived-view formatting. -->\n");

        public void Amend(string kind)
        {
            var request = CisOutcomeAuthority.Read(Context, Change)!.Request;
            request = kind switch
            {
                "unrelated-scope" => request with { Scope = [.. request.Scope, new("support", "Separate supporting work.", ["support"])] },
                "large-unrelated-scope" => request with { Scope = [.. request.Scope, .. Enumerable.Range(0, 300)
                    .Select(index => new CisOutcomeScope("support-" + index, "Separate supporting behavior. " + new string('a', 900), ["support/" + index]))] },
                "selected-scope" => request with { Scope = request.Scope.Select(scope => scope with { Description = "A materially different selected behavior." }).ToArray() },
                "constraint" => request with { Constraints = [.. request.Constraints, "An additional material delivery restriction."] },
                "visual-ui" => request with { VisualUi = true },
                "no-visual-ui" => request with { VisualUi = false },
                _ => throw new InvalidOperationException(kind)
            };
            var amended = Changes.Authorize(new(Authority.Path, Change, request with { DecisionReference = "fixture:participant-amendment:" + kind },
                "Maintainer", "A specific synthetic amendment decision: " + kind));
            Assert.True(amended.ExitCode == 0, string.Join("; ", amended.Errors));
        }

        public void SetTaskCategory(string category)
        {
            var path = $"{Context.DocumentationRoot}/changes/{Change}/plan.json";
            var plan = JsonNode.Parse(Authority.Read(path))!;
            var task = Assert.Single(plan["workItems"]!.AsArray(), row => row!["id"]!.GetValue<string>() == TaskId)!;
            task["category"] = category;
            Authority.Write(path, plan.ToJsonString(JsonOptions));
            Authority.Write(TaskPath, Authority.Read(TaskPath).Replace("category: implementation", "category: " + category, StringComparison.Ordinal));
        }

        public void TamperApproval(string kind)
        {
            var current = CisOutcomeAuthority.Read(Context, Change)!;
            current = kind switch
            {
                "reviewer" => current with { Reviewer = "Another maintainer", History = current.History.Select(revision => revision with { Reviewer = "Another maintainer" }).ToArray() },
                "reason" => current with { Reason = "A replaced approval rationale." },
                "time" => current with { AuthorizedAtUtc = DateTimeOffset.Parse(current.AuthorizedAtUtc, System.Globalization.CultureInfo.InvariantCulture).AddSeconds(1).ToString("O") },
                "attribution" => current with { VisualUiDerivedFromAcceptedSource = !current.VisualUiDerivedFromAcceptedSource },
                "history-reason" => current with { History = current.History.Select(revision => revision with { Reason = "A replaced historical rationale." }).ToArray() },
                "dropped-revision" => current with { History = current.History.Skip(1).ToArray() },
                _ => throw new InvalidOperationException(kind)
            };
            Authority.Write($"{Context.DocumentationRoot}/changes/{Change}/authorization.json", JsonSerializer.Serialize(current, JsonOptions));
        }

        public void Mutate(string kind)
        {
            if (kind is "selected-scope" or "constraint") { Amend(kind); return; }
            if (kind == "product-authority") { Authority.Write("docs/cis/specs/business-requirements.md", "Changed applicable product authority."); return; }
            if (kind == "blocking-decision")
            {
                var decision = new DecisionService(Changes, Resolver).Create(new(Authority.Path, Change,
                    "Which selected behavior is permitted?", "scope", ["Keep the selected behavior", "Change the selected behavior"],
                    true, "implementation", "fixture:selected-scope-question"));
                Assert.True(decision.ExitCode == 0, string.Join("; ", decision.Errors));
                return;
            }
            const string replacement = "A materially different task-specific acceptance criterion.";
            var path = $"{Context.DocumentationRoot}/changes/{Change}/plan.json";
            var plan = JsonNode.Parse(Authority.Read(path))!;
            var task = Assert.Single(plan["workItems"]!.AsArray(), row => row!["id"]!.GetValue<string>() == TaskId)!;
            var previous = task["acceptanceCriteria"]!.GetValue<string>();
            if (kind == "task-contract")
            {
                task["acceptanceCriteria"] = replacement;
                Authority.Write(path, plan.ToJsonString(JsonOptions));
            }
            Authority.Write(TaskPath, Authority.Read(TaskPath).Replace(previous, replacement, StringComparison.Ordinal));
        }

        public void Dispose() { Participant.Dispose(); Authority.Dispose(); }
    }
}
