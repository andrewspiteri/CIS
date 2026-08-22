using Cis.Abstractions;

namespace Cis.Modules.Standards;

public sealed record StandardPatternCatalogRequest(
    string RepositoryPath,
    IReadOnlyList<string> Languages,
    bool ApplicableOnly,
    bool Strict);

public sealed record StandardPatternCatalogResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    int ProviderCount,
    IReadOnlyList<StandardPatternCatalogItem> Patterns,
    IReadOnlyList<StandardPatternDiagnostic> Diagnostics,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record StandardPatternCatalogItem(
    CisStandardPatternDefinition Definition,
    bool Applicable,
    string Readiness,
    IReadOnlyList<string> MissingGraphCapabilities);

public sealed record StandardPatternDiagnostic(
    string Code,
    string Severity,
    string PatternId,
    string Source,
    string Message,
    string Remediation);

public sealed record StandardInferenceRequest(
    string RepositoryPath,
    IReadOnlyList<string> PatternIds,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Components,
    int EvidenceLimit);

public sealed record StandardInferenceResult(
    string Status,
    int ExitCode,
    string? RepositoryPath,
    string? GraphBuildId,
    string Freshness,
    int PatternCount,
    int EvaluatedCount,
    int CandidateCount,
    string? JsonPath,
    string? MarkdownPath,
    IReadOnlyList<StandardPatternEvaluation> Evaluations,
    IReadOnlyList<InferredStandardCandidate> Candidates,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record StandardPatternEvaluation(
    string PatternId,
    int Version,
    string Status,
    int EligibleOccurrences,
    int MatchingOccurrences,
    double Consistency,
    IReadOnlyList<string> MissingGraphCapabilities,
    IReadOnlyList<StandardPatternEvidence> Matches,
    IReadOnlyList<StandardPatternEvidence> Counterexamples);

public sealed record StandardPatternEvidence(
    string NodeKey,
    string Label,
    string? Path,
    string? Locator,
    IReadOnlyList<string> RelatedNodeKeys);

public sealed record InferredStandardCandidate(
    string Id,
    string PatternId,
    int PatternVersion,
    string Status,
    string Target,
    string ProposedLevel,
    string ProposedWording,
    string SuggestedEnforcement,
    string Verification,
    int EligibleOccurrences,
    int MatchingOccurrences,
    double Consistency,
    string GraphBuildId,
    IReadOnlyList<StandardPatternEvidence> Evidence,
    IReadOnlyList<StandardPatternEvidence> Counterexamples);
