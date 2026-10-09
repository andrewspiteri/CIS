using System.Text.Json;

namespace Cis.Modules.Plan;

internal static class EngineeringEvidenceJson
{
    internal static string Text(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
    internal static double Number(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var result) && double.IsFinite(result) ? result : -1;
}
