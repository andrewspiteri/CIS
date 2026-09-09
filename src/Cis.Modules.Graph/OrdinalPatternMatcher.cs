namespace Cis.Modules.Graph;

/// <summary>Finds many ordinal substrings in one pass without changing String.Contains semantics.</summary>
internal sealed class OrdinalPatternMatcher
{
    private sealed class State
    {
        public Dictionary<char, int> Next { get; } = [];
        public int Failure { get; set; }
        public int OutputLink { get; set; }
        public string? Pattern { get; set; }
    }

    private readonly List<State> _states = [new()];

    public OrdinalPatternMatcher(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns.Distinct(StringComparer.Ordinal))
        {
            if (pattern.Length == 0) throw new ArgumentException("Patterns must not be empty.", nameof(patterns));
            var state = 0;
            foreach (var character in pattern)
            {
                if (!_states[state].Next.TryGetValue(character, out var next))
                {
                    next = _states.Count;
                    _states[state].Next.Add(character, next);
                    _states.Add(new());
                }
                state = next;
            }
            _states[state].Pattern = pattern;
        }

        var pending = new Queue<int>(_states[0].Next.Values);
        while (pending.TryDequeue(out var parent))
        {
            foreach (var (character, child) in _states[parent].Next)
            {
                var fallback = _states[parent].Failure;
                while (fallback != 0 && !_states[fallback].Next.ContainsKey(character)) fallback = _states[fallback].Failure;
                _states[child].Failure = _states[fallback].Next.GetValueOrDefault(character);
                var failure = _states[child].Failure;
                _states[child].OutputLink = _states[failure].Pattern is null ? _states[failure].OutputLink : failure;
                pending.Enqueue(child);
            }
        }
    }

    public IReadOnlySet<string> Find(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var state = 0;
        foreach (var character in text)
        {
            while (state != 0 && !_states[state].Next.ContainsKey(character)) state = _states[state].Failure;
            state = _states[state].Next.GetValueOrDefault(character);
            for (var output = state; output != 0; output = _states[output].OutputLink)
                if (_states[output].Pattern is { } pattern) found.Add(pattern);
        }
        return found;
    }
}
