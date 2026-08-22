namespace Cis.Modules.Skills;

public sealed record SkillImportItem(
    string Name,
    string Source,
    string Destination,
    string Hash,
    string Status,
    int FileCount);

public sealed record SkillImportResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    bool DryRun,
    bool ConfirmationRequired,
    bool Applied,
    IReadOnlyList<SkillImportItem> Skills,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Errors);
