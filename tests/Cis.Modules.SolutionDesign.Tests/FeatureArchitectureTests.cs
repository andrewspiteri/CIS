using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.SolutionDesign;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed class FeatureArchitectureTests
{
    [Fact]
    public void WithoutAiModule_StatusAndCachedDiagramsRemainAvailable()
    {
        using var f = new Fixture();
        var services = new ServiceCollection();
        new SolutionDesignModule().RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ICisFeatureArchitectureGenerator>();
        Assert.Equal("missing", generator.Run(f.Input, false, () => true).Status);
        var unavailable = generator.Run(f.Input, true, () => true);
        Assert.Equal("failed", unavailable.Status);
        Assert.Contains("Start a local CIS model", Assert.Single(unavailable.Errors), StringComparison.Ordinal);

        Assert.Empty(f.Run(true).Errors);
        var cached = generator.Run(f.Input, true, () => true);
        Assert.True(cached.Cached);
        Assert.Equal(3, cached.Diagrams.Count);
        var manifest = Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/preview.json");
        var previous = File.ReadAllText(manifest);
        f.Input = f.Input with { InputHash = "changed" };
        Assert.Equal("stale", generator.Run(f.Input, false, () => true).Status);
        Assert.Equal("failed", generator.Run(f.Input, true, () => true).Status);
        Assert.Equal(previous, File.ReadAllText(manifest));
    }

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
        Assert.Contains("Direction not established", result.Diagrams[2].Svg, StringComparison.Ordinal);
        Assert.DoesNotContain(" → ", result.Diagrams[2].Svg, StringComparison.Ordinal);
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

    [Fact]
    public void MissingContractsLeaveOpenResponsibilitiesWithoutProductContextFallback()
    {
        using var f = new Fixture();
        f.Model.Mode = "empty-interactions";
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Diagrams.Count);
        Assert.Contains(result.Warnings, w => w.Contains("no evidenced external relationship", StringComparison.Ordinal));
        Assert.DoesNotContain("Customer", result.Diagrams[0].Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Existing backend", result.Diagrams[1].Svg, StringComparison.Ordinal);
        Assert.Contains("Catalogue synchronisation", result.Diagrams[2].Svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sumsub")]
    [InlineData("Verification Vendor")]
    public void ExclusionsArePreservedAndCannotSupportAnEndpoint(string vendor)
    {
        using var f = new Fixture();
        f.AddVendor(vendor);
        f.Input = f.Input with { Source = f.Input.Source + $"\n\n## Business objectives\n\n| BO-015 | Keep KYC and {vendor} outside the Referral Platform. |\n\n## Out of scope\n\n### Identity checks\n\n{vendor} performs identity verification and receives verification requests.\n" };
        Assert.Empty(f.Run(true).Errors);
        Assert.All(f.Model.Prompts, prompt => Assert.Contains("BO-015", prompt, StringComparison.Ordinal));
        Assert.All(f.Model.Prompts.Where(p => p.Contains("KNOWN PEERS:", StringComparison.Ordinal)), prompt =>
            Assert.DoesNotContain(vendor, prompt.Split("KNOWN PEERS:")[1].Split("SAVED DIRECTION:")[0], StringComparison.Ordinal));
        Assert.DoesNotContain(f.Run(false).Diagrams, d => d.Svg.Contains(vendor, StringComparison.Ordinal));
        var manifest = Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/preview.json");
        var before = File.ReadAllText(manifest);
        f.Input = f.Input with { InputHash = "retry" };
        f.Model.Mode = "excluded-peer";
        var rejected = f.Run(true);
        Assert.Contains(rejected.Errors, e => e.Contains("affirmative feature evidence", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllText(manifest));
    }

    [Fact]
    public void ASectionHeadingOrNegativeClauseCannotSupplyContractEvidence()
    {
        using var f = new Fixture();
        f.Model.Mode = "unsupported-evidence";
        var result = f.Run(true);
        Assert.NotEmpty(result.Errors);
        Assert.False(File.Exists(Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/preview.json")));
    }

    [Fact]
    public void UnrelatedSavedAnswersCannotCreateACrossComponentContract()
    {
        using var f = new Fixture();
        f.Input = f.Input with { Direction = "Customers submit contact details for the capture form." };
        f.Model.Mode = "cross-component";
        var result = f.Run(true);
        Assert.Contains(result.Errors, e => e.Contains("affirmative feature evidence", StringComparison.Ordinal));
        var catalogue = f.Model.Prompts.Single(p => p.Contains("FEATURE BRD PASSAGE:\n## Catalogue", StringComparison.Ordinal));
        Assert.DoesNotContain("customer |", catalogue.Split("KNOWN PEERS:")[1].Split("SAVED DIRECTION:")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SavedDirectionCannotReintroduceAVendorExcludedByTheBrd()
    {
        using var f = new Fixture(); f.AddVendor("Sumsub");
        f.Input = f.Input with { Source = f.Input.Source + "\n\n## Boundary\n\n| BO-015 | Keep KYC outside the feature. |",
            Direction = "Catalogue synchronisation: Sumsub receives catalogue requests." };
        f.Model.Mode = "excluded-peer";
        Assert.NotEmpty(f.Run(true).Errors);
        var catalogue = f.Model.Prompts.Single(p => p.Contains("FEATURE BRD PASSAGE:\n## Catalogue", StringComparison.Ordinal));
        Assert.DoesNotContain("kyc |", catalogue.Split("KNOWN PEERS:")[1].Split("SAVED DIRECTION:")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BankHandoffCanUseAnUnmappedNamedRoleWithoutAssumingAVendor()
    {
        using var f = new Fixture();
        f.AddVendor("Sumsub");
        f.Input = f.Input with { Source = f.Input.Source + "\n\n## Actors\n\n| Actor | Responsibility |\n|---|---|\n| bank | Receives the customer handoff. |\n\n## Excluded capabilities\n\n- KYC, identity verification and Sumsub.\n- Bank application and funding logic.\n" };
        f.Model.Mode = "bank-role";
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        Assert.All(result.Diagrams, d => Assert.DoesNotContain("Sumsub", d.Svg, StringComparison.Ordinal));
        Assert.Contains("bank", result.Diagrams[0].Svg, StringComparison.Ordinal);
        var diagramText = Regex.Replace(Regex.Replace(result.Diagrams[1].Svg, "<[^>]+>", " "), @"\s+", " ");
        Assert.Contains("system mapping remains open", diagramText, StringComparison.Ordinal);
        Assert.Equal(3, f.Model.Calls); // The actor table is context, not a third component.
    }

    [Fact]
    public void PlainTextBusinessFlowsCanEstablishCustomerContracts()
    {
        using var f = new Fixture();
        f.Input = f.Input with { Source = f.Input.Source.Replace("Customers submit contact information and are redirected to the bank.",
            "```text\nCustomer enters Full Name and Email Address\n    ↓\nServer validates the fields and records the captured referral\n```", StringComparison.Ordinal) };
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        Assert.Contains("Customer", result.Diagrams[2].Svg, StringComparison.Ordinal);
        var proposal = File.ReadAllText(Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/last-proposal.json"));
        Assert.Contains("Customer enters Full Name and Email Address", proposal, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutableExamplesCannotEstablishVendorContracts()
    {
        using var f = new Fixture(); f.AddVendor("Sumsub");
        f.Input = f.Input with { Source = f.Input.Source.Replace("## Capture referrals",
            "```csharp\n// Sumsub receives catalogue requests.\n```\n\n## Capture referrals", StringComparison.Ordinal) };
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        var catalogue = f.Model.Prompts.Single(p => p.Contains("FEATURE BRD PASSAGE:\n## Catalogue", StringComparison.Ordinal));
        Assert.DoesNotContain("kyc |", catalogue.Split("KNOWN PEERS:")[1].Split("SAVED DIRECTION:")[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Sumsub", catalogue.Split("AFFIRMATIVE CONTRACTS:\n")[1].Split("FEATURE BRD PASSAGE:")[0], StringComparison.Ordinal);
        Assert.All(result.Diagrams, d => Assert.DoesNotContain("Sumsub", d.Svg, StringComparison.Ordinal));
    }

    [Fact]
    public void BoundedCoverageRetainsCoreAndDeliverySectionsWithoutActorComponents()
    {
        using var f = new Fixture(); f.Model.Mode = "empty-interactions";
        f.Input = f.Input with { Source = f.Input.Source
            + "\n\n## Actors\n\n| Actor | Responsibility |\n|---|---|\n| Bank | Receives the customer handoff after capture. |"
            + "\n\n## Contact capture\n\nCustomers submit their contact information through the hosted capture form."
            + "\n\n## Webhook delivery\n\nSend the captured referral to the bank using an optional authenticated webhook."
            + "\n\n## Organisation access\n\nAdministrators manage the roles and permissions of the organisation members."
            + "\n\n## Privacy records\n\nRetain the recorded privacy policy evidence with each captured referral record."
            + "\n\n## Operational monitoring\n\nOperations staff review health and availability of the referral service." };
        var result = f.Run(true);
        Assert.Empty(result.Errors);
        var components = result.Diagrams[2].Svg;
        Assert.Contains("Contact capture", components, StringComparison.Ordinal);
        Assert.Contains("Catalogue synchronisation", components, StringComparison.Ordinal);
        Assert.Contains("Webhook delivery", components, StringComparison.Ordinal);
        Assert.DoesNotContain(">Actors<", components, StringComparison.Ordinal);
        Assert.Equal(6, f.Model.Calls);
    }

    [Fact]
    public void OldUnscopedPreviewsCannotBeReusedAsCurrent()
    {
        using var f = new Fixture();
        Assert.Empty(f.Run(true).Errors);
        var manifest = Path.Combine(f.Root, ".cis/local/feature-architecture/referrals/preview.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("feature-architecture-5", "feature-architecture-3", StringComparison.Ordinal));
        Assert.Equal("missing", f.Run(false).Status);
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
            Input = new(Root, "referrals", "Referral feature", "Existing product", "source", "## Catalogue synchronisation\n\nRead products from the Existing backend and cache public catalogue queries.\n\n## Capture referrals\n\nCustomers submit contact information and are redirected to the bank.",
                "Bound saved architecture", "<!-- cis:architecture-views\n" + JsonSerializer.Serialize(baseline, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n-->", "Product technical context", "Component ownership", ["referrals", "backend"]);
            Service = new(Model);
        }
        public CisFeatureArchitectureResult Run(bool prepare) => Service.Run(Input, prepare, () => true);
        public void AddVendor(string label)
        {
            var model = ArchitectureDiagramModel.ReadRequired(Input.ProductArchitecture);
            var vendor = new ArchitectureNode("kyc", label, 3, "observed", "software-system", "Performs identity verification and sends review outcomes.");
            model = model with { Views = model.Views.Select(v => v.Level == "component" ? v : v with {
                Nodes = [.. v.Nodes, vendor], Edges = [.. v.Edges, new(v.Level == "context" ? "product" : "backend", "kyc", "Verifies identity", "observed", v.Level == "context" ? null : "HTTPS")]
            }).ToArray() };
            Input = Input with { ProductArchitecture = "<!-- cis:architecture-views\n" + JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n-->" };
        }
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
        public List<string> Prompts { get; } = [];
        public CisAiStatus GetStatus() => new([new("local", "available", "http://localhost", true, [new("test")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++; Prompt = request.Prompt; Prompts.Add(Prompt); Assert.False(request.AllowRemote); Assert.NotNull(request.JsonSchema);
            using var schema = JsonDocument.Parse(request.JsonSchema!);
            var selection = schema.RootElement.GetProperty("properties").TryGetProperty("sections", out var sections);
            var catalogue = request.Prompt.Contains("FEATURE BRD PASSAGE:\n## Catalogue", StringComparison.Ordinal);
            var match = selection ? null : Regex.Match(request.Prompt.Split("AFFIRMATIVE CONTRACTS:\n")[1], @"\[(\d+)\]");
            var id = match?.Success == true ? int.Parse(match.Groups[1].Value) : 0;
            var output = selection ? "{\"hostContainerId\":\"new\",\"sections\":" + sections.GetProperty("const").GetRawText() + "}" : JsonSerializer.Serialize(new {
                label = catalogue ? "Catalogue synchronisation" : "Capture referrals", description = "Proposed feature responsibility",
                interactions = new[] { new { peerId = catalogue ? "backend" : "customer", peerLabel = catalogue ? "Existing backend" : "Customer", peerKind = catalogue ? "software-system" : "person",
                    direction = catalogue ? "out" : "in", label = catalogue ? "Reads catalogue" : "Submits contact details", evidenceId = id } }
            });
            if (Mode == "excluded-peer" && !selection) output = output.Replace("\"peerId\":\"backend\"", "\"peerId\":\"kyc\"", StringComparison.Ordinal);
            if (Mode == "cross-component" && !selection) output = output.Replace("\"peerId\":\"backend\"", "\"peerId\":\"customer\"", StringComparison.Ordinal);
            if (Mode == "unsupported-evidence" && !selection) output = output.Replace("\"evidenceId\":" + id, "\"evidenceId\":99999", StringComparison.Ordinal);
            if (Mode == "bank-role" && !selection && !catalogue) output = JsonSerializer.Serialize(new { label = "Handoff", description = "Redirects to the bank", interactions = new[] {
                new { peerId = "new", peerLabel = "bank", peerKind = "software-system", direction = "out", label = "Redirects", evidenceId = id } } });
            if (Mode == "unsupported-peer" && !selection) output = output.Replace("\"peerId\":\"backend\"", "\"peerId\":\"imaginary\"", StringComparison.Ordinal);
            if (Mode == "wrong-section" && selection) output = """{"hostContainerId":"new","sections":[0,999]}""";
            if (Mode == "empty-interactions" && !selection) output = """{"label":"Component","description":"Incomplete","interactions":[]}""";
            return new("generated", "local", "test", Bad ? "{}" : output, null, Mode != "remote");
        }
    }
}
