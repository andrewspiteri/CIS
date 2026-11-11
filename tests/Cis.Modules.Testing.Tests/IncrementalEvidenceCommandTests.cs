using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Fact]
    public void VerifyUnitCommandUsesTheAcceptedUnitScopeWithoutLaunchingAgain()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with { RecoveryCommand = null });
        var seeded = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json", "unit"), 0,
            TestContext.Current.CancellationToken);
        Assert.True(seeded.ExitCode == 0, string.Join("; ", seeded.Diagnostics));
        var calls = fixture.Calls();
        var statePath = fixture.Full(IncrementalTestService.LocalRoot + "/sample/state.json");
        var state = File.ReadAllBytes(statePath);
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "verify", "--profile", "profile.json", "--repo", fixture.Path,
                "--layer", "unit", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("verified", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("unit", result.RootElement.GetProperty("layer").GetString());
        Assert.Equal(CisIncrementalResultContract.Version, result.RootElement.GetProperty("assessmentContractVersion").GetInt32());
        Assert.Equal(calls, fixture.Calls());
        Assert.Equal(state, File.ReadAllBytes(statePath));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("human")]
    [InlineData("agent")]
    public void VerifyCommandUsesExplicitCurrentRerunEvidenceWithoutExecutingTests(string format)
    {
        using var fixture = new IncrementalEvidenceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture.Inner));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "verify", "--profile", "profile.json", "--refinement-profile", "rerun.json",
                "--repo", fixture.Inner.Path, "--format", format]).Invoke());
        }
        finally { Console.SetOut(previous); }
        if (format == "json")
        {
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal("verified", result.RootElement.GetProperty("status").GetString());
            Assert.False(result.RootElement.GetProperty("qualified").GetBoolean());
            Assert.Equal(100, result.RootElement.GetProperty("effective").GetProperty("score").GetDouble());
        }
        else Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(fixture.Inner.Calls());
    }
}
