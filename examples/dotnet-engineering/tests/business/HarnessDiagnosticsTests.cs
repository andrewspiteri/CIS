using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Example.BusinessTests;

public sealed class HarnessDiagnosticsTests(ITestOutputHelper output)
{
    private const string Password = "synthetic-capture-secret";
    private const string Connection = "Host=localhost;Username=fixture;Password=" + Password + ";Database=fixture";

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    [InlineData("limit")]
    public async Task FailedChildRetainsRedactedPartialOutputAndIsReaped(string failure)
    {
        var evidence = new BusinessEvidence(output);
        var start = new ProcessStartInfo("pwsh")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", ChildScript }) start.ArgumentList.Add(argument);
        start.Environment["CAPTURE_PASSWORD"] = Password;
        start.Environment["CAPTURE_CONNECTION"] = Connection;
        start.Environment["CAPTURE_FAILURE"] = failure;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (failure == "cancelled") cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        var timeout = TimeSpan.FromSeconds(failure == "timeout" ? 5 : 20);
        var error = await Record.ExceptionAsync(() => ProcessCapture.RunAsync(start, timeout, evidence, Connection, cancellation.Token));
        if (failure == "limit") Assert.IsType<InvalidDataException>(error);
        else Assert.IsAssignableFrom<OperationCanceledException>(error);

        var file = Assert.Single(Directory.GetFiles(evidence.DirectoryPath, "cli-*.json"));
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Connection, text, StringComparison.Ordinal);
        Assert.DoesNotContain("synthe", text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Contains("stdout-ready", root.GetProperty("stdout").GetString()!, StringComparison.Ordinal);
        Assert.Contains("stderr-ready", root.GetProperty("stderr").GetString()!, StringComparison.Ordinal);
        Assert.True(root.GetProperty("stdout").GetString()!.Length <= BoundedProcessOutput.Limit);
        Assert.True(root.GetProperty("stderr").GetString()!.Length <= BoundedProcessOutput.Limit);
        Assert.Equal(failure == "limit" ? nameof(InvalidDataException) : failure, root.GetProperty("failure").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cleanupFailure").ValueKind);
        AssertChildExited(root.GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task ReaderFailureLeavesCapturedPrefixInspectable()
    {
        using var reader = new StreamReader(new InterruptedStream(Encoding.UTF8.GetBytes("partial-before-reader-failure")));
        var capture = new BoundedProcessOutput();
        var error = await Assert.ThrowsAsync<IOException>(() => capture.ReadAsync(reader, TestContext.Current.CancellationToken));
        Assert.Equal("controlled-reader-failure", error.Message);
        Assert.Equal("partial-before-reader-failure", capture.Snapshot);
    }

    private static void AssertChildExited(int processId)
    {
        try { using var process = Process.GetProcessById(processId); Assert.True(process.HasExited, "Child process must be reaped before failure is returned."); }
        catch (ArgumentException) { /* The OS has already removed the exited process. */ }
    }

    private sealed class InterruptedStream(byte[] content) : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Position == Length ? ValueTask.FromException<int>(new IOException("controlled-reader-failure")) : base.ReadAsync(buffer, cancellationToken);
    }

    private const string ChildScript = """
        [Console]::Error.Write('stderr-ready ' + $env:CAPTURE_CONNECTION)
        [Console]::Error.Flush()
        $header = 'stdout-ready ' + $env:CAPTURE_PASSWORD + "`n"
        [Console]::Out.Write($header)
        [Console]::Out.Flush()
        if ($env:CAPTURE_FAILURE -eq 'limit') {
            [Console]::Out.Write(('x' * (16384 - $header.Length - 6)) + $env:CAPTURE_PASSWORD + ('x' * 1024))
            [Console]::Out.Flush()
        }
        Start-Sleep -Seconds 60
        """;
}
