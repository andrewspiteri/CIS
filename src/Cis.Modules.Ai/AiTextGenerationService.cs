using Cis.Abstractions;

namespace Cis.Modules.Ai;

public sealed class AiTextGenerationService : ICisTextGenerationService
{
    private readonly IReadOnlyList<ICisAiProvider> _providers;

    public AiTextGenerationService(IEnumerable<ICisAiProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public CisAiStatus GetStatus() => new(_providers.Select(provider => provider.GetStatus()).ToArray());

    public CisTextGenerationResult Generate(CisTextGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new("invalid-request", request.Provider, request.Model, null,
                "A non-empty prompt is required.", true);
        }

        var statuses = _providers
            .Select(provider => (Provider: provider, Status: provider.GetStatus()))
            .ToArray();
        var selected = SelectProvider(statuses, request.Provider);
        if (selected is null)
        {
            var detail = string.IsNullOrWhiteSpace(request.Provider)
                ? "No local text-generation provider is available. Start Ollama or select a configured remote provider explicitly."
                : $"Text-generation provider '{request.Provider}' is unavailable or unknown.";
            return new("unavailable", request.Provider, request.Model, null, detail, true);
        }

        if (!selected.Value.Status.IsLocal && !request.AllowRemote)
        {
            return new("remote-approval-required", selected.Value.Status.Name, request.Model, null,
                "Remote model use requires explicit allow-remote authorization.", false);
        }

        var model = SelectModel(selected.Value.Status.Models, request.Model);
        if (model is null)
        {
            return new("model-unavailable", selected.Value.Status.Name, request.Model, null,
                "The requested provider has no selectable model.", selected.Value.Status.IsLocal);
        }

        return selected.Value.Provider.Generate(request, model);
    }

    private static (ICisAiProvider Provider, CisAiProviderStatus Status)? SelectProvider(
        IReadOnlyList<(ICisAiProvider Provider, CisAiProviderStatus Status)> providers,
        string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = providers.FirstOrDefault(candidate =>
                string.Equals(candidate.Status.Name, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            return match.Provider is not null && match.Status.IsAvailable ? match : null;
        }

        var local = providers.FirstOrDefault(candidate =>
            candidate.Status.IsLocal && candidate.Status.IsAvailable);
        return local.Provider is null ? null : local;
    }

    private static string? SelectModel(IReadOnlyList<CisAiModel> models, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return models.FirstOrDefault(model =>
                string.Equals(model.Name, requested.Trim(), StringComparison.OrdinalIgnoreCase))?.Name;
        }

        return models
            .OrderBy(model => model.SizeBytes ?? long.MaxValue)
            .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Name;
    }
}
