namespace Cis.Abstractions;

public sealed record CisRepositoryDoctorFinding(
    string Code,
    string Severity,
    string Category,
    string Message,
    IReadOnlyList<string> Evidence,
    string SuggestedFix,
    string? FixCommand,
    string Fixability);
