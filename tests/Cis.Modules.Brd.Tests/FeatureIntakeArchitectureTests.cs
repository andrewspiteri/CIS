using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void FeatureDiagramInputsBindBrdDirectionProductArchitectureAndRepositoryBoundary()
    {
        using var f = new Fixture();
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        Assert.Empty(f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash).Errors);
        var generator = new ArchitectureCapture();
        var service = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline], architectureGenerators: [generator]);
        var status = service.Wizard(f.Authority, "referrals");
        var before = f.Snapshot();
        Assert.Empty(service.Architecture(f.Authority, "referrals", true, status.Revision).Errors);
        Assert.Equal(before, f.Snapshot());
        Assert.Equal(File.ReadAllText(f.Request.SourcePath), generator.Input!.Source);
        Assert.NotEmpty(generator.Input.RepositoryIds);
        var first = generator.Input.InputHash;
        Assert.NotEmpty(service.Architecture(f.Authority, "referrals", true, "stale").Errors);
        var unrelated = service.SaveWizard(f.Authority, new("referrals", "experience", new() { ["summary"] = "Keep the UI." }, "Reviewer", status.Revision!));
        Assert.Empty(unrelated.Errors);
        service.Architecture(f.Authority, "referrals", false);
        Assert.Equal(first, generator.Input.InputHash);
        var saved = service.SaveWizard(f.Authority, new("referrals", "architecture", new() { ["architecture-boundary"] = "Propose a separate referral service." }, "Reviewer", unrelated.Revision!));
        Assert.Empty(saved.Errors);
        service.Architecture(f.Authority, "referrals", false);
        Assert.NotEqual(first, generator.Input.InputHash);
        Assert.Contains("Propose a separate referral service.", generator.Input.Direction, StringComparison.Ordinal);
        var directionHash = generator.Input.InputHash;
        var path = Path.Combine(f.Authority, "docs/cis/architecture/overall-solution-design.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Existing product architecture changed.");
        service.Architecture(f.Authority, "referrals", false);
        Assert.NotEqual(directionHash, generator.Input.InputHash);
        Assert.Contains("Existing product architecture changed.", generator.Input.ProductArchitecture, StringComparison.Ordinal);
        Assert.True(generator.Current);
    }

    private sealed class ArchitectureCapture : ICisFeatureArchitectureGenerator
    {
        public CisFeatureArchitectureInput? Input { get; private set; }
        public bool Current { get; private set; }
        public CisFeatureArchitectureResult Run(CisFeatureArchitectureInput input, bool prepare, Func<bool> stillCurrent)
        { Input = input; Current = stillCurrent(); return new("missing", input.InputHash, [], [], []); }
    }
}
