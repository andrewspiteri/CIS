namespace Cis.Modules.Repository.Tests;

public sealed class DotNetStaticImportDiscoveryTests
{
    [Fact]
    public void DotNetStaticImport_LiteralParentImportsPreserveDeclarationOrderAndEvidence()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Build.props", """
            <Project>
              <PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>
              <Import Project="eng/TestDefaults.props" Condition="Exists('eng/TestDefaults.props')" />
            </Project>
            """);
        repo.Write("eng/TestDefaults.props", """
            <Project>
              <PropertyGroup><EnableMSTestRunner>true</EnableMSTestRunner></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" />
                <PackageReference Include="MSTest.TestFramework" />
                <PackageReference Include="MSTest.TestAdapter" />
                <PackageReference Include="$(UnusedRunnerPackage)" Condition="'$(IsTestProject)' == 'false'" />
              </ItemGroup>
              <Target Name="DiscoveryMustNotExecute" BeforeTargets="Build">
                <WriteLinesToFile File="discovery-executed.marker" Lines="executed" />
              </Target>
            </Project>
            """);
        repo.Write("tests/Directory.Build.props", """
            <Project>
              <Import Project="../Directory.Build.props" />
              <PropertyGroup><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup>
            </Project>
            """);
        repo.Write("tests/unit/Example.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var classification = new RepositoryClassifier().Classify(repo.Path);
        var component = Assert.Single(classification.Components);

        Assert.Contains("mstest", component.Frameworks);
        Assert.Contains("dotnet-test", component.Frameworks);
        Assert.DoesNotContain("microsoft-testing-platform", component.Frameworks);
        Assert.DoesNotContain("test-runner-unverified", component.Frameworks);
        Assert.Contains("tests/Directory.Build.props", component.Evidence);
        Assert.Contains("Directory.Build.props", component.Evidence);
        Assert.Contains("eng/TestDefaults.props", component.Evidence);
        Assert.Empty(classification.Warnings);
        Assert.NotNull(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
        Assert.Empty(Directory.GetFiles(repo.Path, "discovery-executed.marker", SearchOption.AllDirectories));
    }

    [Fact]
    public void DotNetStaticImport_UnrelatedConditionalPropertiesDoNotObscureQualifiedTestConfiguration()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
                <PackAsTool Condition="'$(CisStandalone)' == 'true'">false</PackAsTool>
                <DefineConstants Condition="'$(Configuration)' == 'Debug'">$(DefineConstants);TRACE</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        repo.Write("Tests.csproj", QualifiedTestProject());

        var classification = new RepositoryClassifier().Classify(repo.Path);
        var component = Assert.Single(classification.Components);

        Assert.Contains("mstest", component.Frameworks);
        Assert.DoesNotContain("test-runner-unverified", component.Frameworks);
        Assert.DoesNotContain("microsoft-testing-platform", component.Frameworks);
        Assert.Empty(classification.Warnings);
        Assert.NotNull(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("dynamic")]
    [InlineData("outside")]
    [InlineData("cycle")]
    public void DotNetStaticImport_UnresolvedImportsCannotInventVstestBinding(string scenario)
    {
        using var repo = new Fixture();
        var import = scenario switch
        {
            "dynamic" => "$(UnknownImport)",
            "outside" => "../outside.props",
            "cycle" => "eng/Cycle.props",
            _ => "missing.props",
        };
        repo.Write("Tests.csproj", QualifiedTestProject($"<Import Project=\"{import}\" />"));
        if (scenario == "outside")
            File.WriteAllText(System.IO.Path.Combine(repo.ContainerPath, "outside.props"),
                "<Project><PropertyGroup><EnableMSTestRunner>true</EnableMSTestRunner></PropertyGroup></Project>");
        if (scenario == "cycle")
            repo.Write("eng/Cycle.props", "<Project><Import Project=\"../Tests.csproj\" /></Project>");

        var classification = new RepositoryClassifier().Classify(repo.Path);
        var component = Assert.Single(classification.Components);

        Assert.Contains(classification.Warnings, warning => warning.Contains("import", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("test-runner-unverified", component.Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
        Assert.DoesNotContain("--logger trx", RepositoryTestingStarter.Create(repo.Path, "docs", classification).Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("../outside.props", component.Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DotNetStaticImport_UnsupportedSdkAndChooseCannotInventVstestBinding(bool sdk)
    {
        using var repo = new Fixture();
        repo.Write("Tests.csproj", QualifiedTestProject("<Import Project=\"eng/Unsupported.props\" />"));
        repo.Write("eng/Unsupported.props", sdk ? "<Project Sdk=\"Unresolved.Test.Sdk\"><PropertyGroup><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup></Project>" : """
            <Project>
              <PropertyGroup><Gate>true</Gate></PropertyGroup>
              <Choose>
                <When Condition="'$(Gate)' == 'true'">
                  <PropertyGroup><EnableMSTestRunner>true</EnableMSTestRunner></PropertyGroup>
                </When>
                <Otherwise>
                  <PropertyGroup><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup>
                </Otherwise>
              </Choose>
            </Project>
            """);

        var classification = new RepositoryClassifier().Classify(repo.Path);

        Assert.Contains(classification.Warnings, warning => warning.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("test-runner-unverified", Assert.Single(classification.Components).Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Theory]
    [InlineData("Include")]
    [InlineData("Update")]
    [InlineData("Remove")]
    public void DotNetStaticImport_UnresolvedPackageIdentitiesCannotInventVstestBinding(string operation)
    {
        using var repo = new Fixture();
        repo.Write("Tests.csproj", QualifiedTestProject("<Import Project=\"eng/Packages.props\" />"));
        repo.Write("eng/Packages.props", $"<Project><ItemGroup><PackageReference {operation}=\"$(RunnerPackage)\" /></ItemGroup></Project>");

        var classification = new RepositoryClassifier().Classify(repo.Path);

        Assert.Contains(classification.Warnings, warning => warning.Contains("package identity", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("eng/Packages.props", StringComparison.Ordinal));
        Assert.Contains("test-runner-unverified", Assert.Single(classification.Components).Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Fact]
    public void DotNetStaticImport_RepeatedImportsCannotUndoLaterRunnerSelection()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Build.props", "<Project><Import Project=\"eng/Runner.props\" /></Project>");
        repo.Write("eng/Runner.props", "<Project><PropertyGroup><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup></Project>");
        repo.Write("Tests.csproj", QualifiedTestProject("<PropertyGroup><EnableMSTestRunner>true</EnableMSTestRunner></PropertyGroup>"));
        repo.Write("Directory.Build.targets", "<Project><Import Project=\"eng/Runner.props\" /></Project>");

        var classification = new RepositoryClassifier().Classify(repo.Path);

        Assert.Contains("microsoft-testing-platform", Assert.Single(classification.Components).Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Fact]
    public void DotNetStaticImport_ProjectChooseCannotInventVstestBinding()
    {
        using var repo = new Fixture();
        repo.Write("Tests.csproj", QualifiedTestProject("""
            <PropertyGroup><Gate>true</Gate></PropertyGroup>
            <Choose>
              <When Condition="'$(Gate)' == 'true'">
                <PropertyGroup><EnableMSTestRunner>true</EnableMSTestRunner></PropertyGroup>
              </When>
              <Otherwise>
                <PropertyGroup><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup>
              </Otherwise>
            </Choose>
            """));

        var classification = new RepositoryClassifier().Classify(repo.Path);

        Assert.Contains(classification.Warnings, warning => warning.Contains("Choose", StringComparison.Ordinal));
        Assert.Contains("test-runner-unverified", Assert.Single(classification.Components).Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Fact]
    public void DotNetStaticImport_UnrelatedChoosePreservesExplicitNonTestProject()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Build.props", """
            <Project>
              <PropertyGroup><Gate>true</Gate></PropertyGroup>
              <Choose>
                <When Condition="'$(Gate)' == 'true'"><PropertyGroup><CIOutputPath>ci-output</CIOutputPath></PropertyGroup></When>
                <Otherwise><PropertyGroup><CIOutputPath>local-output</CIOutputPath></PropertyGroup></Otherwise>
              </Choose>
            </Project>
            """);
        repo.Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <Choose>
                <When Condition="'$(Gate)' == 'true'"><PropertyGroup><DefineConstants>TRACE</DefineConstants></PropertyGroup></When>
                <Otherwise><PropertyGroup><DefineConstants>DEBUG</DefineConstants></PropertyGroup></Otherwise>
              </Choose>
              <PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup>
            </Project>
            """);

        var classification = new RepositoryClassifier().Classify(repo.Path);
        var component = Assert.Single(classification.Components);

        Assert.Empty(classification.Warnings);
        Assert.DoesNotContain("dotnet-test", component.Frameworks);
        Assert.DoesNotContain("test-runner-unverified", component.Frameworks);
        Assert.DoesNotContain("microsoft-testing-platform", component.Frameworks);
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    [Fact]
    public void DotNetStaticImport_CaseVariantRepeatCannotInventVstestBinding()
    {
        using var repo = new Fixture();
        repo.Write("Directory.Build.props", "<Project><Import Project=\"eng/Runner.props\" /></Project>");
        const string settings = "<Project><PropertyGroup><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup></Project>";
        repo.Write("eng/Runner.props", settings);
        repo.Write("eng/runner.props", settings);
        repo.Write("Tests.csproj", QualifiedTestProject("<PropertyGroup><EnableMSTestRunner>true</EnableMSTestRunner></PropertyGroup>"));
        repo.Write("Directory.Build.targets", "<Project><Import Project=\"eng/runner.props\" /></Project>");

        var classification = new RepositoryClassifier().Classify(repo.Path);
        var component = Assert.Single(classification.Components);

        Assert.True(component.Frameworks.Contains("microsoft-testing-platform") || component.Frameworks.Contains("test-runner-unverified"));
        Assert.Null(RepositoryTestingStarter.CreateImportProfile(repo.Path, classification));
    }

    private static string QualifiedTestProject(string import = "") => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><IsTestProject>true</IsTestProject><EnableMSTestRunner>false</EnableMSTestRunner></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" />
            <PackageReference Include="MSTest.TestFramework" />
            <PackageReference Include="MSTest.TestAdapter" />
          </ItemGroup>
        """ + import + "</Project>";

    private sealed class Fixture : IDisposable
    {
        private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-static-import-tests");
        public string ContainerPath { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(ContainerPath, "repository");

        public Fixture() => Directory.CreateDirectory(Path);

        public void Write(string relative, string content)
        {
            var target = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }

        public void Dispose()
        {
            if (!Cis.Abstractions.CisPathSafety.IsUnderRoot(Root, ContainerPath)) throw new InvalidOperationException();
            Directory.Delete(ContainerPath, true);
        }
    }
}
