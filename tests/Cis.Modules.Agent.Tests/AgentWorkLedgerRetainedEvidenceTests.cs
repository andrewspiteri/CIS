using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void PublishedEvidenceDependenciesReplaceOnlyAfterPublicationAndSurviveGenericSettlement()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateCompactionFixture(f, identity);
        var stage = CompactionStage(f);
        CisAgentWorkReference Settled(string id)
        {
            var admitted = f.Ledger.Admit(f.Context, identity, id, 30, 180, stage: stage, controller: "Controller");
            Assert.True(admitted.Admitted, string.Join("; ", admitted.Result.Errors));
            f.Time = f.Time.AddSeconds(1);
            Assert.Equal(0, f.Ledger.Settle(f.Context, admitted.Reference!, "native-finished", true).ExitCode);
            CloseCompactionAttempt(f, identity);
            return admitted.Reference!;
        }
        var original = Settled("native/evidence-original");
        var replacement = Settled("native/evidence-replacement");
        var publisher = f.Ledger.Admit(f.Context, identity, "native/evidence-publisher", 30, 180, stage: stage, controller: "Controller");
        var path = Path.GetFullPath(Path.Combine(f.Context.RepositoryPath, ".cis/local/current-evidence.json"));
        var first = f.Ledger.ReserveStorage(f.Context, publisher.Reference!, "publish-original", 1024,
            [new(path) { MutatesExisting = true, RetainedWorkReferences = [original] }], false, false, "Controller");
        Assert.True(first.Allowed, string.Join("; ", first.Result.Errors));
        File.WriteAllText(path, "original evidence");
        Assert.True(f.Ledger.SettleStorage(f.Context, publisher.Reference!, first.ReservationId!, true, [path]).Allowed);
        Assert.Equal(0, f.Ledger.Settle(f.Context, publisher.Reference!, "native-finished", true).ExitCode);
        CloseCompactionAttempt(f, identity);
        for (var index = 0; index < 4; index++) Settled("native/evidence-newer-" + index);
        var next = f.Ledger.Admit(f.Context, identity, "native/evidence-next", 30, 180, stage: stage, controller: "Controller");
        Assert.True(next.Admitted, string.Join("; ", next.Result.Errors));
        var nextReference = next.Reference!;
        InflateCompactionFixture(f, identity);

        // The candidate is older than the recent-root window and has no claim
        // yet. Admission itself compacts, so it must preserve both versions.
        var pending = f.Ledger.ReserveStorage(f.Context, nextReference, "failed-publication", 1024,
            [new(path) { MutatesExisting = true, RetainedWorkReferences = [replacement] }], false, false, "Controller");
        Assert.True(pending.Allowed, string.Join("; ", pending.Result.Errors));
        var compacted = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)))!;
        Assert.True(compacted["changes"]![identity.ChangeId]!["compactedExecutionCount"]!.GetValue<long>() > 0);
        Assert.True(new FileInfo(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)).Length < 1024 * 1024);
        Assert.NotNull(f.Ledger.NativeExecution(f.Context, original.ExecutionId));
        Assert.NotNull(f.Ledger.NativeExecution(f.Context, replacement.ExecutionId));
        Assert.Equal(new[] { original.ExecutionId }, Dependencies());
        Assert.True(f.Ledger.SettleStorage(f.Context, nextReference, pending.ReservationId!, true, null).Allowed);
        Assert.Equal(new[] { original.ExecutionId }, Dependencies());

        var published = f.Ledger.ReserveStorage(f.Context, nextReference, "successful-publication", 1024,
            [new(path) { MutatesExisting = true, RetainedWorkReferences = [replacement] }], false, false, "Controller");
        Assert.True(published.Allowed, string.Join("; ", published.Result.Errors));
        File.WriteAllText(path, "replacement evidence");
        Assert.True(f.Ledger.SettleStorage(f.Context, nextReference, published.ReservationId!, true, [path]).Allowed);
        Assert.Equal(new[] { replacement.ExecutionId }, Dependencies());
        Assert.Equal(nextReference.ExecutionId, Claim()["requiredByExecutionId"]!.GetValue<string>());

        var generic = f.Ledger.ReserveStorage(f.Context, nextReference, "ordinary-finalization", 0,
            [new(path) { MutatesExisting = true }], false, false, "Controller");
        Assert.True(generic.Allowed, string.Join("; ", generic.Result.Errors));
        Assert.True(f.Ledger.SettleStorage(f.Context, nextReference, generic.ReservationId!, true, [path]).Allowed);
        Assert.Equal(new[] { replacement.ExecutionId }, Dependencies());
        var release = f.Ledger.ReserveStorage(f.Context, nextReference, "supersede-evidence", 0,
            [new(path) { MutatesExisting = true }], false, false, "Controller");
        Assert.True(release.Allowed, string.Join("; ", release.Result.Errors));
        Assert.True(f.Ledger.SettleStorage(f.Context, nextReference, release.ReservationId!, true, [], [path]).Allowed);
        Assert.Empty(Dependencies());
        Assert.False(Claim()["required"]!.GetValue<bool>());

        JsonNode Claim() => JsonNode.Parse(File.ReadAllText(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)))!
            ["changes"]![identity.ChangeId]!["storage"]!["claims"]![path]!;
        string[] Dependencies() => Claim()["retainedWorkReferences"]!.AsArray()
            .Select(reference => reference!["executionId"]!.GetValue<string>()).ToArray();
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("adoption")]
    [InlineData("owner")]
    [InlineData("execution")]
    public void EvidenceDependenciesRequireExactSameAuthorityAndCanonicalOwner(string mismatch)
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateCompactionFixture(f, identity);
        var admitted = f.Ledger.Admit(f.Context, identity, "native/exact-evidence-owner", 30, 180,
            stage: CompactionStage(f), controller: "Controller");
        Assert.True(admitted.Admitted);
        var reference = admitted.Reference!;
        var invalid = mismatch switch
        {
            "authority" => reference with { AuthorityRepositoryId = "another-authority" },
            "adoption" => reference with { AdoptionId = "another-adoption" },
            "owner" => reference with { OwnerTaskId = "WORK-B" },
            _ => reference with { ExecutionId = "native/missing-evidence" }
        };
        var before = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        var held = f.Ledger.ReserveStorage(f.Context, reference, "invalid-evidence", 0,
            [new(Path.Combine(f.Context.RepositoryPath, ".cis/local/evidence.json")) { RetainedWorkReferences = [invalid] }],
            false, false, "Controller");
        Assert.False(held.Allowed);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
    }
}
