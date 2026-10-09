using System.Diagnostics;

namespace Cis.Abstractions;

/// <summary>Delivers provider input without allowing pipe backpressure to outlive cancellation.</summary>
public static class CisAgentInputDelivery
{
    public static void Write(Process process, string content, CancellationToken cancellationToken)
    {
        async Task Deliver()
        {
            await process.StandardInput.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var delivery = Deliver();
        try { delivery.WaitAsync(cancellationToken).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            // Some pipe implementations cannot cancel an outstanding OS write. Closing the child
            // process tree releases that pipe; observing the task also retains no unhandled fault.
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            _ = delivery.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
