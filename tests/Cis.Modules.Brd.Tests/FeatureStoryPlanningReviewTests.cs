using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void StoryPlanning_UpgradedPrerequisiteBindingRetainsAppliedProgressForVerificationRenewal()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var capture = new PlanningContractCapture();
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], storyExecutor: capture, taskContracts: reader);
        var service = Fresh(); var parent = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", parent, false);
        var split = service.DecomposeStory(f.Authority, new("referrals", parent, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller",
            [new("retention", "Retain the privacy version", "Preserve the accepted versioned privacy document.", [1], [], FixtureSize("low")),
             new("acknowledgement", "Retain acknowledgement", "Preserve the exact accepted acknowledgement contract.", [2], [], FixtureSize("low"))]));
        Assert.Empty(split.Errors);
        CisFeatureStoryResult Prepare(int index, params string[] dependencies)
        {
            var story = service.Story(f.Authority, "referrals", split.ChildStories[index].Id!, false);
            var proposal = service.PrepareStory(f.Authority, new("referrals", story.StoryId, story.InputHash!, story.Revision!, "Fixture controller",
                [FixtureContract("T1", [1], dependencies: dependencies)]));
            Assert.Empty(proposal.Errors);
            var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", story.StoryId, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Review exact bounded privacy work and its prerequisite.");
            Assert.Empty(approved.Errors); return approved;
        }
        var a = Prepare(0); var b = Prepare(1, a.StoryId + "/T1");
        var startedA = service.UpdateStoryWorkflow(f.Authority, "referrals", a.StoryId, "start", a.PlanHash!, a.Revision!, "Fixture implementer", taskId: "T1");
        Assert.Empty(startedA.Errors);
        a = service.UpdateStoryWorkflow(f.Authority, "referrals", a.StoryId, "complete", startedA.PlanHash!, startedA.Revision!, "Fixture implementer",
            taskId: "T1", evidence: "Verified the accepted exact privacy version behavior.", criteriaVerified: true);
        Assert.Empty(a.Errors);
        b = service.Story(f.Authority, "referrals", b.StoryId, false);
        var selected = service.ExecuteStoryTask(f.Authority, "referrals", b.StoryId, "T1", cancellation: TestContext.Current.CancellationToken);
        var applied = service.ExecuteStoryTask(f.Authority, "referrals", b.StoryId, "T1", true, b.PlanHash!, b.Revision!, selected.ExecutionPlan!.Hash,
            "Fixture implementer", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(applied.Errors); Assert.True(applied.TaskProgress[0].Execution!.Applied);
        b = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "complete", applied.PlanHash!, applied.Revision!, "Fixture implementer",
            taskId: "T1", evidence: "Verified the accepted acknowledgement against the unchanged exact prerequisite.", criteriaVerified: true);
        Assert.Empty(b.Errors); Assert.Equal("Complete", b.TaskProgress[0].Status);
        var request = service.Requests(f.Authority).Single();
        var records = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks");
        var aBytes = File.ReadAllBytes(Path.Combine(records, a.StoryId + ".json"));
        var bPath = Path.Combine(records, b.StoryId + ".json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var historical = JsonNode.Parse(File.ReadAllText(bPath))!; var basis = historical["basis"]!.AsObject();
        var removed = basis.Select(item => item.Key).Where(key => key.StartsWith("task prerequisite contract/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(removed); foreach (var key in removed) basis.Remove(key);
        var entries = basis.Select(item => new KeyValuePair<string, string>(item.Key, item.Value!.GetValue<string>())).OrderBy(item => item.Key, StringComparer.Ordinal);
        historical["scopeHash"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, options))));
        File.WriteAllText(bPath, historical.ToJsonString(options));
        var historicalBytes = File.ReadAllBytes(bPath);
        service = Fresh(); var retained = service.Story(f.Authority, "referrals", b.StoryId, false);
        Assert.Empty(retained.Errors); Assert.Equal("Needs review", retained.PlanState); Assert.True(retained.TaskProgress[0].Execution!.Applied);
        Assert.Contains(retained.Warnings, warning => warning.Contains("no exact material prerequisite basis", StringComparison.Ordinal));
        Assert.Equal(historicalBytes, File.ReadAllBytes(bPath));
        var proposalB = service.PrepareStory(f.Authority, new("referrals", b.StoryId, retained.InputHash!, retained.Revision!, "Fixture controller", retained.Tasks));
        Assert.Empty(proposalB.Errors);
        var renewed = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "approve", proposalB.PlanHash!, proposalB.Revision!, "Fixture reviewer", "Establish the exact unchanged prerequisite binding while retaining implemented work.");
        Assert.Empty(renewed.Errors); Assert.Equal("InProgress", renewed.TaskProgress[0].Status);
        Assert.True(renewed.TaskProgress[0].Execution!.Applied); Assert.True(renewed.TaskProgress[0].NeedsVerificationRenewal);
        Assert.Equal(b.Tasks[0].InstanceKey, renewed.Tasks[0].InstanceKey);
        Assert.Equal("Complete", Assert.Single(renewed.PreviousPlans).TaskProgress[0].Status);
        var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "complete", renewed.PlanHash!, renewed.Revision!, "Fixture implementer",
            taskId: "T1", evidence: "Re-verified every current criterion and the exact unchanged prerequisite binding against retained applied work.", criteriaVerified: true);
        Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.TaskProgress[0].Status); Assert.False(complete.TaskProgress[0].NeedsVerificationRenewal);
        Assert.Equal(aBytes, File.ReadAllBytes(Path.Combine(records, a.StoryId + ".json")));
        var before = f.Snapshot(); Assert.Equal("Complete", Fresh().Story(f.Authority, "referrals", b.StoryId, false).TaskProgress[0].Status);
        Assert.Equal(before, f.Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryPlanning_ContainerKeepsOwnPreparationAvailableAfterChildrenCompleteWithoutDuplicatingImplementation(bool preparationStarted)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader);
        var service = Fresh(); var parent = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", parent, false);
        var criterion = "The shared textual preparation preserves the accepted customer privacy behavior.";
        var preparation = FixtureContract("T1", [1]) with
        {
            Category = "wireframe",
            TaskTypeKey = "core.design.wireframe",
            TaskTypeVersion = "1.1",
            FrontendType = "customer",
            RequiredOutputs = ["wireframes.md"],
            AcceptanceCriteria = [criterion],
            VerificationObligations = [new("privacy-wireframe", [criterion], "Review the shared customer preparation.", "artifact", ["Current reviewed wireframes.md"])]
        };
        var proposal = service.PrepareStory(f.Authority, new("referrals", parent, read.InputHash!, read.Revision!, "Fixture controller",
            [preparation, FixtureContract("T2", [2])]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Retain preparation while child outcomes replace parent implementation.");
        Assert.Empty(approved.Errors);
        if (preparationStarted)
        {
            approved = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "start", approved.PlanHash!, approved.Revision!, "Fixture implementer", taskId: "T1");
            Assert.Empty(approved.Errors);
            // Supported legacy classification resolves from the exact maintained type/version.
            var request = service.Requests(f.Authority).Single();
            var path = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks", parent + ".json");
            var legacy = JsonNode.Parse(File.ReadAllText(path))!;
            legacy["plan"]!["tasks"]![0]!.AsObject().Remove("category");
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
            var basis = legacy["basis"]!.AsObject(); var contract = JsonNode.Parse(basis["task contract/T1"]!.GetValue<string>())!;
            contract.AsObject().Remove("category"); basis["task contract/T1"] = contract.ToJsonString(options);
            var entries = basis.Select(item => new KeyValuePair<string, string>(item.Key, item.Value!.GetValue<string>())).OrderBy(item => item.Key, StringComparer.Ordinal);
            legacy["scopeHash"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, options))));
            File.WriteAllText(path, legacy.ToJsonString(options));
            approved = Fresh().Story(f.Authority, "referrals", parent, false);
            Assert.Empty(approved.Errors); Assert.Equal("wireframe", approved.Tasks[0].Category); Assert.True(approved.TaskProgress[0].CanComplete);
        }
        var split = service.DecomposeStory(f.Authority, new("referrals", parent, approved.PlanningBasisHash!, approved.PlanningRevision, "Fixture controller",
            [new("retention", "Retain privacy versions", "Preserve exact accepted version retention.", [1], [], FixtureSize("low")),
             new("acknowledgement", "Retain acknowledgement", "Preserve the exact accepted acknowledgement contract.", [2], [], FixtureSize("low"))]));
        Assert.Empty(split.Errors);
        foreach (var child in split.ChildStories)
        {
            var story = service.Story(f.Authority, "referrals", child.Id!, false);
            var childProposal = service.PrepareStory(f.Authority, new("referrals", child.Id!, story.InputHash!, story.Revision!, "Fixture controller", [FixtureContract("T1", [1])]));
            Assert.Empty(childProposal.Errors);
            var childApproved = service.UpdateStoryWorkflow(f.Authority, "referrals", child.Id!, "approve", childProposal.PlanHash!, childProposal.Revision!, "Fixture reviewer", "Review the bounded child outcome.");
            Assert.Empty(childApproved.Errors);
            var childStarted = service.UpdateStoryWorkflow(f.Authority, "referrals", child.Id!, "start", childApproved.PlanHash!, childApproved.Revision!, "Fixture implementer", taskId: "T1");
            Assert.Empty(childStarted.Errors);
            var childComplete = service.UpdateStoryWorkflow(f.Authority, "referrals", child.Id!, "complete", childStarted.PlanHash!, childStarted.Revision!, "Fixture implementer",
                taskId: "T1", evidence: "Verified every accepted child criterion against current scoped output.", criteriaVerified: true);
            Assert.Empty(childComplete.Errors);
        }
        var current = Fresh().Story(f.Authority, "referrals", parent, false);
        Assert.Empty(current.Errors); Assert.Equal("Approved", current.PlanState);
        Assert.Equal(preparationStarted ? "InProgress" : "Ready", current.TaskProgress[0].Status);
        Assert.True(preparationStarted ? current.TaskProgress[0].CanComplete : current.TaskProgress[0].CanStart);
        Assert.False(current.TaskProgress[1].CanExecute);
        if (!preparationStarted)
        {
            current = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "start", current.PlanHash!, current.Revision!, "Fixture implementer", taskId: "T1");
            Assert.Empty(current.Errors);
        }
        var completed = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "complete", current.PlanHash!, current.Revision!, "Fixture implementer",
            taskId: "T1", evidence: "Verified the retained shared preparation artifact and every current criterion after child delivery.", criteriaVerified: true);
        Assert.Empty(completed.Errors); Assert.Equal("Complete", completed.PlanState);
        Assert.Equal("Complete", completed.TaskProgress[0].Status); Assert.Equal("Ready", completed.TaskProgress[1].Status);
        Assert.False(completed.TaskProgress[1].CanExecute);
        var before = f.Snapshot(); Assert.Equal("Complete", Fresh().Story(f.Authority, "referrals", parent, false).PlanState); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void StoryPlanning_MaterialPrerequisiteRevisionInvalidatesDownstreamCompletionUntilCurrentRenewal()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory
            + "| PRV-003 | The product shall publish the verified privacy contract representation. |\n"
            + "| PRV-004 | The product shall retain an independent privacy reference. |\n");
        CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader);
        var service = Fresh();
        var parent = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", parent, false);
        var decomposed = service.DecomposeStory(f.Authority, new("referrals", parent, read.PlanningBasisHash!, read.PlanningRevision, "Fixture controller",
            new[] { "retention", "acknowledgement", "publication", "reference" }.Select((key, index) =>
                new CisFeatureStoryChild(key, "Bounded privacy " + key, "Preserve the accepted privacy " + key + " outcome.", [index + 1], [], FixtureSize("low"))).ToArray()));
        Assert.Empty(decomposed.Errors);
        CisFeatureStoryResult Prepare(int child, params string[] dependencies)
        {
            var story = service.Story(f.Authority, "referrals", decomposed.ChildStories[child].Id!, false);
            var proposal = service.PrepareStory(f.Authority, new("referrals", story.StoryId, story.InputHash!, story.Revision!, "Fixture controller",
                [FixtureContract("T1", [1], dependencies: dependencies) with { RequiredOutputs = ["artifacts/privacy-" + child + ".md"] }]));
            Assert.Empty(proposal.Errors);
            var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", story.StoryId, "approve", proposal.PlanHash!, proposal.Revision!,
                "Fixture reviewer", "Review the bounded outcome and its exact prerequisite instance.");
            Assert.Empty(approved.Errors); return approved;
        }
        CisFeatureStoryResult Complete(string storyId)
        {
            var current = service.Story(f.Authority, "referrals", storyId, false);
            var started = service.UpdateStoryWorkflow(f.Authority, "referrals", storyId, "start", current.PlanHash!, current.Revision!, "Fixture implementer", taskId: "T1");
            Assert.Empty(started.Errors);
            var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", storyId, "complete", started.PlanHash!, started.Revision!, "Fixture implementer",
                taskId: "T1", evidence: "Explicitly verified the current scoped artifact and every criterion against its exact prerequisite contract.", criteriaVerified: true);
            Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.TaskProgress[0].Status); return complete;
        }
        var a = Prepare(0); var b = Prepare(1, a.StoryId + "/T1"); var c = Prepare(2, b.StoryId + "/T1"); var unrelated = Prepare(3);
        a = Complete(a.StoryId); b = Complete(b.StoryId); unrelated = Complete(unrelated.StoryId);
        Assert.True(service.Story(f.Authority, "referrals", c.StoryId, false).TaskProgress[0].CanExecute);
        c = Complete(c.StoryId);
        var request = service.Requests(f.Authority).Single();
        var records = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks");
        var originalB = File.ReadAllBytes(Path.Combine(records, b.StoryId + ".json"));
        var originalC = File.ReadAllBytes(Path.Combine(records, c.StoryId + ".json"));
        var originalUnrelated = File.ReadAllBytes(Path.Combine(records, unrelated.StoryId + ".json"));
        var revised = service.PrepareStory(f.Authority, new("referrals", a.StoryId, a.InputHash!, a.Revision!, "Fixture controller",
            [a.Tasks[0] with { RequiredOutputs = ["artifacts/privacy-retention-revised.md"] }]));
        Assert.Empty(revised.Errors); Assert.Equal(a.Tasks[0].InstanceKey, revised.Tasks[0].InstanceKey);
        var revisedA = service.UpdateStoryWorkflow(f.Authority, "referrals", a.StoryId, "approve", revised.PlanHash!, revised.Revision!, "Fixture reviewer", "Review the material retention output revision.");
        Assert.Empty(revisedA.Errors);
        service = Fresh();
        var affectedB = service.Story(f.Authority, "referrals", b.StoryId, false);
        Assert.Empty(affectedB.Errors); Assert.Equal("Needs review", affectedB.PlanState); Assert.NotEqual("Complete", affectedB.TaskProgress[0].Status);
        Assert.False(affectedB.TaskProgress[0].CanExecute); Assert.False(service.Story(f.Authority, "referrals", c.StoryId, false).TaskProgress[0].CanExecute);
        Assert.NotEqual("Complete", service.Story(f.Authority, "referrals", c.StoryId, false).TaskProgress[0].Status);
        Assert.Equal(originalB, File.ReadAllBytes(Path.Combine(records, b.StoryId + ".json")));
        Assert.Equal(originalC, File.ReadAllBytes(Path.Combine(records, c.StoryId + ".json")));
        Assert.Contains(JsonNode.Parse(originalB)!["events"]!.AsArray(), item => item!["status"]!.GetValue<string>() == "Complete");
        revisedA = Complete(a.StoryId);
        affectedB = Fresh().Story(f.Authority, "referrals", b.StoryId, false);
        Assert.Equal("Needs review", affectedB.PlanState); Assert.NotEqual("Complete", affectedB.TaskProgress[0].Status);
        Assert.False(Fresh().Story(f.Authority, "referrals", c.StoryId, false).TaskProgress[0].CanExecute);
        var renewedB = service.PrepareStory(f.Authority, new("referrals", b.StoryId, affectedB.InputHash!, affectedB.Revision!, "Fixture controller", affectedB.Tasks));
        Assert.Empty(renewedB.Errors); Assert.True(renewedB.IsRevisionProposal);
        renewedB = service.UpdateStoryWorkflow(f.Authority, "referrals", b.StoryId, "approve", renewedB.PlanHash!, renewedB.Revision!, "Fixture reviewer", "Renew the same acknowledgement instance against the changed exact retention contract.");
        Assert.Empty(renewedB.Errors); Assert.Equal(b.Tasks[0].InstanceKey, renewedB.Tasks[0].InstanceKey);
        Assert.Equal("Complete", Assert.Single(renewedB.PreviousPlans).TaskProgress[0].Status);
        Assert.NotEqual("Complete", renewedB.TaskProgress[0].Status);
        Complete(b.StoryId);
        var affectedC = Fresh().Story(f.Authority, "referrals", c.StoryId, false);
        Assert.Equal("Needs review", affectedC.PlanState); Assert.NotEqual("Complete", affectedC.TaskProgress[0].Status);
        Assert.Equal(originalC, File.ReadAllBytes(Path.Combine(records, c.StoryId + ".json")));
        var renewedC = service.PrepareStory(f.Authority, new("referrals", c.StoryId, affectedC.InputHash!, affectedC.Revision!, "Fixture controller", affectedC.Tasks));
        Assert.Empty(renewedC.Errors);
        renewedC = service.UpdateStoryWorkflow(f.Authority, "referrals", c.StoryId, "approve", renewedC.PlanHash!, renewedC.Revision!, "Fixture reviewer", "Renew the publication contract against its exact current prerequisite chain.");
        Assert.Empty(renewedC.Errors); Assert.Equal(c.Tasks[0].InstanceKey, renewedC.Tasks[0].InstanceKey);
        Assert.Equal("Complete", Assert.Single(renewedC.PreviousPlans).TaskProgress[0].Status);
        var releasedC = Fresh().Story(f.Authority, "referrals", c.StoryId, false);
        Assert.True(releasedC.TaskProgress[0].CanExecute); Assert.NotEqual("Complete", releasedC.TaskProgress[0].Status);
        Complete(c.StoryId);
        var retainedUnrelated = Fresh().Story(f.Authority, "referrals", unrelated.StoryId, false);
        Assert.Equal("Complete", retainedUnrelated.TaskProgress[0].Status); Assert.Equal(unrelated.PlanHash, retainedUnrelated.PlanHash);
        Assert.Equal(unrelated.Revision, retainedUnrelated.Revision); Assert.Equal(originalUnrelated, File.ReadAllBytes(Path.Combine(records, unrelated.StoryId + ".json")));
        var beforeRead = f.Snapshot();
        foreach (var child in decomposed.ChildStories) Assert.Empty(Fresh().Story(f.Authority, "referrals", child.Id!, false).Errors);
        Assert.Equal(beforeRead, f.Snapshot());
    }

    [Fact]
    public void StoryPlanning_RecursiveDecompositionRejectsAncestorOutcomeCycleBeforeWriteButAllowsExactPreparation()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader);
        var service = Fresh(); var parent = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", parent, false);
        var criterion = "The shared customer privacy preparation preserves the accepted retention and acknowledgement behavior.";
        var preparation = FixtureContract("T1", [1, 2]) with
        {
            Category = "wireframe",
            TaskTypeKey = "core.design.wireframe",
            TaskTypeVersion = "1.1",
            FrontendType = "customer",
            RequiredOutputs = ["wireframes.md"],
            AcceptanceCriteria = [criterion],
            VerificationObligations = [new("privacy-preparation", [criterion], "Review the shared customer preparation artifact.", "artifact", ["Current reviewed wireframes.md"])]
        };
        var proposal = service.PrepareStory(f.Authority, new("referrals", parent, read.InputHash!, read.Revision!, "Fixture controller", [preparation]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Retain exact shared preparation before recursive delivery.");
        Assert.Empty(approved.Errors);
        var first = service.DecomposeStory(f.Authority, new("referrals", parent, approved.PlanningBasisHash!, approved.PlanningRevision, "Fixture controller",
            [new("retention", "Retain versioned privacy", "Preserve the accepted versioned privacy document.", [1], [], FixtureSize("high")) { SharedPrerequisiteTaskIds = [parent + "/T1"] },
             new("acknowledgement", "Record exact acknowledgement", "Preserve the accepted UTC acknowledgement contract.", [2], [], FixtureSize("medium"))]));
        Assert.Empty(first.Errors);
        var a = service.Story(f.Authority, "referrals", first.ChildStories[0].Id!, false);
        CisFeatureStoryChild[] Children(bool cycle) =>
            [new("retained-version", "Retain exact version", "Preserve the accepted document version identity.", [1], cycle ? [parent] : [], FixtureSize("low"))
             { SharedPrerequisiteTaskIds = [parent + "/T1"] },
             new("version-reference", "Verify version reference", "Verify the same accepted version reference without new scope.", [1], [], FixtureSize("low"))
             { SharedPrerequisiteTaskIds = [parent + "/T1"] }];
        var before = f.Snapshot();
        var rejected = service.DecomposeStory(f.Authority, new("referrals", a.StoryId, a.PlanningBasisHash!, a.PlanningRevision, "Fixture controller", Children(true)));
        Assert.Contains(rejected.Errors, error => error.Contains("cycle", StringComparison.OrdinalIgnoreCase) && error.Contains(parent, StringComparison.Ordinal));
        Assert.Equal(before, f.Snapshot());
        var retained = Fresh().Story(f.Authority, "referrals", a.StoryId, false);
        Assert.Equal(a.PlanningRevision, retained.PlanningRevision); Assert.Empty(retained.ChildStories);
        var valid = service.DecomposeStory(f.Authority, new("referrals", a.StoryId, retained.PlanningBasisHash!, retained.PlanningRevision, "Fixture controller", Children(false)));
        Assert.Empty(valid.Errors); Assert.Equal(2, valid.ChildStories.Count);
        var x = Fresh().Story(f.Authority, "referrals", valid.ChildStories[0].Id!, false);
        var xProposal = service.PrepareStory(f.Authority, new("referrals", x.StoryId, x.InputHash!, x.Revision!, "Fixture controller", [FixtureContract("T1", [1])]));
        Assert.Empty(xProposal.Errors); Assert.Contains(parent + "/T1", xProposal.Tasks[0].DependsOn);
        var xApproved = service.UpdateStoryWorkflow(f.Authority, "referrals", x.StoryId, "approve", xProposal.PlanHash!, xProposal.Revision!, "Fixture reviewer", "Accept the leaf with its exact ancestor preparation reference.");
        Assert.Empty(xApproved.Errors); Assert.False(xApproved.TaskProgress[0].CanStart);
        var currentParent = service.Story(f.Authority, "referrals", parent, false);
        Assert.True(currentParent.TaskProgress[0].CanStart);
        var started = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "start", currentParent.PlanHash!, currentParent.Revision!, "Fixture controller", taskId: "T1");
        Assert.Empty(started.Errors);
        var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", parent, "complete", started.PlanHash!, started.Revision!, "Fixture controller",
            taskId: "T1", evidence: "Reviewed the shared preparation artifact against all accepted customer privacy criteria.", criteriaVerified: true);
        Assert.Empty(complete.Errors); Assert.NotEqual("Complete", complete.PlanState);
        Assert.True(Fresh().Story(f.Authority, "referrals", x.StoryId, false).TaskProgress[0].CanStart);
    }

    private static IReadOnlyList<CisFeatureStoryTask> ClassifyLegacyPrivacyTasks(IReadOnlyList<CisFeatureStoryTask> tasks)
        => tasks.Select(task => task with
        {
            Category = "backend",
            Complexity = "medium",
            TaskTypeKey = "core.backend.behavior",
            TaskTypeVersion = "1.0",
            ApprovalGate = "none",
            RequiredOutputs = ["Scoped privacy implementation and native regression"],
            Validation = "Verify the retained scoped privacy criteria through the native route.",
            VerificationObligations = [new("privacy-renewal", task.AcceptanceCriteria,
                "Verify each existing criterion against the now explicit task semantics.", "native", ["Current privacy native test and independent review evidence"])
            { SharedGateIds = ["unit"] }]
        }).ToArray();

    private static void RetainSevenFieldStoryRecord(string path)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var record = JsonNode.Parse(File.ReadAllText(path))!;
        var original = new HashSet<string>(["id", "title", "description", "repositoryIds", "requirementNumbers", "acceptanceCriteria", "dependsOn"], StringComparer.Ordinal);
        void Retain(JsonObject task)
        {
            foreach (var key in task.Select(item => item.Key).Where(key => !original.Contains(key)).ToArray()) task.Remove(key);
        }
        foreach (var task in record["plan"]!["tasks"]!.AsArray()) Retain(task!.AsObject());
        var basis = record["basis"]!.AsObject();
        basis.Remove("delivery boundaries/3b");
        foreach (var key in basis.Select(item => item.Key).Where(key => key.StartsWith("task contract/", StringComparison.Ordinal)).ToArray())
        {
            var task = JsonNode.Parse(basis[key]!.GetValue<string>())!.AsObject(); Retain(task);
            basis[key] = task.ToJsonString(options);
        }
        var entries = basis.Select(item => new KeyValuePair<string, string>(item.Key, item.Value!.GetValue<string>())).OrderBy(item => item.Key, StringComparer.Ordinal);
        record["scopeHash"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, options))));
        File.WriteAllText(path, record.ToJsonString(options));
    }

    [Fact]
    public void StoryPlanning_SharedWireframePreparationRemainsExecutableAfterParentDecomposition()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var preparation = FixtureContract("T1", [1]) with
        {
            Category = "wireframe",
            TaskTypeKey = "core.design.wireframe",
            TaskTypeVersion = "1.1",
            FrontendType = "customer",
            RequiredOutputs = ["wireframes.md"],
            VerificationObligations = [new("privacy-wireframe", FixtureContract("T1", [1]).AcceptanceCriteria,
                "Review the accepted customer behavior in the shared textual wireframe.", "artifact", ["Current reviewed wireframes.md"])]
        };
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, read.InputHash!, read.Revision!, "Fixture controller",
            [preparation, FixtureContract("T2", [2], dependencies: ["T1"])]));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Fixture reviewer", "Retain shared preparation before delivery.");
        Assert.Empty(approved.Errors);
        var decomposed = service.DecomposeStory(f.Authority, new("referrals", id, approved.PlanningBasisHash!, approved.PlanningRevision, "Fixture controller",
            [new("retention", "Retain privacy versions", "Preserve exact accepted version retention.", [1], [], FixtureSize("low")),
             new("acknowledgement", "Record acknowledgement", "Preserve exact accepted acknowledgement.", [2], [], FixtureSize("low"))
             { SharedPrerequisiteTaskIds = [id + "/T1"] }]));
        Assert.Empty(decomposed.Errors); Assert.True(decomposed.IsDeliveryContainer);
        var child = service.Story(f.Authority, "referrals", decomposed.ChildStories[1].Id!, false);
        var childProposal = service.PrepareStory(f.Authority, new("referrals", child.StoryId, child.InputHash!, child.Revision!, "Fixture controller", [FixtureContract("T1", [1])]));
        Assert.Empty(childProposal.Errors); Assert.Contains(id + "/T1", childProposal.Tasks[0].DependsOn);
        var childApproved = service.UpdateStoryWorkflow(f.Authority, "referrals", child.StoryId, "approve", childProposal.PlanHash!, childProposal.Revision!, "Fixture reviewer", "Accept the child with its exact shared preparation dependency.");
        Assert.Empty(childApproved.Errors); Assert.False(childApproved.TaskProgress[0].CanStart);
        var parent = service.Story(f.Authority, "referrals", id, false);
        Assert.True(parent.TaskProgress[0].CanStart); Assert.False(parent.TaskProgress[1].CanStart);
        Assert.Empty(reader.ValidateStoryTaskGate(f.Authority, parent.Tasks[0]));
        var started = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "start", parent.PlanHash!, parent.Revision!, "Fixture controller", taskId: "T1");
        Assert.Empty(started.Errors); Assert.True(started.TaskProgress[0].CanComplete);
        var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", started.PlanHash!, started.Revision!, "Fixture controller",
            taskId: "T1", evidence: "Reviewed synthetic customer wireframe behavior against the accepted privacy clauses.", criteriaVerified: true);
        Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.TaskProgress[0].Status);
        Assert.True(service.Story(f.Authority, "referrals", child.StoryId, false).TaskProgress[0].CanStart);
    }

    [Fact]
    public void StoryPlanning_UnacceptedRegenerationAllocatesChangedInstancesAndRemapsDependenciesAboveTwenty()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        var generation = new StoryTaskGeneration();
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], textGeneration: generation, taskContracts: reader);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var proposal = service.PrepareStory(f.Authority, new("referrals", id, read.InputHash!, read.Revision!, "Fixture controller",
            [FixtureContract("T19", [1]) with { InstanceKey = "fixture/privacy/retention" }, FixtureContract("T20", [2], dependencies: ["T19"])]));
        Assert.Empty(proposal.Errors);
        generation.ExplicitTasks = proposal.Tasks.Select(task => task with { InstanceKey = null }).ToArray();
        var same = service.Story(f.Authority, "referrals", id, true, proposal.InputHash, "test-local", "test");
        Assert.Empty(same.Errors); Assert.Equal(proposal.Tasks.Select(task => task.InstanceKey), same.Tasks.Select(task => task.InstanceKey));
        generation.ExplicitTasks = same.Tasks.Select(task => task with
        { InstanceKey = null, Description = task.Id == "T19" ? task.Description + " Inspect the exact archival path before implementation." : task.Description }).ToArray();
        var changed = service.Story(f.Authority, "referrals", id, true, same.InputHash, "test-local", "test");
        Assert.Empty(changed.Errors); Assert.Equal(new[] { "T21", "T22" }, changed.Tasks.Select(task => task.Id));
        Assert.Equal("T21", Assert.Single(changed.Tasks[1].DependsOn));
        Assert.DoesNotContain(changed.Tasks, task => proposal.Tasks.Any(old => old.InstanceKey == task.InstanceKey));
        var fresh = service.Story(f.Authority, "referrals", id, false);
        Assert.Equal(changed.Tasks.Select(task => task.InstanceKey), fresh.Tasks.Select(task => task.InstanceKey));
        Assert.Equal(2, generation.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryPlanning_LegacySemanticsRenewVerificationWithoutLosingAppliedWorkOrProgress(bool previouslyComplete)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }"); CreateIntake(f);
        var capture = new PlanningContractCapture();
        var service = DeliveryService(f, new StoryTaskGeneration(), capture);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var proposal = PrepareAssessedStory(service, f, id, read); Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Historical reviewer", "Accept the original scoped work.");
        Assert.Empty(approved.Errors);
        var selection = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        var executed = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, approved.PlanHash!, approved.Revision!, selection.ExecutionPlan!.Hash,
            "Historical implementer", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(executed.Errors); Assert.True(executed.TaskProgress[0].Execution!.Applied);
        if (previouslyComplete)
        {
            executed = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", executed.PlanHash!, executed.Revision!, "Historical implementer",
                taskId: "T1", evidence: "Retained historical native work and review before explicit classification.", criteriaVerified: true);
            Assert.Empty(executed.Errors);
        }
        var request = service.Requests(f.Authority).Single();
        var recordPath = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks", id + ".json");
        RetainSevenFieldStoryRecord(recordPath);
        File.WriteAllText(Path.Combine(f.Authority, ".cis/engineering-defaults.json"), """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], storyExecutor: capture, taskContracts: reader);
        var retained = service.Story(f.Authority, "referrals", id, false);
        Assert.Empty(retained.Errors); Assert.True(retained.TaskProgress[0].Execution!.Applied); Assert.False(retained.TaskProgress[0].CanExecute);
        var classified = ClassifyLegacyPrivacyTasks(retained.Tasks);
        var prepared = service.PrepareStory(f.Authority, new("referrals", id, retained.InputHash!, retained.Revision!, "Fixture controller", classified));
        Assert.Empty(prepared.Errors);
        var renewed = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", prepared.PlanHash!, prepared.Revision!, "Fixture reviewer", "Classify the same retained work without replacing its implementation.");
        Assert.Empty(renewed.Errors); Assert.Equal("InProgress", renewed.TaskProgress[0].Status);
        Assert.True(renewed.TaskProgress[0].Execution!.Applied); Assert.True(renewed.TaskProgress[0].NeedsVerificationRenewal);
        Assert.True(renewed.TaskProgress[0].CanExecute); Assert.True(renewed.TaskProgress[0].CanComplete);
        var missingProof = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", renewed.PlanHash!, renewed.Revision!, "Fixture controller",
            taskId: "T1", evidence: "A renewed completion still needs the adopted current engineering evidence.", criteriaVerified: true);
        Assert.Contains(missingProof.Errors, error => error.Contains("engineering completion service is unavailable", StringComparison.Ordinal));
        Assert.Equal(renewed.Revision, service.Story(f.Authority, "referrals", id, false).Revision);
        Assert.Equal(previouslyComplete ? "Complete" : "InProgress", Assert.Single(renewed.PreviousPlans).TaskProgress[0].Status);
        var verify = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        var verified = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, renewed.PlanHash!, renewed.Revision!, verify.ExecutionPlan!.Hash,
            "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(verified.Errors); Assert.True(capture.Observed!.NeedsVerificationRenewal); Assert.True(capture.Observed.PreviousExecution!.Applied);
        Assert.False(verified.TaskProgress[0].NeedsVerificationRenewal); Assert.True(verified.TaskProgress[0].CanComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryPlanning_HumanLegacyRenewalCompletesWithExplicitCurrentCriteriaVerificationWithoutAgent(bool previouslyComplete)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }"); CreateIntake(f);
        var service = DeliveryService(f, new StoryTaskGeneration());
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var proposal = PrepareAssessedStory(service, f, id, service.Story(f.Authority, "referrals", id, false));
        Assert.Empty(proposal.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", proposal.PlanHash!, proposal.Revision!, "Historical reviewer", "Accept the original human work contract.");
        Assert.Empty(approved.Errors);
        var started = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "start", approved.PlanHash!, approved.Revision!, "Human implementer", taskId: "T1");
        Assert.Empty(started.Errors);
        if (previouslyComplete)
        {
            var completed = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", started.PlanHash!, started.Revision!, "Human implementer",
                taskId: "T1", evidence: "Originally verified retained document behavior by inspecting each accepted criterion.", criteriaVerified: true);
            Assert.Empty(completed.Errors);
        }
        var request = service.Requests(f.Authority).Single();
        var recordPath = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks", id + ".json");
        RetainSevenFieldStoryRecord(recordPath);
        var reader = new PlanningService(null!, null!, null!, new CisRepositoryContextResolver(), workspaceRegistry: f.Registry);
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], taskContracts: reader);
        service = Fresh();
        var retained = service.Story(f.Authority, "referrals", id, false);
        var prepared = service.PrepareStory(f.Authority, new("referrals", id, retained.InputHash!, retained.Revision!, "Fixture controller", ClassifyLegacyPrivacyTasks(retained.Tasks)));
        Assert.Empty(prepared.Errors);
        var renewed = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", prepared.PlanHash!, prepared.Revision!, "Fixture reviewer", "Clarify the same human work without replacing its implementation.");
        Assert.Empty(renewed.Errors); Assert.Equal("InProgress", renewed.TaskProgress[0].Status);
        Assert.True(renewed.TaskProgress[0].NeedsVerificationRenewal); Assert.True(renewed.TaskProgress[0].CanComplete);
        Assert.Null(renewed.TaskProgress[0].Execution);
        Assert.Equal(previouslyComplete ? "Complete" : "InProgress", Assert.Single(renewed.PreviousPlans).TaskProgress[0].Status);
        var unverified = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", renewed.PlanHash!, renewed.Revision!, "Human implementer",
            taskId: "T1", evidence: "Current evidence must include explicit criteria verification.");
        Assert.NotEmpty(unverified.Errors); Assert.Equal(renewed.Revision, Fresh().Story(f.Authority, "referrals", id, false).Revision);
        var complete = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", renewed.PlanHash!, renewed.Revision!, "Human implementer",
            taskId: "T1", evidence: "Re-verified every current criterion against the retained document behavior and reviewed the explicit output/verification obligations.", criteriaVerified: true);
        Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.TaskProgress[0].Status);
        Assert.False(complete.TaskProgress[0].NeedsVerificationRenewal); Assert.Null(complete.TaskProgress[0].Execution);
        Assert.All(complete.CompletionContexts, context => Assert.Equal("not-adopted", context.AdoptionStatus));
        var before = f.Snapshot(); var current = Fresh().Story(f.Authority, "referrals", id, false);
        Assert.Equal("Complete", current.TaskProgress[0].Status); Assert.Single(current.PreviousPlans); Assert.Equal(before, f.Snapshot());
    }
}
