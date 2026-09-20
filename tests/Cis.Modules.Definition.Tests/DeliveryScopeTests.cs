using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cis.Modules.Definition;
using Cis.Modules.Repository;
using Cis.Modules.SolutionDesign;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeliveryScope_NoPlannedWorkUsesTheNormalActivationGates(bool c4)
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        (int ExitCode, string Output, string Error) Run(params string[] args)
            => Invoke(application, [.. args, "--workspace", repository.Path, "--format", "json"]);
        void Success(params string[] args) { var result = Run(args); Assert.True(result.ExitCode == 0, result.Output + result.Error); }
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis", "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        Success("definition", "init");
        Assert.Equal(5, Run("definition", "prepare", "--page", "business", "--backlog-mode", "no-planned-work", "--actor", "Owner").ExitCode);
        Assert.Equal(5, Run("definition", "prepare", "--page", "delivery", "--backlog-mode", "no-planned-work", "--actor", "Owner").ExitCode);
        Assert.Equal(2, Run("brd", "backlog", "build", "--mode", "no-planned-work").ExitCode);
        Success("graph", "build");
        Success("brd", "init", "--title", "Existing product");
        var brdPath = Path.Combine(repository.Path, "docs/cis/specs/business-requirements.md");
        var brd = Regex.Replace(File.ReadAllText(brdPath), "(?m)^TODO: Complete .+$", "Stakeholders recorded complete and testable requirements.");
        brd = Regex.Replace(brd, "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The product view is available. |\n\n");
        File.WriteAllText(brdPath, brd);
        Success("graph", "build");
        Success("brd", "approve", "--reviewer", "Owner", "--reason", "Existing product baseline reviewed.");
        Success("definition", "prepare", "--page", "technical");
        foreach (var pageId in new[] { "technical", "experience" })
        {
            if (pageId == "experience")
            {
                Success("definition", "prepare", "--page", "architecture");
                if (c4) PrepareC4();
                Success("definition", "prepare", "--page", "experience");
            }
            using var status = JsonDocument.Parse(Run("definition", "status").Output);
            foreach (var question in status.RootElement.GetProperty(pageId == "technical" ? "technicalQuestions" : "uiQuestions").GetProperty("questions").EnumerateArray())
                Success("definition", "answer", "--page", pageId, "--id", question.GetProperty("id").GetString()!, "--answer", question.GetProperty("suggestedAnswer").GetString()!, "--actor", "Owner");
        }
        void PrepareC4()
        {
            var person = new ArchitectureNode("person", "Customer", 0, "observed", "person", "Uses the product");
            var product = new ArchitectureNode("product", "Product", 1, "observed", "software-system", "Provides product services");
            var api = new ArchitectureNode("api", "API", 1, "observed", "container", "Handles product requests", ".NET", "product");
            var handler = new ArchitectureNode("handler", "Handler", 1, "observed", "component", "Handles requests", "C#", "api");
            var model = new ArchitectureDiagramModel([
                new("context", "C1 context", "Hosting is unresolved.", [person, product], [new("person", "product", "Uses", "observed")], "context", "product"),
                new("containers", "C2 containers", "Hosting is unresolved.", [person, api], [new("person", "api", "Requests services", "observed", "HTTPS")], "container", "product"),
                new("components-api", "C3 components", "Hosting is unresolved.", [person, handler], [new("person", "handler", "Requests services", "observed", "HTTPS")], "component", "api")
            ], 2);
            var modelPath = Path.Combine(repository.Path, "c4.json");
            File.WriteAllText(modelPath, JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Success("solution-design", "diagrams", "--model", modelPath);
            Success("graph", "build");
        }
        Success("definition", "prepare", "--page", "delivery", "--backlog-mode", "no-planned-work", "--actor", "Owner");
        using var prepared = JsonDocument.Parse(Run("definition", "status").Output);
        var delivery = prepared.RootElement.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "delivery");
        Assert.True(delivery.GetProperty("complete").GetBoolean(), prepared.RootElement.ToString());
        Assert.True(delivery.GetProperty("current").GetBoolean());
        Assert.Contains("No planned work", delivery.GetProperty("guidance").GetProperty("summary").GetString());
        Assert.True(prepared.RootElement.GetProperty("readyToActivate").GetBoolean(), prepared.RootElement.ToString());
        var diagramPath = Path.Combine(repository.Path, "docs/cis/architecture/high-level-architecture-diagrams.md");
        var oldSource = Regex.Match(File.ReadAllText(diagramPath), "(?m)^  source_hash:.*$").Value;
        var activation = Run("definition", "activate", "--reviewer", "Owner");
        Assert.Equal(0, activation.ExitCode);
        AssertCurrent(activation.Output);
        AssertCurrent(Run("definition", "status").Output);
        Assert.NotEqual(oldSource, Regex.Match(File.ReadAllText(diagramPath), "(?m)^  source_hash:.*$").Value);
        using var backlog = JsonDocument.Parse(Run("brd", "backlog", "status").Output);
        Assert.Equal("Active", backlog.RootElement.GetProperty("validation").GetProperty("effectiveStatus").GetString());
        Assert.Empty(backlog.RootElement.GetProperty("items").EnumerateArray());

        using var featureFixture = TemporaryRepository.Create();
        AssertFeatureIntakePreservesActivatedProduct(repository.Path, featureFixture.Path, Run);

        // Reproduce an older activation: the signed diagrams and consolidated baseline
        // contain the pre-approval source hash. Status must recognize it without writes.
        var legacy = Regex.Replace(File.ReadAllText(diagramPath), "(?m)^  source_hash:.*$", oldSource);
        legacy = Regex.Replace(legacy, "(?m)^  approved_content_hash:.*$", "  approved_content_hash: null");
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
        legacy = Regex.Replace(legacy, "(?m)^  approved_content_hash:.*$", "  approved_content_hash: " + JsonSerializer.Serialize(digest));
        File.WriteAllText(diagramPath, legacy);
        var sessionPath = Path.Combine(repository.Path, ".cis/local/definition-wizard/session.json");
        BindFixtureBaseline();
        var session = File.ReadAllText(sessionPath);
        AssertCurrent(Run("definition", "status").Output);
        Assert.Equal(legacy, File.ReadAllText(diagramPath));
        Assert.Equal(session, File.ReadAllText(sessionPath));

        foreach (var relative in new[] { "architecture/overall-solution-design.md", "references/component-sheet.md", "architecture/high-level-architecture-diagrams.md" })
        {
            var path = Path.Combine(repository.Path, "docs/cis", relative);
            var original = File.ReadAllText(path);
            File.AppendAllText(path, "\nA material architecture edit.\n");
            AssertStale();
            File.WriteAllText(path, original);
        }
        // Even a rebound session cannot hide a different renderer body or broken SVG.
        File.AppendAllText(diagramPath, "\nViews from a different renderer.\n");
        BindFixtureBaseline();
        AssertStale();
        File.WriteAllText(diagramPath, legacy);
        File.WriteAllText(sessionPath, session);
        if (c4)
        {
            using var status = JsonDocument.Parse(Run("definition", "status").Output);
            var svg = Path.Combine(repository.Path, status.RootElement.GetProperty("diagrams")[0].GetProperty("svgRelativePath").GetString()!);
            var image = File.ReadAllText(svg);
            File.Delete(svg);
            AssertStale();
            Assert.False(File.Exists(svg), "Status must not regenerate missing images.");
            File.WriteAllText(svg, image + "\n<!-- modified -->");
            AssertStale();
            File.WriteAllText(svg, image);
        }
        AssertCurrent(Run("definition", "status").Output);

        void AssertCurrent(string output)
        {
            using var status = JsonDocument.Parse(output);
            var architecture = status.RootElement.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "architecture");
            Assert.True(architecture.GetProperty("complete").GetBoolean(), architecture.ToString());
            Assert.True(architecture.GetProperty("current").GetBoolean(), architecture.ToString());
            Assert.Empty(architecture.GetProperty("issues").EnumerateArray());
            Assert.False(status.RootElement.GetProperty("active").GetBoolean());
            Assert.All(status.RootElement.GetProperty("diagrams").EnumerateArray(), diagram => Assert.Equal("Active", diagram.GetProperty("status").GetString()));
            Assert.True(new ProductDefinitionAuthority(new CisRepositoryContextResolver()).Evaluate(repository.Path).Active);
        }
        void AssertStale()
        {
            using var status = JsonDocument.Parse(Run("definition", "status").Output);
            var architecture = status.RootElement.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "architecture");
            Assert.False(architecture.GetProperty("current").GetBoolean(), architecture.ToString());
            Assert.All(status.RootElement.GetProperty("diagrams").EnumerateArray(), diagram => Assert.Equal("Stale", diagram.GetProperty("status").GetString()));
        }
        void BindFixtureBaseline()
        {
            var record = JsonNode.Parse(File.ReadAllText(sessionPath))!;
            var hash = ProductDefinitionAuthority.ComputeBaselineHash(Path.Combine(repository.Path, "docs/cis"), out var missing);
            Assert.Empty(missing);
            record["baselineHash"] = hash;
            record["semanticBaselineHash"] = hash;
            File.WriteAllText(sessionPath, record.ToJsonString());
        }
    }
}
