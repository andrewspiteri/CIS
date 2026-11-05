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
