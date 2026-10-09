using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class EngineeringAdoptionTests
{
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

    private sealed class Fixture : IDisposable
    {
        private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-adoption-tests");
        public string Path { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public CisRepositoryContext Context => new(Path, "example", "docs", System.IO.Path.Combine(Path, "docs"), System.IO.Path.Combine(Path, "docs/catalog.yml"));
        public Fixture() => Directory.CreateDirectory(Path);
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
