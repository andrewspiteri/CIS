using Example.Core;

namespace Example.UnitTests;

public sealed class ReadingTests
{
    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 InvalidQuantityIsRejected")]
    [InlineData("-1")]
    [InlineData("0.123456789")]
    [InlineData("100000000000000000000")]
    public void InvalidQuantityIsRejected(string value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).Validate());
        Assert.Contains("nonnegative", exception.Message, StringComparison.Ordinal);
        Assert.Contains("eight decimal places", exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 ValidQuantityRetainsExactPrecision")]
    [InlineData("0")]
    [InlineData("0.00000001")]
    [InlineData("100.12345678")]
    [InlineData("99999999999999999999.99999999")]
    public void ValidQuantityRetainsExactPrecision(string value)
    {
        var quantity = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var id = Guid.NewGuid();
        var observedAt = DateTimeOffset.UnixEpoch.AddTicks(10);
        var reading = new Reading(id, observedAt, quantity);
        reading.Validate();
        Assert.Equal(id, reading.Id);
        Assert.Equal(observedAt, reading.ObservedAt);
        Assert.Equal(quantity, reading.Quantity);
    }

    [Fact(DisplayName = "TC-FEAT-FR-001-001-001 MissingIdentityAndNonUtcTimeAreRejected")]
    public void MissingIdentityAndNonUtcTimeAreRejected()
    {
        var identity = Assert.Throws<ArgumentException>(() => new Reading(Guid.Empty, DateTimeOffset.UnixEpoch, 1).Validate());
        Assert.Contains("stable identity", identity.Message, StringComparison.Ordinal);
        var time = Assert.Throws<ArgumentException>(() => new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)), 1).Validate());
        Assert.Contains("UTC", time.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "TC-FEAT-FR-001-001-001 EventTimeMustRoundTripWithoutLosingSubMicrosecondTicks")]
    public void EventTimeMustRoundTripWithoutLosingSubMicrosecondTicks()
    {
        var time = DateTimeOffset.UnixEpoch.AddTicks(10);
        var reading = new Reading(Guid.NewGuid(), time, 1);
        reading.Validate();
        Assert.Equal(time, reading.ObservedAt);
        var exception = Assert.Throws<ArgumentException>(() => (reading with { ObservedAt = time.AddTicks(1) }).Validate());
        Assert.Contains("microsecond precision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "architecture")]
    public void CoreDoesNotDependOnPersistenceOrCliAdapters()
    {
        var references = typeof(Reading).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
        Assert.DoesNotContain("Npgsql", references);
        Assert.DoesNotContain("System.CommandLine", references);
        Assert.DoesNotContain("Example.Persistence", references);
        Assert.NotEmpty(typeof(Reading).Assembly.GetTypes());
    }
}
