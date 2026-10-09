namespace Cis.Abstractions;

/// <summary>Validates the deliberately small syntax accepted for explicit native command rules.</summary>
public static class CisAgentCommandPermissions
{
    public static string? Validate(IReadOnlyList<string> commands, string mode, string permission)
    {
        if (commands.Count == 0) return null;
        if (mode != CisAgentRunModes.Implement || permission != CisAgentPermissions.WorkspaceWrite)
            return "Explicit commands require an implementation task with workspace-write permission.";
        if (commands.Count > 32) return "At most 32 explicit commands can be authorized per attempt.";
        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command) || command.Length > 2048 || command != command.Trim()
                || !command.Contains(' ') || command.Contains("  ", StringComparison.Ordinal)
                || !char.IsAsciiLetterOrDigit(command[0])
                || command.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not (' ' or '-' or '_' or '.' or '/' or ':' or '=')))
                return "Explicit commands must be a program and arguments using letters, digits, single spaces, -, _, ., /, :, or =. Wildcards, quotes, shell operators and expansions are not supported.";
        }
        if (commands.Distinct(StringComparer.OrdinalIgnoreCase).Count() != commands.Count)
            return "Explicit commands must not contain duplicate entries.";
        return null;
    }
}
