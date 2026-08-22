using System.CommandLine;
using System.Diagnostics;
using System.Text;
using Cis.Abstractions;

namespace Cis.Host;

public sealed class CisApplication : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly RootCommand _rootCommand;
    private static readonly object ConsoleSync = new();

    internal CisApplication(RootCommand rootCommand, IServiceProvider services)
    {
        _rootCommand = rootCommand;
        _services = services;
    }

    public int Invoke(string[] args)
    {
        var recorder = _services.GetService(typeof(ICisToolUsageRecorder)) as ICisToolUsageRecorder;
        var savings = _services.GetService(typeof(ICisTokenSavingsCollector)) as ICisTokenSavingsCollector;
        if (recorder is null)
        {
            return _rootCommand.Parse(args).Invoke();
        }

        lock (ConsoleSync)
        {
            savings?.Reset();
            var startedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            var capturedOut = new StringBuilder();
            var capturedError = new StringBuilder();
            var exitCode = 1;

            try
            {
                Console.SetOut(new TeeTextWriter(originalOut, capturedOut));
                Console.SetError(new TeeTextWriter(originalError, capturedError));
                exitCode = _rootCommand.Parse(args).Invoke();
                return exitCode;
            }
            finally
            {
                stopwatch.Stop();
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                try
                {
                    recorder.Record(new CisToolUsageCapture(
                        args,
                        Directory.GetCurrentDirectory(),
                        startedAt,
                        DateTimeOffset.UtcNow,
                        stopwatch.ElapsedMilliseconds,
                        exitCode,
                        capturedOut.Length,
                        capturedError.Length,
                        savings?.Snapshot() ?? []));
                }
                catch
                {
                    // Usage evidence must never change the command outcome.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal sealed class TeeTextWriter(TextWriter target, StringBuilder capture) : TextWriter
{
    public override Encoding Encoding => target.Encoding;

    public override void Write(char value)
    {
        target.Write(value);
        capture.Append(value);
    }

    public override void Write(string? value)
    {
        target.Write(value);
        capture.Append(value);
    }

    public override void WriteLine(string? value)
    {
        target.WriteLine(value);
        capture.AppendLine(value);
    }

    public override void Flush() => target.Flush();
}
