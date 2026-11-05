using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void WorkLedger_FirstCanonicalIdentityAttachesOnlyOnceToCorrectionOnlyHistory()
    {
        using var f = new Fixture();
        const string relationship = "feature/unbound-instance";
        var first = f.Ledger.BeginCorrection(f.Context, null, null, relationship, "First controller", ["required:first"]);
        Assert.NotNull(first.Round);
        Assert.Equal(0, f.Ledger.CompleteCorrection(f.Context, null, null, relationship, first.Round.Id, "First controller").ExitCode);
        var identity = f.Identity("WORK-A");
        var second = f.Ledger.BeginCorrection(f.Context, identity, null, relationship, "Next controller", ["required:second"]);
        Assert.NotNull(second.Round); Assert.Equal(2, second.Round.Number);
        Assert.Equal(0, f.Ledger.RecordCorrectionStage(f.Context, identity, null, relationship, second.Round.Id,
            "Next controller", "backend", "native-second-stage", "retained-candidate", ["native-second-stage"]).ExitCode);
        Assert.Equal(0, f.Ledger.CompleteCorrection(f.Context, identity, null, relationship, second.Round.Id, "Next controller").ExitCode);
        var bound = f.Ledger.ReadCorrections(f.Context, relationship, expectedIdentity: identity, inspectIdentity: true);
        Assert.True(bound.Work!.CorrectionOnly); Assert.Equal(identity.ChangeId, bound.Work.ChangeId); Assert.Equal(identity.TaskId, bound.Work.TaskId);
        Assert.Equal(new[] { 1, 2 }, bound.Work.Corrections.Select(round => round.Number));
        Assert.Equal(first.Round.Id, bound.Work.Corrections[0].Id);
        var bytes = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        var replacement = f.Identity("WORK-B");
        var read = f.Ledger.ReadCorrections(f.Context, relationship, expectedIdentity: replacement, inspectIdentity: true);
        Assert.Equal("held", read.Status); Assert.Contains("WORK-A", read.Work!.NextAction); Assert.Contains("WORK-B", read.Work.NextAction);
        var rejected = f.Ledger.BeginCorrection(f.Context, replacement, null, relationship, "Next controller", ["required:third"]);
        Assert.Null(rejected.Round); Assert.Equal(read.Work.HoldReason, rejected.Result.Work!.HoldReason);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
        Assert.Equal("status", f.Ledger.ReadCorrections(f.Context, relationship).Status);
        Assert.Equal("held", f.Ledger.ReadCorrections(f.Context, relationship, expectedIdentity: null, inspectIdentity: true).Status);
        Assert.False(File.Exists(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Adoption)));
    }

    [Fact]
    public void WorkLedger_ApprovedContractObservationSupersedesOldMaterialWithoutRefundingCorrectionRounds()
    {
        using var f = new Fixture();
        const string relationship = "feature/stable-instance";
        CisStoryReviewFinding Finding(string material) => new("F1", "major", "required", "acceptance", "app.txt",
            "The accepted output is missing.", "Correct the accepted output.", "backend", "native-review", material, ["criterion"]);
        var old = f.Ledger.BeginCorrection(f.Context, null, null, relationship, "Controller", ["required:old"],
            "contract:old", "material:old", [Finding("material:old")], [new("backend", "baseline-tree", "candidate-old")]);
        Assert.Equal("contract:old", old.Round!.Context!.ContractDigest);
        var path = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var beforeRead = File.ReadAllBytes(path);
        var revisedProjection = f.Ledger.ReadCorrections(f.Context, relationship, "contract:new");
        Assert.Empty(revisedProjection.Work!.RequiredCorrectionFindingReferences);
        Assert.Null(revisedProjection.Work.RequiredCorrectionContext);
        Assert.Equal(beforeRead, File.ReadAllBytes(path));
        var observed = f.Ledger.ObserveContract(f.Context, null, null, relationship, "Controller", "contract:new");
        Assert.Equal(0, observed.ExitCode);
        var preserved = Assert.Single(observed.Work!.Corrections);
        Assert.Equal(old.Round.Id, preserved.Id);
        Assert.Equal("superseded", preserved.Status);
        Assert.Equal("contract:old", preserved.Context!.ContractDigest);
        var next = f.Ledger.BeginCorrection(f.Context, null, null, relationship, "Controller", ["required:new"],
            "contract:new", "material:new", [Finding("material:new")], [new("backend", "baseline-tree", "candidate-new")]);
        Assert.Equal(2, next.Round!.Number);
        Assert.Equal("candidate-new", Assert.Single(next.Round.Context!.Candidates).CandidateTree);
        Assert.Equal(0, f.Ledger.CompleteCorrection(f.Context, null, null, relationship, next.Round.Id, "Controller").ExitCode);
        var held = f.Ledger.BeginCorrection(f.Context, null, null, relationship, "Controller", ["required:new"],
            "contract:new", "material:new", [Finding("material:new")], [new("backend", "baseline-tree", "candidate-new")]);
        Assert.Null(held.Round);
        Assert.Equal("held", held.Result.Status);
        Assert.Equal("contract:new", held.Result.Work!.RequiredCorrectionContext!.ContractDigest);
        Assert.Equal([1, 2], held.Result.Work.Corrections.Select(round => round.Number).ToArray());
        Assert.Equal("superseded", held.Result.Work.Corrections[0].Status);
        beforeRead = File.ReadAllBytes(path);
        Assert.Equal("held", f.Ledger.ReadCorrections(f.Context, relationship, "contract:new").Status);
        Assert.Equal("status", f.Ledger.ReadCorrections(f.Context, relationship, "contract:another-approved").Status);
        Assert.Equal(beforeRead, File.ReadAllBytes(path));
    }
}
