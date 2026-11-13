using Cis.Modules.Change;

namespace Cis.Modules.Impact;

public sealed record ImpactAnalyseRequest(
    string RepositoryPath,
    string ChangeId,
    IReadOnlyList<ChangeRoot> Roots,
    int Depth,
    int Limit,
    bool IncludeProposed);

public sealed record ImpactFinding(
    string Id,
    string Category,
    string Target,
    string Label,
    string State,
    string Confidence,
    string Evidence,
    string Rationale,
    string ReviewReason)
{
    public ImpactOutcomeDisposition? OutcomeDisposition { get; init; }
}

public sealed record ImpactOutcomeDisposition(string AuthorizationId, string AuthorizationDigest,
    IReadOnlyList<string> ScopeIds, string ScopeBasis, string Reviewer, string DecisionReference,
    string Actor, string Reason, IReadOnlyList<string> Paths);

public sealed record ImpactCompleteness(
    int Total,
    int Proposed,
    int Accepted,
    int Rejected,
    int Deferred,
    IReadOnlyList<string> Categories,
    bool Truncated,
    bool ReviewComplete,
    bool PlanReady,
    IReadOnlyList<string> Gaps);

public sealed record ImpactResult(
    string Status,
    string? ChangeId,
    string? GraphBuildId,
    IReadOnlyList<ImpactFinding> Findings,
    ImpactCompleteness? Completeness,
    IReadOnlyList<string> Diagnostics,
    bool Applied)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 2 : 0;
}
