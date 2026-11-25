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
        "CODEX_TOOL_CALL_ID", "CODEX_APP_TOOLS_PIPE_PATH",
    ];
    internal static readonly string[] NativeSessionEnvironmentPrefixes = ["CODEX_", "BROWSER_USE_ACCESS_"];

    // These namespaces belong to the invoking agent's transport and session,
    // rather than to the application. Canonicalize the whole transport namespace
    // so a new connection does not create a new native test input. A profile can
    // explicitly opt any individual value back into its execution contract.
    private static bool RunnerSessionEnvironmentName(string name)
        => SessionEnvironmentNames.Contains(name, PathComparer)
            || NativeSessionEnvironmentPrefixes.Any(prefix => name.StartsWith(prefix, CisPathSafety.PlatformComparison));

    private static DictionaryEntry[] AmbientEnvironmentInputs(IncrementalTestProfile profile, bool legacy,
        bool ignoreOverriddenTemporary, DictionaryEntry[]? inherited = null)
    {
        var omitted = (legacy ? [] : SessionEnvironmentNames.Concat(profile.Environment.Keys))
            .Concat(CisNativeWorkEnvironment.Keys).ToHashSet(PathComparer);
        if (ignoreOverriddenTemporary) omitted.UnionWith(["TEMP", "TMP", "TMPDIR"]);
        return (inherited ?? Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToArray())
            .Where(pair => !omitted.Contains((string)pair.Key)
                && (legacy || !RunnerSessionEnvironmentName((string)pair.Key)))
            .OrderBy(pair => (string)pair.Key, StringComparer.Ordinal).ToArray();
    }

    private static string AmbientEnvironmentIdentity(IncrementalTestProfile profile, bool legacy, bool ignoreOverriddenTemporary = false)
        => CapturedEnvironmentIdentity(profile, legacy, ignoreOverriddenTemporary,
            Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToArray());

    private static string CapturedEnvironmentIdentity(IncrementalTestProfile profile, bool legacy,
        bool ignoreOverriddenTemporary, DictionaryEntry[] inherited)
    {
        // Work coordinates authorize this invocation; they do not change the native
        // source/test/configuration identity. Keep them available for validated launch.
        // Only digests are retained; inherited values may include credentials.
        return HashObject(AmbientEnvironmentInputs(profile, legacy, ignoreOverriddenTemporary, inherited)
            .Select(pair => new { name = (string)pair.Key, value = (string?)pair.Value }).ToArray());
    }

    // The compact comparison basis retains names and digests, never inherited or
    // declared values. Use the same selection as the actual material identity.
    private static Dictionary<string, string> MaterialEnvironmentDigests(IncrementalTestProfile profile, DictionaryEntry[]? inherited = null)
    {
        var result = AmbientEnvironmentInputs(profile, false, ignoreOverriddenTemporary: true, inherited)
            .ToDictionary(pair => (string)pair.Key, pair => HashObject((string?)pair.Value), PathComparer);
        foreach (var pair in profile.Environment) result[pair.Key] = HashObject(pair.Value);
        return result;
    }

    internal static string PilotEnvironmentIdentity(Dictionary<string, string> declared)
        => HashObject(new
        {
            ambient = AmbientEnvironmentIdentity(new IncrementalTestProfile { Environment = declared }, false, ignoreOverriddenTemporary: true),
            declared = declared.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        });

    internal static Dictionary<string, string> PilotNativeEnvironment(Dictionary<string, string> declared, string working)
        => NativeEnvironment(new IncrementalTestProfile { Environment = declared }, working);

    private static Dictionary<string, string> NativeEnvironment(IncrementalTestProfile profile, string working, DictionaryEntry[]? inherited = null)
    {
        // The trusted worker removes transport namespaces before applying these
        // overrides. Absence stays canonical even when a session adds a new key.
        var names = SessionEnvironmentNames.Where(name => !NativeSessionEnvironmentPrefixes.Any(prefix =>
            name.StartsWith(prefix, CisPathSafety.PlatformComparison)));
        var result = names.ToDictionary(name => name,
            name => name == "PWD" ? working : name == "SHLVL" ? "0" : "", PathComparer);
        // PowerShell prepends its own module directories at each driver boundary.
        // Pin the target to the inherited material value used by identity instead
        // of letting the guard's shell startup silently change the test input.
        result["PSModulePath"] = inherited is null ? Environment.GetEnvironmentVariable("PSModulePath") ?? ""
            : (string?)inherited.FirstOrDefault(pair => PathComparer.Equals((string)pair.Key, "PSModulePath")).Value ?? "";
        foreach (var pair in profile.Environment) result[pair.Key] = pair.Value;
        return result;
    }
}
