namespace Example.MigrationTests;

// AFTER fixture: interval policy is one value type; routing receives its data explicitly.
internal readonly record struct Window(int Start, int End)
{
    internal bool Contains(int value) => value >= Start && value < End;
}

internal sealed class WindowRouting(IReadOnlyList<Window> windows)
{
    internal int Assignments(int value) => windows.Count(window => window.Contains(value));
}
