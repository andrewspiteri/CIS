using System.CommandLine;
using System.CommandLine.Parsing;
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
    internal static readonly object ConsoleSync = new();

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
            lock (ConsoleSync) return _rootCommand.Parse(args).Invoke();
        }

        lock (ConsoleSync)
        {
            savings?.Reset();
            CisInvocationOutcome.Reset();
            var startedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var capturedOut = new CountingTeeTextWriter(originalOut);
            using var capturedError = new CountingTeeTextWriter(originalError);
            CisInvocationTelemetryScope? observations = null;
            string? canonicalCommand = null;
            IReadOnlyList<string>? recognizedOptions = null;
            var exitCode = 1;

            try
            {
                Console.SetOut(capturedOut);
                Console.SetError(capturedError);
                var parsed = _rootCommand.Parse(args);
                var operation = Operation(parsed);
                recognizedOptions = RecognizedOptions(parsed);
                if (operation == "help" && recognizedOptions.Contains("--version", StringComparer.Ordinal)) operation = "version";
                canonicalCommand = string.Join(' ', operation.Split(' ').Take(2));
                observations = CisInvocationTelemetry.Begin(operation);
                if (parsed.Errors.Count > 0)
                {
                    CisInvocationTelemetry.RecordBlocker("CIS-CLI-PARSE");
                    parsed.Invoke();
                    CisInvocationOutcome.Report("invalid-request");
                    exitCode = 2;
                }
                else exitCode = parsed.Invoke();
                return exitCode;
            }
            catch (OperationCanceledException)
            {
                CisInvocationTelemetry.RecordBlocker("CIS-HOST-CANCELLED");
                CisInvocationOutcome.Report("cancelled");
                exitCode = 130;
                Console.Error.WriteLine("CIS command was cancelled.");
                return exitCode;
            }
            catch (Exception exception)
            {
                CisInvocationTelemetry.RecordBlocker("CIS-HOST-UNHANDLED");
                CisInvocationOutcome.Report("failed");
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
                        savings?.Snapshot() ?? [],
                        CisInvocationOutcome.Current,
                        typeof(CisApplication).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion)
                    {
                        CanonicalCommand = canonicalCommand,
                        RecognizedOptions = recognizedOptions,
                        Observations = observations?.Snapshot(),
                        StandardOutputUtf8Bytes = capturedOut.Utf8ByteCount,
                        StandardErrorUtf8Bytes = capturedError.Utf8ByteCount,
                    });
                }
                catch
                {
                    // Usage evidence must never change the command outcome.
                }
                finally { observations?.Dispose(); }
            }
        }
    }

    private static string Operation(ParseResult parsed)
    {
        var names = new Stack<string>();
        for (var result = parsed.CommandResult; result is not null; result = result.Parent as CommandResult)
            if (result.Command is not RootCommand) names.Push(result.Command.Name);
        return names.Count == 0 ? parsed.Errors.Count > 0 ? "unknown" : "help" : string.Join(' ', names);
    }

    private static IReadOnlyList<string> RecognizedOptions(ParseResult parsed)
    {
        var options = new List<string>();
        for (var result = parsed.CommandResult; result is not null; result = result.Parent as CommandResult)
            options.AddRange(result.Children.OfType<OptionResult>().Where(option => !option.Implicit).Select(option => option.Option.Name));
        return options.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
    private long _utf8ByteCount;
    private bool _pendingHighSurrogate;
    private readonly object _sync = new();
    public override Encoding Encoding => target.Encoding;
    public int CharacterCount => (int)Math.Min(int.MaxValue, Interlocked.Read(ref _characterCount));
    public long Utf8ByteCount
    {
        get
        {
            lock (_sync)
            {
                // Flush once the invocation has finished, including an unmatched final surrogate.
                if (_pendingHighSurrogate) { _utf8ByteCount += 3; _pendingHighSurrogate = false; }
                return _utf8ByteCount;
            }
        }
    }

    public override void Write(char value)
    {
        lock (_sync)
        {
            target.Write(value);
            Span<char> characters = stackalloc char[1];
            characters[0] = value;
            Count(characters);
        }
    }

    public override void Write(string? value)
    {
        lock (_sync)
        {
            target.Write(value);
            if (value is not null) Count(value.AsSpan());
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            target.WriteLine(value);
            if (value is not null) Count(value.AsSpan());
            Count(Environment.NewLine.AsSpan());
        }
    }

    public override void Flush() => target.Flush();

    private void Count(ReadOnlySpan<char> characters)
    {
        Interlocked.Add(ref _characterCount, characters.Length);
        if (characters.IsEmpty) return;
        if (_pendingHighSurrogate)
        {
            if (char.IsLowSurrogate(characters[0])) { _utf8ByteCount += 4; characters = characters[1..]; }
            else _utf8ByteCount += 3;
            _pendingHighSurrogate = false;
        }
        if (!characters.IsEmpty && char.IsHighSurrogate(characters[^1]))
        {
            _pendingHighSurrogate = true;
            characters = characters[..^1];
        }
        _utf8ByteCount += Encoding.UTF8.GetByteCount(characters);
    }
}
