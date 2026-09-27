using Cis.Abstractions;

namespace Cis.Modules.Repository;

// Keeps account-backed guidance providers out of general automatic AI routing.
internal sealed class RepositoryGuidanceGenerationService(ICisTextGenerationService? general,
    IReadOnlyList<ICisRepositoryGuidanceProvider> specialized) : ICisTextGenerationService
{
    public CisAiStatus GetStatus() => new(specialized.Select(provider => provider.GetStatus())
        .Concat(general?.GetStatus().Providers ?? []).ToArray());

    public CisTextGenerationResult Generate(CisTextGenerationRequest request)
    {
        var matches = specialized.Where(provider => provider.Name == request.Provider).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && (general?.GetStatus().Providers.Any(provider => provider.Name == request.Provider) ?? false))
            return new("provider-conflict", request.Provider, request.Model, null, "The selected provider name is ambiguous.", false);
        if (matches.Length == 1)
        {
            var status = matches[0].GetStatus();
            if (!status.IsLocal && !request.AllowRemote)
                return new("remote-approval-required", request.Provider, request.Model, null, "Authorize sending the selected guidance first.", false);
            if (!status.IsAvailable || !status.Models.Any(model => model.Name == request.Model))
                return new("model-unavailable", request.Provider, request.Model, null, "The selected guidance model is unavailable.", status.IsLocal);
            return matches[0].Generate(request, request.Model!);
        }
        return (general ?? new UnavailableTextGenerationService()).Generate(request);
    }
}
