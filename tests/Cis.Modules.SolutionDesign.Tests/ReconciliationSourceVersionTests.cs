using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Theory]
    [InlineData("fenced-example")]
    [InlineData("hidden-comment")]
    [InlineData("unrelated-mapping")]
    public void SourceVersion_BindsMatchingMetadataKeysOutsideCanonicalApproval(string location)
    {
        using var fixture = Fixture.Create();
        var source = File.ReadAllText(fixture.TechnicalIntentPath);
        source = location switch
        {
            "fenced-example" => source + "\n```yaml\nstatus: enabled\n```\n",
            "hidden-comment" => source + "\n<!-- evidence\n  approval_reason: enabled\n-->\n",
            _ => source.Replace("last_reviewed: 2026-09-02", "last_reviewed: 2026-09-02\nexample:\n  approved_by: enabled", StringComparison.Ordinal),
        };
        File.WriteAllText(fixture.TechnicalIntentPath, source);
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        Assert.Equal(0, fixture.Service.Approve(fixture.Root, "Owner", "Reviewed source").ExitCode);
        var version = fixture.Service.Status(fixture.Root).TechnicalIntentVersion;

        File.WriteAllText(fixture.TechnicalIntentPath, source.Replace("enabled", "disabled", StringComparison.Ordinal));

        var status = fixture.Service.Status(fixture.Root);
        Assert.NotEqual(version, status.TechnicalIntentVersion);
        Assert.False(status.Validation!.Current);
        Assert.Equal(0, Reconcile(fixture).ExitCode);
        Assert.Equal("Review Required", fixture.Service.Status(fixture.Root).Validation!.DesignStatus);
    }

    [Fact]
    public void SourceVersion_RemainsStableWhenOnlyCanonicalApprovalMetadataChanges()
    {
        using var fixture = Fixture.Create();
        var source = File.ReadAllText(fixture.TechnicalIntentPath).Replace("last_reviewed: 2026-09-02",
            "last_reviewed: 2026-09-02\ncis:\n  approved_by: original\n  approved_at: original\n  approval_reason: original\n  approved_content_hash: original", StringComparison.Ordinal);
        File.WriteAllText(fixture.TechnicalIntentPath, source);
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        Assert.Equal(0, fixture.Service.Approve(fixture.Root, "Owner", "Reviewed source").ExitCode);
        var before = fixture.Service.Status(fixture.Root);

        File.WriteAllText(fixture.TechnicalIntentPath, source.Replace("original", "renewed", StringComparison.Ordinal)
            .Replace("last_reviewed: 2026-09-02", "last_reviewed: 2026-10-06", StringComparison.Ordinal));

        var after = fixture.Service.Status(fixture.Root);
        Assert.Equal(before.TechnicalIntentVersion, after.TechnicalIntentVersion);
        Assert.Equal("Active", after.Validation!.EffectiveStatus);
        Assert.True(after.Validation.Current);
    }
}
