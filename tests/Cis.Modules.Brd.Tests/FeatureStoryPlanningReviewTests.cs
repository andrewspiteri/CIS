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
