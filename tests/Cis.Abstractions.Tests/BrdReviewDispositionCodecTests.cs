using Cis.Abstractions;

namespace Cis.Abstractions.Tests;

public sealed class BrdReviewDispositionCodecTests
{
    [Fact]
    public void RenderParseAndApprovalDigest_RoundTripCanonicalHumanDecisions()
    {
        var finding = new CisBrdReviewFindingDisposition("BRD-REV-001", "major", "testability", "Outcomes",
            "No measure.", "Add a measure.", "accepted", "Andrew Spiteri", "2026-08-30T10:00:00Z", "Required for acceptance.");
        var prepared = new CisBrdReviewDispositionDocument(1, "RUN-1", "result-sha", "brd-sha", "claude",
            "2026-08-30T09:00:00Z", [finding], ApprovedBy: "Andrew Spiteri",
            ApprovedAtUtc: "2026-08-30T10:01:00Z", ApprovalReason: "The bounded remediation is accepted.");
        var approved = prepared with { ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(prepared) };

        var markdown = CisBrdReviewDispositionCodec.Render(approved);

        Assert.True(CisBrdReviewDispositionCodec.TryParse(markdown, out var parsed, out var error), error);
        Assert.True(CisBrdReviewDispositionCodec.IsApproved(parsed!));
        Assert.Contains("Human dispositions", markdown, StringComparison.Ordinal);
        Assert.Equal("accepted", parsed!.Findings.Single().Decision);
    }

    [Fact]
    public void ApprovalBecomesInvalidWhenAnyHumanDecisionChanges()
    {
        var finding = new CisBrdReviewFindingDisposition("BRD-REV-001", "major", "scope", "Scope",
            "Ambiguous.", "Clarify.", "accepted", "Andrew", "2026-08-30T10:00:00Z", "Accept.");
        var prepared = new CisBrdReviewDispositionDocument(1, "RUN-1", "result-sha", "brd-sha", "claude",
            "2026-08-30T09:00:00Z", [finding], "Andrew", "2026-08-30T10:01:00Z", "Approved");
        var approved = prepared with { ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(prepared) };

        var changed = approved with { Findings = [finding with { Decision = "rejected" }] };

        Assert.False(CisBrdReviewDispositionCodec.IsApproved(changed));
    }

    [Fact]
    public void ExactRecommendationApproval_NeedsNoFreeTextRationaleAndBindsHumanEdits()
    {
        var finding = new CisBrdReviewFindingDisposition("BRD-REV-001", "major", "scope", "Scope",
            "Ambiguous.", "Clarify the scope.", "accepted", "Andrew", "2026-08-30T10:00:00Z",
            ApprovedRecommendation: "Clarify the launch scope and name its owner.");
        var prepared = new CisBrdReviewDispositionDocument(2, "RUN-1", "result-sha", "brd-sha", "claude",
            "2026-08-30T09:00:00Z", [finding], "Andrew", "2026-08-30T10:01:00Z");
        var approved = prepared with { ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(prepared) };

        Assert.True(CisBrdReviewDispositionCodec.IsApproved(approved));
        Assert.False(CisBrdReviewDispositionCodec.IsApproved(approved with
        {
            Findings = [finding with { ApprovedRecommendation = "Different remediation." }]
        }));
        Assert.Contains("Approved recommendation: Clarify the launch scope and name its owner.",
            CisBrdReviewDispositionCodec.Render(approved), StringComparison.Ordinal);
    }
}
