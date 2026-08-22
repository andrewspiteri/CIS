using Cis.Abstractions;
using System.CommandLine;

namespace Cis.Host;

internal sealed class CisCommandRegistry(RootCommand rootCommand) : ICisCommandRegistry
{
    public void Add(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (rootCommand.Subcommands.Any(existing =>
            string.Equals(existing.Name, command.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A command named '{command.Name}' is already registered.");
        }

        rootCommand.Subcommands.Add(command);
    }
}
