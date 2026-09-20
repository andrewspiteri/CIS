using Cis.Abstractions;
using Cis.Modules.Brd;

namespace Cis.Modules.Definition.Tests;

public sealed class BusinessGuidanceTests
{
    [Xunit.Fact]
    public void ExistingDraftExplainsSourceDecisionsAndKeepsCompletedWorkOptional()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var brd = Result(false, true) with { Validation = new(false, true, "Review Required", "Review Required", [], [])
            { SourceReviews = [new("source-1", "api", "repo", "BRD.md", "Unreviewed", "TODO", true, 12, ["Choose a decision."])] } };
        var guidance = DefinitionWizardService.BusinessGuidance(brd, Inference());
        Xunit.Assert.Equal("source-decisions", guidance.NextActionId);
        Xunit.Assert.Contains(guidance.Reasons, reason => reason.Contains("1 source document", StringComparison.Ordinal));
        Xunit.Assert.Equal("Complete", Action(guidance, "prepare-evidence").Status);
        Xunit.Assert.Equal("Optional", Action(guidance, "infer-brd").Status);
        Xunit.Assert.Equal("Optional", Action(guidance, "draft-brd").Status);
        Xunit.Assert.Equal("Complete", Action(guidance, "questions").Status);
        Xunit.Assert.Single(guidance.SourceReviews);
    }

    [Xunit.Fact]
    public void StaleEvidenceHasAnExplicitRefreshStepEvenWithoutValidationErrors()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var guidance = DefinitionWizardService.BusinessGuidance(Result(true, false), Inference());
        Xunit.Assert.Equal("refresh-evidence", guidance.NextActionId);
        Xunit.Assert.Contains(guidance.Reasons, reason => reason.Contains("out of date", StringComparison.Ordinal));
        Xunit.Assert.Equal("Later", Action(guidance, "continue").Status);
    }

    [Xunit.Fact]
    public void NewSourceRowsRouteToReconciliationBeforeEditing()
    {
        var brd = Result(false, true) with { Validation = new(false, true, "Review Required", "Review Required", [], [])
            { SourceReviews = [new("source-1", "api", "repo", "BRD.md", "Not recorded", "", true, null, []) { RequiresReconciliation = true }] } };
        Xunit.Assert.Equal("refresh-evidence", DefinitionWizardService.BusinessGuidance(brd, Inference()).NextActionId);
    }

    [Xunit.Fact]
    public void UnansweredQuestionsAreNeededAndDocumentIssuesRemainVisible()
    {
        var brd = Result(false, true) with { Validation = new(false, true, "Review Required", "Draft", [], [])
            { UnansweredQuestionCount = 2, ContentIssues = ["Scope is incomplete."] } };
        var guidance = DefinitionWizardService.BusinessGuidance(brd, Inference());
        Xunit.Assert.Equal("questions", guidance.NextActionId);
        Xunit.Assert.Equal("Needed", Action(guidance, "questions").Status);
        Xunit.Assert.Contains("Scope is incomplete.", guidance.Reasons);
    }

    [Xunit.Fact]
    public void ReadyDraftContinuesWithoutInventingAnotherRequiredReviewGate()
    {
        using var scope = CisDefinitionDraftScope.Enter();
        var guidance = DefinitionWizardService.BusinessGuidance(Result(true, true), Inference());
        Xunit.Assert.Equal("continue", guidance.NextActionId);
        Xunit.Assert.Empty(guidance.Reasons);
        Xunit.Assert.DoesNotContain(guidance.Actions, action => action.Status == "Needed");
        Xunit.Assert.Equal("Optional", Action(guidance, "review-brd").Status);
    }

    [Xunit.Fact]
    public void MissingDraftAndUnavailableChecksHaveDifferentNextSteps()
    {
        var missing = Result(false, false) with { Status = "missing", Validation = new(false, false, "Missing", "Missing", [], []) };
        Xunit.Assert.Equal("infer-brd", DefinitionWizardService.BusinessGuidance(missing, Inference()).NextActionId);
        Xunit.Assert.Equal("draft-brd", DefinitionWizardService.BusinessGuidance(missing, new(true, [])).NextActionId);
        var invalid = missing with { Status = "invalid", Validation = null, Errors = ["Workspace is invalid."] };
        var guidance = DefinitionWizardService.BusinessGuidance(invalid, Inference());
        Xunit.Assert.Equal("doctor", guidance.NextActionId);
        Xunit.Assert.Equal("Later", Action(guidance, "draft-brd").Status);
        Xunit.Assert.Contains("Workspace is invalid.", guidance.Reasons);
    }

    private static CisDefinitionActionGuidance Action(CisDefinitionPageGuidance guidance, string id)
        => guidance.Actions.Single(action => action.Id == id);

    private static BrdResult Result(bool valid, bool current) => new("status", "workspace", "authority", "docs/brd.md", null,
        new(valid, current, valid ? "Ready for Approval" : "Review Required", "Review Required", [], []), [], [], false);

    private static CisDefinitionBusinessInference Inference() => new(true, [new("api", "repo", "fresh")])
    { LastPreparation = [new("repo", [new("api", "api.md", 5, 0, "unchanged")], [], [], false)] };
}
