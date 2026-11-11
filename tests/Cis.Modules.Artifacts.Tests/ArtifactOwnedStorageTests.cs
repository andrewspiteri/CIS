using Cis.Abstractions;
using Cis.Modules.Artifacts;
using Cis.Modules.Repository;
using Cis.Modules.Agent;
using System.Text.Json;

namespace Cis.Modules.Artifacts.Tests;

public sealed partial class ArtifactRetentionTests
{
    [Theory]
    [InlineData("archive", "old.zip")]
    [InlineData("retrieved", "old/report-copy.txt")]
    [InlineData("restore", "old/staged-output.txt")]
    [InlineData("cleanup", "old.json")]
    public void GovernedManagementCleanupRemovesObsoleteCopiesAndKeepsExactControlsAndRequiredResults(string directory, string entry)
    {
        using var fixture = new GovernedArtifactFixture();
        fixture.Policy(directory);
        var obsolete = ".cis/local/artifacts/" + directory + "/" + entry;
        fixture.Repository.Write(obsolete, "obsolete generated copy");
        fixture.Repository.Age(obsolete, ArtifactOld);
        fixture.RetainCurrentEvidence();
        var required = fixture.RequiredBytes();
        var control = Path.GetFullPath(Path.Combine(fixture.Repository.Path, ".cis/local/artifacts/operation.lock"));
        fixture.Repository.Write(".cis/local/artifacts/operation.lock", "");
        var inventory = fixture.Storage.Inspect(fixture.Repository.Path, fixture.Association);
        Assert.Contains(control, inventory.Storage!.ProtectedPaths);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(fixture.Repository.Path, ArtifactRetentionService.StateRoot)), inventory.Storage.ProtectedPaths);
        var plan = fixture.Service.Plan(fixture.Repository.Path, "obsolete", fixture.Association);
        Assert.Empty(plan.Errors);
        Assert.True(Assert.Single(plan.Entries).Candidate);
        Assert.All(fixture.Service.Plan(fixture.Repository.Path, "current", fixture.Association).Entries,
            item => { Assert.True(item.Protected); Assert.False(item.Candidate); });

        var cleaned = fixture.Service.Clean(fixture.Repository.Path, "obsolete", true, fixture.Association);

        Assert.True(cleaned.ExitCode == 0, string.Join("; ", cleaned.Errors));
        Assert.Equal("cleaned", cleaned.Status);
        Assert.False(File.Exists(Path.Combine(fixture.Repository.Path, obsolete)));
        Assert.True(File.Exists(control));
        fixture.AssertRequiredBytes(required);
        Assert.Equal(0, cleaned.OwnedStorage!.Storage!.ReservedBytes);
        var fresh = fixture.Service.Inventory(fixture.Repository.Path, association: fixture.Association);
        Assert.Empty(fresh.Errors);
        Assert.Equal(fresh.Storage!.ManagementBytes, fresh.Storage.TotalReportedBytes - fresh.FamilyStorage
            .Where(item => item.Family == "current").Sum(item => item.LiveBytes));
        var receipt = Assert.Single(cleaned.Outputs);
        Assert.True(File.Exists(Path.Combine(fixture.Repository.Path, receipt)));
        // The current receipt is protected while written, not pinned forever.
        // A later eligible cleanup retires it and retains its own one tombstone.
        if (directory == "cleanup")
        {
            fixture.Repository.Age(receipt, ArtifactOld);
            var next = fixture.Service.Clean(fixture.Repository.Path, "obsolete", true, fixture.Association);
            Assert.Equal(0, next.ExitCode);
            Assert.False(File.Exists(Path.Combine(fixture.Repository.Path, receipt)));
            Assert.True(File.Exists(Path.Combine(fixture.Repository.Path, Assert.Single(next.Outputs))));
            Assert.Single(Directory.GetFiles(Path.Combine(fixture.Repository.Path, ".cis/local/artifacts/cleanup")));
            fixture.AssertRequiredBytes(required);
        }
    }

    [Fact]
    public void GovernedManagementCleanupKeepsAnotherPendingOperationsExactOutput()
    {
        using var fixture = new GovernedArtifactFixture(includeOtherOwner: true);
        fixture.Policy("retrieved");
        const string old = ".cis/local/artifacts/retrieved/old/report-copy.txt";
        const string pending = ".cis/local/artifacts/retrieved/pending/report-copy.txt";
        fixture.Repository.Write(old, "obsolete retrieval");
        fixture.Repository.Age(old, ArtifactOld);
        var admitted = fixture.Native.Admit(new(fixture.Repository.Path, "native/pending-copy", "unit-preparation", 60,
            fixture.Association with { TaskId = "WORK-091" }));
        Assert.True(admitted.Allowed, string.Join("; ", admitted.Errors));
        using var scope = admitted.Scope!;
        var output = Path.GetFullPath(Path.Combine(fixture.Repository.Path, ".cis/local/artifacts/retrieved/pending"));
        var reserved = fixture.Storage.Reserve(fixture.Repository.Path, scope.Reference, "pending-copy", 4096,
            [new(output)], actor: "Cleaner");
        Assert.True(reserved.Allowed, string.Join("; ", reserved.Result.Errors));
        fixture.Repository.Write(pending, "pending generated copy");
        fixture.Repository.Age(pending, ArtifactOld);

        var cleaned = fixture.Service.Clean(fixture.Repository.Path, "obsolete", true, fixture.Association);

        Assert.True(cleaned.ExitCode == 0, string.Join("; ", cleaned.Errors));
        Assert.False(File.Exists(Path.Combine(fixture.Repository.Path, old)));
        Assert.Equal("pending generated copy", File.ReadAllText(Path.Combine(fixture.Repository.Path, pending)));
        Assert.True(cleaned.OwnedStorage!.Storage!.ReservedBytes >= 4096);
        Assert.Contains(output, cleaned.OwnedStorage.Storage.ProtectedPaths);
        Assert.True(fixture.Storage.Settle(fixture.Repository.Path, scope.Reference, reserved.ReservationId!, true).Allowed);
        Assert.Equal(0, scope.Finish("native-finished", "The fixture copy operation ended.", []).ExitCode);
    }

    [Fact]
    public void GovernedManagementCleanupHoldsOnAnActiveLease()
    {
        using var fixture = new GovernedArtifactFixture();
        fixture.Policy("archive");
        const string obsolete = ".cis/local/artifacts/archive/old.zip";
        fixture.Repository.Write(obsolete, "obsolete generated archive");
        fixture.Repository.Age(obsolete, ArtifactOld);
        var control = Path.Combine(fixture.Repository.Path, ".cis/local/artifacts/operation.lock");
        using var lease = new FileStream(control, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var held = fixture.Service.Clean(fixture.Repository.Path, "obsolete", true, fixture.Association);

        Assert.Equal(4, held.ExitCode);
        Assert.False(held.Applied);
        Assert.Equal("obsolete generated archive", File.ReadAllText(Path.Combine(fixture.Repository.Path, obsolete)));
        Assert.Equal(0, held.OwnedStorage!.Storage!.ReservedBytes);
    }

    [Fact]
    public void GovernedManagementCleanupNeedsItsExistingCleanupDelegation()
    {
        using var fixture = new GovernedArtifactFixture(cleanupDelegated: false);
        fixture.Policy("retrieved");
        const string obsolete = ".cis/local/artifacts/retrieved/old/report-copy.txt";
        fixture.Repository.Write(obsolete, "obsolete generated retrieval");
        fixture.Repository.Age(obsolete, ArtifactOld);

        var held = fixture.Service.Clean(fixture.Repository.Path, "obsolete", true, fixture.Association);

        Assert.Equal(4, held.ExitCode);
        Assert.Contains(held.Errors, error => error.Contains("no delegated cleanup authority", StringComparison.Ordinal));
        Assert.Equal("obsolete generated retrieval", File.ReadAllText(Path.Combine(fixture.Repository.Path, obsolete)));
    }

    private static readonly DateTime ArtifactOld = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class GovernedArtifactFixture : IDisposable
    {
        public Fixture Repository { get; } = Fixture.Create();
        public AgentService Controller { get; }
        public ICisNativeWorkController Native => Controller;
        public ICisOwnedStorageController Storage => Controller;
        public CisNativeWorkOptions Association { get; }
        public ArtifactRetentionService Service { get; }

        public GovernedArtifactFixture(bool cleanupDelegated = true, bool includeOtherOwner = false)
        {
            Repository.Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: artifacts-fixture\ndocuments: []\n");
            Repository.Write("docs/cis/changes/CIS-0001/plan.md", "---\nstatus: Approved\nfeature_spec_frontend: false\n---\n# Bounded retention\n");
            Repository.Write("docs/cis/changes/CIS-0001/impact.md", "---\nstatus: Accepted\n---\n# Impact\n| Finding | Status |\n|---|---|\n| IMPACT-1 | accepted |\n");
            Repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md",
                "---\ntask_status: InProgress\ntargets: [\"artifacts-fixture\"]\n---\n# Generated artifact cleanup\nRetain required evidence and retire eligible copies.\n");
            if (includeOtherOwner)
                Repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-091.md",
                    "---\ntask_status: InProgress\ntargets: [\"artifacts-fixture\"]\n---\n# Pending generated copy\nPreserve its existing bounded operation and output.\n");
            Controller = new(new CisRepositoryContextResolver());
            Association = new("CIS-0001", "WORK-090", AuthorityPath: Repository.Path, Actor: "Cleaner");
            var allocation = new CisAgentWorkAllocationRequest(1200, 8, 120,
                includeOtherOwner ? [new("WORK-090", 600, 4, 60), new("WORK-091", 600, 4, 60)] : [new("WORK-090", 1200, 8, 120)],
                "fixture:cleanup-work", "fixture:finite-storage", AcknowledgeHistoricalUnknown: true)
            {
                Controllers = ["Cleaner"],
                Storage = new(32 * 1024 * 1024, [new(Repository.Path, ".cis/local")])
                { RestorationBytes = 1024 * 1024, CleanupControllers = cleanupDelegated ? ["Cleaner"] : [] }
            };
            Repository.Write(".cis/local/artifact-allocation.json", JsonSerializer.Serialize(allocation, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var allocated = Controller.AllocateWork(Repository.Path, "CIS-0001", ".cis/local/artifact-allocation.json", "Allocation owner",
                "Allocate finite retention with the explicit existing cleanup delegation.", adoptAccounting: true);
            Assert.True(allocated.ExitCode == 0, string.Join("; ", allocated.Errors));
            Service = Repository.Service(Controller, Controller);
        }

        public void Policy(string directory) => Repository.Write("docs/cis/references/local-artifact-retention.md", $"""
            | Family | Path | Retain days | Keep latest | Action |
            |---|---|---:|---:|---|
            | obsolete | .cis/local/artifacts/{directory} | 7 | 0 | delete |
            | current | .cis/local/testing/current | 7 | 0 | delete |
            """);

        public void RetainCurrentEvidence()
        {
            var admission = Native.Admit(new(Repository.Path, "native/current-result", "unit-verification", 60, Association));
            Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
            using var scope = admission.Scope!;
            var paths = new[] { "report.trx", "state.json" }.Select(file => Path.Combine(Repository.Path, ".cis/local/testing/current", file)).ToArray();
            var reservation = Storage.Reserve(Repository.Path, scope.Reference, "current-evidence", 4096,
                paths.Select(path => new CisOwnedStoragePath(path)).ToArray(), actor: "Cleaner");
            Assert.True(reservation.Allowed, string.Join("; ", reservation.Result.Errors));
            Repository.Write(".cis/local/testing/current/report.trx", "<TestRun><ResultSummary><Counters total=\"1\" passed=\"1\" failed=\"0\" /></ResultSummary></TestRun>");
            Repository.Write(".cis/local/testing/current/state.json", "{\"fixture\":\"current required report/input metadata\"}");
            foreach (var path in paths) File.SetLastWriteTimeUtc(path, ArtifactOld);
            Assert.True(Storage.Settle(Repository.Path, scope.Reference, reservation.ReservationId!, true, paths).Allowed);
            Assert.Equal(0, scope.Finish("native-finished", "Retain the fixture's required report and input metadata.", paths).ExitCode);
        }

        public Dictionary<string, byte[]> RequiredBytes() => Directory.GetFiles(Path.Combine(Repository.Path, ".cis/local/testing/current"))
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        public void AssertRequiredBytes(IReadOnlyDictionary<string, byte[]> expected)
        { foreach (var file in expected) Assert.Equal(file.Value, File.ReadAllBytes(file.Key)); }
        public void Dispose() => Repository.Dispose();
    }

    [Fact]
    public void VerifiedTransientTransferCleanupKeepsRequiredResultsAndDoesNotBecomeExecutionInput()
    {
        using var repository = Fixture.Create();
        repository.Write("src/ValueStore.cs", "public sealed class ValueStore { public int Value { get; set; } }");
        repository.Write("fixtures/release-input.txt", "An explicitly retained fixture input.");
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        var inputs = CisExecutionIdentity.CaptureInputs(context);
        Assert.Contains(inputs, input => input.Path == "fixtures/release-input.txt");
        repository.Write(".cis/local/cache/verified-transfer/transfer.zip", new string('b', 128_000));
        repository.Write(".cis/local/cache/verified-transfer/staging/ValueStore.cs", "temporary unchanged source copy");
        repository.Write(".cis/local/cache/current/native.trx", "required native result");
        repository.Write("user-output/keep.txt", "Unrelated user-managed data.");
        var userInputs = CisExecutionIdentity.CaptureInputs(context);
        Assert.DoesNotContain(userInputs, input => input.Path.StartsWith(".cis/local/", StringComparison.Ordinal));
        Assert.All(inputs, input => Assert.Contains(input, userInputs));
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var path in new[] { ".cis/local/cache/verified-transfer/transfer.zip",
            ".cis/local/cache/verified-transfer/staging/ValueStore.cs", ".cis/local/cache/current/native.trx" })
            repository.Age(path, old);
        var authority = new ArtifactAuthority(repository.Path)
        { Protected = [Path.Combine(repository.Path, ".cis/local/cache/current")] };
        var service = repository.Service(authority, authority);
        var plan = service.Plan(repository.Path, "cache");
        Assert.Empty(plan.Errors);
        Assert.True(Assert.Single(plan.Entries, item => item.Path.EndsWith("/verified-transfer", StringComparison.Ordinal)).Candidate);
        Assert.False(Assert.Single(plan.Entries, item => item.Path.EndsWith("/current", StringComparison.Ordinal)).Candidate);

        var cleaned = service.Clean(repository.Path, "cache", true);

        Assert.Equal(0, cleaned.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis/local/cache/verified-transfer")));
        Assert.Equal("required native result", File.ReadAllText(Path.Combine(repository.Path, ".cis/local/cache/current/native.trx")));
        Assert.Equal("Unrelated user-managed data.", File.ReadAllText(Path.Combine(repository.Path, "user-output/keep.txt")));
        Assert.Equal(userInputs.Select(input => (input.Path, input.Digest)),
            CisExecutionIdentity.CaptureInputs(context).Select(input => (input.Path, input.Digest)));
        Assert.Equal(1, authority.Settlements);
        Assert.Equal(1, authority.Finishes);
    }

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
