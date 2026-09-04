using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cis.Abstractions;

public sealed record CisBrdReviewFindingDisposition(
    string Id,
    string Severity,
    string Category,
    string Location,
    string Observation,
    string Recommendation,
    string Decision,
    string? DecidedBy = null,
    string? DecidedAtUtc = null,
    string? Rationale = null,
    string? ApprovedRecommendation = null);

public sealed record CisBrdReviewDispositionDocument(
    int SchemaVersion,
    string ReviewRunId,
    string ReviewResultSha256,
    string BrdSha256,
    string ReviewProvider,
    string CreatedAtUtc,
    IReadOnlyList<CisBrdReviewFindingDisposition> Findings,
    string? ApprovedBy = null,
    string? ApprovedAtUtc = null,
    string? ApprovalReason = null,
    string? ApprovedDecisionSha256 = null,
    string? AppliedByRunId = null,
    string? AppliedAtUtc = null,
    string? RevisedBrdSha256 = null);

public sealed record CisBrdReviewFreshness(
    string Status,
    string? WorkspacePath,
    string? ReviewRunId,
    string? CanonicalPath,
    string? ReviewedSha256,
    string? CurrentSha256,
    bool Compatible,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count > 0 ? 4 : 0;
}

public interface ICisBrdReviewFreshness
{
    CisBrdReviewFreshness Freshness(string workspacePath, string runId);
}

public static class CisBrdReviewDispositionCodec
{
    public const string StartMarker = "<!-- cis:brd-review-disposition:start -->";
    public const string EndMarker = "<!-- cis:brd-review-disposition:end -->";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string Render(CisBrdReviewDispositionDocument document)
    {
        var approved = IsApproved(document);
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine("type: brd-review-disposition");
        builder.AppendLine($"status: {(approved ? "Approved" : "Review Required")}");
        builder.AppendLine($"review_run_id: {JsonSerializer.Serialize(document.ReviewRunId)}");
        builder.AppendLine($"review_provider: {JsonSerializer.Serialize(document.ReviewProvider)}");
        builder.AppendLine($"brd_sha256: {document.BrdSha256}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# BRD review recommendations");
        builder.AppendLine();
        builder.AppendLine("> Human dispositions in this document define the exact authorized remediation scope. Agent reviews remain advisory and agents cannot accept, reject, or approve findings.");
        builder.AppendLine();
        builder.AppendLine("## Dispositions");
        builder.AppendLine();
        foreach (var finding in document.Findings)
        {
            builder.AppendLine($"### {Line(finding.Id)} — {Line(finding.Severity)} — {Line(finding.Decision)}");
            builder.AppendLine();
            builder.AppendLine($"- Category: {Line(finding.Category)}");
            builder.AppendLine($"- Location: {Line(finding.Location)}");
            builder.AppendLine($"- Observation: {Line(finding.Observation)}");
            builder.AppendLine($"- Reviewer recommendation: {Line(finding.Recommendation)}");
            if (finding.Decision == "accepted")
                builder.AppendLine($"- Approved recommendation: {Line(EffectiveRecommendation(finding))}");
            builder.AppendLine($"- Decided by: {Line(finding.DecidedBy ?? "Not decided")}");
            builder.AppendLine($"- Decided at: {Line(finding.DecidedAtUtc ?? "Not decided")}");
            if (finding.Decision == "rejected")
                builder.AppendLine($"- Rejection rationale: {Line(finding.Rationale ?? "Not supplied")}");
            builder.AppendLine();
        }
        builder.AppendLine("## Approval");
        builder.AppendLine();
        builder.AppendLine($"- Status: {(approved ? "Approved" : "Not approved")}");
        builder.AppendLine($"- Approved by: {Line(document.ApprovedBy ?? "Not approved")}");
        if (!string.IsNullOrWhiteSpace(document.ApprovalReason))
            builder.AppendLine($"- Legacy approval rationale: {Line(document.ApprovalReason)}");
        builder.AppendLine();
        builder.AppendLine("## Application evidence");
        builder.AppendLine();
        builder.AppendLine($"- Agent run: {Line(document.AppliedByRunId ?? "Not applied")}");
        builder.AppendLine($"- Revised BRD SHA-256: {Line(document.RevisedBrdSha256 ?? "Not applied")}");
        builder.AppendLine();
        builder.AppendLine(StartMarker);
        builder.AppendLine(JsonSerializer.Serialize(document, Options));
        builder.AppendLine(EndMarker);
        return builder.ToString();
    }

    public static bool TryParse(string markdown, out CisBrdReviewDispositionDocument? document, out string? error)
    {
        document = null;
        error = null;
        var start = markdown.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = markdown.IndexOf(EndMarker, start < 0 ? 0 : start + StartMarker.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            error = "The canonical review-disposition managed block is missing.";
            return false;
        }
        var json = markdown[(start + StartMarker.Length)..end].Trim();
        try
        {
            document = JsonSerializer.Deserialize<CisBrdReviewDispositionDocument>(json, Options);
            if (document is null) throw new JsonException("The managed block is empty.");
            return true;
        }
        catch (JsonException exception)
        {
            error = "The canonical review-disposition managed block is invalid: " + exception.Message;
            return false;
        }
    }

    public static string ComputeDecisionDigest(CisBrdReviewDispositionDocument document)
    {
        if (document.SchemaVersion <= 1)
        {
            var legacyDecisions = document.Findings.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new
            {
                item.Id,
                item.Decision,
                item.DecidedBy,
                item.DecidedAtUtc,
                item.Rationale,
            });
            var legacyCanonical = JsonSerializer.Serialize(new
            {
                document.ReviewRunId,
                document.ReviewResultSha256,
                document.BrdSha256,
                Findings = legacyDecisions,
            }, Options);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(legacyCanonical)));
        }
        var decisions = document.Findings.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new
        {
            item.Id,
            item.Decision,
            item.DecidedBy,
            item.DecidedAtUtc,
            ApprovedRecommendation = item.Decision == "accepted" ? EffectiveRecommendation(item) : null,
            RejectionRationale = item.Decision == "rejected" ? item.Rationale : null,
        });
        var canonical = JsonSerializer.Serialize(new
        {
            document.ReviewRunId,
            document.ReviewResultSha256,
            document.BrdSha256,
            Findings = decisions,
        }, Options);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool IsApproved(CisBrdReviewDispositionDocument document)
        => document.Findings.Count > 0
           && document.Findings.All(item => item.Decision is "accepted" or "rejected")
           && document.Findings.All(item => item.Decision != "accepted" || !string.IsNullOrWhiteSpace(EffectiveRecommendation(item)))
           && document.Findings.All(item => item.Decision != "rejected" || !string.IsNullOrWhiteSpace(item.Rationale))
           && !string.IsNullOrWhiteSpace(document.ApprovedBy)
           && !string.IsNullOrWhiteSpace(document.ApprovedAtUtc)
           && (document.SchemaVersion >= 2 || !string.IsNullOrWhiteSpace(document.ApprovalReason))
           && string.Equals(document.ApprovedDecisionSha256, ComputeDecisionDigest(document), StringComparison.OrdinalIgnoreCase);

    public static string EffectiveRecommendation(CisBrdReviewFindingDisposition finding)
        => string.IsNullOrWhiteSpace(finding.ApprovedRecommendation)
            ? finding.Recommendation.Trim() : finding.ApprovedRecommendation.Trim();

    private static string Line(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ').Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
}
