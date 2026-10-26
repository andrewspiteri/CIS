using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static (MutationRunProfile Profile, string Path, string Hash) ReadProfile(string repository, string relative, string kind)
    {
        Require(kind is "canary" or "diagnostic" or "qualification", "Kind must be canary, diagnostic, or qualification.");
        var path = Resolve(repository, relative);
        using var source = File.OpenRead(path);
        Require(source.Length is > 0 and <= 262144, "Mutation profile must contain between 1 byte and 256 KiB.");
        var bytes = new byte[(int)source.Length];
        source.ReadExactly(bytes);
        Require(source.ReadByte() == -1, "Mutation profile changed size while being read.");
        using var document = JsonDocument.Parse(bytes);
        var errors = new List<string>();
        StrykerJsonResultAdapter.CheckDuplicateProperties(document.RootElement, "$", errors);
        Require(errors.Count == 0, "Mutation profile contains duplicate JSON properties.");
        var profile = JsonSerializer.Deserialize<MutationRunProfile>(bytes, Json)
            ?? throw new InvalidDataException("Mutation profile must be an object.");
        Require(profile.SchemaVersion == 1, "Mutation profile requires schemaVersion 1.");
        Require(Regex.IsMatch(profile.Id, @"\A[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant), "Profile id must be a safe lowercase slug of at most 64 characters.");
        Require(double.IsFinite(profile.BreakThreshold) && profile.BreakThreshold is >= 0 and <= 100, "breakThreshold must be a finite number from 0 through 100.");
        Require(profile.TimeoutSeconds is >= 1 and <= 3600 && profile.MaxRunBytes > 0
            && profile.MaxRetainedBytes > 0 && profile.MinFreeBytes >= 0, "Mutation execution limits are invalid.");
        Require(profile.MinimumCanaryKills >= 1, "minimumCanaryKills must be at least 1.");
        Require(profile.Environment is not null && profile.Environment.Count <= 64
            && profile.Environment.All(pair => pair.Key is { Length: > 0 and <= 128 } && !pair.Key.Contains('=') && !pair.Key.Contains('\0')
                && pair.Value is { Length: <= 32768 } && !pair.Value.Contains('\0')),
            "Environment requires at most 64 bounded names/values without NUL or '=' in names.");
        Require(profile.Environment.Keys.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() == profile.Environment.Count,
            "Environment names must be unique under this host's comparison rules.");
        ValidateCommand(profile.Command);
        if (profile.CanaryCommand is not null) ValidateCommand(profile.CanaryCommand);
        Require(Directory.Exists(Resolve(repository, profile.WorkingDirectory, allowRoot: true)), "Mutation workingDirectory does not exist.");
        Require(CisPathSafety.TryResolveUnderRoot(repository, profile.ReportPath, out _), "reportPath must be a safe relative file path inside the fresh output directory.");
        Require(profile.SourceFiles is not null && profile.ConfigFiles is not null
            && profile.SourceFiles.Length <= 2047 && profile.ConfigFiles.Length <= 128
            && profile.SourceFiles.Length + profile.ConfigFiles.Length <= 2047, "Declared input lists exceed their limits or are null.");
        if (profile.HealthCheckCommand is not null) ValidateCommand(profile.HealthCheckCommand, requireOutput: false);
        var inputs = profile.SourceFiles.Concat(profile.ConfigFiles).ToArray();
        Require(inputs.Select(input => Resolve(repository, input)).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() == inputs.Length,
            "Declared source/config input paths must be unique.");
        foreach (var input in inputs) Require(File.Exists(Resolve(repository, input)), "Missing declared source/config input: " + input);
        if (kind == "qualification")
            Require(profile.SourceFiles.Length > 0 && profile.CanaryCommand is { Length: > 0 }, "Qualification requires declared sourceFiles and a separate canaryCommand.");
        return (profile, path, Hash(bytes));
    }

    private static void ValidateCommand(string[] command, bool requireOutput = true)
    {
        Require(command is { Length: > 0 and <= 256 } && command.All(value => value is not null && value.Length <= 32768 && !value.Contains('\0'))
            && !string.IsNullOrWhiteSpace(command[0]), "Native command requires an executable and at most 256 bounded arguments.");
        Require(!requireOutput || command.Any(value => value.Contains("{output}", StringComparison.Ordinal)), "Native command must direct its output using {output}.");
    }

    private static MutationSourceIdentity Identity(string repository, MutationRunProfile profile, string profilePath)
    {
        var paths = profile.SourceFiles.Concat(profile.ConfigFiles).Select(path => Resolve(repository, path)).Append(profilePath)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var inputs = paths.Select(path =>
        {
            using var stream = File.OpenRead(path);
            return new MutationInputIdentity(Path.GetRelativePath(repository, path).Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(stream)));
        }).ToArray();
        return new(Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inputs, Json))), inputs, true);
    }

    private static string Resolve(string root, string relative, bool allowRoot = false)
    {
        Require(relative is { Length: > 0 and <= 1024 }
            && CisPathSafety.TryResolveUnderRoot(root, relative, out _, allowRoot), "Path must be relative and remain inside its owning root.");
        CisPathSafety.TryResolveUnderRoot(root, relative, out var path, allowRoot);
        Require(!CisPathSafety.IsReparsePoint(root) && !CisPathSafety.ContainsReparsePoint(root, path), "Linked mutation input/output paths are forbidden.");
        return path;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
