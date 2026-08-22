using Cis.Abstractions;

namespace Cis.Modules.Feedback;

public sealed class TokenSavingsCollector : ICisTokenSavingsCollector
{
    private readonly List<CisTokenSavingsCandidate> _candidates = [];

    public void Reset() => _candidates.Clear();

    public void Add(CisTokenSavingsCandidate candidate)
    {
        if (candidate.BaselineEstimatedTokens < 0 || candidate.ActualEstimatedTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Token estimates must not be negative.");
        }

        _candidates.Add(candidate);
    }

    public IReadOnlyList<CisTokenSavingsCandidate> Snapshot() => _candidates.ToArray();
}
