using Cis.Modules.Brd;

namespace Cis.Modules.TechnicalIntent;

public sealed record GovernanceRefreshResult(
    string Status,
    BrdResult Brd,
    TechnicalIntentResult? TechnicalIntent,
    BrdBacklogResult? Backlog,
    IReadOnlyList<string> Diagnostics,
    bool Applied)
{
    public int ExitCode => Brd.ExitCode != 0
        ? Brd.ExitCode
        : TechnicalIntent is { ExitCode: not 0 } intent
            ? intent.ExitCode
            : Backlog is { ExitCode: not 0 } backlog
                ? backlog.ExitCode
                : Status == "blocked" ? 5 : 0;
}

/// <summary>
/// Refreshes managed governance baselines while preserving existing human authority
/// only when each canonical service proves that its approved semantic content is unchanged.
/// </summary>
public sealed class GovernanceRefreshService(
    BrdService brd,
    TechnicalIntentService technicalIntent,
    BrdBacklogService backlog)
{
    public GovernanceRefreshResult Refresh(string workspacePath)
    {
        var brdResult = brd.Reconcile(workspacePath);
        if (brdResult.ExitCode != 0 || brdResult.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            return new("blocked", brdResult, null, null,
                ["BRD reconciliation found a material or unresolved evidence change; review only the reported BRD differences."], brdResult.Applied);

        var intentResult = technicalIntent.Initialize(workspacePath);
        if (intentResult.ExitCode != 0 || intentResult.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            return new("blocked", brdResult, intentResult, null,
                ["Technical-intent reconciliation found a material or unresolved change; review only the reported technical differences."],
                brdResult.Applied || intentResult.Applied);

        var backlogResult = backlog.Build(workspacePath);
        if (backlogResult.ExitCode != 0 || backlogResult.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            return new("blocked", brdResult, intentResult, backlogResult,
                ["High-level backlog regeneration changed reviewed product outcomes or found an unresolved gate; review only the reported backlog differences."],
                brdResult.Applied || intentResult.Applied || backlogResult.Applied);

        var applied = brdResult.Applied || intentResult.Applied || backlogResult.Applied;
        return new(applied ? "refreshed" : "unchanged", brdResult, intentResult, backlogResult,
            ["Existing human authority remains current; managed baselines were refreshed without a duplicate approval."], applied);
    }
}
