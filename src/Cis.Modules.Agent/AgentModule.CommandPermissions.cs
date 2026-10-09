using System.CommandLine;

namespace Cis.Modules.Agent;

public sealed partial class AgentModule
{
    private static Option<string[]> CommandPermissionsOption() => new("--allow-command")
    {
        Arity = ArgumentArity.OneOrMore,
        AllowMultipleArgumentsPerToken = false,
        Description = "Add one literal native command permission rule for this attempt; repeat for more commands. Requires a supporting provider and an implementation task with workspace-write permission."
    };
}
