using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Example.WebTests;

public sealed class OpenApiCompatibilityTests(WebFixture server, ITestOutputHelper output) : IClassFixture<WebFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "api-compatibility")]
    public async Task NativeDiffAcceptsBaselineAndRejectsRemovedOperation(bool removeOperation)
    {
        var executable = Environment.GetEnvironmentVariable("EXAMPLE_OASDIFF_PATH");
        Assert.True(!string.IsNullOrWhiteSpace(executable) && File.Exists(executable), "Set EXAMPLE_OASDIFF_PATH to the pinned native oasdiff 1.32.1 executable; absence is not a pass.");
        var version = await Run(executable!, ["--version"]);
        Assert.Equal(0, version.ExitCode);
        Assert.Equal("oasdiff version 1.32.1", version.Output.Trim());
        using var client = server.Client(server.Token(canWrite: false));
        var native = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var directory = Path.Combine(server.EvidenceDirectory, removeOperation ? "breaking-contract" : "compatible-contract");
        Directory.CreateDirectory(directory);
        var candidate = Path.Combine(directory, "openapi.json");
        if (removeOperation)
        {
            var seeded = JsonNode.Parse(native)!;
            Assert.True(seeded["paths"]!["/api/readings"]!.AsObject().Remove("post"));
            native = seeded.ToJsonString();
        }
        await File.WriteAllTextAsync(candidate, native, TestContext.Current.CancellationToken);
        var result = await Run(executable!, ["breaking", Path.Combine(AppContext.BaseDirectory, "contracts/reading-v1.json"), candidate, "--format", "json", "--fail-on", "ERR"]);
        await File.WriteAllTextAsync(Path.Combine(directory, "oasdiff.json"), result.Output, TestContext.Current.CancellationToken);
        output.WriteLine($"Native API comparison evidence: {directory}");
        Assert.Equal(removeOperation ? 1 : 0, result.ExitCode);
        if (removeOperation) Assert.Contains("api-removed-without-deprecation", result.Output, StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, string Output)> Run(string executable, string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        output.WriteLine(await stderr);
        return (process.ExitCode, await stdout);
    }
}
