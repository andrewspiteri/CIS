using System.Globalization;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Feedback;

public sealed class ToolUsageStore : ICisToolUsageRecorder
{
    public const string RelativeLedgerPath = CisToolUsageSnapshot.LedgerPath;
    private const int CharactersPerEstimatedToken = 4;
    private const int DefaultMaximumRetainedEntries = 25_000;
    private const long DefaultCompactionThresholdBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions LineOptions = new(JsonSerializerDefaults.Web);
    private readonly int _maximumRetainedEntries;
    private readonly long _compactionThresholdBytes;
    private readonly TimeSpan _retention;
    private readonly Func<DateTimeOffset> _clock;

    public ToolUsageStore()
        : this(DefaultMaximumRetainedEntries, DefaultCompactionThresholdBytes, DefaultRetention, () => DateTimeOffset.UtcNow)
    {
    }

    public ToolUsageStore(int maximumRetainedEntries, long compactionThresholdBytes, TimeSpan retention,
        Func<DateTimeOffset>? clock = null)
    {
        if (maximumRetainedEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumRetainedEntries));
        if (compactionThresholdBytes < 1) throw new ArgumentOutOfRangeException(nameof(compactionThresholdBytes));
        if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        _maximumRetainedEntries = maximumRetainedEntries;
        _compactionThresholdBytes = compactionThresholdBytes;
        _retention = retention;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

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
        var command = CanonicalCommand(capture.CanonicalCommand);
        var options = (capture.RecognizedOptions ?? []).Take(256).Where(RecognizedOption)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(32).ToArray();
        var candidate = capture.SavingsCandidates
            .Where(item => string.Equals(item.Command, command, StringComparison.Ordinal))
            .OrderByDescending(item => Math.Max(0, item.BaselineEstimatedTokens - (item.ActualEstimatedTokens ?? outputTokens)))
            .FirstOrDefault();
        var baseline = candidate?.BaselineEstimatedTokens ?? outputTokens;
        var actual = candidate?.ActualEstimatedTokens ?? outputTokens;
        var savings = Math.Max(0, baseline - actual);
        var savingsPercent = baseline == 0 ? 0 : Math.Round(100d * savings / baseline, 2);
        var observations = NormalizeObservations(capture.Observations);
        if (observations is not null && capture.RecognizedOptions is { } providedOptions)
            observations = observations with
            {
                DroppedObservations = (int)Math.Min(int.MaxValue,
                (long)observations.DroppedObservations + Math.Max(0, providedOptions.Count - options.Length))
            };
        var entry = new ToolUsageEntry(
            3,
            observations?.InvocationId ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            capture.StartedAtUtc,
            capture.EndedAtUtc,
            capture.ElapsedMilliseconds,
            command,
            options,
            capture.ExitCode,
            capture.StandardOutputCharacters,
            capture.StandardErrorCharacters,
            outputTokens,
            baseline,
            actual,
            savings,
            savingsPercent,
            candidate?.Basis ?? "command-output-only; no defensible counterfactual registered",
            candidate?.Confidence ?? "none",
            capture.Outcome is "succeeded" or "invalid-request" or "blocked" or "governed-findings" or "cancelled" or "failed"
                ? capture.Outcome : ClassifyOutcome(command, capture.ExitCode), capture.BuildIdentity)
        {
            Observations = observations,
            StandardOutputUtf8Bytes = capture.StandardOutputUtf8Bytes is >= 0 ? capture.StandardOutputUtf8Bytes : null,
            StandardErrorUtf8Bytes = capture.StandardErrorUtf8Bytes is >= 0 ? capture.StandardErrorUtf8Bytes : null,
        };
        AppendBounded(ledgerPath, entry);
    }

    private static CisInvocationObservations? NormalizeObservations(CisInvocationObservations? value)
    {
        if (value is null || !Guid.TryParseExact(value.InvocationId, "N", out _)) return null;
        var dropped = (long)Math.Max(0, value.DroppedObservations);
        string? Field(string? field, Func<string, bool> valid)
        {
            if (field is null || valid(field)) return field;
            dropped++;
            return null;
        }
        T[] Bounded<T>(IReadOnlyList<T>? items, Func<T, bool> valid)
        {
            var retained = (items ?? []).Take(CisInvocationTelemetryScope.MaximumObservationsPerKind).Where(valid).Distinct().ToArray();
            dropped += (items?.Count ?? 0) - retained.Length;
            return retained;
        }
        var operation = Field(value.Operation, text => text.Length <= 256 && text.Split(' ') is { Length: <= 8 } parts && parts.All(part => Identifier(part, 64)));
        var parent = Field(value.ParentInvocationId, text => Guid.TryParseExact(text, "N", out _));
        var change = Field(value.ChangeId, text => CanonicalId(text, "CIS-", 4));
        var task = Field(value.TaskId, text => change is not null && CanonicalId(text, "WORK-", 3));
        var phases = Bounded(value.Phases, item => item is not null && Identifier(item.Phase) && double.IsFinite(item.ElapsedMilliseconds)
            && item.ElapsedMilliseconds >= 0 && item.Count > 0);
        var graph = Bounded(value.Graph, item => item is not null && Identifier(item.Consumer)
            && (item.SnapshotId is null || Identifier(item.SnapshotId)) && Identifier(item.Outcome, 32) && Identifier(item.Reason));
        var verification = Bounded(value.Verification, item => item is not null && Identifier(item.Scope)
            && item.Disposition is "selected" or "executed" or "reused" or "invalidated" && item.Batches >= 0 && Identifier(item.Reason));
        var blockers = Bounded(value.BlockerCodes, item => Identifier(item));
        var delivered = value.DeliveredContextUtf8Bytes;
        if (delivered < 0) { delivered = null; dropped++; }
        return new(value.InvocationId, parent, operation, change, task, phases, graph, verification, blockers, delivered,
            (int)Math.Min(int.MaxValue, dropped));
    }

    private static bool Identifier(string? value, int maximum = 128)
        => value is { Length: > 0 } && value.Length <= maximum
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':');

    private static bool CanonicalId(string value, string prefix, int digits)
        => value.Length == prefix.Length + digits && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

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

    private static string CanonicalCommand(string? value)
        => value is { Length: > 0 and <= 129 } && value.Split(' ') is { Length: <= 2 } parts
            && parts.All(part => Identifier(part, 64)) ? value : "unknown";

    private static bool RecognizedOption(string value)
        => value is { Length: > 1 and <= 64 } && value.StartsWith('-')
            && value.TrimStart('-') is { Length: > 0 } name
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static int EstimateTokens(int characters) =>
        characters == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(characters / (double)CharactersPerEstimatedToken));

    internal static string ClassifyOutcome(string command, int exitCode)
    {
        if (exitCode == 0) return "succeeded";
        if (exitCode == 130) return "cancelled";
        if (command.Equals("repo doctor", StringComparison.Ordinal) && exitCode == 5) return "governed-findings";
        return exitCode switch
        {
            2 => "invalid-request",
            4 or 5 => "blocked",
            _ => "failed",
        };
    }

    private void AppendBounded(string ledgerPath, ToolUsageEntry entry)
    {
        var lockPath = ledgerPath + ".lock";
        FileStream? lockStream = null;
        for (var attempt = 0; attempt < 10 && lockStream is null; attempt++)
        {
            try
            {
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(20);
            }
        }

        if (lockStream is null) return;
        using (lockStream)
        {
            File.AppendAllText(ledgerPath, JsonSerializer.Serialize(entry, LineOptions) + Environment.NewLine);
            if (new FileInfo(ledgerPath).Length >= _compactionThresholdBytes)
            {
                Compact(ledgerPath);
            }
        }
    }

    private void Compact(string ledgerPath)
    {
        var cutoff = _clock() - _retention;
        var retained = new Queue<ToolUsageEntry>();
        foreach (var line in File.ReadLines(ledgerPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<ToolUsageEntry>(line, LineOptions);
                if (item is null || item.StartedAtUtc < cutoff) continue;
                retained.Enqueue(item);
                while (retained.Count > _maximumRetainedEntries) retained.Dequeue();
            }
            catch (JsonException)
            {
                // Derived malformed records are omitted during bounded compaction.
            }
        }

        var temporary = ledgerPath + ".compact-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using (var writer = new StreamWriter(temporary, append: false))
        {
            foreach (var item in retained)
                writer.WriteLine(JsonSerializer.Serialize(item, LineOptions));
        }
        File.Move(temporary, ledgerPath, overwrite: true);
    }
}
