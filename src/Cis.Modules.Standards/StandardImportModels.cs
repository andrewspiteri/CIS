namespace Cis.Modules.Standards;

public sealed record StandardImportItem(
    string Id,
    string Title,
    string Source,
    string Destination,
    string Hash,
    string Status,
    int RuleCount);

public sealed record StandardImportResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    bool DryRun,
    bool ConfirmationRequired,
    bool Applied,
    IReadOnlyList<StandardImportItem> Standards,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Errors);

public sealed record StandardImportRequest(
    string RepositoryPath,
    IReadOnlyList<string> Sources,
    bool DryRun,
    bool Confirmed,
    bool Fix,
    bool Strict);
