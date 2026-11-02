using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class SuppliedInputGuidanceTests
{
    [Fact]
    public void FreshAuthorityRoutesBothAgentEntryPointsToTheHeadlessInputAndAuthorisedAnswerProcedures()
    {
        using var repository = new Fixture();
        var initializer = new RepositoryInitializer();
        var registry = new WorkspaceRegistry(new CisRepositoryContextResolver());
        var workspace = new WorkspaceInitializer(initializer, registry);
        Assert.Equal(0, workspace.Initialize(new(repository.Path, "knowledge/product", false, true,
            "sample", "sample", "Sample", "Sample")).ExitCode);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis/workspace.yml")));

        var agents = repository.Read("AGENTS.md");
        Assert.Contains("AGENTS.md", repository.Read("CLAUDE.md"), StringComparison.Ordinal);
        var repositoryInstructions = FollowBacktickRoute(repository, agents, "cis-repository.instructions.md");
        var business = FollowBacktickRoute(repository, repositoryInstructions, "cis-govern-business-requirements/SKILL.md");
        var technical = FollowBacktickRoute(repository, repositoryInstructions, "cis-govern-technical-intent/SKILL.md");
        Assert.Contains("cis-govern-business-requirements/SKILL.md", agents, StringComparison.Ordinal);
        Assert.Contains("graphical wizard is optional", business, StringComparison.Ordinal);
        Assert.Contains("--role business --source", business, StringComparison.Ordinal);
        Assert.Contains("--role technical --source", business, StringComparison.Ordinal);
        Assert.True(business.IndexOf("cis definition documents", StringComparison.Ordinal)
            < business.IndexOf("cis definition prepare", StringComparison.Ordinal));
        Assert.Contains("--page technical --format json", business, StringComparison.Ordinal);
        Assert.Contains("technicalQuestions.questions", business, StringComparison.Ordinal);
        Assert.Contains("readyToActivate", business, StringComparison.Ordinal);
        Assert.Contains("pending baseline adoption", business, StringComparison.Ordinal);
        Assert.Contains("definition answer", technical, StringComparison.Ordinal);
        Assert.Contains("technical-intent decisions resolve", technical, StringComparison.Ordinal);
        Assert.Contains("\"reviewToken\"", technical, StringComparison.Ordinal);
        Assert.Contains("--input", technical, StringComparison.Ordinal);
        Assert.Contains("--actor", technical, StringComparison.Ordinal);
        Assert.DoesNotContain("--review-token", technical, StringComparison.Ordinal);
        Assert.DoesNotContain("<layout.json>", business, StringComparison.Ordinal);
        Assert.DoesNotContain("Greenfield projects require human answers for every direction", technical, StringComparison.Ordinal);
        Assert.Contains("cis-govern-business-requirements/SKILL.md", repository.Read(".github/instructions/cis-business-requirements.instructions.md"), StringComparison.Ordinal);
        Assert.Contains("cis-govern-technical-intent/SKILL.md", repository.Read(".github/instructions/cis-technical-intent.instructions.md"), StringComparison.Ordinal);
        Assert.Equal("unchanged", initializer.Initialize(new(repository.Path, "knowledge/product", false, true, WorkspaceAuthority: true)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidanceUpgradeUsesExistingManagedHashesAndPreservesCustomisedOwningSkills(bool customised)
    {
        using var repository = new Fixture();
        var initializer = new RepositoryInitializer();
        var request = new RepositoryInitRequest(repository.Path, "docs", false, true);
        Assert.Equal(0, initializer.Initialize(request).ExitCode);
        const string path = ".github/skills/cis-govern-business-requirements/SKILL.md";
        const string previous = "---\nname: cis-govern-business-requirements\ndescription: Earlier generated fixture guidance.\n---\n# Earlier requirements guidance\n";
        var manifestPath = Path.Combine(repository.Path, ".cis/starter-manifest.yml");
        var store = new StarterManifestStore();
        var manifest = store.Read(manifestPath).Manifest!;
        File.WriteAllText(manifestPath, store.Write(manifest with
        {
            ManagedArtifacts = manifest.ManagedArtifacts.Select(item => item.Path == path
                ? item with { AppliedHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(previous))) }
                : item).ToArray(),
        }));
        var content = previous + (customised ? "Team-specific authority rule must remain.\n" : "");
        repository.Write(path, content);

        var result = initializer.Initialize(request);
        if (customised)
        {
            Assert.Equal(4, result.ExitCode);
            Assert.Contains(result.Collisions, collision => collision.Contains(path, StringComparison.Ordinal));
            Assert.Equal(content, repository.Read(path));
        }
        else
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("cis definition documents", repository.Read(path), StringComparison.Ordinal);
            Assert.Equal("unchanged", initializer.Initialize(request).Status);
        }
    }

    private static string FollowBacktickRoute(Fixture repository, string source, string suffix)
    {
        var route = Assert.Single(Regex.Matches(source, @"`(?<path>\.github/[^`\r\n]+)`")
            .Select(match => match.Groups["path"].Value).Where(path => path.EndsWith(suffix, StringComparison.Ordinal)).Distinct());
        return repository.Read(route);
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cis-input-guidance-").FullName;
        public string Read(string relative) => File.ReadAllText(System.IO.Path.Combine(Path, relative));
        public void Write(string relative, string content)
        {
            var target = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(System.IO.Path.GetTempPath(), Path)) throw new InvalidOperationException();
            Directory.Delete(Path, true);
        }
    }
}
