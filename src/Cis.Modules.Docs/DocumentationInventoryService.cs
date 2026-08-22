using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Docs;

public sealed partial class DocumentationInventoryService
{
    private readonly DocumentationCatalogReader _catalogReader;
    private readonly MarkdownDocumentInspector _documentInspector;
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;

    public DocumentationInventoryService(
        ICisRepositoryContextResolver repositoryContextResolver,
        DocumentationCatalogReader catalogReader,
        MarkdownDocumentInspector documentInspector)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _catalogReader = catalogReader;
        _documentInspector = documentInspector;
    }

    public DocumentationInventoryResult Inventory(string repositoryPath)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return new DocumentationInventoryResult(
                "invalid-repository",
                null,
                null,
                [],
                [],
                resolution.Errors,
                RepositoryConfigurationValid: false);
        }

        var context = resolution.Context!;
        var catalogResult = _catalogReader.Read(context.CatalogPath);
        if (!catalogResult.IsSuccess)
        {
            return new DocumentationInventoryResult(
                "invalid-catalog",
                context.RepositoryPath,
                context.DocumentationRoot,
                [],
                [],
                catalogResult.Errors,
                RepositoryConfigurationValid: true);
        }

        var warnings = new List<string>();
        var errors = new List<string>();
        var catalogByPath = catalogResult.Catalog!.Documents
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var documents = new List<DocumentationInventoryItem>();

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var absolutePath in Directory
                     .EnumerateFiles(context.DocumentationPath, "*.md", enumerationOptions)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var repositoryRelativePath = ToRepositoryPath(context.RepositoryPath, absolutePath);
            var documentationRelativePath = Path.GetRelativePath(context.DocumentationPath, absolutePath)
                .Replace('\\', '/');
            catalogByPath.TryGetValue(repositoryRelativePath, out var catalogEntry);

            MarkdownDocumentMetadata metadata;
            try
            {
                metadata = _documentInspector.Inspect(absolutePath, documentationRelativePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Unable to read {repositoryRelativePath}: {exception.Message}");
                continue;
            }

            if (metadata.MalformedFrontMatter)
            {
                warnings.Add($"{repositoryRelativePath} has malformed YAML front matter.");
            }

            var type = FirstNonEmpty(catalogEntry?.Type, metadata.Type, InferType(documentationRelativePath));
            var id = FirstNonEmpty(
                catalogEntry?.Id,
                metadata.StableId,
                CreateInferredId(context.RepositoryId, type, documentationRelativePath));
            var metadataSource = catalogEntry is not null
                ? "catalog"
                : metadata.HasFrontMatter
                    ? "front-matter"
                    : "inferred";

            documents.Add(new DocumentationInventoryItem(
                id,
                repositoryRelativePath,
                metadata.Title,
                type,
                FirstNonEmpty(catalogEntry?.Status, metadata.Status, "unknown"),
                FirstNonEmpty(catalogEntry?.Authority, "unclassified"),
                metadataSource,
                catalogEntry is not null,
                metadata.HasFrontMatter,
                metadata.MalformedFrontMatter));
        }

        return new DocumentationInventoryResult(
            errors.Count == 0 ? "inventoried" : "failed",
            context.RepositoryPath,
            context.DocumentationRoot,
            documents,
            warnings,
            errors,
            RepositoryConfigurationValid: true);
    }

    private static string InferType(string documentationRelativePath)
    {
        var normalized = documentationRelativePath.ToLowerInvariant();
        if (normalized.StartsWith("architecture/decisions/", StringComparison.Ordinal))
        {
            return "architecture-decision";
        }

        if (normalized.StartsWith("architecture/", StringComparison.Ordinal))
        {
            return "architecture";
        }

        if (normalized.StartsWith("specs/", StringComparison.Ordinal))
        {
            return "specification";
        }

        if (normalized.StartsWith("references/", StringComparison.Ordinal))
        {
            return "reference";
        }

        if (normalized.StartsWith("policies/", StringComparison.Ordinal))
        {
            return "policy";
        }

        if (normalized.StartsWith("standards/", StringComparison.Ordinal))
        {
            return "standard";
        }

        if (normalized.StartsWith("workflows/", StringComparison.Ordinal))
        {
            return "workflow";
        }

        if (normalized.StartsWith("procedures/", StringComparison.Ordinal))
        {
            return "procedure";
        }

        if (normalized.StartsWith("sops/", StringComparison.Ordinal))
        {
            return "sop";
        }

        if (normalized.StartsWith("runbooks/", StringComparison.Ordinal))
        {
            return "runbook";
        }

        if (normalized.StartsWith("playbooks/", StringComparison.Ordinal))
        {
            return "playbook";
        }

        if (normalized.StartsWith("checklists/", StringComparison.Ordinal))
        {
            return "checklist";
        }

        if (normalized.StartsWith("plans/", StringComparison.Ordinal))
        {
            return "plan";
        }

        if (normalized.StartsWith("guides/", StringComparison.Ordinal))
        {
            return "guide";
        }

        if (normalized.StartsWith("templates/", StringComparison.Ordinal))
        {
            return "template";
        }

        if (normalized.StartsWith("changes/", StringComparison.Ordinal))
        {
            return "change-record";
        }

        return "document";
    }

    private static string CreateInferredId(string repositoryId, string type, string path)
    {
        var withoutExtension = Path.ChangeExtension(path, null)!.Replace('\\', '/');
        if (withoutExtension.EndsWith("/README", StringComparison.OrdinalIgnoreCase))
        {
            withoutExtension = withoutExtension[..^"/README".Length];
        }
        else if (string.Equals(withoutExtension, "README", StringComparison.OrdinalIgnoreCase))
        {
            withoutExtension = "root";
        }

        var slug = InvalidIdCharacters().Replace(withoutExtension.ToLowerInvariant(), ".").Trim('.');
        var kind = InvalidIdCharacters().Replace(type.ToLowerInvariant(), "-").Trim('-');
        return $"{repositoryId}.{kind}.{slug}";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string ToRepositoryPath(string repositoryPath, string absolutePath)
        => Path.GetRelativePath(repositoryPath, absolutePath).Replace('\\', '/');

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidIdCharacters();
}
