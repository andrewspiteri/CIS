using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
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
