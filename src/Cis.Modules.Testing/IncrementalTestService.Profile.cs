using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    public const string LocalRoot = ".cis/local/testing/incremental";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly ICisGraphSnapshotReader _graph;
    public IncrementalTestService(ICisGraphSnapshotReader graph) => _graph = graph;

    private sealed record Input(string Repository, IncrementalTestProfile Profile, string ProfilePath,
        string ProfileHash, string Storage, string Root, string StatePath, string CommonIdentity);

    private static Input Read(IncrementalTestOptions options, string? previousTestingProducer = null, string? previousHelperProducer = null,
        bool legacySessionEnvironment = false, string? environmentIdentity = null)
    {
        Require(options.Layer is "unit" or "mutation" or "all", "Layer must be unit, mutation, or all.");
        Require(options.MaximumBatches is >= 1 and <= 512 && options.MaximumSeconds is >= 1 and <= 3600,
            "Maximum batches must be 1-512 and maximum seconds 1-3600.");
        var repository = Path.GetFullPath(options.RepositoryPath);
        var path = Safe(repository, options.ProfilePath);
        var profile = ReadJson<IncrementalTestProfile>(path, 262144);
        Require(profile.SchemaVersion == 1 && Slug(profile.Id), "Profile requires schemaVersion 1 and a safe lowercase id.");
        Require(profile.SourceFiles is { Length: > 0 and <= 512 } && profile.TestFiles is { Length: > 0 and <= 2048 }
            && profile.ConfigFiles is { Length: <= 512 } && profile.ToolFiles is { Length: > 0 and <= 128 }, "Input inventories are missing or exceed their limits.");
        var inputs = profile.SourceFiles.Concat(profile.TestFiles).Concat(profile.ConfigFiles).ToArray();
        foreach (var file in inputs) Require(File.Exists(Safe(repository, file)), "Missing declared input: " + file);
        Require(inputs.Select(file => Safe(repository, file)).Distinct(PathComparer).Count() == inputs.Length,
            "Declared source, test and configuration paths must be unique.");
        Require(Directory.Exists(Safe(repository, profile.WorkingDirectory, true)), "Working directory is missing.");
        Require(profile.UnitResultFormat is "trx" or "junit", "Unit result format must be trx or junit.");
        Require(profile.FullTestFilter is { Length: > 0 and <= 32768 } && !profile.FullTestFilter.Contains('\0'), "A bounded full test filter is required.");
        Command(profile.UnitCommand, true);
        Safe(repository, profile.UnitReportPath);
        Require(profile.UnitCommand.Any(value => value.Contains("{testFilter}", StringComparison.Ordinal)), "Unit command must use {testFilter}.");
        if (profile.MutationCommand is not null)
        {
            Require(profile.CanarySourceFile is not null && profile.MinimumCanaryKills >= 1
                && profile.SourceFiles.Any(file => PathComparer.Equals(Safe(repository, file), Safe(repository, profile.CanarySourceFile))),
                "Mutation requires a declared canarySourceFile and minimumCanaryKills of at least 1.");
            Command(profile.MutationCommand, true);
            Require(profile.StrykerConfigFile is not null || profile.MutationCommand.Any(value => value.Contains("{sourceFile}", StringComparison.Ordinal)), "Mutation command must use {sourceFile} or strykerConfigFile.");
            Require(profile.StrykerConfigFile is not null || profile.MutationCommand.Any(value => value.Contains("{testFilter}", StringComparison.Ordinal)), "Mutation command must use {testFilter} or strykerConfigFile.");
            Require(profile.RecoveryCommand is not null, "Mutation requires a recoveryCommand that restores the original build.");
            Safe(repository, profile.MutationReportPath);
            if (profile.StrykerConfigFile is not null)
            {
                var template = Safe(repository, profile.StrykerConfigFile);
                Require(profile.ConfigFiles.Any(file => PathComparer.Equals(Safe(repository, file), template)), "Stryker template must be declared in configFiles.");
                Require(profile.MutationCommand.Any(value => value.Contains("{mutationConfig}", StringComparison.Ordinal)), "Stryker command must use {mutationConfig}.");
                Require(ReadJson<System.Text.Json.Nodes.JsonObject>(template, 262144)["stryker-config"] is System.Text.Json.Nodes.JsonObject,
                    "Stryker template requires a stryker-config object.");
            }
        }
        Require(options.Layer != "mutation" || profile.MutationCommand is not null, "Mutation command is not configured.");
        if (profile.RecoveryCommand is not null) Command(profile.RecoveryCommand, false);
        Require(profile.TimeoutSeconds is >= 1 and <= 3600 && profile.RecoveryTimeoutSeconds is >= 1 and <= 600
            && profile.MaxRunBytes > 0 && profile.MaxRetainedBytes > 0 && profile.MinFreeBytes >= 0, "Execution limits are invalid.");
        if (profile.RerunAdditionalTimeoutMilliseconds is int additionalTimeout)
        {
            Require(profile.MutationCommand is not null && profile.StrykerConfigFile is not null,
                "Rerun additional timeout requires a mutation command and declared Stryker template.");
            var template = ReadJson<JsonElement>(Safe(repository, profile.StrykerConfigFile), 262144).GetProperty("stryker-config");
            int baseline = 0;
            Require(template.TryGetProperty("additional-timeout", out var configured) && configured.ValueKind == JsonValueKind.Number
                && configured.TryGetInt32(out baseline) && baseline > 0,
                "Rerun additional timeout requires an explicit positive integer additional-timeout in the template.");
            Require(additionalTimeout is >= 1 and <= 60000 && additionalTimeout >= baseline
                && additionalTimeout < profile.TimeoutSeconds * 1000,
                "Rerun additional timeout must be 1-60000 milliseconds, at least the template value, and below the outer invocation guard.");
            Require(!profile.UnitCommand.Any(value => value.Contains("{mutationConfig}", StringComparison.Ordinal))
                && !(profile.RecoveryCommand?.Any(value => value.Contains("{mutationConfig}", StringComparison.Ordinal)) ?? false),
                "A rerun additional timeout applies only to the mutation command's rendered configuration.");
        }
        Require(double.IsFinite(profile.BreakThreshold) && profile.BreakThreshold is >= 0 and <= 100, "Mutation threshold must be 0-100.");
        Require(profile.Environment is not null && profile.Environment.Count <= 64 && profile.Environment.All(pair =>
            pair.Key is { Length: > 0 and <= 128 } && !pair.Key.Contains('=') && !pair.Key.Contains('\0')
            && pair.Value is { Length: <= 32768 } && !pair.Value.Contains('\0')), "Profile environment is invalid.");
        Require(profile.Environment.Keys.Distinct(PathComparer).Count() == profile.Environment.Count, "Environment names collide on this host.");
        var tools = profile.ToolFiles.Select(file => Path.GetFullPath(file, repository)).Distinct(PathComparer).Order(StringComparer.Ordinal).ToArray();
        foreach (var file in tools)
            Require(File.Exists(file) && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(file)!, file), "Missing or linked tool input: " + file);
        foreach (var command in new[] { profile.UnitCommand, profile.MutationCommand, profile.RecoveryCommand }.Where(value => value is not null))
            Require(Path.IsPathFullyQualified(command![0]) && tools.Contains(Path.GetFullPath(command[0]), PathComparer),
                "Each native command must use an absolute executable included in toolFiles.");
        var common = new
        {
            profile = HashFile(path),
            configuration = profile.ConfigFiles.Order(StringComparer.Ordinal).Select(file => new { file, hash = HashFile(Safe(repository, file)) }).ToArray(),
            tools = tools.Select(file => new { file, hash = HashFile(file) }).ToArray(),
            cis = new[] { previousTestingProducer ?? HashFile(typeof(IncrementalTestService).Assembly.Location), previousHelperProducer ?? HashFile(typeof(CisProcessJob).Assembly.Location) },
            platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            ambientEnvironment = environmentIdentity ?? AmbientEnvironmentIdentity(profile, legacySessionEnvironment),
        };
        var storage = Safe(repository, ".cis/local/testing");
        var root = Safe(repository, LocalRoot + "/" + profile.Id);
        return new(repository, profile, path, common.profile, storage, root, Safe(root, "state.json"), HashObject(common));
    }

    private static void Command(string[]? values, bool output)
    {
        Require(values is { Length: > 0 and <= 256 } && values.All(value => value is { Length: <= 32768 } && !value.Contains('\0'))
            && !string.IsNullOrWhiteSpace(values[0]), "Native command must be a bounded executable/argument array.");
        Require(!output || values!.Any(value => value.Contains("{output}", StringComparison.Ordinal)), "Native command must use {output}.");
    }

    private static IncrementalTestState State(Input input)
    {
        if (!File.Exists(input.StatePath)) return new();
        var state = ReadJson<IncrementalTestState>(input.StatePath, 16 * 1024 * 1024);
        Require(state.SchemaVersion == 1 && state.Batches is { Count: <= 512 }, "Saved incremental state is invalid.");
        return state;
    }

    private static T ReadJson<T>(string path, long maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "JSON input exceeds its size limit.");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var errors = new List<string>();
        StrykerJsonResultAdapter.CheckDuplicateProperties(document.RootElement, "$", errors);
        Require(errors.Count == 0, "JSON input contains duplicate properties.");
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("JSON input is empty.");
    }

    private static string Safe(string root, string relative, bool allowRoot = false)
    {
        Require(relative is { Length: > 0 and <= 2048 } && CisPathSafety.TryResolveUnderRoot(root, relative, out _, allowRoot), "Path must remain inside its owning root.");
        CisPathSafety.TryResolveUnderRoot(root, relative, out var path, allowRoot);
        var linkedRoot = false;
        try { linkedRoot = File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        Require(!linkedRoot && !CisPathSafety.ContainsReparsePoint(root, path), "Linked input/output path is forbidden: " + relative);
        return path;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static bool Slug(string value) => value is not null && Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant);
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string HashObject<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    private static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
        or ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or System.Xml.XmlException;
}
