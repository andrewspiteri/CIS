using System.Diagnostics;
using System.Globalization;

namespace Cis.Abstractions;

/// <summary>Optional, bounded local observations. Identifiers and reason codes must never contain command values or content.</summary>
public static class CisInvocationTelemetry
{
    private static readonly AsyncLocal<CisInvocationTelemetryScope?> Active = new();
    private static readonly IDisposable Disabled = new EmptyScope();
    internal static bool IsActive => Active.Value is not null;

    public static CisInvocationTelemetryScope Begin(string? operation = null)
    {
        var previous = Active.Value;
        var scope = new CisInvocationTelemetryScope(operation, previous, () => Active.Value = previous);
        Active.Value = scope;
        return scope;
    }

    public static IDisposable MeasurePhase(string phase)
        => Active.Value?.StartPhase(phase) ?? Disabled;

    public static void AssociateTask(string changeId, string? taskId)
        => Active.Value?.AssociateTask(changeId, taskId);

    public static void RecordGraph(string consumer, string? snapshotId, string outcome, string reason)
        => Active.Value?.RecordGraph(consumer, snapshotId, outcome, reason);

    public static void RecordVerification(string scope, string disposition, int batches, string reason)
        => Active.Value?.RecordVerification(scope, disposition, batches, reason);

    public static void RecordBlocker(string code) => Active.Value?.RecordBlocker(code);

    /// <summary>Add the complete delivered envelope size; null means the integration did not observe delivery.</summary>
    public static void RecordDeliveredContext(long utf8Bytes) => Active.Value?.RecordDeliveredContext(utf8Bytes);

    internal static void RecordPhase(string phase, double milliseconds) => Active.Value?.RecordPhase(phase, milliseconds);

    internal static bool Identifier(string? value, int maximum = 128)
        => value is { Length: > 0 } && value.Length <= maximum
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':');

    internal static bool Operation(string? value)
        => value is { Length: > 0 and <= 256 } && value.Split(' ') is { Length: <= 8 } parts
            && parts.All(part => Identifier(part, 64));

    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
}

/// <summary>One invocation's observation owner. Durations are inclusive and may overlap; they are not additive wall time.</summary>
public sealed class CisInvocationTelemetryScope : IDisposable
{
    public const int MaximumObservationsPerKind = 16;
    private readonly object _sync = new();
    private readonly Action _restore;
    private readonly Dictionary<string, CisPhaseObservation> _phases = new(StringComparer.Ordinal);
    private readonly List<CisGraphObservation> _graphs = [];
    private readonly List<CisVerificationObservation> _verification = [];
    private readonly HashSet<string> _blockers = new(StringComparer.Ordinal);
    private readonly string? _operation;
    private readonly string? _parent;
    private string? _changeId;
    private string? _taskId;
    private long? _delivered;
    private int _dropped;
    private bool _disposed;

    internal CisInvocationTelemetryScope(string? operation, CisInvocationTelemetryScope? parent, Action restore)
    {
        InvocationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        _parent = parent?.InvocationId;
        _operation = CisInvocationTelemetry.Operation(operation) ? operation : null;
        _restore = restore;
    }

    public string InvocationId { get; }

    public CisInvocationObservations Snapshot()
    {
        lock (_sync)
            return new(InvocationId, _parent, _operation, _changeId, _taskId,
                _phases.Values.OrderBy(phase => phase.Phase, StringComparer.Ordinal).ToArray(),
                _graphs.ToArray(), _verification.ToArray(), _blockers.Order(StringComparer.Ordinal).ToArray(), _delivered, _dropped);
    }

    internal IDisposable StartPhase(string phase)
        => new PhaseScope(this, phase);

    internal void AssociateTask(string changeId, string? taskId)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (!CanonicalId(changeId, "CIS-", 4) || taskId is not null && !CanonicalId(taskId, "WORK-", 3)) { Drop(); return; }
            // One invocation cannot silently relabel observations already associated with another task.
            if (_changeId is not null && (_changeId != changeId || _taskId is not null && _taskId != taskId)) { Drop(); return; }
            _changeId = changeId;
            _taskId = taskId;
        }
    }

    internal void RecordGraph(string consumer, string? snapshotId, string outcome, string reason)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (!CisInvocationTelemetry.Identifier(consumer) || snapshotId is not null && !CisInvocationTelemetry.Identifier(snapshotId)
                || !CisInvocationTelemetry.Identifier(outcome, 32) || !CisInvocationTelemetry.Identifier(reason)) { Drop(); return; }
            var item = new CisGraphObservation(consumer, snapshotId, outcome, reason);
            if (_graphs.Contains(item)) return;
            if (_graphs.Count >= MaximumObservationsPerKind) { Drop(); return; }
            _graphs.Add(item);
        }
    }

    internal void RecordVerification(string scope, string disposition, int batches, string reason)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (!CisInvocationTelemetry.Identifier(scope) || disposition is not ("selected" or "executed" or "reused" or "invalidated")
                || batches < 0 || !CisInvocationTelemetry.Identifier(reason)) { Drop(); return; }
            var item = new CisVerificationObservation(scope, disposition, batches, reason);
            if (_verification.Contains(item)) return;
            if (_verification.Count >= MaximumObservationsPerKind) { Drop(); return; }
            _verification.Add(item);
        }
    }

    internal void RecordBlocker(string code)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (!CisInvocationTelemetry.Identifier(code)) { Drop(); return; }
            if (_blockers.Contains(code)) return;
            if (_blockers.Count >= MaximumObservationsPerKind) { Drop(); return; }
            _blockers.Add(code);
        }
    }

    internal void RecordDeliveredContext(long utf8Bytes)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (utf8Bytes < 0 || utf8Bytes > long.MaxValue - (_delivered ?? 0)) { Drop(); return; }
            _delivered = (_delivered ?? 0) + utf8Bytes;
        }
    }

    internal void RecordPhase(string phase, double milliseconds)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (!CisInvocationTelemetry.Identifier(phase) || !double.IsFinite(milliseconds) || milliseconds < 0) { Drop(); return; }
            if (_phases.TryGetValue(phase, out var previous))
            {
                _phases[phase] = previous with
                {
                    ElapsedMilliseconds = previous.ElapsedMilliseconds + milliseconds,
                    Count = previous.Count == int.MaxValue ? int.MaxValue : previous.Count + 1
                };
                return;
            }
            if (_phases.Count >= MaximumObservationsPerKind) { Drop(); return; }
            _phases.Add(phase, new(phase, milliseconds, 1));
        }
    }

    private void Drop() { if (_dropped < int.MaxValue) _dropped++; }

    private static bool CanonicalId(string? value, string prefix, int digits)
        => value is not null && value.Length == prefix.Length + digits && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _restore();
    }

    private sealed class PhaseScope(CisInvocationTelemetryScope owner, string phase) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.RecordPhase(phase, Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
        }
    }
}

public sealed record CisInvocationObservations(string InvocationId, string? ParentInvocationId, string? Operation,
    string? ChangeId, string? TaskId, IReadOnlyList<CisPhaseObservation> Phases, IReadOnlyList<CisGraphObservation> Graph,
    IReadOnlyList<CisVerificationObservation> Verification, IReadOnlyList<string> BlockerCodes,
    long? DeliveredContextUtf8Bytes, int DroppedObservations);

public sealed record CisPhaseObservation(string Phase, double ElapsedMilliseconds, int Count);
public sealed record CisGraphObservation(string Consumer, string? SnapshotId, string Outcome, string Reason);
public sealed record CisVerificationObservation(string Scope, string Disposition, int Batches, string Reason);
