using Xunit;

namespace Cis.Modules.UiDirection.Tests;

public sealed partial class UiDirectionWorkflowTests
{
    [Fact]
    public void RecordedNoFrontend_SkipsVisualChoicesAndReopensThemWhenScopeChanges()
    {
        using var fixture = Fixture.Create();
        var technicalQuestions = Path.Combine(fixture.Root, "docs/specs/technical-intent-questionnaire.md");
        const string source = """
            ### TI-Q-002 — Frontend technology
            - Status: Answered
            - Answered by: Product owner

            #### Recorded answer

            this project does not employ a frontend.

            ### TI-Q-003 — Backend technology
            Backend direction.
            """;
        File.WriteAllText(technicalQuestions, source);
        File.Delete(fixture.GuidelinesPath);
        var questions = fixture.Questionnaire.Initialize(fixture.Root);
        Assert.False(questions.UiRequired);
        Assert.True(questions.Complete);
        Assert.True(questions.Current);
        Assert.Equal(0, questions.UnansweredCount);
        Assert.Contains(questions.Questions, question => question.Id == "UI-Q-001" && question.Status == "Derived"
            && question.Evidence.Any(evidence => evidence.Contains("TI-Q-002")));
        Assert.Contains(questions.Questions, question => question.Id == "UI-Q-005" && question.Status == "Unanswered");
        var generated = fixture.Service.Initialize(fixture.Root);
        Assert.Equal(0, generated.ExitCode);
        Assert.Equal("Ready for Approval", generated.Validation!.EffectiveStatus);
        var document = File.ReadAllText(fixture.DirectionPath);
        Assert.Contains("visual_ui: false", document);
        Assert.DoesNotContain("## Visual language and tokens", document);
        Assert.Contains("approved_by: null", document);
        Assert.Equal(source, File.ReadAllText(technicalQuestions));
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);
        Assert.Equal("Active", fixture.Service.Approve(fixture.Root, "Owner", "No visual UI is in scope.").Validation!.EffectiveStatus);
        Assert.True(fixture.Service.Evaluate(fixture.Root).Ready);
        Assert.False(fixture.Service.Initialize(fixture.Root).Applied);

        File.WriteAllText(technicalQuestions, source.Replace("this project does not employ a frontend.", "Customer web using React."));
        Assert.False(fixture.Questionnaire.Status(fixture.Root).Current);
        Assert.False(fixture.Service.Status(fixture.Root).Validation!.Current);
        var renewed = fixture.Questionnaire.Initialize(fixture.Root);
        Assert.True(renewed.UiRequired);
        Assert.False(renewed.Complete);
        Assert.Equal(5, fixture.Service.Initialize(fixture.Root).ExitCode);
    }

    [Fact]
    public void ExplicitHumanNoUi_RequiresOnlySurfaceDecisionAndCanBeReversed()
    {
        using var fixture = Fixture.Create();
        fixture.Questionnaire.Initialize(fixture.Root);
        var noUi = fixture.Questionnaire.Answer(fixture.Root, "UI-Q-001", "No user-facing UI", "Owner");
        Assert.True(noUi.Complete);
        Assert.False(noUi.UiRequired);
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        var restored = fixture.Questionnaire.Answer(fixture.Root, "UI-Q-001", "Customer web", "Owner");
        Assert.True(restored.UiRequired);
        Assert.False(restored.Complete);
        Assert.False(fixture.Service.Status(fixture.Root).Validation!.Valid);
    }

    [Theory]
    [InlineData("Unanswered", "No frontend")]
    [InlineData("Answered", "No frontend except an admin screen.")]
    [InlineData("Answered", "CLI and APIs")]
    public void NoUi_IsNotInferredFromUnacceptedOrMixedSurfaceEvidence(string status, string answer)
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, "docs/specs/technical-intent-questionnaire.md"),
            $"### TI-Q-002 — Frontend technology\n- Status: {status}\n\n#### Recorded answer\n\n{answer}\n");
        var questions = fixture.Questionnaire.Initialize(fixture.Root);
        Assert.True(questions.UiRequired);
        Assert.False(questions.Complete);
    }
}
