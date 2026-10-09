using System.Diagnostics;
using Cis.Abstractions;
using Cis.Providers.Agent.Codex;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeStdinTransport_StopsNonReadingChildOnTimeoutOrCancellation(bool cancel)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (cancel) cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var elapsed = Stopwatch.StartNew();

        var result = CisAgentProcessRunner.RunLines(SleepProcess(writeFirst: false), new string('x', 2 * 1024 * 1024),
            TimeSpan.FromSeconds(cancel ? 30 : 1), _ => { }, null, cancellation.Token);

        Assert.Equal(cancel, result.Cancelled);
        Assert.Equal(!cancel, result.TimedOut);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(12), elapsed.Elapsed.ToString());
        AssertProcessExited(result.ProcessId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppServerInput_StopsNonReadingChildOnTimeoutOrCancellation(bool cancel)
    {
        var start = SleepProcess(writeFirst: false);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(cancel ? 30 : 1));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (cancel) cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellation.Token);
        using var writeLock = new SemaphoreSlim(1, 1);
        var elapsed = Stopwatch.StartNew();
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => CodexAgentProvider.SendProtocolMessage(process,
                new { method = "turn/start", prompt = new string('x', 2 * 1024 * 1024) }, writeLock, linked.Token));
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(12), elapsed.Elapsed.ToString());
            Assert.True(process.HasExited);
            Assert.Equal(1, writeLock.CurrentCount);
            Assert.Equal(cancel, cancellation.IsCancellationRequested);
            Assert.Equal(!cancel, timeout.IsCancellationRequested);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } }
    }

    private static void AssertProcessExited(int processId)
    {
        try { using var process = Process.GetProcessById(processId); Assert.True(process.HasExited); }
        catch (ArgumentException) { /* An exited process may already have left the process table. */ }
    }
}
