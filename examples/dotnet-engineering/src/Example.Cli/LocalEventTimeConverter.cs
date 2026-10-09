using System.Text.Json;
using System.Text.Json.Serialization;

namespace Example.Cli;

/// <summary>Resolves offset-free CLI timestamps using the process's local timezone.</summary>
internal sealed class LocalEventTimeConverter(TimeZoneInfo localTimeZone) : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var timestamp = reader.GetDateTime();
        if (timestamp.Kind != DateTimeKind.Unspecified) return reader.GetDateTimeOffset();
        if (localTimeZone.IsInvalidTime(timestamp) || localTimeZone.IsAmbiguousTime(timestamp))
            throw new JsonException("Local event time must identify one instant; use an explicit UTC timestamp at a daylight-saving transition.");
        try
        {
            return new DateTimeOffset(timestamp, localTimeZone.GetUtcOffset(timestamp)).ToUniversalTime();
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Local event time is outside the supported UTC range.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
