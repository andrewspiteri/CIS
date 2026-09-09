using Cis.Modules.Repository;
using System.Xml.Linq;

namespace Cis.Modules.Repository.Tests;

public sealed class ErdDiagramTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Renderer_PreservesRowsProducesPassiveSvgAndIsIdempotent(string newline)
    {
        var table = "# ERD\n\nHuman interpretation stays here.\n\n| Entity | Relationship | Target | Cardinality | Owner | Status | Repository |\n|---|---|---|---|---|---|---|\n" +
            "| Account | product | Product / null | ManyToOne | api | Draft | first |\n" +
            "| Account | owner | import('../user').User | ManyToOne | api | Verified | first |\n" +
            "| Account | unresolved | any | OneToMany | api | Draft | first |\n" +
            "| Account | product | Product | ManyToOne | api | Draft | second |\n";
        table = table.Replace("\n", newline, StringComparison.Ordinal);
        var result = ErdDiagramRenderer.Render(table, "authority");
        Assert.StartsWith(table, result.Markdown, StringComparison.Ordinal);
        Assert.Equal(2, result.Assets.Count);
        Assert.Equal(result.Markdown, ErdDiagramRenderer.Render(result.Markdown, "authority").Markdown);
        Assert.DoesNotContain("\r\r\n", result.Markdown, StringComparison.Ordinal);
        var first = result.Assets[0].Content;
        Assert.Contains("Unresolved target", first, StringComparison.Ordinal);
        Assert.DoesNotContain("import(", first, StringComparison.Ordinal);
        Assert.DoesNotContain(" / null", first, StringComparison.Ordinal);
        Assert.Contains("N:1", first, StringComparison.Ordinal);
        Assert.Contains("1:N", first, StringComparison.Ordinal);
        Assert.Contains("Verified", first, StringComparison.Ordinal);
        foreach (var asset in result.Assets)
        {
            var svg = XDocument.Parse(asset.Content);
            Assert.Equal("svg", svg.Root!.Name.LocalName);
            Assert.DoesNotContain(svg.Descendants(), element => element.Name.LocalName is "script" or "foreignObject" or "image");
            Assert.Contains(asset.RelativePath, result.Markdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Renderer_EscapesLabelsAndDoesNotInventAnEmptyDiagram()
    {
        var empty = ErdDiagramRenderer.Render("# ERD\n| Entity | Relationship | Target |\n|---|---|---|\n| TODO | | |\n", "fixture");
        Assert.Empty(empty.Assets);
        Assert.Contains("No entity relationships", empty.Markdown, StringComparison.Ordinal);
        var unsafeLabels = ErdDiagramRenderer.Render("# ERD\n| Entity | Relationship | Target |\n|---|---|---|\n| <script> | a & b | \" onclick=\"alert(1) |\n", "fixture");
        var svg = XDocument.Parse(Assert.Single(unsafeLabels.Assets).Content);
        Assert.DoesNotContain(svg.Descendants(), element => element.Name.LocalName == "script" || element.Attributes().Any(attribute => attribute.Name.LocalName.StartsWith("on", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => ErdDiagramRenderer.Render(empty.Markdown.Replace(ErdDiagramRenderer.End, "", StringComparison.Ordinal), "fixture"));
    }
}
