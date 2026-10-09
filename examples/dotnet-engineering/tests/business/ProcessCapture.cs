using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Example.BusinessTests;

/// <summary>Owns process lifetime and retains partial evidence before propagating the original failure.</summary>
internal static class ProcessCapture
{
    internal static async Task<CliProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        BusinessEvidence? evidence, string? connectionString, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(timeout);
        using var readers = new CancellationTokenSource();
        var stdout = new BoundedProcessOutput();
        var stderr = new BoundedProcessOutput();
        var outputTasks = new[] { stdout.ReadAsync(process.StandardOutput, readers.Token), stderr.ReadAsync(process.StandardError, readers.Token) };
        Exception? failure = null;
        try { await AwaitAllAsync([process.WaitForExitAsync(operation.Token), .. outputTasks], operation.Token); }
        catch (Exception error) { failure = error; }

        var cleanupFailure = await CleanupAsync(process, outputTasks, readers);
        failure ??= cleanupFailure;
        var reason = failure?.GetType().Name;
        if (failure is OperationCanceledException) reason = cancellationToken.IsCancellationRequested ? "cancelled" : "timeout";
        int? exitCode = process.HasExited ? process.ExitCode : null;
        try
        {
            var diagnostic = new ProcessDiagnostic(exitCode, process.Id, stdout.Snapshot, stderr.Snapshot, reason, cleanupFailure?.GetType().Name);
            await CliProcessEvidence.RetainAsync(evidence, diagnostic, connectionString);
        }
        catch (Exception error) when (failure is not null) { failure.Data["evidenceFailure"] = error.GetType().Name; }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return new CliProcessResult(exitCode!.Value, stdout.Snapshot, stderr.Snapshot);
    }

    private static async Task AwaitAllAsync(List<Task> pending, CancellationToken cancellationToken)
    {
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending).WaitAsync(cancellationToken);
            await completed;
            pending.Remove(completed);
        }
    }

    private static async Task<Exception?> CleanupAsync(Process process, Task[] outputTasks, CancellationTokenSource readers)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cleanup.Token);
            try { await Task.WhenAll(outputTasks).WaitAsync(cleanup.Token); }
            catch (Exception) when (outputTasks.All(task => task.IsCompleted)) { /* Original reader failure is preserved by the caller. */ }
            return null;
        }
        catch (Exception error) { return error; }
        finally { await readers.CancelAsync(); }
    }
}
