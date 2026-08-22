using Microsoft.Extensions.DependencyInjection;

namespace Cis.Abstractions;

public interface ICisModule
{
    string Name { get; }

    string Description { get; }

    void RegisterServices(IServiceCollection services);

    void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services);
}
