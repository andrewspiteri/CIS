using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalNativeWorkTests
{
    [Fact]
    public void DamagedUnitEvidenceThatRequiresAnotherMutationStillNeedsThePilot()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var initial = fixture.Run();
        Assert.True(initial.ExitCode == 0, string.Join("; ", initial.Diagnostics));
        var saved = fixture.SavedState();
        var unit = saved.Batches.Values.First().Unit!;
        File.AppendAllText(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + unit.ReportPath), " ");
        var batch = fixture.Plan().Batches.Single(item => item.Id == saved.Batches.First().Key);
        Assert.False(batch.UnitCurrent);
        Assert.True(batch.MutationCurrent);
        var calls = fixture.Calls();
        var authority = new UnitInvocationAuthority(fixture.Path);

        var held = new IncrementalTestService(fixture, authority, authority).Run(new(fixture.Path, "profile.json"), 0,
            TestContext.Current.CancellationToken);

        Assert.Equal("pilot-required", held.Status);
        Assert.Equal(0, authority.Launched);
        Assert.Equal(calls, fixture.Calls());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void GovernedUnitLayerNeedsNoMutationPilotAndRetainsCurrentness(bool mutationConfigured, bool force)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with
        {
            MutationCommand = mutationConfigured ? fixture.Profile.MutationCommand : null,
            MaxRetainedBytes = 32 * 1024 * 1024,
        });
        var authority = new UnitInvocationAuthority(fixture.Path);
        var service = new IncrementalTestService(fixture, authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120, force);

        var result = service.Run(options, 0, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(2, result.ExecutedUnitBatches);
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.All(fixture.Calls(), call => Assert.StartsWith("unit ", call, StringComparison.Ordinal));
        Assert.Equal(2, authority.Launched);
        Assert.False(result.Qualified);
        Assert.False(result.Pilot!.Ready);
        Assert.All(service.Plan(options with { Force = false }).Batches, batch => Assert.True(batch.UnitCurrent));
        var calls = fixture.Calls();
        var warm = service.Run(options with { Force = false }, 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, warm.ExecutedUnitBatches);
        Assert.Equal(2, warm.ReusedUnitBatches);
        Assert.Equal(calls, fixture.Calls());
    }

    // Controlled admission exercises the real native fixture/guard through the
    // supported contracts; it makes no assertion about real ledger allocation.
    private sealed class UnitInvocationAuthority(string repository) : ICisNativeWorkController, ICisOwnedStorageController
    {
        private string Repository => repository;
        public int Launched { get; private set; }
        public CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null)
            => new(true, "current", "controlled unit protocol admission", null, null, []);
        public CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference reference) => Inspect(authorityPath);
        public CisNativeWorkAdmission Admit(CisNativeWorkRequest request)
            => Admit(request.ExecutionId, request.MaximumSeconds, null);
        private CisNativeWorkAdmission Admit(string executionId, int maximumSeconds, string? parent)
        {
            var reference = new CisAgentWorkReference("fixture", "unit-protocol", "CIS-0001", "WORK-090", "WORK-090", executionId, "fixture-attempt", parent);
            return new(true, "admitted", "controlled unit protocol admission", reference, null, [])
            { AuthorityPath = repository, Scope = new Scope(this, reference, maximumSeconds) };
        }
        CisOwnedStorageResult ICisOwnedStorageController.Inspect(string repositoryPath, CisNativeWorkOptions? association)
            => Storage();
        private CisOwnedStorageResult Storage() => new("current", true,
            new("unit-protocol", "CIS-0001", "WORK-090", "controlled finite ceiling", 64 * 1024 * 1024, 0, 0, 0,
                [Path.Combine(repository, ".cis/local")], [], "fixture-revision"), []);
        public CisOwnedStorageAdmission Reserve(string repositoryPath, CisAgentWorkReference reference, string operationId,
            long maximumGrowthBytes, IReadOnlyList<CisOwnedStoragePath>? claims = null, bool cleanup = false,
            bool clampToRemaining = false, string? actor = null)
            => new(Storage(), Guid.NewGuid().ToString("N"), maximumGrowthBytes);
        public CisOwnedStorageResult Settle(string repositoryPath, CisAgentWorkReference reference, string reservationId,
            bool safelyStopped, IReadOnlyList<string>? requiredPaths = null, IReadOnlyList<string>? supersededRequiredPaths = null)
        { Assert.True(safelyStopped); return Storage(); }
        private sealed class Scope(UnitInvocationAuthority owner, CisAgentWorkReference reference, int maximumSeconds) : ICisNativeWorkScope
        {
            public string AuthorityPath => owner.Repository;
            public string Actor => "Controlled unit controller";
            public CisAgentWorkReference Reference => reference;
            public int MaximumSeconds => maximumSeconds;
            public TimeSpan Remaining => TimeSpan.FromSeconds(maximumSeconds);
            public CisNativeWorkAdmission AdmitChild(string executionId, string operation, int seconds)
                => owner.Admit(executionId, seconds, reference.ExecutionId);
            public void Launch() => owner.Launched++;
            public void Started(int processId, string processStartedAtUtc, CisNativeWorkContainment? containment = null) { }
            public void Returned(bool safelyStopped) => Assert.True(safelyStopped);
            public void MarkUncertain(string reason) => Assert.Fail(reason);
            public CisAgentWorkResult Finish(string status, string? summary, IReadOnlyList<string>? evidence)
                => new("settled", null, [], false);
            public void Dispose() { }
        }
    }
}
