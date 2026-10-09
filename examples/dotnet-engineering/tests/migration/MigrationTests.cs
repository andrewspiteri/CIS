namespace Example.MigrationTests;

public sealed class MigrationTests
{
    [Theory]
    [InlineData("OLD-001", -1, 0)]
    [InlineData("OLD-002", 0, 1)]
    [InlineData("OLD-003", 5, 1)]
    [InlineData("OLD-004", 9, 1)]
    [InlineData("OLD-005", 15, 1)]
    [InlineData("OLD-006", 21, 0)]
    public void NativeCasesPreserveMappedLegacyAssertions(string oldCase, int value, int expected)
    {
        Assert.Contains(oldCase, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "legacy-checks.txt")), StringComparison.Ordinal);
        Assert.Equal(expected, LegacyRouting.Assignments(value));
        Assert.Equal(expected, new WindowRouting([new(0, 10), new(10, 20)]).Assignments(value));
    }

    [Fact]
    public void OmittedBoundaryExposesLegacyDefectAndVerifiesExtractedPolicy()
    {
        Assert.Equal(2, LegacyRouting.Assignments(10));
        var routing = new WindowRouting([new(0, 10), new(10, 20)]);
        Assert.Equal(1, routing.Assignments(10));
        Assert.Equal(0, routing.Assignments(20));
    }

    [Fact]
    public void RoutingCanUseDifferentIntervalsWithoutReplacingBoundaryPolicy()
    {
        var routing = new WindowRouting([new(-10, 0), new(0, 30)]);
        Assert.Equal(1, routing.Assignments(-10));
        Assert.Equal(1, routing.Assignments(0));
        Assert.Equal(0, routing.Assignments(30));
    }
}
