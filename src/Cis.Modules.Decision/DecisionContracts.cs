namespace Cis.Modules.Decision;

public sealed record DecisionCreateRequest(
    string RepositoryPath,
    string ChangeId,
    string Question,
    string Category,
    IReadOnlyList<string> Options,
    bool Blocking,
    string RequiredBefore,
    string Evidence);

public sealed record DecisionRecord(
    string Id,
    string Category,
    string Question,
    bool Blocking,
    string RequiredBefore,
    string Status,
    IReadOnlyList<string> Options,
    string Resolution,
    string Rationale,
    IReadOnlyList<string> Evidence,
    string PromotedAdr);

public sealed record DecisionReadResult(
    IReadOnlyList<DecisionRecord> Decisions,
    int BlockingOpen,
    int AdvisoryOpen,
    IReadOnlyList<string> Errors);

public sealed record DecisionResult(
    string Status,
    string? ChangeId,
    DecisionRecord? Decision,
    IReadOnlyList<DecisionRecord> Decisions,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 2 : 0;
}
