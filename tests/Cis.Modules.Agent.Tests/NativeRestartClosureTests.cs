using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("prepare-restart-closure")]
    [InlineData("verify-restart-closure")]
    public void NativeRestartClosure_LegacyQualificationPreservesEveryRetainedFileAndAllowance(string operation)
    {
        using var fixture = new NativeRestartClosureFixture();
        var before = fixture.Status();
        var files = fixture.Snapshot();
        var result = fixture.Qualify(operation);

        AssertRestartClosureHeld(result, operation, before, fixture.RootId);
        Assert.Equal(2, result.NativeRestartClosure!.ExecutionCount);
        Assert.Equal(before.Task, result.Work!.Task);
        Assert.Equal(before.Change, result.Work.Change);
        Assert.True(result.Work.Task!.ReservedMilliseconds > 0);
        Assert.Equal(1, result.Work.Task.UsedAttempts);
        Assert.Equal(before.LogicalAttemptId, result.Work.LogicalAttemptId);
        Assert.Equal(before.Executions, result.Work.Executions);
        Assert.Null(result.Work.Executions.Single(item => item.ExecutionId == fixture.ChildId)
            .NativeContainment!.WindowsObjectPath);
        fixture.AssertUnchanged(files);

        // A later clock reading, a matching hostname and assertions in contextual
        // evidence do not authenticate original custody or create a challenge.
        fixture.Clock.Advance(90);
        var repeated = fixture.Qualify(operation, evidence:
        [
            "fixture:matching-hostname:" + Environment.MachineName,
            "fixture:claimed-restart-and-stopped-pids",
            "fixture:copied-machine-seal-or-replayed-receipt"
        ]);
        AssertRestartClosureHeld(repeated, operation, before, fixture.RootId);
        Assert.Equal(result.NativeRestartClosure.TreeDigest, repeated.NativeRestartClosure!.TreeDigest);
        Assert.Equal(before.Task, repeated.Work!.Task);
        Assert.False(CisNativeWorkSettlement.Complete(repeated, fixture.Reference));
        fixture.AssertUnchanged(files);
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("child-only")]
    [InlineData("wrong-task")]
    [InlineData("wrong-change")]
    [InlineData("stale-revision")]
    [InlineData("unauthorized-actor")]
    public void NativeRestartClosure_RequiresItsExactRetainedRootOwnerRevisionAndController(string invalid)
    {
        using var fixture = new NativeRestartClosureFixture();
        var files = fixture.Snapshot();
        var request = new CisAgentWorkDispositionRequest("prepare-restart-closure", fixture.Status().Revision)
        { ExecutionId = fixture.RootId };
        var task = "WORK-090";
        var change = "CIS-0001";
        var actor = WorkActor;
        switch (invalid)
        {
            case "missing-root": request = request with { ExecutionId = "native/unrecorded-root" }; break;
            case "child-only": request = request with { ExecutionId = fixture.ChildId }; break;
            case "wrong-task": task = "WORK-091"; break;
            case "wrong-change": change = "CIS-0002"; break;
            case "stale-revision": request = request with { ExpectedRevision = fixture.Reference.AdoptionId + ":0" }; break;
            case "unauthorized-actor": actor = "Unapproved fixture controller"; break;
            default: throw new InvalidOperationException(invalid);
        }

        var result = fixture.Service.DisposeWork(fixture.Repository.Path, change, task,
            WriteWorkInput(fixture.Repository, "invalid-restart-qualification.json", request), actor,
            "Inspect only the exact retained native execution.");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.NativeRestartClosure);
        fixture.AssertUnchanged(files);
    }

    [Theory]
    [InlineData("non-native-root")]
    [InlineData("non-native-child")]
    [InlineData("mixed-owner")]
    [InlineData("mixed-task")]
    [InlineData("mixed-attempt")]
    [InlineData("mixed-root")]
    [InlineData("disconnected-child")]
    public void NativeRestartClosure_RejectsIncompleteOrMixedRetainedTreesWithoutRepairingThem(string invalid)
    {
        using var fixture = new NativeRestartClosureFixture();
        fixture.EditLedger(ledger =>
        {
            var executions = ledger["changes"]!["CIS-0001"]!["executions"]!;
            var child = executions[fixture.ChildId]!;
            switch (invalid)
            {
                case "non-native-root": executions[fixture.RootId]!["stage"]!["nativeOperation"] = null; break;
                case "non-native-child": child["stage"]!["nativeOperation"] = null; break;
                case "mixed-owner": child["reference"]!["ownerTaskId"] = "WORK-091"; break;
                case "mixed-task": child["reference"]!["taskId"] = "WORK-091"; break;
                case "mixed-attempt": child["reference"]!["logicalAttemptId"] = "WORK-090:999"; break;
                case "mixed-root": child["rootId"] = fixture.ChildId; break;
                case "disconnected-child": child["reference"]!["parentExecutionId"] = null; break;
                default: throw new InvalidOperationException(invalid);
            }
        });
        var files = fixture.Snapshot();

        var result = fixture.Qualify("prepare-restart-closure", revision: fixture.Revision);

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.NativeRestartClosure);
        fixture.AssertUnchanged(files);
    }

    [Theory]
    [InlineData("challengeId", "copied-or-replayed-challenge")]
    [InlineData("machineProof", "new-machine-seal")]
    [InlineData("bootIdentifier", "00000000-0000-0000-0000-000000000001")]
    public void NativeRestartClosure_RejectsCallerSuppliedProofFields(string property, string value)
    {
        using var fixture = new NativeRestartClosureFixture();
        var request = JsonSerializer.SerializeToNode(new CisAgentWorkDispositionRequest("verify-restart-closure", fixture.Revision)
        { ExecutionId = fixture.RootId }, JsonOptions)!.AsObject();
        request[property] = value;
        var input = WriteWorkInput(fixture.Repository, "untrusted-restart-proof.json", request);
        var files = fixture.Snapshot();

        var result = fixture.Service.DisposeWork(fixture.Repository.Path, "CIS-0001", "WORK-090", input,
            WorkActor, "Caller-provided proof cannot establish historical custody.");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.NativeRestartClosure);
        fixture.AssertUnchanged(files);
    }

    [Theory]
    [InlineData("hypothesis")]
    [InlineData("nextAction")]
    [InlineData("correctionRelationshipId")]
    [InlineData("historicalRunIds")]
    public void NativeRestartClosure_DoesNotAcceptContinuationOrCorrectionAuthority(string property)
    {
        using var fixture = new NativeRestartClosureFixture();
        var request = JsonSerializer.SerializeToNode(new CisAgentWorkDispositionRequest("prepare-restart-closure", fixture.Revision)
        { ExecutionId = fixture.RootId }, JsonOptions)!.AsObject();
        request[property] = property == "historicalRunIds" ? new JsonArray("fixture:historical-run") : JsonValue.Create("fixture:continuation");
        var input = WriteWorkInput(fixture.Repository, "restart-cannot-continue.json", request);
        var files = fixture.Snapshot();

        var result = fixture.Service.DisposeWork(fixture.Repository.Path, "CIS-0001", "WORK-090", input,
            WorkActor, "Qualification grants no continuation or accounting correction.");

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.NativeRestartClosure);
        fixture.AssertUnchanged(files);
    }

    [Theory]
    [InlineData("prepare-restart-closure", "human")]
    [InlineData("prepare-restart-closure", "json")]
    [InlineData("prepare-restart-closure", "agent")]
    [InlineData("verify-restart-closure", "human")]
    [InlineData("verify-restart-closure", "json")]
    [InlineData("verify-restart-closure", "agent")]
    public void NativeRestartClosure_CommandReportsTheQualificationHoldInEveryFormat(string operation, string format)
    {
        using var fixture = new NativeRestartClosureFixture();
        var provider = new FakeProvider();
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule())
            .AddModule(new WorkControlFixtureModule(fixture.Service, provider)).Build();
        var input = WriteWorkInput(fixture.Repository, "restart-command.json",
            new CisAgentWorkDispositionRequest(operation, fixture.Revision) { ExecutionId = fixture.RootId });
        var files = fixture.Snapshot();

        var result = InvokeWorkCommand(application, "agent", "work", "disposition", "CIS-0001", "WORK-090",
            "--input", input, "--actor", WorkActor, "--reason", "Inspect restart qualification without changing retained work.",
            "--repo", fixture.Repository.Path, "--format", format);

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("not-qualified", result.Output, StringComparison.Ordinal);
        Assert.Contains(fixture.RootId, result.Output, StringComparison.Ordinal);
        Assert.Contains(format == "human" ? "original-host custody" : "original-host-custody-unproven",
            result.Output, StringComparison.Ordinal);
        if (format == "json")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.False(json.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal(operation, json.RootElement.GetProperty("nativeRestartClosure").GetProperty("operation").GetString());
        }
        fixture.AssertUnchanged(files);
    }

    private static void AssertRestartClosureHeld(CisAgentWorkResult result, string operation,
        CisAgentWorkView before, string rootId)
    {
        Assert.Equal(4, result.ExitCode);
        Assert.Equal("held", result.Status);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        var qualification = Assert.IsType<CisNativeRestartClosureQualification>(result.NativeRestartClosure);
        Assert.Equal(operation, qualification.Operation);
        Assert.Equal("not-qualified", qualification.State);
        Assert.Equal(rootId, qualification.ExecutionId);
        Assert.Equal(rootId, qualification.RootExecutionId);
        Assert.Equal(before.Revision, qualification.Revision);
        Assert.Equal(before.AdoptionId, qualification.AdoptionId);
        Assert.Matches("^[0-9a-f]{64}$", qualification.TreeDigest);
        Assert.Contains("original-host-custody-unproven", qualification.Gaps);
        Assert.Contains("anti-copy-rollback-unqualified", qualification.Gaps);
        Assert.Contains("restart-verifier-unqualified", qualification.Gaps);
        Assert.Equal(operation == "verify-restart-closure", qualification.Gaps.Contains("challenge-not-prepared"));
    }

    private sealed class NativeRestartClosureFixture : IDisposable
    {
        public AgentRepository Repository { get; } = AgentRepository.Create();
        public WorkControlClock Clock { get; } = new();
        public AgentService Service { get; }
        public CisAgentWorkReference Reference { get; }
        public string RootId => Reference.ExecutionId;
        public string ChildId { get; } = "native/restart-fixture-child";
        public string Revision { get; }

        public NativeRestartClosureFixture()
        {
            AdoptWorkControl(Repository, secondTask: true);
            Service = new AgentService(new CisRepositoryContextResolver(), clock: Clock.Read);
            var allocation = WorkAllocation(180, 180, 30, ["WORK-090", "WORK-091"]) with
            {
                Storage = new(16 * 1024 * 1024, [new(Repository.Path, ".cis/local")])
                { RestorationBytes = 1024 * 1024 }
            };
            Assert.Equal(0, Service.AllocateWork(Repository.Path, "CIS-0001",
                WriteWorkInput(Repository, "restart-allocation.json", allocation), WorkActor,
                "Retain the original finite native recovery allocation.").ExitCode);
            var native = (ICisNativeWorkController)Service;
            var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: Repository.Path, Actor: WorkActor);
            var admitted = native.Admit(new(Repository.Path, "native/restart-fixture-root", "unit-verification", 60, association));
            Assert.True(admitted.Allowed, string.Join("; ", admitted.Errors));
            using var root = admitted.Scope!;
            Reference = root.Reference;
            var storage = (ICisOwnedStorageController)Service;
            var evidence = Path.GetFullPath(Path.Combine(Repository.Path, ".cis/local/testing/restart-fixture/driver.log"));
            var reservation = storage.Reserve(Repository.Path, root.Reference, "retained-native-output", 4096,
                [new(evidence)], actor: WorkActor);
            Assert.True(reservation.Allowed, string.Join("; ", reservation.Result.Errors));
            Repository.Write(".cis/local/testing/restart-fixture/driver.log", "Native discovery stopped without an assessed case result.");
            Repository.Write(".cis/local/testing/restart-fixture/state.json", "{\"unit\":null,\"pending\":1}");
            Repository.Write(".cis/local/testing/profiles/restart-fixture.json", "{\"timeoutSeconds\":30,\"expectedCases\":2}");
            var childAdmission = root.AdmitChild(ChildId, "unit-native-driver", 30);
            Assert.True(childAdmission.Allowed, string.Join("; ", childAdmission.Errors));
            using var child = childAdmission.Scope!;
            child.Launch();
            using (var process = Process.GetCurrentProcess())
                child.Started(process.Id, process.StartTime.ToUniversalTime().ToString("O"));
            child.MarkUncertain("Synthetic historical driver has no retained closure proof.");
            Assert.Equal(4, child.Finish("interrupted", "Retain the interrupted native driver.", ["fixture:retained-driver-log"]).ExitCode);
            root.MarkUncertain("Retain the original root and its owned child.");
            Assert.Equal(4, root.Finish("interrupted", "No native test result was assessed.", ["fixture:missing-native-result"]).ExitCode);
            // Reproduce a historical three-field receipt in this isolated fixture.
            // It is deliberately not an actual job assignment or closure proof.
            EditLedger(ledger => ledger["changes"]!["CIS-0001"]!["executions"]![ChildId]!["nativeContainment"] =
                JsonSerializer.SerializeToNode(new CisNativeWorkContainment(CisNativeWorkContainment.WindowsOuterJob,
                    "Local\\cis-native-fixture-legacy", 1), JsonOptions));
            Repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md",
                "---\ntask_status: Blocked\ntargets: [\"agent-fixture\"]\n---\n# Retained native work\nOriginal work remains held.\n");
            Revision = Status().Revision;
        }

        public CisAgentWorkView Status() => Service.WorkStatus(Repository.Path, "CIS-0001", "WORK-090").Work!;

        public CisAgentWorkResult Qualify(string operation, IReadOnlyList<string>? evidence = null, string? revision = null)
            => Service.DisposeWork(Repository.Path, "CIS-0001", "WORK-090",
                WriteWorkInput(Repository, "restart-qualification.json", new CisAgentWorkDispositionRequest(operation, revision ?? Revision)
                { ExecutionId = RootId, Evidence = evidence ?? [] }), WorkActor,
                "Inspect historical restart closure qualification without authorizing a restart or settlement.");

        public void EditLedger(Action<JsonNode> edit)
        {
            var path = Path.Combine(Repository.Path, CisAgentWorkPaths.Ledger);
            var ledger = JsonNode.Parse(File.ReadAllBytes(path))!;
            edit(ledger);
            File.WriteAllText(path, ledger.ToJsonString(JsonOptions));
        }

        public IReadOnlyDictionary<string, byte[]> Snapshot()
            => Directory.EnumerateFiles(Repository.Path, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(Repository.Path, path).Replace('\\', '/')
                    .StartsWith(".cis/local/work-test-input/", StringComparison.Ordinal))
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

        public void AssertUnchanged(IReadOnlyDictionary<string, byte[]> before)
        {
            var after = Snapshot();
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach (var file in before) Assert.Equal(file.Value, after[file.Key]);
        }

        public void Dispose() => Repository.Dispose();
    }
}
