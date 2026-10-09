using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private static void ValidateCommandPermissions(ICisAgentProvider? provider, IReadOnlyList<string> commands,
        string mode, string permission, List<string> diagnostics)
    {
        var error = CisAgentCommandPermissions.Validate(commands, mode, permission);
        if (error is not null) diagnostics.Add("ERROR: " + error);
        if (commands.Count > 0 && provider?.Descriptor.SupportsExplicitCommands != true)
            diagnostics.Add("ERROR: The selected provider does not support explicit command permissions.");
    }

    private void RecordCommandPermissions(CisRepositoryContext context, AgentRunManifest manifest)
    {
        foreach (var command in manifest.AllowedCommands)
            AppendPermission(context, manifest, new("command-preauthorization", "Explicit command authorized for this attempt.",
                RequestedCapability: "explicit-command", RequestedTarget: command, RequestApproved: true), true);
    }

    private static string CommandPermissionPrompt(IReadOnlyList<string> commands) => commands.Count == 0 ? string.Empty
        : "The controller supplied native permission rules for the commands below for this attempt. "
            + "Task instruction: invoke each separately in the assigned working directory without shell wrappers, redirects, changed arguments, or a directory change. These instructions are not a native sandbox or full-command confinement guarantee. "
            + "Report a denied or failed command; do not replace it with a claim that it passed. "
            + "This grants no authority to expand the task or change lifecycle state.\n"
            + string.Join("\n", commands.Select(command => "- " + command)) + "\n\n";
}
