using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Ai;

internal sealed class OpenAiCompatibleProvider : IAiProvider
{
    public string Name => "openai-compatible";

    public CisAiProviderStatus GetStatus()
    {
        var endpoint = Environment.GetEnvironmentVariable("CIS_AI_ENDPOINT")?.Trim().TrimEnd('/');
        var model = Environment.GetEnvironmentVariable("CIS_AI_MODEL")?.Trim();
        if (!TryGetEndpoint(endpoint, out _, out var error) || string.IsNullOrWhiteSpace(model))
        {
            return new(Name, "unavailable", endpoint ?? string.Empty, false, [],
                error ?? "CIS_AI_MODEL is not configured.");
        }

        return new(Name, "available", endpoint!, false, [new CisAiModel(model)], null);
    }

    public CisTextGenerationResult Generate(CisTextGenerationRequest request, string model)
    {
        var endpoint = Environment.GetEnvironmentVariable("CIS_AI_ENDPOINT")?.Trim().TrimEnd('/');
        if (!TryGetEndpoint(endpoint, out var uri, out var error))
        {
            return Failure(model, error!);
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 600)),
            };
            var apiKey = Environment.GetEnvironmentVariable("CIS_AI_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            using var response = client.PostAsJsonAsync(uri, new
            {
                model,
                temperature = 0.1,
                max_tokens = Math.Clamp(request.MaxOutputTokens, 32, 32_768),
                messages = new[]
                {
                    new { role = "system", content = "Return only the requested concise routing description." },
                    new { role = "user", content = request.Prompt },
                },
            }).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return Failure(model,
                    $"Remote provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            var text = document.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(text)
                ? Failure(model, "Remote provider returned no generated text.")
                : new("generated", Name, model, text.Trim(), null, false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or IOException
            or KeyNotFoundException
            or InvalidOperationException)
        {
            return Failure(model, exception.Message);
        }
    }

    private static bool TryGetEndpoint(string? endpoint, out Uri? uri, out string? error)
    {
        var requestEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : endpoint + "/v1/chat/completions";
        if (Uri.TryCreate(requestEndpoint, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            error = null;
            return true;
        }

        error = "CIS_AI_ENDPOINT is not a valid HTTP endpoint.";
        return false;
    }

    private CisTextGenerationResult Failure(string? model, string detail) =>
        new("failed", Name, model, null, detail, false);
}
