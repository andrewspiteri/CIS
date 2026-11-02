using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    private const string SuppliedTechnicalDirection = """
# Supplied product direction

#### Business baseline
Preserve the Active BRD scope and business acceptance conditions without adding scope.

##### Design principles
Keep deterministic policy and explicit ownership; preserve all exclusions and later-phase boundaries.

###### Technology and ownership
| Subject | Accepted direction |
| --- | --- |
| Product surfaces | The product uses a local CLI only. |
| Frontend | No frontend or browser UI is required in the current phase. |
| Backend | Use C# and .NET with ASP.NET Core for the application framework. |
| Architecture | Use a modular monolith with explicit module boundaries. |
| Ownership | Use a single repository; the product team owns the release package. |
| Persistence | The primary data store is PostgreSQL. |
| Supporting stores | No cache, search index, message broker, or object storage is required. |
| Contracts | Use versioned HTTP/JSON APIs; old clients must remain compatible for 90 days. |
| Access | No application authentication is required; preserve the trusted-local identity boundary. |
| Hosting | Run on Windows hosts as supervised Windows Services. |
| Work | No background jobs, queues, or scheduled work are required for this phase. |
| Operations | Use structured privacy-safe logs, metrics, truthful readiness, and recovery runbooks. |
| Security | Secrets must not enter logs; retain sensitive configuration only on the local host. |
| Assurance | Use unit, integration, acceptance, and recovery tests. |
| Models | AI and learned models are explicitly excluded from this phase. |
| Constraints | The default request timeout is 45 seconds, except health checks use 2 seconds. A browser UI is deferred to phase 2 and must not block the current release. |

#### Runtime architecture
Preserve the trusted-local process boundary and modular monolith ownership.

#### Business invariants
Writes must commit atomically to the selected system of record, preserving business conditions.

#### Service contract
Use versioned HTTP/JSON contracts and retain compatibility obligations.

#### Trust and security
Use the trusted-local boundary; do not send secrets to telemetry.

#### Shutdown, upgrades and recovery
Retain structured logs and provide a verified recovery runbook before changing stored state.

#### Acceptance and verification
Use native acceptance and recovery tests with explicit evidence.

#### Technical decisions and constraints
No cloud services are required. Preserve the 45 seconds timeout default and its 2 seconds health-check exception.

#### Decision closure
No open technical decisions remain. All technical choices are closed for the current phase.
""";

    private static string SelectTechnicalDirection(WorkspaceEnvironment environment, string content)
    {
        const string relative = "docs/imports/technical/source.md";
        var path = Path.Combine(environment.Authority.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["technical"] = relative }));
        return path;
    }

    private static void AdoptTechnicalDirection(WorkspaceEnvironment environment)
    {
        var imported = environment.Intent.Initialize(environment.Authority.Path);
        Assert.Equal(0, imported.ExitCode);
        Assert.True(imported.Validation!.Valid, string.Join("\n", imported.Validation.Errors));
        Assert.Equal("Ready for Approval", imported.Validation.EffectiveStatus);
        var adopted = environment.Intent.Approve(environment.Authority.Path, "Technical owner", "Accept the supplied current-phase baseline.");
        Assert.Equal(0, adopted.ExitCode);
        Assert.Equal("Active", adopted.Validation!.EffectiveStatus);
    }

    [Fact]
    public void ImportedAuthority_AdoptsOnceThenResolvesSourceFactsWithoutManualAnswers()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var path = SelectTechnicalDirection(environment, SuppliedTechnicalDirection);
        Assert.False(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        AdoptTechnicalDirection(environment);

        var questions = environment.Questionnaire.Initialize(environment.Authority.Path);

        Assert.Equal(0, questions.ExitCode);
        Assert.True(questions.Complete, string.Join("\n", questions.Warnings));
        Assert.All(questions.Questions, question =>
        {
            Assert.Equal("Derived", question.Status);
            Assert.Equal("accepted-document", question.ResolutionSource);
            Assert.Null(question.AnsweredBy);
            Assert.Null(question.AnsweredAtUtc);
            Assert.NotEmpty(question.Evidence!);
            Assert.Contains(question.Evidence!, source => source.StartsWith("docs/imports/technical/source.md:", StringComparison.Ordinal));
        });
        Assert.Contains("No frontend", questions.Questions.Single(question => question.Id == "TI-Q-002").Answer, StringComparison.Ordinal);
        Assert.Contains("90 days", questions.Questions.Single(question => question.Id == "TI-Q-008").Answer, StringComparison.Ordinal);
        var constraints = questions.Questions.Single(question => question.Id == "TI-Q-016").Answer!;
        Assert.Contains("45 seconds", constraints, StringComparison.Ordinal);
        Assert.Contains("except health checks use 2 seconds", constraints, StringComparison.Ordinal);
        Assert.Contains("deferred to phase 2", constraints, StringComparison.Ordinal);
        Assert.Contains(SuppliedTechnicalDirection, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains(Directory.GetFiles(Path.Combine(environment.Authority.Path, ".cis/local/document-import")),
            backup => File.ReadAllText(backup) == SuppliedTechnicalDirection);
        var first = File.ReadAllText(Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md"));
        Assert.Equal("unchanged", environment.Questionnaire.Initialize(environment.Authority.Path).Status);
        Assert.Equal(first, File.ReadAllText(Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md")));
        Assert.True(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        Assert.Equal("Active", environment.Intent.Initialize(environment.Authority.Path).Validation!.EffectiveStatus);
    }

    [Fact]
    public void ImportedAuthority_EmptyCanonicalSectionUsesExplicitDirectionElsewhere()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n## Security and privacy intent\n\n");
        AdoptTechnicalDirection(environment);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal("Derived", result.Questions.Single(question => question.Id == "TI-Q-013").Status);
        Assert.Contains("Secrets must not enter logs", result.Questions.Single(question => question.Id == "TI-Q-013").Answer, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedAuthority_MaterialChangeInvalidatesDerivedAnswersUntilBaselineReadoption()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var path = SelectTechnicalDirection(environment, SuppliedTechnicalDirection);
        AdoptTechnicalDirection(environment);
        Assert.True(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        File.WriteAllText(path, File.ReadAllText(path).Replace("primary data store is PostgreSQL", "primary data store is SQLite", StringComparison.Ordinal));

        var stale = environment.Questionnaire.Status(environment.Authority.Path);

        Assert.False(stale.Complete);
        var data = stale.Questions.Single(question => question.Id == "TI-Q-006");
        Assert.Equal("Unanswered", data.Status);
        Assert.Null(data.Answer);
        Assert.Contains("SQLite", data.SuggestedAnswer, StringComparison.Ordinal);
        Assert.Equal("Stale", environment.Intent.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        AdoptTechnicalDirection(environment);
        var refreshed = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.True(refreshed.Complete);
        Assert.Contains("SQLite", refreshed.Questions.Single(question => question.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgreSQL", refreshed.Questions.Single(question => question.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedAuthority_CompactPrimaryStoreRowsExposeBothConflictingStatements()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var content = SuppliedTechnicalDirection.Replace("| Persistence | The primary data store is PostgreSQL. |",
            "| Persistence | PostgreSQL |\n| Persistence | SQLite |", StringComparison.Ordinal);
        SelectTechnicalDirection(environment, content);

        var result = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Contains(result.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal)
            && error.Contains("| Persistence | PostgreSQL |", StringComparison.Ordinal)
            && error.Contains("| Persistence | SQLite |", StringComparison.Ordinal)
            && Regex.Matches(error, @"source\.md:(\d+)").Select(match => match.Value).Distinct(StringComparer.Ordinal).Count() == 2);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("TI-Q-008", "Clients may connect over HTTP or gRPC.")]
    [InlineData("TI-Q-003", "The backend may be .NET or Java.")]
    public void ImportedAuthority_ClaudeReviewTentativeAlternativesRemainUnresolved(string id, string direction)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n#### Candidate direction\n\n" + direction + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == id);

        Assert.Equal("Unanswered", question.Status);
        Assert.Contains(direction, question.SuggestedAnswer, StringComparison.Ordinal);
        Assert.False(result.Complete);
    }

    [Theory]
    [InlineData("| Persistence | PostgreSQL 16 |")]
    [InlineData("| Primary database | PostgreSQL | Testing team |")]
    public void ImportedAuthority_ClaudeReviewExtendedStoreRowsDeriveAndParticipateInConflicts(string row)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var path = SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace(
            "| Persistence | The primary data store is PostgreSQL. |", row, StringComparison.Ordinal));
        AdoptTechnicalDirection(environment);

        var questions = environment.Questionnaire.Initialize(environment.Authority.Path);
        var store = questions.Questions.Single(item => item.Id == "TI-Q-006");
        Assert.Equal("Derived", store.Status);
        Assert.Contains(row, store.Answer, StringComparison.Ordinal);
        Assert.True(questions.Complete);
        Assert.True(environment.Intent.Evaluate(environment.Authority.Path).Ready);

        File.WriteAllText(path, File.ReadAllText(path) + "\n\n#### Current persistence\n\n| Persistence | SQLite 3 | Data team |\n");
        var conflicting = environment.Intent.Initialize(environment.Authority.Path);
        Assert.NotEqual(0, conflicting.ExitCode);
        Assert.Contains(conflicting.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal)
            && error.Contains(row, StringComparison.Ordinal) && error.Contains("| Persistence | SQLite 3 | Data team |", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportedAuthority_ClaudeReviewAlternativeStoreRowCannotHideBehindSettledDirection()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string alternatives = "| Persistence | PostgreSQL or SQLite |";
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n#### Current persistence choice\n\n" + alternatives + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-006");

        Assert.Equal("Unanswered", question.Status);
        Assert.Contains(alternatives, question.SuggestedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal));
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);
    }

    [Theory]
    [InlineData("Persistence", "Deferred to phase 2 migration")]
    [InlineData("Database", "Local cache only")]
    [InlineData("Database", "Tests only")]
    public void ImportedAuthority_ClaudeReviewExtendedStoreRowsRetainQualifyingNotes(string subject, string note)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var row = $"| {subject} | SQLite | {note} |";
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n#### Store qualifications\n\n" + row + "\n");

        var imported = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal(0, imported.ExitCode);
        Assert.DoesNotContain(imported.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal));
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Technical owner", "Preserve the current store and the qualified supporting detail.").ExitCode);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-006");
        Assert.Equal("Derived", question.Status);
        Assert.Contains("PostgreSQL", question.Answer, StringComparison.Ordinal);
        Assert.Contains(row, question.Answer, StringComparison.Ordinal);
        Assert.True(result.Complete);
        Assert.True(environment.Intent.Evaluate(environment.Authority.Path).Ready);
    }

    [Fact]
    public void ImportedAuthority_ClaudeReviewQualifiedStoreNotesDoNotChooseTheCurrentPrimaryStore()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string notes = """
            | Persistence | SQLite | Deferred to phase 2 migration |
            | Database | SQLite | Local cache only |
            | Database | SQLite | Tests only |
            """;
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace(
            "| Persistence | The primary data store is PostgreSQL. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Store qualifications\n\n" + notes + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-006");

        Assert.Equal("Unanswered", question.Status);
        Assert.Null(question.Answer);
        Assert.All(notes.Split('\n'), row => Assert.Contains(row.Trim(), question.SuggestedAnswer, StringComparison.Ordinal));
        Assert.False(result.Complete);
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);
    }

    [Fact]
    public void ImportedAuthority_ClaudeReviewStoreCellPreservesEachSentenceScope()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string row = "| Persistence | Use PostgreSQL in production. Use SQLite for tests. |";
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace(
            "| Persistence | The primary data store is PostgreSQL. |", row, StringComparison.Ordinal));

        var imported = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal(0, imported.ExitCode);
        Assert.DoesNotContain(imported.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal));
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Technical owner", "Preserve the different production and testing stores.").ExitCode);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal("Derived", result.Questions.Single(item => item.Id == "TI-Q-006").Status);
        Assert.Contains(row, result.Questions.Single(item => item.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
        Assert.True(result.Complete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImportedAuthority_ClaudeReviewGeneralServicesDoNotCreateDatastoreScope(bool businessHeading)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false,
            businessCapabilities: businessHeading ? "Support follow-up consultations.\n\n## Additional services\n\nCustomers can book a follow-up consultation." : null);
        var content = SuppliedTechnicalDirection.Replace(
            "| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal);
        if (!businessHeading) content += "\n\n#### Supporting services\n\nThe auxiliary application service sends appointment reminders.\n";
        SelectTechnicalDirection(environment, content);
        AdoptTechnicalDirection(environment);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        File.Delete(questionnairePath);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Not requested", result.Questions.Single(item => item.Id == "TI-Q-007").Status);
        Assert.True(result.Complete);
        Assert.True(File.Exists(questionnairePath));
    }

    [Theory]
    [InlineData("It is rebuilt nightly from the system of record.")]
    [InlineData("- Owned by the data team.")]
    public void ImportedAuthority_ClaudeReviewSupportingCategoryRetainsCompatibleContext(string detail)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace(
            "| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Supporting data services\n\nA graph database is required.\n\n" + detail + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-007");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Derived", question.Status);
        Assert.Contains("A graph database is required.", question.Answer, StringComparison.Ordinal);
        Assert.Contains(detail, question.Answer, StringComparison.Ordinal);
        Assert.Equal(2, question.Evidence!.Count(evidence => evidence.Contains("Supporting data services", StringComparison.Ordinal)));
        Assert.True(result.Complete);
    }

    [Fact]
    public void ImportedAuthority_ClaudeReviewRecognizedCategoryStillNeedsExplicitDirection()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string tentative = "We might need a graph database.";
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace(
            "| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Supporting data services\n\n" + tentative + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-007");

        Assert.Equal("Unanswered", question.Status);
        Assert.Null(question.Answer);
        Assert.Contains(tentative, question.SuggestedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Errors, error => error.Contains("Input import problem", StringComparison.Ordinal));
        Assert.False(result.Complete);
    }

    [Fact]
    public void ImportedAuthority_ClaudeReviewSeparateCategoryOccurrenceCannotHideUninterpretableDirection()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var content = SuppliedTechnicalDirection.Replace(
            "| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Supporting data services\n\nA graph database is required.\n\n#### Supporting data services\n\nSee the attached table for the selected service.\n";
        SelectTechnicalDirection(environment, content);
        using var scope = CisDefinitionDraftScope.Enter();

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);

        Assert.Equal("Import problem", result.Questions.Single(item => item.Id == "TI-Q-007").Status);
        Assert.Contains(result.Errors, error => error.Contains("See the attached table", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportedAuthority_CompactStoreSourceConflictsWithRecordedHumanAnswer()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var path = SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace(
            "| Persistence | The primary data store is PostgreSQL. |", "| Persistence | PostgreSQL |", StringComparison.Ordinal));
        AdoptTechnicalDirection(environment);
        Assert.True(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        var source = File.ReadAllBytes(path);

        var result = environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-006", "SQLite", "Fixture owner");

        var question = result.Questions.Single(item => item.Id == "TI-Q-006");
        Assert.Equal("Conflict", question.Status);
        Assert.Equal("SQLite", question.Answer);
        Assert.Equal("Fixture owner", question.AnsweredBy);
        Assert.NotNull(question.AnsweredAtUtc);
        Assert.Contains(result.Errors, error => error.Contains("| Persistence | PostgreSQL |", StringComparison.Ordinal)
            && error.Contains("technical-intent-questionnaire.md:", StringComparison.Ordinal)
            && error.Contains("Fixture owner", StringComparison.Ordinal));
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Fact]
    public void ImportedAuthority_CategoryHeadingRecognizesSupportingServiceWithoutInventingVendorChoice()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection
            .Replace("| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Supporting data services\n\nA graph database is required.\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-007");

        Assert.Equal("Derived", question.Status);
        Assert.Contains("A graph database is required.", question.Answer, StringComparison.Ordinal);
        Assert.Contains(question.Evidence!, evidence => evidence.Contains("Supporting data services", StringComparison.Ordinal));
        Assert.True(result.Complete);
    }

    [Fact]
    public void ImportedAuthority_UninterpretableSupportingCategoryRetainsFocusedDiagnostic()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var content = SuppliedTechnicalDirection
            .Replace("| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Supporting data services\n\nSee the attached table for the selected service.\n";
        var path = SelectTechnicalDirection(environment, content);
        using var scope = CisDefinitionDraftScope.Enter();

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-007");

        Assert.Equal("Import problem", question.Status);
        Assert.Contains("See the attached table", question.SuggestedAnswer, StringComparison.Ordinal);
        Assert.Contains(result.Errors, error => error.Contains("TI-Q-007", StringComparison.Ordinal)
            && error.Contains("Supporting data services", StringComparison.Ordinal));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void ImportedAuthority_OptionalPermissionRetainsSettledTransportAndItsQualification()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection
            + "\n\n#### Correlation behavior\n\nClients may send an optional correlation ID over HTTP.\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-008");

        Assert.Equal("Derived", question.Status);
        Assert.Contains("Use versioned HTTP/JSON APIs", question.Answer, StringComparison.Ordinal);
        Assert.Contains("Clients may send an optional correlation ID over HTTP.", question.Answer, StringComparison.Ordinal);
        Assert.True(result.Complete);
    }

    [Theory]
    [InlineData("We may use HTTP or gRPC; the transport is undecided.")]
    [InlineData("We may use HTTP or gRPC.")]
    [InlineData("HTTP could be used.")]
    public void ImportedAuthority_TransportAlternativesRemainUnresolvedAlongsideSettledDirection(string unresolved)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection
            + "\n\n#### Transport choice\n\n" + unresolved + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var question = result.Questions.Single(item => item.Id == "TI-Q-008");

        Assert.Equal("Unanswered", question.Status);
        Assert.Contains(unresolved, question.SuggestedAnswer, StringComparison.Ordinal);
        Assert.False(result.Complete);
    }

    [Theory]
    [InlineData("Use SQLite for a local cache.")]
    [InlineData("| Persistence | `SQLite 3` as a cache | Data team |")]
    public void ImportedAuthority_CompactPrimaryStoreAllowsExplicitSupportingRole(string supporting)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection
            .Replace("| Persistence | The primary data store is PostgreSQL. |", "| Persistence | PostgreSQL |", StringComparison.Ordinal)
            .Replace("| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Supporting store\n\n" + supporting + "\n");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);

        Assert.True(result.Complete, string.Join("\n", result.Errors));
        Assert.Equal("Derived", result.Questions.Single(item => item.Id == "TI-Q-006").Status);
        Assert.Contains(supporting, result.Questions.Single(item => item.Id == "TI-Q-007").Answer, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportedAuthority_AbsentNecessaryStoreRemainsTheOnlyQuestion()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection
            .Replace("| Persistence | The primary data store is PostgreSQL. |\n", "", StringComparison.Ordinal));
        AdoptTechnicalDirection(environment);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal(["TI-Q-006"], result.Questions.Where(question => question.Status == "Unanswered").Select(question => question.Id));
    }

    [Fact]
    public void ImportedAuthority_ConflictingPrimaryStoresRetainBothSourcesButCompatibleDetailDoesNotConflict()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n#### Persistence clarification\n\nThe primary data store is SQLite.\n\nUse a PostgreSQL adapter for migration tests only.\n");
        var result = environment.Intent.Initialize(environment.Authority.Path);
        Assert.Contains(result.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal)
            && error.Contains("PostgreSQL", StringComparison.Ordinal) && error.Contains("SQLite", StringComparison.Ordinal));
        Assert.NotEqual(0, environment.Intent.Approve(environment.Authority.Path, "Technical owner", "Contradictory source must not be adopted.").ExitCode);
        Assert.DoesNotContain(result.Errors, error => error.Contains("adapter", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("# Broken\n\n```text\nUnreadable direction remains in an unclosed block.")]
    [InlineData("# Broken\n\n<!-- Never closed\nUse PostgreSQL.")]
    public void ImportedAuthority_IncompleteExtractionReportsImportProblemWithoutClaimingBusinessAbsence(string content)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var path = SelectTechnicalDirection(environment, content);
        var result = environment.Questionnaire.Status(environment.Authority.Path);
        Assert.Contains(result.Errors, error => error.StartsWith("Input import problem:", StringComparison.Ordinal));
        Assert.Equal(content, File.ReadAllText(path));
        Assert.DoesNotContain(result.Questions, question => question.ResolutionSource == "accepted-document");
    }

    [Fact]
    public void ImportedAuthority_DoesNotDiscardLongOrShortExplicitPassages()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var longCondition = "The selected contracts must preserve " + string.Join(" ", Enumerable.Repeat("all caller-specific conditions,", 170)) + " including the 90 days compatibility obligation.";
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n#### Detailed conditions\n\n" + longCondition + "\n\n#### Backend choice\n\nUse .NET.\n");
        AdoptTechnicalDirection(environment);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Contains(longCondition, result.Questions.Single(question => question.Id == "TI-Q-008").Answer, StringComparison.Ordinal);
        Assert.Contains("Use .NET.", result.Questions.Single(question => question.Id == "TI-Q-003").Answer, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedAuthority_PreservesVisibleProseAroundCommentsAndDoesNotReadFencedExamples()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + """

#### Additional backend direction
<!-- historical example --> Use .NET for the application framework. <!-- Use Java and Spring Boot. --> Keep the local backend.

````text
~~~
The primary data store is SQLite.
```
Use Java and Spring Boot for the backend.
````
""");
        AdoptTechnicalDirection(environment);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        var backend = result.Questions.Single(question => question.Id == "TI-Q-003").Answer!;
        Assert.Contains("Use .NET for the application framework.", backend, StringComparison.Ordinal);
        Assert.Contains("Keep the local backend.", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("Spring Boot", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLite", result.Questions.Single(question => question.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedAuthority_SameProductionScopeContradictionBlocksAdoption()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection + "\n\n#### Store constraints\n\n- The primary data store is PostgreSQL in production.\n- The primary data store is SQLite in production.\n- A Redis cache is deferred to phase 2.\n");
        var result = environment.Intent.Initialize(environment.Authority.Path);
        Assert.Contains(result.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal)
            && error.Contains("PostgreSQL in production", StringComparison.Ordinal) && error.Contains("SQLite in production", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportedAuthority_UnrequestedOptionalCapabilitiesDoNotCreateQuestionsOrAcceptedChoices()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var content = SuppliedTechnicalDirection
            .Replace("| Supporting stores | No cache, search index, message broker, or object storage is required. |\n", "", StringComparison.Ordinal)
            .Replace("| Work | No background jobs, queues, or scheduled work are required for this phase. |\n", "", StringComparison.Ordinal)
            .Replace("| Models | AI and learned models are explicitly excluded from this phase. |\n", "", StringComparison.Ordinal)
            + "\n\n#### Operator readiness\n\nOperators must complete recovery training.\n";
        var path = SelectTechnicalDirection(environment, content);
        var draft = environment.Questionnaire.Status(environment.Authority.Path);
        Assert.DoesNotContain(draft.Questions, question => question.Status == "Not requested");
        AdoptTechnicalDirection(environment);

        var result = environment.Questionnaire.Initialize(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Complete);
        Assert.Equal(0, result.UnansweredCount);
        foreach (var id in new[] { "TI-Q-007", "TI-Q-011", "TI-Q-015" })
        {
            var question = result.Questions.Single(item => item.Id == id);
            Assert.Equal("Not requested", question.Status);
            Assert.Equal("scope-not-requested", question.ResolutionSource);
            Assert.Null(question.Answer);
            Assert.Null(question.AnsweredBy);
            Assert.Null(question.AnsweredAtUtc);
            Assert.NotEmpty(question.Evidence!);
        }
        Assert.True(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        Assert.Equal("unchanged", environment.Questionnaire.Initialize(environment.Authority.Path).Status);
        Assert.Contains(content, File.ReadAllText(path), StringComparison.Ordinal);

        File.AppendAllText(path, "\n\n#### Current model requirement\n\nThe current scope must use AI to suggest alternatives.\n");
        var stale = environment.Questionnaire.Status(environment.Authority.Path);
        Assert.Equal("Unanswered", stale.Questions.Single(question => question.Id == "TI-Q-015").Status);
        Assert.False(stale.Complete);
        AdoptTechnicalDirection(environment);
        var required = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal(["TI-Q-015"], required.Questions.Where(question => question.Status == "Unanswered").Select(question => question.Id));
        var ai = required.Questions.Single(question => question.Id == "TI-Q-015");
        Assert.Null(ai.Answer);
        Assert.Contains("current scope must use AI", ai.SuggestedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("N/A", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedAuthority_UnsupportedSelectionReportsImportProblemAndRetainsInputs()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string relative = "docs/imports/technical/unsupported.txt";
        var path = Path.Combine(environment.Authority.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = Encoding.UTF8.GetBytes("Supplied direction must remain available when its file type is unsupported.");
        File.WriteAllBytes(path, source);
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        var questionsBefore = File.ReadAllBytes(questionnairePath);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["technical"] = relative }));

        var result = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal("input-problem", result.Status);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.StartsWith("Input import problem:", StringComparison.Ordinal)
            && error.Contains("Markdown", StringComparison.Ordinal));
        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.Equal(questionsBefore, File.ReadAllBytes(questionnairePath));
        Assert.False(Directory.Exists(Path.Combine(environment.Authority.Path, ".cis/local/document-import")));
    }

    [Fact]
    public void DefinitionDraft_SelectedSourceFactsResolveWorkingFieldsWithoutAdoptingTheBaseline()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var content = SuppliedTechnicalDirection.Replace("| Models | AI and learned models are explicitly excluded from this phase. |\n", "", StringComparison.Ordinal);
        var path = SelectTechnicalDirection(environment, content);
        using (CisDefinitionDraftScope.Enter())
        {
            var imported = environment.Intent.Initialize(environment.Authority.Path);
            Assert.Equal(0, imported.ExitCode);
            Assert.Equal("Ready for Approval", imported.Validation!.EffectiveStatus);
            var result = environment.Questionnaire.Initialize(environment.Authority.Path);
            Assert.True(result.Complete, string.Join("\n", result.Errors.Concat(result.Warnings)));
            var data = result.Questions.Single(question => question.Id == "TI-Q-006");
            Assert.Equal("Derived", data.Status);
            Assert.Equal("selected-document", data.ResolutionSource);
            Assert.Equal("source-explicit-proposal", data.Confidence);
            Assert.Null(data.AnsweredBy);
            Assert.Null(data.AnsweredAtUtc);
            Assert.Contains("PostgreSQL", data.Answer, StringComparison.Ordinal);
            var ai = result.Questions.Single(question => question.Id == "TI-Q-015");
            Assert.Equal("Not requested", ai.Status);
            Assert.Equal("selected-scope-not-requested", ai.ResolutionSource);
            Assert.Null(ai.Answer);
            Assert.True(environment.Questionnaire.Status(environment.Authority.Path).Complete);
            File.WriteAllText(path, File.ReadAllText(path).Replace("primary data store is PostgreSQL", "primary data store is SQLite", StringComparison.Ordinal));
            var changed = environment.Questionnaire.Initialize(environment.Authority.Path);
            Assert.Contains("SQLite", changed.Questions.Single(question => question.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
            Assert.DoesNotContain("PostgreSQL", changed.Questions.Single(question => question.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
        }
        var outside = environment.Questionnaire.Status(environment.Authority.Path);
        Assert.False(outside.Complete);
        Assert.Equal("Unanswered", outside.Questions.Single(question => question.Id == "TI-Q-006").Status);
        Assert.Null(outside.Questions.Single(question => question.Id == "TI-Q-006").Answer);
        Assert.Equal("Unanswered", outside.Questions.Single(question => question.Id == "TI-Q-015").Status);
        Assert.Contains("status: Draft", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("approved_by: null", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("approved_at: \"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitionDraft_SelectedBrdFactsRemainWorkingProposalsBeforeFinalAdoption()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        SelectTechnicalDirection(environment, SuppliedTechnicalDirection.Replace("| Backend | Use C# and .NET with ASP.NET Core for the application framework. |\n", "", StringComparison.Ordinal));
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        var brd = File.ReadAllText(brdPath);
        brd = CisFrontMatter.Set(brd, "status", "Review Required");
        foreach (var field in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            brd = CisFrontMatter.Set(brd, field, "null", nested: true);
        brd = brd.Replace("## Functional requirements", "## Technical constraint\n\nThe backend must use C# and .NET with ASP.NET Core.\n\n## Functional requirements", StringComparison.Ordinal);
        File.WriteAllText(brdPath, brd);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["business"] = "docs/specs/business-requirements.md", ["technical"] = "docs/imports/technical/source.md" }));

        using (CisDefinitionDraftScope.Enter())
        {
            var initialized = environment.Intent.Initialize(environment.Authority.Path);
            Assert.Equal(0, initialized.ExitCode);
            var result = environment.Questionnaire.Initialize(environment.Authority.Path);
            var backend = result.Questions.Single(question => question.Id == "TI-Q-003");
            Assert.Equal("Derived", backend.Status);
            Assert.Equal("selected-document", backend.ResolutionSource);
            Assert.Contains("The backend must use C# and .NET", backend.Answer, StringComparison.Ordinal);
            Assert.Contains(backend.Evidence!, source => source.StartsWith("docs/specs/business-requirements.md:", StringComparison.Ordinal));
            Assert.Null(backend.AnsweredBy);
        }
        Assert.False(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        Assert.Contains("approved_by: null", File.ReadAllText(brdPath), StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitionDraft_ReadsExplicitDirectionAfterFormerTwoMegabyteExcerptLimit()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        const string trailing = "Use .NET for the fallback backend; preserve its 37 seconds timeout.";
        var content = SuppliedTechnicalDirection + "\n" + new string(' ', 2 * 1024 * 1024)
            + "\n\n#### Fallback runtime condition\n\n" + trailing + "\n";
        var path = SelectTechnicalDirection(environment, content);
        using var scope = CisDefinitionDraftScope.Enter();
        var imported = environment.Intent.Initialize(environment.Authority.Path);
        Assert.Equal(0, imported.ExitCode);
        var result = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(trailing, result.Questions.Single(question => question.Id == "TI-Q-003").Answer, StringComparison.Ordinal);
        Assert.True(new FileInfo(path).Length > 2 * 1024 * 1024);
        Assert.Contains(content, File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SQLite")]
    [InlineData("The primary data store is SQLite.")]
    public void ImportedAuthority_ManualStoreConflictPreservesAnswerUntilSourceBaselineIsReconciled(string manualAnswer)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: false, completeQuestionnaire: false);
        var path = SelectTechnicalDirection(environment, SuppliedTechnicalDirection);
        AdoptTechnicalDirection(environment);
        Assert.True(environment.Questionnaire.Initialize(environment.Authority.Path).Complete);
        var sourceBefore = File.ReadAllBytes(path);

        var recorded = environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-006", manualAnswer, "Owner");

        Assert.False(recorded.Complete);
        var conflict = recorded.Questions.Single(question => question.Id == "TI-Q-006");
        Assert.Equal("Conflict", conflict.Status);
        Assert.Equal(manualAnswer, conflict.Answer);
        Assert.Equal("Owner", conflict.AnsweredBy);
        Assert.NotNull(conflict.AnsweredAtUtc);
        Assert.Contains(recorded.Errors, error => error.Contains("PostgreSQL", StringComparison.Ordinal)
            && error.Contains("SQLite", StringComparison.Ordinal)
            && error.Contains("docs/imports/technical/source.md:", StringComparison.Ordinal)
            && error.Contains("docs/specs/technical-intent-questionnaire.md:", StringComparison.Ordinal)
            && error.Contains("TI-Q-006", StringComparison.Ordinal) && error.Contains("Owner", StringComparison.Ordinal));
        var questionnairePath = Path.Combine(environment.Authority.Path, "docs/specs/technical-intent-questionnaire.md");
        var questionRecord = File.ReadAllBytes(questionnairePath);
        Assert.Contains("\nstatus: Draft\n", File.ReadAllText(questionnairePath), StringComparison.Ordinal);
        var catalogPath = environment.Resolver.Resolve(environment.Authority.Path).Context!.CatalogPath;
        var entryPattern = $@"(?ms)^[ \t]*- id: {Regex.Escape(recorded.AuthorityRepositoryId + ":spec:technical-intent-questionnaire")}[ \t]*\r?$\n(?<entry>.*?)(?=^[ \t]*- id:|\z)";
        Assert.Contains("status: draft", Regex.Match(File.ReadAllText(catalogPath), entryPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["entry"].Value, StringComparison.Ordinal);
        Assert.Contains("#### Recorded answer\n\n" + manualAnswer, File.ReadAllText(questionnairePath), StringComparison.Ordinal);
        Assert.Equal("Conflict", environment.Questionnaire.Status(environment.Authority.Path).Questions.Single(question => question.Id == "TI-Q-006").Status);
        Assert.NotEqual(0, environment.Questionnaire.Initialize(environment.Authority.Path).ExitCode);
        Assert.Equal(questionRecord, File.ReadAllBytes(questionnairePath));
        Assert.Equal(sourceBefore, File.ReadAllBytes(path));
        var blocked = environment.Intent.Status(environment.Authority.Path);
        Assert.NotEqual("Active", blocked.Validation?.EffectiveStatus);
        Assert.Contains(blocked.Errors, error => error.Contains("Authoritative input conflict", StringComparison.Ordinal));
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);
        Assert.NotEqual(0, environment.Intent.Approve(environment.Authority.Path, "Technical owner", "A conflict cannot amend the selected source.").ExitCode);
        Assert.Equal(sourceBefore, File.ReadAllBytes(path));

        var corrected = environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-006", "PostgreSQL", "Owner");
        Assert.Equal(0, corrected.ExitCode);
        Assert.True(corrected.Complete);
        Assert.Equal("Answered", corrected.Questions.Single(question => question.Id == "TI-Q-006").Status);
        Assert.Contains("\nstatus: Complete\n", File.ReadAllText(questionnairePath), StringComparison.Ordinal);
        Assert.Contains("status: active", Regex.Match(File.ReadAllText(catalogPath), entryPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["entry"].Value, StringComparison.Ordinal);
        Assert.Equal("Active", environment.Intent.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        var proposedAgain = environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-006", manualAnswer, "Owner");
        Assert.Equal("Conflict", proposedAgain.Questions.Single(question => question.Id == "TI-Q-006").Status);

        File.WriteAllText(path, File.ReadAllText(path).Replace("primary data store is PostgreSQL", "primary data store is SQLite", StringComparison.Ordinal));
        AdoptTechnicalDirection(environment);
        var reconciled = environment.Questionnaire.Initialize(environment.Authority.Path);
        Assert.True(reconciled.Complete, string.Join("\n", reconciled.Errors.Concat(reconciled.Warnings)));
        var retained = reconciled.Questions.Single(question => question.Id == "TI-Q-006");
        Assert.Equal("Answered", retained.Status);
        Assert.Equal("human", retained.ResolutionSource);
        Assert.Equal(manualAnswer, retained.Answer);
        Assert.Equal("Owner", retained.AnsweredBy);
        Assert.Equal(conflict.AnsweredAtUtc, retained.AnsweredAtUtc);
        Assert.Equal("Active", environment.Intent.Status(environment.Authority.Path).Validation!.EffectiveStatus);
    }
}
