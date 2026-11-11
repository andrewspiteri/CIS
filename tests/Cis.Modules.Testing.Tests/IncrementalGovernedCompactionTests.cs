using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class IncrementalGovernedUnitRetryTests
{
    [Fact]
    public void RevalidatedGovernedUnitEvidenceRetainsOriginalSettlementAcrossRealLedgerCompaction()
    {
        using var fixture = new GovernedUnitFixture();
        var obsolete = CompleteCompactionMetadata(fixture, "obsolete");
        RecordCompactionProgress(fixture, "The unrelated metadata check settled.", closeAttempt: true);

        var first = fixture.Run();

        Assert.True(first.ExitCode == 0, string.Join("; ", first.Diagnostics));
        Assert.True(first.ScopeComplete);
        Assert.Equal(2, first.ExecutedUnitBatches);
        var calls = fixture.Native.Calls();
        Assert.Equal(2, calls.Length);
        var original = fixture.Native.SavedState();
        var originalReference = Assert.IsType<CisAgentWorkReference>(first.WorkReference);
        Assert.Equal(originalReference, original.WorkReference);
        Assert.All(original.Batches.Values, batch =>
        {
            Assert.NotNull(batch.Unit!.Execution!.WorkReference);
            Assert.Equal(originalReference, batch.Unit!.Execution!.SettlementReference);
        });
        var references = original.Batches.Values.SelectMany(batch => new[]
            { batch.Unit!.Execution!.WorkReference, batch.Unit!.Execution!.SettlementReference })
            .Append(original.WorkReference).OfType<CisAgentWorkReference>().Distinct().ToArray();
        Assert.Equal(3, references.Length); // The invocation root and both actual native stages.
        var originalProof = fixture.Work().Executions.Where(execution => references.Any(reference =>
            reference.ExecutionId == execution.ExecutionId)).ToDictionary(execution => execution.ExecutionId);
        var reports = original.Batches.Values.Select(batch => fixture.Native.Full(
            IncrementalTestService.LocalRoot + "/sample/" + batch.Unit!.ReportPath)).ToDictionary(path => path, File.ReadAllBytes);
        RecordCompactionProgress(fixture, "Both selected native unit batches passed.", closeAttempt: true);
        Assert.Null(fixture.Work().LogicalAttemptId);

        var metadata = fixture.Service.Revalidate(fixture.Options(), "Reviewed unchanged native inputs and accepted reports.");

        Assert.True(metadata.ExitCode == 0, string.Join("; ", metadata.Diagnostics));
        Assert.Equal(2, metadata.ReusedUnitBatches);
        var metadataReference = Assert.IsType<CisAgentWorkReference>(metadata.WorkReference);
        Assert.NotEqual(originalReference, metadataReference);
        Assert.Equal(original.WorkReference, fixture.Native.SavedState().WorkReference);
        var ledgerPath = fixture.Native.Full(CisAgentWorkPaths.Ledger);
        var published = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var claims = published["changes"]!["CIS-0001"]!["storage"]!["claims"]!.AsObject();
        foreach (var path in reports.Keys.Append(fixture.StatePath))
            Assert.Equal(metadataReference.ExecutionId, claims[path]!["requiredByExecutionId"]!.GetValue<string>());
        var retained = claims[fixture.StatePath]!["retainedWorkReferences"]!
            .Deserialize<CisAgentWorkReference[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(references, reference => Assert.Contains(reference, retained));

        for (var index = 0; index < 4; index++) CompleteCompactionMetadata(fixture, "newer-" + index);
        var before = fixture.Work();
        var recentRoots = before.Executions.Where(execution => execution.ExecutionId == execution.RootExecutionId)
            .OrderByDescending(execution => execution.CompletedAtUtc!, StringComparer.Ordinal).Take(4).ToArray();
        Assert.DoesNotContain(recentRoots, execution => execution.ExecutionId == originalReference.ExecutionId);
        Assert.NotEqual(originalReference.LogicalAttemptId, before.LogicalAttemptId);
        Assert.All(before.Executions, execution => Assert.True(execution.Finalized));
        Assert.All(before.Checkpoints, checkpoint => Assert.DoesNotContain(checkpoint.Checkpoint.Evidence,
            evidence => references.Append(obsolete).Any(reference => evidence.Contains(reference.ExecutionId, StringComparison.Ordinal))));

        // Use the same bounded inflation as AgentWorkLedgerCompactionTests. The
        // ordinary progress mutation must cross the production 4 MiB threshold.
        var inflated = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var checkpoints = inflated["changes"]!["CIS-0001"]!["owners"]!["WORK-090"]!["checkpoints"]!.AsArray();
        var checkpoint = checkpoints[0]!.DeepClone();
        checkpoint["checkpoint"]!["evidence"] = new JsonArray(Enumerable.Range(0, 40)
            .Select(_ => (JsonNode?)JsonValue.Create(new string('e', 4000))).ToArray());
        for (var index = 0; index < 40; index++) checkpoints.Add(checkpoint.DeepClone());
        File.WriteAllText(ledgerPath, inflated.ToJsonString());
        Assert.InRange(new FileInfo(ledgerPath).Length, 6 * 1024 * 1024, 8 * 1024 * 1024 - 65536);
        before = fixture.Work();
        Assert.True(before.Task!.ConsumedMilliseconds > 0);

        RecordCompactionProgress(fixture, "Retained native reports remain the accepted unit evidence.", closeAttempt: false);

        var after = fixture.Work();
        Assert.Equal(before.Task, after.Task);
        Assert.Equal(before.Change, after.Change);
        Assert.Equal(before.LogicalAttemptId, after.LogicalAttemptId);
        Assert.DoesNotContain(after.Executions, execution => execution.ExecutionId == obsolete.ExecutionId);
        var compacted = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        Assert.True(compacted["changes"]!["CIS-0001"]!["compactedExecutionCount"]!.GetValue<long>() > 0);
        Assert.True(compacted["changes"]!["CIS-0001"]!["owners"]!["WORK-090"]!["compactedCheckpointCount"]!.GetValue<long>() > 0);
        Assert.True(new FileInfo(ledgerPath).Length < 1024 * 1024);
        foreach (var reference in references.Append(metadataReference))
        {
            var settlement = fixture.Controller.InspectSettlement(fixture.Native.Path, reference);
            Assert.Empty(settlement.Errors);
            Assert.True(CisNativeWorkSettlement.Complete(settlement, reference));
            if (originalProof.TryGetValue(reference.ExecutionId, out var execution))
                Assert.Equal(execution, settlement.Work!.Executions.Single(item => item.ExecutionId == reference.ExecutionId));
        }

        var stateBytes = File.ReadAllBytes(fixture.StatePath);
        var plan = fixture.Service.Plan(fixture.Options());
        var status = fixture.Service.Plan(fixture.Options()); // CLI status shares this read-only currentness path.
        var verified = fixture.Service.Verify(new(fixture.Native.Path, "profile.json") { Layer = "unit" });

        Assert.True(plan.ScopeComplete);
        Assert.All(plan.Batches, batch => Assert.True(batch.UnitCurrent));
        Assert.Equal(0, status.PendingUnitBatches);
        Assert.Equal(0, verified.ExitCode);
        Assert.Equal(stateBytes, File.ReadAllBytes(fixture.StatePath));
        var reused = fixture.Run();
        Assert.True(reused.ExitCode == 0, string.Join("; ", reused.Diagnostics));
        Assert.True(reused.ScopeComplete);
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(2, reused.ReusedUnitBatches);
        Assert.Equal(calls, fixture.Native.Calls());
        foreach (var report in reports) Assert.Equal(report.Value, File.ReadAllBytes(report.Key));
    }

    [Fact]
    public void ReconciledInterruptedMetadataPublicationRetainsLegacyUnitProofAcrossLedgerCompaction()
    {
        using var fixture = new GovernedUnitFixture();
        var obsolete = CompleteCompactionMetadata(fixture, "legacy-obsolete");
        RecordCompactionProgress(fixture, "The unrelated historical metadata check settled.", closeAttempt: true);
        var first = fixture.Run();
        Assert.True(first.ExitCode == 0, string.Join("; ", first.Diagnostics));
        Assert.Equal(2, first.ExecutedUnitBatches);
        var original = fixture.Native.SavedState();
        var originalReference = Assert.IsType<CisAgentWorkReference>(first.WorkReference);
        var references = original.Batches.Values.SelectMany(batch => new[]
            { batch.Unit!.Execution!.WorkReference, batch.Unit!.Execution!.SettlementReference })
            .Append(original.WorkReference).OfType<CisAgentWorkReference>().Distinct().ToArray();
        Assert.Equal(3, references.Length);
        var calls = fixture.Native.Calls();
        Assert.Equal(2, calls.Length);
        var reports = original.Batches.Values.Select(batch => fixture.Native.Full(
            IncrementalTestService.LocalRoot + "/sample/" + batch.Unit!.ReportPath)).ToDictionary(path => path, File.ReadAllBytes);
        RecordCompactionProgress(fixture, "The original native unit evidence is complete.", closeAttempt: true);
        var legacy = fixture.Service.Revalidate(fixture.Options(), "Reviewed the unchanged legacy unit cache.");
        Assert.True(legacy.ExitCode == 0, string.Join("; ", legacy.Diagnostics));
        var legacyWriter = Assert.IsType<CisAgentWorkReference>(legacy.WorkReference);
        Assert.Equal(originalReference, fixture.Native.SavedState().WorkReference);
        var ledgerPath = fixture.Native.Full(CisAgentWorkPaths.Ledger);
        var legacyLedger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var legacyStorage = legacyLedger["changes"]!["CIS-0001"]!["storage"]!;
        foreach (var path in reports.Keys.Append(fixture.StatePath))
            Assert.Equal(legacyWriter.ExecutionId, legacyStorage["claims"]![path]!["requiredByExecutionId"]!.GetValue<string>());
        // Reproduce a cache already revalidated by the older release: its writer
        // moved to metadata, but its claims/reservations predate dependency lists.
        foreach (var claim in legacyStorage["claims"]!.AsObject()) claim.Value!.AsObject().Remove("retainedWorkReferences");
        foreach (var reservation in legacyStorage["reservations"]!.AsObject()) reservation.Value!.AsObject().Remove("retainedWorkReferences");
        File.WriteAllText(ledgerPath, legacyLedger.ToJsonString());
        Assert.True(fixture.Service.Plan(fixture.Options()).ScopeComplete);
        Assert.False(legacyStorage["claims"]![fixture.StatePath]!.AsObject().ContainsKey("retainedWorkReferences"));

        var admission = fixture.Controller.Admit(new(fixture.Native.Path, "native/incremental/interrupted-legacy-publication",
            "incremental-revalidate", 30, fixture.Association));
        Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
        using var publisher = admission.Scope!;
        var storage = (ICisOwnedStorageController)fixture.Controller;
        var stateBytes = File.ReadAllBytes(fixture.StatePath);
        var pending = storage.Reserve(fixture.Native.Path, publisher.Reference,
            publisher.Reference.ExecutionId + "/metadata/interrupted-state", stateBytes.Length,
            [new(fixture.StatePath) { MutatesExisting = true, RetainedWorkReferences = references }]);
        Assert.True(pending.Allowed, string.Join("; ", pending.Result.Errors));
        // The existing OwnedJson publication boundary is Reserve -> AtomicJson ->
        // Settle. Inject the interruption after the atomic replacement and use a
        // real busy ledger transaction, retaining the exact pending grant.
        File.WriteAllBytes(fixture.StatePath + ".tmp", stateBytes);
        File.Move(fixture.StatePath + ".tmp", fixture.StatePath, overwrite: true);
        var pendingBytes = File.ReadAllBytes(ledgerPath);
        using (var held = new FileStream(fixture.Native.Full(CisAgentWorkPaths.Lock), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = storage.Settle(fixture.Native.Path, publisher.Reference, pending.ReservationId!, true,
                reports.Keys.Append(fixture.StatePath).ToArray());
            Assert.False(failed.Allowed);
            Assert.Contains(failed.Errors, error => error.Contains(Path.GetFileName(CisAgentWorkPaths.Lock), StringComparison.Ordinal));
        }
        Assert.Equal(pendingBytes, File.ReadAllBytes(ledgerPath));
        Assert.Equal(0, publisher.Finish("native-finished", "The metadata controller stopped after atomic state replacement.", []).ExitCode);
        var beforeRecovery = fixture.Work();
        Assert.All(beforeRecovery.Executions, execution => Assert.True(execution.Finalized));
        Assert.False(CisNativeWorkSettlement.Complete(fixture.Controller.InspectSettlement(fixture.Native.Path, publisher.Reference), publisher.Reference));
        var pendingLedger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var pendingRecord = pendingLedger["changes"]!["CIS-0001"]!["storage"]!["reservations"]![pending.ReservationId!]!;
        Assert.False(pendingRecord["finalized"]!.GetValue<bool>());
        var candidates = pendingRecord["retainedWorkReferences"]![fixture.StatePath]!
            .Deserialize<CisAgentWorkReference[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(references, reference => Assert.Contains(reference, candidates));
        Assert.Empty(pendingLedger["changes"]!["CIS-0001"]!["storage"]!["claims"]![fixture.StatePath]!["retainedWorkReferences"]!.AsArray());
        var recovery = new CisAgentWorkDispositionRequest("reconcile", beforeRecovery.Revision)
        { ExecutionId = publisher.Reference.ExecutionId, Evidence = ["fixture:stopped-metadata-publication-boundary"] };
        fixture.Native.Write(".cis/local/reconcile-metadata-publication.json", JsonSerializer.Serialize(recovery,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var reconciled = fixture.Controller.DisposeWork(fixture.Native.Path, "CIS-0001", "WORK-090",
            ".cis/local/reconcile-metadata-publication.json", "Controller", "Settle the exact safely stopped metadata publication without repeating native work.");

        Assert.True(reconciled.ExitCode == 0, string.Join("; ", reconciled.Errors));
        Assert.Equal("reconciled", reconciled.Status);
        Assert.Equal(beforeRecovery.Task, reconciled.Work!.Task);
        Assert.Equal(beforeRecovery.Change, reconciled.Work.Change);
        Assert.True(CisNativeWorkSettlement.Complete(fixture.Controller.InspectSettlement(fixture.Native.Path, publisher.Reference), publisher.Reference));
        var recoveredLedger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var recoveredStorage = recoveredLedger["changes"]!["CIS-0001"]!["storage"]!;
        Assert.False(recoveredStorage["reservations"]!.AsObject().ContainsKey(pending.ReservationId!));
        Assert.Equal(legacyWriter.ExecutionId, recoveredStorage["claims"]![fixture.StatePath]!["requiredByExecutionId"]!.GetValue<string>());
        Assert.True(recoveredStorage["claims"]![fixture.StatePath]!["required"]!.GetValue<bool>());
        for (var index = 0; index < 4; index++) CompleteCompactionMetadata(fixture, "post-recovery-" + index);
        var before = fixture.Work();
        Assert.NotEqual(originalReference.LogicalAttemptId, before.LogicalAttemptId);
        Assert.DoesNotContain(before.Executions.Where(execution => execution.ExecutionId == execution.RootExecutionId)
            .OrderByDescending(execution => execution.CompletedAtUtc!, StringComparer.Ordinal).Take(4),
            execution => execution.ExecutionId == originalReference.ExecutionId);
        Assert.All(before.Checkpoints, checkpoint => Assert.DoesNotContain(checkpoint.Checkpoint.Evidence,
            evidence => references.Append(obsolete).Any(reference => evidence.Contains(reference.ExecutionId, StringComparison.Ordinal))));
        var inflated = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        var checkpoints = inflated["changes"]!["CIS-0001"]!["owners"]!["WORK-090"]!["checkpoints"]!.AsArray();
        var checkpoint = checkpoints[0]!.DeepClone();
        checkpoint["checkpoint"]!["evidence"] = new JsonArray(Enumerable.Range(0, 40)
            .Select(_ => (JsonNode?)JsonValue.Create(new string('e', 4000))).ToArray());
        for (var index = 0; index < 40; index++) checkpoints.Add(checkpoint.DeepClone());
        File.WriteAllText(ledgerPath, inflated.ToJsonString());
        Assert.InRange(new FileInfo(ledgerPath).Length, 6 * 1024 * 1024, 8 * 1024 * 1024 - 65536);
        before = fixture.Work();

        RecordCompactionProgress(fixture, "Preserve the recovered current unit evidence.", closeAttempt: false);

        var after = fixture.Work();
        Assert.Equal(before.Task, after.Task);
        Assert.Equal(before.Change, after.Change);
        Assert.DoesNotContain(after.Executions, execution => execution.ExecutionId == obsolete.ExecutionId);
        var compacted = JsonNode.Parse(File.ReadAllText(ledgerPath))!["changes"]!["CIS-0001"]!;
        Assert.True(compacted["compactedExecutionCount"]!.GetValue<long>() > 0);
        Assert.False(compacted["storage"]!["reservations"]!.AsObject().ContainsKey(pending.ReservationId!));
        Assert.True(new FileInfo(ledgerPath).Length < 1024 * 1024);
        foreach (var reference in references)
        {
            var settlement = fixture.Controller.InspectSettlement(fixture.Native.Path, reference);
            Assert.Empty(settlement.Errors);
            Assert.True(CisNativeWorkSettlement.Complete(settlement, reference));
        }
        var retained = compacted["storage"]!["claims"]![fixture.StatePath]!["retainedWorkReferences"]!
            .Deserialize<CisAgentWorkReference[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.All(references, reference => Assert.Contains(reference, retained));
        Assert.Equal(stateBytes, File.ReadAllBytes(fixture.StatePath));
        var plan = fixture.Service.Plan(fixture.Options());
        var status = fixture.Service.Plan(fixture.Options()); // CLI status shares this read-only currentness path.
        Assert.True(plan.ScopeComplete);
        Assert.All(plan.Batches, batch => Assert.True(batch.UnitCurrent));
        Assert.Equal(0, status.PendingUnitBatches);
        Assert.Equal(0, fixture.Service.Verify(new(fixture.Native.Path, "profile.json") { Layer = "unit" }).ExitCode);
        var reused = fixture.Run();
        Assert.True(reused.ExitCode == 0, string.Join("; ", reused.Diagnostics));
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(2, reused.ReusedUnitBatches);
        Assert.Equal(calls, fixture.Native.Calls());
        foreach (var report in reports) Assert.Equal(report.Value, File.ReadAllBytes(report.Key));
    }

    private static CisAgentWorkReference CompleteCompactionMetadata(GovernedUnitFixture fixture, string name)
    {
        var admitted = fixture.Controller.Admit(new(fixture.Native.Path, "native/incremental/compaction-" + name,
            "incremental-revalidate", 30, fixture.Association));
        Assert.True(admitted.Allowed, string.Join("; ", admitted.Errors));
        using var scope = admitted.Scope!;
        Assert.Equal(0, scope.Finish("native-finished", "The bounded metadata inspection settled.", []).ExitCode);
        return admitted.Reference!;
    }

    private static void RecordCompactionProgress(GovernedUnitFixture fixture, string outcome, bool closeAttempt)
    {
        var request = new CisAgentWorkProgressRequest(new(outcome, ["fixture:accepted-unit-evidence"],
            "Retain the same accepted inputs and native reports.", "Preserve original accounting and settlement proof.",
            "Reuse the current completed unit batches.")
        { CloseAttempt = closeAttempt, MaterialNewInformation = true }, fixture.Work().Revision);
        fixture.Native.Write(".cis/local/compaction-progress.json", JsonSerializer.Serialize(request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var result = fixture.Controller.RecordWorkProgress(fixture.Native.Path, "CIS-0001", "WORK-090",
            ".cis/local/compaction-progress.json", "Controller");
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors));
    }
}
