using System.Collections.Concurrent;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class RepositoryGuidanceReviewRunnerTests
{
    [Fact]
    public void AuthorizedRemoteFilesOverlapWithABoundOfFourAndRetainOrderAndBothPasses()
    {
        using var firstWave = new Barrier(4);
        var model = new ConcurrentModel(false, firstWave);
        var proposals = Proposals(7);
        var progress = new List<string>();
        var results = new RepositoryGuidanceReviewRunner(model, new("test", "strong", true), progress.Add).Reconcile(proposals);

        Assert.Equal(4, model.Peak);
        Assert.Equal(proposals.Select(item => item.RelativePath), results.Select(item => item.RelativePath));
        Assert.Equal(14, model.Requests.Count);
        Assert.All(model.Calls.Values, count => Assert.Equal(2, count));
        Assert.All(results, result => { Assert.Equal("complete", result.GuidanceReview!.Status); Assert.Equal(2, result.GuidanceReview.CompletedPasses); });
        Assert.All(model.Requests, request => { Assert.Equal("strong", request.Model); Assert.True(request.AllowRemote); });
        Assert.All(progress, line => { using var document = JsonDocument.Parse(line); Assert.Equal(7, document.RootElement.GetProperty("total").GetInt32()); });
        var finished = progress.Select(line => JsonSerializer.Deserialize<ProgressEvent>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .Where(item => item.Kind == "file-finish").ToArray();
        Assert.Equal(Enumerable.Range(0, 7), finished.Select(item => item.Index).Order());
        Assert.All(finished, item => Assert.Equal("complete", item.ReviewStatus));
    }

    [Fact]
    public void LocalFilesStaySerialEvenWhenRemotePermissionIsPresent()
    {
        var model = new ConcurrentModel(true);
        var results = new RepositoryGuidanceReviewRunner(model, new("test", "strong", true)).Reconcile(Proposals(4));
        Assert.Equal(1, model.Peak);
        Assert.Equal(8, model.Requests.Count);
        Assert.All(results, result => Assert.Equal("complete", result.GuidanceReview!.Status));
    }

    [Fact]
    public void SmallRemoteFilesShareBoundedCallsWithoutLosingCoverageScopeOrProposalIdentity()
    {
        var model = new BatchModel();
        var proposals = Enumerable.Range(0, 12).Select(index => {
            RepositoryAgentsMerge.TryCreate("/repo", $"---\napplyTo: 'area{index}/**'\n---\n\n# Guide\n\nUse legacy routing.\n\nKeep domain rule {index}.\n",
                "# CIS\n\nUse CIS routing.\n", out var merge, relativePath: $".github/instructions/file-{index}.instructions.md");
            return merge!;
        }).ToArray();
        var results = new RepositoryGuidanceReviewRunner(model, new("test", "strong", true)).Reconcile(proposals);
        Assert.Equal(4, model.Requests.Count);
        Assert.Equal(proposals.Select(item => item.RelativePath), results.Select(item => item.RelativePath));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.Contains($"applyTo: 'area{index}/**'", results[index].ProposedContent, StringComparison.Ordinal);
            Assert.Contains($"Keep domain rule {index}.", results[index].ProposedContent, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy routing", results[index].ProposedContent, StringComparison.Ordinal);
            Assert.Equal(3, results[index].GuidanceReview!.ReviewedInstructions);
            Assert.Equal(2, results[index].GuidanceReview!.CompletedPasses);
            Assert.Single(results[index].GuidanceReview!.Removals);
        }
        Assert.Contains(model.Requests, request => request.Prompt.Contains("area0/**", StringComparison.Ordinal));
    }

    [Fact]
    public void BatchesNeverMixRepositoriesOrSplitAWholeDocument()
    {
        var proposals = Proposals(10);
        proposals[4] = proposals[4] with { RepositoryPath = "/other" };
        proposals[7] = proposals[7] with { CurrentContent = string.Join("\n\n", Enumerable.Range(0, 360).Select(i => "Rule " + i)) };
        var batches = RepositoryGuidanceReviewRunner.BuildBatches(proposals, true);
        Assert.Equal(Enumerable.Range(0, 10), batches.SelectMany(batch => batch));
        Assert.All(batches, batch => Assert.Single(batch.Select(i => proposals[i].RepositoryPath).Distinct()));
        Assert.Single(batches.Single(batch => batch.Contains(7)));
        Assert.All(batches, batch => Assert.InRange(batch.Length, 1, 8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSelectionOrRemotePermissionNeverStartsGeneration(bool selectModel)
    {
        var model = new ConcurrentModel(false);
        new RepositoryGuidanceReviewRunner(model, selectModel ? new("test", "strong") : null).Reconcile(Proposals(4));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void OneFailedAuditRetainsItsFirstPassAndOtherFilesStillComplete()
    {
        using var firstWave = new Barrier(3);
        var model = new ConcurrentModel(false, firstWave) { FailAudit = true };
        var results = new RepositoryGuidanceReviewRunner(model, new("test", "strong", true)).Reconcile(Proposals(4));
        Assert.Equal("partial", results[0].GuidanceReview!.Status);
        Assert.Equal(1, results[0].GuidanceReview!.CompletedPasses);
        Assert.All(results.Skip(1), result => Assert.Equal("complete", result.GuidanceReview!.Status));
        Assert.Equal(8, model.Requests.Count);
    }

    private static RepositoryFileMerge[] Proposals(int count) => Enumerable.Range(0, count).Select(index =>
    {
        Assert.True(RepositoryAgentsMerge.TryCreate("/repo", "# Team\n\nKeep domain contracts.\n", "# CIS\n\nUse CIS routing.\n",
            out var result, relativePath: $".github/instructions/file-{index}.instructions.md"));
        return result!;
    }).ToArray();

    private sealed class ConcurrentModel(bool local, Barrier? firstWave = null) : ICisTextGenerationService
    {
        private int _active;
        private int _peak;
        private int _arrivals;
        public bool FailAudit { get; init; }
        public int Peak => _peak;
        public ConcurrentBag<CisTextGenerationRequest> Requests { get; } = [];
        public ConcurrentDictionary<string, int> Calls { get; } = new();
        public CisAiStatus GetStatus() => new([new("test", "available", "test", local, [new("strong")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            var active = Interlocked.Increment(ref _active);
            int peak;
            do { peak = _peak; } while (active > peak && Interlocked.CompareExchange(ref _peak, active, peak) != peak);
            try
            {
                Requests.Add(request);
                using var blocks = JsonDocument.Parse(request.Prompt.Split("EXISTING BLOCKS IN DOCUMENT ORDER:\n", StringSplitOptions.None)[1]
                    .Split("\nREAD-ONLY REFERENCES", StringSplitOptions.None)[0]);
                var path = blocks.RootElement[0].GetProperty("path").GetString()!;
                var call = Calls.AddOrUpdate(path, 1, (_, previous) => previous + 1);
                Assert.Equal(call == 2, request.Prompt.Contains("SECOND PASS", StringComparison.Ordinal));
                // The first wave cannot finish until all its requests are active;
                // this proves overlap without relying on timing or a live account.
                if (firstWave is not null && Interlocked.Increment(ref _arrivals) <= firstWave.ParticipantCount)
                    Assert.True(firstWave.SignalAndWait(TimeSpan.FromSeconds(15)), "Remote reviews did not overlap.");
                return FailAudit && path.EndsWith("file-0.instructions.md", StringComparison.Ordinal) && call == 2
                    ? new("failed", "test", "strong", null, "Audit timed out.", local)
                    : new("generated", "test", "strong", "{\"keptIds\":[\"old0\",\"old2\"],\"changes\":[]}", null, local);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed record ProgressEvent(string Kind, int Index, string? ReviewStatus);

    private sealed class BatchModel : ICisTextGenerationService
    {
        public ConcurrentBag<CisTextGenerationRequest> Requests { get; } = [];
        public CisAiStatus GetStatus() => new([new("test", "available", "test", false, [new("strong")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Requests.Add(request);
            using var blocks = JsonDocument.Parse(request.Prompt.Split("EXISTING BLOCKS IN DOCUMENT ORDER:\n", StringSplitOptions.None)[1]
                .Split("\nREAD-ONLY REFERENCES", StringSplitOptions.None)[0]);
            var entries = blocks.RootElement.EnumerateArray().ToArray();
            var changed = entries.Where(item => item.GetProperty("text").GetString()!.Contains("legacy routing", StringComparison.Ordinal)).ToArray();
            var kept = entries.Except(changed).Select(item => item.GetProperty("id").GetString()).ToArray();
            return new("generated", "test", "strong", JsonSerializer.Serialize(new { keptIds = kept,
                changes = changed.Select(item => new { oldId = item.GetProperty("id").GetString(), newId = "new0-2",
                    kind = "conflict", reason = "CIS routing replaces this route", text = (string?)null }), findings = Array.Empty<object>() }), null, false);
        }
    }
}
