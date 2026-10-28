using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private sealed record SourceVariantManifest(int SchemaVersion, SourceVariantEvidenceOptions Evidence);

    /// <summary>Read one bounded manifest under the selected repository and verify its saved diagnostic evidence.</summary>
    public SourceVariantEvidenceResult VerifySourceVariantManifest(string repositoryPath, string manifestPath)
    {
        try
        {
            string root = Path.GetFullPath(repositoryPath), path = Safe(root, manifestPath);
            Require(new FileInfo(path).Length is > 0 and <= 262144, "Source-variant manifest must contain 1 byte through 256 KiB.");
            string manifestHash = HashFile(path);
            var manifest = ReadJson<SourceVariantManifest>(path, 262144);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            SourceVariantFields(json.RootElement, "schemaVersion", "evidence");
            var evidence = json.RootElement.GetProperty("evidence");
            SourceVariantFields(evidence, "repositoryPath", "reportPath", "reportSha256", "sourceFile", "subjectAssemblyPath", "mutantId", "nativeTestId", "original", "variant", "restored");
            foreach (string field in new[] { "repositoryPath", "reportPath", "reportSha256", "sourceFile", "subjectAssemblyPath", "mutantId", "nativeTestId" })
            { Require(evidence.GetProperty(field).ValueKind == JsonValueKind.String && evidence.GetProperty(field).GetString() is { Length: > 0 and <= 2048 }, "Manifest evidence requires explicit bounded strings."); }
            foreach (string phase in new[] { "original", "variant", "restored" })
            {
                SourceVariantFields(evidence.GetProperty(phase), "repositoryPath", "profilePath");
                Require(evidence.GetProperty(phase).EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 and <= 2048 }), "Each phase requires explicit bounded repository and profile strings.");
            }
            Require(manifest.SchemaVersion == 1 && manifest.Evidence is not null, "Source-variant manifest requires schemaVersion 1 and evidence options.");
            var options = manifest.Evidence;
            Require(PathComparer.Equals(SourceVariantRoot(root, options.RepositoryPath), root), "Manifest repositoryPath must match --repo.");
            var normalized = options with
            {
                RepositoryPath = root,
                Original = Normalize(options.Original), Variant = Normalize(options.Variant), Restored = Normalize(options.Restored),
            };
            var result = VerifySourceVariant(normalized);
            Require(HashFile(path) == manifestHash, "Source-variant manifest changed during verification.");
            var bounded = result with { Inputs = result.Inputs.Append(new MutationInputIdentity(path, manifestHash)).Distinct().ToArray() };
            Require(JsonSerializer.SerializeToUtf8Bytes(bounded, Json).Length <= 1048576, "Source-variant receipt exceeds 1 MiB.");
            return bounded;

            SourceVariantEvidencePhase Normalize(SourceVariantEvidencePhase phase)
            {
                Require(phase is not null && !string.IsNullOrWhiteSpace(phase.ProfilePath), "Each source-variant phase requires a repository and profile path.");
                string phaseRoot = SourceVariantRoot(root, phase.RepositoryPath);
                _ = Safe(phaseRoot, phase.ProfilePath);
                return phase with { RepositoryPath = phaseRoot };
            }
        }
        catch (Exception error) when (Expected(error) || error is KeyNotFoundException or FormatException)
        {
            return new("invalid-source-variant-manifest", 2)
            { Diagnostics = [error is InvalidDataException ? error.Message : "Source-variant manifest could not be read: " + error.GetType().Name + "."] };
        }
    }

    private static string SourceVariantRoot(string root, string path)
    {
        Require(path is { Length: > 0 and <= 2048 }, "Each repository path must be explicit and bounded.");
        return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Safe(root, path, true);
    }

    private static void SourceVariantFields(JsonElement value, params string[] names)
    {
        Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == names.Length
            && value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names),
            "Source-variant manifest has missing, unknown or ambiguously cased fields.");
    }
}
