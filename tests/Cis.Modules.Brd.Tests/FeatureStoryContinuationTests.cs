using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Change;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void StoryContinuation_HeadlessBindingPreservesApprovedMeaningHistoryAndOwnerWithoutAllocating()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var dossier = Path.Combine(f.Authority, "docs/cis/changes/CIS-0001"); Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "proposal.md"), "---\nchange_id: CIS-0001\ntitle: Preserve privacy behavior\noutcome: Preserve the exact accepted privacy contract.\nstatus: Approved\nbaseline_kind: graph\nbaseline: synthetic\ngraph_build_id: synthetic\n---\n");
        File.WriteAllText(Path.Combine(dossier, "plan.md"), "---\nstatus: Approved\n---\n| WORK-BOUND | backend | medium | | | Retain privacy behavior | PRV-001, PRV-002 | | | Preserve accepted privacy. | Run affected native privacy tests. | Approved | core.backend.behavior | 1.0 | none | | |\n");
        Directory.CreateDirectory(Path.Combine(dossier, "agent-tasks"));
        File.WriteAllText(Path.Combine(dossier, "agent-tasks/WORK-BOUND.md"), "---\ntask_status: Ready\n---\n# Retain privacy behavior\nPreserve the accepted privacy contract under the approved canonical plan.\n");
        File.WriteAllText(Path.Combine(dossier, "impact.md"), "---\nstatus: Accepted\n---\n| Finding | Status |\n|---|---|\n| IMPACT-PRIVACY | accepted |\n");
        File.WriteAllText(Path.Combine(f.Authority, CisEngineeringPolicy.RelativePath), """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var resolver = new CisRepositoryContextResolver();
        var reader = new PlanningService(new ChangeDossierStore(resolver), null!, null!, resolver, workspaceRegistry: f.Registry);
        var inspector = new BindingInspector(new AgentService(resolver, [], workspaceRegistry: f.Registry, taskContracts: reader));
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader, storyExecutor: inspector);
        var service = Fresh(); var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var assessed = service.AssessStory(f.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller", FixtureSize("medium")));
        Assert.Empty(assessed.Errors);
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, assessed.InputHash!, assessed.Revision!, "Fixture controller", [FixtureContract("T1", [1, 2])]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Approve the bounded privacy outcome.");
        Assert.Empty(approved.Errors); Assert.False(approved.TaskProgress[0].CanExecute);
        var implementationDigest = CisStoryEngineeringContract.Digest(inspector.LastWork!);
        var originalTask = JsonSerializer.Serialize(approved.Tasks[0]);
        using var app = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule()).AddModule(new DocsModule())
            .AddModule(new GraphModule()).AddModule(new BrdModule()).AddModule(new StoryPlanningFixtureModule(service)).Build();
        Assert.Equal(5, app.Invoke(["brd", "feature", "wizard", "story", "work-status", "--workspace", f.Authority, "--slug", "referrals", "--story", id, "--task", "T1", "--format", "json"]));
        Assert.False(File.Exists(Path.Combine(f.Authority, CisAgentWorkPaths.Ledger)));
        var binding = new CisStoryWorkBindingRequest("referrals", id, "T1", approved.PlanHash!, approved.Revision!, "CIS-0001", "WORK-BOUND", "Fixture controller", "Use the exact existing approved canonical task.");
        var input = Path.Combine(f.Root, "story-binding.json"); File.WriteAllText(input, JsonSerializer.Serialize(binding, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0, app.Invoke(["brd", "feature", "wizard", "story", "bind-work", "--workspace", f.Authority, "--input", input, "--format", "json"]));
        var bound = Fresh().Story(f.Authority, "referrals", id, false);
        Assert.Equal(approved.PlanHash, bound.PlanHash); Assert.Equal(approved.ApprovedBy, bound.ApprovedBy);
        Assert.NotEqual(approved.Revision, bound.Revision); Assert.Equal(originalTask, JsonSerializer.Serialize(bound.Tasks[0]));
        Assert.Equal(implementationDigest, CisStoryEngineeringContract.Digest(inspector.LastWork!));
        Assert.Equal(new CisStoryWorkBinding("CIS-0001", "WORK-BOUND"), inspector.LastWork!.CanonicalBinding);
        Assert.True(bound.TaskProgress[0].Work!.BindingCurrent); Assert.Equal("held", bound.TaskProgress[0].Work!.Status);
        Assert.Equal("missing-allocation", bound.TaskProgress[0].Work!.Control!.Status);
        Assert.False(bound.TaskProgress[0].CanExecute); Assert.False(File.Exists(Path.Combine(f.Authority, CisAgentWorkPaths.Ledger)));
        var repeated = service.BindStoryWork(f.Authority, binding with { ExpectedRevision = bound.Revision! });
        Assert.Empty(repeated.Errors); Assert.False(repeated.Applied); Assert.Equal(bound.Revision, repeated.Revision);
        var suffix = service.PrepareStory(f.Authority, new("referrals", id, bound.InputHash!, bound.Revision!, "Fixture controller",
            [bound.Tasks[0] with { Id = "T9", InstanceKey = null }]));
        Assert.Contains(suffix.Errors, error => error.Contains("Retain owned task instance", StringComparison.Ordinal));
        var revised = service.PrepareStory(f.Authority, new("referrals", id, bound.InputHash!, bound.Revision!, "Fixture controller",
            [bound.Tasks[0] with { RequiredOutputs = ["artifacts/current-privacy-contract.md"] }]));
        Assert.Empty(revised.Errors);
        revised = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", revised.PlanHash!, revised.Revision!, "Fixture reviewer", "Review the material output revision under the same retained owner.");
        Assert.Empty(revised.Errors); Assert.Single(revised.PreviousPlans);
        Assert.True(revised.TaskProgress[0].Work!.BindingCurrent); Assert.Equal("WORK-BOUND", revised.TaskProgress[0].Work!.Identity!.TaskId);
        var different = service.BindStoryWork(f.Authority, binding with { ExpectedPlanHash = revised.PlanHash!, ExpectedRevision = revised.Revision!, WorkOwnerTaskId = "WORK-OTHER" });
        Assert.Contains(different.Errors, error => error.Contains("already has a retained work owner", StringComparison.Ordinal));
        var before = f.Snapshot(); Fresh().Story(f.Authority, "referrals", id, false); Assert.Equal(before, f.Snapshot());
        Assert.Equal(0, inspector.ProviderCalls);
    }

    private sealed class BindingInspector(AgentService service) : ICisStoryTaskExecutor
    {
        public CisStoryTaskWork? LastWork { get; private set; }
        public int ProviderCalls { get; private set; }
        public CisStoryWorkStatus InspectWork(CisStoryTaskWork work)
        {
            LastWork = work;
            return service.InspectWork(work);
        }
        public CisStoryExecutionPlan SelectModels(CisStoryTaskWork work) { ProviderCalls++; throw new InvalidOperationException("No model is needed for inspection or binding."); }
        public CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote, Func<bool> stillCurrent,
            CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress) => throw new InvalidOperationException("Binding cannot launch work.");
    }

    [Fact]
    public void StoryContinuation_OptionalMinorAdviceAllowsActualCompletionWithoutFeedbackAnswers()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }"); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var executor = new StructuredStoryExecutor { OptionalOnly = true };
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader, storyExecutor: executor);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var assessed = service.AssessStory(f.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller", FixtureSize("medium")));
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, assessed.InputHash!, assessed.Revision!, "Fixture controller",
            [FixtureContract("T1", [1, 2]) with { RepositoryIds = ["backend", "customer-ui"] }]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Review accepted privacy implementation and evidence.");
        Assert.Empty(approved.Errors);
        var selected = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        var executed = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, approved.PlanHash!, approved.Revision!, selected.ExecutionPlan!.Hash,
            "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(executed.Errors); Assert.True(executed.TaskProgress[0].Execution!.Applied);
        var advice = Assert.Single(executed.TaskProgress[0].ReviewFeedback!);
        Assert.Equal("minor", advice.Severity); Assert.Equal("optional", advice.Disposition); Assert.False(advice.RequiresDecision); Assert.Null(advice.Answer);
        Assert.True(executed.TaskProgress[0].CanComplete);
        var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", executed.PlanHash!, executed.Revision!, "Fixture controller",
            taskId: "T1", evidence: "Verified every accepted privacy criterion and the linked existing participant outputs; optional naming advice remains advisory.", criteriaVerified: true);
        Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.TaskProgress[0].Status);
        Assert.Equal(2, complete.CompletionContexts.Count); Assert.All(complete.CompletionContexts, context => Assert.Equal("not-adopted", context.AdoptionStatus));
        Assert.Equal(1, executor.Calls);
    }

    private sealed class StructuredStoryExecutor : ICisStoryTaskExecutor
    {
        public bool OptionalOnly { get; init; }
        public bool LegacyBlockedQuestion { get; init; }
        public bool ChangeMaterial { get; set; }
        public int Calls { get; private set; }
        public CisStoryExecutionPlan SelectModels(CisStoryTaskWork work) => new("structured-selection", "High", ["Separate bounded delivery from privacy reasoning."],
            new("fixture", "implementation", true), new("fixture", "independent-review", true), []);
        public CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote, Func<bool> stillCurrent,
            CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress)
        {
            Assert.True(stillCurrent()); Calls++;
            var run = "review-" + Calls;
            if (LegacyBlockedQuestion)
                return new("blocked", selection, [new(run, "backend", "Review", "fixture", "independent-review", "completed", "", "Retained historical owner question.", [])],
                    [], ["backend: major — Which existing archive owner handles the accepted privacy request?"], [], false);
            var finding = new CisStoryReviewFinding("local-finding-" + Calls, "minor", OptionalOnly ? "optional" : "required", "correctness", "archive.ts:1",
                OptionalOnly ? "A shorter helper name could improve future readability." : "Confirm the accepted archive ownership direction.",
                OptionalOnly ? "Consider the name in a later authorized cleanup." : "Retain the existing accepted archive owner.", "backend", run,
                ChangeMaterial ? "current-candidate-2" : "current-candidate-1", ["Accepted privacy ownership and verification criteria"])
            { RequiresDecision = !OptionalOnly };
            return new(OptionalOnly ? "reviewed" : "blocked", selection,
                [new(run, "backend", "Review", "fixture", "independent-review", "completed", "", "Incidental reviewer summary " + Calls, ["Reviewed current accepted privacy criteria."])],
                [], [$"backend: minor ({finding.Disposition}) — {finding.Observation} {finding.Recommendation}"], [], OptionalOnly)
            { StructuredFindings = [finding] };
        }
    }

    [Fact]
    public void StoryContinuation_LegacyBlockedQuestionRetainsSupportedAnswerFlowWithoutGrantingCompletion()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var executor = new StructuredStoryExecutor { LegacyBlockedQuestion = true };
        var generation = new FeedbackGeneration("valid");
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader,
            storyExecutor: executor, textGeneration: generation);
        var service = Fresh(); var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var assessed = service.AssessStory(f.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller", FixtureSize("medium")));
        Assert.Empty(assessed.Errors);
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, assessed.InputHash!, assessed.Revision!, "Fixture controller", [FixtureContract("T1", [1, 2])]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Review the bounded accepted privacy contract.");
        Assert.Empty(approved.Errors);
        var selected = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        var current = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, approved.PlanHash!, approved.Revision!, selected.ExecutionPlan!.Hash,
            "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(current.Errors); Assert.Null(current.TaskProgress[0].Execution!.StructuredFindings);
        var question = Assert.Single(Fresh().Story(f.Authority, "referrals", id, false).TaskProgress[0].ReviewFeedback!);
        Assert.Equal("required", question.Disposition); Assert.Equal("major", question.Severity); Assert.True(question.RequiresDecision);
        var suggested = service.SuggestStoryFeedback(f.Authority, "referrals", id, "T1", current.Revision!, "test-feedback", "strong");
        Assert.Empty(suggested.Errors); Assert.Equal(1, generation.Calls);
        question = Assert.Single(suggested.TaskProgress[0].ReviewFeedback!); Assert.NotNull(question.SuggestedAnswer); Assert.Null(question.Answer);
        var saved = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Fixture controller",
            new() { [question.Id] = "Keep the existing document archive under the accepted owner." })
        { ExpectedBasisHashes = new Dictionary<string, string> { [question.Id] = question.BasisHash! } });
        Assert.Empty(saved.Errors); Assert.Equal(current.PlanHash, saved.PlanHash);
        var answered = Assert.Single(saved.TaskProgress[0].ReviewFeedback!); Assert.NotNull(answered.Answer); Assert.True(answered.RequiresDecision);
        Assert.False(saved.TaskProgress[0].CanComplete); Assert.False(saved.TaskProgress[0].Execution!.Applied);
        Assert.Equal("blocked", saved.TaskProgress[0].Execution!.Status);
        var before = f.Snapshot(); Assert.Equal(answered.Answer, Assert.Single(Fresh().Story(f.Authority, "referrals", id, false).TaskProgress[0].ReviewFeedback!).Answer);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StoryContinuation_LegacyAnswerSurvivesRealEarlyProviderFailureAndReachesTheNextRequest(bool failReview, bool structured)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var provider = new LegacyRetryProvider { FailImplementation = !failReview, FailReview = failReview };
        var agent = new AgentService(new CisRepositoryContextResolver(), [provider], textGeneration: new LegacyRetryModels(), workspaceRegistry: f.Registry);
        var executor = new LegacyReviewContinuationExecutor(agent, structured);
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader, storyExecutor: executor);
        var service = Fresh(); var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var assessed = service.AssessStory(f.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller", FixtureSize("medium")));
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, assessed.InputHash!, assessed.Revision!, "Fixture controller", [FixtureContract("T1", [1, 2])]));
        Assert.Empty(proposal.Errors);
        var current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Review the bounded accepted privacy contract.");
        Assert.Empty(current.Errors);
        CisFeatureStoryResult Execute(CisFeatureStoryResult previous)
        {
            var selected = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
            Assert.Empty(selected.Errors);
            return service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, previous.PlanHash!, previous.Revision!, selected.ExecutionPlan!.Hash,
                "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        }
        // Seed the historical wire shape; all retries below use production AgentService result construction.
        current = Execute(current); Assert.Empty(current.Errors);
        var question = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        Assert.Equal(structured, current.TaskProgress[0].Execution!.StructuredFindings is not null); Assert.True(question.RequiresDecision);
        const string answer = "Keep the existing document archive under the accepted owner.";
        current = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Fixture controller", new() { [question.Id] = answer })
        { ExpectedBasisHashes = new Dictionary<string, string> { [question.Id] = question.BasisHash! } });
        Assert.Empty(current.Errors);
        var saved = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        current = Execute(current);
        Assert.Equal(failReview ? "review-failed" : "failed", current.TaskProgress[0].Execution!.Status); Assert.NotEmpty(current.Errors);
        Assert.Equal(failReview ? 2 : 1, provider.Requests.Count); Assert.Equal("implement", provider.Requests[0].Mode);
        var firstFailure = current.TaskProgress[0].Execution!;
        Assert.Equal(structured, firstFailure.StructuredFindings is not null); Assert.Equal(question.Finding, Assert.Single(firstFailure.Findings));
        Assert.NotNull(firstFailure.ReviewEvidenceRunIds);
        Assert.All(firstFailure.ReviewEvidenceRunIds, run => Assert.Contains(firstFailure.Runs, retainedRun => retainedRun.RunId == run));
        if (failReview)
        {
            Assert.Contains(firstFailure.Runs, run => run.RunId == provider.Requests[1].RunId && run.Stage == "Review");
            Assert.DoesNotContain(provider.Requests[1].RunId, firstFailure.ReviewEvidenceRunIds);
        }
        current = Fresh().Story(f.Authority, "referrals", id, false);
        var retained = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        Assert.Equal(saved.Id, retained.Id); Assert.Equal(saved.Answer, retained.Answer); Assert.Equal(saved.Actor, retained.Actor);
        Assert.Equal(saved.SavedAt, retained.SavedAt); Assert.Equal(saved.BasisHash, retained.BasisHash);
        Assert.Equal("current", retained.ApplicabilityState); Assert.Null(retained.RetainedAnswer); Assert.True(retained.RequiresDecision);
        Assert.All(saved.RunIds!, run => Assert.Contains(run, retained.RunIds!));
        Assert.False(current.TaskProgress[0].CanComplete); Assert.False(firstFailure.Applied);
        current = Execute(current);
        Assert.Equal(failReview ? 3 : 2, provider.Requests.Count);
        Assert.Equal(failReview ? "review" : "implement", provider.Requests[^1].Mode);
        var sent = Assert.Single(executor.LastWork!.ReviewResponses);
        Assert.Equal(saved.Id, sent.Id); Assert.Equal(saved.BasisHash, sent.BasisHash); Assert.Equal("current", sent.ApplicabilityState);
        Assert.Equal(saved.Actor, sent.Actor); Assert.Equal(saved.SavedAt, sent.SavedAt);
        var prompt = provider.Requests[^1].Prompt;
        var start = prompt.IndexOf("<human-review-responses>\n", StringComparison.Ordinal) + "<human-review-responses>\n".Length;
        var end = prompt.IndexOf("\n</human-review-responses>", start, StringComparison.Ordinal);
        using (var responses = JsonDocument.Parse(prompt[start..end]))
        {
            var response = Assert.Single(responses.RootElement.EnumerateArray());
            Assert.Equal(saved.Id, response.GetProperty("id").GetString()); Assert.Equal(answer, response.GetProperty("answer").GetString());
            Assert.Equal(saved.Actor, response.GetProperty("actor").GetString()); Assert.Equal(saved.SavedAt, response.GetProperty("savedAt").GetString());
        }
        File.AppendAllText(archive, "\nexport const documentVersionChanged = true;");
        current = Fresh().Story(f.Authority, "referrals", id, false);
        Assert.Empty(current.Errors);
        var changed = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        Assert.Equal(saved.Id, changed.Id); Assert.Null(changed.Answer); Assert.Equal(saved.Answer, changed.RetainedAnswer);
        Assert.Equal("needs-review", changed.ApplicabilityState); Assert.NotEqual(saved.BasisHash, changed.BasisHash);
        Assert.False(current.TaskProgress[0].CanComplete);
        provider.FailImplementation = false;
        provider.FailReview = false;
        var beforeReady = provider.Requests.Count;
        current = Execute(current); Assert.Empty(current.Errors);
        Assert.Empty(executor.LastWork!.ReviewResponses);
        Assert.Equal("reviewed", current.TaskProgress[0].Execution!.Status);
        Assert.NotNull(current.TaskProgress[0].Execution!.StructuredFindings);
        Assert.Empty(current.TaskProgress[0].Execution!.StructuredFindings!);
        Assert.Empty(current.TaskProgress[0].Execution!.Findings);
        Assert.Empty(Fresh().Story(f.Authority, "referrals", id, false).TaskProgress[0].ReviewFeedback!);
        Assert.Equal(failReview ? new[] { "review" } : new[] { "implement", "review" },
            provider.Requests.Skip(beforeReady).Select(request => request.Mode));
        if (failReview) Assert.Single(provider.Requests, request => request.Mode == "implement");
    }

    private sealed class LegacyReviewContinuationExecutor(AgentService agent, bool structured) : ICisStoryTaskExecutor
    {
        private readonly StructuredStoryExecutor historical = new() { LegacyBlockedQuestion = !structured };
        public CisStoryTaskWork? LastWork { get; private set; }
        public CisStoryWorkStatus InspectWork(CisStoryTaskWork work) => agent.InspectWork(work);
        public CisStoryExecutionPlan SelectModels(CisStoryTaskWork work) => agent.SelectModels(work);
        public CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote, Func<bool> stillCurrent,
            CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress)
        {
            LastWork = work;
            return historical.Calls == 0 ? historical.Execute(work, selection, allowRemote, stillCurrent, cancellationToken, progress)
                : agent.Execute(work, selection, allowRemote, stillCurrent, cancellationToken, progress);
        }
    }

    private sealed class LegacyRetryModels : ICisTextGenerationService
    {
        public CisAiStatus GetStatus() => new([new("codex", "available", "test", true, [new("gpt-6-astra"), new("gpt-6-sol")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request) => throw new InvalidOperationException("Model selection does not generate text.");
    }

    private sealed class LegacyRetryProvider : ICisAgentProvider
    {
        public List<CisAgentExecutionRequest> Requests { get; } = [];
        public bool FailImplementation { get; set; } = true;
        public bool FailReview { get; set; }
        public Action? DuringExecution { get; init; }
        public CisAgentProviderDescriptor Descriptor => new("codex", "Fixture coding agent", "test", true, ["exec-json"], ["implement", "review"], ["read-only", "workspace-write"], false, false, "Test only");
        public CisAgentProviderDiagnosis Diagnose(string repositoryPath) => new("codex", "ready", true, null, "test", true, [], []);
        public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            DuringExecution?.Invoke();
            if (request.Mode == "implement" && FailImplementation)
                return new(CisAgentRunStates.Failed, 1, null, "Provider unavailable before implementation or review.", [], [], [], null, null, null, null, ["Provider unavailable"]);
            if (request.Mode == "review" && FailReview)
                return new(CisAgentRunStates.Succeeded, 0, null, "Malformed independent review result.", [], [], [], null, null, null, null, []);
            if (request.Mode == "implement") File.WriteAllText(Path.Combine(request.WorkingDirectory, "task-report.md"), "Retained accepted archive ownership and privacy contract.");
            return new(CisAgentRunStates.Succeeded, 0, null, JsonSerializer.Serialize(new
            {
                summary = request.Mode == "implement" ? "Retained the bounded privacy report." : "Reviewed the current privacy report without findings.",
                changedFiles = request.Mode == "implement" ? new[] { "task-report.md" } : [],
                validations = new[] { "Checked the accepted archive ownership contract." },
                evidence = new[] { "task-report.md" },
                review = request.Mode == "review" ? new AgentBrdReview("ready", ["Checked the accepted scope."], [])
                { NecessityAssessment = new(Decision: "The bounded report retains the accepted responsibilities.") { Applicable = false } } : null
            }), [], [], [], null, null, null, null, []);
        }
    }

    [Fact]
    public void StoryContinuation_AdoptedControlsRequireCurrentInspectorBeforeLegacyExecutorCanRun()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        File.WriteAllText(Path.Combine(f.Authority, CisEngineeringPolicy.RelativePath),
            """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var executor = new StructuredStoryExecutor { OptionalOnly = true };
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader, storyExecutor: executor);
        var service = Fresh(); var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var assessed = service.AssessStory(f.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller", FixtureSize("medium")));
        Assert.Empty(assessed.Errors);
        var prepared = service.PrepareStory(f.Authority, new("referrals", id, assessed.InputHash!, assessed.Revision!, "Fixture controller", [FixtureContract("T1", [1, 2])]));
        Assert.Empty(prepared.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", prepared.PlanHash!, prepared.Revision!, "Fixture reviewer", "Approve the valid bounded contract without granting an execution allowance.");
        Assert.Empty(approved.Errors);
        var before = f.Snapshot();
        var current = Fresh().Story(f.Authority, "referrals", id, false);
        var task = Assert.Single(current.TaskProgress);
        Assert.True(task.Work!.Required); Assert.False(task.CanExecute);
        Assert.Contains(task.Work.Errors, error => error.Contains("Load the current story work-control inspector", StringComparison.Ordinal));
        Assert.Equal("Load the current story work-control inspector before executing adopted story work; inspection must prove the exact owner and allowance.", task.Work.NextAction);
        Assert.Equal(before, f.Snapshot());
        var held = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, current.PlanHash!, current.Revision!, "unavailable",
            "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.NotEmpty(held.Errors); Assert.Equal(0, executor.Calls);
        Assert.False(File.Exists(Path.Combine(f.Authority, CisAgentWorkPaths.Ledger)));
    }

    [Fact]
    public void StoryContinuation_StructuredFindingKeepsResponsesAcrossRunIdentityButRequiresCurrentMaterialBasis()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var executor = new StructuredStoryExecutor();
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader, storyExecutor: executor);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var assessed = service.AssessStory(f.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller", FixtureSize("medium")));
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, assessed.InputHash!, assessed.Revision!, "Fixture controller", [FixtureContract("T1", [1, 2])]));
        var current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Review the bounded accepted owner investigation.");
        CisFeatureStoryResult Execute(CisFeatureStoryResult previous)
        {
            var selected = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
            var result = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, previous.PlanHash!, previous.Revision!, selected.ExecutionPlan!.Hash,
                "Fixture controller", cancellation: TestContext.Current.CancellationToken);
            Assert.Empty(result.Errors); return result;
        }
        current = Execute(current); var finding = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        Assert.Equal("required", finding.Disposition); Assert.True(finding.RequiresDecision);
        current = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Fixture controller",
            new() { [finding.Id] = "Preserve the accepted existing archive owner." })
        { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = finding.BasisHash! } });
        Assert.Empty(current.Errors); var saved = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        current = Execute(current); var repeated = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        Assert.Equal(saved.Id, repeated.Id); Assert.Equal(saved.Answer, repeated.Answer); Assert.Equal(saved.SavedAt, repeated.SavedAt);
        Assert.Equal(new[] { "review-1", "review-2" }, repeated.RunIds);
        executor.ChangeMaterial = true; current = Execute(current); var changed = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        Assert.Equal(saved.Id, changed.Id); Assert.Null(changed.Answer); Assert.Equal(saved.Answer, changed.RetainedAnswer); Assert.Equal("needs-review", changed.ApplicabilityState);
        Assert.False(current.TaskProgress[0].CanComplete);
    }
}
