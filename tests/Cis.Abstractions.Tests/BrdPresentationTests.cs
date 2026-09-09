using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Abstractions.Tests;

public sealed class BrdPresentationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ManagedEvidenceIsHiddenIdempotentlyAndRestoredWithoutChangingValues(string newline)
    {
        var original = """
            # Product

            ## CIS participant baseline

            <!-- cis:baseline:start -->
            | Repository | Graph build | Head | Dirty |
            | app | sha256:original | head | false |
            <!-- cis:baseline:end -->

            ## Source assessment

            <!-- cis:sources:start -->
            | ID | Kind | Repository | Path | Hash | Assessment | Rationale |
            | BRD-SRC-1 | document | app | notes.md | sha256:source | Reference | Read [note](https://example.test/?a=1&b=2); literal --> <!-- and &lt; |
            <!-- cis:sources:end -->

            ## Executive summary

            Customers choose an offering and request a deposit.

            ## Traceability

            <!-- cis:feature-traceability:start -->
            - BRD-SRC-2: approved feature `app/feature.md`.
            <!-- cis:feature-traceability:end -->
            """;
        original = original.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", newline, StringComparison.Ordinal);

        var hidden = CisBrdPresentation.HideManagedEvidence(original);
        var visible = Regex.Replace(hidden, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotContain("BRD-SRC-", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Source assessment", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("CIS participant baseline", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:", visible, StringComparison.Ordinal);
        Assert.Contains("Customers choose an offering and request a deposit.", visible, StringComparison.Ordinal);
        Assert.False(CisBrdPresentation.HasVisibleLinksOrSourceIds(hidden));
        Assert.Equal(hidden, CisBrdPresentation.HideManagedEvidence(hidden));
        Assert.Equal(original, CisBrdPresentation.RestoreManagedEvidence(hidden));
    }

    [Theory]
    [InlineData("Customers choose a deposit. <!-- Evidence: BRD-SRC-1#purchase; [source](src/purchase.ts) -->", false)]
    [InlineData("Customers choose a deposit.\n<!--\nEvidence: https://example.test/rules\n-->", false)]
    [InlineData("Customers choose a deposit [here](product.md).", true)]
    [InlineData("Customers choose a deposit [here][product].", true)]
    [InlineData("[product]: product.md", true)]
    [InlineData("Customers choose a deposit. BRD-SRC-1#purchase", true)]
    [InlineData("<a href='product.md'>Read more</a>", true)]
    [InlineData("See https://example.test/rules", true)]
    [InlineData("See <ftp://example.test/rules>", true)]
    [InlineData("Contact support@example.test", true)]
    public void VisibleReferencesAreDistinguishedFromHiddenEvidence(string markdown, bool expected)
        => Assert.Equal(expected, CisBrdPresentation.HasVisibleLinksOrSourceIds(markdown));
}
