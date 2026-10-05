using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class BrdService
{
    private static bool TryImportSelectedBrd(string original, string title, string stableId,
        IReadOnlyList<BrdRepositoryBaseline> baselines, IReadOnlyList<BrdCandidate> candidates, out string imported)
    {
        imported = original;
        // Do not overwrite an existing CIS identity or repair ambiguous partial evidence.
        if (Regex.IsMatch(original, @"(?m)^cis\s*:|^\s+stable_id\s*:|<!--\s*cis:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return false;
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var frontMatter = Regex.Match(original, @"\A\uFEFF?---\r?\n(?<header>[\s\S]*?)\r?\n---(?=\r?\n|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (original.TrimStart('\uFEFF').StartsWith("---", StringComparison.Ordinal) && !frontMatter.Success) return false;
        var header = frontMatter.Success ? frontMatter.Groups["header"].Value : $"title: {JsonSerializer.Serialize(title)}";
        header = Regex.Replace(header, @"(?m)^status:[^\r\n]*(?:\r?\n)?", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        header = header.TrimEnd('\r', '\n') + newline + "status: Review Required" + newline
            + "cis:" + newline + "  stable_id: " + stableId + newline + "  brd_schema: 1" + newline
            + "  approved_by: null" + newline + "  approved_at: null" + newline
            + "  approval_reason: null" + newline + "  approved_content_hash: null";
        var body = frontMatter.Success ? original[frontMatter.Length..] : newline + newline + original;
        imported = "---" + newline + header + newline + "---" + body + newline + newline
            + "## CIS participant baseline" + newline + newline + BaselineStart + newline
            + RenderBaselines(baselines) + BaselineEnd + newline + newline
            + "## Source assessment" + newline + newline + SourcesStart + newline
            + RenderSources(candidates, new Dictionary<string, SourceRow>(StringComparer.Ordinal)) + SourcesEnd + newline;
        return true;
    }

    private static void BackupImportedBrd(string repository, string file)
    {
        var bytes = File.ReadAllBytes(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory = Path.Combine(repository, ".cis", "local", "document-import");
        if (CisPathSafety.ContainsReparsePoint(repository, directory)) throw new IOException("The document backup directory must not be a link.");
        Directory.CreateDirectory(directory);
        var backup = Path.Combine(directory, hash + ".md");
        if (CisPathSafety.ContainsReparsePoint(repository, backup)) throw new IOException("The document backup must not be a link.");
        if (!File.Exists(backup)) File.WriteAllBytes(backup, bytes);
    }
}
