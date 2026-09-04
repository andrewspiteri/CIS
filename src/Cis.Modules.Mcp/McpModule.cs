using System.CommandLine;
using Cis.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Mcp;

public sealed class McpModule : ICisModule
{
    public string Name => "mcp";
    public string Description => "Expose repository-scoped CIS services over local MCP stdio.";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICisMcpToolService, McpToolService>();
        services.AddSingleton<McpStdioServer>();
    }

    public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
    {
        var mcp = new Command(Name, Description);
        var serve = new Command("serve", "Run the local line-delimited JSON-RPC MCP stdio adapter.");
        var repo = new Option<string>("--repo")
        {
            Description = "Fixed initialized repository scope. Per-tool path overrides are not accepted.",
            Required = true,
        };
        var allowMutations = new Option<bool>("--allow-mutations")
        {
            Description = "Expose mutation tools. Every mutation still requires confirm=true.",
        };
        serve.Options.Add(repo); serve.Options.Add(allowMutations);
        serve.SetAction(parse =>
        {
            var path = parse.GetValue(repo)!;
            var resolution = services.GetRequiredService<ICisRepositoryContextResolver>().Resolve(path);
            if (!resolution.IsSuccess || resolution.Context is null)
            {
                foreach (var error in resolution.Errors) Console.Error.WriteLine(error);
                return 2;
            }
            return services.GetRequiredService<McpStdioServer>().Run(
                resolution.Context.RepositoryPath, parse.GetValue(allowMutations), Console.In, Console.Out);
        });
        mcp.Subcommands.Add(serve);
        commands.Add(mcp);
    }
}
