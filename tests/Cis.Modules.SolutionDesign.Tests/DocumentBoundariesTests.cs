using Xunit;

namespace Cis.Modules.SolutionDesign.Tests;

public sealed partial class SolutionDesignWorkflowTests
{
    [Fact]
    public void SelectedTechnicalDocument_ProjectsExplicitBoundariesWithoutDictionaries()
    {
        using var fixture = Fixture.Create();
        const string content = """
            # Imported technical direction
            ## 2. Runtime architecture

            Deploy independently supervised services. The CLI uses APIs, never database credentials.

            | Process | Owns | Excluded work |
            | --- | --- | --- |
            | `collector` | Market connections and durable spool | Trading and wallets |
            | `workflow` | Simulation and accounting | Capture ownership |

            ## 3. Other material

            | Process | Owns | Excluded work |
            | --- | --- | --- |
            | unrelated | Other product data | Other product scope |
            """;
        File.WriteAllText(fixture.TechnicalIntentPath, content);
        Assert.Equal(5, fixture.Service.Initialize(fixture.Root).ExitCode);
        File.WriteAllText(Path.Combine(fixture.Root, ".cis/product-documents.json"), """{"technical":"docs/specs/technical-intent-spec.md"}""");
        var result = fixture.Service.Initialize(fixture.Root);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Ready for Approval", result.Validation!.EffectiveStatus);
        Assert.Equal(2, result.Components.Count);
        Assert.Contains(result.Components, component => component.Name == "collector" && component.Excludes == "Trading and wallets"
            && component.Classification == "Runtime process (technical intent)");
        Assert.Equal(content, File.ReadAllText(fixture.TechnicalIntentPath));
        Assert.Contains("The CLI uses APIs, never database credentials.", File.ReadAllText(fixture.DesignPath));
        Assert.DoesNotContain("unrelated", File.ReadAllText(fixture.SheetPath));
        Assert.Contains("approved_by: null", File.ReadAllText(fixture.DesignPath));
        Assert.DoesNotContain(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories), path => path.Contains("dictionary"));
        var identifiers = result.Components.Select(component => component.Id).ToArray();
        Assert.False(fixture.Service.Initialize(fixture.Root).Applied);
        Assert.Equal(identifiers, fixture.Service.Status(fixture.Root).Components.Select(component => component.Id));
    }

    [Fact]
    public void ImportedBoundaries_DoNotInventOwnershipFromProseOrExamples()
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, ".cis/product-documents.json"), """{"technical":"docs/specs/technical-intent-spec.md"}""");
        File.WriteAllText(fixture.TechnicalIntentPath, """
            ## Runtime architecture

            We might need a collector and a workflow service.

            ```markdown
            | Process | Owns | Excluded work |
            | --- | --- | --- |
            | example | Example state | Example exclusions |
            ```
            <!--
            | Process | Owns | Excluded work |
            | --- | --- | --- |
            | hidden | Hidden state | Hidden exclusions |
            -->
            """);
        var result = fixture.Service.Initialize(fixture.Root);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("Dictionaries are optional"));
        Assert.False(File.Exists(fixture.DesignPath));
    }
}
