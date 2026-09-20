using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void ScreenInputsFollowSavedExperienceAndBrdWithoutDependingOnUnrelatedSaveTimes()
    {
        using var f = new Fixture();
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        var imported = f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash);
        Assert.Empty(imported.Errors);
        var renderer = new ScreenCapture();
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], [renderer]);
        var state = service.Wizard(f.Authority, "referrals");
        var before = f.Snapshot();
        Assert.Equal(0, service.Screens(f.Authority, "referrals", true, state.Revision).ExitCode);
        Assert.Equal(before, f.Snapshot());
        Assert.Equal(File.ReadAllText(f.Request.SourcePath), renderer.Input!.Source);
        var original = renderer.Input.InputHash;
        Assert.NotEmpty(service.Screens(f.Authority, "referrals", true, "stale").Errors);
        var saved = service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Keep the platform." }, "Reviewer", state.Revision!));
        Assert.Empty(saved.Errors);
        service.Screens(f.Authority, "referrals", false);
        Assert.Equal(original, renderer.Input.InputHash);
        var experience = service.SaveWizard(f.Authority, new("referrals", "experience", new() { ["experience-journeys"] = "Redirect immediately." }, "Reviewer", saved.Revision!));
        Assert.Empty(experience.Errors);
        service.Screens(f.Authority, "referrals", false);
        Assert.NotEqual(original, renderer.Input.InputHash);
        Assert.Contains("Redirect immediately", renderer.Input.Direction, StringComparison.Ordinal);
        Assert.True(renderer.Current);
    }

    [Fact]
    public void ScreenReviewIsDurableBoundToExactImageAndDoesNotReviewQuestionnaireAnswers()
    {
        using var f = new Fixture();
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        Assert.Empty(f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash).Errors);
        var screen = new CisFeatureScreen(new("screen-0", "Contact", "public", "/contact", "Contact capture", "form", "Contact capture",
            [new("Name", "text", "")], [new("Continue", "Bank")], ["Invalid name"]), "<svg/>", "frontend");
        var renderer = new ScreenCapture { Preview = new("current", "preview-hash", [screen], [], []) };
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], [renderer]);
        var status = service.Wizard(f.Authority, "referrals");
        var request = new CisFeatureWizardAnswer("referrals", "experience", [], "Reviewer", status.Revision!) {
            ScreenReview = new(screen.Plan.Id, screen.Revision, "preview-hash", "not-needed", "Handled by the host.") };
        var before = f.Snapshot();
        Assert.NotEmpty(service.SaveWizard(f.Authority, request with { ScreenReview = request.ScreenReview with { ScreenRevision = "stale" } }).Errors);
        Assert.Equal(before, f.Snapshot());
        var saved = service.SaveWizard(f.Authority, request);
        Assert.Empty(saved.Errors);
        Assert.False(saved.Pages.Single(page => page.Id == "experience").Complete);
        Assert.All(saved.Pages.Single(page => page.Id == "experience").Fields, field => Assert.Null(field.Answer));
        Assert.Empty(service.Wizard(f.Authority, "referrals").Errors);
        service.Screens(f.Authority, "referrals", false);
        var review = Assert.Single(renderer.Input!.Reviews);
        Assert.Equal("not-needed", review.Decision);
        Assert.Equal("Handled by the host.", review.Feedback);
        Assert.Equal(screen.Revision, review.ScreenRevision);
        Assert.Contains("Proposed screen review", File.ReadAllText(Path.Combine(f.Authority, preview.Plan.RequestPath)), StringComparison.Ordinal);
        Assert.NotEmpty(service.SaveWizard(f.Authority, request).Errors); // Old tabs cannot overwrite newer choices.
        renderer.Preview = new("missing", null, [], [], []);
        var restored = service.SaveWizard(f.Authority, request with { ExpectedRevision = saved.Revision!,
            ScreenReview = new(screen.Key, screen.Revision, "", "restore", "") });
        Assert.Empty(restored.Errors);
        service.Screens(f.Authority, "referrals", false);
        Assert.Equal("restore", renderer.Input!.Reviews.Last().Decision);
        Assert.Equal(2, renderer.Input.Reviews.Count);
    }

    private sealed class ScreenCapture : ICisFeatureScreenGenerator
    {
        public CisFeatureScreenInput? Input { get; private set; }
        public bool Current { get; private set; }
        public CisFeatureScreensResult? Preview { get; set; }
        public CisFeatureScreensResult Run(CisFeatureScreenInput input, bool prepare, Func<bool> stillCurrent)
        { Input = input; Current = stillCurrent(); return Preview ?? new("missing", input.InputHash, [], [], []); }
    }
}
