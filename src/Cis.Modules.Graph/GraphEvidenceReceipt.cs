using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Graph;

/// <summary>Projects saved evidence data. It never runs verification or trusts a receipt as current authority.</summary>
internal sealed record GraphEvidenceReceipt(IncrementalEvidenceResult? Receipt, string State,
    string? SemanticHash, IReadOnlyList<GraphEvidencePin> Pins)
{
    internal const long MaximumReportBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static GraphEvidenceReceipt Read(string repository, string relativePath)
    {
        if (!TrySafePath(repository, relativePath, out var absolute)) return new(null, "unsafe-reference", null, []);
        if (!File.Exists(absolute)) return new(null, "missing", null, []);
        try
        {
            if (new FileInfo(absolute).Length > 1024 * 1024) return new(null, "receipt-too-large", null, []);
            using var document = JsonDocument.Parse(File.ReadAllBytes(absolute));
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var version) || version != 1
                || !root.TryGetProperty("policy", out var policy) || policy.GetString() != "cis-incremental-evidence-v1"
                || !root.TryGetProperty("assessmentContractVersion", out var assessment) || !assessment.TryGetInt32(out var assessmentVersion)
                || assessmentVersion != CisIncrementalResultContract.Version)
                return new(null, "unsupported-receipt", null, []);
            var receipt = root.Deserialize<IncrementalEvidenceResult>(Json);
            if (receipt is null || receipt.Inputs is null || receipt.Batches is null || receipt.Closures is null
                || receipt.Refinements is null || receipt.Credits is null || receipt.Batches.Any(batch => batch is null || batch.Executions is null)
                || receipt.Closures.Any(closure => closure is null || closure.TestInputIndices is null || closure.DependencyInputIndices is null)
                || receipt.Inputs.Any(input => input is null || input.Path is null || input.Sha256 is null))
                return new(null, "invalid-receipt", null, []);
            var pins = receipt.Inputs.Select(input => InspectPin(repository, input.Path, input.Sha256, null, "input"))
                .Concat(receipt.Batches.Select(batch => InspectPin(repository, batch.UnitReportPath, batch.UnitReportSha256, batch.UnitReportBytes, "unit-report")))
                .Concat(receipt.Batches.Where(batch => batch.MutationReportPath is not null)
                    .Select(batch => InspectPin(repository, batch.MutationReportPath!, batch.MutationReportSha256 ?? "", batch.MutationReportBytes, "mutation-report")))
                .ToArray();
            var semantic = JsonSerializer.Serialize(new
            {
                receipt.AssessmentContractVersion,
                receipt.Status,
                receipt.ExitCode,
                receipt.Layer,
                receipt.ProfileId,
                receipt.ProfilePath,
                receipt.ProfileSha256,
                receipt.RuntimeIdentifier,
                receipt.OsDescription,
                receipt.EnvironmentSha256,
                receipt.TestingProducerSha256,
                receipt.HelperProducerSha256,
                receipt.ScopeComplete,
                receipt.ThresholdMet,
                receipt.Raw,
                receipt.Effective,
                receipt.Inputs,
                receipt.Closures,
                receipt.Batches,
                receipt.Refinements,
                receipt.Credits,
                Pins = pins,
            }, Json);
            return new(receipt, "recorded", Digest(semantic), pins);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new(null, "invalid-receipt", null, []);
        }
    }

    internal static GraphEvidencePin InspectPin(string repository, string path, string digest, long? bytes, string role)
    {
        if (!string.IsNullOrWhiteSpace(path) && (Path.IsPathRooted(path)
            || Regex.IsMatch(path, "^[A-Za-z]:[/\\\\]", RegexOptions.CultureInvariant)))
            return new(path, digest, bytes, null, "unassessed-external", role);
        if (string.IsNullOrWhiteSpace(path) || !TrySafePath(repository, path, out var absolute))
            return new(path ?? "", digest, bytes, null, "unassessed-unsafe", role);
        if (!File.Exists(absolute)) return new(path, digest, bytes, null, "missing", role);
        try
        {
            var length = new FileInfo(absolute).Length;
            if (length > MaximumReportBytes) return new(path, digest, bytes, null, "unassessed-too-large", role);
            using var stream = File.OpenRead(absolute);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var matches = (bytes is null || bytes == length) && actual.Equals(digest.Replace("sha256:", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            return new(path, digest, bytes, actual, matches ? "matches" : "changed", role);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(path, digest, bytes, null, "unreadable", role);
        }
    }

    internal static bool TrySafePath(string repository, string path, out string absolute)
        => CisPathSafety.TryResolveUnderRoot(repository, path, out absolute)
            && !CisPathSafety.ContainsReparsePoint(repository, absolute);

    internal static string Digest(string text)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

internal sealed record GraphEvidencePin(string Path, string RecordedSha256, long? RecordedBytes,
    string? ActualSha256, string State, string Role);
