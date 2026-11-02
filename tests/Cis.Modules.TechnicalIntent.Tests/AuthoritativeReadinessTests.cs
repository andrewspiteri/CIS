using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedAuthority_AdoptionPreservesFactsButNecessaryChoiceBlocksReadinessUntilAnswered(bool persistedQuestionnaire)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection
            .Replace("| Persistence | The primary data store is PostgreSQL. |\n", "", StringComparison.Ordinal));
        AdoptTechnicalDirection(environment);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        if (!persistedQuestionnaire) File.Delete(questionnairePath);
        // Source adoption is an authority action. It does not settle the missing choice.
        Assert.Equal("Active", environment.Intent.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        var readiness = environment.Intent.Evaluate(environment.Authority.Path);
        Assert.False(readiness.Ready);
        Assert.Contains(readiness.Errors, error => error.Contains("TI-Q-006", StringComparison.Ordinal));
        Assert.DoesNotContain(readiness.Errors, error => error.Contains("TI-Q-007", StringComparison.Ordinal));
        Assert.Equal(persistedQuestionnaire, File.Exists(questionnairePath));
        var questions = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal(["TI-Q-006"], questions.Questions.Where(question => question.Status == "Unanswered").Select(question => question.Id));
        Assert.All(questions.Questions.Where(question => question.Id != "TI-Q-006"), question =>
        {
            Assert.Equal("accepted-document", question.ResolutionSource);
            Assert.NotEmpty(question.Evidence!);
        });

        const string answer = "PostgreSQL";
        var answered = environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-006", answer, "Fixture data owner");
        Assert.Equal(0, answered.ExitCode);
        Assert.True(answered.Complete);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var reopened = environment.Questionnaire.Status(environment.Authority.Path);
        var store = reopened.Questions.Single(question => question.Id == "TI-Q-006");
        Assert.Equal(answer, store.Answer);
        Assert.Equal("human", store.ResolutionSource);
        Assert.Equal("Fixture data owner", store.AnsweredBy);
        Assert.NotNull(store.AnsweredAtUtc);
        Assert.Equal("accepted-document", reopened.Questions.Single(question => question.Id == "TI-Q-003").ResolutionSource);
        Assert.True(environment.Intent.Evaluate(environment.Authority.Path).Ready);
    }

    [Fact]
    public void ImportedAuthority_ReadinessReusesCompleteSourceWithoutCreatingAQuestionnaire()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection);
        AdoptTechnicalDirection(environment);
        var path = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        File.Delete(path);
        Assert.True(environment.Intent.Evaluate(environment.Authority.Path).Ready);
        Assert.False(File.Exists(path));
    }
}
