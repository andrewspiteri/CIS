using Cis.Abstractions;
using Cis.Modules.Brd;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    internal static CisDefinitionPageGuidance DeliveryGuidance(BrdBacklogResult backlog, bool upstreamReady)
    {
        var missing = backlog.Validation?.DocumentStatus == "Missing" || backlog.Status == "missing";
        var noWork = backlog.Mode == "no-planned-work";
        var ready = backlog.Validation is { Valid: true, Current: true };
        var reasons = new List<string>();
        if (missing) reasons.Add("No delivery scope has been recorded yet. An existing product can have no planned new work.");
        if (!upstreamReady) reasons.Add("Complete the business, technical, architecture and experience pages before recording delivery scope.");
        if (noWork) reasons.Add($"No planned work was recorded by {backlog.NoPlannedWorkBy}. This does not assert that every requirement is implemented or verified.");
        if (!missing && !ready) reasons.AddRange(backlog.Errors.Concat(backlog.Validation?.Errors ?? []).Concat(backlog.Validation?.Warnings ?? []));
        return new(missing ? "Choose the delivery scope for this baseline."
                : noWork ? ready ? "No planned work is recorded for this baseline." : "Review the no-planned-work decision against the changed baseline."
                : ready ? "Review the candidate backlog before activation." : "The candidate backlog needs attention.",
            ready ? "continue" : "choose-backlog",
            ready ? "Review the recorded delivery scope, then continue to Review and activate."
                : "Create candidate outcomes if new work needs planning, or explicitly record no planned work. Current product capabilities do not automatically imply unfinished delivery work.",
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            [new("build-backlog", noWork || missing ? "Create candidate backlog" : "Rebuild candidate backlog", !upstreamReady ? "Blocked" : ready ? "Optional" : noWork || missing ? "Optional" : "Needed",
                "Generate candidate outcomes from the BRD. Review existing capabilities before treating a candidate as new work."),
             new("record-no-work", noWork ? "Confirm no planned work" : "Record no planned work", !upstreamReady || !missing && !noWork ? "Blocked" : noWork && ready ? "Complete" : "Optional",
                !missing && !noWork ? "Existing backlog items and feature links must be reviewed; this action cannot discard them."
                    : "Record a named human decision for this baseline, without creating delivery items. Final approval remains on Review and activate.")]);
    }
}
