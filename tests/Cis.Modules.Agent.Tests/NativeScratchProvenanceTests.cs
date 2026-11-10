using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

[CollectionDefinition("Native scratch environment", DisableParallelization = true)]
public sealed class NativeScratchEnvironmentCollection { }

[Collection("Native scratch environment")]
public sealed class NativeScratchProvenanceTests
{
    [Fact]
    public void WorkLedger_NativeScratchProvenance_SurvivesDifferentTemporaryContextAndVerifiedSettlement()
    {
        using var f = new ScratchFixture();
        using var environment = new TemporaryContext(f.FirstTemporary);
        var reference = f.Admit();
        var reservation = f.Reserve(reference);
        AssertAdmission(f, reference);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, reference).ExitCode);
        f.Time = f.Time.AddSeconds(2);
        Assert.NotEqual(0, f.Ledger.Settle(f.Context, reference, "Interrupted", false).ExitCode);
        var before = File.ReadAllBytes(f.LedgerPath);

        environment.Use(f.SecondTemporary);
        var reopened = new AgentWorkLedger(() => f.Time);
        var inventory = reopened.InspectStorage(f.Context, reference.ChangeId, reference.TaskId);
        Assert.True(inventory.Allowed, string.Join("; ", inventory.Errors));
        Assert.Contains(f.Scratch, inventory.Storage!.OwnedPaths);
        Assert.True(inventory.Storage.ReservedBytes >= reservation.MaximumGrowthBytes);
        Assert.Equal(before, File.ReadAllBytes(f.LedgerPath));
        Assert.False(reopened.SettleStorage(f.Context, reference, reservation.ReservationId!, false, null).Allowed);
        Assert.Equal(before, File.ReadAllBytes(f.LedgerPath));

        // The trusted controller supplies the same stopped-owner proof required by
        // the existing ledger reconciliation path; a TEMP change grants no proof.
        var reconciled = reopened.Settle(f.Context, reference, "Interrupted", true,
            "The exact original native owner was verified stopped.", ["fixture:verified-original-stop"], conservativeRecovery: true);
        Assert.Equal(0, reconciled.ExitCode);
        Assert.Equal(60_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(1, reconciled.Work.Task.UsedAttempts);
        Assert.Equal(0, reconciled.Work.Task.ReservedMilliseconds);
        Assert.Equal(0, reopened.InspectStorage(f.Context, reference.ChangeId, reference.TaskId).Storage!.ReservedBytes);
        Assert.Contains(f.Scratch, reopened.InspectStorage(f.Context, reference.ChangeId, reference.TaskId).Storage!.ProtectedPaths);
        Assert.Empty(f.Storage()["reservations"]!.AsObject());
        AssertAdmission(f, reference);

        environment.Use(f.ThirdTemporary);
        var after = File.ReadAllBytes(f.LedgerPath);
        Assert.True(new AgentWorkLedger(() => f.Time).InspectStorage(f.Context, reference.ChangeId, reference.TaskId).Allowed);
        Assert.Equal(after, File.ReadAllBytes(f.LedgerPath));
        Assert.Equal("original diagnostic", File.ReadAllText(Path.Combine(f.Scratch, "diagnostic.txt")));
    }

    [Fact]
    public void WorkLedger_NativeScratchProvenance_LegacyExactAdmissionIsReadOnlyUntilExistingSettlementPersistsIt()
    {
        using var f = new ScratchFixture();
        using var environment = new TemporaryContext(f.FirstTemporary);
        var reference = f.Admit();
        var reservation = f.Reserve(reference);
        var state = f.State();
        state["changes"]![reference.ChangeId]!["storage"]!["claims"]![f.Scratch]!.AsObject().Remove("nativeScratchAdmission");
        f.WriteState(state);
        var legacy = File.ReadAllBytes(f.LedgerPath);

        environment.Use(f.SecondTemporary);
        var reopened = new AgentWorkLedger(() => f.Time);
        var inventory = reopened.InspectStorage(f.Context, reference.ChangeId, reference.TaskId);
        Assert.True(inventory.Allowed, string.Join("; ", inventory.Errors));
        Assert.Equal(legacy, File.ReadAllBytes(f.LedgerPath));
        Assert.Null(f.Storage()["claims"]![f.Scratch]!["nativeScratchAdmission"]);
        Assert.True(inventory.Storage!.ReservedBytes > 0);

        Assert.True(reopened.SettleStorage(f.Context, reference, reservation.ReservationId!, true, [f.Scratch]).Allowed);
        Assert.Equal(0, reopened.Settle(f.Context, reference, "native-finished", true).ExitCode);
        AssertAdmission(f, reference);
        Assert.Empty(f.Storage()["reservations"]!.AsObject());
        environment.Use(f.ThirdTemporary);
        Assert.True(new AgentWorkLedger(() => f.Time).InspectStorage(f.Context, reference.ChangeId, reference.TaskId).Allowed);
    }

    [Theory]
    [InlineData("missing-reservation")]
    [InlineData("missing-exact-path")]
    [InlineData("missing-operation-path")]
    [InlineData("different-owner")]
    [InlineData("non-native-execution")]
    public void WorkLedger_NativeScratchProvenance_InsufficientLegacyEvidenceHoldsWithoutChangingHistory(string missing)
    {
        using var f = new ScratchFixture();
        using var environment = new TemporaryContext(f.FirstTemporary);
        var reference = f.Admit();
        var admission = f.Reserve(reference);
        var state = f.State();
        var change = state["changes"]![reference.ChangeId]!;
        var storage = change["storage"]!;
        storage["claims"]![f.Scratch]!.AsObject().Remove("nativeScratchAdmission");
        var reservation = storage["reservations"]![admission.ReservationId!]!;
        switch (missing)
        {
            case "missing-reservation": storage["reservations"]!.AsObject().Remove(admission.ReservationId!); break;
            case "missing-exact-path": reservation["paths"] = new JsonArray(); break;
            case "missing-operation-path": reservation["operationPaths"] = new JsonArray(); break;
            case "different-owner": storage["claims"]![f.Scratch]!["ownerTaskId"] = "WORK-B"; break;
            case "non-native-execution": change["executions"]![reference.ExecutionId]!["stage"]!["nativeOperation"] = null; break;
        }
        f.WriteState(state);
        var before = File.ReadAllBytes(f.LedgerPath);
        environment.Use(f.SecondTemporary);

        var reopened = new AgentWorkLedger(() => f.Time);
        var held = reopened.InspectStorage(f.Context, reference.ChangeId, reference.TaskId);
        Assert.False(held.Allowed);
        Assert.Contains(held.Errors, error => error.Contains("original native admission provenance", StringComparison.Ordinal));
        Assert.False(reopened.SettleStorage(f.Context, reference, admission.ReservationId!, true, [f.Scratch]).Allowed);
        Assert.Equal(before, File.ReadAllBytes(f.LedgerPath));

        // Even a matching current TEMP cannot manufacture missing historical proof.
        environment.Use(f.FirstTemporary);
        Assert.False(reopened.InspectStorage(f.Context, reference.ChangeId, reference.TaskId).Allowed);
        Assert.Equal(before, File.ReadAllBytes(f.LedgerPath));
    }

    [Theory]
    [InlineData("other-temporary")]
    [InlineData("unsupported-name")]
    [InlineData("entire-temporary")]
    [InlineData("nested-temporary")]
    [InlineData("not-scratch")]
    public void WorkLedger_NativeScratchProvenance_UnrelatedOutsideRootAdmissionCannotAcquireAClaim(string invalid)
    {
        using var f = new ScratchFixture();
        using var environment = new TemporaryContext(f.FirstTemporary);
        var reference = f.Admit();
        var path = invalid switch
        {
            "other-temporary" => Path.Combine(f.SecondTemporary, Path.GetFileName(f.Scratch)),
            "unsupported-name" => Path.Combine(f.FirstTemporary, "user-data"),
            "entire-temporary" => f.FirstTemporary,
            "nested-temporary" => Path.Combine(f.FirstTemporary, "nested", Path.GetFileName(f.Scratch)),
            _ => f.Scratch,
        };
        var before = File.ReadAllBytes(f.LedgerPath);
        var held = f.Ledger.ReserveStorage(f.Context, reference, "unrelated", 1024,
            [new(path, Scratch: invalid != "not-scratch")], false, false, "Controller");
        Assert.False(held.Allowed);
        Assert.Equal(before, File.ReadAllBytes(f.LedgerPath));
        Assert.Empty(f.Storage()["claims"]!.AsObject());
        Assert.Empty(f.Storage()["reservations"]!.AsObject());
    }

    [Theory]
    [InlineData("path")]
    [InlineData("temporary-root")]
    [InlineData("execution")]
    public void WorkLedger_NativeScratchProvenance_CorruptRetainedBindingCannotReleaseReservations(string invalid)
    {
        using var f = new ScratchFixture();
        using var environment = new TemporaryContext(f.FirstTemporary);
        var reference = f.Admit();
        var reservation = f.Reserve(reference);
        var state = f.State();
        var provenance = state["changes"]![reference.ChangeId]!["storage"]!["claims"]![f.Scratch]!["nativeScratchAdmission"]!;
        switch (invalid)
        {
            case "path": provenance["path"] = Path.Combine(f.FirstTemporary, "cis-mutation-" + Guid.NewGuid().ToString("N")); break;
            case "temporary-root": provenance["temporaryRoot"] = f.SecondTemporary; break;
            case "execution": provenance["reference"]!["executionId"] = "native/unrecorded"; break;
        }
        f.WriteState(state);
        var before = File.ReadAllBytes(f.LedgerPath);
        environment.Use(f.SecondTemporary);

        var held = new AgentWorkLedger(() => f.Time).InspectStorage(f.Context, reference.ChangeId, reference.TaskId);
        Assert.False(held.Allowed);
        Assert.Contains(held.Errors, error => error.Contains("admission provenance does not match", StringComparison.Ordinal));
        Assert.False(f.Ledger.SettleStorage(f.Context, reference, reservation.ReservationId!, true, [f.Scratch]).Allowed);
        Assert.Equal(before, File.ReadAllBytes(f.LedgerPath));
    }

    private static void AssertAdmission(ScratchFixture f, CisAgentWorkReference reference)
    {
        var claim = f.Storage()["claims"]![f.Scratch]!;
        var admission = claim["nativeScratchAdmission"]!;
        Assert.Equal(f.Scratch, admission["path"]!.GetValue<string>());
        Assert.Equal(f.FirstTemporary, admission["temporaryRoot"]!.GetValue<string>());
        Assert.Equal(reference.ExecutionId, admission["reference"]!["executionId"]!.GetValue<string>());
        Assert.Equal(reference.AdoptionId, admission["reference"]!["adoptionId"]!.GetValue<string>());
        Assert.Equal(reference.OwnerTaskId, claim["ownerTaskId"]!.GetValue<string>());
    }

    private sealed class ScratchFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cis-native-scratch-provenance-tests", Guid.NewGuid().ToString("N"));
        public DateTimeOffset Time { get; set; } = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        public CisRepositoryContext Context { get; }
        public AgentWorkLedger Ledger { get; }
        public string FirstTemporary => Path.Combine(root, "admitting-temp");
        public string SecondTemporary => Path.Combine(root, "inspecting-temp");
        public string ThirdTemporary => Path.Combine(root, "later-temp");
        public string Scratch { get; }
        public string LedgerPath => Path.Combine(root, CisAgentWorkPaths.Ledger);
        public ScratchFixture()
        {
            foreach (var path in new[] { FirstTemporary, SecondTemporary, ThirdTemporary }) Directory.CreateDirectory(path);
            Context = new(root, "fixture-authority", "docs", Path.Combine(root, "docs"), Path.Combine(root, "docs/catalog.yml"));
            Ledger = new(() => Time);
            Scratch = Path.Combine(FirstTemporary, "cis-mutation-" + Guid.NewGuid().ToString("N"));
            var first = new CisAgentWorkIdentity(Context.RepositoryId, "CIS-0001", "WORK-A", [], ["approved fixture outcome"]);
            var second = first with { TaskId = "WORK-B" };
            var request = new CisAgentWorkAllocationRequest(600, 5, 0, [new(first.TaskId, 180, 3), new(second.TaskId, 180, 2)],
                "approved fixture time", "approved finite fixture storage", AcknowledgeHistoricalUnknown: true)
            { Controllers = ["Controller"], Storage = new(4 * 1024 * 1024, [new(root, ".cis/local")]) };
            Assert.Equal(0, Ledger.Allocate(Context, first.ChangeId, request, [first, second], "Controller", "Admit only exact bounded native scratch.").ExitCode);
        }
        public CisAgentWorkReference Admit()
        {
            var identity = new CisAgentWorkIdentity(Context.RepositoryId, "CIS-0001", "WORK-A", [], ["approved fixture outcome"]);
            var stage = new CisAgentWorkStage("native-testing", "fixture-profile", "unit", Context.RepositoryId) { NativeOperation = "incremental-unit" };
            var admitted = Ledger.Admit(Context, identity, "native/original", 60, 180, stage: stage, controller: "Controller");
            Assert.True(admitted.Admitted, string.Join("; ", admitted.Result.Errors));
            return admitted.Reference!;
        }
        public CisOwnedStorageAdmission Reserve(CisAgentWorkReference reference)
        {
            var admission = Ledger.ReserveStorage(Context, reference, "native", 4096, [new(Scratch, Scratch: true)], false, false, "Controller");
            Assert.True(admission.Allowed, string.Join("; ", admission.Result.Errors));
            Directory.CreateDirectory(Scratch);
            File.WriteAllText(Path.Combine(Scratch, "diagnostic.txt"), "original diagnostic");
            return admission;
        }
        public JsonNode State() => JsonNode.Parse(File.ReadAllBytes(LedgerPath))!;
        public JsonNode Storage() => State()["changes"]!["CIS-0001"]!["storage"]!;
        public void WriteState(JsonNode state) => File.WriteAllText(LedgerPath, state.ToJsonString());
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Path.Combine(Path.GetTempPath(), "cis-native-scratch-provenance-tests"), root))
                throw new InvalidOperationException("The fixture temporary context was not restored.");
            Directory.Delete(root, true);
        }
    }

    private sealed class TemporaryContext : IDisposable
    {
        private readonly Dictionary<string, string?> previous = new[] { "TEMP", "TMP", "TMPDIR" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        public TemporaryContext(string path)
        {
            try { Use(path); }
            catch { Dispose(); throw; }
        }
        public void Use(string path)
        {
            foreach (var name in previous.Keys) Environment.SetEnvironmentVariable(name, path);
            Assert.Equal(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
        }
        public void Dispose()
        {
            foreach (var (name, value) in previous) Environment.SetEnvironmentVariable(name, value);
        }
    }
}
