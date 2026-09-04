namespace Cis.Abstractions;

/// <summary>
/// Provider-neutral model boundary. Provider modules may register additional implementations;
/// provider-name collisions are rejected instead of being resolved by load order.
/// </summary>
public interface ICisAiProvider
{
    string Name { get; }

    CisAiProviderStatus GetStatus();

    CisTextGenerationResult Generate(CisTextGenerationRequest request, string model);
}
