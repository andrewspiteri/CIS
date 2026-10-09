using System.Text.Json;

namespace Example.BusinessTests;

/// <summary>Inputs that native JSON conversion used to round or truncate before validation.</summary>
internal static class PrecisionInputCases
{
    private static readonly Dictionary<string, (string Field, object Value)> Cases = new(StringComparer.Ordinal)
    {
        ["long-time-fraction"] = ("observedAt", "1970-01-01T00:00:00.00000001Z"),
        ["long-time-tail"] = ("observedAt", "1970-01-01T00:00:00.123456001Z"),
        ["quantity-rounding"] = ("quantity", JsonSerializer.Deserialize<JsonElement>("99999999999999999999.999999991")),
        ["quantity-underflow"] = ("quantity", JsonSerializer.Deserialize<JsonElement>("0.00000000000000000000000000001")),
        ["quoted-quantity-rounding"] = ("quantity", "99999999999999999999.999999991"),
        ["quoted-quantity-underflow"] = ("quantity", "0.00000000000000000000000000001"),
        ["quantity-exponent-underflow"] = ("quantity", JsonSerializer.Deserialize<JsonElement>("1e-29")),
        ["quoted-quantity-exponent-underflow"] = ("quantity", "1e-29"),
    };

    internal static bool TryApply(string scenario, Dictionary<string, object?> reading)
    {
        if (!Cases.TryGetValue(scenario, out var input)) return false;
        reading[input.Field] = input.Value;
        return true;
    }
}
