using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Host;
using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reconcile_PreservesExplicitDesignAndNotesWithoutInventingApprovalOrInference(bool approved)
    {
        using var fixture = ReconciliationFixture(approved);
        var design = File.ReadAllText(fixture.DesignPath);
        var sheet = File.ReadAllText(fixture.SheetPath);
        var assets = Directory.GetFiles(Path.GetDirectoryName(fixture.DesignPath)!, "*.svg", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        File.AppendAllText(fixture.TechnicalIntentPath, "\nReviewed standards provenance change.\n");

        var result = Reconcile(fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.True(result.Validation!.Current);
        Assert.False(result.Validation.InferenceReconciliationRequired);
        Assert.Equal("Ready for Approval", result.Validation.EffectiveStatus);
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);
        foreach (var (path, original) in new[] { (fixture.DesignPath, design), (fixture.SheetPath, sheet) })
        {
            var content = File.ReadAllText(path);
            Assert.Equal(ReconciliationBody(original), ReconciliationBody(content));
            Assert.Contains("approved_by: null", content, StringComparison.Ordinal);
            Assert.Contains("approved_bundle_hash: null", content, StringComparison.Ordinal);
            Assert.Contains("cis:solution-design-reconciliation\n", content, StringComparison.Ordinal);
            Assert.DoesNotContain("cis:solution-design-implementation-authored", content, StringComparison.Ordinal);
        }
        foreach (var (path, bytes) in assets) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(Reconcile(fixture).Applied);
        Assert.Equal("Active", fixture.Service.Approve(fixture.Root, "Owner", "Reviewed reconciled pair").Validation!.EffectiveStatus);
        Assert.False(Reconcile(fixture).Applied);
        Assert.Equal("Active", fixture.Service.Status(fixture.Root).Validation!.EffectiveStatus);
    }

    [Theory]
    [InlineData("design-hash")]
    [InlineData("sheet-hash")]
    [InlineData("source-version")]
    [InlineData("inactive-source")]
    [InlineData("actor")]
    [InlineData("reason")]
    [InlineData("invalid-diagram")]
    [InlineData("component-drift")]
    public void Reconcile_RejectsUnreviewedOrInvalidInputsWithoutChangingCanonicalBytes(string fault)
    {
        using var fixture = ReconciliationFixture(true);
        File.AppendAllText(fixture.TechnicalIntentPath, "\nStandards evolved.\n");
        if (fault == "inactive-source") fixture.Source.Result = fixture.Source.Result with
        { Validation = new(true, true, "Ready for Approval", "Review Required", [], []) };
        if (fault == "invalid-diagram") File.WriteAllText(fixture.DesignPath, File.ReadAllText(fixture.DesignPath)
            .Replace("## C4 solution diagrams", "## Altered display", StringComparison.Ordinal));
        if (fault == "component-drift") File.WriteAllText(fixture.TechnicalIntentPath, File.ReadAllText(fixture.TechnicalIntentPath)
            .Replace("TI-MOD-WORKSPACE", "TI-MOD-REPLACEMENT", StringComparison.Ordinal));
        var paths = new[] { fixture.DesignPath, fixture.SheetPath, Path.Combine(fixture.Root, "docs/catalog.yml") };
        var originals = paths.ToDictionary(path => path, File.ReadAllBytes);

        var result = fixture.Service.Reconcile(fixture.Root,
            fault == "design-hash" ? new string('0', 64) : FileHash(fixture.DesignPath),
            fault == "sheet-hash" ? "" : FileHash(fixture.SheetPath),
            fault == "source-version" ? "semantic-v1:outdated" : fixture.Service.Status(fixture.Root).TechnicalIntentVersion!,
            fault == "actor" ? " " : "Codex", fault == "reason" ? "" : "Reviewed unchanged topology");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Applied);
        foreach (var path in paths) Assert.Equal(originals[path], File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("yaml")]
    public void Reconcile_CommandDispatchValidatesFormatBeforeMutation(string format)
    {
        using var fixture = ReconciliationFixture(true);
        File.AppendAllText(fixture.TechnicalIntentPath, "\nStandards evolved.\n");
        using var application = new CisHostBuilder().AddModule(new FixtureDependenciesModule(fixture))
            .AddModule(new SolutionDesignModule()).Build();
        var originals = new[] { fixture.DesignPath, fixture.SheetPath, Path.Combine(fixture.Root, "docs/catalog.yml") }
            .ToDictionary(path => path, File.ReadAllBytes);
        var result = Invoke(application, ["solution-design", "reconcile", "--workspace", fixture.Root,
            "--expected-design-sha256", FileHash(fixture.DesignPath), "--expected-components-sha256", FileHash(fixture.SheetPath),
            "--expected-technical-version", fixture.Service.Status(fixture.Root).TechnicalIntentVersion!,
            "--actor", "Codex", "--reason", "No architecture change after standards review", "--format", format]);
        if (format == "yaml")
        {
            Assert.NotEqual(0, result.ExitCode);
            foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
            return;
        }
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("reconciled", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Review Required", document.RootElement.GetProperty("validation").GetProperty("designStatus").GetString());
    }

    [Fact]
    public void Reconcile_RestoresExactBytesIfSourceLosesApprovalDuringValidation()
    {
        using var fixture = ReconciliationFixture(true);
        File.AppendAllText(fixture.TechnicalIntentPath, "\nStandards evolved.\n");
        var version = fixture.Service.Status(fixture.Root).TechnicalIntentVersion!;
        var originals = new[] { fixture.DesignPath, fixture.SheetPath, Path.Combine(fixture.Root, "docs/catalog.yml") }
            .ToDictionary(path => path, File.ReadAllBytes);
        var calls = 0;
        fixture.Source.OnStatus = () => ++calls < 3 ? fixture.Source.Result : fixture.Source.Result with
        { Validation = new(true, false, "Stale", "Active", [], []) };

        var result = fixture.Service.Reconcile(fixture.Root, FileHash(fixture.DesignPath), FileHash(fixture.SheetPath),
            version, "Codex", "Reviewed source before concurrent loss of approval");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.Contains("original files were restored", StringComparison.Ordinal));
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static Fixture ReconciliationFixture(bool approved)
    {
        var fixture = Fixture.Create();
        Assert.Equal(0, fixture.Service.Initialize(fixture.Root).ExitCode);
        var model = Path.Combine(fixture.Root, "c4-model.json");
        File.WriteAllText(model, JsonSerializer.Serialize(C4Model(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(0, fixture.Service.RenderDiagrams(fixture.Root, model).ExitCode);
        File.AppendAllText(fixture.DesignPath, "\nHuman design note: preserve this responsibility.\n");
        File.AppendAllText(fixture.SheetPath, "\nHuman ownership note: keep the existing boundary.\n");
        if (approved) Assert.Equal(0, fixture.Service.Approve(fixture.Root, "Owner", "Reviewed explicit model").ExitCode);
        return fixture;
    }

    private static SolutionDesignResult Reconcile(Fixture fixture) => fixture.Service.Reconcile(fixture.Root,
        FileHash(fixture.DesignPath), FileHash(fixture.SheetPath), fixture.Service.Status(fixture.Root).TechnicalIntentVersion!,
        "Codex", "Reviewed standards provenance; topology and responsibilities remain appropriate.");
    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string ReconciliationBody(string content) => Regex.Replace(Regex.Replace(content,
        @"\A---\r?\n.*?\r?\n---", "", RegexOptions.Singleline),
        @"\s*<!-- cis:solution-design-reconciliation\r?\n.*?\r?\n-->", "", RegexOptions.Singleline).TrimEnd();
}
