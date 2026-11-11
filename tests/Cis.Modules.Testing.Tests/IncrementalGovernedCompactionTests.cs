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
