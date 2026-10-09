namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void RawPhasedSourcePreservesRequirementsDecisionsAndLinksWithoutAdoptingApprovalClaims()
    {
        using var fixture = new Fixture();
        const string raw = """
            # Synthetic reading replay BRD

            This independent qualification source is not an approved product definition.

            ## Foundation
            - CORE-001 Every reading has a stable identity and UTC event time.

            ## MVP
            - REPLAY-001 Replay 500 readings without duplicate effects; see [AC-001](#acceptance).
            - REPLAY-002 Preserve exact quantity totals after stop/restart; depends on CORE-001.

            ## Post-MVP
            - WEB-001 An authorised analyst inspects persisted replay results; depends on REPLAY-002.
            - EXT-001 External operation requires a later activation decision; depends on WEB-001.

            ## Acceptance
            - AC-001 Complete readback equals all input identities and values, including out-of-order replay.

            ## Source decision claims
            DEC-001 is described as accepted by the source author: use synthetic inputs initially.
            This statement is source evidence, not CIS approval authority.

            ## 19. Open business decisions
            1. What resource budget applies to the later browser phase?
            2. Who may authorise external activation?

            ## References
            [Replay acceptance](#acceptance) remains the common contract across phases.
            """;
        File.WriteAllText(fixture.Request.SourcePath, raw);
        var approvalBefore = File.ReadAllBytes(fixture.ApprovedBacklog);
        var preview = fixture.Service.Import(fixture.Authority, fixture.Request, true, false);
        Assert.Equal(2, preview.Plan!.OpenDecisions.Count);
        var imported = fixture.Service.Import(fixture.Authority, fixture.Request, false, true, preview.Plan.PlanHash);
        Assert.True(imported.Applied, string.Join("; ", imported.Errors));
        Assert.Equal(raw, File.ReadAllText(Path.Combine(fixture.Authority, imported.Plan!.SourcePath)));
        Assert.Equal(approvalBefore, File.ReadAllBytes(fixture.ApprovedBacklog));
        Assert.Contains("status: Draft", File.ReadAllText(Path.Combine(fixture.Authority, imported.Plan.RequestPath)), StringComparison.Ordinal);
        var wizard = fixture.Service.Wizard(fixture.Authority, "referrals");
        var stories = wizard.Pages.Single(page => page.Id == "delivery").Fields
            .Where(field => field.Id.StartsWith("delivery-stories-", StringComparison.Ordinal)).ToDictionary(field => field.Id);
        Assert.All(stories.Values, field => Assert.Null(field.Answer));
        Assert.Contains("CORE-001", stories["delivery-stories-foundation"].SuggestedAnswer, StringComparison.Ordinal);
        Assert.Contains("REPLAY-002", stories["delivery-stories-mvp"].SuggestedAnswer, StringComparison.Ordinal);
        Assert.Contains("WEB-001", stories["delivery-stories-post-mvp"].SuggestedAnswer, StringComparison.Ordinal);
        Assert.Contains("EXT-001", stories["delivery-stories-post-mvp"].SuggestedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("EXT-001", stories["delivery-stories-mvp"].SuggestedAnswer, StringComparison.Ordinal);
        Assert.Equal(approvalBefore, File.ReadAllBytes(fixture.ApprovedBacklog));
    }
}
