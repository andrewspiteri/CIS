using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class RepositoryGuidanceReviewSessionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cis-review-checkpoints-").FullName;

    [Fact]
    public void ExpiryKeepsFirstPassAndResumeRunsOnlyTheMissingAudit()
    {
        var elapsed = TimeSpan.Zero;
        var model = new Model { AfterCall = _ => elapsed = TimeSpan.FromMinutes(10) };
        var first = new RepositoryGuidanceReviewSession(_directory, elapsed: () => elapsed);
        var merge = Proposal();
        var partial = new RepositoryGuidanceReconciler(model, new("test", "strong", true), session: first).Reconcile(merge);
        Assert.True(first.Paused);
        Assert.Equal("paused", partial.GuidanceReview!.Status);
        Assert.Equal(1, partial.GuidanceReview.CompletedPasses);
        Assert.Single(model.Requests);
        Assert.Single(Directory.GetFiles(CacheDirectory));

        model.AfterCall = null;
        var completed = new RepositoryGuidanceReconciler(model, new("test", "strong", true),
            session: new(_directory)).Reconcile(merge);
        Assert.Equal("complete", completed.GuidanceReview!.Status);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("SECOND PASS", model.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(CacheDirectory).Length);

        var expired = new RepositoryGuidanceReviewSession(_directory, elapsed: () => TimeSpan.FromHours(1));
        var reused = new RepositoryGuidanceReconciler(model, new("test", "strong", true), session: expired).Reconcile(merge);
        Assert.Equal("complete", reused.GuidanceReview!.Status);
        Assert.False(expired.Paused);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public void TimeoutsUseRemainingBudgetAndNoQueuedFilesStartAfterExpiry()
    {
        var elapsed = TimeSpan.Zero;
        var model = new Model { Local = true, AfterCall = request => elapsed += TimeSpan.FromSeconds(request.TimeoutSeconds) };
        var session = new RepositoryGuidanceReviewSession(budget: TimeSpan.FromSeconds(450), elapsed: () => elapsed);
        var results = new RepositoryGuidanceReviewRunner(model, new("test", "strong"), session: session)
            .Reconcile(Enumerable.Range(0, 4).Select(index => Proposal() with { RelativePath = $".github/instructions/{index}.instructions.md" }).ToArray());
        Assert.Equal(new[] { 300, 150 }, model.Requests.Select(request => request.TimeoutSeconds));
        Assert.True(session.Paused);
        Assert.Equal("complete", results[0].GuidanceReview!.Status);
        Assert.All(results.Skip(1), result => Assert.Equal("paused", result.GuidanceReview!.Status));
    }

    [Fact]
    public void FullEvidenceRevisionAndModelChangesInvalidateSavedPasses()
    {
        var model = new Model();
        var merge = Proposal();
        Reconcile(model, merge);
        Assert.Equal(2, model.Requests.Count);
        Reconcile(model, merge with { ReviewHash = "sha256:" + new string('b', 64) });
        Assert.Equal(4, model.Requests.Count);
        Reconcile(model, merge, "other-model");
        Assert.Equal(6, model.Requests.Count);
        Reconcile(model, merge);
        Assert.Equal(6, model.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidAndFailedResponsesAreNeverCheckpointed(bool failure)
    {
        var model = new Model { Failure = failure, Invalid = !failure };
        var result = Reconcile(model, Proposal());
        Assert.Equal("partial", result.GuidanceReview!.Status);
        Assert.False(Directory.Exists(CacheDirectory));
        model.Failure = false; model.Invalid = false;
        Assert.Equal("complete", Reconcile(model, Proposal()).GuidanceReview!.Status);
        Assert.Equal(3, model.Requests.Count);
    }

    [Fact]
    public void CorruptCheckpointsAreIgnoredAndRemoteAuthorizationIsStillRequired()
    {
        var model = new Model();
        var merge = Proposal();
        Reconcile(model, merge);
        foreach (var file in Directory.GetFiles(CacheDirectory)) File.WriteAllText(file, "invalid checkpoint");
        Reconcile(model, merge);
        Assert.Equal(4, model.Requests.Count);
        var unauthorized = new RepositoryGuidanceReconciler(model, new("test", "strong", false), session: new(_directory)).Reconcile(merge);
        Assert.Equal(0, unauthorized.GuidanceReview!.CompletedPasses);
        Assert.Equal(4, model.Requests.Count);
    }

    [Fact]
    public void FailedCallAtDeadlineMarksTheRunPausedEvenWhenItWasTheLastFile()
    {
        var elapsed = TimeSpan.Zero;
        var model = new Model { Failure = true, AfterCall = _ => elapsed = TimeSpan.FromMinutes(10) };
        var session = new RepositoryGuidanceReviewSession(elapsed: () => elapsed);
        new RepositoryGuidanceReconciler(model, new("test", "strong", true), session: session).Reconcile(Proposal());
        Assert.True(session.Paused);
    }

    private string CacheDirectory => Path.Combine(_directory, ".cis", "local", "guidance-review");
    private RepositoryFileMerge Proposal()
    {
        Assert.True(RepositoryAgentsMerge.TryCreate(_directory, "# Team\n\nKeep domain contracts.\n", "# CIS\n\nUse CIS routing.\n",
            out var result, relativePath: ".github/instructions/team.instructions.md"));
        return result!;
    }
    private RepositoryFileMerge Reconcile(Model model, RepositoryFileMerge merge, string selected = "strong") =>
        new RepositoryGuidanceReconciler(model, new("test", selected, true), session: new(_directory)).Reconcile(merge);
    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class Model : ICisTextGenerationService
    {
        public List<CisTextGenerationRequest> Requests { get; } = [];
        public Action<CisTextGenerationRequest>? AfterCall { get; set; }
        public bool Local { get; init; }
        public bool Invalid { get; set; }
        public bool Failure { get; set; }
        public CisAiStatus GetStatus() => new([new("test", "available", "test", Local, [new("strong"), new("other-model")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Requests.Add(request);
            AfterCall?.Invoke(request);
            using var blocks = JsonDocument.Parse(request.Prompt.Split("EXISTING BLOCKS IN DOCUMENT ORDER:\n", StringSplitOptions.None)[1]
                .Split("\nREAD-ONLY REFERENCES", StringSplitOptions.None)[0]);
            var ids = blocks.RootElement.EnumerateArray().Select(block => block.GetProperty("id").GetString()).ToArray();
            return new(Failure ? "failed" : "generated", "test", request.Model,
                Invalid ? "{\"keptIds\":[],\"changes\":[]}" : JsonSerializer.Serialize(new { keptIds = ids, changes = Array.Empty<object>() }),
                Failure ? "Timed out." : null, Local);
        }
    }
}
