using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_OwnedStorageHistoryCapacityHoldsNewExpansionButKeepsSettlementAndDeletionAvailable()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(600, 5, 0, [new(identity.TaskId, 600, 5)],
            "approved native allocation", "finite native history", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(32 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Keep evidence proof inside a readable ledger.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture", "unit", f.Context.RepositoryId) { NativeOperation = "unit" };
        var prior = f.Ledger.Admit(f.Context, identity, "native/history", 60, 180, stage: stage, controller: "Controller");
        Assert.Equal(0, f.Ledger.Settle(f.Context, prior.Reference!, "native-finished", true).ExitCode);
        var active = f.Ledger.Admit(f.Context, identity, "native/already-active", 60, 180, stage: stage, controller: "Controller");
        Assert.True(active.Admitted);
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var state = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var owner = state["changes"]![identity.ChangeId]!["owners"]![identity.TaskId]!;
        var checkpoint = owner["checkpoints"]![0]!.DeepClone();
        checkpoint["checkpoint"]!["evidence"] = new JsonArray(Enumerable.Range(0, 40).Select(_ => (JsonNode?)JsonValue.Create(new string('e', 4000))).ToArray());
        owner["checkpoints"] = new JsonArray(Enumerable.Range(0, 40).Select(_ => checkpoint.DeepClone()).ToArray());
        File.WriteAllText(ledgerPath, state.ToJsonString());
        Assert.InRange(new FileInfo(ledgerPath).Length, 6 * 1024 * 1024, 8 * 1024 * 1024 - 65536);
        var held = f.Ledger.Admit(f.Context, identity, "native/no-new-expansion", 30, 180, stage: stage, controller: "Controller");
        Assert.False(held.Admitted); Assert.Contains("finite ledger capacity", held.Result.Work!.HoldReason!, StringComparison.Ordinal);
        var deletion = f.Ledger.Admit(f.Context, identity, "artifacts/only-deletion", 30, 180,
            active.Reference!.ExecutionId, stage: stage with { NativeOperation = "artifacts-clean" }, controller: "Controller");
        Assert.True(deletion.Admitted, string.Join("; ", deletion.Result.Errors));
        Assert.Equal(0, f.Ledger.Settle(f.Context, active.Reference!, "native-finished", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, deletion.Reference!, "native-finished", true).ExitCode);
        var final = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId);
        Assert.Equal(0, final.ExitCode); Assert.Equal(0, final.Work!.Task!.ReservedMilliseconds);
        Assert.NotNull(f.Ledger.NativeExecution(f.Context, prior.Reference!.ExecutionId));
    }

    [Fact]
    public void WorkLedger_OwnedStorageCompactsClosedReceiptsWithoutLosingProofOrCommittingUnreadableCounts()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(600, 5, 0, [new(identity.TaskId, 600, 5)],
            "approved native allocation", "bounded accounting history", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(64 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Retain exact native evidence.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture", "unit", f.Context.RepositoryId) { NativeOperation = "unit" };
        var prior = f.Ledger.Admit(f.Context, identity, "native/prior", 60, 180, stage: stage, controller: "Controller");
        var child = f.Ledger.Admit(f.Context, identity, "native/prior-child", 30, 180, prior.Reference!.ExecutionId,
            stage: stage, controller: "Controller");
        Assert.True(child.Admitted);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, child.Reference!).ExitCode);
        Assert.Equal(0, f.Ledger.AttachProcess(f.Context, child.Reference!, 12345, f.Time.ToString("O")).ExitCode);
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, f.Ledger.Settle(f.Context, child.Reference!, "native-finished", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, prior.Reference, "native-finished", true).ExitCode);
        var current = f.Ledger.Admit(f.Context, identity, "native/current", 60, 180, stage: stage, controller: "Controller");
        Assert.True(current.Admitted);
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var state = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var storage = state["changes"]![identity.ChangeId]!["storage"]!;
        var receipts = new JsonObject();
        for (var index = 0; index < 10000; index++) receipts.Add("receipt-" + index, JsonSerializer.SerializeToNode(new
        {
            reference = current.Reference,
            operationId = "closed-" + index,
            maximumGrowthBytes = 0,
            recordHeadroomBytes = 0,
            paths = Array.Empty<string>(),
            cleanup = false,
            finalized = false
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        storage["reservations"] = receipts;
        File.WriteAllText(ledgerPath, state.ToJsonString());
        var beforeRejectedMutation = File.ReadAllBytes(ledgerPath);
        var rejected = f.Ledger.ReserveStorage(f.Context, current.Reference!, "new-stage", 1, [], false, false, "Controller");
        Assert.False(rejected.Allowed);
        Assert.Contains(rejected.Result.Errors, error => error.Contains("record inventory is full", StringComparison.Ordinal));
        Assert.Equal(beforeRejectedMutation, File.ReadAllBytes(ledgerPath));
        Assert.Equal(0, f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).ExitCode);
        foreach (var receipt in receipts.Select(pair => pair.Value!))
        {
            receipt["reference"] = JsonSerializer.SerializeToNode(prior.Reference, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            receipt["finalized"] = true;
        }
        File.WriteAllText(ledgerPath, state.ToJsonString());
        var missing = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/removed-scratch"));
        var required = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/current-summary.json"));
        var reserved = f.Ledger.ReserveStorage(f.Context, current.Reference!, "new-stage", 1024,
            [new(missing), new(required, Required: true)], false, false, "Controller");
        Assert.True(reserved.Allowed, string.Join("; ", reserved.Result.Errors));
        var compacted = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        Assert.Single(compacted["changes"]![identity.ChangeId]!["storage"]!["reservations"]!.AsObject());
        Assert.True(f.Ledger.SettleStorage(f.Context, current.Reference!, reserved.ReservationId!, true, [required]).Allowed);
        Assert.True(f.Ledger.SettleStorage(f.Context, current.Reference!, reserved.ReservationId!, true, [required]).Allowed);
        var claims = JsonNode.Parse(File.ReadAllText(ledgerPath))!["changes"]![identity.ChangeId]!["storage"]!["claims"]!.AsObject();
        Assert.False(claims.ContainsKey(missing)); Assert.True(claims[required]!["required"]!.GetValue<bool>());
        Assert.False(f.Ledger.ReserveStorage(f.Context, current.Reference!, "new-stage", 1024, [], false, false, "Controller").Allowed);
        var proof = f.Ledger.NativeExecution(f.Context, child.Reference!.ExecutionId)!.Value;
        Assert.Equal(child.Reference, proof.Reference); Assert.True(proof.Execution.Finalized);
        Assert.True(proof.Execution.LaunchIntent); Assert.Equal(12345, proof.Execution.ProcessId);
        Assert.Equal(0, f.Ledger.Settle(f.Context, current.Reference!, "native-finished", true).ExitCode);
        Assert.True(f.Ledger.SettleStorage(f.Context, current.Reference!, reserved.ReservationId!, true, [required]).Allowed);
        var final = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(1_000, final.Task!.ConsumedMilliseconds); Assert.Equal(1, final.Task.UsedAttempts);
        Assert.Equal(3, final.Executions.Count); Assert.Equal(0, f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.ReservedBytes);
    }

    [Fact]
    public void WorkLedger_OwnedStorageCleanupDelegationProtectsRestorationExceptBoundedDeletionRecords()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        const long maximum = 8 * 1024 * 1024; const long restoration = maximum / 2;
        var allocation = new CisAgentWorkAllocationRequest(600, 5, 0, [new(identity.TaskId, 600, 5)],
            "approved native allocation", "protected restoration storage", AcknowledgeHistoricalUnknown: true)
        {
            Controllers = ["Cleaner"],
            Storage = new(maximum, [new(f.Context.RepositoryPath, ".cis/local")])
            { RestorationBytes = restoration, CleanupControllers = ["Cleaner"] }
        };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Keep native recovery capacity.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture", "artifacts", f.Context.RepositoryId)
        { NativeOperation = "artifacts-restore", RepositoryPath = f.Context.RepositoryPath };
        var root = f.Ledger.Admit(f.Context, identity, "artifacts/owner", 180, 180, controller: "Cleaner");
        Assert.True(root.Admitted);
        var archive = f.Ledger.Admit(f.Context, identity, "artifacts/restore", 60, 180, root.Reference!.ExecutionId,
            stage: stage, controller: "Cleaner");
        var deletion = f.Ledger.Admit(f.Context, identity, "artifacts/clean", 60, 180,
            root.Reference.ExecutionId, stage: stage with { NativeOperation = "artifacts-clean" }, controller: "Cleaner");
        var broad = f.Ledger.Admit(f.Context, identity, "artifacts/broad-clean-registration", 60, 180,
            root.Reference.ExecutionId, stage: stage with { NativeOperation = "artifacts-clean" }, controller: "Cleaner");
        Assert.True(archive.Admitted); Assert.True(deletion.Admitted); Assert.True(broad.Admitted);
        var payload = Path.Combine(f.Context.RepositoryPath, ".cis/local/native/eligible.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        var broadReservation = f.Ledger.ReserveStorage(f.Context, broad.Reference!, "clean", 512 * 1024,
            [new(Path.GetDirectoryName(payload)!)], true, false, "Cleaner");
        Assert.True(broadReservation.Allowed, string.Join("; ", broadReservation.Result.Errors));
        var remaining = f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.RemainingBytes;
        using (var file = File.Create(payload)) file.SetLength(remaining);
        Assert.Equal(0, f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.RemainingBytes);
        var management = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/artifacts"));
        foreach (var operation in new[] { "compact", "restore", "retrieve", "clean" })
            Assert.False(f.Ledger.ReserveStorage(f.Context, archive.Reference!, operation, 1, [new(management)], true, false, "Cleaner").Allowed);
        Assert.False(f.Ledger.ReserveStorage(f.Context, broad.Reference!, "clean", 1,
            [new(management)], true, false, "Cleaner").Allowed);
        Assert.False(f.Ledger.ReserveStorage(f.Context, deletion.Reference!, "clean", 1,
            [new(Path.GetDirectoryName(payload)!)], true, false, "Cleaner").Allowed);
        var clean = f.Ledger.ReserveStorage(f.Context, deletion.Reference!, "clean", 64 * 1024,
            [new(management)], true, false, "Cleaner");
        Assert.True(clean.Allowed, string.Join("; ", clean.Result.Errors));
        File.Delete(payload);
        Directory.CreateDirectory(management); File.WriteAllText(Path.Combine(management, "cleanup-receipt.json"), "verified eligible deletion");
        Assert.True(f.Ledger.SettleStorage(f.Context, deletion.Reference!, clean.ReservationId!, true, null).Allowed);
        Assert.True(f.Ledger.SettleStorage(f.Context, broad.Reference!, broadReservation.ReservationId!, true, null).Allowed);
        Assert.Equal(0, f.Ledger.Settle(f.Context, deletion.Reference!, "native-finished", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, broad.Reference!, "native-finished", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, archive.Reference!, "native-finished", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, root.Reference, "Succeeded", true).ExitCode);
        var after = f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!;
        Assert.Equal(restoration, after.RestorationBytes); Assert.True(after.RemainingBytes > 0);
        Assert.Equal(1, f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!.Task!.UsedAttempts);
    }

    [Fact]
    public void WorkLedger_OwnedStorageAnotherWorkersCurrentClaimRetainsOriginalProtectionAndSettlesGrowth()
    {
        using var f = new Fixture(); var first = f.Identity("WORK-A"); var second = f.Identity("WORK-B");
        var allocation = new CisAgentWorkAllocationRequest(600, 6, 0, [new(first.TaskId, 240, 3), new(second.TaskId, 240, 3)],
            "approved native allocation", "shared stable evidence storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Worker"], Storage = new(8 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Controller", "Keep original protection without transferring cleanup authority.").ExitCode);
        var original = f.Ledger.Admit(f.Context, first, "evidence/first-owner", 60, 180, controller: "Controller");
        Assert.True(original.Admitted);
        var stable = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/latest-result.json"));
        var originalStorage = f.Ledger.ReserveStorage(f.Context, original.Reference!, "native", 1024,
            [new(stable, Required: true)], false, false, "Controller");
        Assert.True(originalStorage.Allowed, string.Join("; ", originalStorage.Result.Errors));
        Directory.CreateDirectory(Path.GetDirectoryName(stable)!); File.WriteAllText(stable, "stable native evidence");
        Assert.True(f.Ledger.SettleStorage(f.Context, original.Reference!, originalStorage.ReservationId!, true, [stable]).Allowed);
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, f.Ledger.Settle(f.Context, original.Reference!, "Succeeded", true).ExitCode);
        var worker = f.Ledger.Admit(f.Context, second, "evidence/second-owner", 60, 180, controller: "Worker");
        Assert.True(worker.Admitted);
        var before = f.Ledger.Read(f.Context, second.ChangeId, second.TaskId).Work!;
        var reserved = f.Ledger.ReserveStorage(f.Context, worker.Reference!, "native", 8192, [new(stable)], false, false, "Worker");
        Assert.True(reserved.Allowed, string.Join("; ", reserved.Result.Errors));
        Assert.Equal("reserved-protection-retained", reserved.Result.Status);
        var summary = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/second-summary.json"));
        var registered = f.Ledger.ReserveStorage(f.Context, worker.Reference!, "native", 8192,
            [new(stable, Required: true), new(summary)], false, false, "Worker");
        Assert.True(registered.Allowed, string.Join("; ", registered.Result.Errors));
        Assert.Equal(reserved.ReservationId, registered.ReservationId); Assert.Equal(reserved.MaximumGrowthBytes, registered.MaximumGrowthBytes);
        Assert.False(f.Ledger.ReserveStorage(f.Context, worker.Reference!, "unowned", 1,
            [new(Path.Combine(f.Context.RepositoryPath, "user-source.txt"))], false, false, "Worker").Allowed);
        Assert.False(f.Ledger.SettleStorage(f.Context, worker.Reference!, reserved.ReservationId!, false, [stable, summary]).Allowed);
        File.WriteAllText(summary, "second owner's current summary");
        var settled = f.Ledger.SettleStorage(f.Context, worker.Reference!, reserved.ReservationId!, true, [stable, summary]);
        Assert.True(settled.Allowed, string.Join("; ", settled.Errors));
        Assert.Equal("settled-protection-retained", settled.Status); Assert.Equal(0, settled.Storage!.ReservedBytes);
        Assert.Contains(stable, settled.Storage.ProtectedPaths); Assert.Contains(summary, settled.Storage.ProtectedPaths);
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var claim = JsonNode.Parse(File.ReadAllText(ledgerPath))!["changes"]![first.ChangeId]!["storage"]!["claims"]![stable]!;
        Assert.Equal(first.TaskId, claim["ownerTaskId"]!.GetValue<string>());
        Assert.Equal(original.Reference!.ExecutionId, claim["requiredByExecutionId"]!.GetValue<string>());
        Assert.True(claim["required"]!.GetValue<bool>()); Assert.Null(claim["supersededByExecutionId"]);
        f.Time = f.Time.AddSeconds(2);
        var completed = f.Ledger.Settle(f.Context, worker.Reference!, "Succeeded", true);
        Assert.Equal(0, completed.ExitCode); Assert.Equal(2_000, completed.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task!.UsedAttempts, completed.Work.Task.UsedAttempts);
        Assert.Equal(before.Task.AllocatedMilliseconds, completed.Work.Task.AllocatedMilliseconds);
        Assert.Equal(before.Change.UsedAttempts, completed.Work.Change.UsedAttempts);
        var after = File.ReadAllBytes(ledgerPath);
        Assert.True(f.Ledger.SettleStorage(f.Context, worker.Reference!, reserved.ReservationId!, true, [stable, summary]).Allowed);
        Assert.Equal(after, File.ReadAllBytes(ledgerPath));
        Assert.Equal(0, f.Ledger.InspectStorage(f.Context, second.ChangeId, second.TaskId).Storage!.ReservedBytes);
    }

    [Fact]
    public void WorkLedger_OwnedStorageSupersessionRetainsRecoveryAndReleasesAfterItsOriginalScopeCloses()
    {
        using var f = new Fixture(); var first = f.Identity("WORK-A"); var second = f.Identity("WORK-B"); var third = f.Identity("WORK-C");
        var allocation = new CisAgentWorkAllocationRequest(600, 6, 0,
            [new(first.TaskId, 180, 2), new(second.TaskId, 180, 2), new(third.TaskId, 180, 2)],
            "approved native allocation", "bounded protected evidence", AcknowledgeHistoricalUnknown: true)
        {
            Controllers = ["Worker", "Cleaner"],
            Storage = new(8 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) { CleanupControllers = ["Cleaner"] }
        };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second, third], "Controller", "Retain required evidence until safe supersession.").ExitCode);
        var stage = new CisAgentWorkStage("native-testing", "fixture", "mutation", f.Context.RepositoryId)
        { NativeOperation = "mutation", NativeRecoveryBasis = "fixture:original-recovery" };
        var original = f.Ledger.Admit(f.Context, first, "native/original-evidence", 60, 180, stage: stage, controller: "Controller");
        var old = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/old-summary.json"));
        var retained = f.Ledger.ReserveStorage(f.Context, original.Reference!, "native", 1024, [new(old)], false, false, "Controller");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.WriteAllText(old, "original diagnostic");
        Assert.True(f.Ledger.SettleStorage(f.Context, original.Reference!, retained.ReservationId!, true, [old]).Allowed);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, original.Reference!).ExitCode);
        var worker = f.Ledger.Admit(f.Context, second, "metadata/worker", 30, 180, controller: "Worker");
        var workerPath = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/worker.json"));
        var workerStorage = f.Ledger.ReserveStorage(f.Context, worker.Reference!, "metadata", 1024, [new(workerPath)], false, false, "Worker");
        var refusedDemotion = f.Ledger.SettleStorage(f.Context, worker.Reference!, workerStorage.ReservationId!, true, [workerPath], [old]);
        Assert.True(refusedDemotion.Allowed); Assert.Equal("settled-protection-retained", refusedDemotion.Status);
        Assert.Equal(0, refusedDemotion.Storage!.ReservedBytes); Assert.Contains(old, refusedDemotion.Storage.ProtectedPaths);
        Assert.Equal(0, f.Ledger.Settle(f.Context, worker.Reference!, "Succeeded", true).ExitCode);
        var replacement = f.Ledger.Admit(f.Context, third, "metadata/cleanup-authority", 30, 180, controller: "Cleaner");
        var current = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/native/current-summary.json"));
        var next = f.Ledger.ReserveStorage(f.Context, replacement.Reference!, "metadata", 1024, [new(current)], false, false, "Cleaner");
        var deferred = f.Ledger.SettleStorage(f.Context, replacement.Reference!, next.ReservationId!, true, [current], [old]);
        Assert.True(deferred.Allowed); Assert.Equal("settled-protection-retained", deferred.Status);
        Assert.Contains("original cleanup or recovery authority", deferred.Storage!.Coverage, StringComparison.Ordinal);
        Assert.Contains(old, deferred.Storage.ProtectedPaths); Assert.Contains(current, deferred.Storage.ProtectedPaths);
        Assert.Equal(0, deferred.Storage.ReservedBytes);
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, f.Ledger.Settle(f.Context, original.Reference!, "native-finished", true).ExitCode);
        var released = f.Ledger.InspectStorage(f.Context, third.ChangeId, third.TaskId).Storage!;
        Assert.DoesNotContain(old, released.ProtectedPaths); Assert.Contains(current, released.ProtectedPaths);
        Assert.True(File.Exists(old)); Assert.NotNull(f.Ledger.NativeExecution(f.Context, original.Reference!.ExecutionId));
        Assert.Equal(0, f.Ledger.Settle(f.Context, replacement.Reference!, "Succeeded", true).ExitCode);
    }

    [Fact]
    public async Task WorkLedger_OwnedStorageSettlementSurvivesAnUnrelatedRootsConcurrentRevision()
    {
        using var f = new Fixture(); var first = f.Identity("WORK-A"); var second = f.Identity("WORK-B");
        var allocation = new CisAgentWorkAllocationRequest(300, 4, 0, [new(first.TaskId, 120, 2), new(second.TaskId, 120, 2)],
            "approved native allocation", "independent bounded storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(8 * 1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Controller", "Keep stopped settlement independent of unrelated preparation.").ExitCode);
        var left = f.Ledger.Admit(f.Context, first, "storage/left", 60, 180);
        var right = f.Ledger.Admit(f.Context, second, "storage/right", 60, 180);
        var reservations = Enumerable.Range(0, 12).Select(index => f.Ledger.ReserveStorage(f.Context, left.Reference!,
            "metadata/" + index, 1024, [], false, false, "Controller")).ToArray();
        Assert.All(reservations, reservation => Assert.True(reservation.Allowed, string.Join("; ", reservation.Result.Errors)));
        using var barrier = new Barrier(2);
        var token = TestContext.Current.CancellationToken;
        var settlements = new List<CisOwnedStorageResult>(); var revisions = new List<CisAgentWorkResult>();
        await Task.WhenAll(Task.Run(() =>
        {
            foreach (var reservation in reservations)
            {
                barrier.SignalAndWait(token);
                settlements.Add(f.Ledger.SettleStorage(f.Context, left.Reference!, reservation.ReservationId!, true, null));
                barrier.SignalAndWait(token);
            }
        }, token), Task.Run(async () =>
        {
            foreach (var reservation in reservations)
            {
                barrier.SignalAndWait(token);
                await Task.Delay(1, token);
                revisions.Add(f.Ledger.MarkLaunchIntent(f.Context, right.Reference!));
                barrier.SignalAndWait(token);
            }
        }, token));
        Assert.All(revisions, revision => Assert.Equal(0, revision.ExitCode));
        Assert.All(settlements, settled => Assert.True(settled.Allowed, string.Join("; ", settled.Errors)));
        Assert.Equal(0, f.Ledger.InspectStorage(f.Context, first.ChangeId, first.TaskId).Storage!.ReservedBytes);
        Assert.Equal(0, f.Ledger.Settle(f.Context, left.Reference!, "Succeeded", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, right.Reference!, "Succeeded", true).ExitCode);
    }

    [Fact]
    public void WorkLedger_OwnedStorageSharesCapacityCountsCopiesAndSettlesWithoutRefillingWork()
    {
        using var f = new Fixture();
        var first = f.Identity("WORK-A"); var second = f.Identity("WORK-B");
        var policy = new CisOwnedStoragePolicy(4 * 1024 * 1024,
            [new(f.Context.RepositoryPath, ".cis/local/native"), new(f.Context.RepositoryPath, ".cis/local/native/nested"),
                new(f.Context.RepositoryPath, ".cis/local/artifacts")])
        { RestorationBytes = 128 * 1024 };
        var allocation = new CisAgentWorkAllocationRequest(1000, 10, 0, [new(first.TaskId, 450, 5), new(second.TaskId, 450, 5)],
            "approved native allocation", "approved finite storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = policy };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Controller", "Bound native verification.").ExitCode);
        var live = Path.Combine(f.Context.RepositoryPath, ".cis/local/native/nested/report.json");
        var archive = Path.Combine(f.Context.RepositoryPath, ".cis/local/artifacts/archive/report.zip");
        var copy = Path.Combine(f.Context.RepositoryPath, ".cis/local/artifacts/retrieved/report.json");
        foreach (var path in new[] { live, archive, copy }) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "same bytes"); }
        var rootA = f.Ledger.Admit(f.Context, first, "native/a", 60, 60);
        var rootB = f.Ledger.Admit(f.Context, second, "native/b", 60, 60);
        Assert.True(rootA.Admitted); Assert.True(rootB.Admitted);
        var before = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
        var heldA = f.Ledger.ReserveStorage(f.Context, rootA.Reference!, "unit", 3 * 1024 * 1024,
            [new(live)], false, false, "Controller");
        Assert.True(heldA.Allowed, string.Join("; ", heldA.Result.Errors));
        var denied = f.Ledger.ReserveStorage(f.Context, rootB.Reference!, "mutation", 2 * 1024 * 1024,
            [new(copy)], false, false, "Controller");
        Assert.False(denied.Allowed); Assert.Contains("Owned storage prevents expansion", Assert.Single(denied.Result.Errors), StringComparison.Ordinal);
        var claimedAgain = f.Ledger.ReserveStorage(f.Context, rootA.Reference!, "unit", 3 * 1024 * 1024,
            [new(archive)], false, true, "Controller");
        Assert.True(claimedAgain.Allowed); Assert.Equal(heldA.ReservationId, claimedAgain.ReservationId);
        Assert.Equal(heldA.MaximumGrowthBytes, claimedAgain.MaximumGrowthBytes);
        Assert.True(claimedAgain.Result.Storage!.ReservedBytes >= heldA.Result.Storage!.ReservedBytes);
        var repeated = f.Ledger.ReserveStorage(f.Context, rootA.Reference!, "unit", 3 * 1024 * 1024,
            [new(archive)], false, true, "Controller");
        Assert.Equal(claimedAgain.Result.Storage.ReservedBytes, repeated.Result.Storage!.ReservedBytes);
        Assert.False(f.Ledger.SettleStorage(f.Context, rootA.Reference!, heldA.ReservationId!, false, null).Allowed);
        Assert.True(f.Ledger.InspectStorage(f.Context, first.ChangeId, second.TaskId).Storage!.ReservedBytes > 3 * 1024 * 1024);
        f.Time = f.Time.AddSeconds(2);
        Assert.Equal(0, f.Ledger.Settle(f.Context, rootA.Reference!, "Succeeded", true).ExitCode);
        var settled = f.Ledger.SettleStorage(f.Context, rootA.Reference!, heldA.ReservationId!, true, [live]);
        Assert.True(settled.Allowed, string.Join("; ", settled.Errors)); Assert.Equal(0, settled.Storage!.ReservedBytes);
        Assert.Contains(Path.GetFullPath(live), settled.Storage.ProtectedPaths);
        Assert.Equal(Directory.EnumerateFiles(Path.Combine(f.Context.RepositoryPath, ".cis/local"), "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length), settled.Storage.OwnedBytes);
        Assert.True(settled.Storage.ProtectedBytes >= new FileInfo(live).Length);
        var measured = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
        Assert.Equal(2_000, measured.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task!.UsedAttempts, measured.Task.UsedAttempts);
        File.Delete(copy);
        var refreshed = f.Ledger.InspectStorage(f.Context, first.ChangeId, first.TaskId);
        Assert.True(refreshed.Storage!.OwnedBytes < settled.Storage.OwnedBytes);
        Assert.True(f.Ledger.SettleStorage(f.Context, rootA.Reference!, heldA.ReservationId!, true, [live]).Allowed);
        var after = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
        Assert.Equal(measured.Task.ConsumedMilliseconds, after.Task!.ConsumedMilliseconds);
        Assert.Equal(measured.Task.UsedAttempts, after.Task.UsedAttempts);
        Assert.Equal(0, f.Ledger.Settle(f.Context, rootB.Reference!, "Succeeded", true).ExitCode);
    }

    [Fact]
    public void WorkLedger_MissingStorageAndUnownedClaimsHoldWithoutInventingCapacity()
    {
        using var f = new Fixture(); var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(180, 3, 0, [new(identity.TaskId, 120, 3)],
            "approved native allocation", "a reference alone is not a byte ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Bound native work.").ExitCode);
        var root = f.Ledger.Admit(f.Context, identity, "native/missing-storage", 30, 30); Assert.True(root.Admitted);
        var ledger = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var before = File.ReadAllBytes(ledger);
        var missing = f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId);
        Assert.False(missing.Allowed); Assert.Null(missing.Storage);
        Assert.Contains("Storage.MaximumBytes", Assert.Single(missing.Errors), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(ledger));
        var current = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var withStorage = allocation with
        {
            ExpectedRevision = current.Revision,
            Storage = new(1024 * 1024, [new(f.Context.RepositoryPath, ".cis/local/native")])
        };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, withStorage, [identity], "Controller", "Supply the authorized finite ceiling once.").ExitCode);
        var unowned = Path.Combine(f.Context.RepositoryPath, "user-source.txt");
        var denied = f.Ledger.ReserveStorage(f.Context, root.Reference!, "native", 1024, [new(unowned)], false, false, "Controller");
        Assert.False(denied.Allowed); Assert.False(File.Exists(unowned));
        Assert.Equal(0, f.Ledger.InspectStorage(f.Context, identity.ChangeId, identity.TaskId).Storage!.ReservedBytes);
        Assert.Equal(0, f.Ledger.Settle(f.Context, root.Reference!, "held", true).ExitCode);
    }
}
