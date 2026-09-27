using System.Text.Json;

namespace Cis.Modules.Repository.Tests;

public sealed class MinimalImportTests
{
    [Fact]
    public void MinimalImportPreservesExistingDirectivesAndProducesASmallLocalReport()
    {
        using var repo = new Fixture();
        const string original = "# Team\r\nUse parrctx first for navigation.\r\nKeep approved architecture and release gates.\r\n";
        const string tests = "Use Microsoft Playwright for .NET. Run dotnet test. Test backend authorization and tenant isolation.\n";
        repo.Write("AGENTS.md", original);
        repo.Write(".github/instructions/tests.instructions.md", tests);
        repo.Write(".github/copilot-instructions.md", "# Copilot\nKeep the team's conventions.\n");
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        Assert.Equal(0, plan.ExitCode);
        Assert.Equal("minimal", plan.GuidanceMode);
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".cis")));
        Assert.Equal(2, plan.FileMerges.Count);
        Assert.StartsWith(original, plan.FileMerges.Single(item => item.RelativePath == "AGENTS.md").ProposedContent, StringComparison.Ordinal);
        Assert.Empty(plan.GuidanceProviders);
        Assert.Equal(0, plan.RemoteReviewBatchCount);
        Assert.True(plan.Repositories.Single().FilesToCreate.Count <= 17);
        Assert.DoesNotContain(plan.Repositories.Single().FilesToCreate, path => path.Contains("/skills/", StringComparison.Ordinal));
        var assessment = Assert.Single(plan.Assessments);
        Assert.Equal("preserve", assessment.Coverage.Single(item => item.Id == "browser").Action);
        Assert.Equal("add", assessment.Coverage.Single(item => item.Id == "browser-artifacts").Action);
        Assert.Equal("add", assessment.Setup.Single(item => item.Id == "reference.agent-provider-profile").Action);
        Assert.Contains(assessment.Findings, item => item.Kind == "routing-review");
        var result = importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash });
        Assert.True(result.Applied, string.Join("\n", result.Errors.Concat(result.Collisions)));
        Assert.Equal(tests, File.ReadAllText(Path.Combine(repo.Path, ".github/instructions/tests.instructions.md")));
        Assert.StartsWith(original, File.ReadAllText(Path.Combine(repo.Path, "AGENTS.md")), StringComparison.Ordinal);
        Assert.True(new CisRepositoryContextResolver().Resolve(repo.Path).IsSuccess);
        Assert.True(new WorkspaceRegistry(new CisRepositoryContextResolver()).Resolve(repo.Path).IsSuccess);
        Assert.True(File.Exists(Path.Combine(repo.Path, ".cis/local/import/report.json")));
        var repeated = importer.Import(request);
        Assert.Empty(repeated.FileMerges);
        Assert.Empty(repeated.Repositories.Single().FilesToUpdate);
        Assert.Empty(repeated.Repositories.Single().FilesToCreate);
    }

    [Fact]
    public void MissingTopicsGetScopedBaselinesWithoutTheFullStarterLibrary()
    {
        using var repo = new Fixture();
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        Assert.Equal(16, Assert.Single(plan.Assessments).Coverage.Count(item => item.Action == "add"));
        var guidance = plan.Previews.Single(item => item.RelativePath == RepositoryImportAssessmentBuilder.GuidancePath).ProposedContent;
        Assert.Contains("Existing project policy takes precedence", guidance, StringComparison.Ordinal);
        Assert.Contains("Playwright", guidance, StringComparison.Ordinal);
        Assert.Contains("authorization", guidance, StringComparison.Ordinal);
        Assert.Empty(plan.FileMerges);
        Assert.NotNull(plan.MergeReviewHash);
        Assert.Equal(3, importer.Import(request with { DryRun = false, Confirmed = true }).ExitCode);
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".cis")));
        Assert.True(importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash }).Applied);
        var repeated = importer.Import(request);
        Assert.Empty(repeated.Repositories.Single().FilesToUpdate);
        Assert.Empty(repeated.FileMerges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedOrNewGuidanceInvalidatesTheReviewedPlan(bool newFile)
    {
        using var repo = new Fixture();
        repo.Write("AGENTS.md", "# Team\n");
        repo.Write(".github/instructions/validation.instructions.md", "Run dotnet test.\n");
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        repo.Write(newFile ? ".github/instructions/new.instructions.md" : ".github/instructions/validation.instructions.md",
            "Keep project security controls.\n");
        var result = importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash });
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".cis")));
        Assert.Contains(result.Errors, item => item.Contains("changed after review", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludedEvidenceDefersGapAdditions()
    {
        using var repo = new Fixture();
        repo.Write(".github/instructions/large.instructions.md", new string('x', 256_001));
        var plan = Importer().Import(Request(repo));
        var report = Assert.Single(plan.Assessments);
        Assert.False(report.InventoryComplete);
        Assert.All(report.Coverage, item => Assert.Equal("defer", item.Action));
        Assert.Contains(report.Limitations, item => item.Contains("inventory limit", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkedStandardsAreInventoryEvidenceAndAreNeverRewritten()
    {
        using var repo = new Fixture();
        const string standard = "Our security policy: enforce authorization at each backend endpoint.\n";
        repo.Write("AGENTS.md", "Read [our policy](docs/team-policy.md).\n");
        repo.Write("docs/team-policy.md", standard);
        var plan = Importer().Import(Request(repo));
        Assert.Contains(Assert.Single(plan.Assessments).Coverage.Single(item => item.Id == "security").Sources, path => path == "docs/team-policy.md");
        Assert.DoesNotContain(plan.Repositories.Single().FilesToUpdate, path => path == "docs/team-policy.md");
        Assert.Equal(standard, File.ReadAllText(Path.Combine(repo.Path, "docs/team-policy.md")));
    }

    [Fact]
    public void ReviewedEntryPointEditsDoNotTriggerSemanticReview()
    {
        using var repo = new Fixture();
        using var edits = new Fixture();
        repo.Write("AGENTS.md", "# Team\nKeep this project rule.\n");
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        var proposal = Assert.Single(plan.FileMerges);
        var content = proposal.ProposedContent.Replace("## CIS integration", "## CIS tools", StringComparison.Ordinal);
        edits.Write("edits.json", JsonSerializer.Serialize(new[] { new { proposal.RepositoryPath, proposal.RelativePath, content } }));
        var result = importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash,
            MergeEditsPath = Path.Combine(edits.Path, "edits.json") });
        Assert.True(result.Applied, string.Join("\n", result.Errors.Concat(result.Collisions)));
        Assert.Equal(content, File.ReadAllText(Path.Combine(repo.Path, "AGENTS.md")));
        var repeated = importer.Import(request);
        Assert.Empty(repeated.FileMerges);
        Assert.DoesNotContain("AGENTS.md", repeated.Repositories.Single().FilesToUpdate);
    }

    [Fact]
    public void MinimalModeCannotAccidentallyInvokeTheOldModelMigration()
    {
        using var repo = new Fixture();
        var result = Importer().Import(Request(repo) with { MergeModel = new("codex", "test", true) });
        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Applied);
    }

    [Fact]
    public void DoctorDistinguishesDeferredCapabilityFromDamagedOrDeletedAdoptedConfiguration()
    {
        using var repo = new Fixture();
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        Assert.True(importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash }).Applied);
        var context = new CisRepositoryContextResolver().Resolve(repo.Path).Context!;
        var missing = new Cis.Abstractions.CisRepositoryDoctorFinding("CIS-TEST-DOCTOR-001", "error", "testing", "Missing profile", [], "Configure", "cis test validate", "review-required");
        var pending = RepositoryDoctor.DescribeDeferredCapability(context, missing);
        Assert.Equal("setup-pending", pending.Fixability);
        Assert.Equal("warning", pending.Severity);
        const string profile = "docs/cis/references/test-suite-profile.md";
        repo.Write(profile, "Invalid profile\n");
        Assert.Equal(missing, RepositoryDoctor.DescribeDeferredCapability(context, missing));
        File.Delete(Path.Combine(repo.Path, profile));
        var store = new StarterManifestStore();
        var manifestPath = Path.Combine(repo.Path, ".cis/starter-manifest.yml");
        var manifest = store.Read(manifestPath).Manifest!;
        File.WriteAllText(manifestPath, store.Write(manifest with { ManagedArtifacts = [.. manifest.ManagedArtifacts,
            new("testing", profile, "testing", 1, "sha256:test")] }));
        Assert.Equal(missing, RepositoryDoctor.DescribeDeferredCapability(context, missing));
        Assert.Equal(missing with { Code = "OTHER-ERROR" }, RepositoryDoctor.DescribeDeferredCapability(context, missing with { Code = "OTHER-ERROR" }));
    }

    [Theory]
    [InlineData(".github/tmp/manual-tests")]
    [InlineData(".github/TEMP")]
    [InlineData(".github/instructions/.tmp")]
    [InlineData(".github/skills/cache")]
    [InlineData(".github/test-results")]
    [InlineData(".github/playwright-report")]
    [InlineData(".github/copilot-runtime")]
    public void TemporaryEvidenceIsExcludedFromAllImportScansAndReviewHash(string directory)
    {
        using var repo = new Fixture();
        repo.Write("AGENTS.md", $"Read [temporary policy]({directory}/policy.md).\nRead `.github/scripts/../tmp/ignored.md`.\n");
        repo.Write(directory + "/policy.md", "Use Microsoft Playwright for .NET.\n");
        repo.Write(directory + "/Fake.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repo.Write(directory + "/Dockerfile", "FROM temporary\n");
        repo.Write(".github/tmp/ignored.md", "Temporary fixture\n");
        repo.Write(".github/instructions/team.instructions.md", "Keep project rules.\n");
        var importer = Importer();
        var plan = importer.Import(Request(repo));
        var report = Assert.Single(plan.Assessments);
        Assert.True(report.InventoryComplete);
        Assert.DoesNotContain(report.Inventory, item => item.Path.StartsWith(".github/", StringComparison.Ordinal) && RepositoryScanExclusions.IsPath(item.Path));
        Assert.Equal("add", report.Coverage.Single(item => item.Id == "browser").Action);
        Assert.Empty(new RepositoryClassifier().Classify(repo.Path).Components);
        Assert.Empty(RepositorySecurityStarter.EnumerateDockerfiles(repo.Path));
        var warnings = new List<string>();
        var discovered = RepositoryGuidanceDiscovery.Discover(repo.Path, new HashSet<string>(), warnings);
        Assert.Equal([".github/instructions/team.instructions.md"], discovered);
        Assert.Empty(warnings);
        repo.Write(directory + "/policy.md", "Completely changed disposable evidence.\n");
        repo.Write(directory + "/new.instructions.md", "More temporary guidance.\n");
        Assert.Equal(plan.MergeReviewHash, importer.Import(Request(repo)).MergeReviewHash);
    }

    [Fact]
    public void AuthoredScriptsWorkflowsAndTemplatesRemainEligibleEvidence()
    {
        using var repo = new Fixture();
        repo.Write("AGENTS.md", "Read [workflow](.github/workflows/verify.yml), [script](.github/scripts/check.ps1), and [template](.github/templates/policy.md).\n");
        repo.Write(".github/workflows/verify.yml", "name: Verification\n");
        repo.Write(".github/scripts/check.ps1", "dotnet test\n");
        repo.Write(".github/templates/policy.md", "Never expose credentials.\n");
        var report = Assert.Single(Importer().Import(Request(repo)).Assessments);
        Assert.Contains(report.Inventory, item => item.Path == ".github/workflows/verify.yml");
        Assert.Contains(report.Inventory, item => item.Path == ".github/scripts/check.ps1");
        Assert.Contains(report.Inventory, item => item.Path == ".github/templates/policy.md");
    }

    [Fact]
    public void TopicNamesDoNotSuppressIndividualRequirementsOrCoreConfiguration()
    {
        using var repo = new Fixture();
        repo.Write(".github/instructions/security-playwright-tests.instructions.md", "# Security, Playwright and testing\n");
        var plan = Importer().Import(Request(repo));
        var report = Assert.Single(plan.Assessments);
        Assert.All(report.Coverage, item => Assert.Equal("add", item.Action));
        Assert.Contains(plan.Previews, item => item.RelativePath == "docs/cis/references/ai-routing-profile.md");
        Assert.Equal("defer", report.Setup.Single(item => item.Id == "reference.security-suite-profile").Action);
        Assert.DoesNotContain(plan.Previews, item => item.RelativePath.EndsWith("test-suite-profile.md", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Previews, item => item.RelativePath.EndsWith("security-suite-profile.md", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingProfilesArePreservedAndChangesInvalidateReview()
    {
        using var repo = new Fixture();
        const string path = "docs/cis/references/agent-provider-profile.md";
        const string original = "# Team-owned provider choices\nCustom review required.\n";
        repo.Write(path, original);
        var importer = Importer();
        var request = Request(repo);
        var plan = importer.Import(request);
        Assert.Empty(plan.Collisions);
        Assert.DoesNotContain(plan.Previews, item => item.RelativePath == path);
        Assert.Equal("preserve", Assert.Single(plan.Assessments).Setup.Single(item => item.Id == "reference.agent-provider-profile").Action);
        repo.Write(path, original + "Changed policy\n");
        var stale = importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = plan.MergeReviewHash });
        Assert.Equal(2, stale.ExitCode);
        Assert.False(File.Exists(Path.Combine(repo.Path, ".cis/repository.yml")));
        var refreshed = importer.Import(request);
        Assert.True(importer.Import(request with { DryRun = false, Confirmed = true, MergeReviewHash = refreshed.MergeReviewHash }).Applied);
        Assert.Equal(original + "Changed policy\n", File.ReadAllText(Path.Combine(repo.Path, path)));
    }

    [Fact]
    public void MinimalTestBindingPreservesDotNetPlaywrightWithoutInstallingASubstituteRunner()
    {
        using var repo = new Fixture();
        repo.Write("tests/Browser/Browser.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\"/><PackageReference Include=\"Microsoft.Playwright.NUnit\" Version=\"1.0.0\"/></ItemGroup></Project>");
        var plan = Importer().Import(Request(repo));
        var profile = plan.Previews.Single(item => item.RelativePath == "docs/cis/references/test-suite-profile.md").ProposedContent;
        Assert.Contains("dotnet test", profile, StringComparison.Ordinal);
        Assert.Contains("| browser | dotnet-test |", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("npm", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("cis-docs", profile, StringComparison.Ordinal);
        Assert.Contains("not been executed or verified", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedTestBindingsKeepExplicitLayersAndDoNotInventCoverageCollectors()
    {
        using var repo = new Fixture();
        repo.Write("Application.sln", "");
        foreach (var layer in new[] { "business", "integration", "unit" })
            repo.Write($"tests/{layer}/Example.{layer}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\"/></ItemGroup></Project>");
        var plan = Importer().Import(Request(repo));
        var profile = plan.Previews.Single(item => item.RelativePath == "docs/cis/references/test-suite-profile.md").ProposedContent;
        Assert.Contains("| business | dotnet-test |", profile, StringComparison.Ordinal);
        Assert.Contains("| integration | dotnet-test |", profile, StringComparison.Ordinal);
        Assert.Contains("| unit | dotnet-test |", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test Application.sln", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("--collect", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("coverage.cobertura.xml", profile, StringComparison.Ordinal);
    }

    private static RepositoryImporter Importer() => new(new RepositoryInitializer(), new WorkspaceRegistry(new CisRepositoryContextResolver()));
    private static RepositoryImportRequest Request(Fixture repo) => new(repo.Path, "docs/cis", [repo.Path], true, false,
        "owned", "none", EcosystemId: "example", ProductId: "example", MinimalImport: true);
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cis-minimal-import-").FullName;
        public void Write(string path, string content) { var full = System.IO.Path.Combine(Path, path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!); File.WriteAllText(full, content); }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
