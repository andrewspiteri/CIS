namespace Cis.Abstractions;

public interface ICisToolUsageRecorder
{
    void Record(CisToolUsageCapture capture);
}

public interface ICisTokenSavingsCollector
{
    void Reset();

    void Add(CisTokenSavingsCandidate candidate);

    IReadOnlyList<CisTokenSavingsCandidate> Snapshot();
}

public sealed record CisToolUsageCapture(
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    long ElapsedMilliseconds,
    int ExitCode,
    int StandardOutputCharacters,
    int StandardErrorCharacters,
    IReadOnlyList<CisTokenSavingsCandidate> SavingsCandidates,
    string? Outcome = null,
    string? BuildIdentity = null)
{
    /// <summary>Trusted command symbols from parsing; raw Arguments are never a substitute.</summary>
    public string? CanonicalCommand { get; init; }
    public IReadOnlyList<string>? RecognizedOptions { get; init; }
    public CisInvocationObservations? Observations { get; init; }
    public long? StandardOutputUtf8Bytes { get; init; }
    public long? StandardErrorUtf8Bytes { get; init; }
}

public sealed record CisTokenSavingsCandidate(
    int BaselineEstimatedTokens,
    int? ActualEstimatedTokens,
    string Basis,
    string Confidence,
    string? Command = null);
