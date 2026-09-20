using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void NavigationRetainsParallelFeaturesAndSharesOneBaselineEvaluation()
    {
        using var f = new Fixture(); CreateIntake(f);
        var first = f.Service.Wizard(f.Authority, "referrals");
        first = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Use existing product APIs." }, "Owner", first.Revision!));
        Assert.True(first.Pages.Single(page => page.Id == "technical").Complete);
        var other = f.Request with { Slug = "renewals", Title = "Renewals", RepositoryPath = Path.Combine(f.Root, "renewals") };
        var preview = f.Service.Import(f.Authority, other, true, false);
        Assert.Equal("created", f.Service.Import(f.Authority, other, false, true, preview.Plan!.PlanHash).Status);
        var snapshot = f.Snapshot();
        var calls = f.Baseline.Calls;
        var navigation = f.Service.Navigation(f.Authority);
        Assert.Empty(navigation.Errors); Assert.Equal(2, navigation.Features.Count);
        Assert.Equal(calls + 1, f.Baseline.Calls);
        Assert.Equal(2, navigation.Features.Single(feature => feature.Plan.Slug == "referrals").ReviewedPages);
        Assert.Equal("Use existing product APIs.", f.Service.Wizard(f.Authority, "referrals").Pages.Single(page => page.Id == "technical").Fields[0].Answer);
        Assert.Equal(snapshot, f.Snapshot());
    }

    [Fact]
    public void RepositoryWorkSupportsMultipleItemsPerRepositoryDependenciesAndReopening()
    {
        using var f = new Fixture(); CreateIntake(f);
        var backend = Path.Combine(f.Root, "backend"); Directory.CreateDirectory(backend);
        Assert.Equal(0, f.Importer.Import(new(f.Authority, "docs/cis", [backend], false, true, "owned", "none")).ExitCode);
        var status = f.Service.Wizard(f.Authority, "referrals");
        CisFeatureRepositoryWork[] work = [new("REF-API", "referrals", "Referral API", "Issue referral codes and validate their expiry.", [], []),
            new("BANKS", "backend", "Bank mode", "Expose the bank referral mode.", [], []),
            new("HANDOFF", "backend", "Customer handoff", "Redirect to the referral destination.", ["REF-API", "BANKS"], [])];
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "delivery", new() { ["summary"] = "Plan the independent APIs in parallel, then integrate." }, "Owner", status.Revision!) { RepositoryWork = work });
        Assert.Empty(saved.Errors); Assert.Equal(3, saved.RepositoryWork.Count); Assert.False(saved.Reviewed);
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Equal(3, reopened.RepositoryWork.Count);
        Assert.Equal(new[] { "REF-API", "BANKS" }, reopened.RepositoryWork.Single(item => item.Id == "HANDOFF").DependsOn);
        Assert.Equal(3, Assert.Single(f.Service.Navigation(f.Authority).Features).RepositoryWork.Count);
        Assert.Contains("### Repository work breakdown", File.ReadAllText(Path.Combine(f.Authority, status.Plan!.RequestPath)));
        // Saving a different page preserves the complete breakdown.
        var technical = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Use product conventions." }, "Owner", reopened.Revision!));
        Assert.Empty(technical.Errors); Assert.Equal(3, technical.RepositoryWork.Count);
        var before = f.Snapshot();
        foreach (var invalid in new[] {
            work.Select(item => item.Id == "REF-API" ? item with { DependsOn = ["HANDOFF"] } : item).ToArray(),
            new[] { work[0] with { RepositoryId = "unregistered" } },
            new[] { work[0] with { DependsOn = ["missing"] } },
            new[] { work[0], work[0] },
            new[] { work[0] with { ChangeIds = ["../../outside"] } },
        }) {
            var blocked = f.Service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Owner", technical.Revision!) { RepositoryWork = invalid });
            Assert.NotEmpty(blocked.Errors); Assert.Equal(before, f.Snapshot());
        }
        var stale = f.Service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Owner", status.Revision!) { RepositoryWork = [] });
        Assert.NotEmpty(stale.Errors); Assert.Equal(before, f.Snapshot());
    }
}
