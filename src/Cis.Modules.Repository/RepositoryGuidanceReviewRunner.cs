using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Bounds remote review concurrency while keeping each file's two passes ordered.</summary>
internal sealed class RepositoryGuidanceReviewRunner(ICisTextGenerationService generation,
    RepositoryGuidanceModel? route, Action<string>? reportProgress = null, RepositoryGuidanceReviewSession? session = null)
{
    internal const int RemoteConcurrency = 4;
    internal const int MaximumBatchFiles = 8;
    internal const int MaximumBatchInstructions = 350;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal RepositoryFileMerge[] Reconcile(IReadOnlyList<RepositoryFileMerge> merges)
    {
        session ??= new RepositoryGuidanceReviewSession();
        var providers = route is null ? [] : generation.GetStatus().Providers.Where(provider => provider.Name == route.Provider).ToArray();
        // Local models share limited memory and remain serial. Never enable remote
        // generation, change the route, or increase the number of passes here.
        var parallel = route is { AllowRemote: true } && providers.Length == 1 && providers[0].IsAvailable
            && !providers[0].IsLocal && providers[0].Models.Any(model => model.Name == route.Model);
        var concurrency = Math.Min(parallel ? RemoteConcurrency : 1, Math.Max(merges.Count, 1));
        var batches = BuildBatches(merges, parallel && merges.Count > 8);
        concurrency = Math.Min(concurrency, batches.Count);
        var results = new RepositoryFileMerge[merges.Count];
        var progressLock = new object();
        void Report(string kind, int index, string message, string? reviewStatus = null)
        {
            if (reportProgress is null) return;
            // Concurrent model calls must never interleave a progress line or
            // require the caller's output writer to be thread-safe.
            lock (progressLock)
                reportProgress(JsonSerializer.Serialize(new { kind, index, total = merges.Count, concurrency, elapsedMs = (long)session.Elapsed.TotalMilliseconds,
                    repositoryPath = index >= 0 ? merges[index].RepositoryPath : null,
                    relativePath = index >= 0 ? merges[index].RelativePath : null, message, reviewStatus }, JsonOptions));
        }

        Report("review-start", -1, route is null ? "Preparing editable guidance. Select a model for semantic review." : concurrency > 1
            ? $"Reviewing {merges.Count} files in {batches.Count} bounded batches, with up to {concurrency} model requests concurrently. Every file receives two full passes."
            : "Reviewing one bounded batch at a time. Each file receives two full passes with the selected model.");
        void Review(int[] indices)
        {
            foreach (var index in indices) Report("file-start", index, indices.Length > 1
                ? $"Preparing a bounded batch of {indices.Length} complete files; each retains its own scope and editable proposal."
                : "Preparing guidance and checking the selected model.");
            var batchResults = new RepositoryGuidanceReconciler(generation, route,
                message => { foreach (var index in indices) Report("file-progress", index, message); }, session)
                .ReconcileBatch(indices.Select(index => merges[index]).ToArray());
            for (var offset = 0; offset < indices.Length; offset++)
            {
                var index = indices[offset];
                results[index] = batchResults[offset];
                var review = results[index].GuidanceReview!;
                Report("file-finish", index, $"{review.CompletedPasses} of 2 passes completed; "
                    + $"{review.ReviewedInstructions} of {review.TotalInstructions} instruction blocks reviewed; "
                    + $"{review.Removals.Count} proposed changes.", review.Status);
            }
        }
        if (concurrency == 1)
            foreach (var batch in batches) Review(batch);
        else
            Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency) }, Review);
        return results;
    }

    internal static IReadOnlyList<int[]> BuildBatches(IReadOnlyList<RepositoryFileMerge> merges, bool batchSmallFiles)
    {
        var result = new List<int[]>();
        var pending = new List<int>();
        var count = 0;
        for (var index = 0; index < merges.Count; index++)
        {
            var item = merges[index];
            var instructions = RepositoryGuidanceReconciler.InstructionCount(item);
            var isolated = !batchSmallFiles || RepositoryAgentsMerge.IsEntryPoint(item.RelativePath)
                || RepositoryGuidanceReconciler.Sensitive(item.CurrentContent) || instructions > MaximumBatchInstructions;
            var candidates = pending.Select(i => merges[i]).Append(item).ToArray();
            var size = candidates.Sum(candidate => candidate.CurrentContent.Length)
                + candidates.SelectMany(candidate => candidate.GuidanceSources).DistinctBy(source => (source.Path, source.Content)).Sum(source => source.Content.Length)
                + candidates.SelectMany(candidate => candidate.ContextSources).DistinctBy(source => (source.Path, source.ContentHash)).Sum(source => source.Content.Length);
            if (pending.Count > 0 && (isolated || pending.Count == MaximumBatchFiles || count + instructions > MaximumBatchInstructions || size > 80_000
                || merges[pending[0]].RepositoryPath != item.RepositoryPath)) Flush();
            if (isolated) result.Add([index]);
            else { pending.Add(index); count += instructions; }
        }
        Flush();
        return result;

        void Flush() { if (pending.Count > 0) result.Add(pending.ToArray()); pending.Clear(); count = 0; }
    }
}
