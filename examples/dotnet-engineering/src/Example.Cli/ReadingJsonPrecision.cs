using System.Text.Json;

namespace Example.Cli;

/// <summary>Rejects information loss in supplied JSON before a reading can reach application code.</summary>
internal static class ReadingJsonPrecision
{
    internal static void Validate(JsonElement reading)
    {
        foreach (var property in reading.EnumerateObject())
        {
            if (property.Name.Equals("observedAt", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                ValidateTime(property.Value.GetString()!);
            else if (property.Name.Equals("quantity", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                ExactQuantity.Validate(property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()! : property.Value.GetRawText());
        }
    }

    private static void ValidateTime(string value)
    {
        var fraction = value.IndexOf('.', StringComparison.Ordinal);
        if (fraction < 0) return;
        for (var index = fraction + 1; index < value.Length && char.IsAsciiDigit(value[index]); index++)
        {
            if (index > fraction + 6 && value[index] != '0')
                throw new JsonException("Event time must have exact microsecond precision.");
        }
    }
}
