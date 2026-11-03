using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Graph;
using Cis.Modules.TechnicalIntent;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    [Fact]
    public void DecisionAssessment_PreservesArchitectureForModelAnswerButReopensForArchitectureAnswerAcrossServices()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var first = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == "TI-DEC-001");
        Assert.Equal(0, environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-015", "Initially no AI capability.", "Model owner").ExitCode);
        environment.Intent.Initialize(environment.Authority.Path);
        first = FreshIntent(environment).Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == first.Id);
        Assert.Equal("Accepted", first.Status);
        Assert.False(first.NeedsReview, string.Join("\n", first.Issues));
        Assert.Equal(0, environment.Intent.ResolveDecision(environment.Authority.Path,
            new(first.Id, "Reviewed component ownership and inward dependencies.", "Architecture accepted.", first.ReviewToken!, "Architect")).ExitCode);
        var recorded = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == first.Id);
        Assert.False(recorded.NeedsReview);
        var staleWriteToken = recorded.ReviewToken!;
        Assert.Equal(0, environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-015", "No AI in this product.", "Model owner").ExitCode);
        environment.Intent.Initialize(environment.Authority.Path);
        var fresh = FreshIntent(environment);
        var unaffected = fresh.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == first.Id);
        Assert.False(unaffected.NeedsReview, string.Join("\n", unaffected.Issues));
        Assert.Equal("Current", unaffected.AssessmentStatus);
        Assert.Equal(recorded.RecordedResolution, unaffected.RecordedResolution);
        Assert.Equal(recorded.RecordedAtUtc, unaffected.RecordedAtUtc);
        // Raw optimistic concurrency remains strict even when assessment carries.
        Assert.Equal("conflict", fresh.ResolveDecision(environment.Authority.Path,
            new(first.Id, recorded.RecordedResolution!, recorded.RecordedReason, staleWriteToken, "Architect")).Status);
        Assert.Equal(0, environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-004", "Independent event-driven services.", "Architect").ExitCode);
        environment.Intent.Initialize(environment.Authority.Path);
        var affected = FreshIntent(environment).Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == first.Id);
        Assert.True(affected.NeedsReview);
        Assert.Equal("NeedsReview", affected.AssessmentStatus);
        Assert.Contains("TI-Q-004", affected.AssessmentReason, StringComparison.Ordinal);
        Assert.Equal(recorded.RecordedResolution, affected.RecordedResolution);
        Assert.Equal("Architect", affected.RecordedBy);
    }

    [Fact]
    public void RepeatedHumanSaves_AreNoopsAndRetainAggregateApprovalWhileNewActorIsAudited()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var decision = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == "TI-DEC-001");
        Assert.Equal(0, environment.Intent.ResolveDecision(environment.Authority.Path,
            new(decision.Id, "Reviewed component ownership.", "Accepted scope.", decision.ReviewToken!, "Architect")).ExitCode);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Technical authority", "Intent accepted.").ExitCode);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        var beforeQuestionnaire = File.ReadAllBytes(questionnairePath);
        var beforeDocument = File.ReadAllBytes(environment.TechnicalIntentPath);
        var current = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == decision.Id);
        var saved = FreshIntent(environment).ResolveDecision(environment.Authority.Path,
            new(decision.Id, current.RecordedResolution!, current.RecordedReason, current.ReviewToken!, "Architect"));
        Assert.False(saved.Applied);
        Assert.Equal("unchanged", saved.Status);
        var question = environment.Questionnaire.Status(environment.Authority.Path).Questions.Single(item => item.Id == "TI-Q-004");
        var answer = environment.Questionnaire.Answer(environment.Authority.Path, question.Id, question.Answer!, question.AnsweredBy!);
        Assert.False(answer.Applied);
        Assert.Equal(beforeQuestionnaire, File.ReadAllBytes(questionnairePath));
        Assert.Equal(beforeDocument, File.ReadAllBytes(environment.TechnicalIntentPath));
        Assert.Equal("Active", FreshIntent(environment).Status(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.False(environment.Questionnaire.Initialize(environment.Authority.Path).Applied);
        Assert.False(environment.Intent.Initialize(environment.Authority.Path).Applied);
        Assert.Equal(beforeDocument, File.ReadAllBytes(environment.TechnicalIntentPath));
        var reassigned = FreshIntent(environment).ResolveDecision(environment.Authority.Path,
            new(decision.Id, current.RecordedResolution!, "Second authority confirms this scope.", current.ReviewToken!, "Second architect"));
        Assert.True(reassigned.Applied);
        var updated = FreshIntent(environment).Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == decision.Id);
        Assert.Equal("Second architect", updated.RecordedBy);
        Assert.False(updated.NeedsReview);
        Assert.Contains("Architect", File.ReadAllText(environment.TechnicalIntentPath), StringComparison.Ordinal);
        Assert.NotEqual("Active", FreshIntent(environment).Status(environment.Authority.Path).Validation!.EffectiveStatus);
    }

    [Theory]
    [InlineData("AI and model lifecycle", "The AI model evaluation shall require human approval.", "TI-Q-015", "TI-Q-004")]
    [InlineData("Runtime architecture", "The product shall use a modular monolith with explicit module boundaries.", "TI-Q-004", "TI-Q-015")]
    public void BrdRenewal_RetainsHumanAnswersAndOnlyReassessesRelevantQuestions(string heading, string requirement, string affectedId, string unaffectedId)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        var before = environment.Questionnaire.Status(environment.Authority.Path).Questions.ToDictionary(item => item.Id);
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        File.AppendAllText(brdPath, $"\n## {heading}\n\nBRD-FR-016: {requirement}\n");
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Changed one requirement.").ExitCode);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        var beforeRead = File.ReadAllBytes(questionnairePath);
        var status = FreshQuestionnaire(environment).Status(environment.Authority.Path);
        Assert.Equal(beforeRead, File.ReadAllBytes(questionnairePath));
        var affected = status.Questions.Single(item => item.Id == affectedId);
        var unaffected = status.Questions.Single(item => item.Id == unaffectedId);
        Assert.Equal(before[affectedId].Answer, affected.Answer);
        Assert.Equal(before[affectedId].AnsweredBy, affected.AnsweredBy);
        Assert.Equal(before[affectedId].AnsweredAtUtc, affected.AnsweredAtUtc);
        Assert.Equal("Answered", affected.Status);
        Assert.Equal("NeedsReview", affected.AssessmentStatus);
        Assert.Contains("BRD-FR-016", affected.AssessmentReason, StringComparison.Ordinal);
        Assert.Equal("Current", unaffected.AssessmentStatus);
        Assert.Equal(before[unaffectedId].AnsweredAtUtc, unaffected.AnsweredAtUtc);
        Assert.Equal(0, status.UnansweredCount);
        Assert.True(status.ReassessmentCount > 0);
        Assert.False(status.Complete);
        var initialized = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal(before[affectedId].Answer, initialized.Questions.Single(item => item.Id == affectedId).Answer);
        var renewed = FreshQuestionnaire(environment).Answer(environment.Authority.Path, affectedId, affected.Answer!, affected.AnsweredBy!);
        var renewedQuestion = renewed.Questions.Single(item => item.Id == affectedId);
        Assert.Equal("Current", renewedQuestion.AssessmentStatus);
        Assert.Equal(before[affectedId].History.Count + 1, renewedQuestion.History.Count);
        Assert.Equal(before[affectedId].AnsweredAtUtc, renewedQuestion.History[^1].AnsweredAtUtc);
        Assert.False(FreshQuestionnaire(environment).Answer(environment.Authority.Path, affectedId, affected.Answer!, affected.AnsweredBy!).Applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralSupportingContract_ReopensOnlyRelevantAnswersAndDecisionsWithoutReinterpretingSourceFacts(bool comment)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        static string Literal(string value, bool inComment) => inComment ? "<!-- " + value + " -->" : "```json\n" + value + "\n```";
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        File.AppendAllText(brdPath, "\n## Runtime architecture\n\nPreserve the reviewed dependency contract.\n\n"
            + Literal("{\"boundary\":\"module\"}", comment)
            + "\n\n## AI and model lifecycle\n\nPreserve the reviewed evaluation contract.\n\n"
            + Literal("{\"model\":\"local-v1\"}", comment) + "\n");
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Reviewed literal contracts.").ExitCode);
        foreach (var question in environment.Questionnaire.Status(environment.Authority.Path).Questions
            .Where(question => question.AssessmentStatus is "NeedsReview" or "Unknown"))
            environment.Questionnaire.Answer(environment.Authority.Path, question.Id, question.Answer!, question.AnsweredBy!);
        Assert.True(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var architecture = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == "TI-DEC-001");
        Assert.Equal(0, environment.Intent.ResolveDecision(environment.Authority.Path,
            new(architecture.Id, "The reviewed module boundary remains authoritative.", "Architecture reviewed.", architecture.ReviewToken!, "Architect")).ExitCode);
        var recorded = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == architecture.Id);
        var recordedAnswer = environment.Questionnaire.Status(environment.Authority.Path).Questions.Single(item => item.Id == "TI-Q-004");

        File.WriteAllText(brdPath, File.ReadAllText(brdPath).Replace("local-v1", "local-v2", StringComparison.Ordinal));
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Changed only the model literal.").ExitCode);
        var questions = FreshQuestionnaire(environment).Status(environment.Authority.Path);
        Assert.Equal("NeedsReview", questions.Questions.Single(item => item.Id == "TI-Q-015").AssessmentStatus);
        Assert.Equal("Current", questions.Questions.Single(item => item.Id == "TI-Q-004").AssessmentStatus);
        var unaffected = FreshIntent(environment).Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == architecture.Id);
        Assert.False(unaffected.NeedsReview, string.Join("\n", unaffected.Issues));
        // Audit-only provenance changes are not changes to the answer's meaning.
        Assert.True(FreshQuestionnaire(environment).Answer(environment.Authority.Path, recordedAnswer.Id,
            recordedAnswer.Answer!, "Second technical owner").Applied);
        unaffected = FreshIntent(environment).Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == architecture.Id);
        Assert.False(unaffected.NeedsReview);
        Assert.Equal(recorded.RecordedAtUtc, unaffected.RecordedAtUtc);

        File.WriteAllText(brdPath, File.ReadAllText(brdPath).Replace("\"boundary\":\"module\"", "\"boundary\":\"process\"", StringComparison.Ordinal));
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Changed the runtime literal.").ExitCode);
        var path = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        var beforeRead = File.ReadAllBytes(path);
        questions = FreshQuestionnaire(environment).Status(environment.Authority.Path);
        Assert.Equal(beforeRead, File.ReadAllBytes(path));
        var affectedAnswer = questions.Questions.Single(item => item.Id == "TI-Q-004");
        Assert.Equal("NeedsReview", affectedAnswer.AssessmentStatus);
        Assert.Contains("raw-literal", affectedAnswer.AssessmentReason, StringComparison.Ordinal);
        Assert.Equal(recordedAnswer.Answer, affectedAnswer.Answer);
        Assert.Equal("Second technical owner", affectedAnswer.AnsweredBy);
        var affected = FreshIntent(environment).Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == architecture.Id);
        Assert.True(affected.NeedsReview);
        Assert.Equal(recorded.RecordedResolution, affected.RecordedResolution);
        Assert.Equal(recorded.RecordedAtUtc, affected.RecordedAtUtc);
        Assert.Equal("Architect", affected.RecordedBy);
    }

    [Fact]
    public void LegacyAnswers_AreRetainedWithLocalUnknownEvidenceAndAuthoredIdsDoNotInventDependencies()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        File.WriteAllText(questionnairePath, Regex.Replace(File.ReadAllText(questionnairePath), @"(?m)^- Assessment record:.*\r?\n", ""));
        Assert.True(FreshQuestionnaire(environment).Status(environment.Authority.Path).Complete);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        File.AppendAllText(environment.TechnicalIntentPath, "\n| TI-DEC-901 | Confirm financial settlement policy. | Change dossier creation | Accepted | Reviewed by financial owner. |\n");
        var authored = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == "TI-DEC-901");
        Assert.False(authored.QuestionnaireOverlap);
        Assert.Empty(authored.RelatedAnswers);
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        File.AppendAllText(brdPath, "\n## AI and model lifecycle\n\nBRD-FR-016: AI model evaluation requires approval.\n");
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "AI requirement changed.").ExitCode);
        var changed = FreshQuestionnaire(environment).Status(environment.Authority.Path);
        Assert.All(changed.Questions.Where(item => item.ResolutionSource == "human"), question =>
        {
            Assert.Equal("Unknown", question.AssessmentStatus);
            Assert.Equal("Answered", question.Status);
            Assert.Equal("Technical owner", question.AnsweredBy);
            Assert.NotNull(question.AnsweredAtUtc);
        });
        environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Contains(FreshQuestionnaire(environment).Status(environment.Authority.Path).Questions, item => item.AssessmentStatus == "Unknown");
    }

    private static TechnicalIntentService FreshIntent(WorkspaceEnvironment environment)
        => new(environment.Registry, environment.Resolver, new GraphSnapshotReader(environment.Resolver), environment.Brd, environment.Merger,
            () => new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void QuestionnaireAnswer_RejectsSupportingSourceRaceBeforeWrite()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        var path = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        var before = File.ReadAllBytes(path);
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        var service = new TechnicalIntentQuestionnaireService(environment.Registry, environment.Resolver, environment.Brd, environment.Merger,
            () =>
            {
                File.AppendAllText(brdPath, "\n## Runtime architecture\n\nChanged supporting architecture during the answer save.\n");
                return new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);
            });
        var result = service.Answer(environment.Authority.Path, "TI-Q-004", "Use independent bounded services.", "Architect");
        Assert.Equal("conflict", result.Status);
        Assert.False(result.Applied);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExistingDiscovery_RefreshesDraftWithRetainedReassessmentWhileOrdinaryGenerationRemainsBlocked()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var recorded = environment.Questionnaire.Status(environment.Authority.Path).Questions.Single(item => item.Id == "TI-Q-004");
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        File.AppendAllText(brdPath, "\n## Runtime architecture\n\nBRD-FR-016: The product shall use explicit module boundaries across separate repositories.\n");
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Reviewed updated architecture requirement.").ExitCode);
        var before = File.ReadAllBytes(brdPath);
        var strict = environment.Intent.Initialize(environment.Authority.Path);
        Assert.Equal("blocked", strict.Status);
        Assert.Contains(strict.Errors, error => error.Contains("TI-Q-004", StringComparison.Ordinal));
        Assert.Empty(FreshIntent(environment).PrepareExistingDraft(environment.Authority.Path).Errors);
        Assert.Equal(before, File.ReadAllBytes(brdPath));
        var retained = FreshQuestionnaire(environment).Status(environment.Authority.Path).Questions.Single(item => item.Id == recorded.Id);
        Assert.Equal("NeedsReview", retained.AssessmentStatus);
        Assert.Equal(recorded.Answer, retained.Answer);
        Assert.Equal(recorded.AnsweredBy, retained.AnsweredBy);
        Assert.Equal(recorded.AnsweredAtUtc, retained.AnsweredAtUtc);
        Assert.Equal("blocked", environment.Intent.Approve(environment.Authority.Path, "Architect", "Draft review.").Status);
    }

    private static TechnicalIntentQuestionnaireService FreshQuestionnaire(WorkspaceEnvironment environment)
        => new(environment.Registry, environment.Resolver, environment.Brd, environment.Merger,
            () => new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero));
}
