using System.Text.Json;

namespace Example.BusinessTests;

internal static class CliInputCases
{
    internal const string PrivateInputMarker = "synthetic-input-marker-not-for-output";

    internal static Dictionary<string, object?> ValidReading() => new()
    {
        ["id"] = Guid.NewGuid(),
        ["observedAt"] = "1970-01-01T00:00:00Z",
        ["quantity"] = 1.25m,
    };

    internal static string RejectedPayload(string scenario)
    {
        var reading = ValidReading();
        if (scenario.StartsWith("missing-", StringComparison.Ordinal))
            reading.Remove(scenario[8..]);
        else if (scenario is "null-id" or "null-observedAt" or "null-quantity")
            reading[scenario[5..]] = null;
        else if (PrecisionInputCases.TryApply(scenario, reading))
            return JsonSerializer.Serialize(new[] { reading });
        else
        {
            switch (scenario)
            {
                case "empty-id": reading["id"] = Guid.Empty; break;
                case "offset-time": reading["observedAt"] = "1970-01-01T01:00:00+01:00"; break;
                case "sub-microsecond": reading["observedAt"] = "1970-01-01T00:00:00.0000001Z"; break;
                case "negative-quantity": reading["quantity"] = -1m; break;
                case "excess-precision": reading["quantity"] = 0.123456789m; break;
                case "excess-range": reading["quantity"] = 100000000000000000000m; break;
                case "null-entry": return "[null]";
                case "null-root": return "null";
                case "object-root": return "{}";
                case "malformed": return "[" + PrivateInputMarker;
                case "oversized": return "[]".PadRight(1_048_577);
                case "too-many-records": return JsonSerializer.Serialize(Enumerable.Repeat(reading, 10_001));
                case "valid-then-invalid":
                    reading["id"] = Guid.Empty;
                    return JsonSerializer.Serialize(new[] { ValidReading(), reading });
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }
        return JsonSerializer.Serialize(new[] { reading });
    }
}
