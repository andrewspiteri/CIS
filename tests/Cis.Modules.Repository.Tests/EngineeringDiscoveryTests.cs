namespace Cis.Modules.Repository.Tests;

public sealed class EngineeringDiscoveryTests
{
    [Theory]
    [InlineData("global")]
    [InlineData("property")]
    [InlineData("update")]
    [InlineData("mstest-sdk")]
    [InlineData("runner-property")]
    [InlineData("malformed-global")]
    public void UnqualifiedRunnerIsVisibleAndCannotAcquireAVstestBinding(string scenario)
    {
        using var repo = new Fixture();
        repo.Write("Tests.csproj", scenario == "update"
            ? "<Project><ItemGroup><PackageReference Update=\"xunit\" /></ItemGroup></Project>"
            : "<Project><ItemGroup><PackageReference Include=\"xunit.v3\" /></ItemGroup></Project>");
        repo.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        if (scenario == "global") repo.Write("global.json", """{"test":{"runner":"Microsoft.Testing.Platform"}}""");
        if (scenario == "property") repo.Write("Directory.Build.props", "<Project><PropertyGroup><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport></PropertyGroup></Project>");
        if (scenario == "runner-property") repo.Write("Directory.Build.props", "<Project><PropertyGroup><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner></PropertyGroup></Project>");
        if (scenario == "mstest-sdk") repo.Write("Tests.csproj", "<Project Sdk=\"MSTest.Sdk/3.0.0\" />");
        if (scenario == "malformed-global") repo.Write("global.json", "{invalid");
        var classification = new RepositoryClassifier().Classify(repo.Path);
        Assert.Contains(classification.Warnings, value => value.Contains("qualification", StringComparison.Ordinal));
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
        Assert.DoesNotContain("--logger trx", RepositoryTestingStarter.Create(repo.Path, "docs", classification).Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-test", Assert.Single(classification.Components, component => component.Evidence.Contains("App.csproj")).Frameworks);
    }

    [Theory]
    [InlineData("xunit.v3", "xunit")]
    [InlineData("NUnit", "nunit")]
    [InlineData("MSTest.TestFramework", "mstest")]
    [InlineData("TUnit", "tunit")]
    public void InheritedReferencesIdentifyNativeFrameworkWithoutEvaluatingProject(string package, string framework)
    {
        using var repo = new Fixture();
        repo.Write("tests/Directory.Build.props", $"<Project><ItemGroup><PackageReference Include=\"{package}\" /></ItemGroup></Project>");
        repo.Write("tests/unit/Example.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var component = Assert.Single(new RepositoryClassifier().Classify(repo.Path).Components);
        Assert.Contains(framework, component.Frameworks);
        Assert.Contains("dotnet-test", component.Frameworks);
        Assert.Contains("tests/Directory.Build.props", component.Evidence);
    }

    [Fact]
    public void CentralVersionsCommentsAndConditionalPackagesDoNotInventAdoption()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Packages.props", "<Project><ItemGroup><PackageVersion Include=\"xunit\" Version=\"1\" /></ItemGroup></Project>");
        repo.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><!-- xunit and NUnit are alternatives --><ItemGroup Condition=\"'$(Test)'=='true'\"><PackageReference Include=\"xunit\" /></ItemGroup></Project>");
        var result = new RepositoryClassifier().Classify(repo.Path);
        Assert.Contains("test-runner-unverified", Assert.Single(result.Components).Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, result));
        Assert.Contains(result.Warnings, value => value.Contains("Conditional", StringComparison.Ordinal));
    }

    [Fact]
    public void NearestBuildPropsAndExplicitFalseTakePrecedence()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Build.props", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        repo.Write("src/Directory.Build.props", "<Project />");
        repo.Write("src/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        repo.Write("Other.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup></Project>");
        Assert.All(new RepositoryClassifier().Classify(repo.Path).Components,
            component => Assert.DoesNotContain("dotnet-test", component.Frameworks));
    }

    [Fact]
    public void InitAndImportKeepMixedSolutionLayersAndDoNotInventCoverage()
    {
        using var repo = new Fixture();
        repo.Write("Example.slnx", "<Solution />");
        foreach (var layer in new[] { "unit", "business", "integration" })
            repo.Write($"tests/{layer}/Example.{layer}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"xunit.v3\" /></ItemGroup></Project>");
        var classification = new RepositoryClassifier().Classify(repo.Path);
        var initialized = RepositoryTestingStarter.Create(repo.Path, "docs", classification).Profile;
        var imported = RepositoryTestingStarter.CreateImportProfile(repo.Path, classification)!;
        foreach (var profile in new[] { initialized, imported })
        {
            foreach (var layer in new[] { "unit", "business", "integration" }) Assert.Contains($"| {layer} | dotnet-test |", profile, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet test Example.slnx", profile, StringComparison.Ordinal);
            Assert.DoesNotContain("coverage.cobertura.xml", profile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EmptyRepositoryDoesNotAcquireAFictitiousTestRunner()
    {
        using var repo = new Fixture();
        var classification = new RepositoryClassifier().Classify(repo.Path);
        var starter = RepositoryTestingStarter.Create(repo.Path, "docs", classification);
        Assert.DoesNotContain("cis-docs", starter.Profile, StringComparison.Ordinal);
        Assert.DoesNotContain("documentation.xml", starter.Profile, StringComparison.Ordinal);
        Assert.Contains("cis docs validate", starter.Workflow, StringComparison.Ordinal);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-engineering-tests");
        public string Path { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public void Write(string relative, string content)
        {
            var target = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
        public void Dispose()
        {
            if (!Cis.Abstractions.CisPathSafety.IsUnderRoot(Root, Path)) throw new InvalidOperationException();
            Directory.Delete(Path, true);
        }
    }
}
