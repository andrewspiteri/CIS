using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cis.FunctionProfiler;

// Deliberately outside Cis.Abstractions: normal CIS builds have no profiler dependency.
// Only method identities and numeric measurements are recorded, never arguments or content.
public static class Probe
{
    private static readonly string? Output = Environment.GetEnvironmentVariable("CIS_PROFILE_OUTPUT");
    private static readonly long Started = Stopwatch.GetTimestamp();
    private static readonly ConcurrentBag<State> States = [];
    [ThreadStatic] private static State? _state;
    private static int _flushed;

    static Probe()
    {
        if (!string.IsNullOrWhiteSpace(Output))
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => Flush();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Enter(int id)
    {
        if (string.IsNullOrWhiteSpace(Output) || Volatile.Read(ref _flushed) != 0) return;
        var entry = Stopwatch.GetTimestamp();
        var state = _state;
        if (state is null)
        {
            state = _state = new State(Environment.CurrentManagedThreadId);
            States.Add(state);
        }
        if (!state.Methods.TryGetValue(id, out var metric))
        {
            metric = new Metric(id, -1, state.ThreadId);
            state.Methods.Add(id, metric);
            state.Published.Add(metric);
        }
        var caller = state.Depth == 0 ? -1 : state.Frames[state.Depth - 1].Metric.Id;
        var key = ((long)(caller + 1) << 32) | (uint)id;
        if (!state.Edges.TryGetValue(key, out var edge))
        {
            edge = new Metric(id, caller, state.ThreadId);
            state.Edges.Add(key, edge);
            state.PublishedEdges.Add(edge);
        }
        metric.Calls++;
        edge.Calls++;
        if (state.Depth == state.Frames.Length) Array.Resize(ref state.Frames, state.Depth * 2);
        var started = Stopwatch.GetTimestamp();
        state.Frames[state.Depth++] = new Frame(metric, edge, entry, started);
        state.OverheadTicks += started - entry;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Exit()
    {
        if (string.IsNullOrWhiteSpace(Output) || Volatile.Read(ref _flushed) != 0) return;
        var ended = Stopwatch.GetTimestamp();
        var state = _state!;
        var frame = state.Frames[--state.Depth];
        var inclusive = ended - frame.Started;
        var self = Math.Max(0, inclusive - frame.Children);
        Add(frame.Metric, inclusive, self);
        Add(frame.Edge, inclusive, self);
        var finished = Stopwatch.GetTimestamp();
        if (state.Depth > 0) state.Frames[state.Depth - 1].Children += finished - frame.Entry;
        state.OverheadTicks += finished - ended;
    }

    private static void Add(Metric metric, long inclusive, long self)
    {
        metric.InclusiveTicks += inclusive;
        metric.SelfTicks += self;
        metric.MaxTicks = Math.Max(metric.MaxTicks, inclusive);
        metric.Completed++;
    }

    public static void Flush()
    {
        if (string.IsNullOrWhiteSpace(Output) || Interlocked.Exchange(ref _flushed, 1) != 0) return;
        var ended = Stopwatch.GetTimestamp();
        try
        {
            var path = Path.GetFullPath(Output.Replace("{pid}", Environment.ProcessId.ToString()));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Concurrent bags publish immutable identities. Snapshotting never enumerates a
            // live thread's ordinary dictionaries; still-active calls are reported separately.
            var states = States.ToArray();
            var report = new
            {
                schemaVersion = 1, processId = Environment.ProcessId,
                measuredAtUtc = DateTimeOffset.UtcNow, stopwatchFrequency = Stopwatch.Frequency,
                observedMs = Milliseconds(ended - Started),
                probeBookkeepingMs = Milliseconds(states.Sum(s => Interlocked.Read(ref s.OverheadTicks))),
                semantics = "Physical managed invocations. Async/iterator MoveNext rows count execution segments, not logical operations. Inclusive totals overlap; self includes uninstrumented framework/I/O time. Probe costs and JIT effects perturb timings.",
                methods = states.SelectMany(s => s.Published).Select(Snapshot).ToArray(),
                edges = states.SelectMany(s => s.PublishedEdges).Select(Snapshot).ToArray()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"cis.function-profile: report could not be written ({error.GetType().Name}).");
        }
    }

    private static object Snapshot(Metric metric) => new
    {
        id = metric.Id, callerId = metric.CallerId, threadId = metric.ThreadId,
        calls = Interlocked.Read(ref metric.Calls), completed = Interlocked.Read(ref metric.Completed),
        inclusiveMs = Milliseconds(Interlocked.Read(ref metric.InclusiveTicks)),
        selfMs = Milliseconds(Interlocked.Read(ref metric.SelfTicks)),
        maxMs = Milliseconds(Interlocked.Read(ref metric.MaxTicks))
    };
    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private sealed class State(int threadId)
    {
        public readonly int ThreadId = threadId;
        public readonly Dictionary<int, Metric> Methods = [];
        public readonly Dictionary<long, Metric> Edges = [];
        public readonly ConcurrentBag<Metric> Published = [];
        public readonly ConcurrentBag<Metric> PublishedEdges = [];
        public Frame[] Frames = new Frame[128];
        public int Depth;
        public long OverheadTicks;
    }
    private sealed class Metric(int id, int callerId, int threadId)
    {
        public readonly int Id = id, CallerId = callerId, ThreadId = threadId;
        public long Calls, Completed, InclusiveTicks, SelfTicks, MaxTicks;
    }
    private struct Frame(Metric metric, Metric edge, long entry, long started)
    {
        public readonly Metric Metric = metric, Edge = edge;
        public readonly long Entry = entry, Started = started;
        public long Children;
    }
}
