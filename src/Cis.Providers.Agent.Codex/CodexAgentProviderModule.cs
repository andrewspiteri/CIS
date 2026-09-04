using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Providers.Agent.Codex;

public sealed class CodexAgentProviderModule : ICisModule
{
    public string Name => "agent-codex";
    public string Description => "Codex App Server and structured JSONL transport for provider-neutral CIS agent runs.";
    public void RegisterServices(IServiceCollection services) => services.AddSingleton<ICisAgentProvider, CodexAgentProvider>();
    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
}
