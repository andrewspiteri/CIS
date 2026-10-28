using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cis.Abstractions.Tests;

public sealed class CisProcessSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedStreamsAreCapturedWithoutASpareWorker(bool constrained)
    {
        using var observation = await RunFixture(constrained ? "one-worker" : "normal");
        var root = observation.RootElement;
        Assert.Equal(constrained, root.GetProperty("constrained").GetBoolean());
        Assert.False(root.GetProperty("mainIsThreadPoolThread").GetBoolean());
        Assert.True(root.GetProperty("workerIsThreadPoolThread").GetBoolean());
        if (constrained)
        {
            Assert.Equal(1, root.GetProperty("minimumWorkers").GetInt32());
            Assert.Equal(1, root.GetProperty("maximumWorkers").GetInt32());
        }
        var result = root.GetProperty("result");
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.False(result.GetProperty("timedOut").GetBoolean());
        Assert.Equal("process-capture-stdout" + Environment.NewLine, result.GetProperty("standardOutput").GetString());
        Assert.Equal("process-capture-stderr" + Environment.NewLine, result.GetProperty("standardError").GetString());
        Assert.False(result.GetProperty("outputTruncated").GetBoolean());
    }

    [Fact]
    public async Task BothStreamsAreDrainedAfterTheirRetentionLimit()
    {
        using var observation = await RunFixture("large");
        var result = observation.RootElement.GetProperty("result");
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.False(result.GetProperty("timedOut").GetBoolean());
        Assert.Equal(new string('x', 128), result.GetProperty("standardOutput").GetString());
        Assert.Equal(new string('y', 128), result.GetProperty("standardError").GetString());
        Assert.True(result.GetProperty("outputTruncated").GetBoolean());
    }

    private static async Task<JsonDocument> RunFixture(string mode)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var fixture = Path.Combine(AppContext.BaseDirectory, "process-safety-fixture", "Cis.ProcessSafety.NativeFixture.dll");
        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(fixture);
        start.ArgumentList.Add(mode);
        using var process = new Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            var error = await stderr;
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(error);
            var observation = JsonDocument.Parse(output);
            Assert.Equal(JsonValueKind.Null, observation.RootElement.GetProperty("failure").ValueKind);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CisProcessSafety).Assembly.Location))),
                observation.RootElement.GetProperty("helperSha256").GetString());
            return observation;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                Assert.True(process.WaitForExit(2_000), "The bounded fixture did not exit after termination.");
            }
        }
    }
}
