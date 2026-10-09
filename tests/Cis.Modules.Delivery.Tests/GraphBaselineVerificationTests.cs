using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Repository;
using Cis.Modules.Verify;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class ExecutionModulesTests
{
    [Fact]
    public void GraphBaselinePreservesCreationInputsAndDetectsAddedModifiedAndDeletedFiles()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("src/modified.txt", "before");
        repository.Write("src/deleted.txt", "before");
        WriteGraphAnchor(repository);
        var resolver = new CisRepositoryContextResolver();
        var changes = new ChangeDossierStore(resolver, Clock);
        var created = changes.Create(new(repository.Path, "Graph baseline", "Retain creation inputs", []));
        Assert.Equal(0, created.ExitCode);
        // Reopen the store: the baseline must survive a separate command/process.
        var service = new VerifyService(resolver, new ChangeDossierStore(resolver, Clock), [], Clock);
        var unchanged = service.Diff(repository.Path, created.Change!.Id);
        Assert.True(unchanged.ExitCode == 0, string.Join("; ", unchanged.Findings.Select(x => x.Message)));
        Assert.DoesNotContain(unchanged.Snapshot!.Files, x => x.Path.StartsWith("src/", StringComparison.Ordinal));

        repository.Write("src/modified.txt", "after");
        repository.Write("src/added.txt", "new");
        File.Delete(Path.Combine(repository.Path, "src/deleted.txt"));
        repository.Write(".cis/local/unrelated.json", "disposable");
        var changed = service.Diff(repository.Path, created.Change.Id);
        Assert.Equal(0, changed.ExitCode);
        Assert.Contains(changed.Snapshot!.Files, x => x.Path == "src/modified.txt" && x.Status == "M");
        Assert.Contains(changed.Snapshot.Files, x => x.Path == "src/added.txt" && x.Status == "A");
        Assert.Contains(changed.Snapshot.Files, x => x.Path == "src/deleted.txt" && x.Status == "D" && x.ContentDigest == "missing");
        Assert.DoesNotContain(changed.Snapshot.Files, x => x.Path.StartsWith(".cis/local/", StringComparison.Ordinal));
        Assert.All(changed.Snapshot.Repositories!, x => Assert.True(x.Exact));

        repository.Write("src/modified.txt", "after again");
        var stale = service.Validate(repository.Path, created.Change.Id);
        Assert.Contains(stale.Findings, x => x.Code == "CIS-VERIFY-SNAPSHOT-STALE");
    }

    [Fact]
    public void LegacyGraphBaselineCannotBeReconstructedFromCurrentFiles()
    {
        using var repository = ExecutionRepository.Create();
        repository.Write("docs/cis/changes/CIS-0001/proposal.md",
            "---\nchange_id: CIS-0001\ntitle: Legacy\noutcome: Verify history\nstatus: Active\nbaseline_kind: graph\nbaseline: sha256:original\ngraph_build_id: sha256:original\nimpact_roots: []\n---\n");
        var resolver = new CisRepositoryContextResolver();
        var service = new VerifyService(resolver, new ChangeDossierStore(resolver), []);
        var result = service.Diff(repository.Path, "CIS-0001");
        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Contains(result.Findings, x => x.Code == "CIS-VERIFY-GRAPH-BASELINE-MISSING");
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis/local/verify/CIS-0001/snapshot.json")));
    }

    private static void WriteGraphAnchor(ExecutionRepository repository) => repository.Write(
        ".cis/local/graph/manifest.json", JsonSerializer.Serialize(new CisGraphManifest(
            1, "sha256:fixture", "execution-fixture", "docs/cis", null, false, [], [])));

    [Theory]
    [InlineData("../outside.txt", false)]
    [InlineData("src/file.txt", true)]
    [InlineData("src/file.txt", false)]
    public void GraphBaselineRejectsUnsafeDuplicateOrMalformedInventory(string path, bool duplicate)
    {
        using var repository = ExecutionRepository.Create();
        var input = new CisRepositoryInput(path, path.StartsWith("..", StringComparison.Ordinal) || duplicate ? new string('a', 64) : "invalid");
        var baselines = JsonSerializer.Serialize(JsonSerializer.Serialize(new[]
        {
            new ChangeRepositoryBaseline("execution-fixture", "graph", "sha256:fixture", CreationInputs: duplicate ? [input, input] : [input])
        }));
        repository.Write("docs/cis/changes/CIS-0001/proposal.md", $"---\nchange_id: CIS-0001\ntitle: Inventory safety\noutcome: Fail closed\nstatus: Active\nbaseline_kind: graph\nbaseline: sha256:fixture\ngraph_build_id: sha256:fixture\nimpact_roots: []\nrepository_baselines: {baselines}\n---\n");
        var resolver = new CisRepositoryContextResolver();
        var result = new VerifyService(resolver, new ChangeDossierStore(resolver), []).Diff(repository.Path, "CIS-0001");
        Assert.False(result.Applied);
        Assert.Contains(result.Findings, x => x.Code == "CIS-VERIFY-GRAPH-BASELINE");
    }

    [Fact]
    public void NonGitAuthorityDoesNotInheritParentCheckoutBaseline()
    {
        using var parent = ExecutionRepository.Create(git: true);
        parent.Write("authority/.cis/repository.yml", "schema_version: 1\nrepository:\n  id: authority\ndocumentation_root: docs/cis\n");
        parent.Write("authority/docs/cis/catalog.yml", "schema_version: 1\nrepository: authority\ndocuments: []\n");
        parent.Write("authority/.cis/local/graph/manifest.json", JsonSerializer.Serialize(new CisGraphManifest(
            1, "sha256:child-graph", "authority", "docs/cis", parent.Head(), true, [], [])));
        var path = Path.Combine(parent.Path, "authority");
        var resolver = new CisRepositoryContextResolver();
        var changes = new ChangeDossierStore(resolver);
        var result = changes.Create(new(path, "Child authority", "Do not inherit parent Git", []));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("graph", result.Change!.BaselineKind);
        Assert.Equal("sha256:child-graph", result.Change.Baseline);
        Assert.NotNull(Assert.Single(result.Change.RepositoryBaselines!).CreationInputs);
        Assert.Equal(0, new VerifyService(resolver, changes, []).Diff(path, result.Change.Id).ExitCode);
    }

    [Theory]
    [InlineData(false, "modify")]
    [InlineData(true, "modify")]
    [InlineData(true, "locked")]
    [InlineData(true, "locked-unicode")]
    [InlineData(true, "rename-out")]
    [InlineData(true, "rename-in")]
    [InlineData(true, "preexisting-out")]
    [InlineData(true, "preexisting-in")]
    public void CreationInventoryExcludesRegisteredNestedDependencies(bool git, string scenario)
    {
        using var authority = ExecutionRepository.Create(git: git);
        WriteGraphAnchor(authority);
        authority.Write(".cis/workspace.yml", """
            schema_version: 2
            ecosystem:
              id: fixture
              name: Fixture
            product:
              id: fixture
              name: Fixture
            repositories:
            - id: execution-fixture
              path: .
              documentation_root: docs/cis
              role: authority
              participation: owned
              relationship: none
              components: []
            - id: dependency
              path: external/dependency
              documentation_root: docs/cis
              role: participant
              participation: dependency
              relationship: producer
              components: []
            """);
        authority.Write("external/dependency/.cis/repository.yml", "schema_version: 1\nrepository:\n  id: dependency\ndocumentation_root: docs/cis\n");
        authority.Write("external/dependency/docs/cis/catalog.yml", "schema_version: 1\nrepository: dependency\ndocuments: []\n");
        authority.Write("external/dependency/src/file.txt", "before");
        if (scenario is "rename-in" or "preexisting-in")
        {
            authority.Git("add", "external/dependency/src/file.txt");
            authority.Git("commit", "-m", "tracked dependency fixture");
        }
        if (scenario.StartsWith("preexisting-", StringComparison.Ordinal)) MoveAcrossBoundary();
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        Assert.True(registry.Resolve(authority.Path).IsSuccess);
        var changes = new ChangeDossierStore(resolver, workspaceRegistry: registry);
        var created = changes.Create(new(authority.Path, "Owned inputs", "Exclude external dependency", []));
        Assert.Equal(0, created.ExitCode);
        var baseline = Assert.Single(created.Change!.RepositoryBaselines!);
        if (git) Assert.DoesNotContain(baseline.WorkingTree!, x => x.Path.StartsWith("external/dependency/", StringComparison.Ordinal));
        else Assert.DoesNotContain(baseline.CreationInputs!, x => x.Path.StartsWith("external/dependency/", StringComparison.Ordinal));
        if (scenario == "preexisting-out") Assert.Contains(baseline.WorkingTree!, x => x.Path == "src/example.txt" && x.Status == "D");
        if (scenario.StartsWith("rename-", StringComparison.Ordinal)) MoveAcrossBoundary();
        else if (scenario is "modify" or "locked") authority.Write("external/dependency/src/file.txt", "after");
        authority.Write("src/owned.txt", "owned change");
        var lockedPath = scenario == "locked-unicode" ? "external/dependency/src/café.txt" : "external/dependency/src/file.txt";
        if (scenario == "locked-unicode") authority.Write(lockedPath, "excluded Unicode input");
        using var locked = scenario.StartsWith("locked", StringComparison.Ordinal) ? File.Open(Path.Combine(authority.Path, lockedPath),
            FileMode.Open, FileAccess.Read, FileShare.None) : null;
        var result = new VerifyService(resolver, changes, [], workspaceRegistry: registry).Diff(authority.Path, created.Change.Id);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Snapshot!.Files, x => x.Path == "src/owned.txt");
        Assert.DoesNotContain(result.Snapshot.Files, x => x.Path.StartsWith("external/dependency/", StringComparison.Ordinal));
        if (scenario == "rename-out") Assert.Contains(result.Snapshot.Files, x => x.Path == "src/example.txt" && x.Status == "D");
        if (scenario == "rename-in") Assert.Contains(result.Snapshot.Files, x => x.Path == "src/imported.txt" && x.Status == "A");
        if (scenario.StartsWith("preexisting-", StringComparison.Ordinal))
            Assert.DoesNotContain(result.Snapshot.Files, x => x.Path is "src/example.txt" or "src/imported.txt");

        void MoveAcrossBoundary()
        {
            var intoOwned = scenario.EndsWith("-in", StringComparison.Ordinal);
            var source = intoOwned ? "external/dependency/src/file.txt" : "src/example.txt";
            var target = intoOwned ? "src/imported.txt" : "external/dependency/moved.txt";
            File.Move(Path.Combine(authority.Path, source), Path.Combine(authority.Path, target));
            authority.Git("add", "--all");
        }
    }
}
