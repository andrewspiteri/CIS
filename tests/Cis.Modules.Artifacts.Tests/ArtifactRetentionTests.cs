using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Modules.Artifacts;
using Cis.Modules.Repository;

namespace Cis.Modules.Artifacts.Tests;

public sealed class ArtifactRetentionTests
{
    [Fact]
    public void Doctor_SkipsFullyProtectedFamiliesButDetectsEligibilityWhenKeepLatestIsExceeded()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action |
            |---|---|---:|---:|---|
            | runs | .cis/local/runs | 7 | 2 | archive |
            """);
        foreach (var name in new[] { "old", "new" })
        {
            repository.Write($".cis/local/runs/{name}/nested/result.txt", "retained");
            repository.Age($".cis/local/runs/{name}/nested/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        }
        var service = repository.Service(); var doctor = new ArtifactDoctorCheck(service);
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        Assert.Empty(doctor.Inspect(context));
        Assert.Equal(2, service.Plan(repository.Path).Entries.Count);
        repository.Write(".cis/local/runs/latest/result.txt", "recent");
        var finding = Assert.Single(doctor.Inspect(context));
        Assert.Equal(Assert.Single(service.Plan(repository.Path).Entries, e => e.Candidate).Path, Assert.Single(finding.Evidence));
    }

    [Fact]
    public void Doctor_DoesNotOpenRetainedArtifactContents()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "retained evidence");
        repository.Age(".cis/local/runs/old/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        var service = repository.Service();
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        using var locked = new FileStream(System.IO.Path.Combine(repository.Path, ".cis/local/runs/old/result.txt"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var finding = Assert.Single(new ArtifactDoctorCheck(service).Inspect(context));

        Assert.Equal("CIS-ARTIFACT-DOCTOR-002", finding.Code);
        // Explicit plans still require a real digest and cannot silently reuse a health summary.
        Assert.Throws<IOException>(() => service.Plan(repository.Path));
    }

    [Fact]
    public void Doctor_MatchesPlanEligibilityAndRechecksNestedFileEdits()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action |
            |---|---|---:|---:|---|
            | runs | .cis/local/runs | 7 | 1 | archive |
            """);
        repository.Write(".cis/local/runs/old/nested/result.txt", "old");
        repository.Write(".cis/local/runs/new/result.txt", "new");
        repository.Age(".cis/local/runs/old/nested/result.txt", DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime());
        repository.Age(".cis/local/runs/new/result.txt", DateTime.Parse("2026-02-01T00:00:00Z").ToUniversalTime());
        var service = repository.Service();
        var doctor = new ArtifactDoctorCheck(service);
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        var before = Assert.Single(doctor.Inspect(context));
        var plan = service.Plan(repository.Path);
        Assert.Equal(Assert.Single(plan.Entries, entry => entry.Candidate).Path, Assert.Single(before.Evidence));
        Assert.Equal(".cis/local/runs/old", Assert.Single(before.Evidence));

        repository.Age(".cis/local/runs/old/nested/result.txt", DateTime.Parse("2026-08-27T11:00:00Z").ToUniversalTime());

        var after = Assert.Single(doctor.Inspect(context));
        Assert.Equal(".cis/local/runs/new", Assert.Single(after.Evidence));
        Assert.Equal(Assert.Single(service.Plan(repository.Path).Entries, entry => entry.Candidate).Path,
            Assert.Single(after.Evidence));
    }

    [Fact]
    public void Inventory_PreservesNestedSizesTimestampsAndEmptyDirectoryDigests()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/tree/a.txt", "abc");
        repository.Write(".cis/local/runs/tree/nested/b.txt", "12345");
        var stamp = DateTime.Parse("2026-02-01T00:00:00Z").ToUniversalTime();
        repository.Age(".cis/local/runs/tree/a.txt", stamp.AddDays(-1));
        repository.Age(".cis/local/runs/tree/nested/b.txt", stamp);
        Directory.CreateDirectory(System.IO.Path.Combine(repository.Path, ".cis/local/runs/empty"));

        var entries = repository.Service().Plan(repository.Path).Entries;

        var tree = Assert.Single(entries, entry => entry.Path.EndsWith("/tree", StringComparison.Ordinal));
        Assert.Equal(8, tree.SizeBytes);
        Assert.Equal(stamp, DateTimeOffset.Parse(tree.LastWriteUtc).UtcDateTime);
        Assert.Equal(64, tree.Digest.Length);
        var empty = Assert.Single(entries, entry => entry.Path.EndsWith("/empty", StringComparison.Ordinal));
        Assert.Equal(0, empty.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([])), empty.Digest);
    }

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

    [Fact]
    public void Plan_BytePressureSelectsOnlyOldestUnprotectedRecentEntries()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action | Maximum bytes |
            |---|---|---:|---:|---|---:|
            | runs | .cis/local/runs | 30 | 1 | delete | 8 |
            """);
        foreach (var (name, day) in new[] { ("old", 24), ("middle", 25), ("new", 26) })
        {
            repository.Write($".cis/local/runs/{name}/result.txt", "1234");
            repository.Age($".cis/local/runs/{name}/result.txt", new DateTime(2026, 8, day, 0, 0, 0, DateTimeKind.Utc));
        }

        var service = repository.Service();
        var plan = service.Plan(repository.Path);

        Assert.Empty(plan.Errors);
        Assert.Equal(".cis/local/runs/old", Assert.Single(plan.Entries, item => item.Candidate).Path);
        var usage = Assert.Single(plan.FamilyStorage);
        Assert.Equal(12, usage.LiveBytes);
        Assert.Equal(4, usage.ProtectedBytes);
        Assert.Equal(4, usage.CandidateBytes);
        Assert.Equal(8, usage.PlannedLiveBytes);
        Assert.True(usage.BudgetExceeded);
        Assert.False(usage.BudgetBlocked);
        Assert.Equal(4, service.Clean(repository.Path, "runs", false).ExitCode);
        Assert.True(Directory.Exists(System.IO.Path.Combine(repository.Path, ".cis/local/runs/old")));
        var cleaned = service.Clean(repository.Path, "runs", true);
        Assert.Equal("cleaned", cleaned.Status);
        Assert.Equal(8, Assert.Single(cleaned.FamilyStorage).LiveBytes);
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".cis/local/runs/new/result.txt")));
    }

    [Fact]
    public void InitializedRepository_ProvidesTheMutationRunBudget()
    {
        using var repository = Fixture.CreateInitialized();

        var inventory = repository.Service().Inventory(repository.Path, "mutation-runs");

        Assert.Empty(inventory.Errors);
        var rule = Assert.Single(inventory.Rules);
        Assert.Equal(".cis/local/testing/mutation/runs", rule.Path);
        Assert.Equal("delete", rule.Action);
        Assert.Equal(1, rule.RetainDays);
        Assert.Equal(2, rule.KeepLatest);
        Assert.Equal(536870912, rule.MaximumBytes);
    }

    [Fact]
    public void Plan_ProtectedEntriesExceedingBudgetBlockCleanupWithoutDeletingAnything()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action | Maximum bytes |
            |---|---|---:|---:|---|---:|
            | runs | .cis/local/runs | 1 | 2 | delete | 3 |
            """);
        repository.Write(".cis/local/runs/first.txt", "1234");
        repository.Write(".cis/local/runs/second.txt", "5678");

        var service = repository.Service();
        var plan = service.Plan(repository.Path);

        Assert.Equal(4, plan.ExitCode);
        Assert.Contains(plan.Errors, error => error.Contains("8 protected bytes", StringComparison.Ordinal));
        Assert.True(Assert.Single(plan.FamilyStorage).BudgetBlocked);
        Assert.DoesNotContain(plan.Entries, entry => entry.Candidate);
        Assert.False(service.Clean(repository.Path, "runs", true).Applied);
        Assert.Equal(2, Directory.GetFiles(System.IO.Path.Combine(repository.Path, ".cis/local/runs")).Length);
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        Assert.Equal("CIS-ARTIFACT-DOCTOR-001", Assert.Single(new ArtifactDoctorCheck(service).Inspect(context)).Code);
    }

    [Fact]
    public void Plan_AgeCandidatesCountTowardBudgetAndPreserveRemainsProtected()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action | Maximum bytes |
            |---|---|---:|---:|---|---:|
            | runs | .cis/local/runs | 7 | 1 | delete | 4 |
            | retained | .cis/local/retained | 0 | 0 | preserve | 1 |
            """);
        repository.Write(".cis/local/runs/old.txt", "1234");
        repository.Write(".cis/local/runs/new.txt", "5678");
        repository.Age(".cis/local/runs/old.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        repository.Age(".cis/local/runs/new.txt", new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc));
        repository.Write(".cis/local/retained/result.txt", "retain");

        var plan = repository.Service().Plan(repository.Path);

        Assert.Single(plan.Entries, entry => entry.Candidate);
        Assert.False(Assert.Single(plan.FamilyStorage, usage => usage.Family == "runs").BudgetBlocked);
        Assert.True(Assert.Single(plan.FamilyStorage, usage => usage.Family == "retained").BudgetBlocked);
        Assert.DoesNotContain(plan.Entries, entry => entry.Family == "retained" && entry.Candidate);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("512 MiB")]
    [InlineData("9223372036854775808")]
    public void Plan_RejectsInvalidByteBudgets(string budget)
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", $"| runs | .cis/local/runs | 1 | 0 | delete | {budget} |");

        var plan = repository.Service().Plan(repository.Path);

        Assert.Equal(4, plan.ExitCode);
        Assert.Contains(plan.Errors, error => error.Contains("Invalid artifact byte budget", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_FiveColumnsAndEmptyBudgetPreserveAgeOnlyBehavior()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | old-format | .cis/local/first | 7 | 0 | delete |
            | empty-budget | .cis/local/second | 7 | 0 | delete | |
            """);
        repository.Write(".cis/local/first/recent.txt", "recent");
        repository.Write(".cis/local/second/recent.txt", "recent");

        var plan = repository.Service().Plan(repository.Path);

        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Rules.Count);
        Assert.All(plan.Rules, rule => Assert.Null(rule.MaximumBytes));
        Assert.DoesNotContain(plan.Entries, entry => entry.Candidate);
    }

    [Fact]
    public void Doctor_FindsBytePressureWithoutOpeningArtifactContent()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", "| runs | .cis/local/runs | 30 | 0 | delete | 0 |");
        repository.Write(".cis/local/runs/recent.txt", "recent");
        var path = System.IO.Path.Combine(repository.Path, ".cis/local/runs/recent.txt");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;

        var finding = Assert.Single(new ArtifactDoctorCheck(repository.Service()).Inspect(context));

        Assert.Equal("CIS-ARTIFACT-DOCTOR-002", finding.Code);
        Assert.Equal(".cis/local/runs/recent.txt", Assert.Single(finding.Evidence));
    }

    [Fact]
    public void Compact_ReportsArchiveGrowthSeparatelyFromLiveFamilyBytes()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", new string('a', 200_000));
        repository.Write(".cis/local/runs/old/nested/z.txt", "last");
        repository.Write(".cis/local/runs/old/nested/a.txt", "first");
        repository.Age(".cis/local/runs/old/result.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        repository.Age(".cis/local/runs/old/nested/z.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        repository.Age(".cis/local/runs/old/nested/a.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = repository.Service();

        var compacted = service.Compact(repository.Path, "runs", true);

        Assert.Empty(compacted.Errors);
        var storage = Assert.IsType<ArtifactStorageTotals>(compacted.Storage);
        Assert.Equal(0, storage.ConfiguredLiveBytes);
        Assert.True(storage.ArchiveBytes > 0);
        Assert.True(storage.ManagementBytes > storage.ArchiveBytes);
        Assert.Equal(storage.ManagementBytes, storage.TotalReportedBytes);
        var retrieved = service.Retrieve(repository.Path, Assert.Single(compacted.Archives).ArchiveId);
        Assert.Equal("retrieved", retrieved.Status);
        Assert.True(retrieved.Storage!.ManagementBytes > storage.ManagementBytes);
    }

    [Fact]
    public void CompactAndRetrieve_PreserveAnEmptyDirectory()
    {
        using var repository = Fixture.Create();
        var directory = System.IO.Path.Combine(repository.Path, ".cis/local/runs/empty");
        Directory.CreateDirectory(directory);
        Directory.SetLastWriteTimeUtc(directory, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = repository.Service();

        var compacted = service.Compact(repository.Path, "runs", true);
        var archive = Assert.Single(compacted.Archives);
        var retrieved = service.Retrieve(repository.Path, archive.ArchiveId);

        Assert.Empty(compacted.Errors);
        Assert.Empty(retrieved.Errors);
        Assert.True(Directory.Exists(System.IO.Path.Combine(repository.Path, ".cis/local/artifacts/retrieved", archive.ArchiveId, ".cis/local/runs/empty")));
    }

    [Fact]
    public void Retrieve_RejectsAMissingDeclaredEmptyDirectory()
    {
        using var repository = Fixture.Create();
        var directory = System.IO.Path.Combine(repository.Path, ".cis/local/runs/empty");
        Directory.CreateDirectory(directory);
        Directory.SetLastWriteTimeUtc(directory, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = repository.Service();
        var manifest = Assert.Single(service.Compact(repository.Path, "runs", true).Archives);
        RewriteArchive(repository, manifest, archive => archive.GetEntry(".cis/local/runs/empty/")!.Delete());

        var result = service.Retrieve(repository.Path, manifest.ArchiveId);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("Archive verification failed", StringComparison.Ordinal));
        Assert.Empty(result.Outputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetrieveAndRestore_VerifyDeclaredContentEvenWhenZipDigestIsRewritten(bool restore)
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "original");
        repository.Age(".cis/local/runs/old/result.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = repository.Service();
        var manifest = Assert.Single(service.Compact(repository.Path, "runs", true).Archives);
        RewriteArchive(repository, manifest, archive =>
        {
            archive.GetEntry(".cis/local/runs/old/result.txt")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(".cis/local/runs/old/result.txt").Open());
            writer.Write("modified");
        });

        var result = restore ? service.Restore(repository.Path, manifest.ArchiveId, true) : service.Retrieve(repository.Path, manifest.ArchiveId);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("Archive verification failed", StringComparison.Ordinal));
        Assert.Empty(result.Outputs);
        Assert.False(Directory.Exists(System.IO.Path.Combine(repository.Path, ".cis/local/runs/old")));
    }

    [Theory]
    [InlineData("../outside.txt", false)]
    [InlineData(".cis/local/runs/old/result.txt", false)]
    [InlineData(".cis/local/runs/old/link", true)]
    public void Retrieve_StillRejectsTraversalDuplicateAndSymbolicLinkEntries(string name, bool symbolicLink)
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", "original");
        repository.Age(".cis/local/runs/old/result.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = repository.Service();
        var manifest = Assert.Single(service.Compact(repository.Path, "runs", true).Archives);
        RewriteArchive(repository, manifest, archive =>
        {
            var entry = archive.CreateEntry(name);
            if (symbolicLink) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("injected");
        });

        var result = service.Retrieve(repository.Path, manifest.ArchiveId);

        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Outputs);
    }

    [Theory]
    [InlineData("| state | .cis/local/artifacts | 0 | 0 | delete |")]
    [InlineData("| state | .cis/local/artifacts\\archive | 0 | 0 | delete |")]
    [InlineData("| first | .cis/local/runs | 0 | 0 | delete |\n| nested | .cis/local/runs/nested | 0 | 0 | delete |")]
    [InlineData("| first | .cis/local/runs | 0 | 0 | delete |\n| nested | .cis/local/runs\\nested | 0 | 0 | delete |")]
    public void Plan_RejectsManagementAndOverlappingFamilyAuthority(string profile)
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", profile);

        Assert.Equal(4, repository.Service().Plan(repository.Path).ExitCode);
    }

    private static void RewriteArchive(Fixture repository, ArtifactArchiveManifest manifest, Action<ZipArchive> edit)
    {
        var path = System.IO.Path.Combine(repository.Path, manifest.ZipPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update)) edit(archive);
        var rewritten = manifest with { ZipDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
        var manifestPath = System.IO.Path.Combine(repository.Path, ".cis/local/artifacts/archive", manifest.ArchiveId + ".json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(rewritten, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; }
        private Fixture(string path) => Path = path;
        public static Fixture CreateInitialized()
        {
            var fixture = new Fixture(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-artifacts-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture.Path);
            var initialized = new RepositoryInitializer().Initialize(new RepositoryInitRequest(fixture.Path, "docs/cis", DryRun: false, Confirmed: true));
            Assert.Equal(0, initialized.ExitCode);
            return fixture;
        }
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
