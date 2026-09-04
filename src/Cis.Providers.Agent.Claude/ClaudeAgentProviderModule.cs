using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Providers.Agent.Claude;

public sealed class ClaudeAgentProviderModule : ICisModule
{
    public string Name => "agent-claude";
    public string Description => "Claude structured headless transport for provider-neutral CIS agent runs.";
    public void RegisterServices(IServiceCollection services) => services.AddSingleton<ICisAgentProvider, ClaudeAgentProvider>();
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
}
