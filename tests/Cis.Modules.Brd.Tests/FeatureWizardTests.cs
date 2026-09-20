using Cis.Abstractions;
using Cis.Host;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void WizardResumesEightPagesAndKeepsQuestionsUnresolvedUntilExplicitlySaved()
    {
        using var f = new Fixture();
        CreateIntake(f);
        var before = f.Snapshot();
        var status = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(status.Errors); Assert.Equal(8, status.Pages.Count);
        Assert.True(status.Pages[0].Complete); Assert.False(status.Reviewed);
        var business = status.Pages.Single(page => page.Id == "business");
        Assert.Equal(3, business.Fields.Count);
        Assert.All(business.Fields, field => Assert.Null(field.Answer));
        Assert.Single(f.Service.Requests(f.Authority));
        Assert.Equal(before, f.Snapshot());
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "business",
            new() { ["summary"] = "A reusable referral core with a BridgeLink adapter." }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors); Assert.True(saved.Applied);
        Assert.False(saved.Pages.Single(page => page.Id == "business").Complete);
        Assert.Contains(saved.Pages.Single(page => page.Id == "business").Attention, item => item.Contains("2 answers", StringComparison.Ordinal));
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Equal("A reusable referral core with a BridgeLink adapter.", reopened.Pages.Single(page => page.Id == "business").Fields[0].Answer);
        Assert.DoesNotContain(File.ReadAllText(Path.Combine(f.Authority, status.Plan!.RequestPath)), "status: Active", StringComparison.Ordinal);
    }

    [Fact]
    public void SaveEvaluatesBaselineOnceAndPlaceholdersRemainUnresolved()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var before = f.Baseline.Calls;
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "TBD" }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors); Assert.Equal(before + 1, f.Baseline.Calls);
        Assert.False(saved.Pages.Single(page => page.Id == "technical").Complete);
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Equal(saved.Revision, reopened.Revision);
    }

    [Fact]
    public void WizardReviewRequiresAllPagesAndDriftRetainsAnswersButRevokesCompletion()
    {
        using var f = new Fixture(); CreateIntake(f);
        var originalApprovals = File.ReadAllBytes(f.ApprovedBacklog);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var blocked = f.Service.SaveWizard(f.Authority, new("referrals", "review", new() { ["summary"] = "Reviewed" }, "Reviewer", status.Revision!));
        Assert.NotEmpty(blocked.Errors);
        foreach (var page in status.Pages.Where(page => page.Id is not ("foundation" or "review")))
        {
            status = f.Service.SaveWizard(f.Authority, new("referrals", page.Id,
                page.Fields.ToDictionary(field => field.Id, field => "Explicit human answer for " + field.Label), "Reviewer", status.Revision!));
            Assert.Empty(status.Errors);
        }
        Assert.False(status.Reviewed);
        status = f.Service.SaveWizard(f.Authority, new("referrals", "review", new() { ["summary"] = "Feature definition reviewed; backlog approval remains required." }, "Reviewer", status.Revision!));
        Assert.Empty(status.Errors); Assert.True(status.Reviewed);
        Assert.Equal(originalApprovals, File.ReadAllBytes(f.ApprovedBacklog));
        File.AppendAllText(Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md"), "\nA changed product constraint.\n");
        var stale = f.Service.Wizard(f.Authority, "referrals");
        Assert.False(stale.Reviewed);
        Assert.All(stale.Pages.Where(page => page.Id is not ("foundation" or "review")), page =>
        {
            Assert.False(page.Complete); Assert.NotNull(page.Fields[0].Answer);
            Assert.Contains(page.Attention, message => message.Contains("baseline", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void WizardRejectsStaleSaveUnknownFieldsAndChangedSourceWithoutLosingWork()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Reviewed technical changes." }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors);
        var before = f.Snapshot();
        var old = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Outdated overwrite." }, "Reviewer", status.Revision!));
        Assert.NotEmpty(old.Errors); Assert.Equal(before, f.Snapshot());
        var invalid = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["arbitrary-field"] = "No" }, "Reviewer", saved.Revision!));
        Assert.NotEmpty(invalid.Errors); Assert.Equal(before, f.Snapshot());
        File.AppendAllText(Path.Combine(f.Authority, status.Plan!.SourcePath), "Tampered");
        Assert.NotEmpty(f.Service.Wizard(f.Authority, "referrals").Errors);
        Assert.NotEmpty(f.Service.Wizard(f.Authority, "../escape").Errors);
    }

    [Fact]
    public void FeatureWizardCommandsAreRegisteredAndStatusDispatches()
    {
        using var f = new Fixture(); CreateIntake(f);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).Build();
        foreach (var command in new[] { "list", "status", "save", "navigation" })
            Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", command, "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", "status", "--slug", "referrals", "--workspace", f.Authority, "--format", "json"]));
    }

    private static void CreateIntake(Fixture f)
    {
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        var created = f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash);
        Assert.Equal("created", created.Status);
    }
}
