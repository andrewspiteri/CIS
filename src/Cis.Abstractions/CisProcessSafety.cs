using System.Diagnostics;
using System.Text;

namespace Cis.Abstractions;

public sealed record CisProcessResult(
    int? ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated)
{
    public bool Cancelled { get; init; }
    // This proves this owned process stopped, not that a separate native job or session did.
    public bool SafelyStopped { get; init; }
}

public static partial class CisProcessSafety
{
    public const int DefaultMaximumCharactersPerStream = 1_048_576;

    public static CisProcessResult Run(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        int maximumCharactersPerStream = DefaultMaximumCharactersPerStream,
        Action<Process>? processStarted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maximumCharactersPerStream < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCharactersPerStream));
        if (cancellationToken.IsCancellationRequested)
            return new(null, false, "", "", false) { Cancelled = true, SafelyStopped = true };

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        using var process = new Process { StartInfo = startInfo };
        var clock = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException($"Process '{startInfo.FileName}' could not be started.");

        var output = StartReader(process.StandardOutput, maximumCharactersPerStream);
        var error = StartReader(process.StandardError, maximumCharactersPerStream);
        var cancelled = false;
        var timedOut = false;
        try
        {
            processStarted?.Invoke(process);
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
                var remaining = timeout - clock.Elapsed;
                if (remaining <= TimeSpan.Zero) { timedOut = true; break; }
                var wait = cancellationToken.CanBeCanceled ? Math.Min(100, remaining.TotalMilliseconds) : remaining.TotalMilliseconds;
                process.WaitForExit(Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(wait))));
            }
        }
        catch
        {
            Stop(process);
            Task.WaitAll([output, error], 2_000);
            throw;
        }
        if (timedOut || cancelled)
        {
            Stop(process);
        }

        Task.WaitAll([output, error], 2_000);
        var stdout = output.IsCompletedSuccessfully ? output.Result : new BoundedText(string.Empty, true);
        var stderr = error.IsCompletedSuccessfully ? error.Result : new BoundedText(string.Empty, true);
        return new(timedOut || cancelled || !process.HasExited ? null : process.ExitCode, timedOut,
            stdout.Text, stderr.Text, stdout.Truncated || stderr.Truncated)
        { Cancelled = cancelled, SafelyStopped = process.HasExited };
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        process.WaitForExit(2_000);
    }

    private static Task<BoundedText> StartReader(StreamReader reader, int maximumCharacters)
        // Run blocks its caller while waiting for the process and its streams. Neither
        // pipe may depend on another worker being available to finish that wait.
        => Task.Factory.StartNew(() => ReadBounded(reader, maximumCharacters), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static BoundedText ReadBounded(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[8_192];
        var retained = new StringBuilder(Math.Min(maximumCharacters, 65_536));
        var truncated = false;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            var available = maximumCharacters - retained.Length;
            if (available > 0) retained.Append(buffer, 0, Math.Min(available, read));
            if (read > available) truncated = true;
        }
        return new(retained.ToString(), truncated);
    }

    private sealed record BoundedText(string Text, bool Truncated);
}
