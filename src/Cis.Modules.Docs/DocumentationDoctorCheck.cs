using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Docs;

public sealed partial class DocumentationDoctorCheck : ICisRepositoryDoctorCheck
{
    private readonly DocumentationInventoryService _inventoryService;
    private readonly DocumentationValidationService _validationService;

    public DocumentationDoctorCheck(
        DocumentationValidationService validationService,
        DocumentationInventoryService inventoryService)
    {
        _validationService = validationService;
        _inventoryService = inventoryService;
    }

    public string Name => "documentation";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var findings = new List<CisRepositoryDoctorFinding>();
        var validation = _validationService.Validate(context.RepositoryPath, strict: false);
        findings.AddRange(validation.Errors.Select(error => new CisRepositoryDoctorFinding(
            "CIS-DOC-001",
            "error",
            "documentation",
            error,
            [],
            "Correct the catalog or Markdown document, then run strict documentation validation.",
            "cis docs validate --strict",
            "review-required")));
        findings.AddRange(validation.Warnings.Select(warning => new CisRepositoryDoctorFinding(
            "CIS-DOC-002",
            "warning",
            "documentation",
            warning,
            [],
            "Reconcile the document metadata and catalog registration.",
            "cis docs validate --strict",
            "review-required")));

        var inventory = _inventoryService.Inventory(context.RepositoryPath);
        if (inventory.Errors.Count > 0)
        {
            findings.AddRange(inventory.Errors.Select(error => new CisRepositoryDoctorFinding(
                "CIS-DOC-003",
                "error",
                "documentation-inventory",
                error,
                [],
                "Correct the inventory error and rerun repository doctor.",
                "cis docs inventory",
                "review-required")));
            return findings;
        }

        var drafts = inventory.Documents
            .Where(document => string.Equals(document.Status, "draft", StringComparison.OrdinalIgnoreCase))
            .Select(document => document.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (drafts.Length > 0)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-DOC-004",
                "warning",
                "documentation-readiness",
                $"The documentation workspace contains {drafts.Length} Draft document(s).",
                LimitEvidence(drafts),
                "Review Draft documents and promote only those whose content and evidence are complete.",
                "cis docs inventory",
                "review-required"));
        }

        var todoDocuments = inventory.Documents
            .Where(document => !string.Equals(document.Type, "template", StringComparison.OrdinalIgnoreCase))
            .Where(document => ContainsTodo(context.RepositoryPath, document.Path))
            .Select(document => document.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (todoDocuments.Length > 0)
        {
            findings.Add(new CisRepositoryDoctorFinding(
                "CIS-DOC-005",
                "warning",
                "documentation-readiness",
                $"The documentation workspace contains TODO markers in {todoDocuments.Length} document(s).",
                LimitEvidence(todoDocuments),
                "Resolve each required TODO or explicitly document why it remains open.",
                null,
                "review-required"));
        }

        return findings;
    }

    private static bool ContainsTodo(string repositoryPath, string repositoryRelativePath)
    {
        try
        {
            var content = File.ReadAllText(Path.Combine(
                repositoryPath,
                repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            return TodoPattern().IsMatch(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> LimitEvidence(IReadOnlyList<string> values)
    {
        const int limit = 20;
        if (values.Count <= limit)
        {
            return values;
        }

        return values.Take(limit)
            .Append($"... and {values.Count - limit} more")
            .ToArray();
    }

    [GeneratedRegex(
        "^\\s*(?:(?:[-*]\\s+)?TODO(?:\\s*:.*)?|\\|[^\\r\\n]*\\bTODO\\b[^\\r\\n]*\\|)\\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TodoPattern();
}
