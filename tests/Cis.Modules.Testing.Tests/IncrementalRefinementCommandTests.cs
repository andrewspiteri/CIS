using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Fact]
    public void RefineCommandDispatchesVerifiedNativeEvidenceWithoutExecutingTests()
    {
        using var fixture = new IncrementalRefinementTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture.Inner));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "refine", "--profile", "profile.json", "--rerun-profile", "rerun.json",
                "--repo", fixture.Inner.Path, "--reason", "Reviewed native filter and source ranges.", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("refined", result.RootElement.GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("qualified").GetBoolean());
        Assert.Equal(50, result.RootElement.GetProperty("raw").GetProperty("score").GetDouble());
        Assert.Equal(100, result.RootElement.GetProperty("effective").GetProperty("score").GetDouble());
        Assert.Empty(fixture.Inner.Calls());
    }
}
