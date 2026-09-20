using Cis.Abstractions;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentService
{
    // Advisory text only: rendering or refreshing never records a human choice.
    // Keep suggestions local and deterministic so status does not start a model or scan.
    private static string SuggestDecisionResolution(string decision, string[] questionIds, CisTechnicalDecisionAnswer[] answers)
    {
        var subject = decision.ToLowerInvariant();
        string proposal;
        if (subject.Contains("reconcil", StringComparison.Ordinal)
            && (subject.Contains("ledger", StringComparison.Ordinal) || subject.Contains("financial", StringComparison.Ordinal)))
            proposal = "Use the ledger responsible for posted transactions as the financial source of truth, with local balances, settlement views and statements reconciled against it. Preserve issued statements and transaction history; correct errors through auditable adjustments rather than rewriting historical records. Surface mismatches for review by the accountable finance or operations owner, and pause affected financial transitions until the discrepancy is resolved. This keeps corrections traceable and prevents conflicting representations of the same funds.";
        else if (subject.Contains("failure", StringComparison.Ordinal)
            && (subject.Contains("signing", StringComparison.Ordinal) || subject.Contains("replay", StringComparison.Ordinal) || subject.Contains("notification", StringComparison.Ordinal)))
            proposal = "Reject operations that cannot establish required signing, authentication or replay protection. Persist outbound delivery work durably, retry temporary publishing and notification failures with bounded backoff and idempotency, and expose exhausted retries for operational recovery. Report a delivery as complete only after acknowledgement. Assign recovery ownership to the team operating the affected integration. This prevents silent loss or duplicate processing while making failures visible and recoverable.";
        else if (subject.Contains("retention", StringComparison.Ordinal)
            && (subject.Contains("erasure", StringComparison.Ordinal) || subject.Contains("export", StringComparison.Ordinal) || subject.Contains("privacy", StringComparison.Ordinal)))
            proposal = "Apply export, erasure and retention handling across primary records, history, operational logs, documents, reports, identity records and third-party copies. Maintain an inventory of those locations and record completion or a justified exception for each request. Have the accountable privacy owner approve record-specific retention periods and legal holds before enabling deletion; retain only what that schedule requires and restrict retained records. Track downstream erasure requests and their confirmations. This gives a consistent, auditable scope without assuming one retention period fits every record.";
        else if (questionIds.Contains("TI-Q-006"))
            proposal = "Keep the recorded storage technologies and assign one authoritative owner to each business record. Treat caches and derived views as rebuildable. Use transactions within an ownership boundary and explicit reconciliation across boundaries. Version and rehearse migrations, test backup restoration, and require an approved retention schedule before deleting records. This preserves data consistency and a recoverable history without introducing an unnecessary storage migration.";
        else if (questionIds.Contains("TI-Q-008"))
            proposal = "Keep the recorded integration and asynchronous-work approach. Assign each contract an owner, version externally consumed contracts, and test compatibility before release. Authenticate requests, use bounded retries and idempotency for repeatable operations, and expose failed delivery for recovery. This preserves existing consumers while making failures predictable.";
        else if (questionIds.Contains("TI-Q-009"))
            proposal = "Keep the recorded identity and security baseline. Enforce authorization on the server at each protected operation, separate privileged administration from routine access, and limit access to sensitive data and audit records by role. Record privileged changes and verify authorization and data exposure boundaries with tests. This keeps access decisions consistent across product surfaces.";
        else if (questionIds.Contains("TI-Q-010"))
            proposal = "Keep the recorded deployment, operations and testing direction. Assign environment and incident ownership, monitor critical workflows, and require tested rollback and recovery procedures at release. Set capacity, cost and recovery targets from measured demand and business tolerance before production changes. This gives the existing platform measurable operating limits and a clear recovery path.";
        else if (questionIds.Contains("TI-Q-015"))
            proposal = "Follow the recorded AI applicability decision. Where models are in scope, version models and evaluation evidence, require evaluation before promotion, and retain rollback and human oversight for consequential output. This keeps model changes reviewable without expanding the agreed product scope.";
        else if (questionIds.Contains("TI-Q-001"))
            proposal = "Keep the recorded technology, component and repository boundaries. Give each component one accountable owner, keep business rules with that owner, and route cross-component changes through explicit contracts. Record any ownership exception before changing the boundary. This preserves the existing architecture while clarifying responsibility and dependency direction.";
        else
            proposal = "Preserve the current documented approach within this decision's scope, with one accountable owner and explicit contracts at affected boundaries. Require evidence of compatibility, failure handling and recovery before changing the approach, and record any exception in the technical intent. This limits unintended changes while keeping the decision testable. Tailor this proposal to the specific choice described above before saving.";

        var recorded = string.Join("\n\n", answers.Select(answer => $"{answer.Area}: {answer.Answer}"));
        // The form's bounded input must always be able to display the complete suggestion.
        return recorded.Length > 12000 ? proposal : string.Join("\n\n", new[] { recorded, proposal }.Where(text => text.Length > 0));
    }
}
