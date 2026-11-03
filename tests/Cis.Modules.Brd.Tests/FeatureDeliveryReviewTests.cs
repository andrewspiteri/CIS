using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    private const string PrivacyStory = "# Feature\n## Functional Requirements\n### Privacy evidence\n| ID | Requirement |\n|---|---|\n| PRV-001 | The capture flow shall retain a versioned Azure Storage document. |\n| PRV-002 | The referral shall store its acknowledgement timestamp and document version. |\n";

    [Fact]
    public void DeliverySensitiveCheckUsesSourceTextInsteadOfEscapedExcerptLineNumbers()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.service.ts", "export class Archive {\n getDocumentVersion() { return this.azureStorage.versionId; }\n password = '';\n}");
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var result = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Evidence, e => e.Path == "archive.service.ts");
    }

    [Fact]
    public void DeliverySearchUsesRequirementsAndCodeContentBeyondStoryFilenames()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.service.ts", "export class Archive {\n async fetch(versionId: string) {\n const documentVersion = await this.azureStorage.getDocument(versionId);\n return documentVersion;\n }\n}");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var result = service.Delivery(f.Authority, "referrals", false);
        var story = Assert.Single(result.Stories);
        Assert.Equal("not-assessed", story.AssessmentState);
        Assert.Contains(result.Evidence, e => e.Path == "archive.service.ts" && e.Excerpt.Contains("documentVersion", StringComparison.Ordinal));
        Assert.Equal(2, story.Requirements.Count);
        Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void DeliveryMissingEvidenceAndRejectedModelClaimsExplainDifferentReasons()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var service = DeliveryService(f, new DeliveryGeneration());
        var empty = service.Delivery(f.Authority, "referrals", false);
        Assert.Equal("no-matching-evidence", Assert.Single(empty.Stories).AssessmentState);
        Assert.Contains("does not prove absence", empty.Stories[0].AssessmentReason);
        AddDeliveryCode(f, "existing-admin", "archive.service.ts", "export class Archive { getDocumentVersion() { return this.azureStorage.versionId; } }");
        service = DeliveryService(f, new DeliveryGeneration { Mode = "missing-evidence" });
        var rejected = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Contains("without supporting code references", Assert.Single(rejected.Stories).AssessmentReason);
    }

    [Fact]
    public void DeliveryPlanningDecisionPersistsWithoutModelOrChangingOtherAnswersAndBecomesStaleOnCodeChange()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var file = AddDeliveryCode(f, "backend", "archive.service.ts", "export class Archive { getDocumentVersion() { return this.azureStorage.versionId; } }");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals");
        wizard = service.SaveWizard(f.Authority, new("referrals", "business", new() { ["summary"] = "Preserve this business review." }, "Reviewer", wizard.Revision!));
        var assessment = service.Delivery(f.Authority, "referrals", false);
        var request = new CisFeatureDeliveryReviewRequest(assessment.Stories[0].Id, "extend", ["backend"],
            "Reuse document version retrieval. Add referral acknowledgement and version recording.", ["backend/archive.service.ts"], assessment.InputHash!);
        var save = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = request });
        Assert.Empty(save.Errors); Assert.True(save.Applied); Assert.False(save.Reviewed);
        Assert.Equal("Preserve this business review.", save.Pages.Single(p => p.Id == "business").Fields[0].Answer);
        Assert.False(save.Pages.Single(p => p.Id == "delivery").Complete);
        var reopened = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.True(reopened.Stories[0].ReviewCurrent);
        Assert.Equal("extend", reopened.Stories[0].Review!.Treatment);
        Assert.Equal("Reviewer", reopened.Stories[0].Review!.Actor);
        Assert.Contains("saved planning decision", reopened.SuggestedAnswers["delivery-stories-foundation"]);
        Assert.Equal(0, generation.Calls);
        File.AppendAllText(file, "\nexport const versionChanged = true;\n");
        var stale = service.Delivery(f.Authority, "referrals", false);
        Assert.False(stale.Stories[0].ReviewCurrent);
        Assert.Equal(request.Plan, stale.Stories[0].Review!.Plan);
        Assert.NotEmpty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", save.Revision!) { DeliveryReview = request }).Errors);
    }

    [Fact]
    public void DeliveryNewWorkCanBeRecordedWithoutCodeButReuseRequiresOwnedSafeEvidence()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory); CreateIntake(f);
        var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var assessment = service.Delivery(f.Authority, "referrals", false);
        var request = new CisFeatureDeliveryReviewRequest(assessment.Stories[0].Id, "reuse", [assessment.FeatureRepositoryId!], "Implement the privacy evidence requirements.", [], assessment.InputHash!);
        CisFeatureWizardResult Save(CisFeatureDeliveryReviewRequest choice, string? revision = null) => service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Reviewer", revision ?? wizard.Revision!) { DeliveryReview = choice });
        Assert.NotEmpty(Save(request).Errors);
        Assert.NotEmpty(Save(request with { EvidencePaths = [assessment.FeatureRepositoryId + "/../../outside.ts"] }).Errors);
        Assert.NotEmpty(Save(request with { Owners = ["unregistered"], Treatment = "new" }).Errors);
        var saved = Save(request with { Treatment = "new", Owners = [] });
        Assert.Empty(saved.Errors);
        Assert.NotEmpty(Save(request with { Treatment = "new" }).Errors); // Stale wizard revision cannot overwrite.
        var result = service.Delivery(f.Authority, "referrals", false);
        Assert.True(result.Stories[0].ReviewCurrent); Assert.Equal("new", result.Stories[0].Review!.Treatment);
        Assert.Empty(result.Stories[0].Review!.Owners);
        Assert.Empty(Assert.Single(Assert.Single(service.Navigation(f.Authority).Features).Stories).RepositoryIds);
        Assert.Equal("unresolved", result.Stories[0].Treatment); // Human planning choice never becomes a code finding.
    }

    [Fact]
    public void DeliveryDecisionsKeepTheirBasisAcrossUnrelatedPageCodeAndTimestampChanges()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var file = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var unrelated = Path.Combine(Path.GetDirectoryName(file)!, "weather.ts");
        File.WriteAllText(unrelated, "export class Weather { getForecast() {} }");
        CreateIntake(f); var generation = new DeliveryGeneration(); var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Retain document versions and add acknowledgement storage.", ["backend/archive.ts"], delivery.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var before = service.Delivery(f.Authority, "referrals", false).Stories.Single().Review!;
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddMinutes(1));
        File.AppendAllText(unrelated, "\nexport const forecastDays = 7;");
        saved = service.SaveWizard(f.Authority, new("referrals", "business", new() { ["summary"] = "Change the billing report's visual presentation." }, "Reviewer", saved.Revision!));
        Assert.Empty(saved.Errors);
        var after = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(after.ReviewCurrent); Assert.Equal("current", after.ReviewState);
        Assert.Equal(before.Actor, after.Review!.Actor); Assert.Equal(before.SavedAt, after.Review.SavedAt);
        Assert.Equal(before.Plan, after.Review.Plan); Assert.Equal(0, generation.Calls);
        File.AppendAllText(file, "\nexport const documentVersionChanged = true;");
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Contains("implementation/backend/archive.ts", changed.ReviewReason);
        Assert.Equal(before.SavedAt, changed.Review!.SavedAt);
    }

    [Fact]
    public void DeliveryBasisSearchDetectsSameLengthSameTimestampNewRelevantContentAndDeletedDependencies()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var file = AddDeliveryCode(f, "backend", "archive.ts", "import { Retention } from './retention';\nexport class Archive { getDocumentVersion() { return Retention.days; } }");
        var root = Path.GetDirectoryName(file)!;
        var dependency = Path.Combine(root, "retention.ts"); File.WriteAllText(dependency, "export class Retention { static days = 7; }");
        var lead = Path.Combine(root, "spare.ts");
        const string before = "export class Spare { getWeatherForecast() {} }";
        const string after = "export class Spare { getDocumentVersion() {} }";
        Assert.Equal(before.Length, after.Length); File.WriteAllText(lead, before);
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Add acknowledgement storage using the archive retention policy.", ["backend/archive.ts"], delivery.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors); Assert.True(service.Delivery(f.Authority, "referrals", false).Stories.Single().ReviewCurrent);
        var timestamp = File.GetLastWriteTimeUtc(lead); File.WriteAllText(lead, after); File.SetLastWriteTimeUtc(lead, timestamp);
        var changed = service.Delivery(f.Authority, "referrals", false);
        Assert.False(changed.Stories.Single().ReviewCurrent); Assert.Contains("spare.ts", changed.Stories.Single().ReviewReason);
        Assert.Contains(changed.Evidence, evidence => evidence.Path == "spare.ts");
        File.WriteAllText(lead, before); File.SetLastWriteTimeUtc(lead, timestamp);
        Assert.True(service.Delivery(f.Authority, "referrals", false).Stories.Single().ReviewCurrent);
        File.Delete(dependency);
        var missing = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(missing.ReviewCurrent); Assert.Equal("needs-review", missing.ReviewState);
        Assert.Equal("unknown", missing.AssessmentState);
        Assert.Contains("dependency", missing.ReviewReason);
    }

    [Fact]
    public void DeliveryReviewIncludesRelevantCodeBeyondSelectedExcerptsAndRetainsUnchangedStory()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n### Email notices\n| ID | Requirement |\n|---|---|\n| NTC-001 | The application shall send an email notice. |\n");
        var archive = AddDeliveryCode(f, "backend", "a.ts", "export class Archive { getDocumentVersion() {} }");
        var root = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(root, "b.ts"), "export class Backup { getDocumentVersion() {} }");
        var unselected = Path.Combine(root, "z.ts"); File.WriteAllText(unselected, "export class Secondary { getDocumentVersion() {} }");
        File.WriteAllText(Path.Combine(root, "notice.ts"), "export class Mailer { sendEmailNotice() {} }");
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        foreach (var title in new[] { "Privacy evidence", "Email notices" })
        {
            var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
            var story = delivery.Stories.Single(story => story.Title == title);
            var path = title == "Privacy evidence" ? "a.ts" : "notice.ts";
            var choice = new CisFeatureDeliveryReviewRequest(story.Id, "extend", ["backend"], "Review and extend " + title + ".", ["backend/" + path], delivery.InputHash!);
            Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        }
        Assert.DoesNotContain(service.Delivery(f.Authority, "referrals", false).Evidence, evidence => evidence.Path == "z.ts");
        File.AppendAllText(unselected, "\nexport const versionRetentionDays = 90;");
        var changed = service.Delivery(f.Authority, "referrals", false);
        Assert.False(changed.Stories.Single(story => story.Title == "Privacy evidence").ReviewCurrent);
        Assert.True(changed.Stories.Single(story => story.Title == "Email notices").ReviewCurrent);
        File.WriteAllText(Path.Combine(root, "untracked.ts"), "export class FurtherArchive { getDocumentVersion() {} }");
        Assert.False(service.Delivery(f.Authority, "referrals", false).Stories.Single(story => story.Title == "Privacy evidence").ReviewCurrent);
    }

    [Fact]
    public void DeliveryGraphDependencyContentMattersWhileEquivalentBuildIdentityDoesNot()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var root = Path.GetDirectoryName(archive)!;
        var contract = Path.Combine(root, "retention-contract.md"); File.WriteAllText(contract, "Retain versioned documents for seven years.");
        CreateIntake(f); var graph = new DeliveryDependencyGraph(root, archive, contract);
        var service = DeliveryService(f, new DeliveryGeneration(), graphReader: graph);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Use the document retention contract when adding acknowledgement storage.", ["backend/archive.ts"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var before = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(before.ReviewCurrent);
        graph.BuildId = "equivalent-new-build";
        var after = DeliveryService(f, new DeliveryGeneration(), graphReader: graph).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(after.ReviewCurrent); Assert.Equal(before.Review!.SavedAt, after.Review!.SavedAt);
        graph.Freshness = "stale"; // Unrelated graph input does not invalidate this closed dependency scope.
        Assert.True(service.Delivery(f.Authority, "referrals", false).Stories.Single().ReviewCurrent);
        File.WriteAllText(contract, "Retain versioned documents for thirty days.");
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Equal("unknown", changed.ReviewState);
        Assert.Contains("dependency changed", changed.ReviewReason); Assert.Equal(before.Review.Plan, changed.Review!.Plan);
    }

    [Fact]
    public void DeliveryAssessmentSurvivesInventoryRefreshButChangesWithApplicableConfiguration()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var root = Path.GetDirectoryName(archive)!;
        var configuration = Path.Combine(root, "package.json"); File.WriteAllText(configuration, "{\"dependencies\":{\"archive-sdk\":\"1.0.0\"}}");
        CreateIntake(f); var generation = new DeliveryGeneration { Mode = "document-grounded" }; var service = DeliveryService(f, generation);
        var result = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(result.Errors); Assert.Equal("proposed", result.Stories.Single().AssessmentState);
        File.WriteAllText(Path.Combine(root, "weather.ts"), "export class Weather { getForecast() {} }");
        var same = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.Equal("current", same.Status); Assert.True(same.Cached);
        Assert.Equal(result.Stories.Single().Treatment, same.Stories.Single().Treatment); Assert.Equal(1, generation.Calls);
        var repeated = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Equal("current", repeated.Status); Assert.Equal(1, generation.Calls);
        File.WriteAllText(configuration, "{\"dependencies\":{\"archive-sdk\":\"2.0.0\"}}");
        var changed = service.Delivery(f.Authority, "referrals", false);
        Assert.Equal("stale", changed.Status); Assert.Contains("configuration/backend/package.json", changed.Stories.Single().AssessmentReason);
        Assert.Equal(1, generation.Calls);
    }

    [Fact]
    public void BoundedUnknownAssessmentSurvivesUnrelatedInventoryRefreshAndPrepareWithoutAnotherModelCall()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var root = Path.GetDirectoryName(archive)!;
        var omitted = Path.Combine(root, "unsearched.ts"); File.WriteAllText(omitted, new string('x', 262_145));
        CreateIntake(f); var generation = new DeliveryGeneration { Mode = "document-grounded" }; var service = DeliveryService(f, generation);
        var revision = service.Wizard(f.Authority, "referrals").Revision;
        var prepared = service.Delivery(f.Authority, "referrals", true, revision);
        Assert.Empty(prepared.Errors); Assert.Equal("unknown", prepared.Stories.Single().AssessmentState);
        Assert.Equal("extend", prepared.Stories.Single().Treatment); Assert.Equal(1, generation.Calls);
        File.WriteAllText(Path.Combine(root, "weather.ts"), "export class Weather { getForecast() {} }");
        var same = service.Delivery(f.Authority, "referrals", false);
        Assert.Equal("current", same.Status); Assert.True(same.Cached);
        Assert.Equal(prepared.Stories.Single().ExistingCapability, same.Stories.Single().ExistingCapability);
        Assert.Equal("unknown", same.Stories.Single().AssessmentState); Assert.Contains("256 KiB", same.Stories.Single().AssessmentReason);
        var repeated = service.Delivery(f.Authority, "referrals", true, revision);
        Assert.Empty(repeated.Errors); Assert.True(repeated.Cached); Assert.Equal(1, generation.Calls);
        Assert.DoesNotContain(repeated.Warnings, warning => warning.Contains("flagged scope conflicts", StringComparison.Ordinal));
        File.AppendAllText(omitted, "changed");
        var changed = service.Delivery(f.Authority, "referrals", false);
        Assert.Equal("stale", changed.Status); Assert.False(changed.Cached);
        Assert.NotEqual(prepared.Stories.Single().ExistingCapability, changed.Stories.Single().ExistingCapability);
        var renewed = service.Delivery(f.Authority, "referrals", true, revision);
        Assert.Empty(renewed.Errors); Assert.Equal(2, generation.Calls); Assert.Equal("unknown", renewed.Stories.Single().AssessmentState);
    }

    [Fact]
    public void DeliveryPrepareRegeneratesOnlyTheChangedStoryAndRetainsOtherAssessmentProvenance()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n### Email notices\n| ID | Requirement |\n|---|---|\n| NTC-001 | The application shall send an email notice. |\n");
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(archive)!, "notice.ts"), "export class Mailer { sendEmailNotice() {} }");
        CreateIntake(f); var initial = new DeliveryGeneration { Mode = "document-grounded" }; var service = DeliveryService(f, initial);
        var revision = service.Wizard(f.Authority, "referrals").Revision;
        var prepared = service.Delivery(f.Authority, "referrals", true, revision);
        Assert.Empty(prepared.Errors); Assert.Equal(2, initial.Calls);
        var previous = prepared.Stories.Single(story => story.Title == "Email notices");
        File.AppendAllText(archive, "\nexport const documentVersionChanged = true;");
        var refresh = new DeliveryGeneration { Mode = "document-grounded", ProviderName = "second-local", ModelName = "second-model" };
        service = DeliveryService(f, refresh);
        var updated = service.Delivery(f.Authority, "referrals", true, revision);
        Assert.Empty(updated.Errors); Assert.Equal(1, refresh.Calls);
        var retained = updated.Stories.Single(story => story.Id == previous.Id);
        Assert.Equal(previous.ExistingCapability, retained.ExistingCapability); Assert.Equal(previous.RemainingWork, retained.RemainingWork);
        Assert.Equal(previous.AssessmentProvider, retained.AssessmentProvider); Assert.Equal(previous.AssessmentModel, retained.AssessmentModel);
        Assert.Equal("second-local", updated.Stories.Single(story => story.Title == "Privacy evidence").AssessmentProvider);
        Assert.Equal("second-model", updated.Stories.Single(story => story.Title == "Privacy evidence").AssessmentModel);
    }

    [Fact]
    public void StreamedSourceBeyondFortyMiBKeepsUnrelatedChangesLocalAndDetectsLateRelevantCodeAndDependencies()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "zArchive.ts", "import { Policy } from './zPolicy';\nexport class Archive { getDocumentVersion() { return Policy.days; } }");
        var root = Path.GetDirectoryName(archive)!;
        for (var index = 0; index < 160; index++)
            File.WriteAllText(Path.Combine(root, $"a{index:000}.ts"), "/*" + new string('x', 261_996) + "*/");
        File.WriteAllText(Path.Combine(root, "a160.ts"), "/*" + new string('x', 22_996) + "*/");
        var policy = Path.Combine(root, "zPolicy.ts"); File.WriteAllText(policy, "export class Policy { static days = 7; }");
        var weather = Path.Combine(root, "zWeather.ts"); File.WriteAllText(weather, "export class Weather { getForecast() {} }");
        CreateIntake(f); var generation = new DeliveryGeneration { Mode = "document-grounded" }; var service = DeliveryService(f, generation);
        var wizard = service.Wizard(f.Authority, "referrals");
        var prepared = service.Delivery(f.Authority, "referrals", true, wizard.Revision);
        Assert.Empty(prepared.Errors); Assert.Contains(prepared.Evidence, item => item.Path == "zArchive.ts");
        var choice = new CisFeatureDeliveryReviewRequest(prepared.Stories.Single().Id, "extend", ["backend"],
            "Extend acknowledgement using the archive and retention policy.", ["backend/zArchive.ts"], prepared.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        File.AppendAllText(weather, "\nexport const forecastDays = 10;");
        File.AppendAllText(Path.Combine(root, "a000.ts"), new string(' ', 100));
        var unchanged = service.Delivery(f.Authority, "referrals", true, saved.Revision);
        Assert.Empty(unchanged.Errors); Assert.True(unchanged.Cached); Assert.True(unchanged.Stories.Single().ReviewCurrent);
        Assert.Equal(1, generation.Calls); Assert.NotEqual("unknown", unchanged.Stories.Single().AssessmentState);
        File.WriteAllText(policy, "export class Policy { static days = 9; }");
        var dependencyChanged = service.Delivery(f.Authority, "referrals", false);
        Assert.False(dependencyChanged.Stories.Single().ReviewCurrent);
        Assert.Equal("needs-review", dependencyChanged.Stories.Single().ReviewState); Assert.Contains("zPolicy.ts", dependencyChanged.Stories.Single().ReviewReason);
        File.WriteAllText(policy, "export class Policy { static days = 7; }");
        File.WriteAllText(Path.Combine(root, "zNew.ts"), "export class NewArchive { getDocumentVersion() {} }");
        var newRelevant = service.Delivery(f.Authority, "referrals", false);
        Assert.False(newRelevant.Stories.Single().ReviewCurrent); Assert.Contains("zNew.ts", newRelevant.Stories.Single().ReviewReason);
    }

    [Theory]
    [InlineData("packages.lock.json")]
    [InlineData("NuGet.Config")]
    public void DeliveryNativeDependencyBasisIncludesDotNetLockAndNugetConfiguration(string name)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.cs", "public class Archive { public int getDocumentVersion() => 1; }");
        var configuration = Path.Combine(Path.GetDirectoryName(archive)!, name); File.WriteAllText(configuration, "initial resolved package contract");
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"], "Extend the version archive.", ["backend/archive.cs"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        File.WriteAllText(configuration, "changed resolved package contract");
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Contains("configuration/backend/" + name, changed.ReviewReason);
    }

    [Fact]
    public void DeliveryGraphHeadingKeepsUnchangedContractAcrossUnrelatedDocumentSectionRebuild()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var root = Path.GetDirectoryName(archive)!;
        var contract = Path.Combine(root, "retention-contract.md");
        File.WriteAllText(contract, "# Retention\nRetain versioned documents for seven years.\n# Notices\nSend email.\n");
        CreateIntake(f); var graph = new DeliveryDependencyGraph(root, archive, contract, "Retention");
        var service = DeliveryService(f, new DeliveryGeneration(), graphReader: graph);
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Use the retention policy when adding acknowledgement storage.", ["backend/archive.ts"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        File.WriteAllText(contract, "# Retention\nRetain versioned documents for seven years.\n# Notices\nSend push notifications.\n");
        graph.Rebuild();
        Assert.True(service.Delivery(f.Authority, "referrals", false).Stories.Single().ReviewCurrent);
        File.WriteAllText(contract, "# Retention\nRetain versioned documents for thirty days.\n# Notices\nSend push notifications.\n");
        graph.Rebuild();
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Contains("heading:Retention", changed.ReviewReason);
    }

    [Fact]
    public void DeliveryOwnerScopeChangesInvalidateWhileComponentOrderAndDisplayMetadataDoNot()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var workspace = f.Registry.Resolve(f.Authority).Workspace!;
        void Register(string[] components, string name)
        {
            var currentWorkspace = f.Registry.Resolve(f.Authority).Workspace!;
            File.WriteAllText(workspace.ConfigurationPath, JsonSerializer.Serialize(new
            {
                schema_version = 2,
                ecosystem = new { id = workspace.Ecosystem!.Id, name = workspace.Ecosystem.Name },
                product = new { id = workspace.Product!.Id, name },
                repositories = currentWorkspace.Repositories.Select(repository => new
                {
                    id = repository.Id,
                    path = repository.RepositoryPath,
                    documentation_root = repository.DocumentationRoot,
                    role = repository.Role,
                    participation = repository.Participation,
                    relationship = repository.Relationship,
                    components = repository.Id == "backend" ? components : repository.Components
                })
            }));
        }
        Register(["archive", "retention"], workspace.Product!.Name);
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Use the registered archive and retention authority.", ["backend/archive.ts"], delivery.InputHash!);
        Assert.Empty(service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice }).Errors);
        var before = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Register(["retention", "archive"], "Updated display label");
        var unchanged = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(unchanged.ReviewCurrent); Assert.Equal(before.Review!.SavedAt, unchanged.Review!.SavedAt);
        Register(["archive"], "Updated display label");
        var changed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.False(changed.ReviewCurrent); Assert.Contains("owner/backend", changed.ReviewReason);
        Assert.Equal(before.Review.SavedAt, changed.Review!.SavedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedOmittedImplementationKeepsHumanDecisionAndNoopUntilItsActualContentChanges(bool sensitive)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        var omitted = Path.Combine(Path.GetDirectoryName(archive)!, "unsearched.ts");
        File.WriteAllText(omitted, sensitive ? "export const api_key = 'synthetic-test-credential-value';" : new string('x', 262_145));
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Retain archive ownership and inspect unsearched implementation before delivery completion.", ["backend/archive.ts"], delivery.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var reviewed = service.Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(reviewed.ReviewCurrent); Assert.Equal("unknown", reviewed.AssessmentState);
        Assert.Contains("coverage remains uncertain", reviewed.ReviewReason);
        var same = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", saved.Revision!)
        { DeliveryReview = choice with { ExpectedInputHash = service.Delivery(f.Authority, "referrals", false).InputHash! } });
        Assert.Empty(same.Errors); Assert.False(same.Applied); Assert.Equal(saved.Revision, same.Revision);
        Assert.Equal(reviewed.Review!.SavedAt, service.Delivery(f.Authority, "referrals", false).Stories.Single().Review!.SavedAt);
        var original = File.ReadAllText(archive); File.AppendAllText(archive, "\nexport const documentVersionChanged = true;");
        var implementationChanged = service.Delivery(f.Authority, "referrals", false);
        Assert.False(implementationChanged.Stories.Single().ReviewCurrent); Assert.Equal("needs-review", implementationChanged.Stories.Single().ReviewState);
        Assert.Contains("implementation/backend/archive.ts", implementationChanged.Stories.Single().ReviewReason);
        Assert.Contains(implementationChanged.Warnings, warning => warning.Contains("renew only the affected choice", StringComparison.Ordinal));
        Assert.DoesNotContain(implementationChanged.Warnings, warning => warning.Contains("Resolve the affected story's coverage", StringComparison.Ordinal));
        File.WriteAllText(archive, original);
        var timestamp = File.GetLastWriteTimeUtc(omitted); File.AppendAllText(omitted, "x"); File.SetLastWriteTimeUtc(omitted, timestamp);
        var changed = service.Delivery(f.Authority, "referrals", false);
        Assert.False(changed.Stories.Single().ReviewCurrent); Assert.Contains("unsearched/backend/unsearched.ts", changed.Stories.Single().ReviewReason);
        var renewed = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", same.Revision!)
        { DeliveryReview = choice with { ExpectedInputHash = changed.InputHash! } });
        Assert.Empty(renewed.Errors); Assert.True(renewed.Applied);
        var reopened = DeliveryService(f, new DeliveryGeneration()).Delivery(f.Authority, "referrals", false).Stories.Single();
        Assert.True(reopened.ReviewCurrent); Assert.Equal("unknown", reopened.AssessmentState);
    }

    [Fact]
    public void LegacyDecisionRequiresExactContentBoundTokenDespiteUnchangedSelectedEvidenceAndFileMetadata()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "a.ts", "export class Archive { getDocumentVersion() {} }");
        var root = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(root, "b.ts"), "export class Backup { getDocumentVersion() {} }");
        var unselected = Path.Combine(root, "z.ts");
        const string previous = "export class Spare { getDocumentVersion() { return 7; } }";
        const string changed = "export class Spare { getDocumentVersion() { return 8; } }";
        File.WriteAllText(unselected, previous);
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration());
        var wizard = service.Wizard(f.Authority, "referrals"); var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.DoesNotContain(delivery.Evidence, item => item.Path == "z.ts");
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Retain the archive owner and verify every document-retention operation.", ["backend/a.ts"], delivery.InputHash!);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.Empty(saved.Errors);
        var request = Path.Combine(f.Authority, saved.Plan!.RequestPath);
        var content = File.ReadAllText(request); const string marker = "<!-- cis:feature-review-data\n";
        var start = content.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = content.IndexOf("\n-->", start, StringComparison.Ordinal);
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(content[start..end])!;
        legacy["deliveryReviews"]![0]!.AsObject().Remove("basis");
        File.WriteAllText(request, content[..start] + legacy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + content[end..]);
        var compatible = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(compatible.Errors);
        Assert.True(compatible.Stories.Single().ReviewCurrent); Assert.Null(compatible.Stories.Single().Review!.Basis);
        var timestamp = File.GetLastWriteTimeUtc(unselected); Assert.Equal(previous.Length, changed.Length);
        File.WriteAllText(unselected, changed); File.SetLastWriteTimeUtc(unselected, timestamp);
        var stale = DeliveryService(f, new DeliveryGeneration()).Delivery(f.Authority, "referrals", false);
        Assert.Equal(compatible.Evidence.Select(item => item.ContentHash), stale.Evidence.Select(item => item.ContentHash));
        Assert.DoesNotContain(stale.Evidence, item => item.Path == "z.ts");
        Assert.False(stale.Stories.Single().ReviewCurrent); Assert.Equal("needs-review", stale.Stories.Single().ReviewState);
        Assert.Contains("predates scoped", stale.Stories.Single().ReviewReason);
        Assert.Equal(compatible.Stories.Single().Review!.SavedAt, stale.Stories.Single().Review!.SavedAt);
        Assert.Equal(choice.Plan, stale.Stories.Single().Review!.Plan);
    }

    [Fact]
    public void DeliverySelectedRootAssetChangeDuringFinalSaveCheckRejectsTheDecision()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, PrivacyStory);
        var archive = AddDeliveryCode(f, "backend", "archive.ts", "import './styles.css';\nexport function run() { return 1; }\n");
        var root = Path.GetDirectoryName(archive)!;
        var asset = Path.Combine(root, "styles.css"); File.WriteAllText(asset, ".capture { color: red; }");
        var contract = Path.Combine(root, "retention-contract.md"); File.WriteAllText(contract, "# Contract\nPreserve owned runtime boundaries.\n");
        CreateIntake(f);
        var graph = new DeliveryDependencyGraph(root, archive, contract);
        var generation = new DeliveryGeneration();
        var service = DeliveryService(f, generation, graphReader: graph);
        var wizard = service.Wizard(f.Authority, "referrals");
        var delivery = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(delivery.Evidence);
        var choice = new CisFeatureDeliveryReviewRequest(delivery.Stories.Single().Id, "extend", ["backend"],
            "Extend the reviewed handler while retaining its imported presentation contract.", ["backend/archive.ts"], delivery.InputHash!);
        var requestPath = Path.Combine(f.Authority, wizard.Plan!.RequestPath);
        var original = File.ReadAllBytes(requestPath);
        var reads = 0;
        graph.BeforeRead = () =>
        {
            // The old-input read and prospective-root read have completed.
            // Mutate the asset during the final prewrite validation read.
            if (++reads == 3) File.WriteAllText(asset, ".capture { color: tan; }");
        };
        var rejected = service.SaveWizard(f.Authority,
            new("referrals", "delivery", new(), "Reviewer", wizard.Revision!) { DeliveryReview = choice });
        Assert.True(reads >= 3);
        Assert.NotEmpty(rejected.Errors);
        Assert.Equal(original, File.ReadAllBytes(requestPath));
        var reopened = DeliveryService(f, generation).Delivery(f.Authority, "referrals", false);
        Assert.Empty(reopened.Errors);
        Assert.Null(reopened.Stories.Single().Review);
        Assert.Equal(0, generation.Calls);
    }

    private sealed class DeliveryDependencyGraph : ICisGraphSnapshotReader
    {
        private readonly string root;
        private IReadOnlyList<CisGraphInput> inputs = [];
        private readonly string[] files;
        private readonly string? contractHeading;
        public string BuildId { get; set; } = "initial-build";
        public string Freshness { get; set; } = "fresh";
        public Action? BeforeRead { get; set; }
        public DeliveryDependencyGraph(string root, string archive, string contract, string? contractHeading = null)
        {
            this.root = root; files = [archive, contract]; this.contractHeading = contractHeading; Rebuild();
        }
        public void Rebuild()
        {
            BuildId = Guid.NewGuid().ToString("N");
            inputs = files.Select(path => new CisGraphInput(Path.GetFileName(path),
                "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant())).ToArray();
        }
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            if (!string.Equals(Path.GetFullPath(root), Path.GetFullPath(repositoryPath), StringComparison.OrdinalIgnoreCase))
                return new("missing", 4, repositoryPath, "missing", null, null, [], [], []);
            BeforeRead?.Invoke();
            var build = new CisGraphBuildMetadata(BuildId, "backend", "same-head", true, "complete");
            CisGraphNode Node(string id, string path) => new(id, "backend", "component", "implementation", id, id, [], "discovered", "active",
                [new(path, id == "retention" && contractHeading is not null ? "heading" : "file", id == "retention" ? contractHeading ?? "" : "")], new Dictionary<string, string>(), []);
            var graph = new CisGraphDocument(1, build, [Node("archive", "archive.ts"), Node("retention", "retention-contract.md")],
                [new("archive-retention", "depends-on", "archive", "retention", "discovered", "high", [], new Dictionary<string, string>())], new(0, 0, 0));
            return new("available", 0, repositoryPath, Freshness, graph, new(1, BuildId, "backend", "docs/cis", "same-head", true, inputs, ["test-dependencies"]),
                ["compiler.calls"], [], []);
        }
    }
}
