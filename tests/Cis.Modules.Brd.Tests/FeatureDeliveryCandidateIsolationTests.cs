using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void DeliveryCandidateIsolation_DirectCandidateChangesMatterWithoutFollowingItsOwnFallbackCandidates()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n### Weather forecast\n| ID | Requirement |\n|---|---|\n| WTH-001 | The application shall retrieve the weather forecast. |\n");
        var archive = AddDeliveryCode(f, "backend", "archive.cs",
            "namespace Synthetic; public class Archive { public int getDocumentVersion() { return new Result(1).Value; } private record Result(int Value); }");
        var root = Path.GetDirectoryName(archive)!;
        var resultCandidate = Path.Combine(root, "result-candidate.cs");
        File.WriteAllText(resultCandidate,
            "namespace Synthetic; public class ResultCandidate { private record Result(int Value); private record Options(int Days); }");
        var optionsOwner = Path.Combine(root, "options-owner.cs");
        File.WriteAllText(optionsOwner, "namespace Synthetic; public class OptionsOwner { private record Options(int Days); }");
        File.WriteAllText(Path.Combine(root, "other-options.cs"), "namespace Synthetic; public class OtherOptions { private record Options(int Days); }");
        File.WriteAllText(Path.Combine(root, "weather.cs"), "namespace Synthetic; public class Weather { public int getWeatherForecast() => 7; }");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        void Review(string title, string source)
        {
            var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
            Assert.Empty(delivery.Errors);
            var story = delivery.Stories.Single(item => item.Title == title);
            var choice = new CisFeatureDeliveryReviewRequest(story.Id, "extend", ["backend"],
                "Retain the reviewed " + title + " plan while verifying its bounded evidence.", ["backend/" + source], delivery.InputHash!);
            Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        }
        Review("Privacy evidence", "archive.cs"); Review("Weather forecast", "weather.cs");
        var before = service.Delivery(f.Authority, "referrals", false);
        Assert.All(before.Stories, story => Assert.True(story.ReviewCurrent, story.ReviewReason));
        var acceptedArchive = before.Stories.Single(story => story.Title == "Privacy evidence").Review!;
        var acceptedWeather = before.Stories.Single(story => story.Title == "Weather forecast").Review!;
        File.AppendAllText(optionsOwner, "\n// Unrelated options-owner implementation changed.\n");
        var unrelated = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.Empty(unrelated.Errors);
        Assert.All(unrelated.Stories, story => Assert.True(story.ReviewCurrent, story.ReviewReason));
        Assert.Equal(acceptedArchive.SavedAt, unrelated.Stories.Single(story => story.Title == "Privacy evidence").Review!.SavedAt);
        Assert.Equal(acceptedWeather.SavedAt, unrelated.Stories.Single(story => story.Title == "Weather forecast").Review!.SavedAt);
        File.WriteAllText(Path.Combine(root, "added-options.cs"), "namespace Synthetic; public class AddedOptions { private record Options(int Days); }");
        var addedDeclaration = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.Empty(addedDeclaration.Errors);
        Assert.All(addedDeclaration.Stories, story => Assert.True(story.ReviewCurrent, story.ReviewReason));
        var archiveAfterAddition = addedDeclaration.Stories.Single(story => story.Title == "Privacy evidence").Review!;
        var weatherAfterAddition = addedDeclaration.Stories.Single(story => story.Title == "Weather forecast").Review!;
        Assert.Equal(acceptedArchive.SavedAt, archiveAfterAddition.SavedAt); Assert.Equal(acceptedArchive.Actor, archiveAfterAddition.Actor);
        Assert.Equal(acceptedArchive.Plan, archiveAfterAddition.Plan);
        Assert.Equal(acceptedWeather.SavedAt, weatherAfterAddition.SavedAt); Assert.Equal(acceptedWeather.Actor, weatherAfterAddition.Actor);
        Assert.Equal(acceptedWeather.Plan, weatherAfterAddition.Plan);
        File.AppendAllText(resultCandidate, "\n// A direct Result candidate changed.\n");
        var affected = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        var archiveStory = affected.Stories.Single(story => story.Title == "Privacy evidence");
        var weatherStory = affected.Stories.Single(story => story.Title == "Weather forecast");
        Assert.False(archiveStory.ReviewCurrent); Assert.Equal("needs-review", archiveStory.ReviewState);
        Assert.Contains("result-candidate.cs", archiveStory.ReviewReason, StringComparison.Ordinal);
        Assert.Equal(acceptedArchive.Actor, archiveStory.Review!.Actor); Assert.Equal(acceptedArchive.SavedAt, archiveStory.Review.SavedAt);
        Assert.Equal(acceptedArchive.Plan, archiveStory.Review.Plan);
        Assert.True(weatherStory.ReviewCurrent, weatherStory.ReviewReason);
        Assert.Equal(acceptedWeather.SavedAt, weatherStory.Review!.SavedAt); Assert.Equal(0, generation.Calls);
    }
}
