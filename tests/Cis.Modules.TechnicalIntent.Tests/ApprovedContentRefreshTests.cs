using System.Text.RegularExpressions;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    [Fact]
    public void Initialize_AmendedReviewedIntentPreservesCustomSectionsAfterApprovalIsCleared()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        var sections = new[] { "business-evidence", "component-map", "module-architecture", "integration-points" };
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        foreach (var section in sections)
        {
            var marker = $"<!-- cis:technical-intent-{section}:end -->";
            content = content.Replace(marker, $"Reviewed {section}: retain this project-specific boundary.\n{marker}", StringComparison.Ordinal);
        }
        File.WriteAllText(environment.TechnicalIntentPath, content);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner", "Reviewed custom direction.").ExitCode);
        File.AppendAllText(environment.TechnicalIntentPath, "\nDocumentary correction awaiting renewed review.\n");
        var standardPath = Path.Combine(environment.Participant.Path, "docs", "cis", "standards", "testing-standard.md");
        File.AppendAllText(standardPath, "\n<!-- renewed assurance evidence -->\n");
        environment.RebuildParticipantGraph();
        Assert.Equal("Active", environment.Brd.Reconcile(environment.Authority.Path).Validation!.EffectiveStatus);
        var amended = File.ReadAllText(environment.TechnicalIntentPath);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var refreshed = environment.Intent.Initialize(environment.Authority.Path);
            Assert.Equal(0, refreshed.ExitCode);
            Assert.NotEqual("Active", refreshed.Validation!.EffectiveStatus);
            var actual = File.ReadAllText(environment.TechnicalIntentPath);
            foreach (var section in sections)
            {
                var pattern = $"(?s)<!-- cis:technical-intent-{section}:start -->.*?<!-- cis:technical-intent-{section}:end -->";
                Assert.Equal(Regex.Match(amended, pattern).Value, Regex.Match(actual, pattern).Value);
            }
            Assert.Contains("Documentary correction awaiting renewed review.", actual, StringComparison.Ordinal);
            Assert.Contains("approved_by: null", actual, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Initialize_EvidenceOnlyRefreshPreservesReviewedTechnicalContent(bool includeImplementation, bool staleRepositoryBaseline)
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, includeImplementation: includeImplementation);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        foreach (var section in new[] { "business-evidence", "component-map", "module-architecture", "integration-points" })
        {
            var marker = $"<!-- cis:technical-intent-{section}:end -->";
            Assert.Contains(marker, content, StringComparison.Ordinal);
            content = content.Replace(marker, $"Reviewed {section} constraint: no external activation or inferred runtime component.\n{marker}", StringComparison.Ordinal);
        }
        File.WriteAllText(environment.TechnicalIntentPath, content);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner",
            "Accept the refined technical boundaries.").ExitCode);
        var approved = File.ReadAllText(environment.TechnicalIntentPath);
        if (!includeImplementation)
        {
            // Authority-only workspaces have no participant repository baseline to drift.
            Assert.DoesNotContain("| repository |", approved, StringComparison.Ordinal);
        }
        if (staleRepositoryBaseline)
        {
            content = Regex.Replace(approved,
                @"(?m)^(\| repository \| [^|]+ \| )sha256:[a-f0-9]{64}( \|)",
                "$1sha256:0000000000000000000000000000000000000000000000000000000000000000$2");
            Assert.NotEqual(approved, content);
            File.WriteAllText(environment.TechnicalIntentPath, content);
        }

        var refreshed = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal(0, refreshed.ExitCode);
        Assert.Equal("Active", refreshed.Validation!.EffectiveStatus);
        var refreshedContent = File.ReadAllText(environment.TechnicalIntentPath);
        foreach (var section in new[] { "business-evidence", "component-map", "module-architecture", "integration-points" })
        {
            Assert.Contains($"Reviewed {section} constraint: no external activation or inferred runtime component.",
                refreshedContent, StringComparison.Ordinal);
        }
        Assert.Equal(approved, refreshedContent);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        Assert.Equal(approved, File.ReadAllText(environment.TechnicalIntentPath));
    }
}
