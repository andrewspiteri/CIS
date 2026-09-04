using System.Diagnostics;
using System.Text;

namespace Cis.Abstractions;

public sealed record CisProcessResult(
    int? ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated);

public static class CisProcessSafety
{
    public const int DefaultMaximumCharactersPerStream = 1_048_576;

    public static CisProcessResult Run(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        int maximumCharactersPerStream = DefaultMaximumCharactersPerStream)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maximumCharactersPerStream < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCharactersPerStream));

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Process '{startInfo.FileName}' could not be started.");

        var output = ReadBoundedAsync(process.StandardOutput, maximumCharactersPerStream);
        var error = ReadBoundedAsync(process.StandardError, maximumCharactersPerStream);
        var timedOut = !process.WaitForExit((int)timeout.TotalMilliseconds);
        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.WaitForExit(2_000);
        }

        Task.WaitAll([output, error], 2_000);
        var stdout = output.IsCompletedSuccessfully ? output.Result : new BoundedText(string.Empty, true);
        var stderr = error.IsCompletedSuccessfully ? error.Result : new BoundedText(string.Empty, true);
        return new(timedOut || !process.HasExited ? null : process.ExitCode, timedOut,
            stdout.Text, stderr.Text, stdout.Truncated || stderr.Truncated);
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[8_192];
        var retained = new StringBuilder(Math.Min(maximumCharacters, 65_536));
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var available = maximumCharacters - retained.Length;
            if (available > 0) retained.Append(buffer, 0, Math.Min(available, read));
            if (read > available) truncated = true;
        }
        return new(retained.ToString(), truncated);
    }

    private sealed record BoundedText(string Text, bool Truncated);
}
