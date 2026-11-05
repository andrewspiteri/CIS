namespace Cis.Abstractions;

/// <summary>Required work is distinct from severity; historical task findings retain their original meaning.</summary>
public static class CisTaskReviewSemantics
{
    public static bool TryDisposition(string severity, string? disposition, bool requireExplicit, out string effective)
    {
        effective = "invalid";
        if (severity is not ("blocking" or "major" or "minor" or "observation")) return false;
        if (disposition is "required" or "optional") { effective = disposition; return true; }
        if (disposition is not null || requireExplicit) return false;
        effective = severity == "observation" ? "optional" : "required";
        return true;
    }

    public static string? VerdictError(string? recommendation, IEnumerable<string> dispositions)
    {
        var values = dispositions.ToArray();
        if (recommendation is not ("ready" or "revise" or "blocked") || values.Any(value => value is not ("required" or "optional")))
            return "Task review has an invalid verdict or finding disposition.";
        var required = values.Contains("required", StringComparer.Ordinal);
        if (recommendation == "ready" && required) return "A ready task review cannot retain unresolved required findings.";
        if (recommendation is "revise" or "blocked" && !required)
            return "A corrective or blocked task review needs a required finding; optional advice cannot manufacture work or a hold.";
        return null;
    }

    public static bool ValidBasisReferences(IReadOnlyList<string>? references)
        => references is { Count: > 0 and <= 32 } && references.All(value => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 2000 && !value.Contains('\0'));

    public static bool ValidNecessityAssessment(CisReviewNecessityAssessment? assessment)
        => assessment is null || new[] { assessment.Invariant, assessment.NativeAlternative, assessment.CostEvidence,
            assessment.MaintenanceAndPassSemantics, assessment.DuplicationAvoided, assessment.Decision }
            .All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000 && !value.Contains('\0'));
}

/// <summary>Optional judgment recorded by the existing reviewer when verification machinery materially expands.</summary>
public sealed record CisReviewNecessityAssessment(string Invariant, string NativeAlternative, string CostEvidence,
    string MaintenanceAndPassSemantics, string DuplicationAvoided, string Decision);
