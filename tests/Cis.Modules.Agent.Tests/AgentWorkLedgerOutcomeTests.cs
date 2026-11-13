using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void OutcomeDistribution_ControllerAddsSupportingWorkAndSplitRetainsOriginalCostAndOwner()
    {
        using var f = new Fixture();
        var authorization = WriteOutcomeAuthorization(f, 300, 10, 30);
        var first = f.Identity("WORK-A"); var supporting = f.Identity("WORK-B");
        var initial = OutcomeAllocation(authorization, [new(first.TaskId, 180, 4, 20)]);
        var allocated = f.Ledger.Allocate(f.Context, first.ChangeId, initial, [first], "Controller", "Distribute the already authorized outcome.");
        Assert.Equal(0, allocated.ExitCode);
        var running = f.Ledger.Admit(f.Context, first, "outcome/first", 60, 3600, controller: "Controller");
        Assert.True(running.Admitted);
        f.Time = f.Time.AddSeconds(2);
        var settled = f.Ledger.Settle(f.Context, running.Reference!, "Succeeded", true);

        var addition = initial with { Tasks = [new(supporting.TaskId, 100, 3, 10)], ExpectedRevision = settled.Work!.Revision };
        var added = f.Ledger.Allocate(f.Context, first.ChangeId, addition, [supporting], "Controller", "Add a necessary supporting task within the same grant.");
        Assert.Equal(0, added.ExitCode);
        var child = f.Identity("WORK-A-CHILD", [first.TaskId]);
        var childRun = f.Ledger.Admit(f.Context, child, "outcome/child", 30, 3600, controller: "Controller");
        Assert.True(childRun.Admitted);
        Assert.Equal(first.TaskId, childRun.Reference!.OwnerTaskId);
        f.Time = f.Time.AddSeconds(3);
        var completed = f.Ledger.Settle(f.Context, childRun.Reference!, "Succeeded", true);
        Assert.Equal(5_000, completed.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(300_000, completed.Work.Change.AllocatedMilliseconds);
        Assert.Equal(1, completed.Work.Task.UsedAttempts);
        var retained = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Context.RepositoryPath, AgentWorkLedger.RelativePath)))!;
        var change = retained["changes"]![first.ChangeId]!;
        Assert.Equal("Maintainer", change["actor"]!.GetValue<string>());
        Assert.Equal(authorization.Id, change["authorizationId"]!.GetValue<string>());
        Assert.Equal("Controller", change["allocationHistory"]![1]!["actor"]!.GetValue<string>());
        Assert.Equal("Maintainer", change["allocationHistory"]![1]!["authorizedBy"]!.GetValue<string>());
        Assert.False(change["owners"]![first.TaskId]!["fixed"]!.GetValue<bool>());
        Assert.Equal(2, change["owners"]!.AsObject().Count);
    }

    [Fact]
    public void OutcomeDistribution_OneHundredMetadataOwnersCannotMultiplyTheAuthorizedTotal()
    {
        using var f = new Fixture();
        var authorization = WriteOutcomeAuthorization(f, 1000, 120, 20);
        var identities = Enumerable.Range(1, 100).Select(index => f.Identity("WORK-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var request = OutcomeAllocation(authorization, identities.Select(task => new CisAgentWorkTaskAllocation(task.TaskId, 8, 1)).ToArray());
        var allocated = f.Ledger.Allocate(f.Context, "CIS-0001", request, identities, "Controller", "Distribute one grant across metadata only; launch no workers.");
        Assert.Equal(0, allocated.ExitCode);
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, AgentWorkLedger.RelativePath);
        var before = File.ReadAllBytes(ledgerPath);
        var overflow = request with
        {
            Tasks = identities.Select(task => new CisAgentWorkTaskAllocation(task.TaskId, 11, 1)).ToArray(),
            ExpectedRevision = allocated.Work!.Revision
        };
        var denied = f.Ledger.Allocate(f.Context, "CIS-0001", overflow, identities, "Controller", "An internal distribution cannot create extra capacity.");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Contains(denied.Errors, error => error.Contains("Aggregate task", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(ledgerPath));
        Assert.Empty(allocated.Work!.Executions);
        var unchanged = CisOutcomeAuthority.Read(f.Context, "CIS-0001")!;
        Assert.Equal(authorization.Id, unchanged.Id);
        Assert.Equal(authorization.Digest, unchanged.Digest);
        Assert.Equal(authorization.AuthorizedAtUtc, unchanged.AuthorizedAtUtc);
        Assert.Empty(unchanged.History);
    }

    [Fact]
    public void OutcomeDistribution_AnOutsideControllerOrHigherOverallBudgetNeedsItsSpecificDecision()
    {
        using var f = new Fixture();
        var authorization = WriteOutcomeAuthorization(f, 300, 10, 30);
        var task = f.Identity("WORK-A");
        var request = OutcomeAllocation(authorization, [new(task.TaskId, 120, 3, 10)]);
        var allocated = f.Ledger.Allocate(f.Context, task.ChangeId, request, [task], "Controller", "Use the one authorized grant.");
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, AgentWorkLedger.RelativePath);
        var before = File.ReadAllBytes(ledgerPath);
        var update = request with { ExpectedRevision = allocated.Work!.Revision };
        Assert.NotEqual(0, f.Ledger.Allocate(f.Context, task.ChangeId, update, [task], "Other actor", "No delegated authority.").ExitCode);
        var denied = f.Ledger.Allocate(f.Context, task.ChangeId, update with { ChangeSeconds = 301 }, [task], "Controller", "Request a real increase.");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Contains(denied.Errors, error => error.Contains("specific increase", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(ledgerPath));
    }

    [Fact]
    public void OutcomeDistribution_ExplicitOverallAmendmentPreservesOriginalDecisionCostAndAttempts()
    {
        using var f = new Fixture();
        var original = WriteOutcomeAuthorization(f, 300, 10, 30);
        var task = f.Identity("WORK-A");
        var request = OutcomeAllocation(original, [new(task.TaskId, 120, 3, 10)]);
        Assert.Equal(0, f.Ledger.Allocate(f.Context, task.ChangeId, request, [task], "Controller", "Distribute original authority.").ExitCode);
        var running = f.Ledger.Admit(f.Context, task, "outcome/amendment", 30, 3600, controller: "Controller");
        Assert.True(running.Admitted);
        f.Time = f.Time.AddSeconds(2);
        var settled = f.Ledger.Settle(f.Context, running.Reference!, "Succeeded", true);
        var amendedRequest = CisOutcomeAuthority.Normalize(original.Request with
        {
            Seconds = 360, Attempts = 12, DecisionReference = "fixture:explicit-overall-amendment",
            Storage = original.Request.Storage with { MaximumBytes = 192 * 1024 * 1024 }
        });
        var amendment = original with
        {
            Request = amendedRequest, Reason = "Explicit synthetic decision on the additional overall capacity.",
            AuthorizedAtUtc = f.Time.ToString("O"), Digest = CisOutcomeAuthority.Digest(amendedRequest),
            History = [new(original.Request, original.Reviewer, original.Reason, original.AuthorizedAtUtc, original.Digest)]
        };
        File.WriteAllText(CisOutcomeAuthority.Path(f.Context, task.ChangeId),
            JsonSerializer.Serialize(amendment, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var increased = OutcomeAllocation(amendment, [new(task.TaskId, 180, 4, 10)]) with { ExpectedRevision = settled.Work!.Revision };
        var applied = f.Ledger.Allocate(f.Context, task.ChangeId, increased, [task], "Controller", "Apply the real amended outcome without resetting work.");
        Assert.Equal(0, applied.ExitCode);
        Assert.Equal(360_000, applied.Work!.Change.AllocatedMilliseconds);
        Assert.Equal(2_000, applied.Work.Change.ConsumedMilliseconds);
        Assert.Equal(1, applied.Work.Change.UsedAttempts);
        Assert.Equal(original.Digest, applied.Work.OriginalAuthorizationDigest);
        Assert.Equal(amendment.Digest, applied.Work.AllocationAuthorizationDigest);
    }

    private static CisOutcomeAuthorization WriteOutcomeAuthorization(Fixture fixture, long seconds, int attempts, long restoration)
    {
        var storage = new CisOwnedStoragePolicy(128 * 1024 * 1024, [new(fixture.Context.RepositoryPath, ".cis/local")])
        { RestorationBytes = 4 * 1024 * 1024 };
        var request = CisOutcomeAuthority.Normalize(new("Complete the neutral fixture outcome.",
            [new("fixture", "Implement the neutral fixture and its supporting checks.", ["subject.cs"])],
            ["No publication or unrelated scope."], seconds, attempts, restoration, storage,
            ["Controller"], true, true, false, false, "fixture:one-outcome-decision"));
        var authorization = new CisOutcomeAuthorization("fixture-outcome", "CIS-0001", request, "Maintainer",
            "Authorize this complete bounded outcome once.", fixture.Time.ToString("O"), CisOutcomeAuthority.Digest(request));
        var path = CisOutcomeAuthority.Path(fixture.Context, authorization.ChangeId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(authorization, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return authorization;
    }

    private static CisAgentWorkAllocationRequest OutcomeAllocation(CisOutcomeAuthorization authorization, IReadOnlyList<CisAgentWorkTaskAllocation> tasks)
        => new(authorization.Request.Seconds, authorization.Request.Attempts, authorization.Request.RestorationSeconds,
            tasks, authorization.Request.DecisionReference, "fixture:shared-storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = authorization.Request.Controllers, Storage = authorization.Request.Storage };
}
