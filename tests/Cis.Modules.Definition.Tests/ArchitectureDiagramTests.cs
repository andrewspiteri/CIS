using System.Text.Json;
using Cis.Modules.SolutionDesign;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArchitecturePreparation_RendersInferredDraftRepairsImagesAndKeepsActivationBlocked(bool c4)
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        var status = Invoke(application, ["solution-design", "status", "--workspace", repository.Path, "--format", "json"]);
        using var source = JsonDocument.Parse(status.Output);
        var version = source.RootElement.GetProperty("technicalIntentVersion").GetString();
        var authority = source.RootElement.GetProperty("authorityRepositoryId").GetString();
        var designPath = Path.Combine(repository.Path, "docs/cis/architecture/overall-solution-design.md");
        var sheetPath = Path.Combine(repository.Path, "docs/cis/references/component-sheet.md");
        Directory.CreateDirectory(Path.GetDirectoryName(designPath)!);
        var header = $"---\nstatus: Review Required\ncis:\n  solution_design_schema: 1\n  technical_intent_hash: {version}\n";
        var design = header + $"  stable_id: {authority}:architecture:overall-solution-design\ntype: overall-solution-design\n---\n# Architecture\n<!-- cis:solution-design-managed:start -->\n";
        foreach (var heading in new[] { "Architecture drivers", "System context and boundaries", "Logical component topology", "Data ownership and consistency",
            "Integration architecture", "Security and trust boundaries", "Deployment, operations, and recovery", "Quality and verification architecture", "UI design handoff", "Traceability" })
            design += $"\n## {heading}\n\n`TI-MOD-SYSTEM` owns the observed application boundary; deployment is unresolved.\n";
        design += "\n<!-- cis:solution-design-managed:end -->\n<!-- cis:solution-design-implementation-authored -->\n<!-- cis:architecture-views\n";
        design += JsonSerializer.Serialize(new { views = new[] { "system-context", "component-topology", "integration-trust", "deployment-operations" }
            .Select(id => new { id, title = id, notes = "The deployment environment is unresolved.", nodes = new[] {
                new { id = "system", label = "Observed application", layer = 0, status = "observed" } }, edges = Array.Empty<object>() }) }) + "\n-->\n";
        var sheet = header + $"  stable_id: {authority}:reference:component-sheet\ntype: component-sheet\n---\n# Components\n<!-- cis:component-sheet-managed:start -->\n";
        foreach (var heading in new[] { "Component catalogue", "Component responsibility profiles", "Component interaction catalogue", "Ownership rules", "Component-specific notes and accepted exceptions" })
            sheet += $"\n## {heading}\n\nApplication ownership remains subject to review.\n";
        sheet += "\n| `TI-MOD-SYSTEM` | System | application | Product requests | Product state | External identity | BR-FR-001 |\n<!-- cis:component-sheet-managed:end -->\n<!-- cis:solution-design-implementation-authored -->\n";
        File.WriteAllText(designPath, design); File.WriteAllText(sheetPath, sheet);
        if (c4)
        {
            var person = new ArchitectureNode("person", "Customer", 0, "observed", "person", "Uses the product");
            var product = new ArchitectureNode("product", "Product", 1, "observed", "software-system", "Provides product services");
            var api = new ArchitectureNode("api", "API", 1, "observed", "container", "Handles product requests", ".NET", "product");
            var handler = new ArchitectureNode("handler", "Handler", 1, "observed", "component", "Validates and handles requests", "C#", "api");
            var model = new ArchitectureDiagramModel([
                new("context", "C1 context", "Hosting is unresolved.", [person, product], [new("person", "product", "Uses", "observed")], "context", "product"),
                new("containers", "C2 containers", "Hosting is unresolved.", [person, api], [new("person", "api", "Requests services", "observed", "HTTPS")], "container", "product"),
                new("components-api", "C3 components", "Hosting is unresolved.", [person, handler], [new("person", "handler", "Requests services", "observed", "HTTPS")], "component", "api")
            ], 2);
            var modelPath = Path.Combine(repository.Path, "c4.json");
            File.WriteAllText(modelPath, JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var rendered = Invoke(application, ["solution-design", "diagrams", "--model", modelPath, "--workspace", repository.Path, "--format", "json"]);
            Assert.True(rendered.ExitCode == 0, rendered.Output);
            design = File.ReadAllText(designPath);
            Assert.Contains("## C4 solution diagrams", design, StringComparison.Ordinal);
        }
        var prepared = Invoke(application, ["definition", "prepare", "--page", "architecture", "--workspace", repository.Path, "--format", "json"]);
        Assert.Equal(0, prepared.ExitCode);
        using var result = JsonDocument.Parse(prepared.Output);
        var diagrams = result.RootElement.GetProperty("diagrams").EnumerateArray().ToArray();
        Assert.Equal(c4 ? 3 : 4, diagrams.Length);
        Assert.False(result.RootElement.GetProperty("readyToActivate").GetBoolean());
        var paths = diagrams.Select(diagram => Path.Combine(repository.Path, diagram.GetProperty("svgRelativePath").GetString()!)).ToArray();
        foreach (var path in paths) Assert.True(File.Exists(path), path);
        Assert.All(diagrams, diagram => Assert.Equal("svg", diagram.GetProperty("sourceFormat").GetString()));
        var image = File.ReadAllText(paths[0]);
        File.Delete(paths[0]);
        Assert.Equal(0, Invoke(application, ["definition", "prepare", "--page", "architecture", "--workspace", repository.Path, "--format", "json"]).ExitCode);
        Assert.Equal(image, File.ReadAllText(paths[0]));
        Assert.Equal(design, File.ReadAllText(designPath));
        Assert.Equal(sheet, File.ReadAllText(sheetPath));
        var diagramPath = Path.Combine(repository.Path, "docs/cis/architecture/high-level-architecture-diagrams.md");
        var originalDiagrams = File.ReadAllText(diagramPath);
        var technicalPath = Path.Combine(repository.Path, "docs/cis/specs/technical-intent-spec.md");
        File.AppendAllText(technicalPath, "\nThe product's technical direction has changed.\n");
        var blocked = Invoke(application, ["definition", "prepare", "--page", "architecture", "--workspace", repository.Path, "--format", "json"]);
        Assert.Equal(5, blocked.ExitCode);
        using var blockedResult = JsonDocument.Parse(blocked.Output);
        Assert.False(blockedResult.RootElement.GetProperty("applied").GetBoolean());
        var architecture = blockedResult.RootElement.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "architecture");
        Assert.Equal("reconcile-architecture", architecture.GetProperty("guidance").GetProperty("nextActionId").GetString());
        Assert.False(architecture.GetProperty("current").GetBoolean());
        Assert.Equal(design, File.ReadAllText(designPath));
        Assert.Equal(sheet, File.ReadAllText(sheetPath));
        Assert.Equal(originalDiagrams, File.ReadAllText(diagramPath));
    }
}
