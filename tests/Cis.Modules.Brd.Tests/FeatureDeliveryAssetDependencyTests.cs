using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData("styles.css", "import './styles.css';", ".capture { color: red; }", ".capture { color: tan; }")]
    [InlineData("styles.css", "import rawStyles from './styles.css?raw';", ".capture { color: red; }", ".capture { color: tan; }")]
    [InlineData("data.json", "import data from './data.json';", "{\"days\":7}", "{\"days\":9}")]
    [InlineData("data.json", "import data from './data';", "{\"days\":7}", "{\"days\":9}")]
    [InlineData("icon.svg", "import icon from './icon.svg';", "<svg><path d=\"M0 0\" /></svg>", "<svg><path d=\"M1 1\" /></svg>")]
    [InlineData("icon.svg", "import icon from './icon.svg?raw';",
        "<svg><defs><linearGradient id=\"grad\"/></defs><rect style=\"fill:url(&quot;#grad&quot;)\" x=\"0\"/></svg>",
        "<svg><defs><linearGradient id=\"grad\"/></defs><rect style=\"fill:url(&quot;#grad&quot;)\" x=\"1\"/></svg>")]
    public void DeliveryAssetDependency_OwnedAssetsRetainReviewAcrossUnrelatedEditsAndStaleOnAssetContent(
        string assetName, string imports, string initialContent, string changedContent)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", imports + "\nexport class Archive { getDocumentVersion() { return 1; } }");
        var root = Path.GetDirectoryName(archive)!;
        var asset = Path.Combine(root, assetName); File.WriteAllText(asset, initialContent);
        var weather = Path.Combine(root, "weather.ts"); File.WriteAllText(weather, "export class Weather { getForecast() {} }");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals");
        var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(delivery.Errors);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Retain the imported presentation and data assets while adding acknowledgement storage.", ["backend/archive.ts"], delivery.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Asset reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(reviewed.ReviewCurrent); Assert.Equal("current", reviewed.ReviewState);
        var original = reviewed.Review!;
        Assert.Contains(original.Basis!, entry => entry.Key.StartsWith("asset-dependency/", StringComparison.Ordinal)
            && entry.Key.Contains(assetName, StringComparison.Ordinal));

        var assetTime = File.GetLastWriteTimeUtc(asset);
        File.SetLastWriteTimeUtc(asset, assetTime.AddMinutes(1));
        File.AppendAllText(weather, "\nexport const forecastDays = 10;");
        var fresh = DeliveryService(f, generation);
        var unchanged = fresh.Delivery(f.Authority, "referrals", false);
        var retained = unchanged.Stories.Single();
        Assert.Empty(unchanged.Errors); Assert.True(retained.ReviewCurrent); Assert.Equal("current", retained.ReviewState);
        Assert.Equal(original.Actor, retained.Review!.Actor); Assert.Equal(original.SavedAt, retained.Review.SavedAt);
        Assert.Equal(original.Plan, retained.Review.Plan);
        var latestWizard = fresh.Wizard(f.Authority, "referrals");
        var requestPath = Path.Combine(f.Authority, latestWizard.Plan!.RequestPath);
        var requestBytes = File.ReadAllBytes(requestPath);
        var same = fresh.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Asset reviewer", latestWizard.Revision!)
        { DeliveryReview = choice with { ExpectedInputHash = unchanged.InputHash! } });
        Assert.Empty(same.Errors); Assert.Equal("unchanged", same.Status); Assert.False(same.Applied);
        Assert.Equal(requestBytes, File.ReadAllBytes(requestPath)); Assert.Equal(0, generation.Calls);

        Assert.Equal(initialContent.Length, changedContent.Length);
        File.WriteAllText(asset, changedContent); File.SetLastWriteTimeUtc(asset, assetTime);
        var changed = fresh.Delivery(f.Authority, "referrals", false);
        var stale = changed.Stories.Single();
        Assert.Empty(changed.Errors); Assert.False(stale.ReviewCurrent); Assert.Equal("needs-review", stale.ReviewState);
        Assert.Contains(assetName, stale.ReviewReason, StringComparison.Ordinal);
        Assert.Equal(original.Actor, stale.Review!.Actor); Assert.Equal(original.SavedAt, stale.Review.SavedAt);
        Assert.Equal(original.Plan, stale.Review.Plan); Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void DeliveryAssetDependency_OutsideOwnedAndLinkedTargetsRemainUnknownWithoutReadingThem()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var outside = Path.Combine(f.Root, "outside-policy.json"); File.WriteAllText(outside, "{\"days\":7}");
        var archive = AddDeliveryCode(f, "backend", "archive.ts",
            "import data from '../outside-policy.json';\nexport class Archive { getDocumentVersion() { return 1; } }");
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        // An attempted hash/read before the ownership or reparse-point guard
        // fails this fixture instead of silently accepting outside source bytes.
        using var held = new FileStream(outside, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        void AssertUnknown()
        {
            var wizard = service.Wizard(f.Authority, "referrals");
            var delivery = service.Delivery(f.Authority, "referrals", false);
            Assert.Empty(delivery.Errors);
            var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
                "Retain the decision while resolving the unavailable dependency boundary.", ["backend/archive.ts"], delivery.InputHash!);
            var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Asset reviewer", wizard.Revision!) { DeliveryReview = choice });
            Assert.Empty(saved.Errors);
            var story = service.Delivery(f.Authority, "referrals", false).Stories.Single();
            Assert.False(story.ReviewCurrent); Assert.Equal("unknown", story.ReviewState);
            Assert.NotNull(story.Review); Assert.Equal("Asset reviewer", story.Review.Actor);
            Assert.DoesNotContain(story.Review.Basis!, entry => entry.Key.StartsWith("asset-dependency/", StringComparison.Ordinal));
        }
        AssertUnknown();
        var linked = Path.Combine(Path.GetDirectoryName(archive)!, "linked-policy.json");
        File.CreateSymbolicLink(linked, outside);
        try
        {
            File.WriteAllText(archive, "import data from './linked-policy.json';\nexport class Archive { getDocumentVersion() { return 1; } }");
            AssertUnknown();
        }
        finally { File.Delete(linked); }
    }

    [Theory]
    [InlineData("rules.mjs", "import { rule } from './rules.mjs';", "import { days } from './policy.mjs'; export const rule = days;",
        "policy.mjs", "export const days = 7;", "export const days = 9;", "rules.mjs")]
    [InlineData("rules.mjs", "import { rule } from './rules';", "import { days } from './policy.mjs'; export const rule = days;",
        "policy.mjs", "export const days = 7;", "export const days = 9;", "rules")]
    [InlineData("rules.mts", "import { rule } from './rules.mjs';", "import { days } from './policy.mts'; export const rule = days;",
        "policy.mts", "export const days = 7;", "export const days = 9;", "rules.mjs")]
    [InlineData("rules.cts", "const { rule } = require('./rules.cjs');", "const { days } = require('./policy.cts'); exports.rule = days;",
        "policy.cts", "exports.days = 7;", "exports.days = 9;", "rules.cjs")]
    [InlineData("styles.css", "import './styles.css';", "@import './theme.css'; .capture { display: block; }",
        "theme.css", ".capture { color: red; }", ".capture { color: tan; }", "styles.css")]
    [InlineData("icon.svg", "import icon from './icon.svg?raw';", "<svg><style>@import url('./theme.css');</style></svg>",
        "theme.css", ".capture { color: red; }", ".capture { color: tan; }", "icon.svg")]
    [InlineData("icon.svg", "import icon from './icon.svg?raw';", "<svg><rect style=\"fill:url(./pattern.svg#p)\"/></svg>",
        "pattern.svg", "<svg id=\"p\"><path d=\"M0 0\"/></svg>", "<svg id=\"p\"><path d=\"M1 1\"/></svg>", "icon.svg")]
    public void DeliveryAssetDependency_UntraversedExecutableAndStylesheetChainsRemainUnknownAfterTargetChanges(
        string dependencyName, string imports, string dependencyContent, string targetName, string initialTarget, string changedTarget, string reasonDependency)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", imports + "\nexport class Archive { getDocumentVersion() { return 1; } }");
        var root = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(root, dependencyName), dependencyContent);
        var hasInventoriedSibling = imports.Contains("'./rules';", StringComparison.Ordinal);
        if (hasInventoriedSibling) File.WriteAllText(Path.Combine(root, "rules.ts"), "export const rule = 7;");
        var target = Path.Combine(root, targetName); File.WriteAllText(target, initialTarget);
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var retained = RecordUnknownAssetDependencyReview(f, service);
        Assert.False(retained.ReviewCurrent); Assert.Equal("unknown", retained.ReviewState);
        Assert.Contains(reasonDependency, retained.ReviewReason, StringComparison.Ordinal);
        if (dependencyName.EndsWith(".css", StringComparison.Ordinal))
            Assert.True(retained.ReviewReason.Contains("import", StringComparison.OrdinalIgnoreCase)
                || retained.ReviewReason.Contains("transitive", StringComparison.OrdinalIgnoreCase), retained.ReviewReason);
        var original = retained.Review!;

        File.WriteAllText(target, changedTarget);
        var changed = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.Empty(changed.Errors);
        var story = changed.Stories.Single();
        Assert.False(story.ReviewCurrent); Assert.Equal("unknown", story.ReviewState);
        Assert.Contains(reasonDependency, story.ReviewReason, StringComparison.Ordinal);
        Assert.Equal(original.Actor, story.Review!.Actor); Assert.Equal(original.SavedAt, story.Review.SavedAt);
        Assert.Equal(original.Plan, story.Review.Plan); Assert.Equal(0, generation.Calls);
        if (hasInventoriedSibling)
        {
            File.AppendAllText(Path.Combine(root, dependencyName), "\nexport const ruleVersion = 2;");
            var changedModule = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
            Assert.False(changedModule.ReviewCurrent); Assert.Equal("unknown", changedModule.ReviewState);
            Assert.Contains(reasonDependency, changedModule.ReviewReason, StringComparison.Ordinal);
            Assert.Equal(original.SavedAt, changedModule.Review!.SavedAt);
        }
    }

    [Fact]
    public void DeliveryAssetDependency_BenignAssetNoteDoesNotHideUnsupportedWorkerLoaderReason()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts",
            "import worker from './worker.js?worker';\nimport './styles.css';\nexport class Archive { getDocumentVersion() { return 1; } }");
        var root = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(root, "worker.js"), "export class WorkerHandler { run() { return 1; } }");
        File.WriteAllText(Path.Combine(root, "styles.css"), ".capture { color: red; }");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var story = RecordUnknownAssetDependencyReview(f, service);
        Assert.False(story.ReviewCurrent); Assert.Equal("unknown", story.ReviewState);
        Assert.Contains("worker.js?worker", story.ReviewReason, StringComparison.Ordinal);
        Assert.False(story.ReviewReason.StartsWith("Owned static asset bytes are captured", StringComparison.Ordinal), story.ReviewReason);
        Assert.Equal("Asset reviewer", story.Review!.Actor); Assert.Equal(0, generation.Calls);
    }

    private static CisFeatureDeliveryStory RecordUnknownAssetDependencyReview(Fixture f, FeatureIntakeService service)
    {
        var wizard = service.Wizard(f.Authority, "referrals");
        var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(delivery.Errors);
        var review = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Retain the original owner while resolving the named transitive dependency before implementation.", ["backend/archive.ts"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Asset reviewer", wizard.Revision!) { DeliveryReview = review }).Errors);
        return service.Delivery(f.Authority, "referrals", false).Stories.Single();
    }
}
