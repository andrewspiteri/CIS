using Cis.Abstractions;
using Cis.Host;

namespace Cis.Modules.Repository.Tests;

public sealed class AutomaticExampleTests
{
    [Fact]
    public void PreviewAndApplyInstallReferencesWithoutAdoptingTheirImplementation()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        var preview = initializer.Initialize(new(fixture.Repository, "docs", true, false, DeclaredStack: "csharp"));
        Assert.Equal(0, preview.ExitCode);
        Assert.Contains(RepositoryExampleInstallation.RecipeRoot + "/README.md", preview.FilesToCreate);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Repository));
        var result = initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp"));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("first", fixture.ReadExample("README.md"));
        Assert.Empty(new RepositoryClassifier().Classify(fixture.Repository).Components);
        var context = new CisRepositoryContextResolver().Resolve(fixture.Repository).Context!;
        var identity = CisExecutionIdentity.Capture(context);
        fixture.WriteExample("src/Example.cs", "class Example { /* local edit */ }");
        Assert.Equal(identity, CisExecutionIdentity.Capture(context));
        Assert.DoesNotContain(CisExecutionIdentity.CaptureInputs(context), file => file.Path.Contains("local/examples", StringComparison.Ordinal));
        Assert.Equal("unchanged", initializer.Initialize(new(fixture.Repository, "docs", false, true)).Status);
        Assert.Contains(".cis/local/examples/dotnet-engineering/README.md", File.ReadAllText(Path.Combine(fixture.Repository, ".github/skills/cis-dotnet-test/SKILL.md")));
        Assert.Contains("*", File.ReadAllText(Path.Combine(fixture.Repository, RepositoryExampleInstallation.Root, ".gitignore")));
    }

    [Fact]
    public void UpgradeUpdatesOnlyUnchangedFilesAndPreservesCustomizationsAndRemovedFiles()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        fixture.WriteExample("README.md", "my notes");
        fixture.WriteBundle("README.md", "second");
        fixture.WriteBundle("src/Example.cs", "class Example { public int Version => 2; }");
        fixture.WriteBundle("added.txt", "new reference");
        File.Delete(Path.Combine(fixture.Distribution, "examples/dotnet-engineering/retired.txt"));
        var preview = initializer.Initialize(new(fixture.Repository, "docs", true, false));
        Assert.Contains(RepositoryExampleInstallation.RecipeRoot + "/src/Example.cs", preview.FilesToUpdate);
        Assert.DoesNotContain(RepositoryExampleInstallation.RecipeRoot + "/README.md", preview.FilesToUpdate);
        Assert.Contains(preview.Warnings, warning => warning.Contains("edit retained", StringComparison.Ordinal));
        Assert.Equal("class Example {}", fixture.ReadExample("src/Example.cs"));
        var confirmation = initializer.Initialize(new(fixture.Repository, "docs", false, false));
        Assert.Equal(3, confirmation.ExitCode);
        Assert.Equal("class Example {}", fixture.ReadExample("src/Example.cs"));
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.Equal("my notes", fixture.ReadExample("README.md"));
        Assert.Contains("Version => 2", fixture.ReadExample("src/Example.cs"));
        Assert.Equal("new reference", fixture.ReadExample("added.txt"));
        Assert.Equal("keep me", fixture.ReadExample("retired.txt"));
        var repeat = initializer.Initialize(new(fixture.Repository, "docs", false, true));
        Assert.Equal("unchanged", repeat.Status);
        Assert.Contains(repeat.Warnings, warning => warning.Contains("edit retained", StringComparison.Ordinal));
    }

    [Fact]
    public void OptOutPersistsAndReenableInstallsWithoutDeletingExistingCopies()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp", Examples: "off")).ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, RepositoryExampleInstallation.Root)));
        Assert.Equal("unchanged", initializer.Initialize(new(fixture.Repository, "docs", false, true)).Status);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, Examples: "auto")).ExitCode);
        Assert.Equal("first", fixture.ReadExample("README.md"));
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, Examples: "off")).ExitCode);
        fixture.WriteBundle("README.md", "new version");
        Assert.Equal("unchanged", initializer.Initialize(new(fixture.Repository, "docs", false, true)).Status);
        Assert.Equal("first", fixture.ReadExample("README.md"));
    }

    [Fact]
    public void DetectedCSharpInstallsButUnknownStackDoesNot()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, RepositoryExampleInstallation.Root)));
        File.WriteAllText(Path.Combine(fixture.Repository, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.Equal("first", fixture.ReadExample("README.md"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(".git/config")]
    public void InvalidManifestBlocksAllInitializationWrites(string path)
    {
        using var fixture = new Fixture();
        fixture.WriteExample("README.md", "user content");
        var manifest = $$"""{"schemaVersion":1,"bundleDigest":"sha256:{{new string('a', 64)}}","files":[{"path":"{{path}}","appliedHash":null}]}""";
        File.WriteAllText(Path.Combine(fixture.Repository, RepositoryExampleInstallation.ManifestPath), manifest);
        var result = new RepositoryInitializer(fixture.Distribution).Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp"));
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, "docs")));
        Assert.Equal("user content", fixture.ReadExample("README.md"));
    }

    [Fact]
    public void UnownedCopyIsPreservedAndNotAdoptedAsManaged()
    {
        using var fixture = new Fixture();
        fixture.WriteExample("README.md", "personal example");
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        fixture.WriteBundle("README.md", "updated bundle");
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.Equal("personal example", fixture.ReadExample("README.md"));
    }

    [Fact]
    public void MissingBundleFailsVisiblyAndExplicitOptOutAllowsInitialization()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(Path.Combine(fixture.Root, "missing"));
        Assert.Equal(2, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Repository));
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp", Examples: "off")).ExitCode);
    }

    [Fact]
    public void CommandPersistsOptOutAndRejectsInvalidMode()
    {
        using var fixture = new Fixture();
        using var host = new CisHostBuilder().AddModule(new RepositoryModule()).Build();
        Assert.Equal(2, host.Invoke(["repo", "init", "--repo", fixture.Repository, "--root", "docs", "--examples", "invalid"]));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Repository));
        Assert.Equal(0, host.Invoke(["repo", "init", "--repo", fixture.Repository, "--root", "docs", "--stack", "csharp", "--examples", "off", "--yes"]));
        Assert.Contains("off", File.ReadAllText(Path.Combine(fixture.Repository, RepositoryExampleInstallation.SettingsPath)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedSettingsOrOversizedBundleFailWithoutPartialInitialization(bool malformedSettings)
    {
        using var fixture = new Fixture();
        if (malformedSettings)
        {
            Directory.CreateDirectory(Path.Combine(fixture.Repository, ".cis"));
            File.WriteAllText(Path.Combine(fixture.Repository, RepositoryExampleInstallation.SettingsPath), "{broken");
        }
        else fixture.WriteBundle("large.txt", new string('x', 1024 * 1024 + 1));
        var result = new RepositoryInitializer(fixture.Distribution).Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp"));
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, "docs")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, RepositoryExampleInstallation.Root)));
    }

    [Fact]
    public void DirectoryAtExampleFileBlocksWithoutOverwriting()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Repository, RepositoryExampleInstallation.RecipeRoot, "README.md"));
        var result = new RepositoryInitializer(fixture.Distribution).Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp"));
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, "docs")));
        Assert.True(Directory.Exists(Path.Combine(fixture.Repository, RepositoryExampleInstallation.RecipeRoot, "README.md")));
    }

    [Fact]
    public void IdenticalUnownedFileRemainsUnownedOnLaterUpgrade()
    {
        using var fixture = new Fixture();
        fixture.WriteExample("README.md", "first");
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        fixture.WriteBundle("README.md", "second");
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.Equal("first", fixture.ReadExample("README.md"));
    }

    [Fact]
    public void RetiredFileKeepsItsOwnershipAndWarningAcrossReconciliation()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        File.Delete(Path.Combine(fixture.Distribution, "examples/dotnet-engineering/retired.txt"));
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        var repeat = initializer.Initialize(new(fixture.Repository, "docs", false, true));
        Assert.Equal("unchanged", repeat.Status);
        Assert.Contains(repeat.Warnings, warning => warning.Contains("Retired example file retained", StringComparison.Ordinal));
        fixture.WriteBundle("retired.txt", "reintroduced");
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.Equal("reintroduced", fixture.ReadExample("retired.txt"));
    }

    [Fact]
    public void ReservedDocumentationRootFailsBeforeAnyWrites()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        var result = initializer.Initialize(new(fixture.Repository, RepositoryExampleInstallation.RecipeRoot, false, true, DeclaredStack: "csharp"));
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Repository));
    }

    [Fact]
    public void UpdatingHardLinkedExampleDoesNotModifyExternalAlias()
    {
        using var fixture = new Fixture();
        var initializer = new RepositoryInitializer(fixture.Distribution);
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp")).ExitCode);
        var alias = Path.Combine(fixture.Root, "outside-repository.txt");
        var target = Path.Combine(fixture.Repository, RepositoryExampleInstallation.RecipeRoot, "README.md");
        var command = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "fsutil.exe" : "/bin/ln")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            command.ArgumentList.Add("hardlink"); command.ArgumentList.Add("create");
            command.ArgumentList.Add(alias); command.ArgumentList.Add(target);
        }
        else { command.ArgumentList.Add(target); command.ArgumentList.Add(alias); }
        using var process = System.Diagnostics.Process.Start(command)!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
        fixture.WriteBundle("README.md", "second");
        Assert.Equal(0, initializer.Initialize(new(fixture.Repository, "docs", false, true)).ExitCode);
        Assert.Equal("second", fixture.ReadExample("README.md"));
        Assert.Equal("first", File.ReadAllText(alias));
    }

    [Fact]
    public void MinimalImportDoesNotInstallExamples()
    {
        using var fixture = new Fixture();
        var result = new RepositoryInitializer(fixture.Distribution).Initialize(new(fixture.Repository, "docs", false, true,
            MinimalImport: true, DeclaredStack: "csharp"));
        Assert.Equal(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, RepositoryExampleInstallation.Root)));
    }

    public static bool IsUnix => !OperatingSystem.IsWindows();

    [Theory(SkipUnless = nameof(IsUnix), Skip = "Unix FIFO boundary requires a Unix host; Windows qualification does not cover it.")]
    [InlineData(true)]
    [InlineData(false)]
    public void UnixFifoSourceAndDestinationAreRejectedBeforeOpening(bool source)
    {
        using var fixture = new Fixture();
        var target = source ? Path.Combine(fixture.Distribution, "examples/dotnet-engineering/fifo.txt")
            : Path.Combine(fixture.Repository, RepositoryExampleInstallation.RecipeRoot, "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var executable = File.Exists("/usr/bin/mkfifo") ? "/usr/bin/mkfifo" : "/bin/mkfifo";
        var command = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
        command.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(command)!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
        var result = new RepositoryInitializer(fixture.Distribution).Initialize(new(fixture.Repository, "docs", false, true, DeclaredStack: "csharp"));
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Repository, "docs")));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-auto-examples", Guid.NewGuid().ToString("N"));
        internal string Repository => Path.Combine(Root, "repo");
        internal string Distribution => Path.Combine(Root, "distribution");
        internal Fixture()
        {
            Directory.CreateDirectory(Repository);
            WriteBundle("README.md", "first");
            WriteBundle("src/Example.cs", "class Example {}");
            WriteBundle("src/Example.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            WriteBundle("retired.txt", "keep me");
        }
        internal void WriteBundle(string path, string content) => Write(Path.Combine(Distribution, "examples/dotnet-engineering", path), content);
        internal void WriteExample(string path, string content) => Write(Path.Combine(Repository, RepositoryExampleInstallation.RecipeRoot, path), content);
        internal string ReadExample(string path) => File.ReadAllText(Path.Combine(Repository, RepositoryExampleInstallation.RecipeRoot, path));
        private static void Write(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Path.Combine(Path.GetTempPath(), "cis-auto-examples"), Root)) throw new InvalidOperationException();
            Directory.Delete(Root, true);
        }
    }
}
