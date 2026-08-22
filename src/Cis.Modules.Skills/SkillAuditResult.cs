namespace Cis.Modules.Skills;

public sealed record SkillAuditFinding(
    string Id,
    string Kind,
    string Severity,
    IReadOnlyList<string> Skills,
    string Summary,
    IReadOnlyList<string> Evidence,
    string Method,
    double Confidence);

public sealed record SkillAuditResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    int SkillCount,
    int CandidatePairCount,
    int DuplicateCount,
    int OverlapCount,
    int ConflictCount,
    string LlmStatus,
    string? Provider,
    string? Model,
    bool FixRequested,
    bool Applied,
    string? JsonPath,
    string? MarkdownPath,
    string? QuarantinePath,
    IReadOnlyList<string> QuarantinedSkills,
    IReadOnlyList<SkillAuditFinding> Findings,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record SkillAuditRequest(
    string RepositoryPath,
    bool Strict,
    bool DisableLlm,
    bool Fix,
    string? Model,
    int MaximumPairs);
