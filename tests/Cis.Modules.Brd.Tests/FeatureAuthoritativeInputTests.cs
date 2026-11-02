using System.Text;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData("remains")]
    [InlineData("must remain")]
    public void ClaudeReview_IndependentConditionalObligationDoesNotBecomePlatformDirection(string exceptionModality)
    {
        using var fixture = new Fixture(); CreateIntake(fixture);
        SelectTechnicalDocument(fixture, """
            # Accepted product choices

            Runtime: use .NET for the product.

            Except for the standalone command-line utility, which remains on Python.

            Unless stated otherwise, all timestamps must be recorded in UTC.
            """.Replace("which remains", "which " + exceptionModality, StringComparison.Ordinal));
        var platform = fixture.Service.Wizard(fixture.Authority, "referrals").Pages.Single(page => page.Id == "technical")
            .Fields.Single(field => field.Id == "technical-platform");
        Assert.True(platform.Inherited);
        Assert.Contains(".NET", platform.SuggestedAnswer);
        Assert.Contains("Python", platform.SuggestedAnswer);
        Assert.DoesNotContain("timestamps", platform.SuggestedAnswer);
        Assert.NotEmpty(platform.SourceReferences);
    }

    [Fact]
    public void AcceptedPlatformRetainsSeparateExceptionWithoutTakingUnrelatedDirection()
    {
        using var fixture = new Fixture(); CreateIntake(fixture);
        SelectTechnicalDocument(fixture, """
            # Accepted product choices

            Runtime: use .NET for the product.

            Except for the standalone command-line utility, which remains on Python.

            Operators retain observations for 30 days.

            ## Recovery
            Recovery retries use the accepted product defaults.
            """);
        var before = fixture.Snapshot();
        var platform = fixture.Service.Wizard(fixture.Authority, "referrals").Pages.Single(page => page.Id == "technical")
            .Fields.Single(field => field.Id == "technical-platform");
        Assert.True(platform.Inherited);
        Assert.False(platform.Required);
        Assert.Contains("Runtime: use .NET for the product.", platform.SuggestedAnswer);
        Assert.Contains("Except for the standalone command-line utility, which remains on Python.", platform.SuggestedAnswer);
        Assert.DoesNotContain("Operators retain", platform.SuggestedAnswer);
        Assert.DoesNotContain("Recovery retries", platform.SuggestedAnswer);
        Assert.Contains("selected.md@sha256:", Assert.Single(platform.SourceReferences));
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void AcceptedFactsOutsideEmptyHeadingsResolveFieldsWithCompleteSourceAndNoHumanRecord()
    {
        using var f = new Fixture(); CreateIntake(f);
        var longConditions = string.Join("\n", Enumerable.Range(1, 100).Select(index =>
            $"- Condition {index}: retain the original observation and its identifier when recovery retries a failed delivery."));
        var selected = SelectTechnicalDocument(f, """
            # Accepted direction
            ## Runtime
            ## Security
            ## Recovery
            ## Recorded product choices

            Runtime: use C# and .NET 10 on Windows Services.

            Security: bind APIs to loopback only; application roles are excluded in phase 1.

            Recovery: the retry delay defaults to 30 seconds, except during explicit shutdown.
            """ + "\n" + longConditions + "\n- Disaster recovery replication is deferred until phase 2.\n");
        var before = f.Snapshot();
        var page = f.Service.Wizard(f.Authority, "referrals").Pages.Single(item => item.Id == "technical");
        Assert.True(page.Complete);
        foreach (var id in new[] { "technical-platform", "technical-security", "technical-operations" })
        {
            var field = page.Fields.Single(field => field.Id == id);
            Assert.True(field.Inherited); Assert.False(field.Required); Assert.Null(field.Answer);
            Assert.Contains("docs/cis/imports/technical/selected.md@sha256:", Assert.Single(field.SourceReferences));
        }
        var recovery = page.Fields.Single(field => field.Id == "technical-operations").SuggestedAnswer;
        Assert.Contains("30 seconds, except during explicit shutdown", recovery);
        Assert.Contains("Condition 100", recovery);
        Assert.Contains("deferred until phase 2", recovery);
        Assert.DoesNotContain("Excerpt ends", recovery);
        Assert.Equal(before, f.Snapshot());
        var reopened = f.Service.Wizard(f.Authority, "referrals").Pages.Single(item => item.Id == "technical");
        Assert.Equal(recovery, reopened.Fields.Single(field => field.Id == "technical-operations").SuggestedAnswer);
        var priorReference = Assert.Single(reopened.Fields.Single(field => field.Id == "technical-operations").SourceReferences);
        File.WriteAllText(selected, File.ReadAllText(selected).Replace("30 seconds", "45 seconds", StringComparison.Ordinal));
        f.Baseline.Active = false;
        var stale = f.Service.Wizard(f.Authority, "referrals").Pages.Single(item => item.Id == "technical");
        Assert.DoesNotContain(stale.Fields, field => field.Inherited);
        Assert.False(stale.Complete);
        f.Baseline.Active = true; // The fixture represents explicit adoption of the new baseline.
        var adopted = f.Service.Wizard(f.Authority, "referrals").Pages.Single(item => item.Id == "technical");
        var adoptedRecovery = adopted.Fields.Single(field => field.Id == "technical-operations");
        Assert.Contains("45 seconds", adoptedRecovery.SuggestedAnswer);
        Assert.NotEqual(priorReference, Assert.Single(adoptedRecovery.SourceReferences));
    }

    [Fact]
    public void MissingAcceptedFactLeavesOnlyThatFieldRequiredAndExamplesCannotResolveIt()
    {
        using var f = new Fixture(); CreateIntake(f);
        SelectTechnicalDocument(f, """
            # Accepted direction

            Runtime: use .NET 10 on Windows Services.

            Security: bind APIs to loopback only. No new database or application login.

            ```markdown
            Recovery: retry for 30 seconds.
            ```
            <!-- Recovery: use a hosted backup service. -->
            """);
        var page = f.Service.Wizard(f.Authority, "referrals").Pages.Single(item => item.Id == "technical");
        Assert.True(page.Fields.Single(field => field.Id == "technical-platform").Inherited);
        Assert.True(page.Fields.Single(field => field.Id == "technical-security").Inherited);
        Assert.False(page.Fields.Single(field => field.Id == "technical-operations").Inherited);
        Assert.True(page.Fields.Single(field => field.Id == "technical-operations").Required);
        Assert.False(page.Complete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableAcceptedDirectionReportsImportProblemInsteadOfQuestions(bool oversized)
    {
        using var f = new Fixture(); CreateIntake(f);
        var path = SelectTechnicalDocument(f, "# Direction\n");
        File.WriteAllBytes(path, oversized ? new byte[2_097_153] : [0xff, 0xfe, 0xff]);
        var result = f.Service.Wizard(f.Authority, "referrals");
        Assert.Contains(result.Errors, error => error.Contains("Import problem", StringComparison.Ordinal));
        Assert.Empty(result.Pages);
    }

    [Fact]
    public void FeatureReusesAcceptedBusinessSourceWhenTechnicalDocumentDoesNotRepeatTheFact()
    {
        using var f = new Fixture(); CreateIntake(f);
        SelectTechnicalDocument(f, "# Technical direction\n## Operations\nRecovery retries use the accepted product defaults.\n");
        File.WriteAllText(Path.Combine(f.Authority, "docs/cis/specs/business-requirements.md"),
            "# Accepted business direction\n\nRuntime: use .NET 10 on Windows Services.\n\nSecurity: APIs must bind to loopback only; no application login is required.\n");
        var page = f.Service.Wizard(f.Authority, "referrals").Pages.Single(item => item.Id == "technical");
        var platform = page.Fields.Single(field => field.Id == "technical-platform");
        Assert.True(platform.Inherited);
        Assert.Contains(".NET 10", platform.SuggestedAnswer);
        Assert.Contains("specs/business-requirements.md@sha256:", Assert.Single(platform.SourceReferences));
        Assert.True(page.Fields.Single(field => field.Id == "technical-security").Inherited);
    }
}
