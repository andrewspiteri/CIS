using System.Text.Json;

namespace Cis.Modules.Api;

public sealed partial class OpenApiCompatibilityService
{
    // Flatten supported paths for the existing direction-aware comparison. Coverage gaps
    // are retained explicitly; a partial traversal must never be reported as compatible.
    private static JsonElement FlattenSchema(JsonElement root, JsonElement schema, ISet<string> limitations, string surface)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var references = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonElement value, string path, int depth)
        {
            if (depth > 64 || value.ValueKind != JsonValueKind.Object)
            { limitations.Add($"{surface} {path}: unsupported schema or traversal depth."); return; }
            string? reference = null;
            if (value.TryGetProperty("$ref", out var refValue))
            {
                reference = refValue.GetString();
                if (reference is null || !reference.StartsWith("#/", StringComparison.Ordinal))
                { limitations.Add($"{surface} {path}: external or invalid reference."); return; }
                if (!references.Add(reference))
                { limitations.Add($"{surface} {path}: recursive reference; finite paths compared only."); return; }
                var target = root;
                foreach (var part in reference[2..].Split('/'))
                {
                    if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal), out target))
                    { limitations.Add($"{surface} {path}: unresolved reference."); references.Remove(reference); return; }
                }
                Visit(target, path, depth + 1);
                references.Remove(reference);
                if (value.EnumerateObject().Any(item => item.Name != "$ref"))
                    limitations.Add($"{surface} {path}: reference siblings require review.");
                return;
            }
            foreach (var keyword in new[] { "allOf", "oneOf", "anyOf", "not", "if", "then", "else", "additionalProperties", "patternProperties", "unevaluatedProperties", "prefixItems", "contains", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "pattern", "minItems", "maxItems", "uniqueItems", "minProperties", "maxProperties", "const", "format", "discriminator", "readOnly", "writeOnly" })
                if (value.TryGetProperty(keyword, out _)) limitations.Add($"{surface} {path}: {keyword} semantics are not compared.");
            properties[path] = value.Clone();
            if (value.TryGetProperty("nullable", out _))
                limitations.Add($"{surface} {path}: directional nullable semantics require review.");
            if (value.TryGetProperty("properties", out var children) && children.ValueKind == JsonValueKind.Object)
            {
                var names = Required(value);
                foreach (var child in children.EnumerateObject())
                {
                    var name = path.Length == 0 ? EscapePath(child.Name) : path + "/" + EscapePath(child.Name);
                    if (names.Contains(child.Name)) required.Add(name);
                    Visit(child.Value, name, depth + 1);
                }
            }
            if (value.TryGetProperty("items", out var items)) Visit(items, path + "/[]", depth + 1);
            foreach (var composition in new[] { "allOf", "oneOf", "anyOf" })
                if (value.TryGetProperty(composition, out var branches) && branches.ValueKind == JsonValueKind.Array)
                    foreach (var branch in branches.EnumerateArray()) Visit(branch, path, depth + 1);
        }
        Visit(schema, "", 0);
        return JsonSerializer.SerializeToElement(new { properties, required = required.Order(StringComparer.Ordinal).ToArray() });
    }

    private static string EscapePath(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
