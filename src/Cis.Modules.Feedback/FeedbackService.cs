namespace Cis.Modules.Feedback;

public sealed class FeedbackService(ToolUsageStore store)
{
    public FeedbackSummary Summary(string repositoryPath, DateTimeOffset? sinceUtc)
    {
        var usage = store.ReadAll(repositoryPath, sinceUtc);
        if (usage.ExitCode != 0)
        {
            return new FeedbackSummary(usage.Status, usage.ExitCode, usage.LedgerPath, sinceUtc,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], usage.Errors);
        }

        var entries = usage.Entries;
        var baseline = entries.Sum(item => item.BaselineEstimatedTokens);
        var savings = entries.Sum(item => item.PossibleTokenSavings);
        var commands = entries
            .GroupBy(item => item.Command, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FeedbackCommandSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.ExitCode != 0),
                group.Sum(item => item.ElapsedMilliseconds),
                group.Sum(item => item.OutputEstimatedTokens),
                group.Sum(item => item.PossibleTokenSavings)))
            .ToArray();
        return new FeedbackSummary(
            entries.Count == 0 ? "empty" : "valid",
            0,
            usage.LedgerPath,
            sinceUtc,
            entries.Count,
            entries.Count(item => item.ExitCode == 0),
            entries.Count(item => item.ExitCode != 0),
            entries.Sum(item => item.ElapsedMilliseconds),
            entries.Sum(item => item.OutputEstimatedTokens),
            baseline,
            entries.Sum(item => item.ActualEstimatedTokens),
            savings,
            baseline == 0 ? 0 : Math.Round(100d * savings / baseline, 2),
            entries.Count(item => !string.Equals(item.SavingsConfidence, "none", StringComparison.Ordinal)),
            commands,
            usage.Errors);
    }

    public FeedbackOpportunityResult Opportunities(string repositoryPath, DateTimeOffset? sinceUtc)
    {
        var usage = store.ReadAll(repositoryPath, sinceUtc);
        if (usage.ExitCode != 0)
        {
            return new FeedbackOpportunityResult(usage.Status, usage.ExitCode, usage.LedgerPath, [], usage.Errors);
        }

        var opportunities = new List<FeedbackOpportunity>();
        foreach (var group in usage.Entries.GroupBy(item => item.Command, StringComparer.Ordinal))
        {
            var failures = group.Count(item => item.ExitCode != 0);
            if (failures >= 2)
            {
                opportunities.Add(new FeedbackOpportunity(
                    "CIS-FEEDBACK-REPEATED-FAILURE",
                    "warning",
                    group.Key,
                    $"{failures} of {group.Count()} recorded invocations failed.",
                    "Inspect the latest failure and run `cis repo doctor` before repeating the same command.",
                    [$"failed={failures}", $"runs={group.Count()}"]));
            }

            var outputTokens = group.Sum(item => item.OutputEstimatedTokens);
            if (outputTokens >= 1_000 && group.All(item => string.Equals(item.SavingsConfidence, "none", StringComparison.Ordinal)))
            {
                opportunities.Add(new FeedbackOpportunity(
                    "CIS-FEEDBACK-COMPACTION",
                    "information",
                    group.Key,
                    $"The command emitted about {outputTokens} tokens without a registered counterfactual estimate.",
                    "Add a compact agent output or a command-owned raw-versus-focused token estimate.",
                    [$"outputEstimatedTokens={outputTokens}", $"runs={group.Count()}"]));
            }
        }

        if (usage.Entries.Count > 0 && usage.Entries.All(item => string.Equals(item.SavingsConfidence, "none", StringComparison.Ordinal)))
        {
            opportunities.Add(new FeedbackOpportunity(
                "CIS-FEEDBACK-NO-ESTIMATES",
                "information",
                "all",
                "No recorded command has supplied a defensible token-saving counterfactual.",
                "Use context packs and file-card routing, or add a command-specific estimator before claiming savings.",
                [$"runs={usage.Entries.Count}"]));
        }

        return new FeedbackOpportunityResult(
            opportunities.Count == 0 ? "clear" : "opportunities-found",
            0,
            usage.LedgerPath,
            opportunities,
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
                System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount < 0)
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
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            error = "Since must be a positive duration such as 12h, 7d, or 30m.";
            return false;
        }

        sinceUtc = DateTimeOffset.UtcNow - duration;
        return true;
    }
}
