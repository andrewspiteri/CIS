using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    // Verbatim passages from the accepted public CISLIB technical intent and BRD.
    private const string AcceptedLibraryStoreAndModelDirection = "No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.";
    private const string AcceptedLibraryFrontendDirection = """
No user-facing UI

The product has no frontend application or browser/mobile/desktop presentation technology. HTTP routes are health-reporting contracts inside an application host. The line above records the settled source direction. Accepted-document questionnaire derivation requires the actual approved technical baseline; the canonical no-visual-UI applicability record remains separate and governed by supported questionnaire/adoption controls.
""";
    private const string AcceptedLibraryBusinessSurface = "The product has no visual frontend. Machine-facing Healthcheck HTTP and local-pipe reporting, the matching probe executable, consumer examples and IDE/native developer workflows do not create a web or desktop application product. [S02, S06, S07]";
    private const string AcceptedLibraryHealthDirection = "Healthcheck retains explicit monitor criticality, policy transitions, counts/revisions, safe collection ownership and immutable detached snapshots. Snapshot capture performs no collection or failure replay. Observed exception/runtime text is projected through the existing bounded diagnostic rules; explicit caller inputs retain their own validation contract. A watchdog may affect a caller's wait without gaining health authority or forced cancellation.";
    private const string AcceptedLibraryOperationsDirection = "Application hosts own runtime startup/shutdown, deployment, external resources, cancellation requests and recovery policy. Preserve explicit Logging flush/stop outcomes, file recovery, Type Manager immutable preparation reports and Healthcheck truthful detached facts. Retired generated receipts, campaigns and old recovery owners supply neither operational authority nor new capacity.";

    private static string AcceptedLibraryFactDirection => SuppliedTechnicalDirection
        .Replace("| Frontend | No frontend or browser UI is required in the current phase. |\n", "", StringComparison.Ordinal)
        .Replace("| Persistence | The primary data store is PostgreSQL. |\n", "", StringComparison.Ordinal)
        .Replace("| Models | AI and learned models are explicitly excluded from this phase. |\n", "", StringComparison.Ordinal)
        .Replace("| Operations | Use structured privacy-safe logs, metrics, truthful readiness, and recovery runbooks. |\n", "", StringComparison.Ordinal)
        + "\n\n## Frontend technology\n\n" + AcceptedLibraryFrontendDirection
        + "\n\n## Application model and invariants\n\n" + AcceptedLibraryHealthDirection
        + "\n\n" + AcceptedLibraryStoreAndModelDirection
        + "\n\n## Operations, migration, and recovery intent\n\n" + AcceptedLibraryOperationsDirection + "\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedAuthority_ActualAcceptedLibraryFactsResolveWithoutInventingHumanAnswers(bool persistedQuestionnaire)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var brd = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        File.AppendAllText(brd, "\n\n## Product surfaces\n\n" + AcceptedLibraryBusinessSurface + "\n");
        Assert.Equal(0, environment.Brd.Approve(environment.Authority.Path, "Product owner", "Retain the supplied library surface.").ExitCode);
        var source = SelectTechnicalDirection(environment, AcceptedLibraryFactDirection);
        AdoptTechnicalDirection(environment);
        if (persistedQuestionnaire) Assert.True(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        if (!persistedQuestionnaire) File.Delete(questionnairePath);
        var acceptedSource = File.ReadAllBytes(source);
        var acceptedBrd = File.ReadAllBytes(brd);

        var readiness = environment.Intent.Evaluate(environment.Authority.Path);

        Assert.True(readiness.Ready, string.Join("\n", readiness.Errors));
        Assert.Equal(persistedQuestionnaire, File.Exists(questionnairePath));
        var projected = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.True(projected.Complete, string.Join("\n", projected.Errors.Concat(projected.Warnings)));
        foreach (var id in new[] { "TI-Q-002", "TI-Q-006", "TI-Q-012", "TI-Q-015" })
        {
            var fact = projected.Questions.Single(question => question.Id == id);
            Assert.Equal("Derived", fact.Status);
            Assert.Equal("accepted-document", fact.ResolutionSource);
            Assert.Equal("Current", fact.AssessmentStatus);
            Assert.Null(fact.AnsweredBy);
            Assert.Null(fact.AnsweredAtUtc);
            Assert.NotEmpty(fact.Evidence!);
            Assert.Contains(fact.Evidence!, item => item.StartsWith("docs/imports/technical/source.md:", StringComparison.Ordinal));
            Assert.NotEmpty(fact.SourceFactAuthority!.Basis!);
        }
        Assert.Contains(AcceptedLibraryStoreAndModelDirection, projected.Questions.Single(question => question.Id == "TI-Q-015").Answer, StringComparison.Ordinal);
        var operations = projected.Questions.Single(question => question.Id == "TI-Q-012");
        Assert.Contains(AcceptedLibraryHealthDirection, operations.Answer, StringComparison.Ordinal);
        Assert.Contains(AcceptedLibraryOperationsDirection, operations.Answer, StringComparison.Ordinal);
        Assert.Contains(operations.Evidence!, item => item.Contains("Operations, migration, and recovery intent", StringComparison.Ordinal));
        Assert.Contains(projected.Questions.Single(question => question.Id == "TI-Q-002").Evidence!,
            item => item.StartsWith("docs/specs/business-requirements.md:", StringComparison.Ordinal));
        Assert.Equal(acceptedSource, File.ReadAllBytes(source));
        Assert.Equal(acceptedBrd, File.ReadAllBytes(brd));
        var saved = File.ReadAllBytes(questionnairePath);
        Assert.Equal("unchanged", environment.Questionnaire.Initialize(environment.Authority.Path).Status);
        Assert.Equal(saved, File.ReadAllBytes(questionnairePath));
    }

    [Theory]
    [InlineData(false, "Recovery may use the application owner or a separate restart controller.")]
    [InlineData(true, "Recovery may use the application owner or a separate restart controller.")]
    [InlineData(false, "A watchdog may affect a caller's wait without gaining health authority or forced cancellation, but the recovery owner is undecided.")]
    [InlineData(true, "A watchdog may affect a caller's wait without gaining health authority or forced cancellation, but the recovery owner is undecided.")]
    public void ImportedAuthority_SettledLibraryOperationsCannotHideAnUndecidedOperationalChoice(bool persistedQuestionnaire, string undecided)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var source = SelectTechnicalDirection(environment, AcceptedLibraryFactDirection
            + "\n\n## Operations, migration, and recovery intent\n\n" + undecided + "\n");
        AdoptTechnicalDirection(environment);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        if (persistedQuestionnaire) Assert.False(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        else File.Delete(questionnairePath);
        var sourceBytes = File.ReadAllBytes(source);

        var readiness = environment.Intent.Evaluate(environment.Authority.Path);

        Assert.False(readiness.Ready);
        Assert.Contains(readiness.Errors, error => error.Contains("TI-Q-012", StringComparison.Ordinal));
        Assert.Equal(persistedQuestionnaire, File.Exists(questionnairePath));
        var projected = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.False(projected.Complete);
        var operations = projected.Questions.Single(question => question.Id == "TI-Q-012");
        Assert.Equal("Unanswered", operations.Status);
        Assert.Null(operations.Answer);
        Assert.Null(operations.AnsweredBy);
        Assert.Null(operations.AnsweredAtUtc);
        Assert.Contains(AcceptedLibraryHealthDirection, operations.SuggestedAnswer, StringComparison.Ordinal);
        Assert.Contains(AcceptedLibraryOperationsDirection, operations.SuggestedAnswer, StringComparison.Ordinal);
        Assert.Contains(undecided, operations.SuggestedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(projected.Questions.Where(question => question.Id is "TI-Q-002" or "TI-Q-006" or "TI-Q-015"),
            question => question.Status == "Unanswered");
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedAuthority_ActualAcceptedExclusionDoesNotAnswerAMissingNecessaryChoice(bool persistedQuestionnaire)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, AcceptedLibraryFactDirection
            .Replace("| Backend | Use C# and .NET with ASP.NET Core for the application framework. |\n", "", StringComparison.Ordinal));
        AdoptTechnicalDirection(environment);
        var path = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        if (persistedQuestionnaire) environment.Questionnaire.Initialize(environment.Authority.Path);
        else File.Delete(path);

        var readiness = environment.Intent.Evaluate(environment.Authority.Path);

        Assert.False(readiness.Ready);
        Assert.Contains(readiness.Errors, error => error.Contains("TI-Q-003", StringComparison.Ordinal));
        Assert.DoesNotContain(readiness.Errors, error => error.Contains("TI-Q-015", StringComparison.Ordinal));
        Assert.Equal(persistedQuestionnaire, File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedAuthority_ActualAcceptedExclusionAndNegatedModelUseRemainConsistent(bool persistedQuestionnaire)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var source = SelectTechnicalDirection(environment, AcceptedLibraryFactDirection
            + "\n\n## Current model use\n\nThe product does not use AI.\n");
        AdoptTechnicalDirection(environment);
        var path = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        if (persistedQuestionnaire) Assert.True(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        else File.Delete(path);
        var acceptedSource = File.ReadAllBytes(source);

        var readiness = environment.Intent.Evaluate(environment.Authority.Path);

        Assert.True(readiness.Ready, string.Join("\n", readiness.Errors));
        Assert.Equal(persistedQuestionnaire, File.Exists(path));
        var projection = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.True(projection.Complete, string.Join("\n", projection.Errors.Concat(projection.Warnings)));
        var fact = projection.Questions.Single(question => question.Id == "TI-Q-015");
        Assert.Equal("Derived", fact.Status);
        Assert.Equal("accepted-document", fact.ResolutionSource);
        Assert.Null(fact.AnsweredBy);
        Assert.Null(fact.AnsweredAtUtc);
        Assert.Contains(AcceptedLibraryStoreAndModelDirection, fact.Answer, StringComparison.Ordinal);
        Assert.Contains("The product does not use AI.", fact.Answer, StringComparison.Ordinal);
        Assert.Equal(acceptedSource, File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedAuthority_ActualAcceptedExclusionCannotHideConflictingCurrentModelDirection(bool persistedQuestionnaire)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var source = SelectTechnicalDirection(environment, AcceptedLibraryFactDirection);
        AdoptTechnicalDirection(environment);
        var path = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        if (persistedQuestionnaire) Assert.True(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        else File.Delete(path);
        File.AppendAllText(source, "\n\n## Current AI selection\n\nUse a managed model API for the current AI inference boundary.\n");
        var conflictingSource = File.ReadAllBytes(source);

        var readiness = environment.Intent.Evaluate(environment.Authority.Path);
        var projection = environment.Questionnaire.Initialize(environment.Authority.Path);

        Assert.False(readiness.Ready);
        Assert.Contains(readiness.Errors, error => error.Contains("Authoritative input conflict: AI/model scope", StringComparison.Ordinal));
        Assert.False(projection.Applied);
        Assert.False(projection.Complete);
        var fact = projection.Questions.Single(question => question.Id == "TI-Q-015");
        Assert.Equal("Conflict", fact.Status);
        Assert.Null(fact.AnsweredBy);
        Assert.Null(fact.AnsweredAtUtc);
        Assert.Contains(projection.Errors, error => error.Contains("do not become selected architecture", StringComparison.Ordinal)
            && error.Contains("managed model API", StringComparison.Ordinal)
            && error.Contains("docs/imports/technical/source.md:", StringComparison.Ordinal));
        Assert.Equal(persistedQuestionnaire, File.Exists(path));
        Assert.Equal(conflictingSource, File.ReadAllBytes(source));
    }

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
