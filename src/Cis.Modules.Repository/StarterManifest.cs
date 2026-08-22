using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Modules.Repository;

internal sealed record StarterManifest(
    string Shape,
    IReadOnlyList<RepositoryComponentClassification> Components,
    IReadOnlyList<ManagedStarterArtifact> ManagedArtifacts);

internal sealed record ManagedStarterArtifact(
    string Id,
    string Path,
    string Definition,
    int TemplateVersion,
    string AppliedHash,
    string Ownership = "managed");

internal sealed record StarterManifestReadResult(
    StarterManifest? Manifest,
    IReadOnlyList<string> Errors);

internal sealed class StarterManifestStore
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .DisableAliases()
        .Build();

    public StarterManifestReadResult Read(string path)
    {
        if (!File.Exists(path))
        {
            return new StarterManifestReadResult(
                new StarterManifest("unclassified", [], []),
                []);
        }

        try
        {
            var source = _deserializer.Deserialize<ManifestFile>(File.ReadAllText(path));
            if (source is null)
            {
                return Failure("Starter manifest is empty.");
            }

            if (source.SchemaVersion != 1)
            {
                return Failure($"Unsupported starter manifest schema version '{source.SchemaVersion}'.");
            }

            var components = (source.Components ?? []).Select(component =>
                new RepositoryComponentClassification(
                    component.Id ?? string.Empty,
                    component.Root ?? string.Empty,
                    component.Languages ?? [],
                    component.Frameworks ?? [],
                    component.Roles ?? [],
                    component.Capabilities ?? [],
                    component.Confidence ?? "unknown",
                    component.Evidence ?? [])).ToArray();
            var artifacts = (source.ManagedArtifacts ?? []).Select(artifact =>
                new ManagedStarterArtifact(
                    artifact.Id ?? string.Empty,
                    artifact.Path ?? string.Empty,
                    artifact.Definition ?? string.Empty,
                    artifact.TemplateVersion,
                    artifact.AppliedHash ?? string.Empty,
                    artifact.Ownership ?? "managed")).ToArray();
            return new StarterManifestReadResult(
                new StarterManifest(source.RepositoryShape ?? "unclassified", components, artifacts),
                []);
        }
        catch (Exception exception) when (exception is YamlException or IOException or UnauthorizedAccessException)
        {
            return Failure($"Starter manifest is invalid: {exception.Message}");
        }
    }

    public string Write(StarterManifest manifest)
    {
        var source = new ManifestFile
        {
            SchemaVersion = 1,
            RepositoryShape = manifest.Shape,
            Components = manifest.Components.Select(component => new ManifestComponent
            {
                Id = component.Id,
                Root = component.Root,
                Languages = component.Languages.ToList(),
                Frameworks = component.Frameworks.ToList(),
                Roles = component.Roles.ToList(),
                Capabilities = component.Capabilities.ToList(),
                Confidence = component.Confidence,
                Evidence = component.Evidence.ToList(),
            }).ToList(),
            ManagedArtifacts = manifest.ManagedArtifacts.Select(artifact => new ManifestArtifact
            {
                Id = artifact.Id,
                Path = artifact.Path,
                Definition = artifact.Definition,
                TemplateVersion = artifact.TemplateVersion,
                AppliedHash = artifact.AppliedHash,
                Ownership = artifact.Ownership,
            }).ToList(),
        };

        return _serializer.Serialize(source).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static StarterManifestReadResult Failure(string error)
        => new(null, [error]);

    private sealed class ManifestFile
    {
        public int SchemaVersion { get; set; }

        public string? RepositoryShape { get; set; }

        public List<ManifestComponent>? Components { get; set; }

        public List<ManifestArtifact>? ManagedArtifacts { get; set; }
    }

    private sealed class ManifestComponent
    {
        public string? Id { get; set; }

        public string? Root { get; set; }

        public List<string>? Languages { get; set; }

        public List<string>? Frameworks { get; set; }

        public List<string>? Roles { get; set; }

        public List<string>? Capabilities { get; set; }

        public string? Confidence { get; set; }

        public List<string>? Evidence { get; set; }
    }

    private sealed class ManifestArtifact
    {
        public string? Id { get; set; }

        public string? Path { get; set; }

        public string? Definition { get; set; }

        public int TemplateVersion { get; set; }

        public string? AppliedHash { get; set; }

        public string? Ownership { get; set; }
    }
}
