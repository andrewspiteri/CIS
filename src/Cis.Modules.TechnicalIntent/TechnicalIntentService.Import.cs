using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentService
{
    private static readonly IReadOnlyDictionary<string, string[]> ImportedSectionAliases = new Dictionary<string, string[]>
    {
        ["Approved business baseline"] = ["Business baseline", "Business requirements baseline", "Purpose and decision status"],
        ["Design goals and principles"] = ["Design principles", "Purpose and objectives", "Purpose and decision status"],
        ["Technical surface and ownership"] = ["Repository and ownership layout", "Implementation stack baseline", "Technology and ownership"],
        ["Runtime architecture and trust boundaries"] = ["Runtime architecture", "System architecture"],
        ["Application model and invariants"] = ["Business invariants", "Application invariants", "Invariants carried into the implementation"],
        ["API, integration, and compatibility intent"] = ["Service contract", "APIs and contracts", "Integration contracts"],
        ["Security and privacy intent"] = ["Trust and security", "Trusted-local API and configuration"],
        ["Operations, migration, and recovery intent"] = ["Shutdown, upgrades and recovery", "Observability and diagnosis"],
        ["Quality attributes and verification direction"] = ["Technical acceptance and BRD traceability", "Acceptance and verification"],
        ["Decisions and delivery constraints"] = ["Technical baseline closure and deployment inputs", "Technical decisions and constraints"],
        ["Open technical decisions"] = ["Technical baseline closure and deployment inputs", "Decision closure"],
    };

    private static string ExtractImportedSection(string content, string required)
    {
        if (!ImportedSectionAliases.TryGetValue(required, out var aliases)) return string.Empty;
        var body = aliases.Select(alias => ExtractSection(content, alias)).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
        // A differently titled closure section replaces the open-decision section only
        // when it explicitly states closure. Its table statuses are checked separately.
        if (required == "Open technical decisions" && !Regex.IsMatch(body,
            @"\b(?:technical\s+)?(?:choices|decisions)\s+(?:are\s+)?(?:now\s+)?closed\b|\bno\s+(?:open|unresolved)\s+technical\s+decisions\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return string.Empty;
        return body;
    }

    private static string RouteSelectedTechnicalCatalog(string repository, string path, string catalog, string stableId, string relative)
    {
        var selected = CisProductDocumentPaths.Read(repository).GetValueOrDefault("technical");
        if (selected is null || !string.Equals(CisProductDocumentPaths.ValidatePath(repository, selected), path, CisPathSafety.PlatformComparison)) return catalog;
        // Rebind only the explicitly selected role. The catalog merger still checks
        // whether the selected path belongs to another identity before anything is saved.
        return Regex.Replace(catalog,
            $@"(?m)(^[ \t]*- id: {Regex.Escape(stableId)}[ \t]*\r?\n(?:(?![ \t]*- id:)[^\n]*\n)*?[ \t]*path:)[^\r\n]*",
            match => match.Groups[1].Value + " " + JsonSerializer.Serialize(relative),
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool TryImportSelectedTechnicalIntent(string repository, string path, string original,
        string stableId, out string imported)
    {
        imported = original;
        var selected = CisProductDocumentPaths.Read(repository).GetValueOrDefault("technical");
        if (selected is null || !string.Equals(CisProductDocumentPaths.ValidatePath(repository, selected), path, CisPathSafety.PlatformComparison)) return false;
        // Explicit document selection permits adopting unmanaged text, never replacing
        // another CIS identity or repairing ambiguous managed evidence.
        if (Regex.IsMatch(original, @"(?m)^cis\s*:|^\s+stable_id\s*:|<!--\s*cis:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return false;
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var frontMatter = Regex.Match(original, @"\A\uFEFF?---\r?\n(?<header>[\s\S]*?)\r?\n---(?=\r?\n|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (original.TrimStart('\uFEFF').StartsWith("---", StringComparison.Ordinal) && !frontMatter.Success) return false;
        var header = frontMatter.Success ? frontMatter.Groups["header"].Value : "title: " + JsonSerializer.Serialize("Imported technical intent");
        foreach (var key in new[] { "status", "scope", "last_reviewed" })
            header = Regex.Replace(header, $@"(?m)^{key}:[^\r\n]*(?:\r?\n)?", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        header = header.TrimEnd('\r', '\n') + newline + "status: Draft" + newline + "scope: Workspace" + newline + "last_reviewed: null" + newline
            + "cis:" + newline + "  stable_id: " + stableId + newline + "  technical_intent_schema: 1" + newline
            + "  approved_by: null" + newline + "  approved_at: null" + newline + "  approval_reason: null" + newline + "  approved_content_hash: null";
        var body = frontMatter.Success ? original[frontMatter.Length..] : newline + newline + original;
        imported = "---" + newline + header + newline + "---" + body;
        return true;
    }

    private static void BackupImportedTechnicalIntent(string repository, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory = Path.Combine(repository, ".cis", "local", "document-import");
        var backup = Path.Combine(directory, hash + ".md");
        if (CisPathSafety.ContainsReparsePoint(repository, directory) || CisPathSafety.ContainsReparsePoint(repository, backup))
            throw new IOException("The document backup path must not be a link.");
        Directory.CreateDirectory(directory);
        if (!File.Exists(backup)) File.WriteAllBytes(backup, bytes);
    }
}
