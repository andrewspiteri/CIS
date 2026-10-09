using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using Cis.Abstractions;
using static Cis.Modules.Plan.EngineeringEvidenceJson;

namespace Cis.Modules.Plan;

/// <summary>Retains native scanner policy and rechecks time-limited acceptances at completion.</summary>
public static class EngineeringSecurityEvidence
{
    public static string? Rejection(string repository, JsonElement root, string documentationRoot)
    {
        if (Text(root, "status") is not ("passed" or "passed-with-findings")) return "security manifest did not pass";
        if (!ProfileCurrent(repository, root, documentationRoot)) return "scanner profile path or digest does not match the current documentation root";
        if (!root.TryGetProperty("suites", out var suites) || suites.ValueKind != JsonValueKind.Array || suites.GetArrayLength() == 0) return "scanner suites are missing";
        if (!suites.EnumerateArray().All(suite => Passes(root, suite) && EngineeringArtifactEvidence.HasBoundResult(root, suite, "scanner-result")))
            return "a scanner suite has blocking/unclassified findings, invalid acceptances, or an unbound scanner report";
        return EngineeringArtifactEvidence.ArtifactsIntact(repository, root) ? null : "scanner report bytes or hashes changed";
    }
    private static readonly HashSet<string> Severities = new(StringComparer.OrdinalIgnoreCase) { "critical", "high", "medium", "low", "info" };
    internal static bool ProfileCurrent(string repository, JsonElement manifest, string documentationRoot)
    {
        var expected = documentationRoot.TrimEnd('/', '\\').Replace('\\', '/') + "/references/security-suite-profile.md";
        if (Text(manifest, "profilePath") != expected) return false;
        if (!CisPathSafety.TryResolveUnderRoot(repository, Text(manifest, "profilePath"), out var path)
            || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return false;
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream)) == Text(manifest, "profileDigest");
    }

    internal static bool Passes(JsonElement manifest, JsonElement suite)
    {
        if (Text(suite, "status") is not ("passed" or "passed-with-findings")
            || string.IsNullOrWhiteSpace(Text(suite, "tool")) || string.IsNullOrWhiteSpace(Text(suite, "category"))
            || !suite.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array) return false;
        if (findings.GetArrayLength() == 0) return true;
        if (!suite.TryGetProperty("blockingSeverities", out var thresholds) || thresholds.ValueKind != JsonValueKind.Array || thresholds.GetArrayLength() == 0
            || thresholds.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || !Severities.Contains(item.GetString()!))) return false;
        var blocked = thresholds.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in findings.EnumerateArray())
        {
            if (Text(finding, "status") == "accepted")
            {
                if (!HasCurrentAcceptance(manifest, finding)) return false;
            }
            else if (!Severities.Contains(Text(finding, "severity")) || blocked.Contains(Text(finding, "severity"))) return false;
        }
        return true;
    }

    private static bool HasCurrentAcceptance(JsonElement manifest, JsonElement finding)
    {
        if (!manifest.TryGetProperty("acceptances", out var acceptances) || acceptances.ValueKind != JsonValueKind.Array) return false;
        var matches = acceptances.EnumerateArray().Where(item => Text(item, "id") == Text(finding, "acceptanceId")
            && Text(item, "scanner").Equals(Text(finding, "scanner"), StringComparison.OrdinalIgnoreCase)
            && Text(item, "fingerprint") == Text(finding, "fingerprint")).ToArray();
        if (matches.Length != 1) return false;
        var acceptance = matches[0];
        return new[] { "id", "scanner", "fingerprint", "reason", "owner", "approvedBy", "approvalReference" }
            .All(key => !string.IsNullOrWhiteSpace(Text(acceptance, key)))
            && DateOnly.TryParseExact(Text(acceptance, "acceptedUntil"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)
            && expiry >= DateOnly.FromDateTime(DateTime.UtcNow);
    }

}
