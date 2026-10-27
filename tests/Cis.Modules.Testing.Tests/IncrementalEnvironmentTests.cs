using System.Text.Json;
using Xunit;

namespace Cis.Modules.Testing.Tests;

[CollectionDefinition("Incremental environment", DisableParallelization = true)]
public sealed class IncrementalEnvironmentCollection;

[Collection("Incremental environment")]
public sealed class IncrementalEnvironmentTests
{
    private static readonly string[] SessionNames =
    [
        "SSH_CLIENT", "SSH_CONNECTION", "SSH_TTY", "XDG_SESSION_ID", "TERM_SESSION_ID",
        "WT_SESSION", "SHLVL", "_", "PWD", "OLDPWD",
    ];

    [Fact]
    public void NewSessionReusesCompletedUnitAndMutationReportsWithoutExecution()
    {
        using var environment = new EnvironmentScope(SessionNames);
        foreach (var name in SessionNames) Environment.SetEnvironmentVariable(name, "first-session");
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var calls = fixture.Calls();
        var before = fixture.SavedState();

        foreach (var name in SessionNames) Environment.SetEnvironmentVariable(name, "second-session");
        var result = fixture.Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.ReusedUnitBatches);
        Assert.Equal(2, result.ReusedMutationBatches);
        Assert.Equal(0, result.ExecutedUnitBatches);
        Assert.Equal(0, result.ExecutedMutationBatches);
        Assert.Equal(calls, fixture.Calls());
        var after = fixture.SavedState();
        foreach (var (id, batch) in before.Batches)
        {
            Assert.Equal(batch.Unit!.ReportSha256, after.Batches[id].Unit!.ReportSha256);
            Assert.Equal(batch.Mutation!.ReportSha256, after.Batches[id].Mutation!.ReportSha256);
        }
    }

    [Theory]
    [InlineData("CIS_INCREMENTAL_TEST_INPUT")]
    [InlineData("SSH_AUTH_SOCK")]
    [InlineData("PATH")]
    public void MeaningfulAmbientChangesStillInvalidateCompletedResults(string name)
    {
        using var environment = new EnvironmentScope([name]);
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        Environment.SetEnvironmentVariable(name, "changed-input-for-planning-only");

        var plan = fixture.Plan("unit");

        Assert.Equal(0, plan.ExitCode);
        Assert.Equal(2, plan.PendingUnitBatches);
        Assert.All(plan.Batches, batch => Assert.False(batch.UnitCurrent));
    }

    [Fact]
    public void NativeCommandsReceiveCanonicalSessionValuesAndExplicitProfileOverrides()
    {
        using var environment = new EnvironmentScope(SessionNames.Append("CIS_INCREMENTAL_TEST_INPUT"));
        foreach (var name in SessionNames) Environment.SetEnvironmentVariable(name, "parent-session");
        Environment.SetEnvironmentVariable("CIS_INCREMENTAL_TEST_INPUT", "ambient-input");
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var names = SessionNames.Append("CIS_INCREMENTAL_TEST_INPUT").ToArray();
        fixture.Write(".cis/environment-probe.json", JsonSerializer.Serialize(names));
        fixture.Save(fixture.Profile with { Environment = new() { ["SSH_CLIENT"] = "declared-session", ["CIS_INCREMENTAL_TEST_INPUT"] = "declared-input" } });

        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var observed = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(fixture.Full(".cis/environment-observed.json")))!;
        Assert.Equal("declared-session", observed["SSH_CLIENT"]);
        Assert.Equal("declared-input", observed["CIS_INCREMENTAL_TEST_INPUT"]);
        Assert.Equal(fixture.Path, observed["PWD"]);
        Assert.Equal("0", observed["SHLVL"]);
        foreach (var name in SessionNames.Except(["SSH_CLIENT", "PWD", "SHLVL"])) Assert.True(string.IsNullOrEmpty(observed[name]), name);

        Environment.SetEnvironmentVariable("CIS_INCREMENTAL_TEST_INPUT", "another-ambient-input");
        Assert.Equal(0, fixture.Run("unit").ExecutedUnitBatches);
        fixture.Save(fixture.Profile with { Environment = new() { ["SSH_CLIENT"] = "different-declared-session", ["CIS_INCREMENTAL_TEST_INPUT"] = "declared-input" } });
        Assert.Equal(2, fixture.Plan("unit").PendingUnitBatches);
    }

    private sealed class EnvironmentScope(IEnumerable<string> names) : IDisposable
    {
        private readonly Dictionary<string, string?> previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        public void Dispose()
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
