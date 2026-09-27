using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Bounds one review run and checkpoints only validated model passes.</summary>
internal sealed class RepositoryGuidanceReviewSession(string? repositoryPath = null,
    TimeSpan? budget = null, Func<TimeSpan>? elapsed = null)
{
    internal const int BudgetSeconds = 600;
    private const int MaximumCacheBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private int _paused;
    internal bool Paused => Volatile.Read(ref _paused) != 0;
    internal TimeSpan Elapsed => elapsed?.Invoke() ?? _timer.Elapsed;

    internal CisTextGenerationResult Generate(ICisTextGenerationService generation, CisTextGenerationRequest request,
        string scope, out bool cached)
    {
        cached = false;
        var path = CachePath(request, scope);
        try
        {
            if (path is not null && File.Exists(path) && new FileInfo(path).Length <= MaximumCacheBytes)
            {
                var saved = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path), JsonOptions);
                if (saved?.Text is not null && saved.Key == Key(request, scope)
                    && saved.ContentHash == RepositoryAgentsMerge.Hash(saved.Text))
                {
                    cached = true;
                    return new("generated", request.Provider, request.Model, saved.Text, null, saved.IsLocal);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        var remaining = (int)Math.Floor(((budget ?? TimeSpan.FromSeconds(BudgetSeconds)) - Elapsed).TotalSeconds);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _paused, 1);
            return new("review-paused", request.Provider, request.Model, null,
                "The review reached its time limit. Continue with the same model to reuse validated passes; unfinished files still need review.", true);
        }
        // Providers must honor the remaining run allowance, including a pass that
        // starts near the deadline. Keep individual slow calls below five minutes.
        var result = generation.Generate(request with { TimeoutSeconds = Math.Min(request.TimeoutSeconds, Math.Min(300, remaining)) });
        if (!result.IsSuccess && Elapsed >= (budget ?? TimeSpan.FromSeconds(BudgetSeconds))) Interlocked.Exchange(ref _paused, 1);
        return result;
    }

    internal void Store(CisTextGenerationRequest request, string scope, CisTextGenerationResult result, Action<string>? report)
    {
        if (!result.IsSuccess || result.Text is null || RepositoryGuidanceReconciler.Sensitive(result.Text)) return;
        var path = CachePath(request, scope);
        if (path is null)
        {
            if (repositoryPath is not null) report?.Invoke("Warning: The local checkpoint path is unsafe; this pass cannot be reused.");
            return;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var content = JsonSerializer.Serialize(new Checkpoint(Key(request, scope), result.Text,
                RepositoryAgentsMerge.Hash(result.Text), result.IsLocal), JsonOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(content) > MaximumCacheBytes)
            { report?.Invoke("Warning: This validated pass exceeds the local checkpoint size limit and was not saved."); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (CachePath(request, scope) != path) throw new IOException("Checkpoint path changed.");
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { report?.Invoke("Warning: Could not save this validated pass locally; a later review may repeat it."); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private string? CachePath(CisTextGenerationRequest request, string scope)
    {
        if (repositoryPath is null) return null;
        var relative = ".cis/local/guidance-review/" + Key(request, scope)[7..] + ".json";
        return CisPathSafety.TryResolveUnderRoot(repositoryPath, relative, out var path)
            && !CisPathSafety.ContainsReparsePoint(repositoryPath, path) ? path : null;
    }

    // Include input review hashes (full reference/automation revisions), exact
    // prompt/schema, route and policy. Runtime timeout does not invalidate work.
    private static string Key(CisTextGenerationRequest request, string scope) => RepositoryAgentsMerge.Hash(JsonSerializer.Serialize(new
    {
        policy = "guidance-checkpoint-v1", scope, request.Provider, request.Model, request.AllowRemote,
        request.Prompt, request.JsonSchema, request.MaxOutputTokens, request.ContextWindowTokens,
    }, JsonOptions));

    private sealed record Checkpoint(string Key, string Text, string ContentHash, bool IsLocal);
}
