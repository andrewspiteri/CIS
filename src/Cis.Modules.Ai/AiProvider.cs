using Cis.Abstractions;

namespace Cis.Modules.Ai;

internal interface IAiProvider
{
    string Name { get; }

    CisAiProviderStatus GetStatus();

    CisTextGenerationResult Generate(CisTextGenerationRequest request, string model);
}
