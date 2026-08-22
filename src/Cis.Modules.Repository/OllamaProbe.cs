using System.Text.Json;

namespace Cis.Modules.Repository;

public interface IOllamaProbe
{
    OllamaProbeResult Probe();
}

public sealed record OllamaProbeResult(
    string Status,
    string Endpoint,
    IReadOnlyList<string> Models,
    string? Detail)
{
    public bool IsAvailable => string.Equals(Status, "available", StringComparison.Ordinal);
}

public sealed class OllamaProbe : IOllamaProbe
{
    private const string DefaultEndpoint = "http://127.0.0.1:11434";

    public OllamaProbeResult Probe()
    {
        var configuredEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        var endpoint = NormalizeEndpoint(configuredEndpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return Unavailable(endpoint, "OLLAMA_HOST is not a valid HTTP endpoint.");
        }

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(2),
            };
            using var response = client.GetAsync("/api/tags").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(
                    endpoint,
                    $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            using var content = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(content);
            var models = ReadModels(document.RootElement);
            return new OllamaProbeResult("available", endpoint, models, null);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or IOException)
        {
            return Unavailable(endpoint, exception.Message);
        }
    }

    private static string NormalizeEndpoint(string? configuredEndpoint)
    {
        var endpoint = string.IsNullOrWhiteSpace(configuredEndpoint)
            ? DefaultEndpoint
            : configuredEndpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
        {
            endpoint = "http://" + endpoint;
        }

        return endpoint.TrimEnd('/');
    }

    private static IReadOnlyList<string> ReadModels(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("models", out var modelsElement)
            || modelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return modelsElement
            .EnumerateArray()
            .Select(model => ReadModelName(model))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReadModelName(JsonElement model)
    {
        if (model.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var propertyName in new[] { "name", "model" })
        {
            if (model.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static OllamaProbeResult Unavailable(string endpoint, string detail)
        => new("unavailable", endpoint, [], detail);
}
