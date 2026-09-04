using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Providers.Ci.GitHub;

public sealed class GitHubCiProviderModule : ICisModule
{
    public string Name => "ci-github";
    public string Description => "GitHub Actions transport for the provider-neutral CIS CI host.";
    public void RegisterServices(IServiceCollection services) => services.AddSingleton<ICisCiProvider, GitHubCiProvider>();
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
}
