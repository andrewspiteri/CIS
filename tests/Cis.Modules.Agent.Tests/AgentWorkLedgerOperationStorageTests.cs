using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_ExactRetainedScratchMutationPreservesItsAdmissionAndSupersessionGuards()
    {
        using var f = new Fixture();
        var first = f.Identity("WORK-A");
        var second = f.Identity("WORK-B");
        var allocation = new CisAgentWorkAllocationRequest(600, 6, 0,
            [new(first.TaskId, 240, 3), new(second.TaskId, 240, 3)], "approved scratch retirement", "finite shared storage",
            AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(16 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Allocation owner",
            "Retire only known stopped scratch without granting cross-owner supersession.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture", "unit", f.Context.RepositoryId) { NativeOperation = "native-unit" };
        var original = f.Ledger.Admit(f.Context, first, "native/original-scratch", 60, 180, stage: stage, controller: "Controller");
        var foreign = f.Ledger.Admit(f.Context, second, "native/foreign-owner", 60, 180, stage: stage, controller: "Controller");
        Assert.True(original.Admitted); Assert.True(foreign.Admitted);
        var scratch = Path.Combine(Path.GetTempPath(), "cis-mutation-" + Guid.NewGuid().ToString("N"));
        var reservation = f.Ledger.ReserveStorage(f.Context, original.Reference!, "original-scratch", 4096,
            [new(scratch, Scratch: true)], false, false, "Controller");
        Assert.True(reservation.Allowed, string.Join("; ", reservation.Result.Errors));
        Directory.CreateDirectory(scratch);
        var payload = Path.Combine(scratch, "diagnostic.txt");
        File.WriteAllText(payload, "retained native scratch");
        try
        {
            var originalPendingLedger = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
            var live = f.Ledger.ReserveStorage(f.Context, foreign.Reference!, "live-scratch-mutation", 0,
                [new(scratch, Scratch: true) { MutatesExisting = true }], false, false, "Controller");
            Assert.False(live.Allowed);
            Assert.Contains(live.Result.Errors, error => error.Contains("overlapping writable storage domain", StringComparison.Ordinal));
            Assert.Equal(originalPendingLedger, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
            Assert.Equal("retained native scratch", File.ReadAllText(payload));
            Assert.True(f.Ledger.SettleStorage(f.Context, original.Reference!, reservation.ReservationId!, true, [scratch]).Allowed);
            f.Time = f.Time.AddSeconds(1);
            Assert.Equal(0, f.Ledger.Settle(f.Context, original.Reference!, "native-finished", true).ExitCode);
            var wrongOwner = f.Ledger.ReserveStorage(f.Context, foreign.Reference!, "foreign-scratch-mutation", 0,
                [new(scratch, Scratch: true) { MutatesExisting = true }], false, false, "Controller");
            Assert.False(wrongOwner.Allowed);
            Assert.Contains(wrongOwner.Result.Errors, error => error.Contains("Required evidence", StringComparison.Ordinal));
            Assert.Equal("retained native scratch", File.ReadAllText(payload));
            var current = f.Ledger.Admit(f.Context, first, "native/current-owner", 60, 180, stage: stage, controller: "Controller");
            Assert.True(current.Admitted);
            var unknown = Path.Combine(Path.GetTempPath(), "cis-mutation-" + Guid.NewGuid().ToString("N"));
            Assert.False(f.Ledger.ReserveStorage(f.Context, current.Reference!, "unknown-scratch-mutation", 0,
                [new(unknown, Scratch: true) { MutatesExisting = true }], false, false, "Controller").Allowed);
            var before = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
            var mutation = f.Ledger.ReserveStorage(f.Context, current.Reference!, "known-scratch-mutation", 0,
                [new(scratch, Scratch: true) { MutatesExisting = true }], false, false, "Controller");
            Assert.True(mutation.Allowed, string.Join("; ", mutation.Result.Errors));
            Directory.Delete(scratch, recursive: true);
            Assert.True(f.Ledger.SettleStorage(f.Context, current.Reference!, mutation.ReservationId!, true, null,
                supersededRequiredPaths: [scratch]).Allowed);
            Assert.Equal(0, f.Ledger.Settle(f.Context, current.Reference!, "native-finished", true).ExitCode);
            var after = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
            Assert.Equal(before.Task!.ConsumedMilliseconds, after.Task!.ConsumedMilliseconds);
            Assert.Equal(before.Task.UsedAttempts, after.Task.UsedAttempts);
            Assert.Equal(0, after.Task.ReservedMilliseconds);
            Assert.DoesNotContain(scratch, f.Ledger.InspectStorage(f.Context, first.ChangeId, first.TaskId).Storage!.ProtectedPaths);
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public void WorkLedger_RequiredPromotionWaitsForAnAlreadyAuthorizedForeignMutationThenSettlesWithoutNewCapacity()
    {
        using var f = new Fixture();
        var first = f.Identity("WORK-A");
        var second = f.Identity("WORK-B");
        var allocation = new CisAgentWorkAllocationRequest(600, 6, 0,
            [new(first.TaskId, 240, 3), new(second.TaskId, 240, 3)], "approved mutation/promotion interleave", "finite shared storage",
            AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(16 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Controller",
            "Do not award required protection behind an already admitted destructive lease.").ExitCode);
        var a = f.Ledger.Admit(f.Context, first, "mutation-owner", 60, 180, controller: "Controller");
        var b = f.Ledger.Admit(f.Context, second, "promotion-owner", 60, 180, controller: "Controller");
        Assert.True(a.Admitted); Assert.True(b.Admitted);
        var path = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/evidence/original.json"));
        var separate = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/disjoint-output"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(path, original);
        var mutator = f.Ledger.ReserveStorage(f.Context, a.Reference!, "mutator", 4096,
            [new(path) { MutatesExisting = true }], false, false, "Controller");
        var publisher = f.Ledger.ReserveStorage(f.Context, b.Reference!, "publisher", 8192,
            [new(separate)], false, false, "Controller");
        Assert.True(mutator.Allowed, string.Join("; ", mutator.Result.Errors));
        Assert.True(publisher.Allowed, string.Join("; ", publisher.Result.Errors));
        var reserved = f.Ledger.InspectStorage(f.Context, first.ChangeId, first.TaskId).Storage!.ReservedBytes;

        var held = f.Ledger.SettleStorage(f.Context, b.Reference!, publisher.ReservationId!, true, [path]);

        Assert.False(held.Allowed);
        var waiting = f.Ledger.InspectStorage(f.Context, first.ChangeId, first.TaskId).Storage!;
        Assert.Equal(reserved, waiting.ReservedBytes);
        // The pending writable lease protects the path, but B has not acquired a
        // durable Required claim that could contradict A's earlier decision.
        Assert.False(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(f.Context.RepositoryPath,
            CisAgentWorkPaths.Ledger)))!["changes"]![first.ChangeId]!["storage"]!["claims"]![path]!["required"]!.GetValue<bool>());
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(f.Ledger.SettleStorage(f.Context, a.Reference!, mutator.ReservationId!, true, null).Allowed);
        Assert.Equal(0, f.Ledger.Settle(f.Context, a.Reference!, "Succeeded", true).ExitCode);
        var final = f.Ledger.SettleStorage(f.Context, b.Reference!, publisher.ReservationId!, true, [path]);
        Assert.True(final.Allowed, string.Join("; ", final.Errors));
        Assert.Contains(path, final.Storage!.ProtectedPaths);
        Assert.Equal(0, final.Storage.ReservedBytes);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(0, f.Ledger.Settle(f.Context, b.Reference!, "Succeeded", true).ExitCode);
        Assert.Equal(1, f.Ledger.Read(f.Context, second.ChangeId, second.TaskId).Work!.Task!.UsedAttempts);
    }

    [Fact]
    public void WorkLedger_OperationGrowthRemainsIndependentOfDisjointOwnedWritesAndKeepsItsOriginalBaseline()
    {
        using var f = new Fixture();
        var first = f.Identity("WORK-A");
        var second = f.Identity("WORK-B");
        const long ceiling = 16 * 1024 * 1024;
        var allocation = new CisAgentWorkAllocationRequest(600, 6, 0,
            [new(first.TaskId, 240, 3), new(second.TaskId, 240, 3)], "approved bounded domain test", "one finite shared ceiling",
            AcknowledgeHistoricalUnknown: true)
        {
            Controllers = ["Controller"],
            Storage = new(ceiling,
                [new(f.Context.RepositoryPath, ".cis/local/domain-a"), new(f.Context.RepositoryPath, ".cis/local/domain-b"),
                    new(f.Context.RepositoryPath, ".cis/local/domain-extra")]),
        };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Controller",
            "Two disjoint owned domains spend separate grants under one existing ceiling.").ExitCode);
        var a = f.Ledger.Admit(f.Context, first, "domain-a", 60, 180, controller: "Controller");
        var b = f.Ledger.Admit(f.Context, second, "domain-b", 60, 180, controller: "Controller");
        Assert.True(a.Admitted); Assert.True(b.Admitted);
        string Domain(string name) => Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/" + name));
        var aRoot = Domain("domain-a");
        var bRoot = Domain("domain-b");
        var extra = Domain("domain-extra");
        var aGrant = f.Ledger.ReserveStorage(f.Context, a.Reference!, "native", 4096, [new(aRoot)], false, false, "Controller");
        var bGrant = f.Ledger.ReserveStorage(f.Context, b.Reference!, "native", 8192, [new(bRoot)], false, false, "Controller");
        Assert.True(aGrant.Allowed, string.Join("; ", aGrant.Result.Errors));
        Assert.True(bGrant.Allowed, string.Join("; ", bGrant.Result.Errors));
        var before = aGrant.Result.Storage!.OwnedBytes;
        Directory.CreateDirectory(bRoot);
        var bFile = Path.Combine(bRoot, "output.bin");
        File.WriteAllBytes(bFile, new byte[6144]);

        var refreshed = f.Ledger.ReserveStorage(f.Context, a.Reference!, "native", 4096, null, false, false, "Controller");

        Assert.True(refreshed.Allowed, string.Join("; ", refreshed.Result.Errors));
        Assert.Equal(aGrant.ReservationId, refreshed.ReservationId);
        Assert.Equal(4096, refreshed.Operation!.MaximumGrowthBytes);
        Assert.Equal(0, refreshed.Operation.InitialBytes);
        Assert.Equal(0, refreshed.Operation.CurrentBytes);
        Assert.True(refreshed.Result.Storage!.OwnedBytes >= before + 6144);
        Assert.False(f.Ledger.ReserveStorage(f.Context, a.Reference!, "overlap", 1, [new(bRoot)], false, false, "Controller").Allowed);
        Directory.CreateDirectory(aRoot);
        var aFile = Path.Combine(aRoot, "output.bin");
        File.WriteAllBytes(aFile, new byte[512]);
        Directory.CreateDirectory(extra);
        File.WriteAllBytes(Path.Combine(extra, "preexisting.bin"), new byte[256]);
        var extended = f.Ledger.ReserveStorage(f.Context, a.Reference!, "native", 4096, [new(extra)], false, false, "Controller");
        Assert.True(extended.Allowed, string.Join("; ", extended.Result.Errors));
        Assert.Equal(aGrant.ReservationId, extended.ReservationId);
        Assert.Equal(256, extended.Operation!.InitialBytes);
        Assert.Equal(768, extended.Operation.CurrentBytes);
        Assert.Equal(512, extended.Operation.CurrentBytes - extended.Operation.InitialBytes);
        Assert.Equal(4096, extended.Operation.MaximumGrowthBytes);
        File.WriteAllBytes(aFile, new byte[4097]);
        var overgrown = f.Ledger.ReserveStorage(f.Context, a.Reference!, "native", 4096, null, false, false, "Controller");
        Assert.True(overgrown.Operation!.CurrentBytes > overgrown.Operation.InitialBytes + overgrown.Operation.MaximumGrowthBytes);
        using (var file = File.OpenWrite(bFile)) file.SetLength(ceiling);
        var exhausted = f.Ledger.InspectStorage(f.Context, first.ChangeId, first.TaskId).Storage!;
        Assert.True(exhausted.OwnedBytes > ceiling);
        Assert.Equal(0, exhausted.RemainingBytes);
        Assert.False(f.Ledger.ReserveStorage(f.Context, a.Reference!, "additional", 1, null, false, false, "Controller").Allowed);
        Assert.True(f.Ledger.SettleStorage(f.Context, a.Reference!, aGrant.ReservationId!, true, null).Allowed);
        Assert.True(f.Ledger.SettleStorage(f.Context, b.Reference!, bGrant.ReservationId!, true, null).Allowed);
        Assert.Equal(0, f.Ledger.Settle(f.Context, a.Reference!, "Succeeded", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, b.Reference!, "Succeeded", true).ExitCode);
    }
}
