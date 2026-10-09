using System.Text.RegularExpressions;

namespace Cis.Abstractions;

/// <summary>One grammar for generated task and verification-ledger usage bookkeeping.</summary>
public static class CisToolUsageSnapshot
{
    public const string LedgerPath = ".cis/local/feedback/tool-usage.jsonl";
    private const string Label = "CIS tool-usage snapshot";
    private static readonly Regex Values = new(@"\Ainvocations=[0-9]+; failed=[0-9]+; possibleTokenSavings=[0-9]+; ledgerDigest=sha256:[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string FormatValues(int invocations, int failed, long possibleTokenSavings, string ledgerDigest)
        => FormattableString.Invariant($"invocations={Math.Max(0, invocations)}; failed={Math.Max(0, failed)}; possibleTokenSavings={Math.Max(0, possibleTokenSavings)}; ledgerDigest={ledgerDigest}");

    public static string TaskRow(string values) => $"| {Label} | `{LedgerPath}` | Recorded | {values} |";
    public static string VerificationRow(string taskId, string values) => $"| {taskId} | {Label} | `{LedgerPath}` | Recorded | {values} |";
    public static bool IsTaskRow(string line) => Matches(line, hasTaskId: false);
    public static bool IsVerificationRow(string line) => Matches(line, hasTaskId: true);

    private static bool Matches(string line, bool hasTaskId)
    {
        var cells = line.Split('|', StringSplitOptions.TrimEntries);
        var offset = hasTaskId ? 1 : 0;
        return cells.Length == 6 + offset && cells[0] == "" && cells[^1] == ""
            && (!hasTaskId || cells[1].StartsWith("WORK-", StringComparison.Ordinal))
            && cells[1 + offset] == Label && cells[2 + offset] == $"`{LedgerPath}`"
            && cells[3 + offset] == "Recorded" && Values.IsMatch(cells[4 + offset]);
    }
}
