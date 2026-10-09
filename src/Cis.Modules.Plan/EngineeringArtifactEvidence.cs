using System.Text.Json;
using System.Security.Cryptography;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

internal static class EngineeringArtifactEvidence
{
    internal static bool ArtifactsIntact(string repository, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array || artifacts.GetArrayLength() == 0) return false;
        var hasResult = false;
        foreach (var artifact in artifacts.EnumerateArray())
        {
            var relative = Text(artifact, "path");
            if (!CisPathSafety.TryResolveUnderRoot(repository, relative, out var path)
                || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path) || new FileInfo(path).Length > 64 * 1024 * 1024) return false;
            using var stream = File.OpenRead(path);
            if (stream.Length != Number(artifact, "bytes")
                || !Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(Text(artifact, "digest").Replace("sha256:", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)) return false;
            hasResult |= Text(artifact, "kind") is "test-result" or "mutation-result" or "scanner-result";
        }
        return hasResult;
    }

    internal static bool HasBoundResult(JsonElement manifest, JsonElement suite, string kind, string? sourcePath = null)
    {
        if (!manifest.TryGetProperty("artifacts", out var all) || all.ValueKind != JsonValueKind.Array
            || !suite.TryGetProperty("artifacts", out var own) || own.ValueKind != JsonValueKind.Array) return false;
        // Every suite artifact must be one of the exact records verified at manifest level.
        if (!own.EnumerateArray().All(artifact => all.EnumerateArray().Any(candidate =>
            Text(candidate, "path") == Text(artifact, "path") && Text(candidate, "digest") == Text(artifact, "digest")
            && Text(candidate, "kind") == Text(artifact, "kind") && Number(candidate, "bytes") == Number(artifact, "bytes")
            && Text(candidate, "suiteId") == Text(artifact, "suiteId")))) return false;
        return own.EnumerateArray().Any(artifact => Text(artifact, "kind") == kind
            && (sourcePath is null || !string.IsNullOrWhiteSpace(sourcePath) && Text(artifact, "path") == sourcePath));
    }

}
