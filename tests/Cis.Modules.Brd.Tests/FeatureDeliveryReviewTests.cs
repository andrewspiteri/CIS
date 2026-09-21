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
        var saved = Save(request with { Treatment = "new" });
        Assert.Empty(saved.Errors);
        Assert.NotEmpty(Save(request with { Treatment = "new" }).Errors); // Stale wizard revision cannot overwrite.
        var result = service.Delivery(f.Authority, "referrals", false);
        Assert.True(result.Stories[0].ReviewCurrent); Assert.Equal("new", result.Stories[0].Review!.Treatment);
        Assert.Equal("unresolved", result.Stories[0].Treatment); // Human planning choice never becomes a code finding.
    }
}
