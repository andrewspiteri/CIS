using Cis.Abstractions;
using Cis.Modules.Brd;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    internal static CisDefinitionPageGuidance BusinessGuidance(BrdResult brd, CisDefinitionBusinessInference inference)
    {
        var validation = brd.Validation;
        var exists = validation is not null && validation.DocumentStatus != "Missing";
        var checkedState = validation is not null;
        var complete = Accepted(validation?.Valid, validation?.Current, validation?.EffectiveStatus);
        var sources = validation?.SourceReviews ?? [];
        var pending = sources.Count(source => source.NeedsReview);
        var unanswered = validation?.UnansweredQuestionCount ?? 0;
        var stale = exists && (validation?.Current != true || sources.Any(source => source.RequiresReconciliation));
        var contentIssues = validation?.ContentIssues ?? [];
        var owned = inference.Repositories.Count > 0;
        var prepared = owned && inference.Repositories.All(repository => repository.GraphFreshness == "fresh"
            && inference.LastPreparation.Any(report => report.RepositoryPath == repository.RepositoryPath
                && report.Errors.Count == 0 && report.Inventories.Count > 0));
        var reasons = new List<string>();
        if (!checkedState) reasons.AddRange(brd.Errors.DefaultIfEmpty("CIS could not check the business definition."));
        else if (!exists) reasons.Add("A business requirements document has not been created yet.");
        else
        {
            if (pending > 0) reasons.Add($"{pending} source document{(pending == 1 ? string.Empty : "s")} need a review decision or supporting explanation.");
            if (unanswered > 0) reasons.Add($"{unanswered} business question{(unanswered == 1 ? string.Empty : "s")} still need an answer.");
            if (stale) reasons.Add("The BRD's repository evidence is out of date. Refresh it and review any changes.");
            reasons.AddRange(contentIssues);
            if (!complete && reasons.Count == 0) reasons.Add("Review the BRD validation details and resolve the remaining findings.");
        }
        var next = !checkedState ? "doctor" : !exists ? owned ? prepared ? "infer-brd" : "prepare-evidence" : "draft-brd"
            : stale ? "refresh-evidence" : pending > 0 ? "source-decisions" : unanswered > 0 ? "questions"
            : !complete ? "open-brd" : "continue";
        var actions = new List<CisDefinitionActionGuidance>
        {
            new("prepare-evidence", "Prepare existing-system context", !owned ? "Not needed" : prepared ? "Complete" : exists ? "Optional" : "Needed",
                !owned ? "No imported implementation repositories are selected." : prepared
                    ? "Dictionary preparation is recorded for all selected repositories. These are the last run's counts; rerunning is not needed to resolve source decisions."
                    : exists ? "A BRD already exists. Preparing dictionaries again is not required to resolve source decisions."
                    : "Discover API, data, workflow and other dictionaries before drafting from the implementation."),
            new("infer-brd", "Infer from existing project", !owned ? "Not needed" : exists ? "Optional" : "Needed",
                exists ? "The BRD already exists. Use this only if you intend to draft it again from the implementation."
                    : "Draft the business narrative from the selected product repositories."),
            new("draft-brd", "Draft from references", exists || owned ? "Optional" : "Needed",
                exists ? "The BRD already exists; drafting again is not needed to complete its review."
                    : "Use reference documents to draft the business narrative; this is an alternative to implementation inference."),
            new("refresh-evidence", "Refresh BRD evidence", stale ? "Needed" : "Optional",
                stale ? "Bring the BRD's references up to date with the repositories, then review any changed source decisions."
                    : "Recheck and reconcile repository evidence when it changes."),
            new("source-decisions", "Review source decisions", !exists ? "Later" : pending > 0 ? "Needed" : "Complete",
                !exists ? "Create the BRD first." : pending > 0
                    ? $"Review {pending} source document{(pending == 1 ? string.Empty : "s")}. Decide how each supports the BRD and explain your choice."
                    : sources.Count == 0 ? "No discovered source documents require a decision." : "All discovered source decisions are recorded and current."),
            new("open-brd", "Read or edit the BRD", exists && contentIssues.Count > 0 ? "Needed" : "Optional",
                contentIssues.Count > 0 ? "Resolve the listed document content issues, then refresh readiness." : "Review the product narrative and edit it whenever needed."),
            new("review-brd", "Independent review", exists ? "Optional" : "Later",
                "Use a second provider to check the draft. This is an advisory review, not business approval; rerun it if the draft changes."),
            new("questions", unanswered > 0 ? $"Answer {unanswered} open questions" : "Review business questions", !exists ? "Later" : unanswered > 0 ? "Needed" : "Complete",
                !exists ? "Create the BRD first." : unanswered > 0 ? "Record human answers to the open business questions."
                    : "No unanswered questions were found. You can still inspect the recorded questions."),
            new("continue", "Continue to technical direction", complete ? "Ready" : "Later",
                complete ? "This page is complete and current. Approval of the whole baseline takes place on the final page."
                    : "Resolve the needed actions before continuing."),
            new("doctor", "Open Repository Doctor", !checkedState ? "Needed" : "Optional", "Inspect workspace configuration and loading errors."),
        };
        if (!checkedState) actions = actions.Select(action => action.Id == "doctor" ? action
            : action with { Status = "Later", Reason = "Resolve the workspace check before choosing a business-definition action." }).ToList();
        return new(!checkedState ? "The business definition could not be checked." : complete ? "The business definition is complete and current." : exists
            ? "The BRD exists, but its review is not complete." : "Start the business definition.", next,
            actions.Single(action => action.Id == next).Reason, reasons, actions) { SourceReviews = sources };
    }
}
