namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    private const string ClickStory = """
        # Feature
        ## Functional Requirements
        ### Click Tracking
        | ID | Requirement |
        |---|---|
        | CLK-001 | A Product click shall be recorded when a customer selects an eligible Product and enters the Referral journey. |
        | CLK-002 | The click shall have a unique Click ID. |
        | CLK-003 | The click shall reference organisation, Bank, Product, Product revision, catalogue version, channel, timestamp, Visitor ID, source, campaign, placement, and test/live state where available. |
        | CLK-004 | Public Product API retrieval shall not create a click. |
        | CLK-005 | Browser replay and double-click behaviour shall use idempotency or bounded deduplication without suppressing legitimate later selections. |
        | CLK-006 | Bot, test, and suspected invalid-traffic indicators shall be retained where detected. |
        | CLK-007 | A Referral shall reference its originating Click ID where available. |
        | CLK-008 | Click records shall be retained for ten years. |
        """;

    [Fact]
    public void DeliveryDistinguishesProductSelectionHookFromPaymentIdentifiersTestsAndUnrelatedUiClicks()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, ClickStory);
        var main = AddDeliveryCode(f, "customer-ui", "products.component.ts", """
            export class Products {
              onDocumentClick(event) { closeDropdown(event); }
              download() { link.click(); }
              selectProduct(product: Product) {
                const navState = { product };
                this.router.navigate(['/deposit'], { state: navState });
              }
              setSelectedProduct(product: Product) { this.selectedProduct = product; }
            }
            """);
        AddDeliveryCode(f, "payments", "payout.dto.ts", """
            // click tracking product selection and click record retention
            export class PayoutRequest {
              @ApiProperty({ description: 'Unique reference ID for tracking; Product click; selects eligible Product', example: '2026-01-01' })
              timestamp: string;
              productBank: ProductBank;
              mode = OperationMode.REAL_TIME_PRODUCT_BANK_API;
              referenceId: string;
            }
            """);
        AddDeliveryCode(f, "admin-ui", "notification-template.component.ts", """
            export class NotificationTemplate {
              setupSelectionTracking() { this.quill.on('selection-change', () => this.range = true); }
              saveTemplate() { store.save(this.template); }
              onClick() { button.click(); }
            }
            """);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(main)!, "test-product-click.js"), "function selectProduct(product) { page.click(product); }");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(main)!, "product_click_test.py"), "def select_product(product): return click_tracking(product)");
        CreateIntake(f); var generation = new DeliveryGeneration { Mode = "low-confidence" }; var service = DeliveryService(f, generation);
        var result = service.Delivery(f.Authority, "referrals", false);
        Assert.Empty(result.Errors);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("customer-ui", evidence.RepositoryId);
        Assert.Equal("integration-point", evidence.Kind);
        Assert.Equal([1], evidence.RequirementNumbers);
        Assert.Contains("selectProduct", evidence.Summary);
        Assert.Contains("this.router.navigate", evidence.Excerpt);
        Assert.DoesNotContain("onDocumentClick", evidence.Excerpt);
        Assert.DoesNotContain("link.click", evidence.Excerpt);
        Assert.DoesNotContain("setSelectedProduct", evidence.Excerpt);
        var story = Assert.Single(result.Stories);
        Assert.Empty(story.Owners); // Finding a code lead does not assign delivery ownership.
        Assert.Contains("possible integration points", story.ExistingCapability);
        Assert.Equal(8, story.RequirementChecks.Count);
        Assert.Equal([evidence.Id], story.RequirementChecks[0].EvidenceIds);
        Assert.All(story.RequirementChecks.Skip(1), check => Assert.Empty(check.EvidenceIds));
        var cached = service.Delivery(f.Authority, "referrals", false);
        Assert.Equal(evidence.Excerpt, Assert.Single(cached.Evidence).Excerpt);
        Assert.Equal(evidence.Summary, cached.Evidence[0].Summary);
        Assert.Equal(evidence.RequirementNumbers, cached.Evidence[0].RequirementNumbers);
        var assessed = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(assessed.Errors);
        Assert.DoesNotContain("payout", generation.LastPrompt);
        Assert.DoesNotContain("test-product", generation.LastPrompt);
        Assert.Contains("integration-point", generation.LastPrompt);
        Assert.Equal("inconclusive", assessed.Stories[0].AssessmentState);
        Assert.DoesNotContain("Review these requirements:", assessed.Stories[0].RemainingWork);
    }

    [Fact]
    public void DeliveryCannotClaimWholeStoryReuseFromRelatedEntryPointEvenAtHighConfidence()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, ClickStory);
        AddDeliveryCode(f, "existing-admin", "products.component.ts", "export class Products { selectProduct(product) { this.router.navigate(product); } }");
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration { Mode = "reuse" });
        var result = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(result.Errors);
        Assert.Equal("unresolved", Assert.Single(result.Stories).Treatment);
        Assert.Contains("integration point", result.Stories[0].AssessmentReason);
    }

    [Theory]
    [InlineData("hook-overclaim", "unresolved")]
    [InlineData("hook-grounded", "extend")]
    public void DeliveryRejectsClaimThatRelatedNavigationImplementsTheStoryButAllowsNamedOperationProposal(string mode, string expected)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, ClickStory);
        AddDeliveryCode(f, "customer-ui", "products.component.ts", "export class Products { selectProduct(product) { return this.router.navigate(product); } }");
        CreateIntake(f); var service = DeliveryService(f, new DeliveryGeneration { Mode = mode });
        var result = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, Assert.Single(result.Stories).Treatment);
        if (expected == "unresolved") Assert.Contains("CIS rejected that claim", result.Stories[0].AssessmentReason);
    }

    [Fact]
    public void DeliveryFindsDedicatedCapabilityAlongsideRelatedIntegrationHookWithoutCallingItComplete()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, ClickStory);
        AddDeliveryCode(f, "referral-runtime", "click-tracking.service.ts", "export class ClickTracking { recordClick(product) { return this.store.save(product); } }");
        AddDeliveryCode(f, "customer-ui", "products.component.ts", "export class Products { selectProduct(product) { return this.router.navigate(product); } }");
        CreateIntake(f); var result = DeliveryService(f, new DeliveryGeneration()).Delivery(f.Authority, "referrals", false);
        Assert.Contains(result.Evidence, e => e.RepositoryId == "referral-runtime" && e.Kind == "capability-candidate");
        Assert.Contains(result.Evidence, e => e.RepositoryId == "customer-ui" && e.Kind == "integration-point");
        Assert.Equal("unresolved", Assert.Single(result.Stories).Treatment);
    }
}
