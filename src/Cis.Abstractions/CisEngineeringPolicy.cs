using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Abstractions;

/// <summary>One bounded adoption reader shared by execution and completion, including deleted managed policy.</summary>
public static class CisEngineeringPolicy
{
    public const string RelativePath = ".cis/engineering-defaults.json";
    public const string AdoptionPath = ".cis/engineering-adoption.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EngineeringDefaultsPolicy? Read(string repository)
    {
        var path = Path.Combine(repository, RelativePath);
        if (!File.Exists(path))
        {
            if (WasManaged(repository))
                throw new InvalidDataException("The adopted engineering-defaults policy is missing; deletion does not remove completion obligations.");
            return null;
        }
        EnsureFile(repository, path, 64 * 1024);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != 1
            || !root.TryGetProperty("requireTaskCompletion", out var completion)
            || completion.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Engineering policy requires schemaVersion=1 and explicit requireTaskCompletion.");
        // An explicit opt-out needs no gate inventory; it is not an adopted passing result.
        if (!completion.GetBoolean())
        {
            if (WasManaged(repository))
                throw new InvalidDataException("The managed engineering policy cannot be disabled by an opt-out edit; completion obligations remain active.");
            return new(1, false, []);
        }
        var policy = root.Deserialize<EngineeringDefaultsPolicy>(JsonOptions);
        if (policy?.AdditionalGates is null || policy.AdditionalGates.Any(id => string.IsNullOrWhiteSpace(id)
            || id.Length > 80 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
            throw new InvalidDataException("Adopted engineering policy requires valid additionalGates identifiers.");
        ValidateThresholds(policy);
        return policy;
    }

    public static void ValidateThresholds(EngineeringDefaultsPolicy policy)
    {
        if (!double.IsFinite(policy.MinimumCoverageLines) || policy.MinimumCoverageLines is < 95 or > 100
            || !double.IsFinite(policy.MinimumMutationScore) || policy.MinimumMutationScore is < 80 or > 100)
            throw new InvalidDataException("Engineering thresholds require coverage 95–100 and mutation 80–100; stronger adopted thresholds cannot be replaced by the defaults.");
    }

    private static bool WasManaged(string repository)
    {
        var adoption = Path.Combine(repository, AdoptionPath);
        if (File.Exists(adoption))
        {
            EnsureFile(repository, adoption, 64 * 1024);
            using var record = JsonDocument.Parse(File.ReadAllText(adoption));
            if (record.RootElement.ValueKind != JsonValueKind.Object
                || !record.RootElement.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != 1
                || !record.RootElement.TryGetProperty("adopted", out var adopted) || adopted.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("Engineering adoption history is invalid; disabling the adoption record does not withdraw obligations.");
            return true;
        }
        var path = Path.Combine(repository, ".cis/starter-manifest.yml");
        if (!File.Exists(path)) return false;
        EnsureFile(repository, path, 2 * 1024 * 1024);
        try
        {
            var manifest = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties().Build().Deserialize<AdoptionManifest>(File.ReadAllText(path));
            if (manifest is not { SchemaVersion: 1, ManagedArtifacts: not null })
                throw new InvalidDataException("Starter adoption history cannot be verified.");
            return manifest.ManagedArtifacts.Any(item => item?.Path == RelativePath);
        }
        catch (YamlException exception) { throw new InvalidDataException("Starter adoption history is malformed.", exception); }
    }

    private static void EnsureFile(string repository, string path, long maximum)
    {
        if (CisPathSafety.ContainsReparsePoint(repository, path) || new FileInfo(path).Length > maximum)
            throw new InvalidDataException("Engineering adoption evidence must be a bounded regular repository file.");
    }
    private sealed class AdoptionManifest
    {
        public int SchemaVersion { get; set; }
        public List<AdoptionArtifact?>? ManagedArtifacts { get; set; }
    }
    private sealed class AdoptionArtifact { public string? Path { get; set; } }
}
