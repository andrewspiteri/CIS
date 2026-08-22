namespace Cis.Modules.Standards;

public sealed record StandardDefinition(
    string Id,
    string Path,
    string Title,
    string Status,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Stacks,
    string Owner,
    string LastReviewed,
    string ReviewCadence,
    IReadOnlyList<string> RuleIds);

public sealed record StandardInventoryResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    IReadOnlyList<StandardDefinition> Standards,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

public sealed record StandardConformanceEntry(
    string StandardId,
    string RuleId,
    string GovernedSurface,
    string Enforcement,
    string Evidence,
    string Status,
    string Notes);

public sealed record StandardsConformanceResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    IReadOnlyList<StandardConformanceEntry> Entries,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 5;
}

public sealed record StandardsValidationDiagnostic(
    string Code,
    string Severity,
    string Path,
    string Message,
    string Remediation);

public sealed record StandardsValidationResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    int Standards,
    int Rules,
    int ConformanceEntries,
    bool Strict,
    IReadOnlyList<StandardsValidationDiagnostic> Diagnostics)
{
    public int Errors => Diagnostics.Count(item => item.Severity == "error");
    public int Warnings => Diagnostics.Count(item => item.Severity == "warning");
    public int ExitCode => Errors > 0 || (Strict && Warnings > 0) ? 5 : 0;
}
