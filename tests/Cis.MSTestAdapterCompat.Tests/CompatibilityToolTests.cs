using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Cis.MSTestAdapterCompat.Tests;

public sealed class CompatibilityToolTests
{
    private static string Official => Path.Combine(AppContext.BaseDirectory, "fixture-input", "MSTestAdapter.PlatformServices.dll");

    [Fact]
    public async Task RealOverlayVerifiesAndChangedRuntimeCannotApproveItself()
    {
        using var scratch = new Scratch();
        var overlay = Path.Combine(scratch.Path, "overlay");
        Assert.Equal(0, (await Run("--input", Official, "--output-directory", overlay)).ExitCode);
        Assert.Equal(0, (await Run("--verify-overlay", overlay)).ExitCode);
        var runtime = Path.Combine(overlay, "Cis.MSTestAdapterCompat.Runtime.dll");
        var bytes = await File.ReadAllBytesAsync(runtime, TestContext.Current.CancellationToken);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(runtime, bytes, TestContext.Current.CancellationToken);
        var manifestPath = Path.Combine(overlay, "compatibility-manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken))!;
        manifest["outputs"]!.AsArray().Single(entry => entry!["file"]!.GetValue<string>() == "Cis.MSTestAdapterCompat.Runtime.dll")!["sha256"] =
            Convert.ToHexStringLower(SHA256.HashData(bytes));
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), TestContext.Current.CancellationToken);
        Assert.Equal(2, (await Run("--verify-overlay", overlay)).ExitCode);
    }

    [Fact]
    public async Task ChangedOfficialBytesAreRejectedWithoutPublishingAnything()
    {
        using var scratch = new Scratch();
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(Official, TestContext.Current.CancellationToken));
        var inputDirectory = Directory.CreateDirectory(Path.Combine(scratch.Path, "input"));
        var input = Path.Combine(inputDirectory.FullName, "changed.dll");
        var bytes = await File.ReadAllBytesAsync(Official, TestContext.Current.CancellationToken);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
        var overlay = Path.Combine(scratch.Path, "overlay");
        Assert.Equal(2, (await Run("--input", input, "--output-directory", overlay)).ExitCode);
        Assert.False(Directory.Exists(overlay));
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(Official, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task OversizedInputIsRejectedBeforeCreatingAnOverlay()
    {
        using var scratch = new Scratch();
        var input = Path.Combine(scratch.Path, "large.dll");
        using (var stream = File.Create(input)) stream.SetLength(16 * 1024 * 1024);
        var overlay = Path.Combine(scratch.Path, "overlay");
        var result = await Run("--input", input, "--output-directory", overlay);
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(overlay));
    }

    [Fact]
    public async Task ExistingOutputIsNeverOverwritten()
    {
        using var scratch = new Scratch();
        var overlay = Directory.CreateDirectory(Path.Combine(scratch.Path, "overlay"));
        var sentinel = Path.Combine(overlay.FullName, "owned-by-another-run.txt");
        await File.WriteAllTextAsync(sentinel, "preserve", TestContext.Current.CancellationToken);
        Assert.Equal(2, (await Run("--input", Official, "--output-directory", overlay.FullName)).ExitCode);
        Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(overlay.FullName));
    }

    [Fact]
    public async Task OutputCannotBeCreatedInsideTheOfficialInputDirectory()
    {
        var output = Path.Combine(Path.GetDirectoryName(Official)!, "forbidden-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(2, (await Run("--input", Official, "--output-directory", output)).ExitCode);
        Assert.False(Directory.Exists(output));
    }

    private static async Task<(int ExitCode, string Output)> Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Cis.MSTestAdapterCompat.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "Cis.MSTestAdapterCompat.Tests.deps.json"),
            Path.Combine(AppContext.BaseDirectory, "Cis.MSTestAdapterCompat.Tool.dll") }.Concat(args)) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            using var reap = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(reap.Token);
            throw;
        }
        var output = await stdout + await stderr;
        Assert.True(output.Length < 65536, "The small tool emitted excessive output.");
        return (process.ExitCode, output);
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cis-mstest-compat-tests-").FullName;
        public void Dispose()
        {
            var temporaryRoot = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())) + System.IO.Path.DirectorySeparatorChar;
            if (!System.IO.Path.GetFullPath(Path).StartsWith(temporaryRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || !System.IO.Path.GetFileName(Path).StartsWith("cis-mstest-compat-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected temporary cleanup target.");
            Directory.Delete(Path, recursive: true);
        }
    }
}
