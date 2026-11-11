using Xunit;

namespace Cis.Modules.UiDirection.Tests;

public sealed partial class UiDirectionWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedCislibNoUiPassages_ResolveApplicabilityWithOrWithoutTechnicalQuestionnaire(bool persisted)
    {
        using var fixture = Fixture.Create();
        var technical = WriteAcceptedNoUiSource(fixture);
        var questionnaire = persisted ? WriteAcceptedNoUiQuestionnaire(fixture) : null;
        var technicalBefore = File.ReadAllText(technical);
        var questionnaireBefore = questionnaire is null ? null : File.ReadAllText(questionnaire);
        File.Delete(fixture.GuidelinesPath);

        var result = fixture.Questionnaire.Initialize(fixture.Root);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Current);
        Assert.True(result.Complete);
        Assert.False(result.UiRequired);
        Assert.Equal(0, result.UnansweredCount);
        var scope = Assert.Single(result.Questions, question => question.Id == "UI-Q-001");
        Assert.Equal("Derived", scope.Status);
        Assert.Equal("repository-evidence", scope.ResolutionSource);
        Assert.Equal("No user-facing UI", scope.Answer);
        Assert.NotEqual("Andrew Spiteri", scope.AnsweredBy);
        Assert.Null(scope.AnsweredAtUtc);
        Assert.Contains(scope.Evidence, evidence => evidence.Contains("docs/specs/technical-intent-spec.md:", StringComparison.Ordinal)
            && evidence.Contains("Frontend technology", StringComparison.Ordinal) && evidence.Contains("sha256:", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        Assert.Contains("visual_ui: false", File.ReadAllText(fixture.DirectionPath), StringComparison.Ordinal);
        Assert.Equal(technicalBefore, File.ReadAllText(technical));
        if (questionnaire is not null) Assert.Equal(questionnaireBefore, File.ReadAllText(questionnaire));
    }

    [Fact]
    public void AcceptedNoUiReader_DoesNotInvalidateExistingHumanApprovalOrRelabelItsAnswer()
    {
        using var fixture = Fixture.Create();
        fixture.Questionnaire.Initialize(fixture.Root);
        fixture.Questionnaire.Answer(fixture.Root, "UI-Q-001", "No user-facing UI", "Andrew Spiteri");
        fixture.Service.Initialize(fixture.Root);
        Assert.Equal("Active", fixture.Service.Approve(fixture.Root, "Andrew Spiteri", "The accepted product has no visual UI.").Validation!.EffectiveStatus);
        var approved = File.ReadAllText(fixture.DirectionPath);
        var questionsPath = Path.Combine(fixture.Root, "docs/specs/ui-direction-questionnaire.md");
        var questionsBefore = File.ReadAllText(questionsPath);
        // These source facts were already covered by the fixture's current solution
        // authority. Discovering their wording must not invent a new baseline version.
        WriteAcceptedNoUiSource(fixture);
        WriteAcceptedNoUiQuestionnaire(fixture);

        var status = fixture.Questionnaire.Status(fixture.Root);
        var refreshed = fixture.Questionnaire.Initialize(fixture.Root);

        Assert.True(status.Current);
        Assert.False(refreshed.Applied);
        var scope = Assert.Single(refreshed.Questions, question => question.Id == "UI-Q-001");
        Assert.Equal("Answered", scope.Status);
        Assert.Equal("human", scope.ResolutionSource);
        Assert.Equal("Andrew Spiteri", scope.AnsweredBy);
        Assert.Equal(questionsBefore, File.ReadAllText(questionsPath));
        Assert.True(fixture.Service.Evaluate(fixture.Root).Ready);
        Assert.False(fixture.Service.Initialize(fixture.Root).Applied);
        Assert.Equal(approved, File.ReadAllText(fixture.DirectionPath));
    }

    [Theory]
    [InlineData("Answered", "Customer web using React.")]
    [InlineData("Derived", "No frontend except an admin screen.")]
    public void AcceptedNoUiSource_ConflictingRecordedVisualChoicesStillBlock(string status, string answer)
    {
        using var fixture = Fixture.Create();
        WriteAcceptedNoUiSource(fixture);
        File.WriteAllText(Path.Combine(fixture.Root, "docs/specs/technical-intent-questionnaire.md"),
            $"### TI-Q-002 — Frontend technology\n- Status: {status}\n\n#### Recorded answer\n\n{answer}\n");

        var result = fixture.Questionnaire.Initialize(fixture.Root);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("conflicts", StringComparison.Ordinal));
        Assert.NotEqual(0, fixture.Service.Initialize(fixture.Root).ExitCode);
    }

    [Fact]
    public void AcceptedVisualSource_ConflictingRecordedNoUiStillBlocks()
    {
        using var fixture = Fixture.Create();
        var path = WriteAcceptedNoUiSource(fixture);
        File.WriteAllText(path, File.ReadAllText(path).Replace(NoUiFrontendPassage, "Customer web using React.", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(fixture.Root, "docs/specs/technical-intent-questionnaire.md"),
            "### TI-Q-002 — Frontend technology\n- Status: Answered\n\n#### Recorded answer\n\nNo user-facing UI\n");

        var result = fixture.Questionnaire.Initialize(fixture.Root);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("conflicts", StringComparison.Ordinal));
        Assert.NotEqual(0, fixture.Service.Initialize(fixture.Root).ExitCode);
    }

    [Theory]
    [InlineData("Draft", "No user-facing UI\n\nThe product has no frontend application or browser/mobile/desktop presentation technology.")]
    [InlineData("Active", "No user-facing UI\n\nAlso provide an admin screen using React.")]
    public void UnacceptedOrConflictingCanonicalNoUi_DoesNotEraseMissingVisualChoices(string status, string answer)
    {
        using var fixture = Fixture.Create();
        var path = WriteAcceptedNoUiSource(fixture);
        File.WriteAllText(path, File.ReadAllText(path).Replace("status: Active", "status: " + status, StringComparison.Ordinal)
            .Replace(NoUiFrontendPassage, answer, StringComparison.Ordinal));

        var result = fixture.Questionnaire.Initialize(fixture.Root);

        Assert.True(result.UiRequired);
        Assert.False(result.Complete);
        Assert.NotEqual(0, fixture.Service.Initialize(fixture.Root).ExitCode);
    }

    // Public CISLIB source wording, with synthetic repository identity and approval
    // fixtures. The solution-source stub supplies approval/currentness validation.
    private const string NoUiProductSurfaces = "The primary surface is the .NET in-process library API. Supporting machine-facing surfaces are host-integrated Healthcheck HTTP reporting, local pipe protocol and matching non-core probe executable. Type Manager supplies build-time inventory generation and runtime catalog/query APIs. Console/file logging are output capabilities used by a host, not standalone application frontends.";
    private const string NoUiFrontendExplanation = "The product has no frontend application or browser/mobile/desktop presentation technology. HTTP routes are health-reporting contracts inside an application host. The line above records the settled source direction. Accepted-document questionnaire derivation requires the actual approved technical baseline; the canonical no-visual-UI applicability record remains separate and governed by supported questionnaire/adoption controls.";
    private const string NoUiFrontendPassage = "No user-facing UI\n\n" + NoUiFrontendExplanation;
    private const string NoUiBusinessScope = "The product has no visual frontend. Machine-facing Healthcheck HTTP and local-pipe reporting, the matching probe executable, consumer examples and IDE/native developer workflows do not create a web or desktop application product. [S02, S06, S07]";

    private static string WriteAcceptedNoUiSource(Fixture fixture)
    {
        var path = Path.Combine(fixture.Root, "docs/specs/technical-intent-spec.md");
        File.WriteAllText(path, "---\ntitle: Product technical intent\ntype: specification\nstatus: Active\ncis:\n"
            + "  stable_id: product:spec:technical-intent\n  approved_by: \"Andrew Spiteri\"\n"
            + "  approved_at: \"2026-09-03T10:00:00Z\"\n  approval_reason: \"Accepted product direction\"\n"
            + "  approved_content_hash: \"sha256:fixture\"\n---\n\n# Product technical intent\n\n"
            + "## Technical surface and ownership\n\n### Product surfaces\n\n" + NoUiProductSurfaces
            + "\n\n### Frontend technology\n\n" + NoUiFrontendPassage + "\n\n### Backend technology and topology\n\nExisting .NET library.\n");
        return path;
    }

    private static string WriteAcceptedNoUiQuestionnaire(Fixture fixture)
    {
        var path = Path.Combine(fixture.Root, "docs/specs/technical-intent-questionnaire.md");
        File.WriteAllText(path, "### TI-Q-002 — Frontend technology\n- Status: Derived\n- Answered by: Not recorded\n"
            + "- Answered at: Not recorded\n- Resolution source: accepted-document\n- Confidence: source-explicit\n"
            + "- Evidence: docs/specs/technical-intent-spec.md:18 — Frontend technology (sha256:fixture)\n\n"
            + "#### Recorded answer\n\n" + NoUiProductSurfaces + "\n\n" + NoUiFrontendExplanation + "\n\n" + NoUiBusinessScope + "\n");
        return path;
    }
}
