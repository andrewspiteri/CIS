using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Docs;

public sealed partial class DocumentationValidationService
{
    private static readonly HashSet<string> AllowedAuthorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "canonical",
        "derived",
        "proposal",
        "routing",
    };

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly DocumentationCatalogReader _catalogReader;
    private readonly DocumentationInventoryService _inventoryService;
    private readonly MarkdownDocumentInspector _documentInspector;
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;

    public DocumentationValidationService(
        ICisRepositoryContextResolver repositoryContextResolver,
        DocumentationCatalogReader catalogReader,
        DocumentationInventoryService inventoryService,
        MarkdownDocumentInspector documentInspector)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _catalogReader = catalogReader;
        _inventoryService = inventoryService;
        _documentInspector = documentInspector;
    }

    public DocumentationValidationResult Validate(string repositoryPath, bool strict)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return InvalidRepository(resolution.Errors, strict);
        }

        var context = resolution.Context!;
        var catalogResult = _catalogReader.Read(context.CatalogPath);
        if (!catalogResult.IsSuccess)
        {
            return Result(context, 0, 0, [], catalogResult.Errors, strict);
        }

        var catalog = catalogResult.Catalog!;
        var inventory = _inventoryService.Inventory(context.RepositoryPath);
        var errors = new List<string>(inventory.Errors);
        var warnings = new List<string>(inventory.Warnings);

        if (catalog.SchemaVersion != 1)
        {
            errors.Add($"Unsupported documentation catalog schema version '{catalog.SchemaVersion}'.");
        }

        AddDuplicateErrors(catalog.Documents, entry => entry.Id, "id", errors);
        AddDuplicateErrors(catalog.Documents, entry => entry.Path, "path", errors);

        var documentationPrefix = context.DocumentationPath + Path.DirectorySeparatorChar;
        foreach (var entry in catalog.Documents)
        {
            ValidateRequiredFields(entry, errors);
            if (!string.IsNullOrWhiteSpace(entry.Id) && !StableIdPattern().IsMatch(entry.Id))
            {
                errors.Add($"Catalog id '{entry.Id}' contains unsupported characters.");
            }

            if (!string.IsNullOrWhiteSpace(entry.Authority)
                && !AllowedAuthorities.Contains(entry.Authority))
            {
                errors.Add(
                    $"Catalog entry '{DisplayId(entry)}' has unsupported authority '{entry.Authority}'.");
            }

            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            if (Path.IsPathRooted(entry.Path))
            {
                errors.Add($"Catalog entry '{DisplayId(entry)}' path must be repository-relative.");
                continue;
            }

            string absolutePath;
            try
            {
                absolutePath = Path.GetFullPath(Path.Combine(context.RepositoryPath, entry.Path));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"Catalog entry '{DisplayId(entry)}' has an invalid path: {exception.Message}");
                continue;
            }

            if (!absolutePath.StartsWith(documentationPrefix, PathComparison))
            {
                errors.Add(
                    $"Catalog entry '{DisplayId(entry)}' path must resolve inside {context.DocumentationRoot}.");
                continue;
            }

            if (!File.Exists(absolutePath))
            {
                errors.Add($"Catalog entry '{DisplayId(entry)}' points to a missing file: {entry.Path}");
                continue;
            }

            if (!string.Equals(Path.GetExtension(absolutePath), ".md", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Catalog entry '{DisplayId(entry)}' must point to a Markdown file: {entry.Path}");
                continue;
            }

            var relativeToDocumentation = Path.GetRelativePath(context.DocumentationPath, absolutePath)
                .Replace('\\', '/');
            var metadata = _documentInspector.Inspect(absolutePath, relativeToDocumentation);
            if (metadata.MalformedFrontMatter)
            {
                errors.Add($"Cataloged document has malformed YAML front matter: {entry.Path}");
            }
            else if (!metadata.HasFrontMatter)
            {
                warnings.Add($"Cataloged document has no YAML front matter: {entry.Path}");
            }
        }

        foreach (var document in inventory.Documents.Where(document => !document.Cataloged))
        {
            warnings.Add($"Markdown document is not cataloged: {document.Path}");
        }

        return Result(
            context,
            catalog.Documents.Count,
            inventory.Documents.Count,
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
            strict);
    }

    private static void ValidateRequiredFields(
        DocumentationCatalogEntry entry,
        ICollection<string> errors)
    {
        AddRequired(entry, entry.Id, "id", errors);
        AddRequired(entry, entry.Path, "path", errors);
        AddRequired(entry, entry.Type, "type", errors);
        AddRequired(entry, entry.Status, "status", errors);
        AddRequired(entry, entry.Authority, "authority", errors);
    }

    private static void AddRequired(
        DocumentationCatalogEntry entry,
        string value,
        string field,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Catalog entry '{DisplayId(entry)}' must define {field}.");
        }
    }

    private static void AddDuplicateErrors(
        IEnumerable<DocumentationCatalogEntry> entries,
        Func<DocumentationCatalogEntry, string> selector,
        string field,
        ICollection<string> errors)
    {
        foreach (var value in entries
                     .Select(selector)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            errors.Add($"Documentation catalog contains duplicate {field} '{value}'.");
        }
    }

    private static string DisplayId(DocumentationCatalogEntry entry)
        => string.IsNullOrWhiteSpace(entry.Id) ? "<missing-id>" : entry.Id;

    private static DocumentationValidationResult InvalidRepository(
        IReadOnlyList<string> errors,
        bool strict)
        => new(
            "invalid-repository",
            null,
            null,
            0,
            0,
            [],
            errors,
            strict,
            RepositoryConfigurationValid: false);

    private static DocumentationValidationResult Result(
        CisRepositoryContext context,
        int catalogEntries,
        int markdownDocuments,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        bool strict)
    {
        var isValid = errors.Count == 0 && (!strict || warnings.Count == 0);
        return new DocumentationValidationResult(
            isValid ? "valid" : "invalid",
            context.RepositoryPath,
            context.DocumentationRoot,
            catalogEntries,
            markdownDocuments,
            warnings,
            errors,
            strict,
            RepositoryConfigurationValid: true);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9:._-]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StableIdPattern();
}
