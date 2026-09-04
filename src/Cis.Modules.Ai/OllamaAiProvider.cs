using System.Net.Http.Json;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Ai;

internal sealed class OllamaAiProvider : ICisAiProvider
{
    private const string DefaultEndpoint = "http://127.0.0.1:11434";

    public string Name => "ollama";

    public CisAiProviderStatus GetStatus()
    {
        var endpoint = ResolveEndpoint();
        if (!TryGetBaseUri(endpoint, out var baseUri, out var error))
        {
            return Unavailable(endpoint, error!);
        }

        try
        {
            using var client = CreateClient(baseUri!, TimeSpan.FromSeconds(2));
            using var response = client.GetAsync("/api/tags").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(endpoint,
                    $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            return new(Name, "available", endpoint, true, ReadModels(document.RootElement), null);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or IOException)
        {
            return Unavailable(endpoint, exception.Message);
        }
    }

    public CisTextGenerationResult Generate(CisTextGenerationRequest request, string model)
    {
        var endpoint = ResolveEndpoint();
        if (!TryGetBaseUri(endpoint, out var baseUri, out var error))
        {
            return Failure(model, error!);
        }

        try
        {
            using var client = CreateClient(baseUri!, TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 600)));
            using var response = client.PostAsJsonAsync("/api/generate", new
            {
                model,
                prompt = request.Prompt,
                stream = false,
                format = request.JsonMode ? "json" : null,
                options = new
                {
                    temperature = 0.1,
                    num_predict = Math.Clamp(request.MaxOutputTokens, 32, 32_768),
                },
            }).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return Failure(model,
                    $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            var text = document.RootElement.TryGetProperty("response", out var generated)
                && generated.ValueKind == JsonValueKind.String
                    ? generated.GetString()
                    : null;
            return string.IsNullOrWhiteSpace(text)
                ? Failure(model, "Ollama returned no generated text.")
                : new("generated", Name, model, text.Trim(), null, true);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or IOException)
        {
            return Failure(model, exception.Message);
        }
    }

    private static HttpClient CreateClient(Uri baseUri, TimeSpan timeout) => new()
    {
        BaseAddress = baseUri,
        Timeout = timeout,
    };

    private static IReadOnlyList<CisAiModel> ReadModels(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return models.EnumerateArray()
            .Select(model =>
            {
                var name = model.TryGetProperty("name", out var nameValue)
                    ? nameValue.GetString()
                    : model.TryGetProperty("model", out var modelValue) ? modelValue.GetString() : null;
                long? size = model.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsed)
                    ? parsed
                    : null;
                return string.IsNullOrWhiteSpace(name) ? null : new CisAiModel(name, size);
            })
            .Where(model => model is not null)
            .Cast<CisAiModel>()
            .DistinctBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
        {
            endpoint = "http://" + endpoint;
        }

        return endpoint.TrimEnd('/');
    }

    private static bool TryGetBaseUri(string endpoint, out Uri? uri, out string? error)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            error = null;
            return true;
        }

        error = "OLLAMA_HOST is not a valid HTTP endpoint.";
        return false;
    }

    private CisAiProviderStatus Unavailable(string endpoint, string detail) =>
        new(Name, "unavailable", endpoint, true, [], detail);

    private CisTextGenerationResult Failure(string? model, string detail) =>
        new("failed", Name, model, null, detail, true);
}
