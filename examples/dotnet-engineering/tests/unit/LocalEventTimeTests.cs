using System.Globalization;
using System.Text.Json;
using Example.Cli;

namespace Example.UnitTests;

public sealed class LocalEventTimeTests
{
    private static readonly JsonSerializerOptions SeasonalOptions = CreateOptions(SeasonalZone());
    private static readonly JsonSerializerOptions WesternOptions = CreateOptions(
        TimeZoneInfo.CreateCustomTimeZone("SyntheticWest", TimeSpan.FromHours(-5), "West", "West"));
    [Theory]
    [InlineData("2026-01-15T12:30:00.123456", "2026-01-15T10:30:00.123456Z")]
    [InlineData("2026-06-15T12:30:00.123456", "2026-06-15T09:30:00.123456Z")]
    [InlineData("2026-01-15T12:30:00Z", "2026-01-15T12:30:00Z")]
    [InlineData("2026-01-15T12:30:00+00:00", "2026-01-15T12:30:00Z")]
    public void ResolvesLocalTimeUsingTheOffsetAtThatDate(string input, string expected)
    {
        var result = Read(input, SeasonalOptions);
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), result);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void LocalTimeCanCrossIntoTheNextUtcDate()
    {
        Assert.Equal(new DateTimeOffset(2026, 1, 16, 4, 30, 0, TimeSpan.Zero), Read("2026-01-15T23:30:00", WesternOptions));
    }

    [Fact]
    public void ExplicitNonzeroOffsetRemainsVisibleToCoreValidation()
    {
        var result = Read("2026-01-15T12:30:00+02:00", SeasonalOptions);
        Assert.Equal(TimeSpan.FromHours(2), result.Offset);
        Assert.Throws<ArgumentException>(() => new Example.Core.Reading(Guid.NewGuid(), result, 1).Validate());
    }

    [Theory]
    [InlineData("2026-03-31T02:30:00")]
    [InlineData("2026-10-31T02:30:00")]
    [InlineData("0001-01-01T00:00:00")]
    public void RejectsNonexistentAmbiguousOrOutOfRangeInstants(string input)
        => Assert.Throws<JsonException>(() => Read(input, SeasonalOptions));

    private static DateTimeOffset Read(string input, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<DateTimeOffset>(JsonSerializer.Serialize(input), options);

    private static JsonSerializerOptions CreateOptions(TimeZoneInfo zone)
        => new() { Converters = { new LocalEventTimeConverter(zone) } };

    private static TimeZoneInfo SeasonalZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 31),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0, DateTimeKind.Unspecified), 10, 31));
        return TimeZoneInfo.CreateCustomTimeZone("SyntheticSeasonal", TimeSpan.FromHours(2), "Seasonal", "Standard", "Summer", [rule]);
    }
}
