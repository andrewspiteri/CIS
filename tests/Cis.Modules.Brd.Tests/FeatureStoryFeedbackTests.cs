using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData("implementation")]
    [InlineData("review-evidence")]
    [InlineData("severity")]
    [InlineData("omitted")]
    public void FindingResponsesSurviveRunIdsOrderingAndUnrelatedCodeButRespectChangedMaterialBasis(string change)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        var unrelated = Path.Combine(Path.GetDirectoryName(archive)!, "weather.ts");
        File.WriteAllText(unrelated, "export class Weather { getForecast() {} }");
        var omitted = Path.Combine(Path.GetDirectoryName(archive)!, "unsearched.ts");
        if (change == "omitted") File.WriteAllText(omitted, new string('x', 262_145));
        CreateIntake(f); var executor = new RepeatingFindingExecutor(); var service = DeliveryService(f, new StoryTaskGeneration(), executor);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var plan = service.Story(f.Authority, "referrals", id, true, read.InputHash);
        var current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", plan.PlanHash!, plan.Revision!, "Reviewer", "Reviewed");
        CisFeatureStoryResult Execute(CisFeatureStoryResult state)
        {
            var preview = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
            Assert.Empty(preview.Errors);
            var result = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, state.PlanHash, state.Revision,
                preview.ExecutionPlan!.Hash, "Reviewer", true, TestContext.Current.CancellationToken);
            Assert.Empty(result.Errors); return result;
        }
        current = Execute(current);
        var finding = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Finding.StartsWith("High:", StringComparison.Ordinal));
        const string answer = "Keep the archive owner and verify retained document versions before completion.";
        Assert.NotEmpty(service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer", new() { [finding.Id] = answer })).Errors);
        current = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer", new() { [finding.Id] = answer })
        { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = finding.BasisHash! } });
        Assert.Empty(current.Errors);
        Assert.True(current.Applied);
        var accepted = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id);
        Assert.Equal("current", accepted.ApplicabilityState); Assert.Equal(answer, accepted.Answer);
        var unchanged = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, " Reviewer ", new() { [finding.Id] = " " + answer + " " })
        { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = accepted.BasisHash! } });
        Assert.Empty(unchanged.Errors); Assert.Equal(current.Revision, unchanged.Revision);
        Assert.False(unchanged.Applied);
        Assert.Equal(accepted.SavedAt, unchanged.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id).SavedAt);
        File.AppendAllText(unrelated, "\nexport const forecastDays = 10;");
        executor.Reverse = true; current = Execute(unchanged);
        var repeated = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id);
        Assert.Equal(answer, repeated.Answer); Assert.Equal(accepted.Actor, repeated.Actor); Assert.Equal(accepted.SavedAt, repeated.SavedAt);
        Assert.Equal(new[] { "review-1", "review-2" }, repeated.RunIds);
        Assert.False(current.TaskProgress[0].CanComplete); Assert.Equal("Blocked", current.TaskProgress[1].Status);
        executor.DuplicateEvidence = true; current = Execute(current);
        var duplicate = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id);
        Assert.Equal(answer, duplicate.Answer); Assert.Equal(accepted.Actor, duplicate.Actor); Assert.Equal(accepted.SavedAt, duplicate.SavedAt);
        Assert.Equal(new[] { "review-1", "review-2", "review-3", "review-3-duplicate" }, duplicate.RunIds);
        Assert.Equal("current", duplicate.ApplicabilityState); Assert.False(current.TaskProgress[0].CanComplete);
        if (change == "implementation")
        {
            File.AppendAllText(archive, "\nexport const documentVersionChanged = true;");
            var stale = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer", new() { [finding.Id] = answer })
            { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = repeated.BasisHash! } });
            Assert.NotEmpty(stale.Errors); Assert.Contains(stale.Errors, error => error.Contains("material basis changed", StringComparison.Ordinal));
        }
        if (change == "review-evidence") executor.Validation = "Version-retention assertion failed for the changed contract.";
        if (change == "severity") executor.Severity = "Critical";
        if (change == "omitted") File.AppendAllText(omitted, "changed");
        current = Execute(current);
        if (change == "severity")
        {
            var changed = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Finding.StartsWith("Critical:", StringComparison.Ordinal));
            Assert.NotEqual(finding.Id, changed.Id); Assert.Null(changed.Answer);
        }
        else
        {
            var changed = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id);
            Assert.Null(changed.Answer); Assert.Equal(answer, changed.RetainedAnswer);
            Assert.Equal(accepted.Actor, changed.Actor); Assert.Equal(accepted.SavedAt, changed.SavedAt);
            Assert.Equal("needs-review", changed.ApplicabilityState); Assert.Contains("changed", changed.ApplicabilityReason);
            if (change == "omitted")
            {
                current = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer", new() { [finding.Id] = answer })
                { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = changed.BasisHash! } });
                Assert.Empty(current.Errors); Assert.True(current.Applied);
                var renewed = current.TaskProgress[0].ReviewFeedback!.Single(item => item.Id == finding.Id);
                Assert.Equal(answer, renewed.Answer); Assert.Equal("current", renewed.ApplicabilityState);
                Assert.Contains("coverage remains uncertain", renewed.ApplicabilityReason);
                var noop = service.SaveStoryFeedback(f.Authority, new("referrals", id, "T1", current.PlanHash!, current.Revision!, "Reviewer", new() { [finding.Id] = answer })
                { ExpectedBasisHashes = new Dictionary<string, string> { [finding.Id] = renewed.BasisHash! } });
                Assert.Empty(noop.Errors); Assert.False(noop.Applied); Assert.Equal(current.Revision, noop.Revision);
            }
        }
        Assert.False(current.TaskProgress[0].CanComplete); Assert.False(current.TaskProgress[0].Execution!.Applied);
    }

    private sealed class RepeatingFindingExecutor : ICisStoryTaskExecutor
    {
        private int calls;
        public bool Reverse { get; set; }
        public bool DuplicateEvidence { get; set; }
        public string Validation { get; set; } = "Version-retention assertion remains unverified.";
        public string Severity { get; set; } = "High";
        public CisStoryExecutionPlan SelectModels(CisStoryTaskWork work) => new("execution-hash", "Low", [],
            new("codex", "gpt-6-luna", false), new("codex", "gpt-6-astra", false), []);
        public CisStoryExecutionResult Execute(CisStoryTaskWork work, CisStoryExecutionPlan selection, bool allowRemote, Func<bool> stillCurrent,
            CancellationToken cancellationToken, Action<CisAgentProviderEvent>? progress)
        {
            calls++; Assert.True(stillCurrent());
            string[] findings = [Severity + ": Missing acceptance evidence.", "Medium: Acknowledgement timestamp still needs verification."];
            if (Reverse) Array.Reverse(findings);
            var run = new CisStoryExecutionRun("review-" + calls, "backend", "review", "codex", "test", "changes-requested", "", "Review of document retention.", [Validation]);
            return new("changes-requested", selection,
                DuplicateEvidence ? [run, run with { RunId = run.RunId + "-duplicate", Round = 2 }] : [run],
                [], findings, [], false);
        }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("unsupported")]
    [InlineData("empty")]
    [InlineData("stale")]
    [InlineData("remote")]
    public void StoryFeedbackSuggestionsRequireEvidenceAndConsentAndNeverBecomeHumanAnswers(string mode)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f); var executor = new StoryTaskExecutor(); var service = DeliveryService(f, new StoryTaskGeneration(), executor);
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var read = service.Story(f.Authority, "referrals", id, false);
        var plan = service.Story(f.Authority, "referrals", id, true, read.InputHash);
        var current = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", plan.PlanHash!, plan.Revision!, "Reviewer", "Reviewed");
        var preview = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        current = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, current.PlanHash, current.Revision,
            preview.ExecutionPlan!.Hash, "Reviewer", true, TestContext.Current.CancellationToken);
        Assert.Empty(current.Errors);
        var request = Path.Combine(f.Authority, service.Wizard(f.Authority, "referrals").Plan!.RequestPath);
        var generation = new FeedbackGeneration(mode) { DuringGeneration = mode == "stale" ? () => File.AppendAllText(request, "\nChanged scope.\n") : null };
        service = DeliveryService(f, generation, executor);
        if (mode == "remote")
        {
            Assert.NotEmpty(service.SuggestStoryFeedback(f.Authority, "referrals", id, "T1", current.Revision!, "test-feedback", "strong").Errors);
            Assert.Equal(0, generation.Calls);
        }
        var result = service.SuggestStoryFeedback(f.Authority, "referrals", id, "T1", current.Revision!, "test-feedback", "strong", mode == "remote");
        if (mode is "unsupported" or "stale") { Assert.NotEmpty(result.Errors); return; }
        Assert.Empty(result.Errors); Assert.Equal(current.Revision, result.Revision); Assert.Equal(current.PlanHash, result.PlanHash);
        var feedback = Assert.Single(result.TaskProgress[0].ReviewFeedback!);
        Assert.Null(feedback.Answer); Assert.Null(feedback.Actor);
        Assert.False(result.TaskProgress[0].CanComplete);
        Assert.Equal(mode != "empty", feedback.SuggestedAnswer is not null);
        Assert.Equal(feedback.SuggestedAnswer, Assert.Single(service.Story(f.Authority, "referrals", id, false).TaskProgress[0].ReviewFeedback!).SuggestedAnswer);
        Assert.Equal(1, generation.Calls);
    }

    private sealed class FeedbackGeneration(string mode) : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public Action? DuringGeneration { get; init; }
        public CisAiStatus GetStatus() => new([new("test-feedback", "available", "test", mode != "remote", [new("strong")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++;
            var start = request.Prompt.IndexOf("<review-context>\n", StringComparison.Ordinal) + "<review-context>\n".Length;
            var end = request.Prompt.IndexOf("\n</review-context>", start, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(request.Prompt[start..end]);
            var root = json.RootElement;
            var id = root.GetProperty("questions")[0].GetProperty("id").GetString();
            var evidence = mode == "unsupported" ? "A quotation that is not present in any supplied document." : root.GetProperty("evidence").GetString()![..60];
            object[] answers = mode == "empty" ? [] : [new { id, answer = "Keep the existing document archive and verify retained versions.", evidence }];
            DuringGeneration?.Invoke();
            return new("generated", "test-feedback", "strong", JsonSerializer.Serialize(new { answers }), null, mode != "remote");
        }
    }
}
