using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("Blocked")]
    [InlineData("Cancelled")]
    [InlineData("retired-task")]
    [InlineData("unapproved-plan")]
    public void NativeStorageInspection_RetainedOwnershipSurvivesLossOfLaunchEligibility(string change)
    {
        using var fixture = new NativeStorageInspectionFixture();
        var before = fixture.Storage.Inspect(fixture.Repository.Path, fixture.Association);
        Assert.True(before.Allowed, string.Join("; ", before.Errors));
        fixture.RemoveLaunchEligibility(change);
        var files = fixture.Snapshot();
        var work = fixture.Service.WorkStatus(fixture.Repository.Path, "CIS-0001", "WORK-090").Work!;

        var inspected = fixture.Storage.Inspect(fixture.Repository.Path, fixture.Association);

        Assert.True(inspected.Allowed, string.Join("; ", inspected.Errors));
        Assert.Equal("current", inspected.Status);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before.Storage, JsonOptions),
            System.Text.Json.JsonSerializer.Serialize(inspected.Storage, JsonOptions));
        Assert.True(inspected.Storage!.ReservedBytes >= 4096);
        Assert.Equal(16 * 1024 * 1024, inspected.Storage.MaximumBytes);
        Assert.Equal("WORK-090", inspected.Storage.OwnerTaskId);
        fixture.AssertUnchanged(files);
        var rejected = fixture.Native.Admit(new(fixture.Repository.Path, "native/must-not-launch", "unit-verification", 30,
            fixture.Association));
        Assert.False(rejected.Allowed);
        Assert.NotEmpty(rejected.Errors);
        Assert.Equal(work.Revision, fixture.Service.WorkStatus(fixture.Repository.Path, "CIS-0001", "WORK-090").Work!.Revision);
        fixture.AssertUnchanged(files);
    }

    [Fact]
    public void NativeStorageInspection_CanFollowItsSettledRetainedParentWithoutExecutionAuthority()
    {
        using var fixture = new NativeStorageInspectionFixture();
        Assert.Equal(0, fixture.Scope.Finish("native-finished", "Preparation ended without a native launch.",
            ["fixture:stopped-preparation"]).ExitCode);
        fixture.RemoveLaunchEligibility("Blocked");
        var files = fixture.Snapshot();
        var association = new CisNativeWorkOptions(ParentExecutionId: fixture.Scope.Reference.ExecutionId,
            AuthorityPath: fixture.Repository.Path);

        var inspected = fixture.Storage.Inspect(fixture.Repository.Path, association);

        Assert.True(inspected.Allowed, string.Join("; ", inspected.Errors));
        Assert.Equal("WORK-090", inspected.Storage!.OwnerTaskId);
        Assert.True(inspected.Storage.ReservedBytes > 0);
        Assert.False(fixture.Native.Inspect(fixture.Repository.Path, association).Allowed);
        fixture.AssertUnchanged(files);
    }

    [Theory]
    [InlineData("unallocated-task")]
    [InlineData("unrecorded-parent")]
    [InlineData("unsafe-claim")]
    public void NativeStorageInspection_DoesNotReplaceMissingOrInvalidOwnershipWithZeroUsage(string invalid)
    {
        using var fixture = new NativeStorageInspectionFixture();
        fixture.RemoveLaunchEligibility("Blocked");
        var association = fixture.Association;
        if (invalid == "unallocated-task") association = association with { TaskId = "WORK-999" };
        else if (invalid == "unrecorded-parent") association = association with { ParentExecutionId = "native/unrecorded-parent" };
        else
        {
            var ledgerPath = Path.Combine(fixture.Repository.Path, CisAgentWorkPaths.Ledger);
            var ledger = JsonNode.Parse(File.ReadAllBytes(ledgerPath))!;
            ledger["changes"]!["CIS-0001"]!["storage"]!["claims"]![fixture.ReportPath]!["path"] =
                Path.GetFullPath(Path.Combine(fixture.Repository.Path, "not-an-owned-output"));
            File.WriteAllText(ledgerPath, ledger.ToJsonString(JsonOptions));
        }
        var files = fixture.Snapshot();

        var inspected = fixture.Storage.Inspect(fixture.Repository.Path, association);

        Assert.False(inspected.Allowed);
        Assert.Equal("unknown", inspected.Status);
        Assert.Null(inspected.Storage);
        Assert.NotEmpty(inspected.Errors);
        fixture.AssertUnchanged(files);
    }

    [Fact]
    public void NativeStorageInspection_BlockingDoesNotPreventSettlementOfAnExactExistingStorageGrant()
    {
        using var fixture = new NativeStorageInspectionFixture();
        fixture.RemoveLaunchEligibility("Blocked");

        var settled = fixture.Storage.Settle(fixture.Repository.Path, fixture.Scope.Reference,
            fixture.Reservation.ReservationId!, true, [fixture.ReportPath]);

        Assert.True(settled.Allowed, string.Join("; ", settled.Errors));
        Assert.Equal(0, fixture.Scope.Finish("native-finished", "The existing preparation and report are safely stopped.",
            ["fixture:retained-report"]).ExitCode);
        var inspected = fixture.Storage.Inspect(fixture.Repository.Path, fixture.Association);
        Assert.True(inspected.Allowed, string.Join("; ", inspected.Errors));
        Assert.Equal(0, inspected.Storage!.ReservedBytes);
        Assert.Contains(fixture.ReportPath, inspected.Storage.ProtectedPaths);
        Assert.Equal("retained diagnostic", File.ReadAllText(fixture.ReportPath));
        Assert.False(fixture.Native.Admit(new(fixture.Repository.Path, "native/blocked-new-work", "unit-verification", 30,
            fixture.Association)).Allowed);
    }

    private sealed class NativeStorageInspectionFixture : IDisposable
    {
        public AgentRepository Repository { get; } = AgentRepository.Create();
        public AgentService Service { get; }
        public ICisOwnedStorageController Storage => Service;
        public ICisNativeWorkController Native => Service;
        public ICisNativeWorkScope Scope { get; }
        public CisOwnedStorageAdmission Reservation { get; }
        public CisNativeWorkOptions Association { get; }
        public string ReportPath { get; }

        public NativeStorageInspectionFixture()
        {
            AdoptWorkControl(Repository);
            var taskPath = Path.Combine(Repository.Path, "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md");
            File.WriteAllText(taskPath, File.ReadAllText(taskPath).Replace("task_status: Ready", "task_status: InProgress", StringComparison.Ordinal));
            Service = new AgentService(new CisRepositoryContextResolver(), clock: new WorkControlClock().Read);
            var allocation = WorkAllocation(180, 180, 30, ["WORK-090"]) with
            { Storage = new(16 * 1024 * 1024, [new(Repository.Path, ".cis/local")]) };
            Assert.Equal(0, Service.AllocateWork(Repository.Path, "CIS-0001",
                WriteWorkInput(Repository, "storage-inspection-allocation.json", allocation), WorkActor,
                "Keep existing storage observable while execution is blocked.").ExitCode);
            Association = new("CIS-0001", "WORK-090", AuthorityPath: Repository.Path, Actor: WorkActor);
            var admission = Native.Admit(new(Repository.Path, "native/storage-inspection-root", "unit-verification", 60, Association));
            Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
            Scope = admission.Scope!;
            ReportPath = Path.GetFullPath(Path.Combine(Repository.Path, ".cis/local/testing/storage-inspection/report.log"));
            Reservation = Storage.Reserve(Repository.Path, Scope.Reference, "retained-inspection", 4096,
                [new(ReportPath)], actor: WorkActor);
            Assert.True(Reservation.Allowed, string.Join("; ", Reservation.Result.Errors));
            Repository.Write(".cis/local/testing/storage-inspection/report.log", "retained diagnostic");
        }

        public void RemoveLaunchEligibility(string change)
        {
            var task = Path.Combine(Repository.Path, "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md");
            if (change == "retired-task") File.Delete(task);
            else if (change == "unapproved-plan")
            {
                var plan = Path.Combine(Repository.Path, "docs/cis/changes/CIS-0001/plan.md");
                File.WriteAllText(plan, File.ReadAllText(plan).Replace("status: Approved", "status: Draft", StringComparison.Ordinal));
            }
            else File.WriteAllText(task, File.ReadAllText(task).Replace("task_status: InProgress", "task_status: " + change, StringComparison.Ordinal));
        }

        public IReadOnlyDictionary<string, byte[]> Snapshot()
            => Directory.EnumerateFiles(Repository.Path, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

        public void AssertUnchanged(IReadOnlyDictionary<string, byte[]> before)
        {
            var after = Snapshot();
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach (var file in before) Assert.Equal(file.Value, after[file.Key]);
        }

        public void Dispose()
        {
            Scope.Dispose();
            Repository.Dispose();
        }
    }
}
