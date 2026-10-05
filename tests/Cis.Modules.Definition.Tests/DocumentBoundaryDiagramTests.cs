using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Brd;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.SolutionDesign;
using Cis.Modules.TechnicalIntent;
using Cis.Modules.UiDirection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Fact]
    public void ArchitecturePreparation_RendersImportedBoundariesAndReportsMissingInputs()
    {
        using var repository = TemporaryRepository.Create();
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule()).AddModule(new WorkspaceModule()).AddModule(new DocsModule())
            .AddModule(new GraphModule()).AddModule(new BrdModule()).AddModule(new TechnicalIntentModule())
            .AddModule(new DocumentBoundarySourceModule()).AddModule(new SolutionDesignModule())
            .AddModule(new UiDirectionModule()).AddModule(new DefinitionModule()).Build();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        (int ExitCode, string Output, string Error) Prepare() => Invoke(application,
            ["definition", "prepare", "--page", "architecture", "--workspace", repository.Path, "--format", "json"]);
        var missing = Prepare();
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("preparation-blocked", missing.Output);

        var technical = Path.Combine(repository.Path, "docs/cis/imported-direction.md");
        const string source = """
            # Technical direction
            ## 2. Runtime architecture

            Independent runtime processes communicate through durable data.

            | Process | Owns | Excluded work |
            | --- | --- | --- |
            | collector | Durable collection | Trading |
            | workflow | Simulation and accounting | Collection |
            """;
        File.WriteAllText(technical, source);
        File.WriteAllText(Path.Combine(repository.Path, ".cis/product-documents.json"), """{"technical":"docs/cis/imported-direction.md"}""");
        var prepared = Prepare();
        Assert.True(prepared.ExitCode == 0, prepared.Output + prepared.Error);
        using var output = JsonDocument.Parse(prepared.Output);
        var diagram = Assert.Single(output.RootElement.GetProperty("diagrams").EnumerateArray());
        Assert.Equal("svg", diagram.GetProperty("sourceFormat").GetString());
        Assert.Equal("Review Required", diagram.GetProperty("status").GetString());
        var imagePath = Path.Combine(repository.Path, diagram.GetProperty("svgRelativePath").GetString()!);
        var svg = File.ReadAllText(imagePath);
        Assert.Contains("collector", svg);
        Assert.Contains("workflow", svg);
        Assert.Contains("not calls or data flows", svg);
        Assert.DoesNotContain("Observed implementation", svg);
        Assert.Equal(source, File.ReadAllText(technical));
        var repeated = Prepare();
        Assert.True(repeated.ExitCode == 0, repeated.Output + repeated.Error);
        Assert.Equal(svg, File.ReadAllText(imagePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(imagePath)!, "*.svg"));

        File.WriteAllText(Path.Combine(repository.Path, "docs/cis/specs/technical-intent-questionnaire.md"),
            "### TI-Q-002 — Frontend technology\n- Status: Answered\n\n#### Recorded answer\n\nNo frontend\n");
        var experience = Invoke(application, ["definition", "prepare", "--page", "experience", "--workspace", repository.Path, "--format", "json"]);
        Assert.True(experience.ExitCode == 0, experience.Output + experience.Error);
        using var preparedExperience = JsonDocument.Parse(experience.Output);
        Assert.False(preparedExperience.RootElement.GetProperty("uiQuestions").GetProperty("uiRequired").GetBoolean());
        var page = preparedExperience.RootElement.GetProperty("pages").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "experience");
        Assert.True(page.GetProperty("complete").GetBoolean());
        Assert.True(page.GetProperty("current").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preparedExperience.RootElement.GetProperty("preview").ValueKind);
        Assert.False(File.Exists(Path.Combine(repository.Path, "docs/cis/design/ui-system-preview.svg")));
        var direction = Path.Combine(repository.Path, "docs/cis/design/ui-direction.md");
        var noUiRecord = File.ReadAllText(direction);
        var noUiHash = ProductDefinitionAuthority.ComputeBaselineHash(Path.Combine(repository.Path, "docs/cis"), out var absent);
        Assert.DoesNotContain(absent, path => path.Contains("ui-system-preview"));
        File.WriteAllText(direction, noUiRecord.Replace("  visual_ui: false\n", ""));
        var uiHash = ProductDefinitionAuthority.ComputeBaselineHash(Path.Combine(repository.Path, "docs/cis"), out absent);
        Assert.NotEqual(noUiHash, uiHash);
        Assert.Contains("design/ui-system-preview.svg", absent);
    }

    private sealed class DocumentBoundarySourceModule : ICisModule, ISolutionDesignTechnicalIntentSource
    {
        public string Name => "document-boundary-test-source";
        public string Description => "Isolate diagram preparation from upstream review in this test.";
        public void RegisterServices(IServiceCollection services) => services.AddSingleton<ISolutionDesignTechnicalIntentSource>(this);
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
        public TechnicalIntentResult Status(string workspacePath) => new("status", workspacePath, "sample",
            "docs/cis/imported-direction.md", new(true, true, "Active", "Active", [], []), [], [], [], false);
    }
}
