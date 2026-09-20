using System.Text.Json;
using Cis.Modules.SolutionDesign;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Theory]
    [InlineData("mixed-context")]
    [InlineData("wrong-container")]
    [InlineData("cross-container-component")]
    [InlineData("identity-drift")]
    [InlineData("missing-technology")]
    [InlineData("missing-description")]
    [InlineData("boundary-layout")]
    [InlineData("disconnected")]
    [InlineData("null-view")]
    [InlineData("null-node")]
    [InlineData("null-edge")]
    [InlineData("unknown-schema")]
    public void C4_RejectsInvalidScopeAndIncompleteModels(string failure)
    {
        var model = C4Model(); var views = model.Views.ToArray();
        switch (failure)
        {
            case "mixed-context": views[0] = views[0] with { Nodes = [views[0].Nodes[0], views[1].Nodes[1]] }; break;
            case "wrong-container": views[2] = views[2] with { ScopeId = "product" }; break;
            case "cross-container-component": views[2] = views[2] with { Nodes = views[2].Nodes.Select(n => n.Kind == "component" ? n with { ParentId = "data" } : n).ToArray() }; break;
            case "identity-drift": views[2] = views[2] with { Nodes = views[2].Nodes.Select(n => n.Id == "data" ? n with { Label = "Different database" } : n).ToArray() }; break;
            case "missing-technology": views[1] = views[1] with { Edges = [views[1].Edges[0] with { Technology = null }] }; break;
            case "missing-description": views[0] = views[0] with { Nodes = views[0].Nodes.Select(n => n with { Description = "" }).ToArray() }; break;
            case "boundary-layout": views[2] = views[2] with { Nodes = views[2].Nodes.Select(n => n.Id == "data" ? n with { Layer = 1 } : n).ToArray() }; break;
            case "disconnected": views[1] = views[1] with { Edges = [views[1].Edges[0]] }; break;
            case "null-view": views[0] = null!; break;
            case "null-node": views[0] = views[0] with { Nodes = [null!, views[0].Nodes[1]] }; break;
            case "null-edge": views[0] = views[0] with { Edges = [null!] }; break;
            case "unknown-schema": model = model with { SchemaVersion = 99 }; break;
        }
        var json = JsonSerializer.Serialize(model with { Views = views }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Throws<InvalidDataException>(() => ArchitectureDiagramModel.ReadRequired("<!-- cis:architecture-views\n" + json + "\n-->"));
    }

    [Fact]
    public void C4_RenderCommandEmbedsViewsRepairsAssetsAndPreservesCanonicalContent()
    {
        using var fixture = Fixture.Create(); fixture.Service.Initialize(fixture.Root);
        File.AppendAllText(fixture.DesignPath, "\nHuman note: preserve settlement ownership.\n");
        var original = File.ReadAllText(fixture.DesignPath); var sheet = File.ReadAllText(fixture.SheetPath); var technical = File.ReadAllText(fixture.TechnicalIntentPath);
        var path = Path.Combine(fixture.Root, "c4-model.json");
        File.WriteAllText(path, JsonSerializer.Serialize(C4Model(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var rendered = fixture.Service.RenderDiagrams(fixture.Root, path);
        Assert.Equal(0, rendered.ExitCode); Assert.True(rendered.Applied);
        var next = File.ReadAllText(fixture.DesignPath);
        Assert.Contains("## C4 solution diagrams", next, StringComparison.Ordinal);
        Assert.Contains("![C1 - Implementation &amp; architecture](architecture-diagrams/", next, StringComparison.Ordinal);
        Assert.Contains("Human note: preserve settlement ownership.", next, StringComparison.Ordinal);
        Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath)); Assert.Equal(technical, File.ReadAllText(fixture.TechnicalIntentPath));
        Assert.Equal(original[..original.IndexOf("<!-- cis:solution-design-managed:start -->", StringComparison.Ordinal)], next[..next.IndexOf("<!-- cis:solution-design-managed:start -->", StringComparison.Ordinal)]);
        var image = C4Model().Render()[0]; var asset = Path.Combine(Path.GetDirectoryName(fixture.DesignPath)!, image.RelativePath);
        File.Delete(asset);
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root).ExitCode);
        Assert.Equal(image.Content, File.ReadAllText(asset)); Assert.Equal(next, File.ReadAllText(fixture.DesignPath));
        var applied = fixture.Service.ApplyExistingDraft(fixture.Root, next, sheet, next, sheet);
        Assert.Empty(applied.Errors); // Generated images do not violate the source-citation rule on inference reruns.
    }

    [Theory]
    [InlineData("human-display")]
    [InlineData("svg")]
    [InlineData("active")]
    [InlineData("upstream")]
    public void C4_RenderPreservesModifiedAssetsHumanDisplayAndReviewGates(string failure)
    {
        using var fixture = Fixture.Create(); fixture.Service.Initialize(fixture.Root);
        var path = Path.Combine(fixture.Root, "c4-model.json"); File.WriteAllText(path, JsonSerializer.Serialize(C4Model(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root, path).ExitCode);
        if (failure == "human-display") File.WriteAllText(fixture.DesignPath, File.ReadAllText(fixture.DesignPath).Replace("## C4 solution diagrams", "## My architecture notes", StringComparison.Ordinal));
        if (failure == "svg") File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.DesignPath)!, C4Model().Render()[0].RelativePath), "Human SVG refinement");
        if (failure == "active") fixture.Service.Approve(fixture.Root, "Owner", "Reviewed architecture");
        if (failure == "upstream") File.AppendAllText(fixture.TechnicalIntentPath, "\nChanged technical direction.\n");
        var before = File.ReadAllText(fixture.DesignPath); var sheet = File.ReadAllText(fixture.SheetPath);
        var result = fixture.Service.RenderDiagrams(fixture.Root);
        Assert.NotEqual(0, result.ExitCode); Assert.False(result.Applied);
        Assert.Equal(before, File.ReadAllText(fixture.DesignPath)); Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath));
    }

    [Fact]
    public void C4_ValidationDetectsModelDisplayDriftAndCommandRepairsIt()
    {
        using var fixture = Fixture.Create(); fixture.Service.Initialize(fixture.Root);
        var path = Path.Combine(fixture.Root, "c4-model.json"); File.WriteAllText(path, JsonSerializer.Serialize(C4Model(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root, path).ExitCode);
        var original = File.ReadAllText(fixture.DesignPath);
        File.WriteAllText(fixture.DesignPath, original.Replace("\"label\":\"Submits requests\"", "\"label\":\"Requests product operations\"", StringComparison.Ordinal));
        Assert.False(fixture.Service.Validate(fixture.Root).Validation!.Valid);
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root).ExitCode);
        Assert.True(fixture.Service.Validate(fixture.Root).Validation!.Valid);
    }
}
