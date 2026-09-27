using System.Text.Json;

namespace Cis.Modules.Repository.Tests;

public sealed class ConnectedGuidanceTests
{
    [Fact]
    public void DiscoversUnlinkedInstructionsSkillsAgentsAndPromptsBeyondTheOldEightFileLimit()
    {
        using var repo = new Fixture();
        repo.Write("AGENTS.md", "# Team\n");
        for (var i = 0; i < 10; i++) repo.Write($".github/instructions/route-{i}.instructions.md", "Route with legacy-index.\n");
        repo.Write(".github/skills/scaffold/SKILL.md", "# Scaffold\nUse legacy routing before generation.\n");
        repo.Write(".github/agents/docs.agent.md", "Use legacy routing.\n");
        repo.Write(".github/prompts/register.prompt.md", "Register docs in the old catalogue.\n");
        var plan = Importer().Import(Request(repo));
        Assert.Equal(0, plan.ExitCode);
        Assert.Equal(14, plan.FileMerges.Count);
        Assert.Contains(plan.FileMerges, item => item.RelativePath == ".github/skills/scaffold/SKILL.md");
        Assert.Contains(plan.FileMerges, item => item.RelativePath == ".github/agents/docs.agent.md");
        Assert.All(plan.FileMerges, item => Assert.Contains(item.GuidanceSources, source => source.Path.EndsWith("cis-engineering-assurance.instructions.md", StringComparison.Ordinal)));
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".cis")));
    }

    [Fact]
    public void RetirementPreservesOriginalOutsideDiscoveryAndRequiresTheReviewedSet()
    {
        using var repo = new Fixture();
        using var edits = new Fixture();
        repo.Write("AGENTS.md", "# Team\nRead `.github/instructions/obsolete.instructions.md`.\n");
        const string path = ".github/instructions/obsolete.instructions.md";
        const string original = "---\napplyTo: '**'\n---\nUse legacy routing.\n";
        repo.Write(path, original);
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        edits.Write("edits.json", JsonSerializer.Serialize(plan.FileMerges.Select(merge => new {
            merge.RepositoryPath, merge.RelativePath, content = merge.RelativePath == "AGENTS.md" ? "# Reviewed CIS guidance\n" : "",
            retire = merge.RelativePath == path,
        })));
        var result = importer.Import(request with { DryRun = false, Confirmed = true,
            MergeReviewHash = plan.MergeReviewHash, MergeEditsPath = Path.Combine(edits.Path, "edits.json") });
        Assert.True(result.Applied, string.Join("\n", result.Errors.Concat(result.Collisions)));
        Assert.False(File.Exists(Path.Combine(repo.Path, path)));
        var backup = Assert.Single(Directory.EnumerateFiles(Path.Combine(repo.Path, ".cis", "retired-guidance"), "obsolete.instructions.md", SearchOption.AllDirectories));
        Assert.Equal(original, File.ReadAllText(backup));
        var repeated = importer.Import(request);
        Assert.Empty(repeated.FileMerges);
    }

    [Fact]
    public void RejectsIncomingRetirementLinksAndStaleAutomationBeforeWriting()
    {
        using var repo = new Fixture();
        using var edits = new Fixture();
        repo.Write("AGENTS.md", "Read `.github/instructions/obsolete.instructions.md`. Run `legacy route`.\n");
        repo.Write(".github/instructions/obsolete.instructions.md", "Use legacy route.\n");
        repo.Write(".github/workflows/check.yml", "run: legacy route\n");
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        edits.Write("edits.json", JsonSerializer.Serialize(plan.FileMerges.Select(merge => new {
            merge.RepositoryPath, merge.RelativePath, content = merge.CurrentContent,
            retire = merge.RelativePath.EndsWith("obsolete.instructions.md", StringComparison.Ordinal),
        })));
        var apply = request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash,
            MergeEditsPath = Path.Combine(edits.Path, "edits.json") };
        Assert.Equal(2, importer.Import(apply).ExitCode);
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".cis")));
        repo.Write(".github/workflows/check.yml", "run: legacy route --changed\n");
        var stale = importer.Import(apply);
        Assert.Contains(stale.Errors, error => error.Contains("changed after review", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".cis")));
    }

    [Fact]
    public void DependencyReviewReportsRemainingAutomationWithoutExecutingItOrSendingItToModels()
    {
        using var repo = new Fixture();
        repo.Write(".github/scripts/gate.py", "raise Exception('must never execute')\n# legacy route\n");
        var merge = new RepositoryFileMerge(repo.Path, "AGENTS.md", "Run `legacy route`.\n", "Use CIS routing.\n", "test");
        var attached = RepositoryGuidanceValidation.AttachAutomation([merge]);
        Assert.DoesNotContain("must never execute", JsonSerializer.Serialize(attached), StringComparison.Ordinal);
        var checkedMerge = Assert.Single(RepositoryGuidanceValidation.Validate(attached));
        var finding = Assert.Single(checkedMerge.GuidanceReview!.Findings);
        Assert.Equal("enforcement", finding.Kind);
        Assert.Equal(".github/scripts/gate.py", finding.Path);
        Assert.Contains("review candidate", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NumberedPreparationRemainsOneProcedureAcrossIndentedExamplesAndParagraphs()
    {
        const string source = "2. List templates.\n\n   ```sh\n   tool list\n   ```\n\n   Select the smallest slice.\n\n3. Describe it.\n\n   ~~~sh\n   tool describe\n   ~~~\n\n4. Prepare verified facts.\n5. Validate.\n";
        var result = RepositoryGuidanceFormatting.Clean(source);
        Assert.Contains("1. List templates.", result, StringComparison.Ordinal);
        Assert.Contains("2. Describe it.", result, StringComparison.Ordinal);
        Assert.Contains("3. Prepare verified facts.\n4. Validate.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("- List", result, StringComparison.Ordinal);
        Assert.Equal(result, RepositoryGuidanceFormatting.Clean(result));
    }

    [Fact]
    public void ExistingDotNetPlaywrightGetsBrowserEvidenceInsteadOfASolutionWideUnitLabel()
    {
        using var repo = new Fixture();
        repo.Write("App.sln", "");
        repo.Write("tests/Browser/Browser.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"1\"/><PackageReference Include=\"Microsoft.Playwright\" Version=\"1\"/></ItemGroup></Project>");
        repo.Write("tests/Unit/Unit.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"1\"/></ItemGroup></Project>");
        var classification = new RepositoryClassifier().Classify(repo.Path);
        Assert.Contains(classification.Components, component => component.Frameworks.Contains("playwright-dotnet"));
        var starter = RepositoryTestingStarter.Create(repo.Path, "docs/cis", classification);
        Assert.Contains("| browser | dotnet-test | dotnet test tests/Browser/Browser.csproj", starter.Profile, StringComparison.Ordinal);
        Assert.Contains("| trx |", starter.Profile, StringComparison.Ordinal);
        Assert.Contains("matching Playwright browsers", starter.Profile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test App.sln", starter.Profile, StringComparison.Ordinal);
        Assert.DoesNotContain("playwright install chromium", starter.Workflow, StringComparison.Ordinal);
        Assert.Contains("| unit | dotnet-test | dotnet test tests/Unit/Unit.csproj", starter.Profile, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ΓÇö", true)]
    [InlineData("— café", false)]
    [InlineData("\uFFFD", true)]
    public void DetectsIntroducedEncodingCorruption(string text, bool expected) =>
        Assert.Equal(expected, RepositoryGuidanceValidation.HasBrokenEncoding(text));

    private static RepositoryImporter Importer() => new(new RepositoryInitializer(), new WorkspaceRegistry(new CisRepositoryContextResolver()));
    private static RepositoryImportRequest Request(Fixture repo) => new(repo.Path, "docs/cis", [repo.Path], true, false,
        "owned", "none", EcosystemId: "example", ProductId: "example", MinimalImport: false);
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cis-connected-test-").FullName;
        public void Write(string path, string content) { var full = System.IO.Path.Combine(Path, path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!); File.WriteAllText(full, content); }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
