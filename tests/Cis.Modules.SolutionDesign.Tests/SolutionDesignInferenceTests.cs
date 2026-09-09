using System.Text.Json;
using System.Xml.Linq;
using Cis.Modules.SolutionDesign;
using Cis.Modules.TechnicalIntent;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Fact]
    public void Inference_AcceptsExistingTechnicalResponsibilityProfilesWithoutRewritingTechnicalIntent()
    {
        using var fixture = Fixture.Create();
        var technical = "# Technical direction\n\n## Product module architecture\n\n### Module responsibility profiles\n\n" +
            "#### TI-MOD-EXPERIENCES — customer applications\n\n**Purpose:** Present customer journeys.\n\n**Data and state:** Transient browser state and API DTOs.\n\n" +
            "#### TI-MOD-IDENTITY — identity and tenancy\n\n**Purpose:** Establish user and tenant context.\n\n**Data and state:** Local users plus external sessions.\n";
        File.WriteAllText(fixture.TechnicalIntentPath, technical);
        Assert.Empty(fixture.Service.PrepareExistingDraft(fixture.Root).Errors);
        var result = fixture.Service.Status(fixture.Root);
        Assert.Equal(2, result.Components.Count);
        Assert.Contains(result.Components, item => item.Id == "TI-MOD-EXPERIENCES" && item.Name == "customer applications" && item.Responsibility == "Present customer journeys.");
        Assert.Equal(technical, File.ReadAllText(fixture.TechnicalIntentPath));
    }

    [Fact]
    public void Inference_PreparesReviewOnlyBundleWithoutWeakeningOrdinaryGates()
    {
        using var fixture = Fixture.Create();
        fixture.Source.Result = fixture.Source.Result with { Validation = new TechnicalIntentValidation(true, false, "Review Required", "Draft", [], []) };
        Assert.Equal("blocked", fixture.Service.Initialize(fixture.Root).Status);
        var technical = File.ReadAllText(fixture.TechnicalIntentPath);
        var prepared = fixture.Service.PrepareExistingDraft(fixture.Root);
        Assert.Empty(prepared.Errors);
        Assert.True(prepared.Applied);
        Assert.Equal(technical, File.ReadAllText(fixture.TechnicalIntentPath));
        Assert.Contains("status: Review Required", File.ReadAllText(fixture.DesignPath), StringComparison.Ordinal);
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);
        Assert.Equal("blocked", fixture.Service.Approve(fixture.Root, "Reviewer", "Review draft").Status);
    }

    [Fact]
    public void Inference_AppliesOneBundlePreservesNarrativeAndExposesUpstreamDrift()
    {
        using var fixture = Fixture.Create();
        fixture.Service.Initialize(fixture.Root);
        var design = File.ReadAllText(fixture.DesignPath); var sheet = File.ReadAllText(fixture.SheetPath);
        var proposed = design + "\nObserved services share the application boundary; deployment is unresolved.\n" + DiagramJson();
        var applied = fixture.Service.ApplyExistingDraft(fixture.Root, design, sheet, proposed, sheet + "\nObserved responsibility profiles remain subject to review.\n");
        Assert.Empty(applied.Errors);
        Assert.True(applied.Applied);
        var inferred = File.ReadAllText(fixture.DesignPath); var inferredSheet = File.ReadAllText(fixture.SheetPath);
        Assert.False(fixture.Service.Initialize(fixture.Root).Applied);
        Assert.Equal(inferred, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(inferredSheet, File.ReadAllText(fixture.SheetPath));
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);
        File.AppendAllText(fixture.TechnicalIntentPath, "\nA changed upstream architecture direction.\n");
        fixture.Service.Initialize(fixture.Root);
        Assert.Equal(inferred, File.ReadAllText(fixture.DesignPath));
        Assert.False(fixture.Service.Status(fixture.Root).Validation!.Current);
        Assert.Empty(fixture.Service.PrepareExistingDraft(fixture.Root).Errors);
        Assert.Contains("Observed services share", File.ReadAllText(fixture.DesignPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("component")]
    [InlineData("diagram")]
    [InlineData("canonical")]
    [InlineData("human-notes")]
    public void Inference_RejectsInvalidHalfWithoutApplyingEitherHalf(string failure)
    {
        using var fixture = Fixture.Create();
        fixture.Service.Initialize(fixture.Root);
        File.AppendAllText(fixture.SheetPath, "\nHuman note: keep settlement ownership with the application.\n");
        var design = File.ReadAllText(fixture.DesignPath); var sheet = File.ReadAllText(fixture.SheetPath);
        var proposed = design + DiagramJson(); var proposedSheet = sheet + "\nObserved contracts.\n";
        if (failure == "metadata") proposedSheet = proposedSheet.Replace("status: Review Required", "status: Active", StringComparison.Ordinal);
        if (failure == "component") proposedSheet = proposedSheet.Replace("TI-MOD-IDENTITY", "TI-MOD-INVENTED", StringComparison.Ordinal);
        if (failure == "diagram") proposed = proposed.Replace("system-context", "invented-view", StringComparison.Ordinal);
        if (failure == "canonical") File.AppendAllText(fixture.SheetPath, "\nHuman edit during inference.\n");
        if (failure == "human-notes") proposedSheet = proposedSheet.Replace("Human note: keep settlement ownership with the application.", "Agent replaced human ownership.", StringComparison.Ordinal);
        var beforeSheet = File.ReadAllText(fixture.SheetPath);
        var result = fixture.Service.ApplyExistingDraft(fixture.Root, design, sheet, proposed, proposedSheet);
        Assert.NotEmpty(result.Errors);
        Assert.False(result.Applied);
        Assert.Equal(design, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(beforeSheet, File.ReadAllText(fixture.SheetPath));
    }

    [Fact]
    public void Inference_PreservesAnActiveBundle()
    {
        using var fixture = Fixture.Create();
        fixture.Service.Initialize(fixture.Root);
        fixture.Service.Approve(fixture.Root, "Owner", "Reviewed together");
        var design = File.ReadAllText(fixture.DesignPath); var sheet = File.ReadAllText(fixture.SheetPath);
        Assert.NotEmpty(fixture.Service.PrepareExistingDraft(fixture.Root).Errors);
        Assert.Equal(design, File.ReadAllText(fixture.DesignPath));
        Assert.Equal(sheet, File.ReadAllText(fixture.SheetPath));
    }

    [Fact]
    public void DiagramModel_RendersPassiveDeterministicImagesAndRejectsBrokenRelationships()
    {
        var json = DiagramJson();
        var model = ArchitectureDiagramModel.ReadRequired(json);
        var images = model.Render();
        Assert.Equal(4, images.Count);
        Assert.Equal(images, model.Render());
        foreach (var image in images)
        {
            var xml = XDocument.Parse(image.Content);
            Assert.DoesNotContain(xml.Descendants(), node => node.Name.LocalName is "script" or "image" or "foreignObject");
            Assert.Contains("unresolved", image.Content, StringComparison.Ordinal);
            Assert.Contains("&amp;", image.Content, StringComparison.Ordinal);
        }
        Assert.Throws<InvalidDataException>(() => ArchitectureDiagramModel.ReadRequired(json.Replace("\"to\":\"api\"", "\"to\":\"missing\"", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => ArchitectureDiagramModel.ReadRequired(json + json));
        Assert.Throws<InvalidDataException>(() => ArchitectureDiagramModel.ReadRequired(json.Replace("\"observed\"", "\"approved\"", StringComparison.Ordinal)));
    }

    private static string DiagramJson()
        => "\n<!-- cis:architecture-views\n" + JsonSerializer.Serialize(new { views = new[] { "system-context", "component-topology", "integration-trust", "deployment-operations" }
            .Select(id => new { id, title = "Implementation & architecture", notes = "Runtime deployment remains unresolved; no live environment was contacted.",
                nodes = new[] { new { id = "client", label = "Customer application", layer = 0, status = "observed" },
                    new { id = "api", label = "Application API", layer = 1, status = "observed" }, new { id = "runtime", label = "Deployment environment", layer = 2, status = "unresolved" } },
                edges = new[] { new { from = "client", to = "api", label = "Authenticated product requests", status = "observed" },
                    new { from = "api", to = "runtime", label = "Runtime placement unverified", status = "unresolved" } } }) }) + "\n-->\n";
}
