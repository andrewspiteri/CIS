using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;

namespace Cis.Modules.Definition;

public sealed class ProductDefinitionAuthority(ICisRepositoryContextResolver repositories)
    : ICisProductDefinitionAuthority
{
    private const string SessionRelativePath = ".cis/local/definition-wizard/session.json";
    private static readonly string[] DefinitionArtifacts =
    [
        "specs/business-requirements.md",
        "specs/technical-intent-questionnaire.md",
        "specs/technical-intent-spec.md",
        "architecture/overall-solution-design.md",
        "architecture/high-level-architecture-diagrams.md",
        "references/component-sheet.md",
        "references/dictionary-index.md",
        "specs/ui-direction-questionnaire.md",
        "design/ui-direction.md",
        "design/ui-system-preview.md",
        "design/ui-system-preview.svg",
        "plans/high-level-backlog.md",
        "references/api-dictionary.md",
        "references/command-dictionary.md",
        "references/event-dictionary.md",
        "references/workflow-state-dictionary.md",
        "references/projection-dictionary.md",
        "references/permissions-dictionary.md",
        "references/configuration-dictionary.md",
        "references/data-dictionary.md",
        "references/problem-details-catalogue.md",
        "references/screen-route-map.md",
        "references/package-catalogue.md",
        "references/module-ownership-map.md",
        "references/business-invariant-catalogue.md",
        "references/traceability-matrix.md",
        "references/erd.md",
    ];
    private static readonly HashSet<string> InventoryOnlyArtifacts = new(
        DefinitionArtifacts.Where(path => path == "references/dictionary-index.md"
            || path.StartsWith("references/", StringComparison.Ordinal)
            && path is not "references/component-sheet.md"), StringComparer.Ordinal);

    public CisProductDefinitionAuthority Evaluate(string repositoryPath)
    {
        var contextResult = repositories.Resolve(repositoryPath);
        if (!contextResult.IsSuccess || contextResult.Context is null)
            return new(false, false, null, null, null, contextResult.Errors);
        var context = contextResult.Context;
        var sessionPath = Path.Combine(context.RepositoryPath,
            SessionRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sessionPath)) return new(false, false, null, null, null, []);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(sessionPath));
            var root = document.RootElement;
            var sessionId = Text(root, "sessionId");
            var activatedAt = Text(root, "activatedAtUtc");
            var recordedHash = Text(root, "baselineHash");
            var recordedSemanticHash = Text(root, "semanticBaselineHash");
            var sessionActive = root.TryGetProperty("active", out var activeValue) && activeValue.ValueKind == JsonValueKind.True;
            var currentHash = ComputeBaselineHash(context.DocumentationPath, out var missing);
            var errors = new List<string>();
            if (sessionActive || string.IsNullOrWhiteSpace(activatedAt))
                errors.Add("The high-level product-definition wizard has not been consolidated and activated.");
            if (missing.Count > 0)
                errors.Add("The activated product-definition baseline is incomplete: " + string.Join(", ", missing));
            if (!sessionActive && !string.IsNullOrWhiteSpace(activatedAt) && string.IsNullOrWhiteSpace(recordedHash))
                errors.Add("The product-definition activation predates baseline binding; reopen and activate the definition once.");
            else if (!sessionActive && !string.IsNullOrWhiteSpace(activatedAt)
                     && string.IsNullOrWhiteSpace(recordedSemanticHash))
                errors.Add("The product-definition activation predates semantic baseline binding; run `cis definition status` to migrate a still-current approved definition.");
            else if (!string.IsNullOrWhiteSpace(recordedSemanticHash)
                     && !string.Equals(recordedSemanticHash, currentHash, StringComparison.Ordinal))
                errors.Add("The high-level product-definition artifacts changed after consolidated activation.");
            return new(true, errors.Count == 0, sessionId, activatedAt, recordedHash ?? currentHash, errors);
        }
        catch (JsonException exception)
        {
            return new(true, false, null, null, null,
                [$"The product-definition activation record is unreadable: {exception.Message}"]);
        }
    }

    internal static string ComputeBaselineHash(string documentationPath, out IReadOnlyList<string> missing)
    {
        var absent = new List<string>();
        var builder = new StringBuilder("cis-product-definition-baseline-v1\n");
        foreach (var relative in DefinitionArtifacts.Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(documentationPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                absent.Add(relative);
                continue;
            }
            builder.Append(relative).Append(':');
            if (InventoryOnlyArtifacts.Contains(relative)) builder.Append("present");
            else builder.Append(Hash(Encoding.UTF8.GetBytes(NormalizedArtifact(relative, File.ReadAllText(path)))));
            builder.Append('\n');
        }
        missing = absent;
        return "sha256:" + Hash(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static string Hash(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string NormalizedArtifact(string relativePath, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (relativePath == "specs/business-requirements.md")
            return "semantic-v1:" + BrdDocumentDigest.Compute(normalized);
        if (relativePath == "specs/technical-intent-spec.md")
        {
            normalized = CisTechnicalIntentPresentation.RestoreManagedEvidence(normalized);
            foreach (var key in new[] { "status", "last_reviewed" })
                normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
                normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            normalized = Regex.Replace(normalized,
                Regex.Escape("<!-- cis:technical-intent-baseline:start -->") + ".*?" +
                Regex.Escape("<!-- cis:technical-intent-baseline:end -->"),
                "<managed-technical-intent-baseline>", RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            return normalized;
        }
        if (relativePath != "plans/high-level-backlog.md") return normalized;
        normalized = Regex.Replace(normalized, "(?m)^  approved_content_hash:.*$",
            "  approved_content_hash: <managed>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var lines = normalized.Split('\n');
        var insideItems = false;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index] == "<!-- cis:brd-backlog-items:start -->") { insideItems = true; continue; }
            if (lines[index] == "<!-- cis:brd-backlog-items:end -->") { insideItems = false; continue; }
            if (!insideItems || !lines[index].TrimStart().StartsWith('|')) continue;
            var cells = lines[index].Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length != 9 || !cells[0].StartsWith("HLT-", StringComparison.Ordinal)) continue;
            cells[7] = "<managed-feature-link>";
            lines[index] = "| " + string.Join(" | ", cells) + " |";
        }
        return string.Join('\n', lines);
    }
}
