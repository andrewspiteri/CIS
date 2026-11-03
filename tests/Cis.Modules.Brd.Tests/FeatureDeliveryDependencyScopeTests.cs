using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void DeliveryDependencyScope_SharedCalleeDoesNotCoupleSiblingStoriesButItsChangeReopensBoth()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n### Weather forecast\n| ID | Requirement |\n|---|---|\n| WTH-001 | The application shall retrieve the weather forecast. |\n");
        var archive = AddDeliveryCode(f, "backend", "archive.ts",
            "import { UniqueLogger } from './logger';\nexport class Archive { getDocumentVersion() { return UniqueLogger.write('version'); } }");
        var root = Path.GetDirectoryName(archive)!;
        var weather = Path.Combine(root, "weather.ts");
        File.WriteAllText(weather,
            "import { UniqueLogger } from './logger';\nexport class Weather { getWeatherForecast() { return UniqueLogger.write('forecast'); } }");
        var logger = Path.Combine(root, "logger.ts");
        File.WriteAllText(logger, "export class UniqueLogger { static write(value: string) { return value.length; } }");
        CreateIntake(f);
        var generation = new DeliveryGeneration();
        var service = DeliveryService(f, generation);
        void Review(string title, string source)
        {
            var wizard = service.Wizard(f.Authority, "referrals");
            var delivery = service.Delivery(f.Authority, "referrals", false);
            Assert.Empty(delivery.Errors);
            var story = delivery.Stories.Single(item => item.Title == title);
            var choice = new CisFeatureDeliveryReviewRequest(story.Id, "extend", ["backend"],
                "Review the existing " + title + " behavior and extend only its acceptance requirements.",
                ["backend/" + source], delivery.InputHash!);
            Assert.Empty(service.SaveWizard(f.Authority,
                new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        }
        Review("Privacy evidence", "archive.ts");
        Review("Weather forecast", "weather.ts");
        var before = service.Delivery(f.Authority, "referrals", false);
        Assert.All(before.Stories, story => Assert.True(story.ReviewCurrent, story.ReviewReason));
        var acceptedArchive = before.Stories.Single(story => story.Title == "Privacy evidence").Review!;

        File.AppendAllText(weather, "\nexport const forecastDays = 14;");
        var siblings = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        var unaffected = siblings.Stories.Single(story => story.Title == "Privacy evidence");
        Assert.True(unaffected.ReviewCurrent, unaffected.ReviewReason);
        Assert.Equal(acceptedArchive.SavedAt, unaffected.Review!.SavedAt);
        Assert.Equal(acceptedArchive.Actor, unaffected.Review.Actor);
        Assert.Equal(acceptedArchive.Plan, unaffected.Review.Plan);
        var affected = siblings.Stories.Single(story => story.Title == "Weather forecast");
        Assert.False(affected.ReviewCurrent);
        Assert.Contains("weather.ts", affected.ReviewReason, StringComparison.Ordinal);

        Review("Weather forecast", "weather.ts");
        Assert.All(service.Delivery(f.Authority, "referrals", false).Stories, story => Assert.True(story.ReviewCurrent, story.ReviewReason));
        File.AppendAllText(logger, "\nexport const tracingEnabled = true;");
        var dependents = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.All(dependents.Stories, story =>
        {
            Assert.False(story.ReviewCurrent);
            Assert.Contains("logger.ts", story.ReviewReason, StringComparison.Ordinal);
            Assert.Equal("Reviewer", story.Review!.Actor);
        });
        Assert.Equal(acceptedArchive.SavedAt, dependents.Stories.Single(story => story.Title == "Privacy evidence").Review!.SavedAt);
        Assert.Equal(0, generation.Calls);
    }
}
