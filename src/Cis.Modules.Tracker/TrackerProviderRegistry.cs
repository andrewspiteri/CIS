namespace Cis.Modules.Tracker;

public sealed class TrackerProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ICisTrackerProvider> _providers;

    public TrackerProviderRegistry(IEnumerable<ICisTrackerProvider> providers)
    {
        var all = providers.OrderBy(provider => provider.Kind, StringComparer.Ordinal).ToArray();
        var duplicate = all.GroupBy(provider => provider.Kind, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Tracker provider kind is registered more than once: {duplicate.Key}");
        _providers = all.ToDictionary(provider => provider.Kind, StringComparer.OrdinalIgnoreCase);
    }

    public ICisTrackerProvider? Find(string kind) => _providers.GetValueOrDefault(kind);
}
