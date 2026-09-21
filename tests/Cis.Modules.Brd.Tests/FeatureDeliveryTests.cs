using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void ConfirmedExistingMaintenanceIsDetectedOnReadAndCannotBecomeNewCrudFromModelOutput()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# Feature\n## Functional Requirements\n### Product catalogue\n| ID | Requirement |\n|---|---|\n| CAT-001 | An authorised user shall create and maintain manually managed products. |\n### Notification delivery\n| ID | Requirement |\n| NOT-001 | The service shall send notifications asynchronously. |\n");
        AddDeliveryCode(f, "existing-admin", "products.service.ts", "export class Products { createProduct() {} updateProduct() {} }");
        AddDeliveryCode(f, "customer-ui", "products.component.ts", "export class Products { download() { const link = document.createElement('a'); const url = URL.createObjectURL(blob); } }");
        CreateIntake(f); var generation = new DeliveryGeneration { Mode = "new" }; var service = DeliveryService(f, generation);
        var initial = service.Wizard(f.Authority, "referrals");
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new() { ["delivery-ownership"] = "All product maintenance stays in the existing application. Reuse its management UI." }, "Reviewer", initial.Revision!));
        var read = service.Delivery(f.Authority, "referrals", false);
        Assert.Equal(0, generation.Calls);
        Assert.Equal("conflict", read.Stories.Single(s => s.Title == "Product catalogue").Treatment);
        Assert.DoesNotContain("customer-ui", read.Stories.Single(s => s.Title == "Product catalogue").Owners);
        Assert.NotEqual("conflict", read.Stories.Single(s => s.Title == "Notification delivery").Treatment);
        var prepared = service.Delivery(f.Authority, "referrals", true, saved.Revision);
        Assert.Empty(prepared.Errors);
        Assert.Equal("conflict", prepared.Stories.Single(s => s.Title == "Product catalogue").Treatment);
        Assert.Contains("Keep maintenance", prepared.Stories.Single(s => s.Title == "Product catalogue").RemainingWork);
    }

    [Fact]
    public void DeliveryReconcilesAllOwnedRepositoriesPreservesRequirementsAndCachesByImplementationAndDirection()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, StoryBrd);
        var code = AddDeliveryCode(f, "existing-admin", "products.service.ts", "export class Products {\n async createProduct(input) { return this.store.save(input); }\n}");
        var excluded = AddDeliveryCode(f, "external-products", "products.service.ts", "export class ExternalProducts { createProduct() {} }", "dependency");
        CreateIntake(f);
        var generation = new DeliveryGeneration();
        var service = DeliveryService(f, generation);
        var status = service.Wizard(f.Authority, "referrals");
        var original = File.ReadAllBytes(Path.Combine(f.Authority, status.Plan!.RequestPath));
        Assert.Equal("missing", service.Delivery(f.Authority, "referrals", false).Status);
        Assert.Equal(0, generation.Calls);
        var result = service.Delivery(f.Authority, "referrals", true, status.Revision);
        Assert.Empty(result.Errors);
        Assert.Equal("current", result.Status);
        Assert.Contains(result.Evidence, e => e.RepositoryId == "existing-admin" && e.Path == "products.service.ts");
        Assert.DoesNotContain(result.Evidence, e => e.RepositoryId == "external-products");
        Assert.Contains(result.Stories, s => s.Title == "Product catalogue" && s.Treatment == "extend" && s.Owners.Contains("existing-admin"));
        Assert.Contains("CAT-001", result.SuggestedAnswers["delivery-stories-mvp"]);
        Assert.Contains("Public queries shall return only eligible products", result.SuggestedAnswers["delivery-stories-mvp"]);
        Assert.Contains("Remaining work", result.SuggestedAnswers["delivery-stories-mvp"]);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Authority, status.Plan.RequestPath)));
        var calls = generation.Calls;
        Assert.True(service.Delivery(f.Authority, "referrals", true, status.Revision).Cached);
        Assert.Equal(calls, generation.Calls);
        File.AppendAllText(code, "\nexport const changed = true;\n");
        Assert.Equal("stale", service.Delivery(f.Authority, "referrals", false).Status);
        var updated = service.Delivery(f.Authority, "referrals", true, status.Revision);
        Assert.Empty(updated.Errors);
        var saved = service.SaveWizard(f.Authority, new("referrals", "delivery", new() { ["delivery-ownership"] = "All product maintenance stays in existing-admin. The feature only consumes products." }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors);
        Assert.False(saved.Pages.Single(p => p.Id == "delivery").Complete);
        Assert.Equal("stale", service.Delivery(f.Authority, "referrals", false).Status);
        Assert.Empty(service.Delivery(f.Authority, "referrals", true, saved.Revision).Errors);
        Assert.Contains(generation.Prompts, p => p.Contains("All product maintenance stays", StringComparison.Ordinal));
        Assert.Contains("Sumsub", generation.LastPrompt); // Exclusion remains a constraint, never a delivery story.
        Assert.False(generation.LastRequest!.AllowRemote);
        Assert.DoesNotContain("external-products", generation.LastPrompt);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(code)!, "products-ui.ts"), "export class ProductsUi {}");
        Assert.Equal("stale", service.Delivery(f.Authority, "referrals", false).Status);
        Assert.True(File.Exists(excluded));
    }

    [Theory]
    [InlineData("missing-evidence")]
    [InlineData("foreign-owner")]
    [InlineData("low-confidence")]
    public void DeliveryDoesNotPresentUnsupportedReuseClaimsAsEstablished(string mode)
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, StoryBrd);
        AddDeliveryCode(f, "existing-admin", "products.service.ts", "export class Products { createProduct() {} }"); CreateIntake(f);
        var service = DeliveryService(f, new DeliveryGeneration { Mode = mode });
        var result = service.Delivery(f.Authority, "referrals", true, service.Wizard(f.Authority, "referrals").Revision);
        Assert.Empty(result.Errors);
        Assert.All(result.Stories, s => Assert.Equal("unresolved", s.Treatment));
        Assert.Contains(result.Warnings, w => w.Contains("evidence gaps", StringComparison.Ordinal));
    }

    [Fact]
    public void DeliveryRetainsConflictsAndRejectsConcurrentChangeRemoteProviderAndStaleRevision()
    {
        using var f = new Fixture(); File.WriteAllText(f.Request.SourcePath, StoryBrd);
        var code = AddDeliveryCode(f, "existing-admin", "products.service.ts", "export class Products { createProduct() {} }"); CreateIntake(f);
        var generation = new DeliveryGeneration { Mode = "conflict" }; var service = DeliveryService(f, generation);
        var state = service.Wizard(f.Authority, "referrals");
        Assert.NotEmpty(service.Delivery(f.Authority, "referrals", true, "wrong").Errors);
        Assert.Equal(0, generation.Calls);
        var result = service.Delivery(f.Authority, "referrals", true, state.Revision);
        Assert.Contains(result.Stories, s => s.Treatment == "conflict" && s.Conflict!.Contains("ownership", StringComparison.Ordinal));
        Assert.Contains("Original BRD requirements", result.SuggestedAnswers["delivery-stories-mvp"]);
        File.AppendAllText(code, "\n// changed\n");
        generation.DuringGeneration = () => File.AppendAllText(code, "\n// changed again\n");
        Assert.Contains(service.Delivery(f.Authority, "referrals", true, state.Revision).Errors, e => e.Contains("changed during", StringComparison.Ordinal));
        var remote = new DeliveryGeneration { Remote = true };
        Assert.NotEmpty(DeliveryService(f, remote).Delivery(f.Authority, "referrals", true, state.Revision).Errors);
        Assert.Equal(0, remote.Calls);
    }

    private static FeatureIntakeService DeliveryService(Fixture f, ICisTextGenerationService generation)
        => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], textGeneration: generation);

    private static string AddDeliveryCode(Fixture f, string name, string file, string content, string participation = "owned")
    {
        var root = Path.Combine(f.Root, name); Directory.CreateDirectory(root);
        Assert.Equal(0, f.Importer.Import(new(f.Authority, "docs/cis", [root], false, true, participation, participation == "owned" ? "none" : "producer")).ExitCode);
        var path = Path.Combine(root, file); File.WriteAllText(path, content); return path;
    }

    private sealed class DeliveryGeneration : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public string Mode { get; init; } = "extend";
        public bool Remote { get; init; }
        public string LastPrompt { get; private set; } = "";
        public List<string> Prompts { get; } = [];
        public CisTextGenerationRequest? LastRequest { get; private set; }
        public Action? DuringGeneration { get; set; }
        public CisAiStatus GetStatus() => new([new("test-local", "available", "http://localhost", !Remote, [new("test", 1)], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++; LastPrompt = request.Prompt; LastRequest = request; Prompts.Add(request.Prompt);
            var start = request.Prompt.IndexOf("<evidence>\n", StringComparison.Ordinal) + "<evidence>\n".Length;
            var end = request.Prompt.IndexOf("\n</evidence>", start, StringComparison.Ordinal);
            using var context = JsonDocument.Parse(request.Prompt[start..end]);
            var evidence = context.RootElement.GetProperty("implementation").EnumerateArray().FirstOrDefault();
            var found = evidence.ValueKind == JsonValueKind.Object;
            var owner = found ? evidence.GetProperty("repositoryId").GetString() : "existing-admin";
            var evidenceId = found ? evidence.GetProperty("id").GetString() : null;
            var stories = context.RootElement.GetProperty("stories").EnumerateArray().Select(s => new
            {
                id = s.GetProperty("id").GetString(), treatment = Mode is "conflict" or "new" or "reuse" ? Mode : "extend",
                existingCapability = Mode == "hook-overclaim" ? "Click Tracking is already implemented in the frontend component." : Mode == "hook-grounded" ? "selectProduct navigates to the deposit page." : "Existing product maintenance is available.",
                remainingWork = "Add only the integration and required policy fields.",
                owners = new[] { Mode == "foreign-owner" ? "unknown" : owner },
                evidenceIds = Mode == "missing-evidence" || !found ? Array.Empty<string>() : new[] { evidenceId },
                conflict = Mode == "conflict" ? "The product catalogue source and saved ownership direction differ." : null,
                confidence = Mode == "low-confidence" ? "low" : "medium"
            }).ToArray();
            DuringGeneration?.Invoke();
            return new("generated", "test-local", "test", JsonSerializer.Serialize(new { stories }), null, true);
        }
    }
}
