namespace Cis.Modules.Skills;

public sealed record SkillInventoryItem(
    string Name,
    string Path,
    string Description,
    int LineCount);

public sealed record SkillDiagnostic(
    string Code,
    string Severity,
    string Path,
    string Message,
    IReadOnlyList<string> Evidence);

public sealed record SkillValidationResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    string SkillsRoot,
    int SkillCount,
    int ErrorCount,
    int WarningCount,
    bool Strict,
    bool FixRequested,
    bool Applied,
    IReadOnlyList<string> FixedPaths,
    IReadOnlyList<SkillInventoryItem> Skills,
    IReadOnlyList<SkillDiagnostic> Diagnostics);
