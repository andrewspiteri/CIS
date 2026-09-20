using Cis.Abstractions;
using Cis.Modules.Definition;
using Cis.Modules.TechnicalIntent;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed class TechnicalGuidanceTests
{
    [Fact]
    public void CompletedQuestionnaireStillRoutesToUnresolvedDocumentDecisions()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var decision = new CisTechnicalDecisionReview("TI-DEC-001", "Confirm recovery ownership", "Change dossier creation",
            "Open", "Awaiting operator review.", true, 70, ["Technical decision remains unresolved: TI-DEC-001"]);
        var intent = Intent(false, true) with { Validation = new(false, true, "Review Required", "Review Required", decision.Issues, []) { Decisions = [decision] } };
        var guidance = DefinitionWizardService.TechnicalGuidance(intent, Questions(), true);
        Assert.Equal("decisions", guidance.NextActionId);
        Assert.Contains("Questionnaire complete", guidance.Summary, StringComparison.Ordinal);
        Assert.Equal("Complete", Action(guidance, "questions").Status);
        Assert.Equal("Needed", Action(guidance, "decisions").Status);
        Assert.Equal("Optional", Action(guidance, "infer-technical").Status);
        Assert.Equal("Optional", Action(guidance, "prepare-technical").Status);
        Assert.Equal("Later", Action(guidance, "continue").Status);
        Assert.Equal(decision, Assert.Single(guidance.TechnicalDecisions));
    }

    [Fact]
    public void CompletedAnswersDoNotHideContentOrFreshnessProblems()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var intent = Intent(false, true) with { Validation = new(false, true, "Review Required", "Review Required", ["Security section is incomplete."], []) };
        var guidance = DefinitionWizardService.TechnicalGuidance(intent, Questions(), false);
        Assert.Equal("open-intent", guidance.NextActionId);
        Assert.Contains("Security section is incomplete.", guidance.Reasons);
        guidance = DefinitionWizardService.TechnicalGuidance(Intent(true, false), Questions(), false);
        Assert.Equal("prepare-technical", guidance.NextActionId);
        Assert.Contains(guidance.Reasons, reason => reason.Contains("out of date", StringComparison.Ordinal));
        guidance = DefinitionWizardService.TechnicalGuidance(Intent(true, true), Questions() with { Current = false }, false);
        Assert.Equal("prepare-technical", guidance.NextActionId);
        Assert.Equal("Needed", Action(guidance, "questions").Status);
    }

    [Fact]
    public void ReadyDocumentContinuesAndIncompleteAnswersRemainActionable()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var guidance = DefinitionWizardService.TechnicalGuidance(Intent(true, true), Questions(), true);
        Assert.Equal("continue", guidance.NextActionId); Assert.Empty(guidance.Reasons);
        Assert.DoesNotContain(guidance.Actions, action => action.Status == "Needed");
        guidance = DefinitionWizardService.TechnicalGuidance(Intent(true, true), Questions() with { Complete = false, UnansweredCount = 1 }, true);
        Assert.Equal("questions", guidance.NextActionId);
        Assert.Equal("Needed", Action(guidance, "questions").Status);
    }

    [Fact]
    public void MissingDraftAndFailedWorkspaceCheckHaveExplicitRoutes()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var missing = Intent(false, false) with { Validation = new(false, false, "Missing", "Missing", ["Missing document."], []) };
        Assert.Equal("infer-technical", DefinitionWizardService.TechnicalGuidance(missing, Questions(), true).NextActionId);
        Assert.Equal("prepare-technical", DefinitionWizardService.TechnicalGuidance(missing, Questions(), false).NextActionId);
        Assert.Equal("prepare-technical", DefinitionWizardService.TechnicalGuidance(missing, Questions() with { Questions = [] }, true).NextActionId);
        var failed = DefinitionWizardService.TechnicalGuidance(missing with { Validation = null, Errors = ["Authority unavailable."] }, Questions(), true);
        Assert.Equal("doctor", failed.NextActionId);
        Assert.Equal("Later", Action(failed, "questions").Status);
        Assert.Contains("Authority unavailable.", failed.Reasons);
    }

    private static CisDefinitionActionGuidance Action(CisDefinitionPageGuidance guidance, string id) => guidance.Actions.Single(action => action.Id == id);
    private static TechnicalIntentResult Intent(bool valid, bool current) => new("status", "workspace", "authority", "docs/technical.md",
        new(valid, current, valid && current ? "Ready for Approval" : "Review Required", "Review Required", [], []), [], [], [], false);
    private static TechnicalIntentQuestionnaireResult Questions() => new("status", "workspace", "authority", "docs/questions.md", "hash", true, true, 1, 0,
        [new("TI-Q-001", "Scope", "Which surfaces?", "Ownership", [], "", "Derived", "Existing surfaces", "CIS", null)], [], [], false);
}
