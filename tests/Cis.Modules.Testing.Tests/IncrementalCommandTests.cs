using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Fact]
    public void IncrementalCommandsRegisterAndDispatchPlanWithoutExecutingNativeWork()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "plan", "--profile", "profile.json", "--repo", fixture.Path,
                "--layer", "unit", "--max-batches", "1", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("planned", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("pendingUnitBatches").GetInt32());
        Assert.Empty(fixture.Calls());
    }

    private sealed class IncrementalCommandServices(TestingService service, ICisGraphSnapshotReader graph) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(TestingService) ? service : type == typeof(ICisGraphSnapshotReader) ? graph : null;
    }

    [Fact]
    public void RevalidateCommandRequiresCompatibilityInputsAndDoesNotExecuteTests()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), fixture));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "incremental", "revalidate", "--profile", "profile.json", "--repo", fixture.Path,
                "--previous-testing-assembly", typeof(IncrementalTestService).Assembly.Location, "--reason", "Reviewed compatible guard update.",
                "--legacy-session-environment", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("cache-revalidated", result.RootElement.GetProperty("status").GetString());
        Assert.Empty(fixture.Calls());
    }
}
