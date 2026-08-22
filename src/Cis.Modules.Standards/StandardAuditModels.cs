namespace Cis.Modules.Standards;

public sealed record StandardAuditFinding(
    string Id,
    string Kind,
    string Severity,
    IReadOnlyList<string> Standards,
    string Summary,
    IReadOnlyList<string> Evidence,
    string Method,
    double Confidence);

public sealed record StandardAuditResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    int StandardCount,
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
    IReadOnlyList<string> QuarantinedStandards,
    IReadOnlyList<StandardAuditFinding> Findings,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record StandardAuditRequest(
    string RepositoryPath,
    bool Strict,
    bool DisableLlm,
    bool Fix,
    string? Model,
    int MaximumPairs);
