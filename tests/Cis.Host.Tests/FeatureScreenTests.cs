using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Host.Tests;

public sealed class FeatureScreenTests
{
    [Fact]
    public void RegistersDraftGeneratorSeparatelyFromDeliveryDesign()
    {
        var services = new ServiceCollection();
        new DesignModule().RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<FeatureScreenGenerator>(provider.GetRequiredService<ICisFeatureScreenGenerator>());
    }

    [Fact]
    public void RendersSourceBoundControlsCachesAndDetectsBaselineOrInputDrift()
    {
        using var f = new Fixture();
        Assert.Equal("missing", f.Run(false).Status);
        Assert.Equal(0, f.Model.Calls);
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        var screen = Assert.Single(result.Screens);
        Assert.Contains("#FF6841", screen.Svg, StringComparison.Ordinal);
        Assert.Contains("Full name", screen.Svg, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", screen.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", screen.Svg, StringComparison.Ordinal);
        Assert.True(f.Run(false).Cached);
        Assert.True(f.Run(true).Cached);
        Assert.Equal(2, f.Model.Calls);
        f.Baseline.Hash = "changed-style";
        Assert.Equal("stale", f.Run(false).Status);
        Assert.Equal(2, f.Model.Calls);
        f.Baseline.Hash = "baseline";
        f.Input = f.Input with { InputHash = "changed-answers" };
        Assert.Equal("stale", f.Run(false).Status);
        Assert.False(File.Exists(Path.Combine(f.Root, "docs/cis/design/design-approval.md")));
    }

    [Fact]
    public void ConcurrentGenerationAndFailedRefreshPreserveLastGoodGallery()
    {
        using var f = new Fixture();
        var first = f.Run(true);
        Assert.Empty(first.Errors);
        f.Input = f.Input with { InputHash = "updated" };
        var manifest = Path.Combine(f.Root, ".cis/local/feature-screens/referrals/preview.json");
        var before = File.ReadAllText(manifest);
        using (var held = new FileStream(Path.Combine(Path.GetDirectoryName(manifest)!, "generate.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.NotEmpty(f.Run(true).Errors);
        f.Model.Bad = true;
        Assert.NotEmpty(f.Run(true).Errors);
        Assert.Equal(before, File.ReadAllText(manifest));
        Assert.Single(f.Run(false).Screens);
    }

    [Fact]
    public void InputsChangingDuringGenerationCannotPublishScreens()
    {
        using var f = new Fixture();
        var checks = 0;
        var result = f.Service.Run(f.Input, true, () => ++checks == 1);
        Assert.Contains(result.Errors, error => error.Contains("changed while generating", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(f.Root, ".cis/local/feature-screens/referrals/preview.json")));
    }

    [Fact]
    public void RejectsRemoteOutputUnsafePathsAndCredentials()
    {
        using var f = new Fixture();
        f.Model.Remote = true;
        Assert.NotEmpty(f.Run(true).Errors);
        f.Input = f.Input with { Slug = "../../escape" };
        Assert.NotEmpty(f.Run(true).Errors);
        f.Input = f.Input with { Slug = "referrals", Source = "api_key=abcdefghijklmnopqrstuvwxyz" };
        var calls = f.Model.Calls;
        Assert.NotEmpty(f.Run(true).Errors);
        Assert.Equal(calls, f.Model.Calls);
    }

    [Fact]
    public void LongPrefilledAnswersCannotCrowdOutOtherExperienceTopics()
    {
        using var f = new Fixture();
        f.Input = f.Input with { ExperienceAnswers = new Dictionary<string, string> {
            ["experience-baseline"] = new string('b', 6000) + "Keep the orange theme.",
            ["experience-controls"] = new string('c', 6000) + "Show inline email errors.",
            ["experience-journeys"] = new string('j', 6000) + "Redirect immediately." } };
        Assert.Empty(f.Run(true).Errors);
        Assert.All(f.Model.Prompts, prompt => {
            Assert.Contains("Keep the orange theme.", prompt, StringComparison.Ordinal);
            Assert.Contains("Show inline email errors.", prompt, StringComparison.Ordinal);
            Assert.Contains("Redirect immediately.", prompt, StringComparison.Ordinal);
            Assert.True(prompt.Length < 9000);
        });
    }

    [Fact]
    public void AmendmentsUseSavedFeedbackAndPreserveUnrelatedScreenPixels()
    {
        using var f = new Fixture();
        f.Model.TwoScreens = true;
        f.Input = f.Input with { Source = f.Input.Source + "\n\n## Export process\n\nAn operator requests an export and an independent reviewer approves the request before download." };
        var first = f.Run(true);
        Assert.Empty(first.Errors);
        Assert.Equal(2, first.Screens.Count);
        var screen = first.Screens[0]; var other = first.Screens[1];
        var review = Review(screen, "amend", "Rename the screen to Updated contact capture.");
        f.Input = f.Input with { Reviews = [review], TargetScreenKey = screen.Key };
        Assert.Equal("changes-requested", f.Run(false).Status);
        var calls = f.Model.Calls;
        var amended = f.Run(true);
        Assert.Empty(amended.Errors);
        Assert.Equal(calls + 1, f.Model.Calls);
        Assert.Contains(review.Feedback, f.Model.Prompts.Last(), StringComparison.Ordinal);
        Assert.Equal("Updated contact capture", amended.Screens[0].Plan.Title);
        Assert.NotEqual(screen.Revision, amended.Screens[0].Revision);
        Assert.Equal(other.Svg, amended.Screens[1].Svg);
        Assert.Equal(review.Id, amended.Screens[0].AppliedReviewId);
        Assert.Equal("current", f.Run(false).Status);
        Assert.True(f.Run(true).Cached);
    }

    [Fact]
    public void RejectionsSurviveReopeningRenumberedBrdAndCacheLossWithoutModelDecisions()
    {
        using var f = new Fixture();
        var screen = Assert.Single(f.Run(true).Screens);
        f.Input = f.Input with { Reviews = [Review(screen, "not-needed", "Already handled by the host.")] };
        Assert.Equal("not-needed", Assert.Single(f.Run(false).Reviews).Decision);
        var calls = f.Model.Calls;
        Assert.True(f.Run(true).Cached);
        Assert.Equal(calls, f.Model.Calls);
        File.Delete(Path.Combine(f.Root, ".cis/local/feature-screens/referrals/preview.json"));
        f.Input = f.Input with { Source = f.Input.Source.Replace("## Contact", "## 7.1 Contact", StringComparison.Ordinal), InputHash = "new-brd" };
        var regenerated = f.Run(true);
        Assert.Empty(regenerated.Errors);
        Assert.Empty(regenerated.Screens);
        Assert.Equal("not-needed", Assert.Single(regenerated.Reviews).Decision);
        Assert.Equal(calls + 1, f.Model.Calls); // Selection only; rejected form is never regenerated.
    }

    [Fact]
    public void ExcludedImagesCannotCollideWithNewScreensAfterSectionInsertion()
    {
        using var f = new Fixture();
        var excluded = Assert.Single(f.Run(true).Screens);
        f.Input = f.Input with { Reviews = [Review(excluded, "not-needed", "Handled by the host.")],
            Source = "## Product selection\n\nShow eligible offers, their rates and terms. The customer selects an offer before continuing.\n\n" + f.Input.Source,
            InputHash = "inserted-section" };
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Screens.Count);
        Assert.Equal(2, result.Screens.Select(screen => screen.Plan.Id).Distinct().Count());
        Assert.Contains(result.Screens, screen => screen.Key == excluded.Key && screen.Svg == excluded.Svg);
        f.Input = f.Input with { TargetScreenKey = "unknown-screen" };
        Assert.NotEmpty(f.Run(true).Errors);
    }

    [Fact]
    public void FailedAmendmentLeavesFeedbackPendingAndPreservesPreviousImage()
    {
        using var f = new Fixture();
        var screen = Assert.Single(f.Run(true).Screens);
        f.Input = f.Input with { Reviews = [Review(screen, "amend", "Rename the title.")] };
        f.Model.Bad = true;
        Assert.NotEmpty(f.Run(true).Errors);
        var pending = f.Run(false);
        Assert.Equal("changes-requested", pending.Status);
        Assert.Equal(screen.Svg, Assert.Single(pending.Screens).Svg);
        Assert.Equal("Rename the title.", Assert.Single(pending.Reviews).Feedback);
    }

    private static CisFeatureScreenReview Review(CisFeatureScreen screen, string decision, string feedback) => new(
        Guid.NewGuid().ToString("N"), screen.Key, screen.Plan.Title, screen.Plan.Evidence, screen.Plan.FrontendType,
        decision, feedback, "Reviewer", "2026-09-20", "source", screen.Revision);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-feature-screen-tests", Guid.NewGuid().ToString("N"));
        public Model Model { get; } = new();
        public Baseline Baseline { get; } = new();
        public CisFeatureScreenInput Input { get; set; }
        public FeatureScreenGenerator Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Input = new(Root, "referrals", "Referrals", "source-and-answers", "## Contact capture\n\nCollect full name and email. Continue redirects immediately to the bank. Show field validation before continuing.", "");
            Service = new(Model, [Baseline], new NodeDesignProcessRunner());
        }
        public CisFeatureScreensResult Run(bool prepare) => Service.Run(Input, prepare, () => true);
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Path.Combine(Path.GetTempPath(), "cis-feature-screen-tests"), Root)) throw new InvalidOperationException();
            Directory.Delete(Root, true);
        }
    }
    private sealed class Baseline : ICisUiBaselineDiscovery
    {
        public string Hash { get; set; } = "baseline";
        public CisUiBaselineResult Discover(string path) => new("ready", path, Hash, false,
            [new("frontend", path, 1, 1, false, [], [new("$color-primary", "#FF6841", new("style.scss", 1))])], new Dictionary<string, string>(), [], []);
    }
    private sealed class Model : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public bool Bad { get; set; }
        public bool Remote { get; set; }
        public bool TwoScreens { get; set; }
        public List<string> Prompts { get; } = [];
        public CisAiStatus GetStatus() => new([new("local", "available", "http://localhost", true, [new("test")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++;
            Prompts.Add(request.Prompt);
            Assert.False(request.AllowRemote);
            var json = request.Prompt.Contains("numbered BRD", StringComparison.Ordinal)
                ? TwoScreens ? """{"sections":[{"index":0,"frontendType":"public","layout":"form"},{"index":1,"frontendType":"backoffice","layout":"form"}]}""" : """{"sections":[{"index":0,"frontendType":"public","layout":"form"}]}"""
                : JsonSerializer.Serialize(new { title = request.Prompt.StartsWith("Revise ONE", StringComparison.Ordinal) ? "Updated contact capture" : "Contact capture", purpose = "Capture contact details before handoff",
                    fields = new[] { new { label = "Full name", kind = "text", value = "<script>" } },
                    actions = new[] { new { label = "Continue", destination = "Redirect immediately to the bank" } }, states = new[] { "Reject invalid email" } });
            return new("generated", "local", "test", Bad ? "{}" : json, null, !Remote);
        }
    }
}
