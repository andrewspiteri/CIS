namespace Example.MigrationTests;

// Deliberately retained synthetic BEFORE fixture, not recommended application code.
// Routing, interval construction and boundary policy are concentrated here.
internal static class LegacyRouting
{
    internal static int Assignments(int value)
    {
        var assigned = 0;
        foreach (var bounds in new[] { new[] { 0, 10 }, new[] { 10, 20 } })
            if (value >= bounds[0] && value <= bounds[1]) assigned++;
        return assigned;
    }
}
