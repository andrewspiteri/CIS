using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void DeliveryDependencyScope_ExplicitSelectedRootRetainsUnrelatedChangesAndReopensForItsGenericDependency()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var handler = AddDeliveryCode(f, "backend", "handler.ts", "import { z } from './p';\nexport function run() { return z; }\n");
        var root = Path.GetDirectoryName(handler)!;
        var dependency = Path.Combine(root, "p.ts");
        File.WriteAllText(dependency, "export const z = 7;\n");
        var unrelated = Path.Combine(root, "weather.ts");
        File.WriteAllText(unrelated, "export const forecast = 1;\n");
        CreateIntake(f);
        var generation = new DeliveryGeneration();
        var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals");
        var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(delivery.Evidence);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Extend the reviewed handler to retain the required privacy evidence.", ["backend/handler.ts"], delivery.InputHash!);
        var saved = service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var accepted = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(accepted.ReviewCurrent, accepted.ReviewReason);

        File.AppendAllText(unrelated, "export const region = 'north';\n");
        var independent = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(independent.ReviewCurrent, independent.ReviewReason);
        Assert.Equal(accepted.Review!.Actor, independent.Review!.Actor);
        Assert.Equal(accepted.Review.SavedAt, independent.Review.SavedAt);
        Assert.Equal(accepted.Review.Plan, independent.Review.Plan);

        var timestamp = File.GetLastWriteTimeUtc(dependency);
        File.WriteAllText(dependency, "export const z = 9;\n");
        File.SetLastWriteTimeUtc(dependency, timestamp);
        service = DeliveryService(f, generation);
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent);
        Assert.Equal("needs-review", changed.ReviewState);
        Assert.Contains("p.ts", changed.ReviewReason, StringComparison.Ordinal);
        Assert.Equal(accepted.Review!.Actor, changed.Review!.Actor);
        Assert.Equal(accepted.Review.SavedAt, changed.Review.SavedAt);
        Assert.Equal(accepted.Review.Plan, changed.Review.Plan);
        Assert.NotEmpty(service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Reviewer", saved.Revision!) { DeliveryReview = choice }).Errors);
        Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void DeliveryDependencyScope_UnsupportedPythonCoverageDoesNotProveItsImportedInputIndependent()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.py", "from p import (\n    limit,\n)\n\ndef privacyEvidence():\n    return limit\n");
        var dependency = Path.Combine(Path.GetDirectoryName(archive)!, "p.py");
        File.WriteAllText(dependency, "limit = 7\n");
        var weather = Path.Combine(Path.GetDirectoryName(archive)!, "weather.ts");
        File.WriteAllText(weather, "export const forecast = 1;\n");
        CreateIntake(f);
        var generation = new DeliveryGeneration();
        var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals");
        var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Contains(delivery.Evidence, evidence => evidence.Path == "archive.py");
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Extend the reviewed archive to retain the required privacy evidence.", ["backend/archive.py"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var accepted = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.Equal("Reviewer", accepted.Review!.Actor);
        Assert.True(accepted.ReviewCurrent, accepted.ReviewReason);
        Assert.Equal("unknown", accepted.AssessmentState);
        Assert.Contains("conservative", accepted.AssessmentReason, StringComparison.OrdinalIgnoreCase);
        File.AppendAllText(weather, "export const region = 'north';\n");
        var independent = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(independent.ReviewCurrent, independent.ReviewReason);
        Assert.Equal(accepted.Review.SavedAt, independent.Review!.SavedAt);

        File.WriteAllText(dependency, "limit = 9\n");
        var changed = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent);
        Assert.Equal("needs-review", changed.ReviewState);
        Assert.Equal("unknown", changed.AssessmentState);
        Assert.Contains("p.py", changed.ReviewReason, StringComparison.Ordinal);
        Assert.Contains("dependenc", changed.ReviewReason + " " + changed.AssessmentReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(accepted.Review.Actor, changed.Review!.Actor);
        Assert.Equal(accepted.Review.SavedAt, changed.Review.SavedAt);
        Assert.Equal(accepted.Review.Plan, changed.Review.Plan);
        Assert.Equal(0, generation.Calls);
    }

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

    [Fact]
    public void DeliveryDependencyScope_DeclarationCollisionsAcrossLanguagesDoNotBroadenSupportedStory()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts",
            "import { Config } from './config';\nexport class Archive { getDocumentVersion() { return Config.version; } }");
        var root = Path.GetDirectoryName(archive)!;
        var config = Path.Combine(root, "config.ts"); File.WriteAllText(config, "export class Config { static version = 7; }");
        var unrelated = Path.Combine(root, "weather.py"); File.WriteAllText(unrelated, "class Config:\n    limit = 7\n");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Extend the archive with its supported configuration dependency.", ["backend/archive.ts"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(reviewed.ReviewCurrent, reviewed.ReviewReason); Assert.NotEqual("unknown", reviewed.AssessmentState);

        var scripts = Path.Combine(root, "scripts"); Directory.CreateDirectory(scripts);
        var locked = Path.Combine(scripts, "unavailable.py"); File.WriteAllText(locked, "class Config:\n    limit = 1\n");
        using var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        File.WriteAllText(unrelated, "class Config:\n    limit = 9\n");
        var retainedDelivery = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.Empty(retainedDelivery.Errors);
        var retained = retainedDelivery.Stories.Single();
        Assert.True(retained.ReviewCurrent, retained.ReviewReason); Assert.NotEqual("unknown", retained.AssessmentState);
        Assert.Equal(reviewed.Review!.Actor, retained.Review!.Actor); Assert.Equal(reviewed.Review.SavedAt, retained.Review.SavedAt);
        File.WriteAllText(config, "export class Config { static version = 9; }");
        var changed = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Equal("needs-review", changed.ReviewState);
        Assert.Contains("config.ts", changed.ReviewReason, StringComparison.Ordinal); Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void DeliveryDependencyScope_SelectedDotAndPlatformCaseAliasesKeepDependencyRoots()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        Directory.CreateDirectory(Path.Combine(f.Root, "backend", "src"));
        var handler = AddDeliveryCode(f, "backend", "src/Handler.ts", "import { z } from './p';\nexport function run() { return z; }");
        var root = Path.GetDirectoryName(handler)!;
        var dependency = Path.Combine(root, "p.ts"); File.WriteAllText(dependency, "export const z = 7;");
        var unrelated = Path.Combine(root, "weather.ts"); File.WriteAllText(unrelated, "export const forecast = 1;");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(delivery.Evidence);
        var selection = OperatingSystem.IsWindows() ? "backend/SRC/./handler.ts" : "backend/src/./Handler.ts";
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Extend the selected handler while preserving its dependency boundary.", [selection], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var reviewed = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(reviewed.ReviewCurrent, reviewed.ReviewReason);
        Assert.Contains("backend/src/Handler.ts", reviewed.Review!.EvidenceHashes.Keys);

        File.AppendAllText(unrelated, "\nexport const forecastDays = 10;");
        var retained = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(retained.ReviewCurrent, retained.ReviewReason); Assert.Equal(reviewed.Review.SavedAt, retained.Review!.SavedAt);
        File.WriteAllText(dependency, "export const z = 9;");
        var changed = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Equal("needs-review", changed.ReviewState);
        Assert.Contains("p.ts", changed.ReviewReason, StringComparison.Ordinal); Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void DeliveryDependencyScope_ExcludedSelectedEvidenceIsRejectedWithoutPersistingDecision()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        Directory.CreateDirectory(Path.Combine(f.Root, "backend", "scripts"));
        var handler = AddDeliveryCode(f, "backend", "scripts/handler.ts", "import { z } from './p';\nexport function run() { return z; }");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(handler)!, "p.ts"), "export const z = 7;");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var requestPath = Path.Combine(f.Authority, wizard.Plan!.RequestPath); var before = File.ReadAllBytes(requestPath);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Review the selected script only with complete dependency coverage.", ["backend/scripts/handler.ts"], delivery.InputHash!);
        var rejected = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Contains(rejected.Errors, error => error.Contains("implementation inventory", StringComparison.OrdinalIgnoreCase));
        Assert.False(rejected.Applied); Assert.Equal(before, File.ReadAllBytes(requestPath));
        Assert.Null(DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single().Review);
        Assert.Equal(0, generation.Calls);
    }
}
