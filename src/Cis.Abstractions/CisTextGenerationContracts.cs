namespace Cis.Abstractions;

public interface ICisTextGenerationService
{
    CisAiStatus GetStatus();

    CisTextGenerationResult Generate(CisTextGenerationRequest request);
}

public sealed record CisAiModel(
    string Name,
    long? SizeBytes = null);

public sealed record CisAiProviderStatus(
    string Name,
    string Status,
    string Endpoint,
    bool IsLocal,
    IReadOnlyList<CisAiModel> Models,
    string? Detail)
{
    public bool IsAvailable => string.Equals(Status, "available", StringComparison.Ordinal);
}

public sealed record CisAiStatus(
    IReadOnlyList<CisAiProviderStatus> Providers);

public sealed record CisTextGenerationRequest(
    string Prompt,
    string? Provider = null,
    string? Model = null,
    bool AllowRemote = false,
    int TimeoutSeconds = 120,
    int MaxOutputTokens = 2_048,
    bool JsonMode = false)
{
    public string? JsonSchema { get; init; }
    public int? ContextWindowTokens { get; init; }
}

public sealed record CisTextGenerationResult(
    string Status,
    string? Provider,
    string? Model,
    string? Text,
    string? Detail,
    bool IsLocal)
{
    public bool IsSuccess => string.Equals(Status, "generated", StringComparison.Ordinal);
}

public sealed class UnavailableTextGenerationService : ICisTextGenerationService
{
    public CisAiStatus GetStatus() => new([]);

    public CisTextGenerationResult Generate(CisTextGenerationRequest request) =>
        new("unavailable", request.Provider, request.Model, null,
            "No CIS AI provider module is loaded.", true);
}
