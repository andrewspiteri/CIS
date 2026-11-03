using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData("const fs = require('fs');", false)]
    [InlineData("import path from 'node:path';", false)]
    [InlineData("const vscode = require('vscode');", true)]
    public void DeliveryRuntimeDependency_KnownRuntimeAndDeclaredHostKeepReviewAndCacheUntilContractChanges(string import, bool host)
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", import + "\nexport class Archive { getDocumentVersion() { return 1; } }");
        var root = Path.GetDirectoryName(archive)!;
        var package = Path.Combine(root, "package.json");
        var engines = new Dictionary<string, string> { ["node"] = ">=20" };
        if (host) engines["vscode"] = "^1.95.0";
        void WriteContract() => File.WriteAllText(package, JsonSerializer.Serialize(new { name = "runtime-fixture", version = "1.0.0", engines }));
        WriteContract();
        CreateIntake(f);
        var generation = new DeliveryGeneration { Mode = "document-grounded" };
        var service = DeliveryService(f, generation);
        var prepared = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(prepared.Errors);
        Assert.Equal("current", prepared.Status);
        Assert.NotEqual("unknown", prepared.Stories.Single().AssessmentState);
        RecordRuntimeDependencyReview(f, service, "Privacy evidence", "archive.ts");
        var recorded = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(recorded.ReviewCurrent, recorded.ReviewReason);

        File.WriteAllText(Path.Combine(root, "weather.ts"), "export class Weather { getForecast() { return 7; } }");
        var fresh = DeliveryService(f, generation);
        var retained = fresh.Delivery(f.Authority, "referrals", false);
        Assert.Equal("current", retained.Status);
        Assert.True(retained.Cached);
        Assert.True(retained.Stories.Single().ReviewCurrent, retained.Stories.Single().ReviewReason);
        Assert.Equal(recorded.Review!.SavedAt, retained.Stories.Single().Review!.SavedAt);
        Assert.True(fresh.Delivery(f.Authority, "referrals", true, fresh.Wizard(f.Authority, "referrals").Revision).Cached);
        Assert.Equal(1, generation.Calls);

        engines[host ? "vscode" : "node"] = host ? "^1.96.0" : ">=22";
        WriteContract();
        var changed = fresh.Delivery(f.Authority, "referrals", false);
        Assert.Equal("stale", changed.Status);
        Assert.False(changed.Stories.Single().ReviewCurrent);
        Assert.Equal("needs-review", changed.Stories.Single().ReviewState);
        Assert.Contains("configuration/backend/package.json", changed.Stories.Single().ReviewReason, StringComparison.Ordinal);
        Assert.Equal(recorded.Review.SavedAt, changed.Stories.Single().Review!.SavedAt);
        Assert.Equal(1, generation.Calls);
        if (host)
        {
            engines.Remove("vscode");
            WriteContract();
            var undeclared = fresh.Delivery(f.Authority, "referrals", false).Stories.Single();
            Assert.False(undeclared.ReviewCurrent);
            Assert.Equal("unknown", undeclared.ReviewState);
            Assert.Equal("unknown", undeclared.AssessmentState);
        }
    }

    [Fact]
    public void DeliveryRuntimeDependency_UnknownPackageDoesNotBorrowRuntimeContractOrBlockUnrelatedStory()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n### Weather forecast\n| ID | Requirement |\n|---|---|\n| WTH-001 | The application shall retrieve the weather forecast. |\n");
        var archive = AddDeliveryCode(f, "backend", "archive.ts",
            "const external = require('@unregistered/retention');\nexport class Archive { getDocumentVersion() { return 1; } }");
        var root = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(root, "package.json"), "{\"name\":\"runtime-fixture\",\"engines\":{\"node\":\">=20\",\"vscode\":\"^1.95.0\"}}");
        File.WriteAllText(Path.Combine(root, "weather.ts"), "export class Weather { getWeatherForecast() { return 7; } }");
        CreateIntake(f);
        var generation = new DeliveryGeneration();
        var service = DeliveryService(f, generation);
        RecordRuntimeDependencyReview(f, service, "Privacy evidence", "archive.ts");
        RecordRuntimeDependencyReview(f, service, "Weather forecast", "weather.ts");
        var result = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        var unknown = result.Stories.Single(story => story.Title == "Privacy evidence");
        Assert.False(unknown.ReviewCurrent);
        Assert.Equal("unknown", unknown.ReviewState);
        Assert.Equal("unknown", unknown.AssessmentState);
        Assert.True(result.Stories.Single(story => story.Title == "Weather forecast").ReviewCurrent);
        Assert.Equal(0, generation.Calls);
    }

    private static void RecordRuntimeDependencyReview(Fixture f, FeatureIntakeService service, string title, string source)
    {
        var wizard = service.Wizard(f.Authority, "referrals");
        var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(delivery.Errors);
        var story = delivery.Stories.Single(item => item.Title == title);
        var review = new CisFeatureDeliveryReviewRequest(story.Id, "extend", ["backend"],
            "Preserve the existing owner and verify this story's runtime contract before implementation completion.",
            ["backend/" + source], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = review }).Errors);
    }
}
