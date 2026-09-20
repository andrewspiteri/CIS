using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Fact]
    public void ArchitectureApproval_RequiresApprovedUpstreamAndApprovesOnlyCurrentArchitectureBundle()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        (int ExitCode, string Output, string Error) Run(params string[] args)
            => Invoke(application, [.. args, "--workspace", repository.Path, "--format", "json"]);
        void Success(params string[] args) { var result = Run(args); Assert.True(result.ExitCode == 0, result.Output + result.Error); }
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis", "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        Success("definition", "init");
        Assert.Equal(5, Run("definition", "approve", "--page", "architecture", "--reviewer", "Reviewer").ExitCode);
        Assert.Equal(2, Run("definition", "approve", "--page", "business", "--reviewer", "Reviewer").ExitCode);
        Success("graph", "build");
        Success("brd", "init", "--title", "Product BRD");
        var brdPath = Path.Combine(repository.Path, "docs/cis/specs/business-requirements.md");
        var brd = File.ReadAllText(brdPath);
        brd = Regex.Replace(brd, "(?m)^TODO: Complete .+$", "Stakeholders recorded complete and testable requirements.");
        brd = Regex.Replace(brd, "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The product view is available. |\n\n");
        File.WriteAllText(brdPath, brd);
        Success("graph", "build");
        Success("brd", "approve", "--reviewer", "Product owner", "--reason", "Requirements accepted.");
        Success("technical-intent", "questions", "init");
        using (var questions = JsonDocument.Parse(Run("technical-intent", "questions", "status").Output))
            foreach (var question in questions.RootElement.GetProperty("questions").EnumerateArray())
                Success("technical-intent", "questions", "answer", question.GetProperty("id").GetString()!, "--answer", question.GetProperty("suggestedAnswer").GetString()!, "--actor", "Technical owner");
        Success("technical-intent", "init");
        Success("definition", "prepare", "--page", "architecture");
        var design = Path.Combine(repository.Path, "docs/cis/architecture/overall-solution-design.md");
        var before = File.ReadAllText(design);
        Assert.Equal(5, Run("definition", "approve", "--page", "architecture", "--reviewer", "Reviewer").ExitCode);
        Assert.Equal(before, File.ReadAllText(design));
        Success("technical-intent", "approve", "--reviewer", "Technical owner", "--reason", "Technical direction accepted.");
        Success("definition", "prepare", "--page", "architecture");
        var upstream = File.ReadAllText(Path.Combine(repository.Path, "docs/cis/specs/technical-intent-spec.md"));
        Success("definition", "approve", "--page", "architecture", "--reviewer", "Architecture owner");
        using var status = JsonDocument.Parse(Run("definition", "status").Output);
        var page = status.RootElement.GetProperty("pages").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "architecture");
        Assert.Equal("Active", page.GetProperty("status").GetString());
        Assert.True(page.GetProperty("current").GetBoolean());
        Assert.True(page.GetProperty("complete").GetBoolean());
        Assert.All(status.RootElement.GetProperty("diagrams").EnumerateArray(), diagram => Assert.Equal("Active", diagram.GetProperty("status").GetString()));
        Assert.True(status.RootElement.GetProperty("active").GetBoolean(), "The complete definition session remains open for the later pages.");
        Assert.Equal(upstream, File.ReadAllText(Path.Combine(repository.Path, "docs/cis/specs/technical-intent-spec.md")));
        var catalogPath = Path.Combine(repository.Path, "docs/cis/catalog.yml");
        var goodCatalog = File.ReadAllText(catalogPath);
        File.AppendAllText(catalogPath, "\n  - id: sample:reference:missing\n    path: docs/cis/references/missing.md\n    type: reference\n    status: draft\n    authority: canonical\n");
        var protectedPaths = new[] { design, Path.Combine(repository.Path, "docs/cis/references/component-sheet.md"), Path.Combine(repository.Path, "docs/cis/architecture/high-level-architecture-diagrams.md"), catalogPath };
        var snapshots = protectedPaths.ToDictionary(path => path, File.ReadAllText);
        var failed = Run("definition", "approve", "--page", "architecture", "--reviewer", "Different reviewer");
        Assert.Equal(5, failed.ExitCode);
        Assert.Contains("restored", failed.Output);
        foreach (var snapshot in snapshots) Assert.Equal(snapshot.Value, File.ReadAllText(snapshot.Key));
        File.WriteAllText(catalogPath, goodCatalog);
        File.AppendAllText(design, "\nA newly edited architecture boundary.\n");
        var edited = File.ReadAllText(design);
        Assert.Equal(5, Run("definition", "approve", "--page", "architecture", "--reviewer", "Architecture owner").ExitCode);
        Assert.Equal(edited, File.ReadAllText(design));
    }
}
