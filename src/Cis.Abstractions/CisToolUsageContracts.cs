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
    IReadOnlyList<CisTokenSavingsCandidate> SavingsCandidates);

public sealed record CisTokenSavingsCandidate(
    int BaselineEstimatedTokens,
    int? ActualEstimatedTokens,
    string Basis,
    string Confidence);
