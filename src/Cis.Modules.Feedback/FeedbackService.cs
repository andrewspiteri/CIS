namespace Cis.Modules.Feedback;

public sealed class FeedbackService
{
    public static readonly TimeSpan DefaultOpportunityLookback = TimeSpan.FromHours(24);
    private const int PerInvocationOutputThreshold = 1_000;
    private const int RecentOutcomeSampleSize = 20;
    private readonly ToolUsageStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public FeedbackService(ToolUsageStore store) : this(store, () => DateTimeOffset.UtcNow) { }

    public FeedbackService(ToolUsageStore store, Func<DateTimeOffset> clock)
    {
        _store = store;
        _clock = clock;
    }

    public FeedbackSummary Summary(string repositoryPath, DateTimeOffset? sinceUtc)
    {
        var usage = _store.ReadAll(repositoryPath, sinceUtc);
        if (usage.ExitCode != 0)
        {
            return new FeedbackSummary(
                Status: usage.Status,
                ExitCode: usage.ExitCode,
                LedgerPath: usage.LedgerPath,
                SinceUtc: sinceUtc,
                InvocationCount: 0,
                SuccessfulCount: 0,
                FailedCount: 0,
                NonSuccessfulCount: 0,
                BlockedCount: 0,
                GovernedFindingCount: 0,
                InvalidRequestCount: 0,
                CancelledCount: 0,
                ElapsedMilliseconds: 0,
                OutputEstimatedTokens: 0,
                BaselineEstimatedTokens: 0,
                ActualEstimatedTokens: 0,
                PossibleTokenSavings: 0,
                PossibleTokenSavingsPercent: 0,
                EstimatedInvocationCount: 0,
                Commands: [],
                Errors: usage.Errors);
        }

        var entries = usage.Entries;
        var baseline = entries.Sum(item => (long)item.BaselineEstimatedTokens);
        var savings = entries.Sum(item => (long)item.PossibleTokenSavings);
        var commands = entries
            .GroupBy(item => item.Command, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FeedbackCommandSummary(
                group.Key,
                group.Count(),
                group.Count(item => Outcome(item) == "failed"),
                group.Count(item => item.ExitCode != 0),
                group.Count(item => Outcome(item) == "blocked"),
                group.Count(item => Outcome(item) == "governed-findings"),
                group.Sum(item => item.ElapsedMilliseconds),
                group.Sum(item => (long)item.OutputEstimatedTokens),
                group.Sum(item => (long)item.PossibleTokenSavings)))
            .ToArray();
        return new FeedbackSummary(
            entries.Count == 0 ? "empty" : "valid",
            0,
            usage.LedgerPath,
            sinceUtc,
            entries.Count,
            entries.Count(item => item.ExitCode == 0),
            entries.Count(item => Outcome(item) == "failed"),
            entries.Count(item => item.ExitCode != 0),
            entries.Count(item => Outcome(item) == "blocked"),
            entries.Count(item => Outcome(item) == "governed-findings"),
            entries.Count(item => Outcome(item) == "invalid-request"),
            entries.Count(item => Outcome(item) == "cancelled"),
            entries.Sum(item => item.ElapsedMilliseconds),
            entries.Sum(item => (long)item.OutputEstimatedTokens),
            baseline,
            entries.Sum(item => (long)item.ActualEstimatedTokens),
            savings,
            baseline == 0 ? 0 : Math.Round(100d * savings / baseline, 2),
            entries.Count(item => !string.Equals(item.SavingsConfidence, "none", StringComparison.Ordinal)),
            commands,
            usage.Errors);
    }

    public FeedbackOpportunityResult Opportunities(string repositoryPath, DateTimeOffset? sinceUtc)
    {
        var effectiveSince = sinceUtc ?? _clock() - DefaultOpportunityLookback;
        var usage = _store.ReadAll(repositoryPath, effectiveSince);
        if (usage.ExitCode != 0)
        {
            return new FeedbackOpportunityResult(usage.Status, usage.ExitCode, usage.LedgerPath,
                effectiveSince, [], usage.Errors);
        }

        var opportunities = new List<FeedbackOpportunity>();
        var analyzable = usage.Entries
            .Where(item => !item.Command.StartsWith("feedback ", StringComparison.Ordinal))
            .ToArray();
        foreach (var group in analyzable.GroupBy(OpportunityCommand, StringComparer.Ordinal))
        {
            var ordered = group.OrderByDescending(item => item.StartedAtUtc).ToArray();
            var recent = ordered.Take(RecentOutcomeSampleSize).ToArray();
            var failures = recent.Count(item => Outcome(item) == "failed");
            var nonSuccessful = recent.Count(item => item.ExitCode != 0);
            var consecutiveNonSuccessful = recent.TakeWhile(item => item.ExitCode != 0).Count();
            if (!group.Key.Equals("repo doctor", StringComparison.Ordinal)
                && recent.Length > 0 && recent[0].ExitCode != 0
                && (failures >= 2 || consecutiveNonSuccessful >= 2
                    || nonSuccessful >= 2 && nonSuccessful / (double)recent.Length >= 0.25))
            {
                opportunities.Add(new FeedbackOpportunity(
                    failures >= 2 ? "CIS-FEEDBACK-REPEATED-FAILURE" : "CIS-FEEDBACK-REPEATED-NON-SUCCESS",
                    failures >= 2 ? "warning" : "information",
                    group.Key,
                    $"The latest bounded sample contains {nonSuccessful} non-success exit(s) in {recent.Length} run(s); {failures} are classified as execution failures.",
                    "Inspect the latest retained evidence. Use Repository Doctor only when repository state or prerequisites may be involved.",
                    [$"failed={failures}", $"nonSuccessful={nonSuccessful}", $"sample={recent.Length}",
                        $"consecutiveNonSuccessful={consecutiveNonSuccessful}"]));
            }

            var output = ordered.Select(item => item.OutputEstimatedTokens).Order().ToArray();
            var average = output.Length == 0 ? 0 : output.Average();
            var percentile95 = Percentile95(output);
            var maximum = output.Length == 0 ? 0 : output[^1];
            var total = output.Sum(item => (long)item);
            if ((average >= PerInvocationOutputThreshold || percentile95 >= PerInvocationOutputThreshold)
                && group.All(item => string.Equals(item.SavingsConfidence, "none", StringComparison.Ordinal)))
            {
                opportunities.Add(new FeedbackOpportunity(
                    "CIS-FEEDBACK-COMPACTION",
                    "information",
                    group.Key,
                    $"The command emits about {Math.Round(average)} tokens per run on average (p95 {percentile95}, max {maximum}) without a registered counterfactual estimate.",
                    "Add or use a compact structured projection. If full evidence is intentional, register a command-owned raw-versus-focused estimate.",
                    [$"averageOutputEstimatedTokens={Math.Round(average)}", $"p95OutputEstimatedTokens={percentile95}",
                        $"maxOutputEstimatedTokens={maximum}", $"totalOutputEstimatedTokens={total}", $"runs={group.Count()}"]));
            }

            var duplicateIntervals = ordered.Zip(ordered.Skip(1), (newer, older) => newer.StartedAtUtc - older.StartedAtUtc)
                .Count(interval => interval >= TimeSpan.Zero && interval <= TimeSpan.FromSeconds(2));
            if (duplicateIntervals >= 3)
            {
                opportunities.Add(new FeedbackOpportunity(
                    "CIS-FEEDBACK-DUPLICATE-QUERY",
                    "information",
                    group.Key,
                    $"The command has {duplicateIntervals} adjacent invocation interval(s) of two seconds or less in the bounded window.",
                    "Share one in-flight structured query within a workspace projection and invalidate it after mutations.",
                    [$"duplicateIntervals={duplicateIntervals}", $"runs={group.Count()}"]));
            }
        }

        if (analyzable.Length > 0
            && analyzable.All(item => string.Equals(item.SavingsConfidence, "none", StringComparison.Ordinal)))
        {
            opportunities.Add(new FeedbackOpportunity(
                "CIS-FEEDBACK-NO-ESTIMATES",
                "information",
                "all",
                "No command in the bounded opportunity window supplied a defensible token-saving counterfactual.",
                "Use context packs and file-card routing, or add a command-specific estimator before claiming savings.",
                [$"runs={analyzable.Length}"]));
        }

        var orderedOpportunities = opportunities
            .OrderBy(item => item.Severity == "warning" ? 0 : 1)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Command, StringComparer.Ordinal)
            .ToArray();
        return new FeedbackOpportunityResult(
            orderedOpportunities.Length == 0 ? "clear" : "opportunities-found",
            0,
            usage.LedgerPath,
            effectiveSince,
            orderedOpportunities,
            usage.Errors);
    }

    public static bool TryParseSince(string? value, out DateTimeOffset? sinceUtc, out string? error)
    {
        sinceUtc = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var unit = char.ToLowerInvariant(value[^1]);
        if (!double.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var amount)
            || !double.IsFinite(amount) || amount <= 0)
        {
            error = "Since must be a positive duration such as 12h, 7d, or 30m.";
            return false;
        }

        TimeSpan duration;
        try
        {
            duration = unit switch
            {
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                _ => throw new FormatException(),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error = "Since must be a positive duration such as 12h, 7d, or 30m.";
            return false;
        }

        sinceUtc = DateTimeOffset.UtcNow - duration;
        return true;
    }

    private static string Outcome(ToolUsageEntry item) => string.IsNullOrWhiteSpace(item.Outcome)
        ? ToolUsageStore.ClassifyOutcome(item.Command, item.ExitCode)
        : item.Outcome;

    private static string OpportunityCommand(ToolUsageEntry item)
    {
        var projection = item.Options.Contains("--summary", StringComparer.Ordinal) ? " --summary" : string.Empty;
        return item.Command + projection;
    }

    private static int Percentile95(IReadOnlyList<int> ordered)
    {
        if (ordered.Count == 0) return 0;
        var index = (int)Math.Ceiling(ordered.Count * 0.95) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }
}
