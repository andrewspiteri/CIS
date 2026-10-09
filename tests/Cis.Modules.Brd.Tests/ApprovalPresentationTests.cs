using System.Text.RegularExpressions;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void Approve_KeepsParticipantEvidenceHiddenAcrossApprovalRenewal()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);

        foreach (var reason in new[] { "Reviewed initial scope.", "Renew reviewed documentary correction." })
        {
            var result = environment.Service.Approve(environment.Authority.Path, "Product owner", reason);
            Assert.Equal(0, result.ExitCode);
            var content = File.ReadAllText(environment.CanonicalPath);
            var visible = Regex.Replace(content, "<!--.*?-->", "", RegexOptions.Singleline);
            Assert.DoesNotContain("| Repository | Graph build |", visible, StringComparison.Ordinal);
            Assert.Contains("| Repository | Graph build |", CisBrdPresentation.RestoreManagedEvidence(content), StringComparison.Ordinal);
            Assert.Equal("Active", environment.Service.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        }
    }
}
