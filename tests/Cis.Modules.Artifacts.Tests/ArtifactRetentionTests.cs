using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Modules.Artifacts;
using Cis.Modules.Repository;

namespace Cis.Modules.Artifacts.Tests;

public sealed class ArtifactRetentionTests
{
    [Fact]
    public void Compact_VerifiesArchiveDeletesOnlyCandidateAndSupportsRetrieveAndRestore()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "old evidence");
        repository.Write(".cis/local/runs/new/result.txt", "new evidence");
        repository.Age(".cis/local/runs/old/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        repository.Age(".cis/local/runs/new/result.txt", DateTime.Parse("2026-08-26T00:00:00Z").ToUniversalTime());
        var service = repository.Service();

        var plan = service.Plan(repository.Path, "runs");
        Assert.Single(plan.Entries, item => item.Candidate);
        Assert.Equal(".cis/local/runs/old", plan.Entries.Single(item => item.Candidate).Path);
        Assert.Equal(4, service.Compact(repository.Path, "runs", false).ExitCode);

        var compacted = service.Compact(repository.Path, "runs", true);

        Assert.Equal("compacted", compacted.Status);
        Assert.False(Directory.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "runs", "old")));
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "runs", "new", "result.txt")));
        var archive = Assert.Single(compacted.Archives);
        var retrieved = service.Retrieve(repository.Path, archive.ArchiveId);
        Assert.Equal("retrieved", retrieved.Status);
        Assert.Contains(retrieved.Outputs, path => path.EndsWith(".cis/local/runs/old/result.txt", StringComparison.Ordinal));

        var restored = service.Restore(repository.Path, archive.ArchiveId, true);
        Assert.Equal("restored", restored.Status);
        Assert.Equal("old evidence", File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "local", "runs", "old", "result.txt")));
    }

    [Fact]
    public void Restore_RejectsConflictingExistingContent()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "archived");
        repository.Age(".cis/local/runs/old/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        var service = repository.Service();
        var archive = Assert.Single(service.Compact(repository.Path, "runs", true).Archives);
        repository.Write(".cis/local/runs/old/result.txt", "conflict");

        var restored = service.Restore(repository.Path, archive.ArchiveId, true);

        Assert.Equal(4, restored.ExitCode);
        Assert.Contains(restored.Errors, error => error.Contains("Restore conflict", StringComparison.Ordinal));
        Assert.Equal("conflict", File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "local", "runs", "old", "result.txt")));
    }

    [Fact]
    public void Clean_RetainsHashTombstoneAndNeverTargetsCanonicalFiles()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/cache/old.json", "derived");
        repository.Write("docs/cis/canonical.md", "canonical");
        repository.Age(".cis/local/cache/old.json", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());

        var cleaned = repository.Service().Clean(repository.Path, "cache", true);

        Assert.Equal("cleaned", cleaned.Status);
        Assert.False(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "cache", "old.json")));
        Assert.Equal("canonical", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "canonical.md")));
        var record = Assert.Single(cleaned.Outputs);
        Assert.Contains(cleaned.Entries.Single().Digest, File.ReadAllText(System.IO.Path.Combine(repository.Path, record.Replace('/', System.IO.Path.DirectorySeparatorChar))), StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_ReadsWorkflowLogsWhileWriterStillOwnsTheFile()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/active/step.log", "partial evidence");
        var path = System.IO.Path.Combine(repository.Path, ".cis", "local", "runs", "active", "step.log");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        var inventory = repository.Service().Inventory(repository.Path, "runs");

        Assert.Empty(inventory.Errors);
        Assert.Single(inventory.Entries);
        Assert.NotEmpty(inventory.Entries[0].Digest);
    }

    [Fact]
    public void Retrieve_RejectsAnUndeclaredArchiveEntryEvenWhenManifestDigestWasRewritten()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "archived");
        repository.Age(".cis/local/runs/old/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        var service = repository.Service();
        var manifest = Assert.Single(service.Compact(repository.Path, "runs", true).Archives);
        var zipPath = System.IO.Path.Combine(repository.Path, manifest.ZipPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            var injected = archive.CreateEntry(".cis/local/injected.txt");
            using var writer = new StreamWriter(injected.Open());
            writer.Write("not declared");
        }
        var manifestPath = System.IO.Path.Combine(repository.Path, ".cis", "local", "artifacts", "archive", manifest.ArchiveId + ".json");
        var rewritten = manifest with { ZipDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zipPath))) };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(rewritten, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        var result = service.Retrieve(repository.Path, manifest.ArchiveId);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("not declared", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(System.IO.Path.Combine(repository.Path, ".cis", "local", "artifacts", "retrieved", manifest.ArchiveId, ".cis", "local", "injected.txt")));
    }

    [Fact]
    public void Compact_UsesACollisionSafeArchiveIdentityWithinTheSameSecond()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "archived");
        repository.Age(".cis/local/runs/old/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        var service = repository.Service();
        var first = Assert.Single(service.Compact(repository.Path, "runs", true).Archives);
        Assert.Equal("restored", service.Restore(repository.Path, first.ArchiveId, true).Status);
        repository.Age(".cis/local/runs/old/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());

        var secondResult = service.Compact(repository.Path, "runs", true);

        Assert.Equal("compacted", secondResult.Status);
        Assert.Equal(2, secondResult.Archives.Count);
        Assert.Contains(secondResult.Archives, item => item.ArchiveId.EndsWith("-2", StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; }
        private Fixture(string path) => Path = path;
        public static Fixture Create()
        {
            var fixture = new Fixture(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-artifacts-tests", Guid.NewGuid().ToString("N")));
            fixture.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: artifacts-fixture\ndocumentation_root: docs/cis\n");
            fixture.Write("docs/cis/references/local-artifact-retention.md", """
                | Family | Path | Retain days | Keep latest | Action |
                |---|---|---:|---:|---|
                | runs | .cis/local/runs | 7 | 0 | archive |
                | cache | .cis/local/cache | 7 | 0 | delete |
                """);
            return fixture;
        }
        public ArtifactRetentionService Service() => new(new CisRepositoryContextResolver(), () => DateTimeOffset.Parse("2026-08-27T12:00:00Z"));
        public void Write(string relative, string content)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
        public void Age(string relative, DateTime value) => File.SetLastWriteTimeUtc(System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)), value);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
