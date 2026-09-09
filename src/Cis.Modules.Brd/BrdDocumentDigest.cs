using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

/// <summary>
/// Computes the business-semantic digest of a canonical BRD. Managed provenance and
/// approval metadata are deliberately excluded so they do not manufacture a new
/// product decision.
/// </summary>
public static class BrdDocumentDigest
{
    private static readonly (string Start, string End)[] ManagedBlocks =
    [
        ("<!-- cis:baseline:start -->", "<!-- cis:baseline:end -->"),
        ("<!-- cis:sources:start -->", "<!-- cis:sources:end -->"),
        ("<!-- cis:feature-traceability:start -->", "<!-- cis:feature-traceability:end -->"),
    ];

    public static string Compute(string content)
    {
        var normalized = CisBrdPresentation.RestoreManagedEvidence(content)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var block in ManagedBlocks)
            normalized = RemoveManagedBlock(normalized, block.Start, block.End);
        return Hash(normalized.TrimEnd());
    }

    private static string RemoveManagedBlock(string content, string start, string end)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex) return content;
        var after = endIndex + end.Length;
        while (after < content.Length && content[after] == '\n') after++;
        return content[..startIndex] + content[after..];
    }

    private static string Hash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
