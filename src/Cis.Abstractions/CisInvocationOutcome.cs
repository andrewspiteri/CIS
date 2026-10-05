namespace Cis.Abstractions;

/// <summary>Bounded semantic outcome supplied by the executing command, never command output.</summary>
public static class CisInvocationOutcome
{
    private static readonly AsyncLocal<string?> Value = new();
    public static string? Current => Value.Value;
    public static void Reset() => Value.Value = null;
    public static void Report(string outcome)
    {
        if (outcome is "succeeded" or "invalid-request" or "blocked" or "governed-findings" or "cancelled" or "failed")
            Value.Value = outcome;
    }
}
