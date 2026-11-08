using System.Security.Cryptography;
using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class EngineeringAdoptionTests
{
    [Fact]
    public void FreshGovernedRepositoryEmitsCurrentAssuranceAndHeadlessWorkRoutes()
    {
        using var repo = new Fixture();
        var initializer = new RepositoryInitializer();

        var initialized = initializer.Initialize(new(repo.Path, "docs", false, true, DeclaredStack: "csharp"));

        Assert.Equal(0, initialized.ExitCode);
        var context = Assert.IsType<CisRepositoryContext>(new CisRepositoryContextResolver().Resolve(repo.Path).Context);
        var assessment = new EngineeringAssessmentService().Assess(context, "documentation");
        Assert.True(assessment.Adopted);
        Assert.Empty(assessment.Diagnostics);
        Assert.Contains(assessment.RequiredGates, gate => gate.Id == "review" && !gate.AllowsInapplicability);
        AssertCurrentOperatingGuidance(repo);
        Assert.Contains("AGENTS.md", repo.Read("CLAUDE.md"), StringComparison.Ordinal);
        Assert.Contains("cis agent work progress",
            repo.Read(".github/instructions/cis-agent-execution.instructions.md"), StringComparison.Ordinal);
        var work = repo.Read(".github/skills/cis-agent-execution/SKILL.md");
        foreach (var route in new[] { "cis agent work status", "cis agent work progress", "cis agent work disposition", "cis agent work amend-time" })
            Assert.Contains(route, work, StringComparison.Ordinal);
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, CisAgentWorkPaths.Ledger)));
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, CisAgentWorkPaths.Adoption)));
        Assert.Equal("unchanged", initializer.Initialize(new(repo.Path, "docs", false, true)).Status);
    }

    [Fact]
    public void GuidanceUpgradePreservesCustomNativePolicyInputsAndRetainedHistory()
    {
        using var repo = new Fixture();
        var initializer = new RepositoryInitializer();
        repo.Write("src/Example/Example.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        repo.Write("tests/Example.Tests/Example.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\"/><PackageReference Include=\"NUnit\" Version=\"4.0.0\"/></ItemGroup></Project>");
        var request = new RepositoryInitRequest(repo.Path, "docs", false, true, AdoptEngineeringDefaults: true);
        Assert.Equal(0, initializer.Initialize(request).ExitCode);

        const string profile = "docs/references/test-suite-profile.md";
        const string command = "dotnet test tests/Example.Tests/Example.Tests.csproj --filter TestCategory=Adoption --logger trx;LogFileName=custom-unit.trx --results-directory .cis/local/testing/custom-results";
        repo.Write(profile, "# Team native binding\n\n"
            + "| Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path |\n"
            + "|---|---|---|---|---|---|---|---|---|---|\n"
            + $"| custom-unit | repository | unit | dotnet-test | {command} | . | trx | .cis/local/testing/custom-results/custom-unit.trx | - | - |\n");
        repo.Write(EngineeringAssessmentService.PolicyPath, """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[],"minimumCoverageLines":99,"minimumMutationScore":90}""");
        File.AppendAllText(System.IO.Path.Combine(repo.Path, "AGENTS.md"), "\n## Team rule\nPreserve the existing NUnit harness and release approval.\n");
        repo.Write("docs/specs/product-intent-spec.md", "# Accepted synthetic business intent\nKeep the reviewed behavior and compatibility obligations.\n");
        repo.Write("docs/specs/technical-intent-spec.md", "# Accepted synthetic technical intent\nKeep NUnit and the explicit native test command.\n");
        Assert.Equal(0, initializer.Initialize(request with { AcceptCurrent = true }).ExitCode);
        repo.Write(".cis/local/testing/prior-outcome.json", """{"fixture":"prior retained evidence","status":"unverified"}""");
        repo.Write(".cis/local/agents/prior-decisions.jsonl", """{"fixture":"prior decision history","decision":"preserve existing compatibility"}""");
        var preservedPaths = new[] { "AGENTS.md", profile, EngineeringAssessmentService.PolicyPath,
            "docs/specs/product-intent-spec.md", "docs/specs/technical-intent-spec.md",
            ".cis/local/testing/prior-outcome.json", ".cis/local/agents/prior-decisions.jsonl" };
        var preserved = preservedPaths.ToDictionary(path => path, path => File.ReadAllBytes(System.IO.Path.Combine(repo.Path, path)));
        var manifestPath = System.IO.Path.Combine(repo.Path, ".cis/starter-manifest.yml");
        var store = new StarterManifestStore();
        var before = Assert.IsType<StarterManifest>(store.Read(manifestPath).Manifest);
        const string instruction = ".github/instructions/cis-engineering-assurance.instructions.md";
        const string priorManaged = "---\napplyTo: '**'\n---\n# Prior managed assurance\nAt each task end rebuild the graph and reassess all standards.\n";
        // A historical generated version is fixture input. Reconciliation itself must apply the upgrade.
        repo.Write(instruction, priorManaged);
        File.WriteAllText(manifestPath, store.Write(before with
        {
            ManagedArtifacts = before.ManagedArtifacts.Select(item => item.Path == instruction
                ? item with { AppliedHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(priorManaged))) }
                : item).ToArray(),
        }));

        var upgraded = initializer.Initialize(request);

        Assert.Equal(0, upgraded.ExitCode);
        Assert.Contains(instruction, upgraded.FilesToUpdate);
        AssertCurrentOperatingGuidance(repo);
        foreach (var (path, bytes) in preserved) Assert.Equal(bytes, File.ReadAllBytes(System.IO.Path.Combine(repo.Path, path)));
        var policy = Assert.IsType<EngineeringDefaultsPolicy>(CisEngineeringPolicy.Read(repo.Path));
        Assert.Equal(99, policy.MinimumCoverageLines);
        Assert.Equal(90, policy.MinimumMutationScore);
        Assert.Contains(command, repo.Read(profile), StringComparison.Ordinal);
        Assert.Contains(new RepositoryClassifier().Classify(repo.Path).Components, component => component.Frameworks.Contains("nunit"));
        var after = Assert.IsType<StarterManifest>(store.Read(manifestPath).Manifest);
        foreach (var path in preservedPaths.Take(5))
        {
            var prior = Assert.Single(before.ManagedArtifacts, item => item.Path == path);
            var current = Assert.Single(after.ManagedArtifacts, item => item.Path == path);
            Assert.Equal("human", current.Ownership);
            Assert.Equal(prior, current);
        }
        var repeated = initializer.Initialize(request with { DryRun = true, Confirmed = false });
        Assert.Equal("dry-run", repeated.Status);
        Assert.Empty(repeated.FilesToCreate);
        Assert.Empty(repeated.FilesToUpdate);
        Assert.Empty(repeated.FileMerges);
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, CisAgentWorkPaths.Ledger)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedPolicyCannotBeDisabledByEditingFalse(bool removeStarterHistory)
    {
        using var repo = new Fixture();
        Assert.Equal(0, new RepositoryInitializer().Initialize(new(repo.Path, "docs", false, true)).ExitCode);
        if (removeStarterHistory) File.Delete(System.IO.Path.Combine(repo.Path, ".cis/starter-manifest.yml"));
        repo.Write(EngineeringAssessmentService.PolicyPath, """{"schemaVersion":1,"requireTaskCompletion":false}""");
        var assessment = new EngineeringAssessmentService().Assess(repo.Context, "backend");
        Assert.True(assessment.Adopted);
        Assert.Contains(assessment.Diagnostics, value => value.Contains("cannot be disabled", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => CisExecutionIdentity.CaptureIfAdopted(repo.Context));
    }

    [Theory]
    [InlineData("- Root: `src/Added`", false)]
    [InlineData("- root: src/Added", false)]
    [InlineData("- Root: `src/Added`", true)]
    public void CustomizedProfileReportsUnownedGrowthWithoutReplacingGuidance(string ownership, bool infrastructure)
    {
        using var repo = new Fixture();
        var initializer = new RepositoryInitializer();
        if (infrastructure) repo.Write("docker-compose.yml", "services: {}\n");
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true, AdoptEngineeringDefaults: true)).ExitCode);
        var profilePath = System.IO.Path.Combine(repo.Path, "docs/references/repository-profile.md");
        File.WriteAllText(profilePath, "- Root: `src/Added`\n" + File.ReadAllText(profilePath)
            + "\nMaintainer-owned guidance must remain.\n");
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true, AcceptCurrent: true)).ExitCode);
        var before = File.ReadAllBytes(profilePath);
        repo.Write("src/Added/Added.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true)).ExitCode);

        var findings = new EngineeringAlignmentDoctorCheck().Inspect(repo.Context);

        Assert.Contains(findings, finding => finding.Code == "CIS-ENGINEERING-004"
            && finding.Evidence.Any(item => item.Contains("src/Added", StringComparison.Ordinal)));
        Assert.Equal(before, File.ReadAllBytes(profilePath));
        File.AppendAllText(profilePath, "\n### reviewed-owner\n\n" + ownership + "\n");
        Assert.DoesNotContain(new EngineeringAlignmentDoctorCheck().Inspect(repo.Context),
            finding => finding.Code == "CIS-ENGINEERING-004");
    }

    [Fact]
    public void ExistingRepositoryRequiresExplicitAdoptionEvenWithDeclaredStack()
    {
        using var repo = new Fixture();
        repo.Write("README.md", "An existing project.");
        var initializer = new RepositoryInitializer();
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, EngineeringAssessmentService.PolicyPath)));
        Assert.False(new EngineeringAssessmentService().Assess(repo.Context, "backend").Adopted);
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", true, false, AdoptEngineeringDefaults: true)).ExitCode);
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, EngineeringAssessmentService.PolicyPath)));
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true, AdoptEngineeringDefaults: true)).ExitCode);
        Assert.True(new EngineeringAssessmentService().Assess(repo.Context, "backend").Adopted);
    }

    [Fact]
    public void ImplementationCannotWaiveBuildUnitOrCoverageAndStrongerThresholdsArePreserved()
    {
        using var repo = new Fixture();
        repo.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        repo.Write(EngineeringAssessmentService.PolicyPath, """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[],"minimumCoverageLines":99,"minimumMutationScore":90}""");
        var policy = CisEngineeringPolicy.Read(repo.Path)!;
        Assert.Equal(99, policy.MinimumCoverageLines);
        Assert.Equal(90, policy.MinimumMutationScore);
        var gates = new EngineeringAssessmentService().Assess(repo.Context, "backend").RequiredGates;
        foreach (var id in new[] { "build", "unit" }) Assert.False(Assert.Single(gates, gate => gate.Id == id).AllowsInapplicability);
        repo.Write(EngineeringAssessmentService.PolicyPath, """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[],"minimumCoverageLines":94}""");
        Assert.Throws<InvalidDataException>(() => CisEngineeringPolicy.Read(repo.Path));
    }

    [Fact]
    public void DeclaredCSharpSeedsGuidanceWithoutInventingSourceOrFrameworkAdoption()
    {
        using var repo = new Fixture();
        var initializer = new RepositoryInitializer();
        var preview = initializer.Initialize(new(repo.Path, "docs", true, false, DeclaredStack: "csharp"));
        Assert.Equal(0, preview.ExitCode);
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, ".cis/engineering-stack.json")));
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        var classification = new RepositoryClassifier().Classify(repo.Path);
        Assert.Equal("csharp", classification.DeclaredStack);
        Assert.Empty(classification.Components);
        Assert.True(File.Exists(System.IO.Path.Combine(repo.Path, ".github/skills/cis-dotnet-test/SKILL.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(repo.Path, "docs/standards/code-quality-standard.md")));
        Assert.Equal("unchanged", initializer.Initialize(new(repo.Path, "docs", false, true)).Status);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Fact]
    public void GrowthReassessesAllStandardsAndSkillsWithoutApplyingThePreview()
    {
        using var repo = new Fixture();
        var initializer = new RepositoryInitializer();
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true)).ExitCode);
        Assert.Empty(new EngineeringAlignmentDoctorCheck().Inspect(repo.Context));
        repo.Write("src/Example.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var findings = new EngineeringAlignmentDoctorCheck().Inspect(repo.Context);
        var growth = Assert.Single(findings, finding => finding.Code == "CIS-ENGINEERING-003");
        Assert.Contains(growth.Evidence, path => path.Contains("code-quality", StringComparison.Ordinal));
        Assert.Contains(growth.Evidence, path => path.Contains("agent-interface", StringComparison.Ordinal));
        Assert.Contains(growth.Evidence, path => path.Contains("cis-dotnet-test", StringComparison.Ordinal));
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, "docs/standards/code-quality-standard.md")));
        var requirements = new EngineeringAssessmentService().Assess(repo.Context, "backend");
        Assert.Contains(requirements.RequiredGates, gate => gate.Id == "unit");
        Assert.Contains(requirements.RequiredGates, gate => gate.Id == "instrumentation");
        Assert.Equal(0, initializer.Initialize(new(repo.Path, "docs", false, true)).ExitCode);
        Assert.Empty(new EngineeringAlignmentDoctorCheck().Inspect(repo.Context));
        Assert.Equal("unchanged", initializer.Initialize(new(repo.Path, "docs", false, true)).Status);
    }

    [Fact]
    public void DeletingAdoptedPolicyCannotSilentlyDisableCompletion()
    {
        using var repo = new Fixture();
        Assert.Equal(0, new RepositoryInitializer().Initialize(new(repo.Path, "docs", false, true)).ExitCode);
        File.Delete(System.IO.Path.Combine(repo.Path, EngineeringAssessmentService.PolicyPath));
        var result = new EngineeringAssessmentService().Assess(repo.Context, "documentation");
        Assert.True(result.Adopted);
        Assert.Contains(result.Diagnostics, message => message.Contains("missing", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => CisExecutionIdentity.CaptureIfAdopted(repo.Context));
    }

    [Fact]
    public void ExplicitMinimalOptOutAndNeverAdoptedRepositoryAgreeAcrossReaders()
    {
        using var repo = new Fixture();
        Assert.False(new EngineeringAssessmentService().Assess(repo.Context, "backend").Adopted);
        Assert.Null(CisExecutionIdentity.CaptureIfAdopted(repo.Context));
        repo.Write(EngineeringAssessmentService.PolicyPath, """{"schemaVersion":1,"requireTaskCompletion":false}""");
        Assert.False(new EngineeringAssessmentService().Assess(repo.Context, "backend").Adopted);
        Assert.Null(CisExecutionIdentity.CaptureIfAdopted(repo.Context));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"requireTaskCompletion\":true,\"additionalGates\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"additionalGates\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"requireTaskCompletion\":true}")]
    [InlineData("{\"schemaVersion\":\"1\",\"requireTaskCompletion\":true,\"additionalGates\":[]}")]
    public void InvalidPolicyCannotBeInterpretedAsDisabled(string policy)
    {
        using var repo = new Fixture();
        repo.Write(EngineeringAssessmentService.PolicyPath, policy);
        var result = new EngineeringAssessmentService().Assess(repo.Context, "backend");
        Assert.True(result.Adopted);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Throws<InvalidDataException>(() => CisExecutionIdentity.CaptureIfAdopted(repo.Context));
    }

    [Fact]
    public void CustomPolicyAddsGatesAndDocumentationScopeDoesNotInheritRuntimeLayers()
    {
        using var repo = new Fixture();
        repo.Write(EngineeringAssessmentService.PolicyPath, "{\"schemaVersion\":1,\"requireTaskCompletion\":true,\"additionalGates\":[\"team-contract\"]}");
        repo.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var result = new EngineeringAssessmentService().Assess(repo.Context, "documentation");
        Assert.Contains(result.RequiredGates, gate => gate.Id == "team-contract");
        Assert.Contains(result.RequiredGates, gate => gate.Id == "review");
        Assert.DoesNotContain(result.RequiredGates, gate => gate.Id == "unit");
        Assert.Contains(new EngineeringAssessmentService().Assess(repo.Context, "custom-implementation").RequiredGates, gate => gate.Id == "unit");
    }

    [Fact]
    public void MtpFrameworkIsPreservedWithoutFabricatedVstestBinding()
    {
        using var repo = new Fixture();
        repo.Write("Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" /></ItemGroup></Project>");
        var classification = new RepositoryClassifier().Classify(repo.Path);
        Assert.Contains("tunit", Assert.Single(classification.Components).Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
        Assert.DoesNotContain("--logger trx", RepositoryTestingStarter.Create(repo.Path, "docs", classification).Workflow, StringComparison.Ordinal);
    }

    private static void AssertCurrentOperatingGuidance(Fixture repo)
    {
        static string Words(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var agents = repo.Read("AGENTS.md");
        Assert.Contains(".github/instructions/cis-repository.instructions.md", agents, StringComparison.Ordinal);
        var routing = repo.Read(".github/instructions/cis-repository.instructions.md");
        Assert.Contains(".github/instructions/cis-engineering-assurance.instructions.md", routing, StringComparison.Ordinal);
        Assert.Contains(".github/skills/cis-feedback-loop/SKILL.md", routing, StringComparison.Ordinal);
        var assurance = Words(repo.Read(".github/instructions/cis-engineering-assurance.instructions.md"));
        Assert.Contains("retain current applicable graph, CIS dependency, standards, skill, review and verification assessments", assurance, StringComparison.Ordinal);
        Assert.Contains("refresh only affected or unresolved portions", assurance, StringComparison.Ordinal);
        Assert.DoesNotContain("At each task end rebuild the graph", assurance, StringComparison.Ordinal);
        Assert.Contains("optional advice does not create a compulsory correction", assurance, StringComparison.Ordinal);
        var tooling = Words(repo.Read(".github/skills/cis-tooling-evidence/SKILL.md"));
        Assert.Contains("Resolve required validator diagnostics, including warnings that block the declared mode or adopted gate", tooling, StringComparison.Ordinal);
        Assert.Contains("Optional review advice alone does not block readiness or require correction", tooling, StringComparison.Ordinal);
        var feedback = Words(repo.Read(".github/skills/cis-feedback-loop/SKILL.md"));
        Assert.Contains("Run Doctor only when repository state or prerequisites may be involved", feedback, StringComparison.Ordinal);
        Assert.Contains("Repeated product-test failures require diagnosis or a changed hypothesis", feedback, StringComparison.Ordinal);
        Assert.Contains("Run `cis repo doctor` only when repository state or prerequisites may be involved",
            Words(repo.Read(".github/instructions/cis-feedback-loop.instructions.md")), StringComparison.Ordinal);
        Assert.Contains("one meaningful native test can support several criteria",
            Words(repo.Read("docs/standards/testing-standard.md")), StringComparison.Ordinal);
        Assert.Contains("A compatible assessment MAY be reused when its material basis is unchanged",
            Words(repo.Read("docs/standards/iteration-completion-standard.md")), StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-adoption-tests");
        public string Path { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public CisRepositoryContext Context => new(Path, "example", "docs", System.IO.Path.Combine(Path, "docs"), System.IO.Path.Combine(Path, "docs/catalog.yml"));
        public Fixture() => Directory.CreateDirectory(Path);
        public string Read(string relative) => File.ReadAllText(System.IO.Path.Combine(Path, relative));
        public void Write(string relative, string text)
        {
            var path = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Root, Path)) throw new InvalidOperationException();
            Directory.Delete(Path, true);
        }
    }
}
