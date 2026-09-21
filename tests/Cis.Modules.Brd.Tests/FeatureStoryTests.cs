namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    private const string StoryBrd = """
        # Business Requirements Document
        ## MVP Scope
        - Public catalogue browsing.
        - Tenant isolation and secure administration.
        ## Functional Requirements
        ### Product catalogue
        | ID | Requirement |
        |---|---|
        | CAT-001 | An authorised user shall be able to publish approved products. |
        | CAT-002 | Public queries shall return only eligible products. |
        ### Direct handoff
        | ID | Requirement |
        |---|---|
        | DIR-001 | The platform shall immediately redirect the customer to the Bank. |
        | DIR-002 | Personal data shall not appear in redirect URLs. |
        ## Non-functional requirements
        ### Security
        | ID | Requirement |
        |---|---|
        | SEC-001 | The platform shall isolate all tenant data. |
        | SEC-002 | Administrative access shall require authenticated users. |
        ## Explicitly Separate Products or Bounded Contexts
        ### KYC
        | ID | Requirement |
        |---|---|
        | KYC-001 | Sumsub shall verify customer identity. |
        ## Excluded from the MVP
        - A visual form builder.
        - KYC and Customer master records.
        ## Future Referral-Domain Extensions
        Potential future extensions include:
        - Additional Product categories.
        - Multilingual hosted pages.
        The following remain separate even if implemented later:
        - KYC, AML, and Sumsub identity verification.
        - Finance and commission processing.
        """;

    [Fact]
    public void DeliverySuggestsThreeStoryListsWithoutChangingTheSourceOrSavingDecisions()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, StoryBrd); CreateIntake(f);
        var before = f.Snapshot();
        var status = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(status.Errors);
        var fields = status.Pages.Single(p => p.Id == "delivery").Fields.Where(f => f.Id.StartsWith("delivery-stories-", StringComparison.Ordinal)).ToDictionary(f => f.Id);
        Assert.Equal(3, fields.Count);
        Assert.Contains("### Security", fields["delivery-stories-foundation"].SuggestedAnswer);
        var mvp = fields["delivery-stories-mvp"].SuggestedAnswer;
        Assert.Contains("### Product catalogue", mvp);
        Assert.Contains("As an authorised user, I want to publish approved products.", mvp);
        Assert.Contains("immediately redirect", mvp);
        Assert.Contains("Personal data shall not appear", mvp);
        Assert.Contains("<!-- Source:", mvp);
        Assert.DoesNotContain("Public catalogue browsing", mvp); // Summary is not a duplicate story.
        var later = fields["delivery-stories-post-mvp"].SuggestedAnswer;
        Assert.Contains("Additional Product categories", later);
        Assert.Contains("not committed for delivery", later);
        foreach (var field in fields.Values)
        {
            Assert.Null(field.Answer);
            Assert.DoesNotContain('\r', field.SuggestedAnswer);
            Assert.DoesNotContain("Sumsub", field.SuggestedAnswer);
            Assert.DoesNotContain("Finance", field.SuggestedAnswer);
            Assert.DoesNotContain("visual form builder", field.SuggestedAnswer);
        }
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void ReviewedStoriesRoundTripWithOtherDeliveryAnswersAndRetainHumanEdits()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, StoryBrd); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var originalSource = File.ReadAllBytes(Path.Combine(f.Authority, status.Plan!.SourcePath));
        var originalApproval = File.ReadAllBytes(f.ApprovedBacklog);
        const string reviewed = "### Reviewed MVP story\n\nAs a customer, I want an immediate redirect.\n\n- Navigation uses the current tab.\n<!-- Source: Direct handoff -->";
        var answers = status.Pages.Single(p => p.Id == "delivery").Fields.Where(f => f.Required)
            .ToDictionary(f => f.Id, f => f.SuggestedAnswer.Length > 0 ? f.SuggestedAnswer : "Explicit reviewed delivery direction.");
        answers["delivery-stories-mvp"] = reviewed;
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "delivery", answers, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors);
        Assert.True(saved.Pages.Single(p => p.Id == "delivery").Complete);
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Equal(reviewed, reopened.Pages.Single(p => p.Id == "delivery").Fields.Single(f => f.Id == "delivery-stories-mvp").Answer);
        Assert.Contains("Foundation — required regardless of release scope", File.ReadAllText(Path.Combine(f.Authority, status.Plan.RequestPath)));
        Assert.Equal(originalSource, File.ReadAllBytes(Path.Combine(f.Authority, status.Plan.SourcePath)));
        Assert.Equal(originalApproval, File.ReadAllBytes(f.ApprovedBacklog));
        var stale = f.Service.SaveWizard(f.Authority, new("referrals", "delivery", answers, "Reviewer", status.Revision!));
        Assert.NotEmpty(stale.Errors);
        Assert.Equal(reviewed, f.Service.Wizard(f.Authority, "referrals").Pages.Single(p => p.Id == "delivery").Fields.Single(f => f.Id == "delivery-stories-mvp").Answer);
    }

    [Fact]
    public void ScopeOnlyBrdsStillProvideStoriesAndDoNotTreatExclusionsAsLaterWork()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# Feature\n## Foundation\n- Authenticated administration.\n## MVP\n- Browse the available products.\n## Out of scope\n### Future integration\n- KYC verification through Sumsub.\n");
        CreateIntake(f);
        var fields = f.Service.Wizard(f.Authority, "referrals").Pages.Single(p => p.Id == "delivery").Fields.ToDictionary(f => f.Id);
        Assert.Contains("Authenticated administration", fields["delivery-stories-foundation"].SuggestedAnswer);
        Assert.Contains("Browse the available products", fields["delivery-stories-mvp"].SuggestedAnswer);
        Assert.StartsWith("No Post-MVP stories", fields["delivery-stories-post-mvp"].SuggestedAnswer);
    }

    [Fact]
    public void StoryProjectionIgnoresCommentAndCodeExamplesAndKeepsLaterPhasesSeparate()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# Feature\n## MVP\n- Browse the available products.\n```text\n## MVP\n- An invented code example.\n```\n<!--\n## Foundation\n- An invented comment.\n-->\n## Post-MVP\n- Export monthly activity reports.\n");
        CreateIntake(f);
        var fields = f.Service.Wizard(f.Authority, "referrals").Pages.Single(p => p.Id == "delivery").Fields.ToDictionary(f => f.Id);
        Assert.Empty(fields["delivery-stories-foundation"].SuggestedAnswer);
        Assert.DoesNotContain("invented", string.Join('\n', fields.Where(f => f.Key.StartsWith("delivery-stories-", StringComparison.Ordinal)).Select(f => f.Value.SuggestedAnswer)));
        Assert.Contains("Export monthly activity reports", fields["delivery-stories-post-mvp"].SuggestedAnswer);
        Assert.DoesNotContain("not committed", fields["delivery-stories-post-mvp"].SuggestedAnswer);
    }

    [Fact]
    public void MatchingIntegrationRequirementsExtendAStoryInsteadOfDuplicatingIt()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# BRD\n## Functional Requirements\n### Optional Bank Webhook\n| ID | Requirement |\n|---|---|\n| WHK-001 | The platform shall send captured referrals to the Bank. |\n## Integration Requirements\n### Bank Webhook\n| ID | Requirement |\n|---|---|\n| INT-001 | The contract shall use authenticated delivery. |\n");
        CreateIntake(f);
        var stories = f.Service.Wizard(f.Authority, "referrals").Pages.Single(p => p.Id == "delivery").Fields.Single(f => f.Id == "delivery-stories-mvp").SuggestedAnswer;
        Assert.Contains("### Optional Bank Webhook", stories);
        Assert.DoesNotContain("### Bank Webhook", stories);
        Assert.Contains("authenticated delivery", stories);
        Assert.Contains("INT-001", stories);
    }
}
