using Cis.Abstractions;
using Xunit;

namespace Cis.Abstractions.Tests;

public sealed class CisFrontMatterTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MetadataEditsPreserveBodyAndOtherYamlMaps(string newline)
    {
        var header = "---\nstatus: Draft\ncis:\n  stable_id: fixture\nother:\n  approved_by: unrelated\n---\n".Replace("\n", newline);
        var body = "# History\n```yaml\nstatus: Draft\n  approved_by: historical\n```\n> approved_by: quoted\n".Replace("\n", newline);
        var updated = CisFrontMatter.Set(header + body, "approved_by", "\"Owner $1\"", nested: true);
        updated = CisFrontMatter.Set(updated, "status", "Active");
        Assert.EndsWith(body, updated);
        Assert.Contains("  approved_by: unrelated", updated);
        Assert.Equal("\"Owner $1\"", CisFrontMatter.Read(updated, "approved_by", nested: true));
        Assert.Equal(updated, CisFrontMatter.Set(updated, "approved_by", "\"Owner $1\"", nested: true));
        Assert.EndsWith(body, CisFrontMatter.NormalizeApproval(updated));
        var removed = CisFrontMatter.Remove(updated, "approved_by", nested: true);
        Assert.EndsWith(body, removed);
        Assert.Contains("  approved_by: unrelated", removed);
        Assert.Null(CisFrontMatter.Read(removed, "approved_by", nested: true));
    }

    [Fact]
    public void AbsentFrontMatterDoesNotTreatExamplesAsMetadata()
    {
        const string content = "# Example\n---\nstatus: Draft\n---\n";
        Assert.Equal(content, CisFrontMatter.Set(content, "status", "Active"));
        Assert.Null(CisFrontMatter.Read(content, "status"));
    }

    [Fact]
    public void ApprovalNormalizationDoesNotMaskOtherMapsOrHistoricalExamples()
    {
        const string content = "---\nstatus: Active\ncis:\n  approved_by: owner\nother:\n  approved_by: significant\n---\nstatus: body\n";
        var normalized = CisFrontMatter.NormalizeApproval(content);
        Assert.Contains("  approved_by: <approval-metadata>", normalized);
        Assert.Contains("  approved_by: significant", normalized);
        Assert.EndsWith("status: body\n", normalized);
    }
}
