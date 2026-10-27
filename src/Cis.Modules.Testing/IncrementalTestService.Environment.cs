using System.Collections;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    // Session coordinates and shell bookkeeping are not native test inputs by default.
    // Canonicalize them for execution as well as identity, so ignored values cannot
    // silently affect a test. A profile may explicitly declare any of them as input.
    private static readonly string[] SessionEnvironmentNames =
    [
        "SSH_CLIENT", "SSH_CONNECTION", "SSH_TTY", "XDG_SESSION_ID", "TERM_SESSION_ID",
        "WT_SESSION", "SHLVL", "_", "PWD", "OLDPWD",
    ];

    private static string AmbientEnvironmentIdentity(IncrementalTestProfile profile, bool legacy)
    {
        var omitted = legacy ? new HashSet<string>(PathComparer) : SessionEnvironmentNames
            .Concat(profile.Environment.Keys).ToHashSet(PathComparer);
        // Only a combined digest is retained; inherited values may include credentials.
        return HashObject(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Where(pair => !omitted.Contains((string)pair.Key))
            .OrderBy(pair => (string)pair.Key, StringComparer.Ordinal)
            .Select(pair => new { name = (string)pair.Key, value = (string?)pair.Value }).ToArray());
    }

    private static Dictionary<string, string> NativeEnvironment(IncrementalTestProfile profile, string working)
    {
        var result = SessionEnvironmentNames.ToDictionary(name => name,
            name => name == "PWD" ? working : name == "SHLVL" ? "0" : "", PathComparer);
        foreach (var pair in profile.Environment) result[pair.Key] = pair.Value;
        return result;
    }
}
