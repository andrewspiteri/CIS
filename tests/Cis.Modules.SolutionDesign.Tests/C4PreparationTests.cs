using System.Text.Json;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Initialize_PreservesExplicitC4BundleAndRequiresReconciliationAfterSourceDrift(bool approved)
    {
        using var fixture = Fixture.Create();
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        var model = Path.Combine(fixture.Root, "c4-model.json");
        File.WriteAllText(model, JsonSerializer.Serialize(C4Model(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root, model).ExitCode);
        File.AppendAllText(fixture.SheetPath, "\nHuman ownership note: keep the existing boundary.\n");
        if (approved) Assert.Equal(0, fixture.Service.Approve(fixture.Root, "Owner", "Reviewed explicit C4 model").ExitCode);
        var design = File.ReadAllText(fixture.DesignPath);
        var sheet = File.ReadAllText(fixture.SheetPath);
        Assert.DoesNotContain("cis:solution-design-implementation-authored", design, StringComparison.Ordinal);

        var unchanged = fixture.Service.Initialize(fixture.Root);

        Assert.Equal(0, unchanged.ExitCode);
        Assert.False(unchanged.Applied);
        Assert.Equal(approved ? "Active" : "Ready for Approval", unchanged.Validation!.EffectiveStatus);
        Assert.Equal(design, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath));
        if (approved)
        {
            Assert.NotEmpty(fixture.Service.PrepareExistingDraft(fixture.Root).Errors);
            Assert.Equal(design, File.ReadAllText(fixture.DesignPath));
            Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath));
        }

        File.AppendAllText(fixture.TechnicalIntentPath, "\nA changed runtime boundary requires review.\n");
        var stale = fixture.Service.Initialize(fixture.Root);

        Assert.False(stale.Validation!.Current);
        Assert.True(stale.Validation.InferenceReconciliationRequired);
        Assert.Equal(design, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath));
        Assert.NotEqual(0, fixture.Service.Approve(fixture.Root, "Owner", "Cannot approve stale diagrams").ExitCode);

        var reopened = fixture.Service.PrepareExistingDraft(fixture.Root);

        Assert.Empty(reopened.Errors);
        Assert.True(reopened.Applied);
        var pending = fixture.Service.Validate(fixture.Root);
        Assert.True(pending.Validation!.Valid);
        Assert.True(pending.Validation.InferenceReconciliationRequired);
        Assert.False(pending.Validation.Current);
        Assert.Equal("Review Required", pending.Validation.DesignStatus);
        foreach (var path in new[] { fixture.DesignPath, fixture.SheetPath })
        {
            var content = File.ReadAllText(path);
            Assert.Contains("cis:solution-design-reconciliation-pending", content, StringComparison.Ordinal);
            Assert.DoesNotContain("cis:solution-design-implementation-authored", content, StringComparison.Ordinal);
            Assert.DoesNotContain("approved_by: \"Owner\"", content, StringComparison.Ordinal);
        }
        Assert.Contains("## C4 solution diagrams", File.ReadAllText(fixture.DesignPath), StringComparison.Ordinal);
        Assert.Contains("Human ownership note", File.ReadAllText(fixture.SheetPath), StringComparison.Ordinal);
        Assert.NotEqual(0, fixture.Service.Approve(fixture.Root, "Owner", "Cannot approve pending reconciliation").ExitCode);
    }

    [Fact]
    public void Initialize_DoesNotEraseInvalidExplicitC4DisplayToMakeValidationPass()
    {
        using var fixture = Fixture.Create();
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        var model = Path.Combine(fixture.Root, "c4-model.json");
        File.WriteAllText(model, JsonSerializer.Serialize(C4Model(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root, model).ExitCode);
        File.WriteAllText(fixture.DesignPath, File.ReadAllText(fixture.DesignPath)
            .Replace("## C4 solution diagrams", "## Human modified diagram content", StringComparison.Ordinal));
        var design = File.ReadAllText(fixture.DesignPath);
        var sheet = File.ReadAllText(fixture.SheetPath);

        var result = fixture.Service.Initialize(fixture.Root);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Validation!.Valid);
        Assert.Equal(design, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath));
    }
}
