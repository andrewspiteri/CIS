using Cis.Abstractions;
using System.Text.Json;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalNativeWorkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedFinalSettlementRetainsNativeReportsAndHoldsReturnedAndSavedOutcomes(bool child)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, MaxRetainedBytes = 32 * 1024 * 1024 });
        var authority = new UnitInvocationAuthority(fixture.Path) { FailFinish = (_, parent) => child == (parent is not null) };
        var service = new IncrementalTestService(fixture, authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120);

        var result = service.Run(options, 0, TestContext.Current.CancellationToken);

        Assert.Equal("settlement-required", result.Status); Assert.Equal(4, result.ExitCode); Assert.False(result.ScopeComplete);
        Assert.True(authority.SettlementFailures > 0);
        Assert.Contains(result.Diagnostics, message => message.Contains("settlement", StringComparison.Ordinal));
        var state = fixture.SavedState();
        var reports = state.Batches.Values.Where(batch => batch.Unit is not null).Select(batch => batch.Unit!).ToArray();
        Assert.NotEmpty(reports);
        Assert.All(reports, report =>
        {
            Assert.True(report.Completed); Assert.Equal(1, report.Passed);
            Assert.True(File.Exists(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + report.ReportPath)));
        });
        var bytes = reports.ToDictionary(report => report.ReportPath,
            report => File.ReadAllBytes(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + report.ReportPath)));
        using var latest = JsonDocument.Parse(File.ReadAllBytes(fixture.Full(IncrementalTestService.LocalRoot + "/sample/latest-run.json")));
        Assert.Equal("settlement-required", latest.RootElement.GetProperty("status").GetString());
        Assert.Equal("settlement-required", service.Plan(options).Status);
        var calls = fixture.Calls();
        Assert.Equal(4, service.Run(options, 0, TestContext.Current.CancellationToken).ExitCode);
        Assert.Equal(calls, fixture.Calls());

        authority.Reconciled = true;
        var repaired = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, repaired.ExitCode); Assert.True(repaired.ScopeComplete);
        Assert.Equal(reports.Length, repaired.ReusedUnitBatches);
        Assert.Equal(2 - reports.Length, repaired.ExecutedUnitBatches);
        foreach (var report in reports)
            Assert.Equal(bytes[report.ReportPath], File.ReadAllBytes(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + report.ReportPath)));
    }

    [Fact]
    public void SuccessfulChildAndRootSettlementRemainValidWhenOnlyTheNextOperationIsHeld()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, MaxRetainedBytes = 32 * 1024 * 1024 });
        var authority = new UnitInvocationAuthority(fixture.Path) { NextOperationHeld = true };
        var result = new IncrementalTestService(fixture, authority, authority).Run(
            new(fixture.Path, "profile.json", "unit"), 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode); Assert.True(result.ScopeComplete);
        Assert.Equal(2, result.ExecutedUnitBatches); Assert.Equal(0, authority.SettlementFailures);
        Assert.NotNull(result.WorkControl!.Work!.HoldReason);
    }

    [Fact]
    public void FinalizationAdmissionFailureCannotBecomeSuccessThroughImplicitDisposal()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, MaxRetainedBytes = 32 * 1024 * 1024 });
        var authority = new UnitInvocationAuthority(fixture.Path) { FailRootFinalizationReservation = true };
        var service = new IncrementalTestService(fixture, authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit");
        var result = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.Equal("settlement-required", result.Status); Assert.Equal(4, result.ExitCode);
        Assert.True(authority.MarkedUncertain > 0);
        Assert.False(CisNativeWorkSettlement.Complete(authority.InspectSettlement(fixture.Path, result.WorkReference!), result.WorkReference!));
        Assert.Equal("settlement-required", service.Plan(options).Status);
        Assert.All(fixture.SavedState().Batches.Values, batch => Assert.True(batch.Unit!.Completed));
    }

    [Fact]
    public void DamagedUnitEvidenceThatRequiresAnotherMutationStillNeedsThePilot()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var initial = fixture.Run();
        var inheritedAuthorityKeys = CisNativeWorkEnvironment.Keys
            .Where(key => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)));
        var authorityFiles = new List<string>();
        for (var directory = new DirectoryInfo(fixture.Path); directory is not null; directory = directory.Parent)
            foreach (var relative in new[] { CisAgentWorkPaths.Adoption, CisAgentWorkPaths.Ledger, ".cis/repository.yml", ".cis/workspace.yml" })
                if (File.Exists(Path.Combine(directory.FullName, relative))) authorityFiles.Add(Path.Combine(directory.FullName, relative));
        Assert.True(initial.ExitCode == 0, string.Join("; ", initial.Diagnostics)
            + "; inherited authority keys: " + string.Join(", ", inheritedAuthorityKeys)
            + "; authority files: " + string.Join(", ", authorityFiles)
            + "; adopted completion: " + CisEngineeringPolicy.Read(fixture.Path)?.RequireTaskCompletion);
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
    internal sealed class UnitInvocationAuthority(string repository, bool required = true) : ICisNativeWorkController, ICisOwnedStorageController
    {
        private string Repository => repository;
        private readonly Dictionary<string, (CisAgentWorkReference Reference, string Operation, bool Finished)> _executions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CisOwnedStorageOperation> _operations = new(StringComparer.Ordinal);
        public Func<string, string?, bool>? FailFinish { get; init; }
        public Action<string, CisAgentWorkReference, string>? BeforeStorageReservation { get; set; }
        public bool Reconciled { get; set; }
        public bool NextOperationHeld { get; init; }
        public bool FailRootFinalizationReservation { get; init; }
        public int MarkedUncertain { get; private set; }
        public int SettlementFailures { get; private set; }
        public int Launched { get; private set; }
        public CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null)
            => new(required, "current", "controlled unit protocol admission", null, null, []);
        public CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference reference)
            => new(required, "verified", "controlled unit protocol admission", reference, InspectSettlement(authorityPath, reference), []);
        public CisAgentWorkResult InspectSettlement(string authorityPath, CisAgentWorkReference reference)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal) { reference.ExecutionId };
            for (var count = -1; count != ids.Count;)
            {
                count = ids.Count;
                foreach (var item in _executions.Values)
                    if (item.Reference.ParentExecutionId is { } parent && ids.Contains(parent)) ids.Add(item.Reference.ExecutionId);
            }
            return Reconciled || _executions.Values.Where(item => ids.Contains(item.Reference.ExecutionId)).All(item => item.Finished)
                ? Control("unchanged") : new("held", null, ["Controlled final settlement remains unresolved."], false);
        }
        private CisAgentWorkResult Control(string status)
        {
            var executions = _executions.Values.Select(item => new CisAgentWorkExecution(item.Reference.ExecutionId,
                item.Reference.TaskId, item.Reference.OwnerTaskId, item.Reference.LogicalAttemptId, item.Reference.ParentExecutionId,
                item.Reference.ParentExecutionId ?? item.Reference.ExecutionId, item.Finished || Reconciled ? "native-finished" : "running",
                "2026-01-01T00:00:00Z", item.Finished || Reconciled ? "2026-01-01T00:00:01Z" : null,
                1000, 1000, item.Finished || Reconciled ? 0 : 120000, null, null, null)
            { Finalized = item.Reference.ParentExecutionId is null && (item.Finished || Reconciled) }).ToArray();
            var allowance = new CisAgentWorkAllowance(600000, 1000, 0, 30000, 6, 1);
            var view = new CisAgentWorkView("unit-protocol", "CIS-0001", "WORK-090", "WORK-090", "fixture", "fixture",
                "controlled", "known", NextOperationHeld ? "Controller reflection is required before the next operation." : null,
                "Inspect the next operation.", 1000, allowance, allowance, "fixture-attempt", 0, "fixture decision", "unit-protocol",
                "controlled finite ceiling", executions, []);
            return new(status, view, [], false);
        }
        public CisNativeWorkAdmission Admit(CisNativeWorkRequest request)
            => Admit(request.ExecutionId, request.MaximumSeconds, request.Association?.ParentExecutionId, request.Operation);
        private CisNativeWorkAdmission Admit(string executionId, int maximumSeconds, string? parent, string operation)
        {
            var reference = new CisAgentWorkReference("fixture", "unit-protocol", "CIS-0001", "WORK-090", "WORK-090", executionId, "fixture-attempt", parent);
            _executions[executionId] = (reference, operation, false);
            return new(required, "admitted", "controlled unit protocol admission", reference, null, [])
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
        {
            BeforeStorageReservation?.Invoke(_executions[reference.ExecutionId].Operation, reference, operationId);
            if (FailRootFinalizationReservation && reference.ParentExecutionId is null
                && operationId.StartsWith("evidence-mutation/", StringComparison.Ordinal) && maximumGrowthBytes > 0)
                return new(new("held", true, Storage().Storage, ["Controlled result-finalization storage admission failure."]), null, 0);
            var key = reference.ExecutionId + "/" + operationId;
            var paths = (claims ?? []).Select(claim => Path.GetFullPath(claim.Path)).ToArray();
            var current = _operations.GetValueOrDefault(key);
            var combined = (current?.Paths ?? []).Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            static long Bytes(string path) => File.Exists(path) ? new FileInfo(path).Length : Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;
            var initial = current?.InitialBytes ?? 0;
            initial += paths.Where(path => !(current?.Paths.Contains(path, StringComparer.OrdinalIgnoreCase) ?? false)).Sum(Bytes);
            var operation = new CisOwnedStorageOperation(combined, initial, combined.Sum(Bytes), current?.MaximumGrowthBytes ?? maximumGrowthBytes);
            _operations[key] = operation;
            return new(Storage(), key, operation.MaximumGrowthBytes) { Operation = operation };
        }
        public CisOwnedStorageResult Settle(string repositoryPath, CisAgentWorkReference reference, string reservationId,
            bool safelyStopped, IReadOnlyList<string>? requiredPaths = null, IReadOnlyList<string>? supersededRequiredPaths = null)
        { Assert.True(safelyStopped); return Storage(); }
        private sealed class Scope(UnitInvocationAuthority owner, CisAgentWorkReference reference, int maximumSeconds) : ICisNativeWorkScope
        {
            private bool uncertain, finishAttempted;
            public string AuthorityPath => owner.Repository;
            public string Actor => "Controlled unit controller";
            public CisAgentWorkReference Reference => reference;
            public int MaximumSeconds => maximumSeconds;
            public TimeSpan Remaining => TimeSpan.FromSeconds(maximumSeconds);
            public CisNativeWorkAdmission AdmitChild(string executionId, string operation, int seconds)
                => owner.Admit(executionId, seconds, reference.ExecutionId, operation);
            public void Launch() => owner.Launched++;
            public void Started(int processId, string processStartedAtUtc, CisNativeWorkContainment? containment = null) { }
            public void Returned(bool safelyStopped) => Assert.True(safelyStopped);
            public void MarkUncertain(string reason) { uncertain = true; owner.MarkedUncertain++; }
            public CisAgentWorkResult Finish(string status, string? summary, IReadOnlyList<string>? evidence)
            {
                finishAttempted = true;
                var item = owner._executions[reference.ExecutionId];
                if (uncertain) return new("held", null, ["Controlled finalization remains uncertain."], false);
                if (!owner.Reconciled && owner.FailFinish?.Invoke(item.Operation, reference.ParentExecutionId) == true)
                {
                    owner.SettlementFailures++;
                    return new("busy", null, ["Controlled final settlement persistence failure."], false);
                }
                owner._executions[reference.ExecutionId] = (item.Reference, item.Operation, true);
                // A next-operation hold does not change this successful settlement status.
                return owner.Control("settled");
            }
            public void Dispose()
            {
                if (!finishAttempted) Finish("preparation-ended", "Controlled implicit scope disposal", []);
            }
        }
    }
}
