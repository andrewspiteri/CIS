using Cis.Abstractions;
using Cis.Modules.Artifacts;
using Cis.Modules.Repository;

namespace Cis.Modules.Artifacts.Tests;

public sealed partial class ArtifactRetentionTests
{
    [Fact]
    public void DisjointReservedGrowthDoesNotRejectThisOperationsPermittedCompaction()
    {
        using var repository = Fixture.Create();
        using var other = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", new string('a', 200_000));
        repository.Age(".cis/local/runs/old/result.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var otherRoot = Path.Combine(other.Path, ".cis/local");
        var authority = new ArtifactAuthority(repository.Path)
        {
            OtherReservedBytes = 2 * 1024 * 1024,
            AdditionalOwned = [otherRoot],
            AfterFirstReservation = () => other.Write(".cis/local/build/output.txt", new string('b', 1024 * 1024))
        };

        var result = repository.Service(authority, authority).Compact(repository.Path, "runs", true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("compacted", result.Status);
        Assert.Single(authority.RequestedGrowth); Assert.Single(authority.GrantedGrowth);
        Assert.True(result.OwnedStorage!.Storage!.OwnedBytes > authority.GrantedGrowth[0]);
        Assert.Equal(2 * 1024 * 1024, result.OwnedStorage.Storage.ReservedBytes);
        Assert.Equal(1024 * 1024, new FileInfo(Path.Combine(otherRoot, "build/output.txt")).Length);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis/local/runs/old")));
        Assert.Single(result.Archives);
    }

    [Fact]
    public void EmptyNestedArchiveNamesCannotSpendCapacityReservedForOtherWork()
    {
        using var repository = Fixture.Create();
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 400; index++)
        {
            var relative = ".cis/local/runs/old/nested/file-" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + new string('é', 48) + ".txt";
            repository.Write(relative, "");
            repository.Age(relative, old);
        }
        const long remaining = 96 * 1024;
        var authority = new ArtifactAuthority(repository.Path) { OtherReservedBytes = 16 * 1024 * 1024 - remaining };

        var result = repository.Service(authority, authority).Compact(repository.Path, "runs", true);

        Assert.Equal(4, result.ExitCode);
        Assert.True(Assert.Single(authority.RequestedGrowth) > remaining);
        Assert.Empty(authority.GrantedGrowth);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ArtifactRetentionService.StateRoot)));
        Assert.Equal(400, Directory.GetFiles(Path.Combine(repository.Path, ".cis/local/runs/old"), "*", SearchOption.AllDirectories).Length);
        Assert.Equal(authority.OtherReservedBytes, result.OwnedStorage!.Storage!.ReservedBytes);
    }

    [Fact]
    public void CompletedArtifactWritesAreCheckedAgainstTheActualGrantBeforeSettlement()
    {
        using var repository = Fixture.Create();
        const string relative = ".cis/local/runs/old/result.txt";
        repository.Write(relative, "");
        repository.Age(relative, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var authority = new ArtifactAuthority(repository.Path) { MaximumGrantedGrowth = 1 };

        var result = repository.Service(authority, authority).Compact(repository.Path, "runs", true);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("operation's admitted growth", StringComparison.Ordinal));
        Assert.Equal(1, Assert.Single(authority.GrantedGrowth));
        Assert.True(Assert.Single(authority.RequestedGrowth) > 1);
        Assert.True(result.OwnedStorage!.Storage!.OwnedBytes < result.OwnedStorage.Storage.MaximumBytes);
        Assert.Equal(1, authority.Settlements);
        Assert.Equal(1, authority.Finishes);
        if (!File.Exists(Path.Combine(repository.Path, relative)))
        {
            var archive = Assert.Single(result.Archives);
            using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(repository.Path, archive.ZipPath));
            Assert.NotNull(zip.GetEntry(relative));
        }
    }

    [Fact]
    public void OwnedRetentionProtectsCurrentEvidenceAndReservesArchiveAndRetrievalGrowth()
    {
        using var repository = Fixture.Create();
        repository.Write(".cis/local/runs/old/result.txt", new string('a', 200_000));
        repository.Write(".cis/local/runs/current/report.json", "required native evidence");
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        repository.Age(".cis/local/runs/old/result.txt", old);
        repository.Age(".cis/local/runs/current/report.json", old);
        var authority = new ArtifactAuthority(repository.Path)
        { Protected = [Path.Combine(repository.Path, ".cis/local/runs/current")] };
        var service = repository.Service(authority, authority);
        var plan = service.Plan(repository.Path, "runs");
        Assert.Empty(plan.Errors);
        Assert.False(Assert.Single(plan.Entries, item => item.Path.EndsWith("/current", StringComparison.Ordinal)).Candidate);
        Assert.True(plan.FamilyStorage.Single().ProtectedBytes > 0);
        var compacted = service.Compact(repository.Path, "runs", true);
        Assert.Empty(compacted.Errors); Assert.Equal("compacted", compacted.Status);
        Assert.True(authority.RequestedGrowth[0] > 400_000);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis/local/runs/current/report.json")));
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis/local/runs/old")));
        Assert.Equal(1, authority.Settlements); Assert.Equal(1, authority.Finishes);
        Assert.NotNull(compacted.OwnedStorage?.Storage);
        var before = compacted.OwnedStorage!.Storage!.OwnedBytes;
        var archive = Assert.Single(compacted.Archives);
        authority.DenyGrowth = true;
        var held = service.Retrieve(repository.Path, archive.ArchiveId);
        Assert.Equal(4, held.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis/local/artifacts/retrieved")));
        authority.DenyGrowth = false;
        var retrieved = service.Retrieve(repository.Path, archive.ArchiveId);
        Assert.Empty(retrieved.Errors);
        Assert.True(retrieved.OwnedStorage!.Storage!.OwnedBytes > before);
        Assert.True(retrieved.Storage!.ManagementBytes > retrieved.Storage.ArchiveBytes);
        Assert.Equal(retrieved.Storage.ConfiguredLiveBytes + retrieved.Storage.ManagementBytes, retrieved.Storage.TotalReportedBytes);
    }

    [Fact]
    public void AdoptedArtifactWritesWithoutAuthorityHoldButReadOnlyInventoryRetainsItsEvidence()
    {
        using var repository = Fixture.Create();
        repository.Write(CisAgentWorkPaths.Adoption, "{\"retained\":true}");
        repository.Write(".cis/local/cache/old.txt", "retained diagnostic");
        repository.Age(".cis/local/cache/old.txt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = new ArtifactRetentionService(new CisRepositoryContextResolver(), () => DateTimeOffset.Parse("2026-08-27T12:00:00Z"));
        var read = service.Inventory(repository.Path, "cache");
        Assert.Equal(4, read.ExitCode); Assert.NotEmpty(read.Entries);
        Assert.All(read.Entries, item => { Assert.False(item.Candidate); Assert.True(item.Protected); });
        Assert.Equal("unknown", read.OwnedStorage!.Status);
        var held = service.Clean(repository.Path, "cache", true);
        Assert.Equal(4, held.ExitCode);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis/local/cache/old.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedRestoreHoldsArchivedDestinationsOutsideCurrentRootsOrUnderRequiredEvidence(bool protectedEvidence)
    {
        using var repository = Fixture.Create();
        const string relative = ".cis/local/runs/old/result.txt";
        repository.Write(relative, "archived native evidence");
        repository.Age(relative, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var archive = Assert.Single(repository.Service().Compact(repository.Path, "runs", true).Archives);
        var target = Path.GetFullPath(Path.Combine(repository.Path, ".cis/local/runs/old"));
        var authority = new ArtifactAuthority(repository.Path)
        {
            Owned = protectedEvidence ? null : [Path.GetFullPath(Path.Combine(repository.Path, ".cis/local/artifacts"))],
            Protected = protectedEvidence ? [target] : []
        };

        var restored = repository.Service(authority, authority).Restore(repository.Path, archive.ArchiveId, true);

        Assert.Equal(4, restored.ExitCode);
        Assert.Contains(restored.Errors, error => error.Contains("outside the current owned roots or contains protected", StringComparison.Ordinal));
        Assert.False(Directory.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis/local/artifacts/restore")));
        Assert.DoesNotContain(target, authority.ClaimedPaths);
    }

    [Fact]
    public void OwnedRestoreUsesTheNativeOutputLeaseForActualCrossFamilyDestinations()
    {
        using var repository = Fixture.Create();
        repository.Write("docs/cis/references/local-artifact-retention.md", """
            | Family | Path | Retain days | Keep latest | Action |
            |---|---|---:|---:|---|
            | native | .cis/local/testing/incremental/reports | 7 | 0 | archive |
            """);
        const string relative = ".cis/local/testing/incremental/reports/old/report.json";
        repository.Write(relative, "retained native report");
        repository.Age(relative, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var archive = Assert.Single(repository.Service().Compact(repository.Path, "native", true).Archives);
        var authority = new ArtifactAuthority(repository.Path);
        var service = repository.Service(authority, authority);
        var nativeLock = Path.Combine(repository.Path, ".cis/local/testing/mutation/run.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeLock)!);
        using (var liveNativeLease = new FileStream(nativeLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var held = service.Restore(repository.Path, archive.ArchiveId, true);
            Assert.Equal(4, held.ExitCode);
            Assert.False(File.Exists(Path.Combine(repository.Path, relative)));
        }

        var restored = service.Restore(repository.Path, archive.ArchiveId, true);

        Assert.Empty(restored.Errors);
        Assert.Equal("restored", restored.Status);
        Assert.Equal("retained native report", File.ReadAllText(Path.Combine(repository.Path, relative)));
        Assert.Contains(Path.GetFullPath(Path.Combine(repository.Path, ".cis/local/testing/incremental/reports/old")), authority.ClaimedPaths);
    }

    private sealed class ArtifactAuthority(string repository, bool governed = true) : ICisNativeWorkController, ICisOwnedStorageController
    {
        private readonly CisAgentWorkReference reference = new("fixture-adoption", "artifacts-fixture", "CIS-0001", "WORK-A", "WORK-A", "native-artifacts", "fixture-attempt", null);
        private readonly Dictionary<string, (string Id, long Growth, long Initial, string[] Paths)> reservations = new(StringComparer.Ordinal);
        private bool firstReservationObserved;
        public string[] Protected { get; init; } = [];
        public string[]? Owned { get; init; }
        public string[] AdditionalOwned { get; init; } = [];
        public Action? AfterFirstReservation { get; init; }
        public bool DenyGrowth { get; set; }
        public long OtherReservedBytes { get; init; }
        public long? MaximumGrantedGrowth { get; init; }
        public List<long> RequestedGrowth { get; } = [];
        public List<long> GrantedGrowth { get; } = [];
        public List<string> ClaimedPaths { get; } = [];
        public int Settlements { get; private set; }
        public int Finishes { get; private set; }
        public CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null)
            => new(governed, governed ? "current" : "unaccounted", "controlled authority fixture", null, null, []) { AuthorityPath = repository };
        public CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference work)
            => work == reference ? new(true, "current", "controlled execution fixture", reference, null, [])
                : new(true, "held", "controlled execution fixture", null, null, ["Unknown fixture work reference."]);
        public CisNativeWorkAdmission Admit(CisNativeWorkRequest request)
            => governed ? new(true, "admitted", "controlled authority fixture", reference, null, []) { AuthorityPath = repository, Scope = new Scope(this, reference, repository) }
                : new(false, "unaccounted", "non-adopted controlled fixture", null, null, []);
        CisOwnedStorageResult ICisOwnedStorageController.Inspect(string repositoryPath, CisNativeWorkOptions? association)
        {
            if (!governed) return new("unaccounted", false, null, []);
            var local = Path.GetFullPath(Path.Combine(repository, ".cis/local"));
            var bytes = Bytes(new[] { local }.Concat(AdditionalOwned));
            var controls = new[] { CisAgentWorkPaths.Ledger, CisAgentWorkPaths.Adoption, CisAgentWorkPaths.Lock }
                .Select(path => Path.GetFullPath(Path.Combine(repository, path))).ToArray();
            return new("current", true, new("artifacts-fixture", "CIS-0001", "WORK-A", "fixture finite ceiling", 16 * 1024 * 1024,
                bytes, 0, OtherReservedBytes, (Owned ?? [local]).Concat(AdditionalOwned).Select(Path.GetFullPath).ToArray(),
                Protected.Concat(controls).Select(Path.GetFullPath).ToArray(), "fixture-revision"), []);
        }
        public CisOwnedStorageAdmission Reserve(string repositoryPath, CisAgentWorkReference work, string operationId,
            long maximumGrowthBytes, IReadOnlyList<CisOwnedStoragePath>? claims = null, bool cleanup = false,
            bool clampToRemaining = false, string? actor = null)
        {
            var inventory = ((ICisOwnedStorageController)this).Inspect(repositoryPath);
            if (reservations.TryGetValue(operationId, out var previous))
            {
                var paths = previous.Paths.Concat((claims ?? []).Select(claim => Path.GetFullPath(claim.Path))).Distinct().ToArray();
                var initial = previous.Initial + Bytes(paths) - Bytes(previous.Paths);
                reservations[operationId] = (previous.Id, previous.Growth, initial, paths);
                ClaimedPaths.AddRange((claims ?? []).Select(claim => Path.GetFullPath(claim.Path)));
                return new(inventory, previous.Id, previous.Growth)
                { Operation = new(paths, initial, Bytes(paths), previous.Growth) };
            }
            RequestedGrowth.Add(maximumGrowthBytes);
            var granted = Math.Min(maximumGrowthBytes, MaximumGrantedGrowth ?? long.MaxValue);
            if (DenyGrowth || inventory.Storage is null || granted > inventory.Storage.RemainingBytes)
                return new(inventory with { Status = "held", Errors = ["The finite owner ceiling holds artifact growth."] }, null, 0);
            ClaimedPaths.AddRange((claims ?? []).Select(claim => Path.GetFullPath(claim.Path)));
            GrantedGrowth.Add(granted);
            var domain = (claims ?? []).Select(claim => Path.GetFullPath(claim.Path)).Distinct().ToArray();
            var baseline = Bytes(domain);
            var id = Guid.NewGuid().ToString("N");
            reservations[operationId] = (id, granted, baseline, domain);
            var admission = new CisOwnedStorageAdmission(inventory, id, granted)
            { Operation = new(domain, baseline, baseline, granted) };
            if (!firstReservationObserved) { firstReservationObserved = true; AfterFirstReservation?.Invoke(); }
            return admission;
        }
        public CisOwnedStorageResult Settle(string repositoryPath, CisAgentWorkReference work, string reservationId,
            bool safelyStopped, IReadOnlyList<string>? requiredPaths = null, IReadOnlyList<string>? supersededRequiredPaths = null)
        {
            Assert.True(safelyStopped); Settlements++;
            foreach (var key in reservations.Where(pair => pair.Value.Id == reservationId).Select(pair => pair.Key).ToArray()) reservations.Remove(key);
            return ((ICisOwnedStorageController)this).Inspect(repositoryPath);
        }
        private static long Bytes(IEnumerable<string> roots)
        {
            var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var root in roots)
                foreach (var file in File.Exists(root) ? new[] { root }
                    : Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : [])
                    files.Add(Path.GetFullPath(file));
            return files.Sum(path => new FileInfo(path).Length);
        }
        private sealed class Scope(ArtifactAuthority owner, CisAgentWorkReference work, string repository) : ICisNativeWorkScope
        {
            public string AuthorityPath => repository;
            public string Actor => "Controller";
            public CisAgentWorkReference Reference => work;
            public int MaximumSeconds => 3600;
            public TimeSpan Remaining => TimeSpan.FromSeconds(3600);
            public CisNativeWorkAdmission AdmitChild(string executionId, string operation, int maximumSeconds) => throw new NotSupportedException();
            public void Launch() => throw new InvalidOperationException("Managed retention cannot launch a provider.");
            public void Started(int processId, string processStartedAtUtc, CisNativeWorkContainment? containment = null) => throw new NotSupportedException();
            public void Returned(bool safelyStopped) { }
            public void MarkUncertain(string reason) => throw new NotSupportedException();
            public CisAgentWorkResult Finish(string status, string? summary, IReadOnlyList<string>? evidence)
            { owner.Finishes++; return new("settled", null, [], false); }
            public void Dispose() { }
        }
    }
}
