using System.Globalization;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Feedback;

public sealed class ToolUsageStore : ICisToolUsageRecorder
{
    public const string RelativeLedgerPath = ".cis/local/feedback/tool-usage.jsonl";
    private const int CharactersPerEstimatedToken = 4;
    private static readonly JsonSerializerOptions LineOptions = new(JsonSerializerDefaults.Web);

    public void Record(CisToolUsageCapture capture)
    {
        var repository = ResolveRepository(capture.Arguments, capture.WorkingDirectory);
        if (repository is null)
        {
            return;
        }

        var ledgerPath = LedgerPath(repository);
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        var outputTokens = EstimateTokens(capture.StandardOutputCharacters + capture.StandardErrorCharacters);
        var candidate = capture.SavingsCandidates
            .OrderByDescending(item => Math.Max(0, item.BaselineEstimatedTokens - (item.ActualEstimatedTokens ?? outputTokens)))
            .FirstOrDefault();
        var baseline = candidate?.BaselineEstimatedTokens ?? outputTokens;
        var actual = candidate?.ActualEstimatedTokens ?? outputTokens;
        var savings = Math.Max(0, baseline - actual);
        var savingsPercent = baseline == 0 ? 0 : Math.Round(100d * savings / baseline, 2);
        var entry = new ToolUsageEntry(
            1,
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            capture.StartedAtUtc,
            capture.EndedAtUtc,
            capture.ElapsedMilliseconds,
            CommandPath(capture.Arguments),
            OptionNames(capture.Arguments),
            capture.ExitCode,
            capture.StandardOutputCharacters,
            capture.StandardErrorCharacters,
            outputTokens,
            baseline,
            actual,
            savings,
            savingsPercent,
            candidate?.Basis ?? "command-output-only; no defensible counterfactual registered",
            candidate?.Confidence ?? "none");
        File.AppendAllText(ledgerPath, JsonSerializer.Serialize(entry, LineOptions) + Environment.NewLine);
    }

    public FeedbackUsageResult Read(string repositoryPath, DateTimeOffset? sinceUtc = null, int limit = 100)
    {
        if (limit is < 1 or > 10_000)
        {
            return new FeedbackUsageResult("invalid-request", 2, null, [],
                ["Limit must be from 1 to 10000."]);
        }

        return ReadCore(repositoryPath, sinceUtc, limit);
    }

    internal FeedbackUsageResult ReadAll(string repositoryPath, DateTimeOffset? sinceUtc = null) =>
        ReadCore(repositoryPath, sinceUtc, limit: null);

    private FeedbackUsageResult ReadCore(string repositoryPath, DateTimeOffset? sinceUtc, int? limit)
    {
        var repository = FindInitializedRoot(repositoryPath);
        if (repository is null)
        {
            return new FeedbackUsageResult("invalid-repository", 2, null, [],
                ["An initialized CIS repository or workspace could not be found."]);
        }

        var ledgerPath = LedgerPath(repository);
        if (!File.Exists(ledgerPath))
        {
            return new FeedbackUsageResult("empty", 0, ledgerPath, [], []);
        }

        var entries = new List<ToolUsageEntry>();
        var errors = new List<string>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(ledgerPath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<ToolUsageEntry>(line, LineOptions);
                if (entry is not null && (sinceUtc is null || entry.StartedAtUtc >= sinceUtc))
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException exception)
            {
                errors.Add($"Ignored malformed ledger line {lineNumber}: {exception.Message}");
            }
        }

        var ordered = entries.OrderByDescending(item => item.StartedAtUtc);
        var selected = limit.HasValue ? ordered.Take(limit.Value).ToArray() : ordered.ToArray();
        return new FeedbackUsageResult(errors.Count == 0 ? "valid" : "partial", errors.Count == 0 ? 0 : 5,
            ledgerPath, selected, errors);
    }

    public static string LedgerPath(string repositoryPath) =>
        Path.Combine(repositoryPath, RelativeLedgerPath.Replace('/', Path.DirectorySeparatorChar));

    public static string? FindInitializedRoot(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var current = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".cis", "repository.yml")) ||
                    File.Exists(Path.Combine(current, ".cis", "workspace.yml")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return null;
    }

    private static string? ResolveRepository(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var workspace = OptionValue(arguments, "--workspace");
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            var resolved = FindInitializedRoot(ResolvePath(workspace, workingDirectory));
            if (resolved is not null)
            {
                return resolved;
            }
        }

        var repo = OptionValue(arguments, "--repo");
        if (!string.IsNullOrWhiteSpace(repo))
        {
            var resolved = FindInitializedRoot(ResolvePath(repo, workingDirectory));
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return FindInitializedRoot(workingDirectory);
    }

    private static string ResolvePath(string path, string workingDirectory) =>
        Path.IsPathRooted(path) ? path : Path.Combine(workingDirectory, path);

    private static string? OptionValue(IReadOnlyList<string> arguments, string option)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
            {
                return arguments[index + 1];
            }

            if (arguments[index].StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index][(option.Length + 1)..];
            }
        }

        return null;
    }

    private static string CommandPath(IReadOnlyList<string> arguments)
    {
        var parts = arguments.TakeWhile(argument => !argument.StartsWith('-')).Take(2).ToArray();
        return parts.Length == 0 ? "help" : string.Join(' ', parts);
    }

    private static IReadOnlyList<string> OptionNames(IReadOnlyList<string> arguments) => arguments
        .Where(argument => argument.StartsWith('-'))
        .Select(argument => argument.Split('=', 2)[0])
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static int EstimateTokens(int characters) =>
        characters == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(characters / (double)CharactersPerEstimatedToken));
}
