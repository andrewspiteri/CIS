using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Abstractions.Tests;

public sealed class TechnicalIntentPresentationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void HiddenProvenanceRoundTripsWithoutHidingDecisionsOrNarrative(string newline)
    {
        var original = "# Architecture\nThe API owns transactions.\n<!-- cis:technical-intent-baseline:start -->\n| app | sha256:original |\n<!-- cis:technical-intent-baseline:end -->\n"
            + "<!-- cis:technical-intent-business-evidence:start -->\n[source](source.md) BRD-SRC-1 literal --> <!-- &lt;\n<!-- cis:technical-intent-business-evidence:end -->\n"
            + "<!-- cis:technical-intent-decision-evidence:start -->\nTI-DEC-001 Open\n<!-- cis:technical-intent-decision-evidence:end -->\n";
        original = original.Replace("\n", newline, StringComparison.Ordinal);
        var hidden = CisTechnicalIntentPresentation.HideManagedEvidence(original);
        var visible = Regex.Replace(hidden, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        Assert.DoesNotContain("sha256:", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("BRD-SRC-", visible, StringComparison.Ordinal);
        Assert.Contains("TI-DEC-001 Open", visible, StringComparison.Ordinal);
        Assert.Contains("The API owns transactions.", visible, StringComparison.Ordinal);
        Assert.Equal(hidden, CisTechnicalIntentPresentation.HideManagedEvidence(hidden));
        Assert.Equal(original, CisTechnicalIntentPresentation.RestoreManagedEvidence(hidden));
    }
}
