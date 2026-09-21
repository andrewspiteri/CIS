namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void LaterFeaturePagesExposeDistinctQuestionsAndPartialAnswersDoNotCompleteThePage()
    {
        using var f = new Fixture(); CreateIntake(f);
        var before = f.Snapshot();
        var status = f.Service.Wizard(f.Authority, "referrals");
        foreach (var id in new[] { "technical", "architecture", "contracts", "experience", "delivery" })
        {
            var page = status.Pages.Single(page => page.Id == id);
            Assert.InRange(page.Fields.Count(field => field.Required), 3, id == "delivery" ? 8 : 4);
            Assert.Equal(page.Fields.Count, page.Fields.Select(field => field.Id).Distinct().Count());
            Assert.All(page.Fields.Where(field => field.Required), field => Assert.Null(field.Answer));
            Assert.False(page.Fields.Single(field => field.Id == "summary").Required);
        }
        Assert.Equal(before, f.Snapshot());
        var partial = f.Service.SaveWizard(f.Authority, new("referrals", "technical",
            new() { ["technical-platform"] = "Use the existing application stack." }, "Reviewer", status.Revision!));
        Assert.Empty(partial.Errors); Assert.False(partial.Pages.Single(page => page.Id == "technical").Complete);
        var remaining = partial.Pages.Single(page => page.Id == "technical").Fields.Where(field => field.Required)
            .ToDictionary(field => field.Id, field => field.Answer ?? "Explicit reviewed direction for " + field.Label);
        var completed = f.Service.SaveWizard(f.Authority, new("referrals", "technical", remaining, "Reviewer", partial.Revision!));
        Assert.Empty(completed.Errors); Assert.True(completed.Pages.Single(page => page.Id == "technical").Complete);
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.True(reopened.Pages.Single(page => page.Id == "technical").Complete);
        Assert.Equal(remaining["technical-security"], reopened.Pages.Single(page => page.Id == "technical").Fields.Single(field => field.Id == "technical-security").Answer);
        Assert.Contains("How will identity, tenant isolation and sensitive data be protected?", File.ReadAllText(Path.Combine(f.Authority, status.Plan!.RequestPath)));
    }

    [Fact]
    public void QuestionsUseRelevantSourceAndBaselineContextWithoutInventingSavedAnswers()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# BRD\n## Security\nTenant separation and encryption are required.\n## Technical specification\nChoose the cookie lifetime.\n- Deployment, migration and support model need definition.\n## Availability\nRecover failed deliveries.\n## Acceptance\n### Direct handoff\nVerify the complete referral journey.\n");
        CreateIntake(f);
        File.WriteAllText(Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md"), "# Technical intent\n## Runtime stack\nExisting .NET services and Azure hosting.\n");
        var before = f.Snapshot();
        var status = f.Service.Wizard(f.Authority, "referrals");
        var technical = status.Pages.Single(page => page.Id == "technical");
        Assert.Contains("Existing .NET services", technical.Fields.Single(field => field.Id == "technical-platform").SuggestedAnswer);
        Assert.Contains("Tenant separation", technical.Fields.Single(field => field.Id == "technical-security").SuggestedAnswer);
        Assert.DoesNotContain("cookie lifetime", technical.Fields.Single(field => field.Id == "technical-security").SuggestedAnswer);
        Assert.Contains("cookie lifetime", technical.Fields.Single(field => field.Id == "technical-decisions").SuggestedAnswer);
        Assert.Contains("Proposed C4 coverage for Referrals", status.Pages.Single(page => page.Id == "architecture").Fields.Single(field => field.Id == "architecture-views").SuggestedAnswer);
        Assert.Contains("Verify the complete referral journey", status.Pages.Single(page => page.Id == "delivery").Fields.Single(field => field.Id == "delivery-acceptance").SuggestedAnswer);
        Assert.Contains("Deployment, migration", status.Pages.Single(page => page.Id == "delivery").Fields.Single(field => field.Id == "delivery-rollout").SuggestedAnswer);
        Assert.All(technical.Fields, field => Assert.Null(field.Answer));
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void LegacyReviewedNarrativeIsPreservedWithoutPretendingItAnsweredIndividualQuestions()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        status = f.Service.SaveWizard(f.Authority, new("referrals", "technical",
            new() { ["summary"] = "Existing reviewed technical narrative." }, "Owner", status.Revision!));
        Assert.Empty(status.Errors);
        var page = status.Pages.Single(page => page.Id == "technical");
        Assert.True(page.Complete);
        Assert.Equal("Previously reviewed narrative", page.Fields[0].Label);
        Assert.All(page.Fields.Where(field => field.Id != "summary"), field => { Assert.False(field.Required); Assert.Null(field.Answer); });
        var before = f.Snapshot();
        Assert.True(f.Service.Wizard(f.Authority, "referrals").Pages.Single(page => page.Id == "technical").Complete);
        Assert.Equal(before, f.Snapshot());
    }
}
