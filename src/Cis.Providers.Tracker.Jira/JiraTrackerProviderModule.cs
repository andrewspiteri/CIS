using Cis.Abstractions;
using Cis.Modules.Tracker;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Providers.Tracker.Jira;

public sealed class JiraTrackerProviderModule : ICisModule
{
    public string Name => "tracker-jira";
    public string Description => "Jira Cloud transport for the provider-neutral CIS tracker host.";
    public void RegisterServices(IServiceCollection services)
        => services.AddSingleton<ICisTrackerProvider, JiraTrackerProvider>();
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
}
