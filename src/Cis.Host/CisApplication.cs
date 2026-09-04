using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Cis.Abstractions;

namespace Cis.Host;

[SuppressMessage("Maintainability", "CA1515", Justification = "The public host surface is consumed by integration hosts and module tests.")]
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

    [SuppressMessage("Design", "CA1031", Justification = "The process boundary must sanitize every unhandled command or evidence-recorder failure without changing the command outcome.")]
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
            using var capturedOut = new CountingTeeTextWriter(originalOut);
            using var capturedError = new CountingTeeTextWriter(originalError);
            var exitCode = 1;

            try
            {
                Console.SetOut(capturedOut);
                Console.SetError(capturedError);
                exitCode = _rootCommand.Parse(args).Invoke();
                return exitCode;
            }
            catch (OperationCanceledException)
            {
                exitCode = 130;
                Console.Error.WriteLine("CIS command was cancelled.");
                return exitCode;
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Console.Error.WriteLine($"CIS-HOST-UNHANDLED: {exception.GetType().Name}. Run `cis repo doctor` and retain the tool-usage record.");
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
                        capturedOut.CharacterCount,
                        capturedError.CharacterCount,
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

internal sealed class CountingTeeTextWriter(TextWriter target) : TextWriter
{
    private long _characterCount;
    public override Encoding Encoding => target.Encoding;
    public int CharacterCount => (int)Math.Min(int.MaxValue, Interlocked.Read(ref _characterCount));

    public override void Write(char value)
    {
        target.Write(value);
        Interlocked.Increment(ref _characterCount);
    }

    public override void Write(string? value)
    {
        target.Write(value);
        if (value is not null) Interlocked.Add(ref _characterCount, value.Length);
    }

    public override void WriteLine(string? value)
    {
        target.WriteLine(value);
        Interlocked.Add(ref _characterCount, (value?.Length ?? 0) + Environment.NewLine.Length);
    }

    public override void Flush() => target.Flush();
}
