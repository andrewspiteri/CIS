using System.Text.Json;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void AuthoritativeImport_RemainingParser_WrappedExceptionRetainsIdentityAndGeneratedAcceptance()
    {
        const string expected = "The service must delete observations after 30 days. Records subject to a legal hold are exempt from this deletion rule.";
        var identities = new List<string>();
        foreach (var qualification in new[] { "Records subject to a legal hold are exempt from this deletion rule.", "Records subject to a legal hold\nare exempt from this deletion rule." })
        {
            using var environment = WorkspaceEnvironment.Create(1);
            SelectedImportSource(environment, "## Requirements\n\nThe service must delete observations after 30 days.\n\n" + qualification);
            Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
            Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic wrapped retention policy.").ExitCode);
            var backlog = ImportedBacklog(environment);
            var item = Assert.Single(backlog.Build(environment.Authority.Path).Items);
            identities.Add(item.RequirementId);
            Assert.Equal(expected, item.Outcome);
            Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic backlog.").ExitCode);
            var feature = backlog.StartFeature(environment.Authority.Path, item.Id);
            Assert.Equal(0, feature.ExitCode);
            Assert.Contains("Satisfy the source acceptance intent: " + expected,
                File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!)));
        }
        Assert.Single(identities.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void AuthoritativeImport_RemainingParser_WrappedIndependentObligationKeepsItsCondition()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            The service must delete observations after 30 days.

            Except for archived observations,
            the service should display timestamps in UTC.
            """);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic independent wrapped requirement.").ExitCode);
        var items = ImportedBacklog(environment).Build(environment.Authority.Path).Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("The service must delete observations after 30 days.", items[0].Outcome);
        Assert.Equal("Except for archived observations, the service should display timestamps in UTC.", items[1].Outcome);
        Assert.Equal("Should", items[1].Priority);
    }

    [Theory]
    [InlineData("REQ-01: Optional exports are deferred until phase 2;\nwhen provided, they must omit sensitive data.", "Could", "Optional exports are deferred until phase 2; when provided, they must omit sensitive data.")]
    [InlineData("**REQ-01 Exports.** Optional exports are deferred until phase 2;\nwhen provided, they must omit sensitive data.", "Could", "Exports")]
    [InlineData("REQ-01: Uploads are queued locally; when connectivity returns,\nthe app should sync them.", "Should", "Uploads are queued locally; when connectivity returns, the app should sync them.")]
    public void AuthoritativeImport_RemainingParser_TitleAndClausePunctuationPreserveScope(string requirement, string priority, string outcome)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\n" + requirement);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic delivery scope.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var item = Assert.Single(backlog.Build(environment.Authority.Path).Items);
        Assert.Equal("REQ-01", item.RequirementId);
        Assert.Equal(priority, item.Priority);
        Assert.Equal(outcome, item.Outcome);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic backlog.").ExitCode);
        var feature = backlog.StartFeature(environment.Authority.Path, item.Id);
        Assert.Equal(0, feature.ExitCode);
        var generated = File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!));
        Assert.Contains(priority == "Could"
            ? "Optional exports are deferred until phase 2; when provided, they must omit sensitive data."
            : "Uploads are queued locally; when connectivity returns, the app should sync them.", generated);
    }

    [Theory]
    [InlineData("REQ-01: If the upload fails, the app should retry three times.", "Should")]
    [InlineData("REQ-01: When the network is unavailable, the app may queue uploads.", "Could")]
    [InlineData("**BR-007 Retry when offline**: The app should retry uploads.", "Should")]
    [InlineData("REQ-02: Uploads are queued locally. When connectivity returns, the app should sync them.", "Should")]
    [InlineData("REQ-02: Uploads are queued locally. If connectivity returns, the app may sync them.", "Could")]
    [InlineData("REQ-02: Uploads are queued locally:\n  - When connectivity returns, the app should sync them.", "Should")]
    [InlineData("- Uploads are queued locally:\n  - If connectivity returns, the app may sync them.", "Could")]
    public void AuthoritativeImport_ClaudeReview_LeadingConditionAndTitlePreserveStatedPriority(string requirement, string priority)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\n" + requirement);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic retry priority.").ExitCode);
        var backlog = ImportedBacklog(environment);
        Assert.Equal(priority, Assert.Single(backlog.Build(environment.Authority.Path).Items).Priority);
        Assert.Equal(priority, Assert.Single(backlog.Build(environment.Authority.Path).Items).Priority);
    }

    [Theory]
    [InlineData("REQ-02: Uploads are queued locally.\n  - When connectivity returns, the app should sync them.", "Should")]
    [InlineData("- Uploads are queued locally.\n  - If connectivity returns, the app may sync them.", "Could")]
    public void AuthoritativeImport_ClaudeReview_ListsAfterSentenceKeepStatedPriority(string requirement, string priority)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\n" + requirement);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic structured retry priority.").ExitCode);
        var backlog = ImportedBacklog(environment);
        Assert.Equal(priority, Assert.Single(backlog.Build(environment.Authority.Path).Items).Priority);
        Assert.Equal(priority, Assert.Single(backlog.Build(environment.Authority.Path).Items).Priority);
    }

    [Theory]
    [InlineData("Unless stated otherwise, all timestamps must be recorded in UTC.")]
    [InlineData("Except for archived observations, the service should display timestamps in UTC.")]
    public void AuthoritativeImport_ClaudeReview_ConditionalIndependentObligationRemainsSeparate(string independent)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            The service must delete observations after 30 days.

            Records subject to a legal hold are exempt from this deletion rule.

            """ + "\n" + independent);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic independent requirements.").ExitCode);
        var items = ImportedBacklog(environment).Build(environment.Authority.Path).Items;
        Assert.Equal(2, items.Count);
        Assert.Contains("legal hold", items[0].Outcome);
        Assert.DoesNotContain("timestamps", items[0].Outcome);
        Assert.Equal(independent, items[1].Outcome);
    }

    [Fact]
    public void AuthoritativeImport_SeparateExceptionReachesGeneratedFeatureAcceptance()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            The service must delete observations after 30 days.

            Records subject to a legal hold are exempt from this deletion rule.
            """);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic retention policy.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var item = Assert.Single(backlog.Build(environment.Authority.Path).Items);
        const string requirement = "The service must delete observations after 30 days. Records subject to a legal hold are exempt from this deletion rule.";
        Assert.Equal(requirement, item.Outcome);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic backlog.").ExitCode);
        var feature = backlog.StartFeature(environment.Authority.Path, item.Id);
        Assert.Equal(0, feature.ExitCode);
        var generated = File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!));
        Assert.Contains("Satisfy the source acceptance intent: " + requirement, generated);
    }

    [Fact]
    public void AuthoritativeImport_QualificationDoesNotJoinIndependentObligationsOrIdentifiedSections()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, """
            ## Requirements

            ### Retention
            The service must delete observations after 30 days.

            Records subject to a legal hold are exempt from this deletion rule.

            The service must preserve operator identifiers.

            ### REQ-02 Reporting
            The service should display the observation count.
            """);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic requirements.").ExitCode);
        var items = ImportedBacklog(environment).Build(environment.Authority.Path).Items;
        Assert.Equal(3, items.Count);
        Assert.Contains("legal hold", items[0].Outcome);
        Assert.DoesNotContain("operator identifiers", items[0].Outcome);
        Assert.Equal("The service must preserve operator identifiers.", items[1].Outcome);
        Assert.Equal("REQ-02", items[2].RequirementId);
        Assert.DoesNotContain("legal hold", items[2].Outcome);
    }

    [Theory]
    [InlineData("Optional exports are deferred until phase 2; when provided, they must omit sensitive data.", "Could")]
    [InlineData("The service must provide exports in phase 2; they must omit sensitive data.", "Must")]
    [InlineData("The service must export observations with an optional correlation field.", "Must")]
    public void AuthoritativeImport_DeliveryScopeIsNotPromotedByConditionalConstraints(string requirement, string priority)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\nREQ-01: " + requirement);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic export scope.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var built = backlog.Build(environment.Authority.Path);
        var item = Assert.Single(built.Items);
        Assert.Equal(priority, item.Priority);
        Assert.Equal(requirement, item.Outcome);
        if (priority != "Could") return;

        var backlogPath = Path.Combine(environment.Authority.Path, built.RelativePath!);
        File.WriteAllText(backlogPath, File.ReadAllText(backlogPath).Replace("| Could |", "| Should |", StringComparison.Ordinal));
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Review the export delivery priority.").ExitCode);
        var reopened = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, reopened.ExitCode);
        Assert.Equal("Should", Assert.Single(reopened.Items).Priority);
        Assert.Equal("Active", reopened.Validation!.EffectiveStatus);
        var feature = backlog.StartFeature(environment.Authority.Path, item.Id);
        Assert.Equal(0, feature.ExitCode);
        Assert.Contains("Satisfy the source acceptance intent: " + requirement,
            File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!)));
    }

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

    [Fact]
    public void AuthoritativeImport_RemainingParser_RequiredFieldsTitleKeepsBodyPriority()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\n### REQ-01 Required fields validation\n\nThe form should validate required fields.");
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic field validation.").ExitCode);
        var item = Assert.Single(ImportedBacklog(environment).Build(environment.Authority.Path).Items);
        Assert.Equal("REQ-01", item.RequirementId);
        Assert.Equal("Required fields validation", item.Outcome);
        Assert.Equal("Should", item.Priority);
    }

    [Fact]
    public void AuthoritativeImport_RemainingParser_SubstantiveTitleConflictIsNotHiddenByOptionalBody()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\n**REQ-01 Mandatory exports.** Optional exports are deferred until phase 2; when provided, they must omit sensitive data.");
        var result = environment.Service.Reconcile(environment.Authority.Path);
        Assert.False(result.Validation!.Valid);
        Assert.Contains(result.Validation.Errors, error => error.Contains("REQ-01", StringComparison.Ordinal)
            && error.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthoritativeImport_RemainingParser_TableOptionalScopeIsNotAConflictingTitle()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        const string requirement = "Optional exports must omit sensitive data.";
        SelectedImportSource(environment, "## Requirements\n\n| ID | Requirement |\n| --- | --- |\n| REQ-01 | " + requirement + " |");
        var reconciled = environment.Service.Reconcile(environment.Authority.Path);
        Assert.True(reconciled.Validation!.Valid, string.Join("; ", reconciled.Validation.Errors));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic optional table requirement.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var item = Assert.Single(backlog.Build(environment.Authority.Path).Items);
        Assert.Equal("REQ-01", item.RequirementId);
        Assert.Equal("Could", item.Priority);
        Assert.Equal(requirement, item.Outcome);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic backlog.").ExitCode);
        var feature = backlog.StartFeature(environment.Authority.Path, item.Id);
        Assert.Equal(0, feature.ExitCode);
        Assert.Contains(requirement,
            File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!)));
    }

    [Fact]
    public void AuthoritativeImport_DeferredTitleRetainsConditionalDeliveryPriority()
    {
        using var environment = WorkspaceEnvironment.Create(1);
        const string requirement = "When phase 2 starts, the service should provide exports.";
        SelectedImportSource(environment, "## Requirements\n\n**REQ-01 Deferred exports.** " + requirement);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Contains(requirement, File.ReadAllText(environment.CanonicalPath));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic conditional export delivery.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var built = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, built.ExitCode);
        var item = Assert.Single(built.Items);
        Assert.Equal("Should", item.Priority);
        Assert.Equal("REQ-01", item.RequirementId);
        Assert.Equal("Deferred exports", item.Outcome);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic backlog.").ExitCode);
        var feature = backlog.StartFeature(environment.Authority.Path, item.Id);
        Assert.Equal(0, feature.ExitCode);
        Assert.Contains("Satisfy the source acceptance intent: " + requirement,
            File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!)));
        var rebuilt = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, rebuilt.ExitCode);
        var reopened = Assert.Single(rebuilt.Items);
        Assert.Equal(item.RequirementId, reopened.RequirementId);
        Assert.Equal(item.Outcome, reopened.Outcome);
        Assert.Equal("Should", reopened.Priority);
        Assert.Contains(requirement, File.ReadAllText(environment.CanonicalPath));
    }

    [Theory]
    [InlineData("When provided, they must omit sensitive data.")]
    [InlineData("When exports are provided, they must omit sensitive data.")]
    public void AuthoritativeImport_RemainingParser_DeferredTitleConstraintCannotReplaceReviewedPriority(string requirement)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        SelectedImportSource(environment, "## Requirements\n\n**REQ-01 Deferred exports.** " + requirement);
        Assert.True(environment.Service.Reconcile(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Adopt synthetic deferred delivery scope.").ExitCode);
        var backlog = ImportedBacklog(environment);
        var built = backlog.Build(environment.Authority.Path);
        var backlogPath = Path.Combine(environment.Authority.Path, built.RelativePath!);
        File.WriteAllText(backlogPath, File.ReadAllText(backlogPath).Replace("| Must |", "| Should |", StringComparison.Ordinal));
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Review deferred export priority.").ExitCode);
        var reopened = backlog.Build(environment.Authority.Path);
        Assert.Equal("Should", Assert.Single(reopened.Items).Priority);
        Assert.Equal("Active", reopened.Validation!.EffectiveStatus);
        var feature = backlog.StartFeature(environment.Authority.Path, Assert.Single(reopened.Items).Id);
        Assert.Equal(0, feature.ExitCode);
        Assert.Contains(requirement, File.ReadAllText(Path.Combine(environment.Authority.Path, feature.RelativePath!)));
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
