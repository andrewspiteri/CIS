using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRecoveryTests
{
    [Fact]
    public void DamagedUnitEvidenceRerunsTheCanaryMutationAfterRestoringItsUnitPrerequisite()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var unit = fixture.SavedState().Batches.Values.Single(batch => batch.SourceFile == "src/A.cs").Unit!;
        File.AppendAllText(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + unit.ReportPath), "damaged");

        var run = fixture.Run();

        Assert.True(run.ExitCode == 0, string.Join("; ", run.Diagnostics));
        Assert.Equal(1, run.ExecutedUnitBatches);
        Assert.Equal(1, run.ExecutedMutationBatches);
        Assert.Equal(1, run.ReusedMutationBatches);
        Assert.True(run.ScopeComplete);
    }
}
