using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Plan;
using Cis.Modules.Change;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    private static CisStorySizeAssessment FixtureSize(string size) => new(size,
        "Synthetic assessment separates the retained document flow from acknowledgement delivery.", "",
        "One coherent observable outcome with its accepted conditions.", "The existing document/version contract remains unchanged.",
        "Preserve atomic acknowledgement and exact UTC/version values.", "One owned outcome; declared shared prerequisites remain explicit.",
        "The fixture has no unresolved stakeholder choice; implementation details are investigated within scope.");

    private static CisFeatureStoryResult PrepareAssessedStory(FeatureIntakeService service, Fixture fixture, string id, CisFeatureStoryResult read)
    {
        if (read.DeliverySizeState != "current")
        {
            var assessment = service.AssessStory(fixture.Authority, new("referrals", id, read.PlanningBasisHash!, read.PlanningRevision,
                "Fixture planning controller", FixtureSize("medium")));
            Assert.Empty(assessment.Errors);
            read = service.Story(fixture.Authority, "referrals", id, false);
        }
        return service.Story(fixture.Authority, "referrals", id, true, read.InputHash);
    }

    private static CisFeatureStoryTask FixtureContract(string id, IReadOnlyList<int> requirements, bool documentation = false,
        IReadOnlyList<string>? dependencies = null)
    {
        var criterion = documentation ? "The privacy interface reference states the exact accepted version and timestamp contract."
            : "The accepted version and timestamp behavior is preserved without changing the public default.";
        return new(id, documentation ? "Document the privacy contract" : "Preserve the privacy behavior",
            documentation ? "Update the required privacy interface reference from accepted source direction."
                : "Implement the assigned accepted privacy behavior and its focused native regression.", [], requirements, [criterion], dependencies ?? [])
        {
            Category = documentation ? "documentation" : "backend",
            Complexity = "medium",
            TaskTypeKey = documentation ? "core.documentation.contracts" : "core.backend.behavior",
            TaskTypeVersion = "1.0",
            ApprovalGate = "none",
            RequiredOutputs = [documentation ? "docs/privacy-interface.md" : "Scoped privacy implementation and native regression"],
            NegativeCriteria = ["Do not expose personal data or alter the accepted public default."],
            Validation = documentation ? "Review docs/privacy-interface.md against the accepted version/timestamp clauses."
                : "Run the affected native privacy behavior tests for exact version and UTC timestamp retention.",
            VerificationObligations = [new("privacy-contract", [criterion], "Demonstrate the assigned accepted privacy outcome.",
                documentation ? "artifact" : "native", [documentation ? "Current reviewed docs/privacy-interface.md" : "Focused native privacy test result"])
                { SharedGateIds = documentation ? [] : ["unit"], BasisReferences = ["Accepted privacy requirements and shared constraints"] }]
        };
    }

    [Fact]
    public void StoryPlanning_DecomposesLargeScopePreservesInstancesContractsAndHistoryAcrossReload()
    {
        using var f = new Fixture();
        const string constraints = "\n## Constraints\n\nNever expose personal data.\n## Public defaults\n\nThe acknowledgement remains opt-in.\n## Compatibility\n\nPreserve existing clients for 90 days.\n## Interfaces\n\nUse the existing versioned archive interface.\n";
        File.WriteAllText(f.Request.SourcePath, PrivacyStory
            + "| PRV-003 | The product shall document the privacy version and timestamp interface contract. |\n" + constraints);
        CreateIntake(f);
        // Retain an actual canonical owner using the existing dossier/plan format.
        var dossier = Path.Combine(f.Authority, "docs/cis/changes/CIS-0001"); Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "proposal.md"), "---\nchange_id: CIS-0001\ntitle: Preserve privacy behavior\noutcome: Preserve the accepted privacy contract.\nstatus: Approved\nbaseline_kind: graph\nbaseline: synthetic\ngraph_build_id: synthetic\n---\n");
        File.WriteAllText(Path.Combine(dossier, "plan.md"), "status: Approved\n| WORK-BACKEND | backend | medium | | | Retain privacy behavior | PRV-001, PRV-002 | | | Preserve accepted privacy. | Run native privacy tests. | Approved | core.backend.behavior | 1.0 | none | | |\n");
        var resolver = new CisRepositoryContextResolver();
        var reader = new PlanningService(new ChangeDossierStore(resolver), null!, null!, resolver, workspaceRegistry: f.Registry);
        Assert.StartsWith("sha256:", reader.ReadTaskDigest(f.Authority, "CIS-0001", "WORK-BACKEND"));
        var generation = new StoryTaskGeneration();
        var execution = new PlanningContractCapture();
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline],
            textGeneration: generation, storyExecutor: execution, taskContracts: reader);
        var service = Fresh();
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule())
            .AddModule(new StoryPlanningFixtureModule(service)).Build();
        void Command<T>(string operation, T input, int expectedExit = 0)
        {
            var path = Path.Combine(f.Root, operation + "-input.json");
            File.WriteAllText(path, JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(expectedExit, application.Invoke(["brd", "feature", "wizard", "story", operation, "--workspace", f.Authority, "--input", path, "--format", "json"]));
        }
        var parent = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", parent, false);
        var original = File.ReadAllBytes(f.Request.SourcePath);
        Command("prepare", new CisFeatureStoryPrepareRequest("referrals", parent, read.InputHash!, read.Revision!, "Fixture controller",
            [FixtureContract("T1", [1, 2]) with { ChangeId = "CIS-0001", WorkOwnerTaskId = "WORK-BACKEND" }, FixtureContract("T2", [3], true, ["T1"])]));
        var parentPlan = service.Story(f.Authority, "referrals", parent, false);
        Assert.Empty(parentPlan.Errors);
        var approvedParent = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "approve", parentPlan.PlanHash!, parentPlan.Revision!,
            "Fixture reviewer", "Accept the retained synthetic parent task contract.");
        Assert.Empty(approvedParent.Errors);
        Assert.All(approvedParent.TaskProgress, task => Assert.False(task.CanExecute));
        Command("assess", new CisFeatureStoryAssessmentRequest("referrals", parent, approvedParent.PlanningBasisHash!, approvedParent.PlanningRevision,
            "Fixture controller", FixtureSize("high")));
        var assessed = service.Story(f.Authority, "referrals", parent, false);
        Assert.Empty(assessed.Errors); Assert.All(assessed.TaskProgress, task => Assert.False(task.CanExecute));
        var request = service.Requests(f.Authority).Single();
        var parentRecord = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks", parent + ".json");
        var parentHistory = File.ReadAllBytes(parentRecord);
        var children = new[]
        {
            new CisFeatureStoryChild("retained-document", "Retain a versioned privacy document", "Retain the accepted versioned document without changing its public default.", [1], [], FixtureSize("low")),
            new CisFeatureStoryChild("acknowledgement", "Record and document acknowledgement", "Record exact UTC acknowledgement/version values and document that interface.", [2, 3], [], FixtureSize("medium"))
        };
        Command("decompose", new CisFeatureStoryDecompositionRequest("referrals", parent, assessed.PlanningBasisHash!, assessed.PlanningRevision,
            "Fixture controller", children));
        var decomposed = service.Story(f.Authority, "referrals", parent, false);
        Assert.Empty(decomposed.Errors); Assert.True(decomposed.IsDeliveryContainer);
        Assert.Equal("Fixture reviewer", decomposed.ApprovedBy); Assert.Equal(parentHistory, File.ReadAllBytes(parentRecord));
        Assert.Equal(new[] { 1, 2, 3 }, decomposed.ChildStories.SelectMany(child => child.RequirementNumbers).Order());
        Assert.Equal(decomposed.ChildStories[0].Id, Assert.Single(decomposed.ChildStories[1].DependsOn));
        var repeated = service.DecomposeStory(f.Authority, new("referrals", parent, decomposed.PlanningBasisHash!, decomposed.PlanningRevision,
            "Fixture controller", children));
        Assert.Empty(repeated.Errors); Assert.False(repeated.Applied);
        service = Fresh();
        var childA = service.Story(f.Authority, "referrals", decomposed.ChildStories[0].Id!, false);
        var childB = service.Story(f.Authority, "referrals", decomposed.ChildStories[1].Id!, false);
        Assert.Equal("current", childA.DeliverySizeState); Assert.Equal("low", childA.DeliverySize!.Size);
        Assert.Equal(parent, childA.ParentStoryId); Assert.Equal(2, childB.Story!.Requirements.Count);
        var a = service.PrepareStory(f.Authority, new("referrals", childA.StoryId, childA.InputHash!, childA.Revision!, "Fixture controller", [FixtureContract("T1", [1])]));
        var b = service.PrepareStory(f.Authority, new("referrals", childB.StoryId, childB.InputHash!, childB.Revision!, "Fixture controller",
            [FixtureContract("T1", [1]), FixtureContract("T2", [2], true, ["T1"])]));
        Assert.Empty(a.Errors); Assert.Empty(b.Errors);
        Command("prepare", new CisFeatureStoryPrepareRequest("referrals", childA.StoryId, a.InputHash!, a.Revision!, "Fixture controller",
            [a.Tasks[0] with { Targets = ["foreign-owner"] }]), expectedExit: 5);
        Assert.Equal(JsonSerializer.Serialize(a.Tasks[0]), JsonSerializer.Serialize(service.Story(f.Authority, "referrals", childA.StoryId, false).Tasks[0]));
        Assert.NotEqual(a.Tasks[0].InstanceKey, b.Tasks[0].InstanceKey);
        Assert.Equal(a.Tasks[0].TaskTypeKey, b.Tasks[0].TaskTypeKey);
        Assert.Equal("WORK-BACKEND", a.Tasks[0].WorkOwnerTaskId); Assert.Equal("WORK-BACKEND", b.Tasks[0].WorkOwnerTaskId);
        Assert.Equal("CIS-0001", b.Tasks[0].ChangeId);
        generation.ExplicitTasks = [a.Tasks[0] with { Id = "T7" }];
        var regenerated = service.Story(f.Authority, "referrals", a.StoryId, true, a.InputHash, "test-local", "test");
        Assert.Empty(regenerated.Errors); Assert.Equal("T1", regenerated.Tasks[0].Id);
        Assert.Equal(a.Tasks[0].InstanceKey, regenerated.Tasks[0].InstanceKey); Assert.Equal(1, generation.Calls);
        Assert.Contains("verificationObligations", generation.Request!.JsonSchema);
        var approvedA = service.UpdateStoryWorkflow(f.Authority, "referrals", a.StoryId, "approve", regenerated.PlanHash!, regenerated.Revision!, "Fixture reviewer", "Accept bounded document outcome.");
        var approvedB = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "approve", b.PlanHash!, b.Revision!, "Fixture reviewer", "Accept bounded acknowledgement and contract.");
        Assert.Empty(approvedA.Errors); Assert.Empty(approvedB.Errors);
        Assert.False(approvedB.TaskProgress[0].CanStart);
        var selection = service.ExecuteStoryTask(f.Authority, "referrals", a.StoryId, "T1", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(selection.Errors); Assert.Equal("High", selection.ExecutionPlan!.Complexity);
        var started = service.ExecuteStoryTask(f.Authority, "referrals", a.StoryId, "T1", true, approvedA.PlanHash!, approvedA.Revision!,
            selection.ExecutionPlan.Hash, "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(started.Errors); Assert.Equal("low", started.DeliverySize!.Size);
        Assert.Contains("acknowledgement remains opt-in", execution.Observed!.Constraints);
        Assert.Contains("90 days", execution.Observed.Constraints);
        Assert.Contains("existing versioned archive interface", execution.Observed.Constraints);
        Assert.Contains("Never expose personal data", execution.Observed.Constraints);
        Assert.Equal(JsonSerializer.Serialize(a.Tasks[0].VerificationObligations), JsonSerializer.Serialize(execution.Observed.Task.VerificationObligations));
        var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", a.StoryId, "complete", started.PlanHash!, started.Revision!, "Fixture controller",
            taskId: "T1", evidence: "Synthetic non-adopted participant records its scoped evidence through the existing completion route.", criteriaVerified: true);
        Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.TaskProgress[0].Status);
        var freshB = Fresh().Story(f.Authority, "referrals", b.StoryId, false);
        Assert.True(freshB.TaskProgress[0].CanStart); Assert.False(freshB.TaskProgress[1].CanStart);
        Assert.Equal("documentation", freshB.Tasks[1].Category); Assert.Equal("artifact", freshB.Tasks[1].VerificationObligations[0].EvidenceRoute);
        var bStarted = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "start", freshB.PlanHash!, freshB.Revision!, "Fixture controller", taskId: "T1");
        var bFinished = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "complete", bStarted.PlanHash!, bStarted.Revision!, "Fixture controller",
            taskId: "T1", evidence: "Synthetic non-adopted workflow records the acknowledgement check.", criteriaVerified: true);
        Assert.Empty(bFinished.Errors);
        var docSelection = service.ExecuteStoryTask(f.Authority, "referrals", b.StoryId, "T2", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(docSelection.Errors);
        var docExecution = service.ExecuteStoryTask(f.Authority, "referrals", b.StoryId, "T2", true, bFinished.PlanHash!, bFinished.Revision!,
            docSelection.ExecutionPlan!.Hash, "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(docExecution.Errors); Assert.Equal("documentation", execution.Observed!.Task.Category);
        Assert.Equal("docs/privacy-interface.md", Assert.Single(execution.Observed.Task.RequiredOutputs));
        Assert.Equal("artifact", Assert.Single(execution.Observed.Task.VerificationObligations).EvidenceRoute);
        var docFinished = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "complete", docExecution.PlanHash!, docExecution.Revision!,
            "Fixture controller", taskId: "T2", evidence: "Synthetic current interface artifact and review retained through the existing route.", criteriaVerified: true);
        Assert.Empty(docFinished.Errors); Assert.Equal("Complete", docFinished.PlanState);
        var droppedOwner = service.PrepareStory(f.Authority, new("referrals", b.StoryId, docFinished.InputHash!, docFinished.Revision!,
            "Fixture controller", [docFinished.Tasks[1] with { Id = "T9", InstanceKey = null, RequirementNumbers = [1, 2], DependsOn = [] }]));
        Assert.Contains(droppedOwner.Errors, error => error.Contains("Retain owned task instance", StringComparison.Ordinal));
        Assert.Equal(docFinished.Revision, Fresh().Story(f.Authority, "referrals", b.StoryId, false).Revision);
        var revisedTasks = docFinished.Tasks.Select(task => task.Id == "T2" ? task with
        { Id = "T8", RequiredOutputs = ["docs/privacy-interface-v2.md"], Validation = "Review the revised versioned privacy interface artifact." } : task).ToArray();
        var revised = service.PrepareStory(f.Authority, new("referrals", b.StoryId, docFinished.InputHash!, docFinished.Revision!,
            "Fixture controller", revisedTasks));
        Assert.Empty(revised.Errors); Assert.True(revised.IsRevisionProposal); Assert.Equal("Proposed", revised.PlanState);
        Assert.All(revised.TaskProgress, task => Assert.False(task.CanExecute));
        Assert.Equal("T2", revised.Tasks[1].Id); Assert.Equal(docFinished.Tasks[1].InstanceKey, revised.Tasks[1].InstanceKey);
        Assert.Equal(docFinished.Tasks[0].WorkOwnerTaskId, revised.Tasks[0].WorkOwnerTaskId);
        Assert.Equal("Complete", Assert.Single(revised.PreviousPlans).TaskProgress[1].Status);
        var restored = service.PrepareStory(f.Authority, new("referrals", b.StoryId, revised.InputHash!, revised.Revision!,
            "Fixture controller", docFinished.Tasks));
        Assert.Empty(restored.Errors); Assert.False(restored.IsRevisionProposal); Assert.Equal("Complete", restored.PlanState);
        Assert.Equal(docFinished.PlanHash, restored.PlanHash); Assert.Equal(docFinished.Revision, restored.Revision);
        Assert.Empty(restored.PreviousPlans); Assert.True(restored.TaskProgress[1].Execution!.Applied);
        revised = service.PrepareStory(f.Authority, new("referrals", b.StoryId, restored.InputHash!, restored.Revision!,
            "Fixture controller", revisedTasks));
        Assert.Empty(revised.Errors); Assert.True(revised.IsRevisionProposal);
        var reapproved = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "approve", revised.PlanHash!, revised.Revision!,
            "Fixture reviewer", "Review only the changed interface output contract while retaining compatible work.");
        Assert.Empty(reapproved.Errors); Assert.False(reapproved.IsRevisionProposal);
        Assert.Equal("Complete", reapproved.TaskProgress[0].Status); Assert.Equal("Ready", reapproved.TaskProgress[1].Status);
        Assert.Null(reapproved.TaskProgress[1].Execution);
        var previousPlan = Assert.Single(reapproved.PreviousPlans);
        Assert.Equal("Fixture reviewer", previousPlan.ApprovedBy); Assert.Equal("docs/privacy-interface.md", Assert.Single(previousPlan.Tasks[1].RequiredOutputs));
        Assert.Equal("Complete", previousPlan.TaskProgress[1].Status); Assert.NotNull(previousPlan.TaskProgress[1].Execution);
        var reloadedRevision = Fresh().Story(f.Authority, "referrals", b.StoryId, false);
        Assert.Equal(reapproved.Revision, reloadedRevision.Revision); Assert.Single(reloadedRevision.PreviousPlans);
        Assert.Equal("Complete", reloadedRevision.TaskProgress[0].Status); Assert.Null(reloadedRevision.TaskProgress[1].Execution);
        var before = f.Snapshot();
        Fresh().Story(f.Authority, "referrals", b.StoryId, false); Fresh().Navigation(f.Authority);
        Assert.Equal(before, f.Snapshot()); Assert.Equal(original, File.ReadAllBytes(f.Request.SourcePath));
        File.WriteAllText(f.Request.SourcePath, File.ReadAllText(f.Request.SourcePath).Replace("acknowledgement timestamp", "authenticated acknowledgement timestamp", StringComparison.Ordinal));
        var preview = Reimport(f); Assert.Empty(preview.Errors); Assert.True(Reimport(f, false, preview.Plan!.PlanHash).Applied);
        var unaffected = Fresh().Story(f.Authority, "referrals", a.StoryId, false);
        var changed = Fresh().Story(f.Authority, "referrals", b.StoryId, false);
        Assert.Equal("current", unaffected.DeliverySizeState); Assert.Equal("Complete", unaffected.TaskProgress[0].Status);
        Assert.Equal("stale", changed.DeliverySizeState); Assert.All(changed.TaskProgress, task => Assert.False(task.CanExecute));
        Assert.Equal(parentHistory, File.ReadAllBytes(parentRecord));
    }

    private sealed class PlanningContractCapture : ICisStoryTaskExecutor
    {
        public CisStoryTaskWork? Observed { get; private set; }
        public CisStoryExecutionPlan SelectModels(CisStoryTaskWork work)
            => new("fixture-model-selection", "High", ["Sensitive privacy review uses stronger reasoning independently of bounded delivery size."],
                new("fixture", "implementation", true), new("fixture", "review", true), []);
        public CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote,
            Func<bool> stillCurrent, CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress)
        {
            Assert.True(stillCurrent()); Observed = work;
            return new("reviewed", selection, [], [], [], [], true);
        }
    }

    private sealed class StoryPlanningFixtureModule(FeatureIntakeService service) : ICisModule
    {
        public string Name => "story-planning-fixture";
        public string Description => "Inject the existing synthetic feature service into its supported commands.";
        public void RegisterServices(IServiceCollection services) => services.AddSingleton(service);
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }

    [Fact]
    public void StoryPlanning_ReadsPriorSevenFieldApprovalWithoutResetAndDiagnosesAdoptedLegacySemantics()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f);
        var service = DeliveryService(f, new StoryTaskGeneration());
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var prepared = PrepareAssessedStory(service, f, id, read);
        Assert.Empty(prepared.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", prepared.PlanHash!, prepared.Revision!, "Historical reviewer", "Accept the original bounded task scope.");
        Assert.Empty(approved.Errors);
        var reread = service.Story(f.Authority, "referrals", id, false);
        Assert.Equal(approved.PlanHash, reread.PlanHash); Assert.Equal(approved.Revision, reread.Revision);
        var started = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "start", approved.PlanHash!, approved.Revision!, "Historical implementer", taskId: "T1");
        Assert.Empty(started.Errors);
        var plan = service.Requests(f.Authority).Single();
        var recordPath = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, plan.RequestPath))!, "story-tasks", id + ".json");
        RetainSevenFieldStoryRecord(recordPath);
        var historicalBytes = File.ReadAllBytes(recordPath);
        var fresh = DeliveryService(f, new StoryTaskGeneration()).Story(f.Authority, "referrals", id, false);
        Assert.Empty(fresh.Errors); Assert.Equal("Approved", fresh.PlanState); Assert.Equal("Historical reviewer", fresh.ApprovedBy);
        Assert.Equal("InProgress", fresh.TaskProgress[0].Status); Assert.True(fresh.TaskProgress[0].CanExecute);
        Assert.Null(fresh.Tasks[0].Category); Assert.Equal(historicalBytes, File.ReadAllBytes(recordPath));
        File.WriteAllText(Path.Combine(f.Authority, ".cis/engineering-defaults.json"), """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var adopted = DeliveryService(f, new StoryTaskGeneration()).Story(f.Authority, "referrals", id, false);
        Assert.Empty(adopted.Errors); Assert.Equal("Approved", adopted.PlanState); Assert.Equal("InProgress", adopted.TaskProgress[0].Status);
        Assert.False(adopted.TaskProgress[0].CanExecute); Assert.Contains("unresolved category", adopted.TaskProgress[0].BlockedReason);
        Assert.Equal(historicalBytes, File.ReadAllBytes(recordPath));
    }

    [Fact]
    public void StoryPlanning_FutureCandidateChildrenRetainUncommittedDeliveryHold()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# Feature\n\n## Future candidate requirements\n\n| FUT-001 | The product shall preserve a versioned privacy interface. |\n");
        CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader);
        var parent = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", parent, false); Assert.True(read.IsFutureCandidate);
        var decomposed = service.DecomposeStory(f.Authority, new("referrals", parent, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller",
            [new("behavior", "Retain the versioned behavior", "Preserve the accepted versioned privacy behavior.", [1], [], FixtureSize("low")),
             new("reference", "Describe the versioned interface", "Document the same accepted privacy interface.", [1], ["behavior"], FixtureSize("low"))]));
        Assert.Empty(decomposed.Errors);
        var child = service.Story(f.Authority, "referrals", decomposed.ChildStories[0].Id!, false);
        Assert.True(child.IsFutureCandidate); Assert.Equal("current", child.DeliverySizeState);
        var proposal = service.PrepareStory(f.Authority, new("referrals", child.StoryId, child.InputHash!, child.Revision!, "Fixture controller", [FixtureContract("T1", [1])]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", child.StoryId, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Review the proposed future scope without committing implementation.");
        Assert.Empty(approved.Errors); Assert.False(approved.TaskProgress[0].CanExecute);
        Assert.Contains("uncommitted future candidate", approved.TaskProgress[0].BlockedReason);
    }
}
