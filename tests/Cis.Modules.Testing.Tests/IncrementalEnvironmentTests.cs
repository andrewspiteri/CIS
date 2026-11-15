using System.Text.Json;
using Cis.Abstractions;
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
        "CODEX_TOOL_CALL_ID", "CODEX_APP_TOOLS_PIPE_PATH",
        "CODEX_SESSION_ID", "CODEX_THREAD_ID", "CODEX_SYNTHETIC_TRANSPORT",
        "BROWSER_USE_ACCESS_PIPE_PATH", "BROWSER_USE_ACCESS_SYNTHETIC_TRANSPORT",
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
        Environment.SetEnvironmentVariable("CODEX_SYNTHETIC_TRANSPORT", null);
        Environment.SetEnvironmentVariable("BROWSER_USE_ACCESS_SYNTHETIC_TRANSPORT", null);
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

    [Fact]
    public void InvocationWorkCoordinatesDoNotInvalidateCurrentNativeEvidence()
    {
        using var environment = new EnvironmentScope(CisNativeWorkEnvironment.Keys);
        foreach (var name in CisNativeWorkEnvironment.Keys) Environment.SetEnvironmentVariable(name, null);
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var calls = fixture.Calls();
        var statePath = fixture.Full(IncrementalTestService.LocalRoot + "/sample/state.json");
        var state = File.ReadAllBytes(statePath);
        foreach (var invocation in new[] { "first-owner-invocation", "second-owner-invocation" })
        {
            foreach (var name in CisNativeWorkEnvironment.Keys) Environment.SetEnvironmentVariable(name, invocation);
            var plan = fixture.Plan();
            Assert.Equal(0, plan.ExitCode);
            Assert.Equal(0, plan.PendingUnitBatches); Assert.Equal(0, plan.PendingMutationBatches);
            Assert.All(plan.Batches, batch => { Assert.True(batch.UnitCurrent); Assert.True(batch.MutationCurrent); });
            Assert.Equal(calls, fixture.Calls());
            Assert.Equal(state, File.ReadAllBytes(statePath));
        }
    }

    [Fact]
    public void ProfileEnvironmentCannotSupplyInvocationWorkAuthority()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        foreach (var name in CisNativeWorkEnvironment.Keys)
        {
            fixture.Save(fixture.Profile with { Environment = new() { [name] = "provider-selected-owner" } });
            var plan = fixture.Plan();
            Assert.Equal(2, plan.ExitCode);
            Assert.Contains(plan.Diagnostics, message => message.Contains("Work authority coordinates belong to the invocation", StringComparison.Ordinal));
        }
        Assert.Empty(fixture.Calls());
    }

    [Theory]
    [InlineData("CIS_INCREMENTAL_TEST_INPUT")]
    [InlineData("SSH_AUTH_SOCK")]
    [InlineData("PATH")]
    public void MeaningfulAmbientChangesStillInvalidateCompletedResults(string name)
    {
        using var environment = new EnvironmentScope([name]);
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var previous = Environment.GetEnvironmentVariable(name);
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        Environment.SetEnvironmentVariable(name, "changed-input-for-planning-only");

        var plan = fixture.Plan("unit");

        Assert.Equal(0, plan.ExitCode);
        Assert.Equal(2, plan.PendingUnitBatches);
        Assert.All(plan.Batches, batch =>
        {
            Assert.False(batch.UnitCurrent);
            Assert.Contains(batch.UnitReasons, reason => reason.Category == "environment" && string.Equals(reason.Input, name,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && reason.Change == (previous is null ? "added" : "changed"));
        });

        Environment.SetEnvironmentVariable(name, previous);
        Assert.Equal(0, fixture.Plan("unit").PendingUnitBatches);
    }

    [Theory]
    [InlineData("SSH_CLIENT")]
    [InlineData("CODEX_TOOL_CALL_ID")]
    [InlineData("CODEX_SESSION_ID")]
    [InlineData("BROWSER_USE_ACCESS_SYNTHETIC_TRANSPORT")]
    public void NativeCommandsReceiveCanonicalSessionValuesAndExplicitProfileOverrides(string sessionName)
    {
        using var environment = new EnvironmentScope(SessionNames.Append("CIS_INCREMENTAL_TEST_INPUT"));
        foreach (var name in SessionNames) Environment.SetEnvironmentVariable(name, "parent-session");
        Environment.SetEnvironmentVariable("CIS_INCREMENTAL_TEST_INPUT", "ambient-input");
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var names = SessionNames.Append("CIS_INCREMENTAL_TEST_INPUT").ToArray();
        fixture.Write(".cis/environment-probe.json", JsonSerializer.Serialize(names));
        fixture.Save(fixture.Profile with { Environment = new() { [sessionName] = "declared-session", ["CIS_INCREMENTAL_TEST_INPUT"] = "declared-input" } });

        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var observed = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(fixture.Full(".cis/environment-observed.json")))!;
        Assert.Equal("declared-session", observed[sessionName]);
        Assert.Equal("declared-input", observed["CIS_INCREMENTAL_TEST_INPUT"]);
        Assert.Equal(fixture.Path, observed["PWD"]);
        Assert.Equal("0", observed["SHLVL"]);
        Assert.Null(observed["CODEX_SYNTHETIC_TRANSPORT"]);
        foreach (var name in SessionNames.Except([sessionName, "PWD", "SHLVL"])) Assert.True(string.IsNullOrEmpty(observed[name]), name);

        Environment.SetEnvironmentVariable("CIS_INCREMENTAL_TEST_INPUT", "another-ambient-input");
        Assert.Equal(0, fixture.Run("unit").ExecutedUnitBatches);
        fixture.Save(fixture.Profile with { Environment = new() { [sessionName] = "different-declared-session", ["CIS_INCREMENTAL_TEST_INPUT"] = "declared-input" } });
        Assert.Equal(2, fixture.Plan("unit").PendingUnitBatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeModulePathMatchesTheMaterialParentValueRatherThanGuardShellStartup(bool declared)
    {
        const string name = "PSModulePath";
        using var environment = new EnvironmentScope([name]);
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var original = fixture.Full("synthetic-parent-modules");
        var explicitValue = fixture.Full("synthetic-declared-modules");
        Environment.SetEnvironmentVariable(name, original);
        fixture.Write(".cis/environment-probe.json", JsonSerializer.Serialize(new[] { name }));
        if (declared) fixture.Save(fixture.Profile with { Environment = new() { [name] = explicitValue } });

        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var calls = fixture.Calls();
        var observed = JsonSerializer.Deserialize<Dictionary<string, string?>>(
            File.ReadAllText(fixture.Full(".cis/environment-observed.json")))!;
        Assert.Equal(declared ? explicitValue : original, observed[name]);

        Environment.SetEnvironmentVariable(name, fixture.Full("synthetic-next-session-modules"));
        Assert.Equal(declared ? 0 : 2, fixture.Plan("unit").PendingUnitBatches);
        Environment.SetEnvironmentVariable(name, original);
        var reused = fixture.Run("unit");
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(2, reused.ReusedUnitBatches);
        Assert.Equal(calls, fixture.Calls());
    }

    [Fact]
    public void EnvironmentComparisonRetainsOnlyDigestsAndExplainsRemovalWithoutNativeReplay()
    {
        const string name = "CIS_SYNTHETIC_PRIVATE_INPUT";
        const string value = "synthetic-sensitive-value";
        using var environment = new EnvironmentScope([name]);
        Environment.SetEnvironmentVariable(name, value);
        using var fixture = new IncrementalTestServiceTests.Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var calls = fixture.Calls();
        var state = fixture.SavedState();
        Assert.Contains(state.InputFacts.Values, fact => fact.Category == "environment" && fact.Input == name);
        Assert.DoesNotContain(value, JsonSerializer.Serialize(state), StringComparison.Ordinal);
        Environment.SetEnvironmentVariable(name, null);

        var plan = fixture.Plan("unit");

        Assert.Equal(2, plan.PendingUnitBatches);
        Assert.All(plan.Batches, batch => Assert.Contains(batch.UnitReasons,
            reason => reason.Category == "environment" && reason.Input == name && reason.Change == "removed"));
        Environment.SetEnvironmentVariable(name, value);
        Assert.Equal(0, fixture.Run("unit").ExecutedUnitBatches);
        Assert.Equal(calls, fixture.Calls());
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
