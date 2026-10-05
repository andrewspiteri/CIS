using Xunit;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void LaterBrdsKeepIndependentFeatureContextAndReplacementRetainsProvenance()
    {
        using var f = new Fixture();
        const string initial = "# Initial BRD\n\n## Purpose\n\nCapture referrals for operators.\n\n## Constraints\n\nDo not log customer identifiers.\n";
        File.WriteAllText(f.Request.SourcePath, initial);
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        var first = f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash);
        Assert.Equal("created", first.Status);
        var laterPath = Path.Combine(f.Root, "later-brd.md");
        File.WriteAllText(laterPath, "# Later BRD\n\n## Purpose\n\nReconcile supplier payments.\n\n## Constraints\n\nAll amounts remain in EUR.\n");
        var later = f.Request with { Title = "Payments", Slug = "payments", SourcePath = laterPath, RepositoryMode = "existing" };
        preview = f.Service.Import(f.Authority, later, true, false);
        Assert.Equal("created", f.Service.Import(f.Authority, later, false, true, preview.Plan!.PlanHash).Status);
        File.Delete(f.Request.SourcePath);
        File.Delete(laterPath);
        var firstWizard = f.Service.Wizard(f.Authority, "referrals");
        var secondWizard = f.Service.Wizard(f.Authority, "payments");
        Assert.Empty(firstWizard.Errors); Assert.Empty(secondWizard.Errors);
        Assert.Contains("Capture referrals", firstWizard.Description);
        Assert.DoesNotContain("supplier payments", firstWizard.Description);
        Assert.Contains("Do not log customer identifiers", firstWizard.PlanningContext);
        Assert.Contains("supplier payments", secondWizard.Description);
        Assert.Contains("amounts remain in EUR", secondWizard.PlanningContext);
        Assert.Equal(f.Request.SourcePath, Assert.Single(firstWizard.SourceBrds).OriginalPath);
        Assert.Equal(laterPath, Assert.Single(secondWizard.SourceBrds).OriginalPath);
        File.WriteAllText(laterPath, "# Revised referrals BRD\n\n## Purpose\n\nCapture and deduplicate referrals.\n");
        var update = f.Service.UpdateSource(f.Authority, "referrals", laterPath, "Reviewer", true, false);
        Assert.Equal("updated", f.Service.UpdateSource(f.Authority, "referrals", laterPath, "Reviewer", false, true, update.Plan!.PlanHash).Status);
        var revised = f.Service.Wizard(f.Authority, "referrals");
        Assert.Contains("deduplicate referrals", revised.Description);
        Assert.Contains(revised.SourceHistory, document => document.Path == first.Plan!.SourcePath);
        Assert.Equal(initial, File.ReadAllText(Path.Combine(f.Authority, first.Plan!.SourcePath)));
        Assert.Contains("supplier payments", f.Service.Wizard(f.Authority, "payments").Description);
        Assert.False(revised.Reviewed);
    }
}
