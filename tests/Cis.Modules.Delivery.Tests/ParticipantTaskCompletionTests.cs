using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void ParticipantCompletionUsesEveryDeclaredTargetAndRetainsAuthorityProvenance()
    {
        using var fixture = new ParticipantClosureFixture();
        Assert.Null(fixture.Service.Prepare(fixture.Change, fixture.Task, null, fixture.Document, null).Template);
        foreach (var id in new[] { "first", "second" })
        {
            var template = fixture.Prepare(id);
            Assert.Equal(id, template.RepositoryId);
            Assert.Equal($"docs/cis/changes/CIS-0001/verification/WORK-030/{id}.json", template.ReceiptPath);
            Assert.Equal(CisExecutionIdentity.Capture(fixture.Context(id)), template.InputDigest);
            Assert.NotEqual(CisExecutionIdentity.Capture(fixture.Context("authority")), template.InputDigest);
            fixture.WriteReceipt(id);
        }
        Assert.Empty(fixture.Review());
        File.Delete(Path.Combine(fixture.Authority.Path, fixture.Prepare("second").ReceiptPath!));
        Assert.Contains(fixture.Review(), error => error.StartsWith("second:", StringComparison.Ordinal) && error.Contains("missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("participant-source", "stale")]
    [InlineData("authority-source", "authority context")]
    [InlineData("task-contract", "contract")]
    [InlineData("wrong-review-target", "different participant")]
    [InlineData("wrong-implementation-target", "different participant")]
    [InlineData("copied-review", "native authority run location")]
    [InlineData("wrong-receipt-target", "different participant")]
    public void ParticipantCompletionRejectsCrossRepositoryOrStaleEvidence(string scenario, string expected)
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.WriteReceipt("first", scenario);
        fixture.WriteReceipt("second");
        if (scenario == "participant-source") fixture.First.Write("changed.md", "New participant input.");
        if (scenario == "authority-source") fixture.Authority.Write("changed.md", "New authority requirement.");
        if (scenario == "task-contract") fixture.Document += "\nAn additional acceptance obligation.";
        Assert.Contains(fixture.Review(), error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EngineeringGateState.Missing)]
    [InlineData(EngineeringGateState.Failed)]
    [InlineData(EngineeringGateState.Stale)]
    [InlineData(EngineeringGateState.Skipped)]
    [InlineData(EngineeringGateState.Inapplicable)]
    public void ParticipantCompletionCannotBorrowAnotherTargetsPassingGate(EngineeringGateState state)
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.WriteReceipt("first");
        fixture.WriteReceipt("second", state: state);
        Assert.Contains(fixture.Review(), error => error.StartsWith("second:", StringComparison.Ordinal) && error.Contains("review", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dependency", "not product-owned")]
    [InlineData("unknown", "not registered")]
    [InlineData("empty", "requires explicit repository targets")]
    [InlineData("duplicate", "duplicate repository identities")]
    public void ParticipantCompletionRejectsUndeclaredOrAmbiguousRouting(string scenario, string expected)
    {
        using var fixture = new ParticipantClosureFixture();
        if (scenario == "dependency") fixture.Authority.Write(".cis/workspace.yml", File.ReadAllText(Path.Combine(fixture.Authority.Path, ".cis/workspace.yml"))
            .Replace("participation: owned\n  relationship: none\n  components: [first]", "participation: dependency\n  relationship: consumer\n  components: [first]", StringComparison.Ordinal));
        var task = fixture.Task with { Targets = scenario switch { "unknown" => ["absent"], "empty" => [], "duplicate" => ["first", "first"], _ => ["first"] } };
        Assert.Contains(fixture.Service.Prepare(fixture.Change, task, null, fixture.Document, "first").Errors,
            error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ParticipantCompletionDetectsAnEarlierTargetChangingDuringLaterAssessment()
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.WriteReceipt("first"); fixture.WriteReceipt("second");
        fixture.GraphAction = path => { if (path == fixture.Second.Path) fixture.First.Write("changed.md", "Concurrent edit."); };
        Assert.Contains(fixture.Review(), error => error.Contains("while other repositories", StringComparison.Ordinal));
    }

    private sealed class ParticipantClosureFixture : IDisposable
    {
        internal TemporaryRepository Authority { get; } = TemporaryRepository.Create(buildGraph: false);
        internal TemporaryRepository First { get; } = TemporaryRepository.Create(buildGraph: false);
        internal TemporaryRepository Second { get; } = TemporaryRepository.Create(buildGraph: false);
        internal string Document { get; set; } = "Scoped documentation acceptance contract.";
        internal Action<string>? GraphAction { get; set; }
        internal PlanWorkItem Task { get; set; } = new("WORK-030", "documentation", "low", null, null, "Align contract", ["REQ-001"], [], [],
            "Scope agrees", "Review scoped evidence", "InProgress", Targets: ["first", "second"]);
        internal ChangeDossier Change { get; }
        internal PlanningService? Plans { get; }
        internal PlanWorkItem? OtherTask { get; }
        internal IReadOnlyList<string>? FallbackTargets { get; }
        internal bool RegistryAvailable { get; set; } = true;
        internal ICisGraphSnapshotReader? GraphOverride { get; set; }
        internal IReadOnlyList<ICisRepositoryDoctorCheck> Checks { get; set; } = [new ClosingAlignment()];
        internal bool RequireBuild { get; set; }
        internal TaskEngineeringCompletion Service => new(new CisRepositoryContextResolver(), RegistryAvailable ? new WorkspaceRegistry(new CisRepositoryContextResolver()) : null,
            new ParticipantAssessment(RequireBuild), GraphOverride ?? new ClosingGraph(path => GraphAction?.Invoke(path)), Checks);

        internal ParticipantClosureFixture(bool seedPlan = false)
        {
            if (seedPlan)
            {
                FallbackTargets = ["first", "second"];
                var services = CreateServices();
                Assert.Equal(0, services.Graph.Build(Authority.Path).ExitCode);
                Change = PrepareSearchChange(Authority, services);
                const string specPath = "docs/cis/specs/search-feature-spec.md";
                Authority.Write(specPath, File.ReadAllText(Path.Combine(Authority.Path, specPath))
                    .Replace("status: Draft", "status: Draft\ntargets: [first, second]", StringComparison.Ordinal)
                    .Replace("| backend |", "| documentation |", StringComparison.Ordinal));
                var imported = services.Plans.ImportSpec(new(Authority.Path, Change.Id, specPath));
                Assert.Empty(imported.Errors);
                Task = Assert.Single(imported.WorkItems, item => item.Category == "documentation");
                var started = services.Plans.TransitionTask(new(Authority.Path, Change.Id, Task.Id, "InProgress", "Fixture", "Exercise participant closure."));
                Assert.Empty(started.Errors);
                Task = Assert.Single(started.WorkItems, item => item.Id == Task.Id);
                OtherTask = started.WorkItems.First(item => item.Id != Task.Id && item.TaskPath is not null);
                var taskPath = Path.Combine(Authority.Path, Change.RelativePath, Task.TaskPath!);
                Document = File.ReadAllText(taskPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
                    .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |", "| Fixture | Native assertions | Passed | Scoped protocol evidence. |", StringComparison.Ordinal);
                File.WriteAllText(taskPath, Document);
                var resolver = new CisRepositoryContextResolver();
                Plans = new(services.Changes, services.Impacts, services.Decisions, resolver,
                    toolUsage: services.Usage,
                    workspaceRegistry: new WorkspaceRegistry(resolver), engineering: new ParticipantAssessment(false),
                    graphReader: new ClosingGraph(), repositoryChecks: [new ClosingAlignment()]);
                var now = DateTimeOffset.UtcNow;
                services.Usage.Record(new(["docs", "validate", "--repo", Authority.Path], Authority.Path, now, now,
                    1, 0, 100, 0, []));
            }
            foreach (var id in new[] { "authority", "first", "second" })
            {
                var repository = Repository(id);
                repository.Write(".cis/repository.yml", $"schema_version: 1\nrepository:\n  id: {id}\ndocumentation_root: docs/cis\n");
                repository.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
            }
            var registrations = string.Join("\n", new[] { "authority", "first", "second" }.Select(id =>
                $"- id: {id}\n  path: {Repository(id).Path.Replace('\\', '/')}\n  documentation_root: docs/cis\n  role: {(id == "authority" ? "authority" : "participant")}\n  participation: owned\n  relationship: none\n  components: [{id}]"));
            Authority.Write(".cis/workspace.yml", "schema_version: 2\necosystem:\n  id: fixture\n  name: Fixture\nproduct:\n  id: fixture\n  name: Fixture\nrepositories:\n" + registrations + "\n");
            Change ??= new("CIS-0001", "Fixture", "Verify scope", "Approved", "fixture", "fixture", "graph-1", Authority.Path,
                "docs/cis", "docs/cis/changes/CIS-0001", []);
        }
        internal CisRepositoryContext Context(string id) => new CisRepositoryContextResolver().Resolve(Repository(id).Path).Context!;
        internal EngineeringCompletionReceipt Prepare(string id)
        {
            var result = Service.Prepare(Change, Task, FallbackTargets, Document, id);
            Assert.Empty(result.Errors);
            return Assert.IsType<EngineeringCompletionReceipt>(result.Template);
        }
        internal IReadOnlyList<string> Review() => Service.Review(Change, Task, FallbackTargets, () => Document, () => EngineeringCompletionReview.TaskDigest(Task, Document));
        internal void OptOut(string id) => Repository(id).Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":false,"additionalGates":[]}""");
        private TemporaryRepository Repository(string id) => id switch { "authority" => Authority, "first" => First, _ => Second };
        internal void WriteReceipt(string id, string? scenario = null, EngineeringGateState state = EngineeringGateState.Passed)
        {
            var receipt = Prepare(id);
            // Synthetic protocol records test rejection rules, not live agent execution or approval.
            var implementationId = "implementation-" + Task.Id + "-" + id;
            var reviewerId = "review-" + Task.Id + "-" + id;
            var implementationRoot = $".cis/local/agents/runs/{implementationId}/";
            var reviewRoot = $".cis/local/agents/runs/{reviewerId}/";
            var implementationResult = Write(implementationRoot + "result.json", new { schemaVersion = 1, envelopeId = "implementation", status = "Succeeded" });
            var implementation = Write(implementationRoot + "manifest.json", new
            {
                schemaVersion = 1, runId = implementationId, taskId = Task.Id, taskContractDigest = receipt.TaskDigest,
                mode = "implement", status = "Succeeded", outputDigest = receipt.InputDigest,
                targetRepositoryPath = scenario == "wrong-implementation-target" ? Authority.Path : Repository(id).Path,
                provider = "protocol", providerSessionId = "implementation", completedAtUtc = "2000-01-01T00:00:00Z",
                envelopeId = "implementation", resultDigest = implementationResult.Sha256
            });
            var result = Write(reviewRoot + "result.json", new { schemaVersion = 2, envelopeId = "review", status = "Succeeded",
                review = new { recommendation = "ready", findings = Array.Empty<object>() } });
            var run = Write(scenario == "copied-review" ? ".cis/local/copied-review.json" : reviewRoot + "manifest.json", new
            {
                schemaVersion = 1, runId = reviewerId, taskId = Task.Id, taskContractDigest = receipt.TaskDigest,
                mode = "review", permission = "read-only", status = "Succeeded", inputDigest = receipt.InputDigest,
                authorityInputDigest = CisExecutionIdentity.Capture(Context("authority")),
                authorityDossierDigest = scenario == "missing-dossier" ? null : CisTaskAuthorityContext.Capture(Context("authority"), Change.Id),
                targetRepositoryPath = scenario == "wrong-review-target" ? Authority.Path : Repository(id).Path,
                provider = "protocol", providerSessionId = "review", startedAtUtc = "2000-01-02T00:00:00Z",
                envelopeId = "review", resultDigest = result.Sha256
            });
            receipt = receipt with { RepositoryId = scenario == "wrong-receipt-target" ? "authority" : id,
                ImplementerRunId = implementationId, Implementation = implementation, ReviewerRunId = reviewerId,
                Gates = [new("review", state, "Protocol evidence for this participant only.", [run, result])] };
            if (scenario == "human")
            {
                const string humanPath = ".cis/local/human-implementation.json";
                Repository(id).Write(humanPath, JsonSerializer.Serialize(new { schemaVersion = 1, runId = implementationId,
                    kind = "human-implemented", actor = "Synthetic fixture author", taskId = Task.Id,
                    taskContractDigest = receipt.TaskDigest, inputDigest = receipt.InputDigest, completedAtUtc = "2000-01-01T00:00:00Z" }));
                receipt = receipt with { Implementation = Artifact(Repository(id), humanPath) };
            }
            if (RequireBuild)
            {
                const string buildPath = ".cis/local/build.json";
                var destination = scenario == "authority-build" ? Authority : Repository(id);
                destination.Write(buildPath, JsonSerializer.Serialize(new { schemaVersion = 2, inputDigest = receipt.InputDigest,
                    status = "succeeded", steps = new[] { new { id = "build", status = "succeeded", exitCode = 0 } } }));
                receipt = receipt with { Gates = [.. receipt.Gates, new("build", EngineeringGateState.Passed,
                    "Synthetic native workflow result for this target.", [Artifact(destination, buildPath)])] };
            }
            Authority.Write(receipt.ReceiptPath!, EngineeringCompletionReview.Serialize(receipt));

            EngineeringEvidenceArtifact Write(string path, object content)
            {
                Authority.Write(path, JsonSerializer.Serialize(content));
                return new(path, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Authority.Path, path)))));
            }
        }
        private static EngineeringEvidenceArtifact Artifact(TemporaryRepository repository, string path)
            => new(path, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository.Path, path)))));
        public void Dispose() { Second.Dispose(); First.Dispose(); Authority.Dispose(); }
    }

    private sealed class ParticipantAssessment(bool build) : ICisEngineeringAssessment
    {
        public EngineeringAssessment Assess(CisRepositoryContext context, string taskCategory)
            => new(1, CisExecutionIdentity.CaptureIfAdopted(context) is not null,
                build ? [new("review", "Protocol fixture", false), new("build", "Protocol fixture", false)] : [new("review", "Protocol fixture", false)], []);
    }
}
