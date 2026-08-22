using Cis.Abstractions;
using Cis.Modules.Tracker;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Providers.Tracker.GitHub;

public sealed class GitHubTrackerProviderModule : ICisModule
{
    public string Name => "tracker-github";
    public string Description => "GitHub Issues transport for the provider-neutral CIS tracker host.";

    public void RegisterServices(IServiceCollection services)
        => services.AddSingleton<ICisTrackerProvider, GitHubTrackerProvider>();

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        // The provider extends `cis tracker`; it deliberately owns no parallel command surface.
    }
}
