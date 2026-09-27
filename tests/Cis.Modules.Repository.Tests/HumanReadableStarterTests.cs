using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository.Tests;

public sealed class HumanReadableStarterTests
{
    [Theory]
    [InlineData("docs")]
    [InlineData("engineering/product/knowledge")]
    public void FreshAndRepeatInitializationBindAllDependenciesAndTruthfulIdentity(string docs)
    {
        using var repo = new Fixture();
        var request = new RepositoryInitRequest(repo.Path, docs, false, true);
        var initializer = new RepositoryInitializer();
        var result = initializer.Initialize(request);
        Assert.Equal(0, result.ExitCode);
        var standard = repo.Read(docs + "/standards/human-readable-content-standard.md");
        Assert.Contains("status: Draft", standard, StringComparison.Ordinal);
        Assert.DoesNotContain("change-impact-studio:standard", standard, StringComparison.Ordinal);
        Assert.Contains("human-readable-content", repo.Read(docs + "/catalog.yml"), StringComparison.Ordinal);
        Assert.Contains("human-readable-content", repo.Read(docs + "/README.md"), StringComparison.Ordinal);
        Assert.Contains("HC-MEANING-01", repo.Read(docs + "/references/standards-conformance-matrix.md"), StringComparison.Ordinal);
        Assert.Contains(docs + "/standards/", repo.Read("AGENTS.md"), StringComparison.Ordinal);
        Assert.Contains("AGENTS.md", repo.Read("CLAUDE.md"), StringComparison.Ordinal);
        Assert.Contains(docs + "/standards/", repo.Read(".github/instructions/cis-human-readable-content.instructions.md"), StringComparison.Ordinal);
        foreach (var name in new[] { "technical-writing", "ux-writing", "content-review" })
        {
            var path = $".github/skills/cis-{name}/SKILL.md";
            var text = repo.Read(path);
            Assert.DoesNotContain("vscode-extension/", text, StringComparison.Ordinal);
            foreach (Match link in Regex.Matches(text, @"\]\(([^)]+)\)"))
                Assert.True(File.Exists(System.IO.Path.GetFullPath(System.IO.Path.Combine(repo.Path, System.IO.Path.GetDirectoryName(path)!, link.Groups[1].Value))), link.Value);
        }
        foreach (var name in new[] { "documentation", "add-documentation", "govern-business-requirements", "govern-technical-intent", "govern-solution-design", "govern-ui-direction", "feature-specification-governance", "impact-review", "design-review" })
            Assert.Contains(docs + "/standards/human-readable", repo.Read($".github/skills/cis-{name}/SKILL.md"), StringComparison.Ordinal);
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, ".github/instructions/cis-ux-content.instructions.md")));
        Assert.Equal("unchanged", initializer.Initialize(request).Status);
        Assert.Contains(new StarterManifestStore().Read(System.IO.Path.Combine(repo.Path, ".cis/starter-manifest.yml")).Manifest!.ManagedArtifacts,
            item => item.Definition == "standard.human-readable-content" && item.TemplateVersion == 2);
    }

    [Theory]
    [InlineData("client", "{\"name\":\"client\",\"dependencies\":{\"react\":\"1\"}}")]
    [InlineData("editor", "{\"name\":\"editor\",\"engines\":{\"vscode\":\"^1.95.0\"}}")]
    public void UiInstructionsUseDetectedTargetRoots(string folder, string package)
    {
        using var repo = new Fixture();
        repo.Write(folder + "/package.json", package);
        repo.Write(folder + "/index.js", "// UI entry");
        Assert.Equal(0, new RepositoryInitializer().Initialize(new(repo.Path, "knowledge", false, true)).ExitCode);
        var instruction = repo.Read(".github/instructions/cis-ux-content.instructions.md");
        Assert.Contains($"applyTo: \"{folder}/**\"", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("vscode-extension/", instruction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UpgradeUsesManagedVersionAndPreservesHumanEdits(bool edited, bool humanOwned)
    {
        using var repo = new Fixture();
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(repo.Path, "docs", false, true);
        Assert.Equal(0, initializer.Initialize(request).ExitCode);
        const string path = ".github/skills/cis-technical-writing/SKILL.md";
        const string old = "---\nname: cis-technical-writing\ndescription: Previous fixture version\n---\n# Previous version\n";
        // Simulate a real earlier template with its own matching hash, solely in this disposable fixture.
        var manifestPath = System.IO.Path.Combine(repo.Path, ".cis/starter-manifest.yml");
        var store = new StarterManifestStore();
        var manifest = store.Read(manifestPath).Manifest!;
        File.WriteAllText(manifestPath, store.Write(manifest with { ManagedArtifacts = manifest.ManagedArtifacts.Select(item => item.Path == path
            ? item with { AppliedHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(old))), TemplateVersion = 1, Ownership = humanOwned ? "human" : "managed" } : item).ToArray() }));
        repo.Write(path, old + (edited ? "Human direction must be kept.\n" : ""));
        var result = initializer.Initialize(request);
        if (edited && !humanOwned)
        {
            Assert.Equal(4, result.ExitCode);
            Assert.Contains(result.Collisions, value => value.Contains(path, StringComparison.Ordinal));
        }
        else Assert.Equal(0, result.ExitCode);
        if (edited) Assert.Equal(old + "Human direction must be kept.\n", repo.Read(path));
        else Assert.Contains("shared standard", repo.Read(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingHostGuidanceIsAReviewableCollisionNeverOverwritten()
    {
        using var repo = new Fixture();
        repo.Write("AGENTS.md", "Human-managed guidance.");
        var result = new RepositoryInitializer().Initialize(new(repo.Path, "docs", false, true));
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Collisions, item => item.Contains("AGENTS.md", StringComparison.Ordinal));
        Assert.Equal("Human-managed guidance.", repo.Read("AGENTS.md"));
        Assert.False(File.Exists(System.IO.Path.Combine(repo.Path, "docs/standards/human-readable-content-standard.md")));
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-content-tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public string Read(string path) => File.ReadAllText(System.IO.Path.Combine(Path, path));
        public void Write(string path, string text)
        {
            var target = System.IO.Path.Combine(Path, path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }
        public void Dispose()
        {
            if (!Cis.Abstractions.CisPathSafety.IsUnderRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-content-tests"), Path)) throw new InvalidOperationException();
            Directory.Delete(Path, true);
        }
    }
}
