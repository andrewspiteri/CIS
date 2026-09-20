using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.SolutionDesign;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed class FeatureArchitectureTests
{
    [Fact]
    public void RegistersGeneratorAndRendersFeatureC4WithExistingIdentities()
    {
        using var f = new Fixture();
        var services = new ServiceCollection();
        services.AddSingleton<ICisTextGenerationService>(f.Model);
        new SolutionDesignModule().RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<FeatureArchitectureGenerator>(provider.GetRequiredService<ICisFeatureArchitectureGenerator>());
        Assert.Equal("missing", f.Run(false).Status); Assert.Equal(0, f.Model.Calls);
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "context", "container", "component" }, result.Diagrams.Select(d => d.Level));
        Assert.Contains("Existing backend", result.Diagrams[1].Svg, StringComparison.Ordinal);
        Assert.Contains("Catalogue synchronisation", result.Diagrams[2].Svg, StringComparison.Ordinal);
        Assert.Contains("observed", result.Diagrams[1].Svg, StringComparison.Ordinal);
        Assert.Contains("proposed", result.Diagrams[2].Svg, StringComparison.Ordinal);
        Assert.Contains("Bound saved architecture", f.Model.Prompt, StringComparison.Ordinal);
        Assert.True(f.Run(true).Cached); Assert.True(f.Run(false).Cached); Assert.Equal(3, f.Model.Calls);
        f.Input = f.Input with { InputHash = "new-baseline" };
        Assert.Equal("stale", f.Run(false).Status); Assert.Equal(3, f.Model.Calls);
    }

    [Fact]
    public void ChangedInputsFailedModelAndConcurrentGenerationPreservePreviousDiagrams()
    {
        using var f = new Fixture();
        Assert.Empty(f.Run(true).Errors);
        var manifest = Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/preview.json");
        var previous = File.ReadAllText(manifest);
        f.Input = f.Input with { InputHash = "changed" };
        using (var locked = new FileStream(Path.Combine(Path.GetDirectoryName(manifest)!, "generate.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.NotEmpty(f.Run(true).Errors);
        var calls = f.Model.Calls;
        var currentChecks = 0;
        Assert.NotEmpty(f.Service.Run(f.Input, true, () => ++currentChecks == 1).Errors);
        Assert.Equal(calls + 3, f.Model.Calls);
        Assert.Equal(previous, File.ReadAllText(manifest));
        f.Model.Bad = true;
        Assert.NotEmpty(f.Run(true).Errors);
        Assert.Equal(previous, File.ReadAllText(manifest));
        Assert.Equal(3, f.Run(false).Diagrams.Count);
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("unsupported-peer")]
    [InlineData("wrong-section")]
    [InlineData("empty-interactions")]
    public void RejectsUnsupportedModelClaims(string mode)
    {
        using var f = new Fixture(); f.Model.Mode = mode;
        Assert.NotEmpty(f.Run(true).Errors);
        Assert.False(File.Exists(Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/preview.json")));
    }

    [Fact]
    public void UnsafePathAndSensitiveContextNeverReachTheModel()
    {
        using var f = new Fixture();
        f.Input = f.Input with { Slug = "../escape" }; Assert.NotEmpty(f.Run(true).Errors);
        f.Input = f.Input with { Slug = "referrals", Source = "api_key=abcdefghijklmnopqrstuvwx" }; Assert.NotEmpty(f.Run(true).Errors);
        Assert.Equal(0, f.Model.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-feature-architecture", Guid.NewGuid().ToString("N"));
        public Model Model { get; } = new();
        public CisFeatureArchitectureInput Input { get; set; }
        public FeatureArchitectureGenerator Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            var product = new ArchitectureNode("product", "Existing product", 1, "observed", "software-system", "Manages deposits.");
            var customer = new ArchitectureNode("customer", "Customer", 0, "observed", "person", "Uses the product.");
            var backend = new ArchitectureNode("backend", "Existing backend", 1, "observed", "container", "Owns the product catalogue.", "Existing runtime", "product");
            var existing = new ArchitectureNode("existing", "Existing workflows", 1, "observed", "component", "Runs product operations.", "Existing code", "backend");
            var baseline = new ArchitectureDiagramModel([
                new("context", "Context", "Existing baseline", [product, customer], [new("customer", "product", "Uses", "observed")], "context", "product"),
                new("containers", "Containers", "Existing baseline", [backend, customer], [new("customer", "backend", "Uses", "observed", "HTTP")], "container", "product"),
                new("components", "Components", "Existing baseline", [existing, customer], [new("customer", "existing", "Uses", "observed", "HTTP")], "component", "backend")], 2);
            Input = new(Root, "referrals", "Referral feature", "Existing product", "source", "## Catalogue synchronisation\n\nRead products from their authoritative owner and cache public catalogue queries.\n\n## Capture referrals\n\nCustomers submit contact information and are redirected to the bank.",
                "Bound saved architecture", "<!-- cis:architecture-views\n" + JsonSerializer.Serialize(baseline, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n-->", "Product technical context", "Component ownership", ["referrals", "backend"]);
            Service = new(Model);
        }
        public CisFeatureArchitectureResult Run(bool prepare) => Service.Run(Input, prepare, () => true);
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Path.Combine(Path.GetTempPath(), "cis-feature-architecture"), Root)) throw new InvalidOperationException();
            Directory.Delete(Root, true);
        }
    }
    private sealed class Model : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public bool Bad { get; set; }
        public string? Mode { get; set; }
        public string Prompt { get; private set; } = "";
        public CisAiStatus GetStatus() => new([new("local", "available", "http://localhost", true, [new("test")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++; Prompt = request.Prompt; Assert.False(request.AllowRemote); Assert.NotNull(request.JsonSchema);
            var selection = request.Prompt.StartsWith("Select 3-5", StringComparison.Ordinal);
            var catalogue = request.Prompt.Contains("FEATURE BRD PASSAGE:\n## Catalogue", StringComparison.Ordinal);
            var output = selection ? """{"hostContainerId":"new","sections":[0,1]}""" : JsonSerializer.Serialize(new {
                label = catalogue ? "Catalogue synchronisation" : "Capture referrals", description = "Proposed feature responsibility",
                interactions = new[] { new { peerId = catalogue ? "backend" : "customer", direction = catalogue ? "out" : "in", label = catalogue ? "Reads catalogue" : "Submits contact details" } }
            });
            if (Mode == "unsupported-peer" && !selection) output = output.Replace("\"peerId\":\"backend\"", "\"peerId\":\"imaginary\"", StringComparison.Ordinal);
            if (Mode == "wrong-section" && selection) output = """{"hostContainerId":"new","sections":[0,999]}""";
            if (Mode == "empty-interactions" && !selection) output = """{"label":"Component","description":"Incomplete","interactions":[]}""";
            return new("generated", "local", "test", Bad ? "{}" : output, null, Mode != "remote");
        }
    }
}
