using Cis.Abstractions;
using Cis.Modules.TechnicalIntent;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    internal static CisDefinitionPageGuidance TechnicalGuidance(TechnicalIntentResult intent,
        TechnicalIntentQuestionnaireResult questions, bool hasImplementation)
    {
        var validation = intent.Validation;
        var checkedState = validation is not null;
        var exists = checkedState && validation!.DocumentStatus != "Missing";
        var initialized = questions.Questions.Count > 0;
        var answersReady = questions.Complete && questions.Current;
        var complete = answersReady && Accepted(validation?.Valid, validation?.Current, validation?.EffectiveStatus);
        var decisions = validation?.Decisions ?? [];
        var pending = decisions.Count(decision => decision.NeedsReview);
        var stale = initialized && !questions.Current || exists && validation?.Current != true;
        var decisionIssues = decisions.SelectMany(decision => decision.Issues).ToHashSet(StringComparer.Ordinal);
        var contentIssues = (validation?.Errors ?? []).Where(issue => !decisionIssues.Contains(issue)).ToArray();
        var reasons = new List<string>();
        if (!checkedState) reasons.AddRange(intent.Errors.DefaultIfEmpty("CIS could not check the technical-intent document."));
        else
        {
            if (!initialized) reasons.Add("The technical questionnaire has not been prepared yet.");
            else if (!questions.Complete) reasons.Add($"{questions.UnansweredCount} questionnaire answers still need to be recorded.");
            if (!exists) reasons.Add("The technical-intent document has not been created yet.");
            if (pending > 0) reasons.Add($"{pending} technical decision{(pending == 1 ? "" : "s")} in the document still need review, separately from the questionnaire answers.");
            if (stale) reasons.Add("The questionnaire or technical-intent evidence is out of date. Refresh it and review the changes.");
            if (exists) reasons.AddRange(contentIssues);
            reasons.AddRange(questions.Errors);
            if (stale) reasons.AddRange(validation?.Warnings ?? []);
            if (!complete && reasons.Count == 0) reasons.Add("Review the technical-intent validation findings before continuing.");
        }
        var next = !checkedState ? "doctor" : !initialized || stale ? "prepare-technical" : !questions.Complete ? "questions"
            : !exists ? hasImplementation ? "infer-technical" : "prepare-technical" : pending > 0 ? "decisions"
            : !complete ? "open-intent" : "continue";
        var actions = new List<CisDefinitionActionGuidance>
        {
            new("questions", answersReady ? "Review questionnaire answers" : "Complete questionnaire answers",
                !initialized ? "Later" : answersReady ? "Complete" : "Needed",
                answersReady ? $"All {questions.Questions.Count} questionnaire answers are resolved and current. The document's open technical decisions are checked separately."
                    : !initialized ? "Prepare the questionnaire first." : "Record the remaining answers, then review the technical-intent document."),
            new("decisions", pending > 0 ? $"Review {pending} technical decisions" : "Review technical decisions",
                !exists ? "Later" : pending > 0 ? "Needed" : "Complete",
                !exists ? "Create the technical-intent document first." : pending > 0
                    ? "Review the prefilled suggested answers, edit any remaining choice, and save each answer in this wizard. Readiness refreshes after saving."
                    : "No technical decision rows require attention."),
            new("open-intent", "Read or edit technical intent", exists && contentIssues.Length > 0 ? "Needed" : exists ? "Optional" : "Later",
                exists && contentIssues.Length > 0 ? "Correct the listed document findings, then refresh readiness." : "Review the technical narrative and the choices it records."),
            new("prepare-technical", "Prepare or refresh technical evidence", !initialized || stale || !exists && !hasImplementation ? "Needed" : "Optional",
                stale ? "Refresh the questionnaire and document baselines while preserving recorded human choices. Then review the remaining findings."
                    : !initialized || !exists ? "Prepare the questionnaire and technical-intent draft from the current baseline."
                    : "The current evidence is already prepared. Repeating preparation does not resolve open technical decisions."),
            new("infer-technical", "Infer from existing repositories", !hasImplementation ? "Not needed" : exists ? "Optional" : "Needed",
                exists ? "The technical-intent document already exists. Generate another draft only if you intend to revisit its narrative."
                    : "Draft the technical narrative from the owned implementation repositories."),
            new("continue", "Continue to solution architecture", complete ? "Ready" : "Later",
                complete ? "The questionnaire and technical-intent document are complete and current. Continue to solution architecture; activation happens on the final page."
                    : "Complete the needed actions before continuing."),
            new("doctor", "Open Repository Doctor", checkedState ? "Optional" : "Needed", "Inspect the workspace configuration and loading errors."),
        };
        if (!checkedState) actions = actions.Select(action => action.Id == "doctor" ? action
            : action with { Status = "Later", Reason = "Resolve the workspace check before choosing a technical-direction action." }).ToList();
        return new(!checkedState ? "Technical direction could not be checked." : complete ? "Technical direction is complete and current."
            : answersReady && pending > 0 ? $"Questionnaire complete; {pending} technical decisions still need review."
            : answersReady ? "Questionnaire complete; the technical-intent document still needs attention." : "Complete the technical questionnaire and document review.",
            next, actions.Single(action => action.Id == next).Reason, reasons.Distinct().ToArray(), actions)
        { TechnicalDecisions = decisions };
    }
}
