namespace Cis.Abstractions;

/// <summary>Recognizes CIS's generated outcome seed, without grading authored requirements.</summary>
public static class CisOutcomeTaskReadiness
{
    public const string SeedInstancePrefix = "outcome:seed:";
    public const string DraftSeedInstance = "outcome:initial";
    public const string SeedAcceptance = "Deliver the shared outcome and its applicable checks.";
    public const string SeedValidation = "Build the affected component and run its connected checks.";

    public static string? UnresolvedSeed(string? instanceKey, string? status, string? acceptance, string? validation,
        bool retainedContainer = false)
    {
        var knownPlaceholder = acceptance?.Trim() == SeedAcceptance || validation?.Trim() == SeedValidation;
        var marked = instanceKey?.StartsWith(SeedInstancePrefix, StringComparison.Ordinal) == true
            || instanceKey == DraftSeedInstance && string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase)
            || instanceKey?.StartsWith("outcome:", StringComparison.Ordinal) == true
                && status is "Draft" or "Blocked" or "Deferred" && knownPlaceholder;
        // A proved split parent retains its accounting identity and children's
        // acceptance; it is not an unresolved executable leaf.
        if (!marked || retainedContainer && status is "Decomposed" or "Complete") return null;
        if (!string.IsNullOrWhiteSpace(acceptance) && acceptance.Trim() != SeedAcceptance
            && !string.IsNullOrWhiteSpace(validation) && validation.Trim() != SeedValidation) return null;
        return "The generated outcome seed still has planning placeholders. Use cis plan task update to supply concrete observable acceptance criteria and targeted validation from the accepted request, or split it into concrete tasks under the existing outcome authority.";
    }
}
