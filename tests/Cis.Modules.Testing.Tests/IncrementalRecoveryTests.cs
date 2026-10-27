using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedUnitEvidenceRerunsTheCanaryMutationAfterRestoringItsUnitPrerequisite(bool forceUnit)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var unit = fixture.SavedState().Batches.Values.Single(batch => batch.SourceFile == "src/A.cs").Unit!;
        File.AppendAllText(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + unit.ReportPath), "damaged");

        if (forceUnit)
        {
            var refresh = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json", "unit", 4, 120, true));
            Assert.Equal(0, refresh.ExitCode);
            Assert.Null(fixture.SavedState().Batches.Values.Single(batch => batch.SourceFile == "src/A.cs").Mutation);
        }

        var run = fixture.Run();

        Assert.True(run.ExitCode == 0, string.Join("; ", run.Diagnostics));
        Assert.Equal(forceUnit ? 0 : 1, run.ExecutedUnitBatches);
        Assert.Equal(1, run.ExecutedMutationBatches);
        Assert.Equal(1, run.ReusedMutationBatches);
        Assert.True(run.ScopeComplete);
    }
}
