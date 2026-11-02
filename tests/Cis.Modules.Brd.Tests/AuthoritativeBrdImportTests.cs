using System.Text.Json;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Theory]
    [InlineData("- The operator must retain observations for 30 days unless a legal hold applies.\n- Optional exports may be added in phase 2.")]
    [InlineData("1. The operator must retain observations for 30 days unless a legal hold applies.\n2. Optional exports may be added in phase 2.")]
    [InlineData("The operator must retain observations for 30 days unless a legal hold applies.\n\nOptional exports may be added in phase 2.")]
    public void AuthoritativeImport_ConsumesPlainRequirementsWithStableIdentitiesAndOriginalQualifications(string requirements)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        var source = SelectedImportSource(environment, "# 6 Product requirements\n\n" + requirements);
        var imported = environment.Service.Reconcile(environment.Authority.Path);
        Assert.True(imported.Validation!.Valid, string.Join("; ", imported.Validation.Errors));
        Assert.Contains(source, File.ReadAllText(environment.CanonicalPath));
        Assert.Contains(Directory.GetFiles(Path.Combine(environment.Authority.Path, ".cis/local/document-import")),
            backup => File.ReadAllText(backup) == source);
        Assert.Contains("approved_by: null", File.ReadAllText(environment.CanonicalPath));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic input baseline.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var first = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(2, first.Items.Count);
        Assert.All(first.Items, item => Assert.StartsWith("BRD-IMPORT-", item.RequirementId));
        Assert.Equal("Must", first.Items[0].Priority);
        Assert.Equal("Could", first.Items[1].Priority);
        Assert.Contains("30 days unless a legal hold applies", first.Items[0].Outcome);
        Assert.Contains("phase 2", first.Items[1].Outcome);
        Assert.Empty(environment.Service.Questions(environment.Authority.Path).Questions);
        var reopened = environment.Service.Reconcile(environment.Authority.Path);
        Assert.Equal("Active", reopened.Validation!.EffectiveStatus);
        Assert.Equal(first.Items.Select(item => item.RequirementId), backlog.Build(environment.Authority.Path).Items.Select(item => item.RequirementId));

        // These are presentation changes only; source approval is still governed
        // by the existing source digest, while extracted identities stay stable.
        var document = File.ReadAllText(environment.CanonicalPath).Replace("# 6 Product requirements", "## Functional requirements");
        document = document.Replace("1. ", "- ").Replace("2. ", "- ");
        File.WriteAllText(environment.CanonicalPath, document);
        Assert.Equal("Stale", environment.Service.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt changed source version.").ExitCode);
        Assert.Equal(first.Items.Select(item => item.RequirementId), backlog.Build(environment.Authority.Path).Items.Select(item => item.RequirementId));
    }

    [Theory]
    [InlineData("REQ-01: The operator must retain observations for 30 days unless a legal hold applies.")]
    [InlineData("### REQ-01 Retention\n\nThe operator must retain observations for 30 days unless a legal hold applies.")]
    [InlineData("**REQ-01 Retention.** The operator must retain observations for 30 days unless a legal hold applies.")]
    [InlineData("| ID | Requirement | Priority | Acceptance |\n| --- | --- | --- | --- |\n| REQ-01 | The operator must retain observations for 30 days unless a legal hold applies. | Must | Verify retention and the legal-hold exception. |")]
    public void AuthoritativeImport_ExistingIdentitiesReachDownstreamReaderAcrossSupportedFormats(string requirements)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Functional requirements\n\n" + requirements);
        var reconciled = environment.Service.Reconcile(environment.Authority.Path);
        Assert.True(reconciled.Validation!.Valid, string.Join("; ", reconciled.Validation.Errors));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic input baseline.").ExitCode);
        var built = ImportedBacklog(environment).Build(environment.Authority.Path);
        Assert.Equal(0, built.ExitCode);
        Assert.Equal("REQ-01", Assert.Single(built.Items).RequirementId);
    }

    [Fact]
    public void AuthoritativeImport_EmptyCanonicalSectionDoesNotHidePopulatedEquivalentAndDuplicatesRemainDiagnostics()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Functional requirements\n\n## Product requirements\n\nREQ-01: Retain the complete source.\n\n## User requirements\n\nREQ-02: Never infer missing observations.");
        var reconciled = environment.Service.Reconcile(environment.Authority.Path);
        Assert.True(reconciled.Validation!.Valid, string.Join("; ", reconciled.Validation.Errors));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic input baseline.").ExitCode);
        var built = ImportedBacklog(environment).Build(environment.Authority.Path);
        Assert.Equal(new[] { "REQ-01", "REQ-02" }, built.Items.Select(item => item.RequirementId));
        File.WriteAllText(environment.CanonicalPath, File.ReadAllText(environment.CanonicalPath).Replace("REQ-02", "REQ-01"));
        Assert.Contains(environment.Service.Validate(environment.Authority.Path).Validation!.Errors, error => error.Contains("identity occurs more than once"));
        Assert.NotEqual(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Cannot adopt conflicting identity.").ExitCode);
    }

    [Fact]
    public void AuthoritativeImport_ProseOnlyGroupIsRetainedWithoutPromotingIdentifiedGroupIntroduction()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            ### Capture
            This introduction must not extend the identified capture requirement.
            **REQ-01 Capture.** Preserve the original observation identifiers.

            ### Replay
            The operator should retain replay results for 30 days unless deletion is requested.

            ### Future option
            - Optional exports in phase 2.
            """);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic input baseline.").ExitCode);
        var built = ImportedBacklog(environment).Build(environment.Authority.Path);
        Assert.Equal(3, built.Items.Count);
        Assert.Equal("REQ-01", built.Items[0].RequirementId);
        Assert.StartsWith("BRD-IMPORT-", built.Items[1].RequirementId);
        Assert.Equal("Should", built.Items[1].Priority);
        Assert.Contains("30 days unless deletion is requested", built.Items[1].Outcome);
        Assert.Equal("Could", built.Items[2].Priority);
        Assert.Equal("Optional exports in phase 2.", built.Items[2].Outcome);
        Assert.DoesNotContain(built.Items, item => item.Outcome.Contains("introduction"));
    }

    [Fact]
    public void AuthoritativeImport_ProhibitionsExceptionsAndNestedTablesRemainInTheRequirement()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            ### REQ-01 Preservation
            The operator must not discard observations unless the owner requests deletion.
            - Retention defaults to 30 days.
            - No new database is permitted.

            | Constraint | Value |
            | --- | --- |
            | LIMIT-01 | 500 readings |

            ## Functional requirements

            <!-- REQ-FAKE-01: A comment is not direction. -->
            ```markdown
            REQ-FAKE-02: A fenced example is not direction.
            ```
            """);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic input baseline.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var built = backlog.Build(environment.Authority.Path);
        Assert.Equal("REQ-01", Assert.Single(built.Items).RequirementId);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic backlog.").ExitCode);
        var feature = backlog.StartFeature(environment.Authority.Path, "HLT-REQ-01");
        Assert.Equal(0, feature.ExitCode);
        var text = File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!));
        Assert.Contains("must not discard observations unless the owner requests deletion", text);
        Assert.Contains("30 days", text);
        Assert.Contains("No new database is permitted", text);
        Assert.Contains("500 readings", text);
        Assert.DoesNotContain("REQ-FAKE", text);
    }

    [Theory]
    [InlineData("```markdown\nREQ-01: Hidden by an unfinished example.", "Markdown fence")]
    [InlineData("<!-- REQ-01: Hidden by an unfinished comment.", "HTML comment")]
    public void AuthoritativeImport_IncompleteExtractionIsAnImportDiagnostic(string requirements, string diagnostic)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        var source = SelectedImportSource(environment, "## Requirements\n\n" + requirements);
        var result = environment.Service.Reconcile(environment.Authority.Path);
        Assert.Contains(result.Errors.Concat(result.Validation?.Errors ?? []), error => error.Contains("BRD import problem") && error.Contains(diagnostic));
        Assert.Contains(source, File.ReadAllText(environment.CanonicalPath));
        Assert.NotEqual(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Cannot adopt incomplete interpretation.").ExitCode);
    }

    [Theory]
    [InlineData("### REQ-01 Empty requirement", "has no requirement text")]
    [InlineData("| ID | Requirement |\n| --- | --- |\n| not_an_identity | The operator must retain observations. |", "could not read requirement identity")]
    public void AuthoritativeImport_UnreadableRequirementDefinitionIsDiagnosticAndCannotBeApproved(string requirements, string diagnostic)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        var source = SelectedImportSource(environment, "## Requirements\n\n" + requirements);
        var result = environment.Service.Reconcile(environment.Authority.Path);
        Assert.Contains(result.Errors.Concat(result.Validation?.Errors ?? []), error => error.Contains(diagnostic));
        Assert.Contains(source, File.ReadAllText(environment.CanonicalPath));
        Assert.NotEqual(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Cannot adopt an unreadable requirement.").ExitCode);
        Assert.Contains(source, File.ReadAllText(environment.CanonicalPath));
    }

    [Fact]
    public void AuthoritativeImport_NestedAnonymousRequirementQualifiersStayWithTheirParentFact()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            - The operator must retain observations.
              - Retention defaults to 30 days unless a legal hold applies.
              - No new database is permitted.
            """);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic input baseline.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var built = backlog.Build(environment.Authority.Path);
        var requirement = Assert.Single(built.Items);
        Assert.Equal("Must", requirement.Priority);
        Assert.Contains("30 days unless a legal hold applies", requirement.Outcome);
        Assert.Contains("No new database is permitted", requirement.Outcome);
        var document = File.ReadAllText(environment.CanonicalPath).Replace("  - Retention", "  * Retention").Replace("  - No new", "  * No new");
        File.WriteAllText(environment.CanonicalPath, document);
        Assert.Equal("Stale", environment.Service.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt presentation-changed source version.").ExitCode);
        var reformatted = Assert.Single(backlog.Build(environment.Authority.Path).Items);
        Assert.Equal(requirement.RequirementId, reformatted.RequirementId);
        Assert.Contains("30 days unless a legal hold applies", reformatted.Outcome);
        Assert.Contains("No new database is permitted", reformatted.Outcome);
    }

    [Theory]
    [InlineData(false, "unreadable text")]
    [InlineData(true, "source limit")]
    public void AuthoritativeImport_UnreadableSelectedSourceReportsImportProblemWithoutQuestionsOrSourceMutation(bool oversized, string diagnostic)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\nREQ-01: Preserve observations.");
        var bytes = oversized ? Enumerable.Repeat((byte)'x', 20_971_521).ToArray() : new byte[] { 0xff, 0x00, 0xff };
        File.WriteAllBytes(environment.CanonicalPath, bytes);
        var reconciled = environment.Service.Reconcile(environment.Authority.Path);
        Assert.Contains(reconciled.Errors.Concat(reconciled.Validation?.Errors ?? []), error => error.Contains(diagnostic));
        var questions = environment.Service.Questions(environment.Authority.Path);
        Assert.Equal("import-problem", questions.Status);
        Assert.Contains(questions.Errors, error => error.Contains(diagnostic));
        Assert.Empty(questions.Questions);
        Assert.NotEqual(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Cannot adopt an unreadable source.").ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(environment.CanonicalPath));
    }

    private static BrdBacklogService ImportedBacklog(WorkspaceEnvironment environment)
    {
        environment.Authority.Write("docs/design/ui-direction.md", "---\ncis:\n  visual_ui: false\n---\nNo visual interface.\n");
        return new(environment.Service, environment.Registry, new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()]);
    }

    [Fact]
    public void AuthoritativeImport_QuestionAliasIsAnsweredAtItsActualSourceAndReopenedWithoutDuplicates()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\nREQ-01: Preserve complete observations.");
        var document = File.ReadAllText(environment.CanonicalPath).Replace("## Open questions\nNone.", """
            ## Open questions

            ## Outstanding questions

            | ID | Question | Answer | Answered by | Answered at UTC |
            | --- | --- | --- | --- | --- |
            | BRD-Q-001 | Who owns operator support? | - | - | - |
            """);
        File.WriteAllText(environment.CanonicalPath, document);
        environment.Service.Reconcile(environment.Authority.Path);
        Assert.Single(environment.Service.Questions(environment.Authority.Path).Questions);
        var answered = environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-001", "The operations owner handles operator support.", "Fixture owner");
        Assert.Equal(0, answered.ExitCode);
        var reopened = environment.Service.Questions(environment.Authority.Path);
        Assert.Equal(0, reopened.ExitCode);
        Assert.Equal("Answered", Assert.Single(reopened.Questions).Status);
        Assert.Equal("The operations owner handles operator support.", reopened.Questions[0].Answer);
        Assert.DoesNotContain("identity occurs more than once", string.Join("; ", reopened.Errors));
        File.AppendAllText(environment.CanonicalPath, "\n## Open questions\n\nBRD-Q-002: Who owns commercial support?\n");
        var ambiguous = environment.Service.Questions(environment.Authority.Path);
        Assert.Contains(ambiguous.Errors, error => error.Contains("more than one populated open-question section"));
        Assert.Empty(ambiguous.Questions);
    }

    private static string SelectedImportSource(WorkspaceEnvironment environment, string requirements)
    {
        var source = """
            # Synthetic observation product
            ## Executive summary
            Preserve operator observations.
            ## Business outcomes
            Operators can inspect retained observations.
            ## Scope
            Capture and replay observations. External ingestion is excluded; export is deferred to phase 2.
            ## Stakeholders and actors
            The operator owns capture requests.
            ## Business capabilities and processes
            Capture, inspect, and replay observations.
            """ + "\n" + requirements + "\n" + """
            ## Quality, regulatory, and operational requirements
            No fabricated observations are permitted.
            ## Constraints and assumptions
            No new database is permitted.
            ## Success measures
            Operators can inspect original evidence.
            ## Traceability
            The supplied source defines the current input baseline.
            ## Open questions
            None.
            """;
        environment.Authority.Write("docs/specs/business-requirements.md", source);
        environment.Authority.Write(".cis/product-documents.json", JsonSerializer.Serialize(new Dictionary<string, string>
        { ["business"] = "docs/specs/business-requirements.md" }));
        return source;
    }
}
