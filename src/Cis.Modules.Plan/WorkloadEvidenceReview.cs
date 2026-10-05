using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Plan;

/// <summary>Checks a declared representative workload receipt; never runs a workload or grants authority.</summary>
public static class WorkloadEvidenceReview
{
    public static bool SuggestsVolumeSensitiveWork(string text)
        => Regex.IsMatch(text, @"\b(dataset|throughput|capacity|large[- ]volume|high[- ]volume|bulk|streaming|load test|memory limit)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<string> Review(string repository, string profilePath, bool suggested)
    {
        if (!File.Exists(profilePath)) return suggested
            ? ["Representative workload evidence is missing. Before rollout, record expected bytes, records, shape, concurrency and CPU/memory/time/storage limits in workload-evidence.json, then run a bounded end-to-end fixture. If inapplicable, record applicable=false and a concrete rationale. Component passes and installation success do not satisfy this evidence."] : [];
        try
        {
            if (!CisPathSafety.TryResolveUnderRoot(repository, Path.GetRelativePath(repository, profilePath), out var checkedPath)
                || CisPathSafety.ContainsReparsePoint(repository, checkedPath) || new FileInfo(checkedPath).Length > 64 * 1024)
                return ["Workload evidence profile is unsafe or exceeds 64 KiB."];
            using var document = JsonDocument.Parse(File.ReadAllText(checkedPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1)
                return ["Workload evidence requires schemaVersion=1."];
            if (!root.TryGetProperty("applicable", out var applicable) || applicable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return ["Workload applicability must be explicitly true or false."];
            string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
            if (!applicable.GetBoolean()) return Meaningful(Text("rationale")) ? [] : ["An inapplicable workload assessment needs a concrete rationale."];
            var errors = new List<string>();
            foreach (var field in new[] { "shape", "softwareIdentity", "fixtureIdentity", "fixtureDifferences", "componentResult", "installationResult" })
                if (!Meaningful(Text(field))) errors.Add($"Workload evidence is missing {field}.");
            foreach (var field in new[] { "sourceBytes", "recordCount", "concurrency", "wallSeconds", "cpuSeconds", "memoryBytes", "storageBytes" })
                if (!root.TryGetProperty(field, out var value) || !value.TryGetDecimal(out var number) || number <= 0)
                    errors.Add($"Workload evidence requires a positive {field}.");
            if (Text("endToEndResult") != "passed") errors.Add("Representative end-to-end workload has not passed; component or installation results do not establish workload readiness.");
            var artifact = Text("artifactPath"); var digest = Text("artifactSha256");
            if (artifact.Length == 0 || !CisPathSafety.TryResolveUnderRoot(repository, artifact, out var path)
                || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path))
                errors.Add("Workload evidence artifact is missing or outside the repository.");
            else
            {
                using var stream = File.OpenRead(path);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), digest, StringComparison.OrdinalIgnoreCase))
                    errors.Add("Workload evidence artifact digest does not match.");
            }
            return errors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException)
        { return ["Workload evidence profile could not be validated; repair its JSON fields and artifact reference."]; }
    }

    private static bool Meaningful(string value) => value.Length >= 3 && value.ToLowerInvariant() is not ("todo" or "unknown" or "pending" or "tbd");
}
