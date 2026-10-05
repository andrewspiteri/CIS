using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryExecutionRequiresApprovedCurrentPlanAndReviewBeforeHumanCompletion(bool previouslyStarted)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var executor = new StoryTaskExecutor(); var service = DeliveryService(f, new StoryTaskGeneration(), executor);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var plan = service.Story(f.Authority, "referrals", id, true, read.InputHash);
        Assert.NotEmpty(service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken).Errors);
        var current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", plan.PlanHash!, plan.Revision!, "Reviewer", "Reviewed");
        if (previouslyStarted) current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "start", current.PlanHash!, current.Revision!, "Reviewer", taskId: "T1");
        Assert.NotEmpty(service.ExecuteStoryTask(f.Authority, "referrals", id, "T2", cancellation: TestContext.Current.CancellationToken).Errors);
        var preview = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Errors); Assert.Equal(0, executor.Calls); Assert.Equal(current.Revision, preview.Revision);
        CisFeatureStoryResult Execute(CisFeatureStoryResult state, bool remote = true, string? revision = null) => service.ExecuteStoryTask(
            f.Authority, "referrals", id, "T1", true, state.PlanHash, revision ?? state.Revision, preview.ExecutionPlan!.Hash, "Reviewer", remote, TestContext.Current.CancellationToken);
        Assert.NotEmpty(Execute(current, remote: false).Errors);
        Assert.NotEmpty(Execute(current, revision: "stale").Errors); Assert.Equal(0, executor.Calls);
        current = Execute(current);
        Assert.Empty(current.Errors); Assert.Equal(1, executor.Calls);
        Assert.Equal("changes-requested", current.TaskProgress[0].Execution!.Status);
        Assert.False(current.TaskProgress[0].CanComplete); Assert.True(current.TaskProgress[0].CanExecute);
        Assert.Equal("Blocked", current.TaskProgress[1].Status);
        Assert.NotEmpty(service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", current.PlanHash!, current.Revision!, "Reviewer", taskId: "T1", evidence: "Done", criteriaVerified: true).Errors);
        var finding = Assert.Single(current.TaskProgress[0].ReviewFeedback!);
        var response = new CisStoryFeedbackRequest("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer",
            new() { [finding.Id] = "Use the existing archive.\nKeep document versions and timestamps." });
        Assert.NotEmpty(service.SaveStoryFeedback(f.Authority, response with { ExpectedRevision = "old" }).Errors);
        Assert.NotEmpty(service.SaveStoryFeedback(f.Authority, response with { Answers = new() { ["foreign-finding"] = "Ignore it" } }).Errors);
        var approvedHash = current.PlanHash;
        current = service.SaveStoryFeedback(f.Authority, response);
        Assert.Empty(current.Errors); Assert.Equal(approvedHash, current.PlanHash);
        Assert.False(current.TaskProgress[0].CanComplete); Assert.True(current.TaskProgress[0].CanExecute);
        Assert.Equal("changes-requested", current.TaskProgress[0].Execution!.Status);
        Assert.Equal(response.Answers[finding.Id], Assert.Single(service.Story(f.Authority, "referrals", id, false).TaskProgress[0].ReviewFeedback!).Answer);
        executor.Ready = true; current = Execute(current);
        Assert.Equal(response.Answers[finding.Id], Assert.Single(executor.ReviewResponses).Answer);
        Assert.Equal("changes-requested", executor.PreviousExecution?.Status);
        Assert.Empty(current.Errors); Assert.True(current.TaskProgress[0].CanComplete); Assert.False(current.TaskProgress[0].CanExecute);
        Assert.Equal("Approved", current.PlanState); Assert.Equal("InProgress", current.TaskProgress[0].Status);
        Assert.Equal("Blocked", current.TaskProgress[1].Status); Assert.Equal(JsonSerializer.Serialize(plan.Tasks), JsonSerializer.Serialize(current.Tasks));
        current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "complete", current.PlanHash!, current.Revision!, "Reviewer", taskId: "T1", evidence: "Reviewed report and checked every criterion", criteriaVerified: true);
        Assert.Empty(current.Errors); Assert.True(current.TaskProgress[1].CanExecute);
        var completedRevision = current.Revision;
        var requestPath = service.Wizard(f.Authority, "referrals").Plan!.RequestPath;
        File.AppendAllText(Path.Combine(f.Authority, requestPath), "\nScope updated after execution.\n");
        var retained = service.Story(f.Authority, "referrals", id, false);
        Assert.Equal("stale", retained.Status); Assert.Equal("Needs review", retained.PlanState);
        Assert.Equal("Complete", retained.TaskProgress[0].Status);
        Assert.NotNull(retained.TaskProgress[0].Execution);
        Assert.Equal("reviewed", retained.TaskProgress[0].Execution!.Status);
        Assert.Contains("checked every criterion", retained.TaskProgress[0].Evidence);
        Assert.All(retained.TaskProgress, task => { Assert.False(task.CanExecute); Assert.False(task.CanComplete); Assert.False(task.CanStart); });
        Assert.Equal(completedRevision, retained.Revision);
        Assert.Equal(plan.Definition, retained.Definition);
        Assert.Equal(plan.Tasks.Count, service.Navigation(f.Authority).Features.Single().Stories.Single().Tasks.Count);
        Assert.NotEmpty(service.ExecuteStoryTask(f.Authority, "referrals", id, "T2", cancellation: TestContext.Current.CancellationToken).Errors);
    }

    private sealed class StoryTaskExecutor : ICisStoryTaskExecutor
    {
        public int Calls { get; private set; }
        public bool Ready { get; set; }
        public CisStoryExecutionResult? PreviousExecution { get; private set; }
        public IReadOnlyList<CisStoryReviewFeedback> ReviewResponses { get; private set; } = [];
        public CisStoryExecutionPlan SelectModels(CisStoryTaskWork work) => new("execution-hash", "Low", [],
            new("codex", "gpt-6-luna", false), new("codex", "gpt-6-astra", false), []);
        public CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote, Func<bool> stillCurrent,
            CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress)
        {
            Calls++; Assert.True(allowRemote); Assert.True(stillCurrent());
            PreviousExecution = work.PreviousExecution;
            ReviewResponses = work.ReviewResponses;
            Assert.NotEmpty(work.StoryCriteria); Assert.Empty(work.Repositories);
            return new(Ready ? "reviewed" : "changes-requested", selection, [], [], Ready ? [] : ["Missing acceptance evidence."], [], Ready);
        }
    }

    [Fact]
    public void StoryWorkflowRequiresApprovalDependenciesAndVerifiedCompletion()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var code = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var service = DeliveryService(f, new StoryTaskGeneration());
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var plan = service.Story(f.Authority, "referrals", id, true, read.InputHash);
        Assert.Empty(plan.Errors); Assert.Equal("Proposed", plan.PlanState);
        Assert.Equal(2, service.Navigation(f.Authority).Features.Single().Stories.Single().Tasks.Count);
        CisFeatureStoryResult Update(CisFeatureStoryResult current, string operation, string? task = null, string? evidence = null, bool verified = false)
            => service.UpdateStoryWorkflow(f.Authority, "referrals", id, operation, current.PlanHash!, current.Revision!, "Reviewer", "Reviewed task scope and criteria", task, evidence, verified);
        Assert.NotEmpty(Update(plan, "start", "T1").Errors);
        var approved = Update(plan, "approve");
        Assert.Empty(approved.Errors); Assert.Equal("Approved", approved.PlanState); Assert.Equal("Reviewer", approved.ApprovedBy);
        Assert.Equal(new[] { "Ready", "Blocked" }, approved.TaskProgress.Select(task => task.Status));
        Assert.NotEmpty(Update(approved, "start", "T2").Errors);
        Assert.NotEmpty(Update(plan, "start", "T1").Errors); // Concurrent/stale screen.
        Assert.NotEmpty(service.Story(f.Authority, "referrals", id, true, approved.InputHash, "test-local", "stronger").Errors);
        var started = Update(approved, "start", "T1");
        Assert.Empty(started.Errors); Assert.Equal("InProgress", started.TaskProgress[0].Status);
        Assert.NotEmpty(Update(started, "complete", "T1", "Checked retention").Errors);
        Assert.NotEmpty(Update(started, "complete", "T1", "", true).Errors);
        // Delivery changes implementation; progress and the approved plan must survive it,
        // including loss of the disposable generation cache.
        File.AppendAllText(code, "\nexport const versionRetained = true;");
        File.Delete(Path.Combine(f.Authority, ".cis/local/feature-stories/referrals", id + ".json"));
        var reopened = service.Story(f.Authority, "referrals", id, false);
        Assert.Equal("Approved", reopened.PlanState); Assert.Equal(started.PlanHash, reopened.PlanHash);
        var firstComplete = Update(reopened, "complete", "T1", "Reviewed archive.ts and verified version retention.", true);
        Assert.Empty(firstComplete.Errors); Assert.True(firstComplete.TaskProgress[1].CanStart);
        var secondStarted = Update(firstComplete, "start", "T2");
        var complete = Update(secondStarted, "complete", "T2", "Acknowledgement tests passed.", true);
        Assert.Empty(complete.Errors); Assert.Equal("Complete", complete.PlanState);
        Assert.All(complete.TaskProgress, task => Assert.Equal("Complete", task.Status));
        Assert.All(service.Navigation(f.Authority).Features.Single().Stories.Single().Tasks, task => Assert.Equal("Complete", task.Status));
    }

    [Fact]
    public void StoryWorkflowRejectsChangedScopeAndWrongPlanDigest()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var service = DeliveryService(f, new StoryTaskGeneration());
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var plan = service.Story(f.Authority, "referrals", id, true, read.InputHash);
        Assert.NotEmpty(service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", "wrong", plan.Revision!, "Reviewer", "Reviewed").Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", plan.PlanHash!, plan.Revision!, "Reviewer", "Reviewed");
        Assert.Empty(approved.Errors);
        var request = service.Wizard(f.Authority, "referrals").Plan!.RequestPath;
        File.AppendAllText(Path.Combine(f.Authority, request), "\nAdditional scope requires another review.\n");
        Assert.NotEmpty(service.UpdateStoryWorkflow(f.Authority, "referrals", id, "start", approved.PlanHash!, approved.Revision!, "Reviewer", taskId: "T1").Errors);
        Assert.NotEmpty(service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", approved.PlanHash!, approved.Revision!, "Reviewer", "Reviewed old plan").Errors);
        Assert.NotEqual("Approved", service.Story(f.Authority, "referrals", id, false).PlanState);
    }

    [Fact]
    public void StoryDefinesAcceptanceAndPersistsTracedTasksWithoutChangingFeatureApproval()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n## Constraints\n\nPreserve UTC timestamps in every acknowledgement.\n");
        var code = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f);
        SelectTechnicalDocument(f, ImportedTechnicalDirection);
        var generation = new StoryTaskGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals");
        var original = File.ReadAllText(Path.Combine(f.Authority, wizard.Plan!.RequestPath));
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var before = service.Story(f.Authority, "referrals", id, false);
        Assert.Empty(before.Errors); Assert.Equal("missing", before.Status);
        Assert.NotEmpty(before.Definition); Assert.Equal(2, before.Story!.Requirements.Count);
        Assert.Empty(before.Tasks); Assert.Equal(0, generation.Calls);
        Assert.NotEmpty(service.Story(f.Authority, "referrals", id, true, "old-hash").Errors);
        Assert.Equal(0, generation.Calls);
        var prepared = service.Story(f.Authority, "referrals", id, true, before.InputHash);
        Assert.Empty(prepared.Errors); Assert.Equal("current", prepared.Status);
        Assert.Equal(2, prepared.Tasks.Count);
        Assert.Empty(prepared.Tasks[0].RepositoryIds);
        Assert.Equal(new[] { "backend", "customer-ui" }, prepared.Tasks[1].RepositoryIds);
        Assert.Equal(new[] { "T1" }, prepared.Tasks[1].DependsOn);
        Assert.All(prepared.Tasks, task => Assert.NotEmpty(task.AcceptanceCriteria));
        Assert.False(generation.Request!.AllowRemote);
        Assert.Contains("versioned Azure Storage document", generation.Request.Prompt);
        Assert.Contains("Preserve UTC timestamps in every acknowledgement", generation.Request.Prompt);
        Assert.Contains("Inherited technical direction", generation.Request.Prompt);
        Assert.Contains("capture gaps and spool capacity", generation.Request.Prompt);
        Assert.Contains(".NET 10", generation.Request.Prompt);
        Assert.Contains(wizard.SourceBrds.Single().Hash, generation.Request.Prompt);
        var reopened = DeliveryService(f, generation).Story(f.Authority, "referrals", id, false);
        Assert.True(reopened.Cached); Assert.Equal(1, generation.Calls);
        Assert.Equal(prepared.Tasks[1].Title, reopened.Tasks[1].Title);
        var navigation = Assert.Single(Assert.Single(service.Navigation(f.Authority).Features).Stories);
        Assert.Equal(new[] { "backend", "customer-ui" }, navigation.RepositoryIds);
        Assert.Equal("2 tasks proposed", navigation.Status);
        Assert.Equal(original, File.ReadAllText(Path.Combine(f.Authority, wizard.Plan.RequestPath)));
        File.AppendAllText(code, "\nexport const changed = true;");
        Assert.Equal("stale", service.Story(f.Authority, "referrals", id, false).Status);
        Assert.Empty(Assert.Single(Assert.Single(service.Navigation(f.Authority).Features).Stories).RepositoryIds);
        Assert.NotEmpty(service.Story(f.Authority, "referrals", "../../other", false).Errors);
    }

    [Theory]
    [InlineData("foreign-repository")]
    [InlineData("cycle")]
    [InlineData("missing-acceptance")]
    [InlineData("missing-coverage")]
    [InlineData("duplicate-repositories")]
    [InlineData("duplicate-requirements")]
    [InlineData("duplicate-acceptance")]
    [InlineData("duplicate-dependencies")]
    [InlineData("remote")]
    public void StoryRejectsUnsupportedOrIncompleteTaskPlans(string mode)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var generation = new StoryTaskGeneration { Mode = mode }; var service = DeliveryService(f, generation);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var before = service.Story(f.Authority, "referrals", id, false);
        Assert.NotEmpty(service.Story(f.Authority, "referrals", id, true, before.InputHash).Errors);
        Assert.Equal("missing", service.Story(f.Authority, "referrals", id, false).Status);
        if (mode == "remote") Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void StoryGenerationRejectsConcurrentEvidenceChanges()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var code = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f);
        var generation = new StoryTaskGeneration { DuringGeneration = () => File.AppendAllText(code, "\n// implementation changed") };
        var service = DeliveryService(f, generation);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var before = service.Story(f.Authority, "referrals", id, false);
        Assert.Contains(service.Story(f.Authority, "referrals", id, true, before.InputHash).Errors, error => error.Contains("changed during", StringComparison.Ordinal));
        Assert.Equal("missing", service.Story(f.Authority, "referrals", id, false).Status);
    }

    [Theory]
    [InlineData("forward")]
    [InlineData("repair")]
    public void StoryOrdersValidDependenciesAndCorrectsOneInvalidResponse(string mode)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var generation = new StoryTaskGeneration { Mode = mode }; var service = DeliveryService(f, generation);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var before = service.Story(f.Authority, "referrals", id, false);
        Assert.Equal("test-local", before.Provider); Assert.Equal("test", before.Model);
        var result = service.Story(f.Authority, "referrals", id, true, before.InputHash);
        Assert.Empty(result.Errors);
        Assert.Equal(mode == "forward" ? "T2" : "T1", result.Tasks[0].Id);
        Assert.Equal(mode == "forward" ? 1 : 2, generation.Calls);
        if (mode == "repair") Assert.Contains("needs 1 to 10 nonempty completion criteria", generation.Request!.Prompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryUsesSelectedStrongerModelAndRequiresAuthorizationForRemote(bool remote)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var generation = new StoryTaskGeneration { Mode = remote ? "remote" : "valid" }; var service = DeliveryService(f, generation);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var before = service.Story(f.Authority, "referrals", id, false);
        if (remote)
        {
            var denied = service.Story(f.Authority, "referrals", id, true, before.InputHash, "test-local", "stronger");
            Assert.Contains(denied.Errors, error => error.Contains("Authorize sending", StringComparison.Ordinal));
            Assert.Equal("stronger", denied.Model);
            Assert.Equal(0, generation.Calls);
        }
        var result = service.Story(f.Authority, "referrals", id, true, before.InputHash, "test-local", "stronger", remote);
        Assert.Empty(result.Errors); Assert.Equal("stronger", result.Model);
        Assert.Equal("stronger", generation.Request!.Model); Assert.Equal(remote, generation.Request.AllowRemote);
        using var schema = JsonDocument.Parse(generation.Request.JsonSchema!);
        AssertSchemaSupportsStructuredOutput(schema.RootElement);
        Assert.Equal("stronger", service.Story(f.Authority, "referrals", id, false).Model);
        var repeated = service.Story(f.Authority, "referrals", id, true, before.InputHash, "test-local", "test", remote);
        Assert.Empty(repeated.Errors); Assert.Equal("test", repeated.Model); Assert.Equal(2, generation.Calls);
        Assert.NotEmpty(service.Story(f.Authority, "referrals", id, true, before.InputHash, "test-local", "unknown", remote).Errors);
        Assert.Equal(2, generation.Calls);
    }

    private static void AssertSchemaSupportsStructuredOutput(JsonElement schema)
    {
        Assert.False(schema.TryGetProperty("uniqueItems", out _));
        if (schema.TryGetProperty("properties", out var properties))
        {
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(properties.EnumerateObject().Select(property => property.Name).Order(),
                schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Order());
            foreach (var property in properties.EnumerateObject()) AssertSchemaSupportsStructuredOutput(property.Value);
        }
        if (schema.TryGetProperty("items", out var items)) AssertSchemaSupportsStructuredOutput(items);
    }

    private sealed class StoryTaskGeneration : ICisTextGenerationService
    {
        public string Mode { get; init; } = "valid";
        public int Calls { get; private set; }
        public Action? DuringGeneration { get; init; }
        public CisTextGenerationRequest? Request { get; private set; }
        public CisAiStatus GetStatus() => new([new("test-local", "available", "http://localhost", Mode != "remote", [new("test", 1), new("stronger", 100)], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++; Request = request;
            var missingAcceptance = Mode == "missing-acceptance" || Mode == "repair" && Calls == 1;
            CisFeatureStoryTask[] tasks = [
                new("T1", "Confirm document retention behavior", "Inspect version retention against the privacy requirement.", [], [1],
                    missingAcceptance ? [] : ["Record the supported version retention behavior."], Mode is "cycle" or "forward" ? ["T2"] : []),
                new("T2", "Record acknowledgement", "Implement and verify acknowledgement timestamp and document version.",
                    Mode == "foreign-repository" ? ["unknown"] : ["backend", "customer-ui"], Mode == "missing-coverage" ? [1] : [2],
                    ["An acknowledgement retains its timestamp and the exact document version."], Mode == "forward" ? [] : ["T1"]),
            ];
            tasks[1] = Mode switch
            {
                "duplicate-repositories" => tasks[1] with { RepositoryIds = ["backend", "backend"] },
                "duplicate-requirements" => tasks[1] with { RequirementNumbers = [2, 2] },
                "duplicate-acceptance" => tasks[1] with { AcceptanceCriteria = [tasks[1].AcceptanceCriteria[0], tasks[1].AcceptanceCriteria[0]] },
                "duplicate-dependencies" => tasks[1] with { DependsOn = ["T1", "T1"] },
                _ => tasks[1]
            };
            DuringGeneration?.Invoke();
            return new("generated", "test-local", request.Model!, JsonSerializer.Serialize(new { tasks }), null, Mode != "remote");
        }
    }
}
