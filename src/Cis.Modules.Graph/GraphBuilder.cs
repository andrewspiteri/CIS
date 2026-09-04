using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Docs;
using Cis.Modules.Api;
using Cis.Modules.Tracker;
using Microsoft.Data.Sqlite;

namespace Cis.Modules.Graph;

public sealed class GraphBuilder
{
    internal const int GraphSchemaVersion = 2;

    private static readonly string[] Extractors =
    [
        "cis.catalog/1",
        "cis.markdown.repository-profile/1",
        "cis.markdown.reference-table/1",
        "cis.markdown.governance-link/1",
        "cis.markdown.api-governance/1",
        "cis.api.state/1",
        "cis.tracker.state/1",
        "cis.markdown.decisions/1",
        "cis.repository-files/1",
        "cis.source-declarations/1",
        "cis.csharp.compiler/3",
        "cis.csharp.semantic-facts/3",
        "cis.test-declarations/1",
        "cis.project-dependencies/1",
        "cis.github-actions/1",
    ];

    internal static IReadOnlyList<string> CurrentExtractors => Extractors;
    internal static IReadOnlyList<string> ComposeExtractors(IEnumerable<ICisGraphAugmenter>? augmenters)
        => Extractors.Concat((augmenters ?? []).Select(item => item.Name).Distinct(StringComparer.Ordinal)).ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly DocumentationCatalogReader _catalogReader;
    private readonly DocumentationInventoryService _inventoryService;
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly SqliteGraphStore _store;
    private readonly DocumentationValidationService _validationService;
    private readonly IReadOnlyList<ICisGraphAugmenter> _augmenters;

    public GraphBuilder(
        ICisRepositoryContextResolver repositoryContextResolver,
        DocumentationCatalogReader catalogReader,
        DocumentationInventoryService inventoryService,
        DocumentationValidationService validationService,
        SqliteGraphStore? store = null,
        IEnumerable<ICisGraphAugmenter>? augmenters = null)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _catalogReader = catalogReader;
        _inventoryService = inventoryService;
        _validationService = validationService;
        _store = store ?? new SqliteGraphStore();
        _augmenters = (augmenters ?? []).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
    }

    public GraphBuildResult Build(string repositoryPath)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return InvalidRepository(resolution.Errors);
        }

        var context = resolution.Context!;
        var diagnostics = new List<CisGraphDiagnostic>();
        var validation = _validationService.Validate(context.RepositoryPath, strict: false);
        diagnostics.AddRange(validation.Errors.Select(error => Diagnostic(
            "CIS-GRAPH-DOC-001",
            "error",
            error)));
        diagnostics.AddRange(validation.Warnings.Select(warning => Diagnostic(
            "CIS-GRAPH-DOC-002",
            "warning",
            warning)));
        if (validation.Errors.Count > 0)
        {
            return Failed(context, diagnostics);
        }

        var catalogResult = _catalogReader.Read(context.CatalogPath);
        if (!catalogResult.IsSuccess)
        {
            diagnostics.AddRange(catalogResult.Errors.Select(error => Diagnostic(
                "CIS-GRAPH-CATALOG-001",
                "error",
                error)));
            return Failed(context, diagnostics);
        }

        var inventory = _inventoryService.Inventory(context.RepositoryPath);
        diagnostics.AddRange(inventory.Errors.Select(error => Diagnostic(
            "CIS-GRAPH-INVENTORY-001",
            "error",
            error)));
        diagnostics.AddRange(inventory.Warnings.Select(warning => Diagnostic(
            "CIS-GRAPH-INVENTORY-002",
            "warning",
            warning)));
        if (inventory.Errors.Count > 0)
        {
            return Failed(context, diagnostics);
        }

        var catalog = catalogResult.Catalog!;
        var inputs = ReadInputs(context, catalog.Documents, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == "error"))
        {
            return Failed(context, diagnostics);
        }

        var git = ReadGitState(context.RepositoryPath);
        var activeExtractors = ComposeExtractors(_augmenters);
        foreach (var duplicate in _augmenters.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            diagnostics.Add(Diagnostic("CIS-GRAPH-AUGMENTER-001", "error", $"Graph augmenter is registered more than once: {duplicate.Key}", duplicate.Key));
        var buildId = CreateBuildId(inputs, activeExtractors);
        var nodes = new Dictionary<string, CisGraphNode>(StringComparer.Ordinal);
        var edges = new Dictionary<string, CisGraphEdge>(StringComparer.Ordinal);
        var hashes = inputs.ToDictionary(input => input.Path, input => input.Hash, StringComparer.OrdinalIgnoreCase);
        var repositoryNode = CreateRepositoryNode(context, hashes);
        AddNode(repositoryNode, nodes, diagnostics);

        var inventoryByPath = inventory.Documents
            .GroupBy(document => document.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var documentNodesById = new Dictionary<string, CisGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Documents.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
        {
            inventoryByPath.TryGetValue(entry.Path, out var inventoryItem);
            var node = CreateDocumentNode(context, entry, inventoryItem, hashes);
            if (AddNode(node, nodes, diagnostics))
            {
                documentNodesById[entry.Id] = node;
                AddEdge(
                    context.RepositoryId,
                    "contains",
                    repositoryNode.Key,
                    node.Key,
                    "declared",
                    "high",
                    node.Provenance,
                    edges);
            }
        }

        var components = ExtractComponents(
            context,
            catalog.Documents,
            documentNodesById,
            hashes,
            nodes,
            edges,
            diagnostics,
            repositoryNode.Key);
        ExtractReferences(
            context,
            catalog.Documents,
            documentNodesById,
            components,
            hashes,
            nodes,
            edges,
            diagnostics);
        ExtractDecisions(
            context,
            catalog.Documents,
            documentNodesById,
            inventoryByPath,
            hashes,
            nodes,
            edges,
            diagnostics);
        ExtractImplementation(
            context,
            components,
            hashes,
            nodes,
            edges,
            diagnostics,
            repositoryNode.Key);
        ExtractApiGovernanceRelationships(
            context,
            catalog.Documents,
            documentNodesById,
            components,
            hashes,
            nodes,
            edges,
            diagnostics);
        ExtractApiDerivedState(context, hashes, nodes, edges, diagnostics, repositoryNode.Key);
        ExtractTrackerDerivedState(context, hashes, nodes, edges, diagnostics, repositoryNode.Key);
        foreach (var augmenter in _augmenters.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() == 1).Select(group => group.Single()))
        {
            var augmentation = augmenter.Augment(context, hashes);
            diagnostics.AddRange(augmentation.Diagnostics);
            foreach (var node in augmentation.Nodes) AddNode(node, nodes, diagnostics);
            foreach (var edge in augmentation.Edges) AddEdge(context.RepositoryId, edge.Type, edge.From, edge.To,
                edge.State, edge.Confidence, edge.Observations, edges, edge.Properties);
        }
        ValidateEdges(nodes, edges, diagnostics);

        var orderedDiagnostics = diagnostics
            .GroupBy(DiagnosticKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(diagnostic => SeverityOrder(diagnostic.Severity))
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
        if (orderedDiagnostics.Any(diagnostic => diagnostic.Severity == "error"))
        {
            return Failed(context, orderedDiagnostics, buildId, nodes.Count, edges.Count);
        }

        var summary = CreateSummary(orderedDiagnostics);
        var graphStatus = summary.Warnings > 0 ? "partial" : "complete";
        var graph = new CisGraphDocument(
            GraphSchemaVersion,
            new CisGraphBuildMetadata(buildId, context.RepositoryId, git.Head, git.Dirty, graphStatus),
            nodes.Values.OrderBy(node => node.Key, StringComparer.Ordinal).ToArray(),
            edges.Values.OrderBy(edge => edge.Key, StringComparer.Ordinal).ToArray(),
            summary);
        var manifest = new CisGraphManifest(
            GraphSchemaVersion,
            buildId,
            context.RepositoryId,
            context.DocumentationRoot,
            git.Head,
            git.Dirty,
            inputs,
            activeExtractors);

        return Persist(context, graph, manifest, orderedDiagnostics);
    }

    private static IReadOnlyList<CisGraphInput> ReadInputs(
        CisRepositoryContext context,
        IReadOnlyList<DocumentationCatalogEntry> documents,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        IReadOnlyList<string> implementationPaths;
        try
        {
            implementationPaths = ImplementationGraphExtractor.EnumerateInputPaths(context.RepositoryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-INPUT-004",
                "error",
                $"Unable to enumerate implementation graph inputs: {exception.Message}",
                context.RepositoryPath));
            implementationPaths = [];
        }

        var paths = new[] { ".cis/repository.yml", ToRepositoryPath(context.RepositoryPath, context.CatalogPath) }
            .Concat(documents.Select(document => document.Path))
            .Concat(implementationPaths)
            .Concat(new[] { ApiGovernanceService.StatePath, ApiGovernanceService.DiagnosticsPath,
                TrackerService.StatePath, TrackerService.ConflictsPath }
                .Where(path => File.Exists(Path.Combine(context.RepositoryPath, path.Replace('/', Path.DirectorySeparatorChar)))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        var inputs = new List<CisGraphInput>();
        foreach (var path in paths)
        {
            var absolutePath = Path.GetFullPath(Path.Combine(
                context.RepositoryPath,
                path.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-INPUT-001",
                    "error",
                    $"Graph input does not exist: {path}",
                    path));
                continue;
            }

            try
            {
                var normalizedPath = path.Replace('\\', '/');
                inputs.Add(new CisGraphInput(normalizedPath, HashInput(normalizedPath, absolutePath)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-INPUT-002",
                    "error",
                    $"Unable to hash graph input '{path}': {exception.Message}",
                    path));
            }
        }

        return inputs;
    }

    private static CisGraphNode CreateRepositoryNode(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, string> hashes)
    {
        const string path = ".cis/repository.yml";
        var evidence = Evidence(
            "repository-configuration",
            path,
            "configuration",
            context.RepositoryId,
            hashes.GetValueOrDefault(path),
            "declared",
            "cis.catalog/1",
            "high");
        return new CisGraphNode(
            CreateNodeKey(context.RepositoryId, "repository", context.RepositoryId),
            context.RepositoryId,
            "repository",
            "local-repository",
            context.RepositoryId,
            context.RepositoryId,
            ["workspace"],
            "canonical",
            "active",
            [new CisGraphLocation(path, "configuration", context.RepositoryId)],
            SortedProperties(("documentationRoot", context.DocumentationRoot)),
            [evidence]);
    }

    private static CisGraphNode CreateDocumentNode(
        CisRepositoryContext context,
        DocumentationCatalogEntry entry,
        DocumentationInventoryItem? inventory,
        IReadOnlyDictionary<string, string> hashes)
    {
        var facets = new List<string>();
        if (entry.Type.Contains("specification", StringComparison.OrdinalIgnoreCase))
        {
            facets.Add("specification");
        }

        if (entry.Type.Contains("reference", StringComparison.OrdinalIgnoreCase))
        {
            facets.Add("reference");
        }

        if (entry.Type.Contains("decision", StringComparison.OrdinalIgnoreCase))
        {
            facets.Add("decision");
        }

        if (string.Equals(entry.Authority, "canonical", StringComparison.OrdinalIgnoreCase))
        {
            facets.Add("canonical-document");
        }

        var method = string.Equals(entry.Authority, "proposal", StringComparison.OrdinalIgnoreCase)
            ? "proposed"
            : "declared";
        var evidence = Evidence(
            "canonical-markdown",
            entry.Path,
            "document",
            entry.Id,
            hashes.GetValueOrDefault(entry.Path),
            method,
            "cis.catalog/1",
            "high");
        return new CisGraphNode(
            CreateNodeKey(context.RepositoryId, "document", entry.Id),
            context.RepositoryId,
            "document",
            entry.Type.ToLowerInvariant(),
            entry.Id,
            inventory?.Title ?? entry.Id,
            facets.Order(StringComparer.Ordinal).ToArray(),
            entry.Authority.ToLowerInvariant(),
            entry.Status.ToLowerInvariant(),
            [new CisGraphLocation(entry.Path, "document", entry.Id)],
            SortedProperties(
                ("path", entry.Path),
                ("metadataSource", inventory?.MetadataSource ?? "catalog")),
            [evidence]);
    }

    private static IReadOnlyDictionary<string, CisGraphNode> ExtractComponents(
        CisRepositoryContext context,
        IReadOnlyList<DocumentationCatalogEntry> documents,
        IReadOnlyDictionary<string, CisGraphNode> documentNodesById,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics,
        string repositoryNodeKey)
    {
        var result = new Dictionary<string, CisGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in documents.Where(IsRepositoryProfile))
        {
            var absolutePath = Path.Combine(
                context.RepositoryPath,
                entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadText(absolutePath, entry.Path, diagnostics, out var content))
            {
                continue;
            }

            documentNodesById.TryGetValue(entry.Id, out var profileDocument);
            foreach (var component in MarkdownGraphExtractor.ExtractComponents(content))
            {
                if (component.Id.Contains("::", StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "CIS-GRAPH-ID-001",
                        "error",
                        $"Component ID contains reserved delimiter '::': {component.Id}",
                        entry.Path));
                    continue;
                }

                var evidence = Evidence(
                    "canonical-markdown",
                    entry.Path,
                    "heading",
                    component.Id,
                    hashes.GetValueOrDefault(entry.Path),
                    "declared",
                    "cis.markdown.repository-profile/1",
                    component.Confidence);
                var node = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "component", component.Id),
                    context.RepositoryId,
                    "component",
                    InferComponentSubtype(component),
                    component.Id,
                    component.Id,
                    component.Roles.Order(StringComparer.Ordinal).ToArray(),
                    entry.Authority.ToLowerInvariant(),
                    entry.Status.ToLowerInvariant(),
                    [new CisGraphLocation(entry.Path, "heading", component.Id)],
                    SortedProperties(
                        ("root", component.Root),
                        ("languages", string.Join(',', component.Languages)),
                        ("frameworks", string.Join(',', component.Frameworks)),
                        ("roles", string.Join(',', component.Roles)),
                        ("capabilities", string.Join(',', component.Capabilities)),
                        ("confidence", component.Confidence),
                        ("evidence", string.Join("; ", component.Evidence))),
                    [evidence]);
                if (!AddNode(node, nodes, diagnostics))
                {
                    continue;
                }

                result[component.Id] = node;
                AddEdge(context.RepositoryId, "contains", repositoryNodeKey, node.Key, "declared", "high", [evidence], edges);
                if (profileDocument is not null)
                {
                    AddEdge(context.RepositoryId, "describes", profileDocument.Key, node.Key, "declared", "high", [evidence], edges);
                }
            }
        }

        return result;
    }

    private static void ExtractReferences(
        CisRepositoryContext context,
        IReadOnlyList<DocumentationCatalogEntry> documents,
        IReadOnlyDictionary<string, CisGraphNode> documentNodesById,
        IReadOnlyDictionary<string, CisGraphNode> components,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        foreach (var entry in documents.Where(document =>
                     document.Type.Contains("reference", StringComparison.OrdinalIgnoreCase)
                     && !IsRepositoryProfile(document)))
        {
            var absolutePath = Path.Combine(
                context.RepositoryPath,
                entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadText(absolutePath, entry.Path, diagnostics, out var content))
            {
                continue;
            }

            var table = MarkdownGraphExtractor.ExtractReferenceTable(entry.Path, content);
            if (table is null)
            {
                continue;
            }

            if (table.Headers.Count == 0)
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-REF-001",
                    "error",
                    $"Governed reference has no Markdown table: {entry.Path}",
                    entry.Path));
                continue;
            }

            var missingHeaders = table.Family.IdentityFields
                .Where(field => !table.Headers.Contains(field, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missingHeaders.Length > 0)
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-REF-002",
                    "error",
                    $"Reference identity fields are missing from {entry.Path}: {string.Join(", ", missingHeaders)}",
                    entry.Path));
                continue;
            }

            if (!documentNodesById.TryGetValue(entry.Id, out var documentNode))
            {
                continue;
            }

            var governingId = MarkdownGraphExtractor.ExtractGoverningDocumentId(content);
            if (!string.IsNullOrWhiteSpace(governingId))
            {
                if (documentNodesById.TryGetValue(governingId, out var governingDocument))
                {
                    AddEdge(
                        context.RepositoryId,
                        "governed-by",
                        documentNode.Key,
                        governingDocument.Key,
                        "declared",
                        "high",
                        documentNode.Provenance,
                        edges);
                }
                else
                {
                    diagnostics.Add(Diagnostic(
                        "CIS-GRAPH-EDGE-001",
                        "warning",
                        $"Governing document ID was not found for {entry.Path}: {governingId}",
                        entry.Path,
                        governingId));
                }
            }

            foreach (var row in table.Rows)
            {
                var identityParts = MarkdownGraphExtractor.CreateIdentityParts(table.Family, row);
                if (identityParts.Any(value => string.IsNullOrWhiteSpace(value)
                    || string.Equals(value, "TODO", StringComparison.OrdinalIgnoreCase)))
                {
                    diagnostics.Add(Diagnostic(
                        "CIS-GRAPH-REF-003",
                        "information",
                        $"Reference placeholder or incomplete identity was skipped in {entry.Path}.",
                        entry.Path));
                    continue;
                }

                var localId = MarkdownGraphExtractor.CreateReferenceLocalId(table.Family, identityParts);
                var label = MarkdownGraphExtractor.CreateReferenceLabel(table.Family, row, identityParts);
                var rowIdentity = string.Join(" / ", identityParts);
                var evidence = Evidence(
                    "canonical-markdown",
                    entry.Path,
                    "table-row",
                    rowIdentity,
                    hashes.GetValueOrDefault(entry.Path),
                    "declared",
                    "cis.markdown.reference-table/1",
                    "high");
                var lifecycle = MarkdownGraphExtractor.GetValue(row, "Status");
                var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in row.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    properties[pair.Key] = pair.Value;
                }
                properties["graphIdentity"] = string.Join(" / ", identityParts);

                var node = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "reference-item", localId),
                    context.RepositoryId,
                    "reference-item",
                    table.Family.Subtype,
                    localId,
                    label,
                    table.Family.Facets.Order(StringComparer.Ordinal).ToArray(),
                    entry.Authority.ToLowerInvariant(),
                    string.IsNullOrWhiteSpace(lifecycle) ? entry.Status.ToLowerInvariant() : lifecycle.ToLowerInvariant(),
                    [new CisGraphLocation(entry.Path, "table-row", rowIdentity)],
                    properties,
                    [evidence]);
                if (!AddNode(node, nodes, diagnostics))
                {
                    continue;
                }

                AddEdge(context.RepositoryId, "declares", documentNode.Key, node.Key, "declared", "high", [evidence], edges);
                foreach (var owner in MarkdownGraphExtractor.FindOwners(table.Family, row))
                {
                    if (components.TryGetValue(owner, out var component))
                    {
                        AddEdge(context.RepositoryId, "owns", component.Key, node.Key, "declared", "high", [evidence], edges);
                    }
                }
            }
        }
    }

    private static void ExtractDecisions(
        CisRepositoryContext context,
        IReadOnlyList<DocumentationCatalogEntry> documents,
        IReadOnlyDictionary<string, CisGraphNode> documentNodesById,
        IReadOnlyDictionary<string, DocumentationInventoryItem> inventoryByPath,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var promotedByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in documents.Where(document =>
                     string.Equals(document.Type, "change-decisions", StringComparison.OrdinalIgnoreCase)))
        {
            var absolutePath = Path.Combine(context.RepositoryPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadText(absolutePath, entry.Path, diagnostics, out var content))
            {
                continue;
            }

            documentNodesById.TryGetValue(entry.Id, out var documentNode);
            foreach (var row in ExtractDecisionRows(content))
            {
                var localId = $"{entry.Id}:{row.Id}";
                var evidence = Evidence(
                    "canonical-markdown",
                    entry.Path,
                    "table-row",
                    row.Id,
                    hashes.GetValueOrDefault(entry.Path),
                    "declared",
                    "cis.markdown.decisions/1",
                    "high");
                var node = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "decision", localId),
                    context.RepositoryId,
                    "decision",
                    row.Category,
                    localId,
                    row.Question,
                    ["decision", row.Blocking ? "blocking" : "advisory"],
                    "canonical",
                    row.Status == "resolved" ? "active" : "draft",
                    [new CisGraphLocation(entry.Path, "table-row", row.Id)],
                    SortedProperties(
                        ("decisionId", row.Id),
                        ("blocking", row.Blocking.ToString().ToLowerInvariant()),
                        ("requiredBefore", row.RequiredBefore),
                        ("status", row.Status),
                        ("resolution", row.Resolution),
                        ("promotedAdr", row.PromotedAdr)),
                    [evidence]);
                if (!AddNode(node, nodes, diagnostics))
                {
                    continue;
                }

                if (documentNode is not null)
                {
                    AddEdge(context.RepositoryId, "declares", documentNode.Key, node.Key, "declared", "high", [evidence], edges);
                }

                if (row.PromotedAdr.Length > 0)
                {
                    promotedByPath[row.PromotedAdr] = node.Key;
                }
            }
        }

        foreach (var entry in documents.Where(document =>
                     string.Equals(document.Type, "architecture-decision", StringComparison.OrdinalIgnoreCase)))
        {
            documentNodesById.TryGetValue(entry.Id, out var documentNode);
            inventoryByPath.TryGetValue(entry.Path, out var inventory);
            var evidence = Evidence(
                "canonical-markdown",
                entry.Path,
                "document",
                entry.Id,
                hashes.GetValueOrDefault(entry.Path),
                "declared",
                "cis.markdown.decisions/1",
                "high");
            var node = new CisGraphNode(
                CreateNodeKey(context.RepositoryId, "decision", entry.Id),
                context.RepositoryId,
                "decision",
                "architecture",
                entry.Id,
                inventory?.Title ?? entry.Id,
                ["decision", "architecture", "promoted"],
                "canonical",
                "active",
                [new CisGraphLocation(entry.Path, "document", entry.Id)],
                SortedProperties(("path", entry.Path), ("status", entry.Status)),
                [evidence]);
            if (!AddNode(node, nodes, diagnostics))
            {
                continue;
            }

            if (documentNode is not null)
            {
                AddEdge(context.RepositoryId, "declares", documentNode.Key, node.Key, "declared", "high", [evidence], edges);
            }

            if (promotedByPath.TryGetValue(entry.Path, out var originKey))
            {
                AddEdge(context.RepositoryId, "generated-from", node.Key, originKey, "confirmed", "high", [evidence], edges);
            }
        }
    }

    private static void ExtractApiGovernanceRelationships(
        CisRepositoryContext context,
        IReadOnlyList<DocumentationCatalogEntry> documents,
        IReadOnlyDictionary<string, CisGraphNode> documentNodesById,
        IReadOnlyDictionary<string, CisGraphNode> components,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var governanceEntry = documents.FirstOrDefault(document =>
            document.Id.EndsWith(":spec:api-design-and-governance", StringComparison.OrdinalIgnoreCase)
            || document.Path.EndsWith("/api-design-and-governance-spec.md", StringComparison.OrdinalIgnoreCase));
        var rules = new Dictionary<string, CisGraphNode>(StringComparer.OrdinalIgnoreCase);
        if (governanceEntry is not null)
        {
            var absolutePath = Path.Combine(context.RepositoryPath, governanceEntry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (TryReadText(absolutePath, governanceEntry.Path, diagnostics, out var content))
            {
                var lineNumber = 0;
                foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    lineNumber++;
                    var match = System.Text.RegularExpressions.Regex.Match(
                        rawLine,
                        "^- `(?<id>API-[A-Z0-9-]+)`: (?<text>.+)$",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var ruleId = match.Groups["id"].Value;
                    var evidence = Evidence(
                        "canonical-markdown",
                        governanceEntry.Path,
                        "line",
                        lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        hashes.GetValueOrDefault(governanceEntry.Path),
                        "declared",
                        "cis.markdown.api-governance/1",
                        "high");
                    var rule = new CisGraphNode(
                        CreateNodeKey(context.RepositoryId, "api-rule", ruleId),
                        context.RepositoryId,
                        "api-rule",
                        "governance",
                        ruleId,
                        match.Groups["text"].Value.Trim(),
                        ["contract", "rule", "api"],
                        "canonical",
                        "active",
                        [new CisGraphLocation(governanceEntry.Path, "line", lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                        SortedProperties(("ruleId", ruleId), ("path", governanceEntry.Path)),
                        [evidence]);
                    if (AddNode(rule, nodes, diagnostics))
                    {
                        rules[ruleId] = rule;
                        if (documentNodesById.TryGetValue(governanceEntry.Id, out var documentNode))
                        {
                            AddEdge(context.RepositoryId, "declares", documentNode.Key, rule.Key, "declared", "high", [evidence], edges);
                        }
                    }
                }
            }
        }

        var permissions = nodes.Values.Where(node => node.Kind == "reference-item" && node.Subtype == "permission")
            .ToDictionary(node => node.Properties.GetValueOrDefault("Permission code") ?? node.Properties.GetValueOrDefault("graphIdentity") ?? node.LocalId,
                StringComparer.OrdinalIgnoreCase);
        var problems = nodes.Values.Where(node => node.Kind == "reference-item" && node.Subtype == "problem")
            .ToDictionary(node => node.Properties.GetValueOrDefault("Problem ID") ?? node.Properties.GetValueOrDefault("graphIdentity") ?? node.LocalId,
                StringComparer.OrdinalIgnoreCase);

        foreach (var operation in nodes.Values.Where(node => node.Kind == "reference-item" && node.Subtype == "api-operation").ToArray())
        {
            var permissionText = operation.Properties.GetValueOrDefault("Permission / auth") ?? string.Empty;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(permissionText, "\\bPERM-[A-Za-z0-9_.:-]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (permissions.TryGetValue(match.Value, out var permission))
                {
                    AddEdge(context.RepositoryId, "authorized-by", operation.Key, permission.Key, "declared", "high", operation.Provenance, edges);
                }
            }

            var errorText = operation.Properties.GetValueOrDefault("Error contract") ?? string.Empty;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(errorText, "\\bPROB-[A-Za-z0-9_.:-]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (problems.TryGetValue(match.Value, out var problem))
                {
                    AddEdge(context.RepositoryId, "fails-with", operation.Key, problem.Key, "declared", "high", operation.Provenance, edges);
                }
            }

            var consumerText = operation.Properties.GetValueOrDefault("Consumer") ?? string.Empty;
            foreach (var consumerId in consumerText.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (components.TryGetValue(consumerId, out var consumer))
                {
                    AddEdge(context.RepositoryId, "consumed-by", operation.Key, consumer.Key, "declared", "high", operation.Provenance, edges);
                }
            }

            var applicableRules = new List<string>
            {
                "API-EXP-01", "API-SCOPE-01", "API-SCOPE-02", "API-ROUTE-01", "API-ROUTE-02",
                "API-THIN-01", "API-DTO-01", "API-OAS-01", "API-DICT-01", "API-AUTHZ-01",
                "API-ERR-01", "API-ERR-02", "API-RATE-01", "API-IDEM-01", "API-CONC-01", "API-VER-01",
            };
            if (string.Equals(operation.Properties.GetValueOrDefault("Exposure"), "public", StringComparison.OrdinalIgnoreCase))
            {
                applicableRules.AddRange(["API-PUB-01", "API-PUB-02"]);
            }
            foreach (var ruleId in applicableRules.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (rules.TryGetValue(ruleId, out var rule))
                {
                    AddEdge(context.RepositoryId, "governed-by", operation.Key, rule.Key, "declared", "high", operation.Provenance, edges);
                }
            }
        }
    }

    private static void ExtractApiDerivedState(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics,
        string repositoryNodeKey)
    {
        var statePath = Path.Combine(context.RepositoryPath, ApiGovernanceService.StatePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(statePath))
        {
            return;
        }

        ApiOperation[] operations;
        ApiDiagnostic[] apiDiagnostics = [];
        try
        {
            operations = JsonSerializer.Deserialize<ApiOperation[]>(File.ReadAllText(statePath), JsonOptions) ?? [];
            var diagnosticsPath = Path.Combine(context.RepositoryPath, ApiGovernanceService.DiagnosticsPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(diagnosticsPath))
            {
                apiDiagnostics = JsonSerializer.Deserialize<ApiDiagnostic[]>(File.ReadAllText(diagnosticsPath), JsonOptions) ?? [];
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic("CIS-GRAPH-API-001", "warning", $"Normalized API state could not be read: {exception.Message}", ApiGovernanceService.StatePath));
            return;
        }

        var canonicalOperations = nodes.Values.Where(node => node.Kind == "reference-item" && node.Subtype == "api-operation")
            .ToDictionary(node => node.Properties.GetValueOrDefault("API ID") ?? node.Properties.GetValueOrDefault("graphIdentity") ?? node.LocalId,
                StringComparer.OrdinalIgnoreCase);
        var rules = nodes.Values.Where(node => node.Kind == "api-rule")
            .ToDictionary(node => node.LocalId, StringComparer.OrdinalIgnoreCase);
        var testsByPath = nodes.Values.Where(node => node.Kind == "test")
            .SelectMany(node => node.Locations.Select(location => (location.Path, Node: node)))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Node).DistinctBy(node => node.Key).ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations)
        {
            if (!canonicalOperations.TryGetValue(operation.ApiId, out var canonical))
            {
                continue;
            }

            if (operation.OpenApiDeclared && !string.IsNullOrWhiteSpace(operation.OpenApiOperation))
            {
                var localId = $"{operation.OpenApiDocument ?? "unknown"}#{operation.OpenApiOperation}";
                var evidencePath = operation.OpenApiDocument ?? ApiGovernanceService.StatePath;
                var evidence = Evidence("derived-api-state", evidencePath, "operation", operation.OpenApiOperation,
                    hashes.GetValueOrDefault(evidencePath), "correlated", "cis.api.state/1", "high");
                var contract = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "openapi-operation", localId), context.RepositoryId,
                    "openapi-operation", "http-contract", localId, $"{operation.Method} {operation.Path}",
                    ["contract", "api"], "derived", "active",
                    [new CisGraphLocation(evidencePath, "operation", operation.OpenApiOperation)],
                    SortedProperties(("operationId", operation.OpenApiOperation), ("method", operation.Method), ("path", operation.Path), ("document", evidencePath)),
                    [evidence]);
                AddOrMergeNode(contract, nodes, diagnostics);
                AddEdge(context.RepositoryId, "documented-by", canonical.Key, contract.Key, "confirmed", "high", [evidence with { Method = "confirmed-correlation" }], edges);
            }

            foreach (var testPath in operation.KnownTests.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (testsByPath.TryGetValue(testPath, out var tests))
                {
                    foreach (var test in tests)
                    {
                        AddEdge(context.RepositoryId, "verified-by", canonical.Key, test.Key, "declared", "high", canonical.Provenance, edges);
                    }
                }
            }
        }

        for (var index = 0; index < apiDiagnostics.Length; index++)
        {
            var item = apiDiagnostics[index];
            var localId = $"{item.Code}-{index + 1}";
            var evidence = Evidence("derived-api-state", ApiGovernanceService.DiagnosticsPath, "array-index", index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hashes.GetValueOrDefault(ApiGovernanceService.DiagnosticsPath), "deterministic-validation", "cis.api.state/1", "high");
            var finding = new CisGraphNode(
                CreateNodeKey(context.RepositoryId, "api-finding", localId), context.RepositoryId,
                "api-finding", item.Severity, localId, item.Message, ["api", "finding", item.Severity],
                "derived", "active", [new CisGraphLocation(ApiGovernanceService.DiagnosticsPath, "array-index", index.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                SortedProperties(("code", item.Code), ("severity", item.Severity), ("rule", item.Rule), ("remediation", item.Remediation)), [evidence]);
            if (!AddNode(finding, nodes, diagnostics)) continue;
            AddEdge(context.RepositoryId, "has-finding", repositoryNodeKey, finding.Key, "discovered", "high", [evidence], edges);
            var operation = operations.FirstOrDefault(candidate => item.Message.Contains(candidate.ApiId, StringComparison.OrdinalIgnoreCase)
                || item.Message.Contains($"{candidate.Method} {candidate.Path}", StringComparison.OrdinalIgnoreCase));
            if (operation is not null && canonicalOperations.TryGetValue(operation.ApiId, out var canonical))
            {
                AddEdge(context.RepositoryId, "has-finding", canonical.Key, finding.Key, "discovered", "high", [evidence], edges);
            }
            if (rules.TryGetValue(item.Rule, out var rule))
            {
                AddEdge(context.RepositoryId, "governed-by", finding.Key, rule.Key, "declared", "high", [evidence], edges);
            }
        }
    }

    private static void ExtractTrackerDerivedState(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics,
        string repositoryNodeKey)
    {
        var statePath = Path.Combine(context.RepositoryPath, TrackerService.StatePath.Replace('/', Path.DirectorySeparatorChar));
        var conflictsPath = Path.Combine(context.RepositoryPath, TrackerService.ConflictsPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(statePath) && !File.Exists(conflictsPath)) return;

        TrackerMapping[] mappings = [];
        TrackerConflict[] conflicts = [];
        try
        {
            if (File.Exists(statePath)) mappings = JsonSerializer.Deserialize<TrackerMapping[]>(File.ReadAllText(statePath), JsonOptions) ?? [];
            if (File.Exists(conflictsPath)) conflicts = JsonSerializer.Deserialize<TrackerConflict[]>(File.ReadAllText(conflictsPath), JsonOptions) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic("CIS-GRAPH-TRACKER-001", "warning", $"External tracker state could not be read: {exception.Message}", TrackerService.StatePath));
            return;
        }

        var taskDocuments = nodes.Values.Where(node => node.Kind == "document" && node.Subtype == "agent-task")
            .ToArray();
        CisGraphNode? FindTask(string changeId, string taskId) => taskDocuments.FirstOrDefault(node =>
            node.Locations.Any(location => location.Path.Replace('\\', '/').EndsWith($"/changes/{changeId}/agent-tasks/{taskId}.md", StringComparison.OrdinalIgnoreCase)));

        var remoteNodes = new Dictionary<string, CisGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            var localId = $"{mapping.Provider}/{mapping.RemoteId}";
            var evidence = Evidence("derived-tracker-state", TrackerService.StatePath, "mapping", localId,
                hashes.GetValueOrDefault(TrackerService.StatePath), "three-way-sync", "cis.tracker.state/1", "high");
            var remote = new CisGraphNode(
                CreateNodeKey(context.RepositoryId, "external-work-item", localId), context.RepositoryId,
                "external-work-item", mapping.Provider, localId, $"{mapping.Provider} {mapping.RemoteId}",
                ["external-tracker", mapping.Provider], "derived", "active",
                [new CisGraphLocation(TrackerService.StatePath, "mapping", localId)],
                SortedProperties(("provider", mapping.Provider), ("remoteId", mapping.RemoteId), ("url", mapping.Url),
                    ("changeId", mapping.ChangeId), ("taskId", mapping.TaskId), ("linkState", mapping.State)), [evidence]);
            AddOrMergeNode(remote, nodes, diagnostics);
            remoteNodes[$"{mapping.Provider}|{mapping.ChangeId}|{mapping.TaskId}"] = remote;
            AddEdge(context.RepositoryId, "contains", repositoryNodeKey, remote.Key, "discovered", "high", [evidence], edges);
            var task = FindTask(mapping.ChangeId, mapping.TaskId);
            if (task is not null) AddEdge(context.RepositoryId, "mirrored-by", task.Key, remote.Key, "confirmed", "high", [evidence with { Method = "confirmed-tracker-link" }], edges);
        }

        foreach (var conflict in conflicts)
        {
            var evidence = Evidence("derived-tracker-state", TrackerService.ConflictsPath, "conflict", conflict.Id,
                hashes.GetValueOrDefault(TrackerService.ConflictsPath), "three-way-conflict", "cis.tracker.state/1", "high");
            var finding = new CisGraphNode(
                CreateNodeKey(context.RepositoryId, "tracker-conflict", conflict.Id), context.RepositoryId,
                "tracker-conflict", conflict.Kind, conflict.Id, conflict.Message,
                ["external-tracker", "conflict", conflict.Status], conflict.Status == "open" ? "review-required" : "derived", "active",
                [new CisGraphLocation(TrackerService.ConflictsPath, "conflict", conflict.Id)],
                SortedProperties(("provider", conflict.Provider), ("changeId", conflict.ChangeId), ("taskId", conflict.TaskId),
                    ("kind", conflict.Kind), ("status", conflict.Status), ("resolution", conflict.Resolution ?? "none")), [evidence]);
            if (!AddNode(finding, nodes, diagnostics)) continue;
            AddEdge(context.RepositoryId, "has-conflict", repositoryNodeKey, finding.Key, "discovered", "high", [evidence], edges);
            var task = FindTask(conflict.ChangeId, conflict.TaskId);
            if (task is not null) AddEdge(context.RepositoryId, "has-conflict", task.Key, finding.Key, "discovered", "high", [evidence], edges);
            if (remoteNodes.TryGetValue($"{conflict.Provider}|{conflict.ChangeId}|{conflict.TaskId}", out var remote))
                AddEdge(context.RepositoryId, "has-conflict", remote.Key, finding.Key, "discovered", "high", [evidence], edges);
        }
    }

    private static IReadOnlyList<ExtractedDecisionRow> ExtractDecisionRows(string content)
    {
        var rows = new List<ExtractedDecisionRow>();
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.StartsWith("| DEC-", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 13 || !bool.TryParse(cells[4], out var blocking))
            {
                continue;
            }

            rows.Add(new ExtractedDecisionRow(
                cells[1],
                cells[2],
                cells[3],
                blocking,
                cells[5],
                cells[6],
                cells[8],
                cells[11]));
        }

        return rows;
    }

    private static void ExtractImplementation(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, CisGraphNode> components,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics,
        string repositoryNodeKey)
    {
        var sourcePaths = hashes.Keys
            .Where(ImplementationGraphExtractor.IsSourceFile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceNodes = new Dictionary<string, CisGraphNode>(StringComparer.OrdinalIgnoreCase);
        var testSources = new List<(CisGraphNode Node, string Content)>();
        var compilerInputs = new List<CSharpCompilerInput>();

        foreach (var path in sourcePaths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var absolutePath = Path.Combine(context.RepositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadText(absolutePath, path, diagnostics, out var content))
            {
                continue;
            }

            var component = FindComponentForPath(path, components.Values);
            var componentId = component?.LocalId ?? context.RepositoryId;
            var language = ImplementationGraphExtractor.LanguageFor(path);
            var sourceEvidence = Evidence(
                "repository-source",
                path,
                "file",
                path,
                hashes.GetValueOrDefault(path),
                "discovered",
                "cis.repository-files/1",
                "high");
            var sourceNode = new CisGraphNode(
                CreateNodeKey(context.RepositoryId, "source-file", path),
                context.RepositoryId,
                "source-file",
                language,
                path,
                Path.GetFileName(path),
                [],
                "canonical",
                "active",
                [new CisGraphLocation(path, "file", path)],
                SortedProperties(("language", language), ("component", component?.LocalId ?? string.Empty)),
                [sourceEvidence]);
            if (!AddNode(sourceNode, nodes, diagnostics))
            {
                continue;
            }

            sourceNodes[path] = sourceNode;
            if (language == "csharp")
            {
                compilerInputs.Add(new CSharpCompilerInput(path, content, componentId));
            }

            AddEdge(context.RepositoryId, "contains", repositoryNodeKey, sourceNode.Key, "discovered", "high", [sourceEvidence], edges);
            if (component is not null)
            {
                AddEdge(context.RepositoryId, "belongs-to", sourceNode.Key, component.Key, "discovered", "high", [sourceEvidence], edges);
            }

            foreach (var declaration in ImplementationGraphExtractor.ExtractDeclarations(path, content, componentId))
            {
                var evidence = Evidence(
                    "repository-source",
                    path,
                    "line",
                    declaration.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    hashes.GetValueOrDefault(path),
                    "syntax-recognition",
                    "cis.source-declarations/1",
                    "medium",
                    "Lexical language adapter; no compiler binding was performed.");
                var symbol = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "symbol", declaration.LocalId),
                    context.RepositoryId,
                    "symbol",
                    declaration.Kind,
                    declaration.LocalId,
                    declaration.Name,
                    [declaration.Language],
                    "derived",
                    "active",
                    [new CisGraphLocation(path, "line", declaration.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    SortedProperties(
                        ("language", declaration.Language),
                        ("component", component?.LocalId ?? string.Empty),
                        ("qualifiedName", declaration.Name)),
                    [evidence]);
                AddOrMergeNode(symbol, nodes, diagnostics);
                AddEdge(context.RepositoryId, "contains", sourceNode.Key, symbol.Key, "discovered", "medium", [evidence], edges);
                if (component is not null)
                {
                    AddEdge(context.RepositoryId, "belongs-to", symbol.Key, component.Key, "discovered", "medium", [evidence], edges);
                    AddEdge(context.RepositoryId, "owns", component.Key, symbol.Key, "discovered", "medium", [evidence], edges);
                }
            }

            foreach (var discoveredTest in ImplementationGraphExtractor.ExtractTests(path, content, componentId))
            {
                var evidence = Evidence(
                    "repository-test",
                    path,
                    "line",
                    discoveredTest.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    hashes.GetValueOrDefault(path),
                    "framework-syntax",
                    "cis.test-declarations/1",
                    "high");
                var test = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "test", discoveredTest.LocalId),
                    context.RepositoryId,
                    "test",
                    discoveredTest.Framework,
                    discoveredTest.LocalId,
                    discoveredTest.Name,
                    ["test", discoveredTest.Language],
                    "derived",
                    "active",
                    [new CisGraphLocation(path, "line", discoveredTest.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    SortedProperties(
                        ("framework", discoveredTest.Framework),
                        ("language", discoveredTest.Language),
                        ("component", component?.LocalId ?? string.Empty)),
                    [evidence]);
                if (!AddNode(test, nodes, diagnostics))
                {
                    continue;
                }

                testSources.Add((test, content));
                AddEdge(context.RepositoryId, "contains", sourceNode.Key, test.Key, "discovered", "high", [evidence], edges);
                if (component is not null)
                {
                    AddEdge(context.RepositoryId, "belongs-to", test.Key, component.Key, "discovered", "high", [evidence], edges);
                }
            }
        }

        ExtractCSharpCompilerEvidence(
            context,
            components,
            hashes,
            nodes,
            edges,
            sourceNodes,
            compilerInputs,
            diagnostics);
        ExtractDependencies(context, components, hashes, nodes, edges, diagnostics);
        ExtractWorkflows(context, components, hashes, nodes, edges, diagnostics, repositoryNodeKey);
        LinkImplementationEvidence(context, nodes, edges, sourcePaths, sourceNodes, testSources);
    }

    private static void ExtractCSharpCompilerEvidence(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, CisGraphNode> components,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        IReadOnlyDictionary<string, CisGraphNode> sourceNodes,
        IReadOnlyList<CSharpCompilerInput> inputs,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        CSharpCompilerAnalysis analysis;
        try
        {
            analysis = CSharpCompilerAnalyzer.Analyze(inputs);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-CSHARP-001",
                "warning",
                $"C# compiler analysis was unavailable; lexical extraction remains active: {exception.Message}"));
            return;
        }
        var callsByCaller = analysis.Calls
            .GroupBy(call => call.CallerLocalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(call => call.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var discovered in analysis.Symbols)
        {
            if (!sourceNodes.TryGetValue(discovered.Path, out var sourceNode))
            {
                continue;
            }

            var line = discovered.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var evidence = Evidence(
                "repository-source",
                discovered.Path,
                "line",
                line,
                hashes.GetValueOrDefault(discovered.Path),
                "compiler-binding",
                "cis.csharp.compiler/3",
                "high",
                "Roslyn resolved the declaration within the repository compilation.");
            var callSemantics = callsByCaller.GetValueOrDefault(discovered.LocalId)?
                .SelectMany(call => call.Semantics)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray() ?? [];
            var symbol = new CisGraphNode(
                CreateNodeKey(context.RepositoryId, "symbol", discovered.LocalId),
                context.RepositoryId,
                "symbol",
                discovered.Kind,
                discovered.LocalId,
                discovered.QualifiedName,
                new[] { "csharp", "compiler-bound" }
                    .Concat(discovered.Facets)
                    .Concat(callSemantics.Contains("http-endpoint-registration", StringComparer.Ordinal) ? ["http-endpoint-registration"] : [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                "derived",
                "active",
                [new CisGraphLocation(discovered.Path, "line", line)],
                SortedProperties(
                    ("language", "csharp"),
                    ("component", discovered.ComponentId),
                    ("qualifiedName", discovered.QualifiedName),
                    ("signature", discovered.Signature),
                    ("binding", "compiler"),
                    ("external", "false"),
                    ("attributes", string.Join("; ", discovered.Attributes)),
                    ("interfaces", string.Join("; ", discovered.Interfaces)),
                    ("generated", (discovered.GeneratedReasons.Count > 0).ToString().ToLowerInvariant()),
                    ("generatedReasons", string.Join("; ", discovered.GeneratedReasons)),
                    ("callSemantics", string.Join("; ", callSemantics))),
                [evidence]);
            AddOrMergeNode(symbol, nodes, diagnostics);
            AddEdge(context.RepositoryId, "contains", sourceNode.Key, symbol.Key, "discovered", "high", [evidence], edges);
            if (components.TryGetValue(discovered.ComponentId, out var component))
            {
                AddEdge(context.RepositoryId, "belongs-to", symbol.Key, component.Key, "discovered", "high", [evidence], edges);
                AddEdge(context.RepositoryId, "owns", component.Key, symbol.Key, "discovered", "high", [evidence], edges);
            }
        }

        var referencedById = analysis.ReferencedSymbols.ToDictionary(symbol => symbol.LocalId, StringComparer.Ordinal);
        foreach (var relation in analysis.Annotations.Concat(analysis.Interfaces))
        {
            var sourceKey = CreateNodeKey(context.RepositoryId, "symbol", relation.SourceLocalId);
            if (!nodes.ContainsKey(sourceKey)) continue;
            var evidence = SemanticEvidence(relation.Path, relation.Line, hashes, relation.Type, relation.TargetLocalId);
            var targetKey = EnsureCompilerTarget(context, relation.TargetLocalId, relation.Path, relation.Line, evidence, referencedById, nodes, diagnostics);
            if (targetKey is null) continue;
            AddEdge(context.RepositoryId, relation.Type, sourceKey, targetKey, "discovered", "high", [evidence], edges);
            if (relation.Type == "annotated-by"
                && referencedById.TryGetValue(relation.TargetLocalId, out var attribute)
                && attribute.Semantics.Contains("authorization", StringComparer.Ordinal))
                AddEdge(context.RepositoryId, "authorized-by", sourceKey, targetKey, "discovered", "high", [evidence], edges);
        }

        var invocationKeys = new Dictionary<(string Caller, int Ordinal), string>();
        foreach (var call in analysis.Calls)
        {
            var callerKey = CreateNodeKey(context.RepositoryId, "symbol", call.CallerLocalId);
            if (!nodes.ContainsKey(callerKey)) continue;
            var evidence = SemanticEvidence(call.Path, call.Line, hashes, "compiler-call", call.TargetQualifiedName);
            var targetKey = EnsureCompilerTarget(context, call.TargetLocalId, call.Path, call.Line, evidence, referencedById, nodes, diagnostics);
            if (targetKey is null) continue;
            var properties = SortedProperties(
                ("scope", call.TargetExternal ? "external" : "repository"),
                ("genericArguments", string.Join("; ", call.GenericArguments)),
                ("argumentKinds", string.Join("; ", call.ArgumentKinds)),
                ("semantics", string.Join("; ", call.Semantics)),
                ("ordinal", call.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            AddEdge(context.RepositoryId, "calls", callerKey, targetKey, "discovered", "high", [evidence], edges, properties);

            // Ordinary call identity and argument facts already live on the calls edge.
            // Materialize invocation nodes only when order contributes to bounded dataflow;
            // otherwise every call adds a node plus three largely redundant edges.
            if (call.Semantics.Count > 0 || call.GenericArguments.Count > 0)
            {
                var invocationLocalId = $"{call.CallerLocalId}/invocation/{Uri.EscapeDataString(call.Path)}/{call.SpanStart}-{call.SpanLength}";
                var invocationKey = CreateNodeKey(context.RepositoryId, "invocation", invocationLocalId);
                var invocation = new CisGraphNode(
                    invocationKey,
                    context.RepositoryId,
                    "invocation",
                    "call",
                    invocationLocalId,
                    $"{call.CallerName} -> {call.TargetName}",
                    new[] { "csharp", "compiler-bound", call.TargetExternal ? "external-call" : "repository-call" }
                        .Concat(call.Semantics).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    "derived",
                    "active",
                    [new CisGraphLocation(call.Path, "line", call.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    SortedProperties(
                        ("caller", call.CallerLocalId),
                        ("target", call.TargetLocalId),
                        ("scope", call.TargetExternal ? "external" : "repository"),
                        ("ordinal", call.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        ("genericArguments", string.Join("; ", call.GenericArguments)),
                        ("argumentKinds", string.Join("; ", call.ArgumentKinds)),
                        ("semantics", string.Join("; ", call.Semantics))),
                    [evidence]);
                AddNode(invocation, nodes, diagnostics);
                AddEdge(context.RepositoryId, "invokes", callerKey, invocationKey, "discovered", "high", [evidence], edges);
                AddEdge(context.RepositoryId, "targets", invocationKey, targetKey, "discovered", "high", [evidence], edges);
                if (call.Semantics.Any(IsFlowSemantic))
                {
                    invocationKeys[(call.CallerLocalId, call.Ordinal)] = invocationKey;
                }
            }

            foreach (var semanticEdge in SemanticEdges(call.Semantics))
                AddEdge(context.RepositoryId, semanticEdge, callerKey, targetKey, "discovered", "high", [evidence], edges, properties);
            if (!call.TargetExternal)
                AddEdge(context.RepositoryId, "delegates", callerKey, targetKey, "discovered", "high", [evidence], edges);

            foreach (var test in nodes.Values.Where(node => node.Kind == "test"
                && string.Equals(node.Label, call.CallerName, StringComparison.Ordinal)
                && node.Locations.Any(location => string.Equals(location.Path, call.Path, StringComparison.OrdinalIgnoreCase))).ToArray())
                AddEdge(context.RepositoryId, "calls", test.Key, targetKey, "discovered", "high", [evidence], edges, properties);
        }

        foreach (var group in analysis.Calls.GroupBy(call => call.CallerLocalId, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(call => call.Ordinal).ToArray();
            var materialized = ordered
                .Where(call => invocationKeys.ContainsKey((group.Key, call.Ordinal)))
                .ToArray();
            for (var index = 0; index + 1 < materialized.Length; index++)
            {
                if (!invocationKeys.TryGetValue((group.Key, materialized[index].Ordinal), out var from)
                    || !invocationKeys.TryGetValue((group.Key, materialized[index + 1].Ordinal), out var to)) continue;
                var evidence = SemanticEvidence(materialized[index].Path, materialized[index].Line, hashes, "call-order", materialized[index + 1].TargetQualifiedName);
                AddEdge(context.RepositoryId, "precedes", from, to, "discovered", "high", [evidence], edges);
            }

            var invariants = FlowInvariants(ordered);
            var orderedSemantics = ordered.SelectMany(call => call.Semantics.Where(IsFlowSemantic)).ToArray();
            if (orderedSemantics.Length == 0) continue;
            var first = ordered[0];
            var flowEvidence = SemanticEvidence(first.Path, first.Line, hashes, "bounded-dataflow", group.Key);
            var flowLocalId = $"{group.Key}/dataflow";
            var flowKey = CreateNodeKey(context.RepositoryId, "dataflow", flowLocalId);
            var flow = new CisGraphNode(
                flowKey,
                context.RepositoryId,
                "dataflow",
                "call-order",
                flowLocalId,
                $"Bounded call flow for {first.CallerName}",
                ["csharp", "compiler-bound", "bounded"],
                "derived",
                "active",
                [new CisGraphLocation(first.Path, "line", first.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                SortedProperties(
                    ("caller", group.Key),
                    ("orderedSemantics", string.Join(">", orderedSemantics)),
                    ("invariants", string.Join("; ", invariants))),
                [flowEvidence]);
            AddNode(flow, nodes, diagnostics);
            var callerKey = CreateNodeKey(context.RepositoryId, "symbol", group.Key);
            AddEdge(context.RepositoryId, "has-dataflow", callerKey, flowKey, "discovered", "high", [flowEvidence], edges);
        }
    }

    private static string? EnsureCompilerTarget(
        CisRepositoryContext context,
        string localId,
        string path,
        int line,
        CisGraphEvidence evidence,
        IReadOnlyDictionary<string, CSharpCompilerReferencedSymbol> referenced,
        IDictionary<string, CisGraphNode> nodes,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var key = CreateNodeKey(context.RepositoryId, "symbol", localId);
        if (nodes.ContainsKey(key)) return key;
        if (!referenced.TryGetValue(localId, out var target)) return null;
        var node = new CisGraphNode(
            key,
            context.RepositoryId,
            "symbol",
            target.Kind,
            target.LocalId,
            target.QualifiedName,
            new[] { "csharp", "compiler-bound", "external" }.Concat(target.Semantics).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "derived",
            "active",
            [new CisGraphLocation(path, "line", line.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            SortedProperties(
                ("language", "csharp"),
                ("component", $"external:{target.Assembly}"),
                ("qualifiedName", target.QualifiedName),
                ("signature", target.Signature),
                ("assembly", target.Assembly),
                ("external", "true"),
                ("semantics", string.Join("; ", target.Semantics))),
            [evidence]);
        AddOrMergeNode(node, nodes, diagnostics);
        return key;
    }

    private static CisGraphEvidence SemanticEvidence(
        string path,
        int line,
        IReadOnlyDictionary<string, string> hashes,
        string method,
        string reason)
        => Evidence(
            "repository-source",
            path,
            "line",
            line.ToString(System.Globalization.CultureInfo.InvariantCulture),
            hashes.GetValueOrDefault(path),
            method,
            "cis.csharp.semantic-facts/3",
            "high",
            $"Roslyn produced a bounded semantic fact for {reason}.");

    private static IEnumerable<string> SemanticEdges(IReadOnlyList<string> semantics)
    {
        if (semantics.Contains("authorization", StringComparer.Ordinal)) yield return "authorized-by";
        if (semantics.Contains("options-binding", StringComparer.Ordinal)) yield return "binds-options";
        if (semantics.Contains("configuration-read", StringComparer.Ordinal)) yield return "reads-configuration";
        if (semantics.Contains("logging", StringComparer.Ordinal)) yield return "logging-call";
        if (semantics.Contains("structured-logging", StringComparer.Ordinal)) yield return "structured-log";
        if (semantics.Contains("unstructured-logging", StringComparer.Ordinal)) yield return "unstructured-log";
        if (semantics.Contains("persistence", StringComparer.Ordinal)) yield return "persistence-call";
        if (semantics.Contains("transaction-commit", StringComparer.Ordinal)) yield return "transaction-call";
        if (semantics.Contains("outbox-write", StringComparer.Ordinal)) yield return "outbox-write";
        if (semantics.Contains("event-publish", StringComparer.Ordinal)) yield return "event-publish";
        if (semantics.Contains("idempotency-check", StringComparer.Ordinal)) yield return "idempotency-check";
    }

    private static IReadOnlyList<string> FlowInvariants(IReadOnlyList<CSharpCompilerCall> calls)
    {
        var result = new List<string>();
        int First(string semantic) => calls.FirstOrDefault(call => call.Semantics.Contains(semantic, StringComparer.Ordinal))?.Ordinal ?? int.MaxValue;
        var stateWrite = First("state-write");
        var outboxWrite = First("outbox-write");
        var commit = First("transaction-commit");
        var publish = First("event-publish");
        var idempotency = First("idempotency-check");
        var firstEffect = Math.Min(stateWrite, publish);
        if (stateWrite < commit) result.Add("repository-before-commit");
        if (stateWrite < outboxWrite && outboxWrite < commit && (publish == int.MaxValue || commit < publish)) result.Add("outbox-atomic-commit");
        if (idempotency < firstEffect) result.Add("idempotency-before-effects");
        return result;
    }

    private static bool IsFlowSemantic(string value)
        => value is "state-write" or "outbox-write" or "transaction-commit" or "event-publish" or "idempotency-check";

    private static void ExtractDependencies(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, CisGraphNode> components,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        foreach (var path in hashes.Keys.Where(IsDependencyManifest).Order(StringComparer.OrdinalIgnoreCase))
        {
            var absolutePath = Path.Combine(context.RepositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadText(absolutePath, path, diagnostics, out var content))
            {
                continue;
            }

            var owner = FindComponentForPath(path, components.Values);
            foreach (var discovered in ImplementationGraphExtractor.ExtractDependencies(path, content))
            {
                var evidence = Evidence(
                    "project-manifest",
                    path,
                    "declaration",
                    discovered.Name,
                    hashes.GetValueOrDefault(path),
                    "structured-declaration",
                    "cis.project-dependencies/1",
                    "high");
                if (discovered.Ecosystem == "project")
                {
                    var target = discovered.ProjectPath is null
                        ? null
                        : FindComponentForPath(discovered.ProjectPath, components.Values);
                    if (owner is not null && target is not null)
                    {
                        AddEdge(context.RepositoryId, "depends-on", owner.Key, target.Key, "discovered", "high", [evidence], edges);
                        continue;
                    }
                }

                var dependency = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "dependency", discovered.LocalId),
                    context.RepositoryId,
                    "dependency",
                    discovered.Ecosystem,
                    discovered.LocalId,
                    discovered.Name,
                    ["dependency"],
                    "derived",
                    "active",
                    [new CisGraphLocation(path, "declaration", discovered.Name)],
                    SortedProperties(
                        ("ecosystem", discovered.Ecosystem),
                        ("version", discovered.Version),
                        ("projectPath", discovered.ProjectPath ?? string.Empty)),
                    [evidence]);
                AddOrMergeNode(dependency, nodes, diagnostics);
                if (owner is not null)
                {
                    AddEdge(context.RepositoryId, "depends-on", owner.Key, dependency.Key, "discovered", "high", [evidence], edges);
                }
            }
        }
    }

    private static void ExtractWorkflows(
        CisRepositoryContext context,
        IReadOnlyDictionary<string, CisGraphNode> components,
        IReadOnlyDictionary<string, string> hashes,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics,
        string repositoryNodeKey)
    {
        foreach (var path in hashes.Keys.Where(IsWorkflowPath).Order(StringComparer.OrdinalIgnoreCase))
        {
            var absolutePath = Path.Combine(context.RepositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadText(absolutePath, path, diagnostics, out var content))
            {
                continue;
            }

            foreach (var discovered in ImplementationGraphExtractor.ExtractWorkflows(path, content))
            {
                var evidence = Evidence(
                    "workflow-definition",
                    path,
                    "line",
                    discovered.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    hashes.GetValueOrDefault(path),
                    "yaml-job",
                    "cis.github-actions/1",
                    "high");
                var workflow = new CisGraphNode(
                    CreateNodeKey(context.RepositoryId, "workflow", discovered.LocalId),
                    context.RepositoryId,
                    "workflow",
                    "github-actions-job",
                    discovered.LocalId,
                    discovered.Name,
                    ["ci"],
                    "canonical",
                    "active",
                    [new CisGraphLocation(path, "line", discovered.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    SortedProperties(("provider", "github-actions"), ("job", discovered.Name)),
                    [evidence]);
                if (!AddNode(workflow, nodes, diagnostics))
                {
                    continue;
                }

                AddEdge(context.RepositoryId, "contains", repositoryNodeKey, workflow.Key, "discovered", "high", [evidence], edges);
                foreach (var component in components.Values.Where(component => WorkflowReferencesComponent(content, component)))
                {
                    AddEdge(context.RepositoryId, "depends-on", workflow.Key, component.Key, "discovered", "medium", [evidence], edges);
                }
            }
        }
    }

    private static void LinkImplementationEvidence(
        CisRepositoryContext context,
        IDictionary<string, CisGraphNode> nodes,
        IDictionary<string, CisGraphEdge> edges,
        IReadOnlySet<string> sourcePaths,
        IReadOnlyDictionary<string, CisGraphNode> sourceNodes,
        IReadOnlyList<(CisGraphNode Node, string Content)> testSources)
    {
        foreach (var reference in nodes.Values.Where(node => node.Kind == "reference-item").ToArray())
        {
            var evidenceValue = reference.Properties
                .FirstOrDefault(pair => string.Equals(pair.Key, "Evidence", StringComparison.OrdinalIgnoreCase)).Value;
            var evidencePath = string.IsNullOrWhiteSpace(evidenceValue)
                ? null
                : ImplementationGraphExtractor.EvidencePath(evidenceValue, sourcePaths);
            if (evidencePath is not null && sourceNodes.TryGetValue(evidencePath, out var sourceNode))
            {
                AddEdge(
                    context.RepositoryId,
                    "implemented-by",
                    reference.Key,
                    sourceNode.Key,
                    "declared",
                    "high",
                    reference.Provenance.Concat(sourceNode.Provenance).ToArray(),
                    edges);
            }

            var identity = reference.Properties.GetValueOrDefault("graphIdentity");
            if (string.IsNullOrWhiteSpace(identity) || identity.Length < 6)
            {
                continue;
            }

            foreach (var testSource in testSources.Where(test => test.Content.Contains(identity, StringComparison.Ordinal)))
            {
                AddEdge(
                    context.RepositoryId,
                    "verified-by",
                    reference.Key,
                    testSource.Node.Key,
                    "discovered",
                    "high",
                    testSource.Node.Provenance,
                    edges);
            }
        }
    }

    private static CisGraphNode? FindComponentForPath(string path, IEnumerable<CisGraphNode> components)
    {
        var normalizedPath = path.Replace('\\', '/').TrimStart('/');
        return components
            .Select(component => (Component: component, Root: NormalizeRoot(component.Properties.GetValueOrDefault("root"))))
            .Where(item => item.Root.Length == 0
                || normalizedPath.Equals(item.Root, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(item.Root + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Root.Length)
            .ThenBy(item => item.Component.LocalId, StringComparer.Ordinal)
            .Select(item => item.Component)
            .FirstOrDefault();
    }

    private static string NormalizeRoot(string? root)
        => string.IsNullOrWhiteSpace(root) || root == "."
            ? string.Empty
            : root.Replace('\\', '/').Trim('/');

    private static bool WorkflowReferencesComponent(string content, CisGraphNode component)
    {
        var root = NormalizeRoot(component.Properties.GetValueOrDefault("root"));
        return root.Length > 0 && content.Replace('\\', '/').Contains(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDependencyManifest(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Package.swift", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkflowPath(string path)
        => path.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private GraphBuildResult Persist(
        CisRepositoryContext context,
        CisGraphDocument graph,
        CisGraphManifest manifest,
        IReadOnlyList<CisGraphDiagnostic> diagnostics)
    {
        const string graphRelativePath = SqliteGraphStore.DatabaseRelativePath;
        const string manifestRelativePath = ".cis/local/graph/manifest.json";
        const string diagnosticsRelativePath = ".cis/local/graph/diagnostics.json";
        var graphPath = Path.Combine(context.RepositoryPath, graphRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var manifestPath = Path.Combine(context.RepositoryPath, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var diagnosticsPath = Path.Combine(context.RepositoryPath, diagnosticsRelativePath.Replace('/', Path.DirectorySeparatorChar));
        GraphStoreBuildState? existing = null;
        try { existing = _store.ReadBuildState(context.RepositoryPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException)
        {
        }
        if (existing is not null
            && string.Equals(existing.BuildId, graph.Build.Id, StringComparison.Ordinal)
            && string.Equals(existing.Head, graph.Build.Head, StringComparison.Ordinal)
            && existing.Dirty == graph.Build.Dirty
            && FileContentMatches(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions))
            && FileContentMatches(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions)))
        {
            return new GraphBuildResult(
                "unchanged",
                context.RepositoryPath,
                context.DocumentationRoot,
                graph.Build.Id,
                graphRelativePath,
                manifestRelativePath,
                diagnosticsRelativePath,
                graph.Nodes.Count,
                graph.Edges.Count,
                diagnostics,
                Applied: false,
                RepositoryConfigurationValid: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(graphPath)!);
        try
        {
            _store.Write(context.RepositoryPath, graph, manifest, diagnostics);
            WriteAtomic(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions));
            WriteAtomic(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
            var legacyGraphPath = Path.Combine(context.RepositoryPath, ".cis", "local", "graph", "graph.json");
            if (File.Exists(legacyGraphPath)) File.Delete(legacyGraphPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException)
        {
            return Failed(context, diagnostics.Append(Diagnostic(
                "CIS-GRAPH-STORE-002", "error", $"Unable to persist the SQLite graph transaction: {exception.Message}", graphRelativePath)).ToArray(),
                graph.Build.Id, graph.Nodes.Count, graph.Edges.Count);
        }
        return new GraphBuildResult(
            graph.Build.Status == "partial" ? "partial" : "built",
            context.RepositoryPath,
            context.DocumentationRoot,
            graph.Build.Id,
            graphRelativePath,
            manifestRelativePath,
            diagnosticsRelativePath,
            graph.Nodes.Count,
            graph.Edges.Count,
            diagnostics,
            Applied: true,
            RepositoryConfigurationValid: true);
    }

    private static bool AddNode(
        CisGraphNode node,
        IDictionary<string, CisGraphNode> nodes,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (node.RepositoryId.Contains("::", StringComparison.Ordinal)
            || node.LocalId.Contains("::", StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-ID-002",
                "error",
                $"Graph identity contains reserved delimiter '::': {node.Key}",
                node.Key));
            return false;
        }

        if (!nodes.TryAdd(node.Key, node))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-ID-003",
                "error",
                $"Duplicate graph node identity: {node.Key}",
                node.Key));
            return false;
        }

        return true;
    }

    private static void AddOrMergeNode(
        CisGraphNode node,
        IDictionary<string, CisGraphNode> nodes,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (!nodes.TryGetValue(node.Key, out var existing))
        {
            AddNode(node, nodes, diagnostics);
            return;
        }

        if (!string.Equals(existing.Kind, node.Kind, StringComparison.Ordinal)
            || !string.Equals(existing.Subtype, node.Subtype, StringComparison.Ordinal)
            || !string.Equals(existing.LocalId, node.LocalId, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-ID-003",
                "error",
                $"Duplicate graph node identity has incompatible semantics: {node.Key}",
                node.Key));
            return;
        }

        nodes[node.Key] = existing with
        {
            Facets = existing.Facets
                .Concat(node.Facets)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Locations = existing.Locations
                .Concat(node.Locations)
                .Distinct()
                .OrderBy(location => location.Path, StringComparer.Ordinal)
                .ThenBy(location => location.LocatorValue, StringComparer.Ordinal)
                .ToArray(),
            Provenance = existing.Provenance
                .Concat(node.Provenance)
                .Distinct()
                .OrderBy(evidence => evidence.Path, StringComparer.Ordinal)
                .ThenBy(evidence => evidence.LocatorValue, StringComparer.Ordinal)
                .ToArray(),
            Properties = MergeProperties(existing.Properties, node.Properties),
        };
    }

    private static IReadOnlyDictionary<string, string> MergeProperties(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in first)
        {
            result[pair.Key] = pair.Value;
        }

        foreach (var pair in second)
        {
            if (!result.TryGetValue(pair.Key, out var existing)
                || string.IsNullOrWhiteSpace(existing))
            {
                result[pair.Key] = pair.Value;
                continue;
            }

            if (string.IsNullOrWhiteSpace(pair.Value)
                || string.Equals(existing, pair.Value, StringComparison.Ordinal))
            {
                continue;
            }

            result[pair.Key] = string.Join(
                "; ",
                new[] { existing, pair.Value }
                    .SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal));
        }

        return result;
    }

    private static void AddEdge(
        string repositoryId,
        string type,
        string from,
        string to,
        string state,
        string confidence,
        IReadOnlyList<CisGraphEvidence> observations,
        IDictionary<string, CisGraphEdge> edges,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var edgeKey = $"{repositoryId}::edge::{HashText($"{from}\n{type}\n{to}")}";
        if (edges.TryGetValue(edgeKey, out var existing))
        {
            edges[edgeKey] = existing with
            {
                Observations = existing.Observations
                    .Concat(observations)
                    .Distinct()
                    .OrderBy(observation => observation.Path, StringComparer.Ordinal)
                    .ThenBy(observation => observation.LocatorValue, StringComparer.Ordinal)
                    .ToArray(),
                Properties = properties is null
                    ? existing.Properties
                    : MergeProperties(existing.Properties, properties),
            };
            return;
        }

        edges[edgeKey] = new CisGraphEdge(
            edgeKey,
            type,
            from,
            to,
            state,
            confidence,
            observations,
            properties ?? new SortedDictionary<string, string>(StringComparer.Ordinal));
    }

    private static void ValidateEdges(
        IReadOnlyDictionary<string, CisGraphNode> nodes,
        IReadOnlyDictionary<string, CisGraphEdge> edges,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        foreach (var edge in edges.Values)
        {
            if (!nodes.ContainsKey(edge.From))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-EDGE-002",
                    "error",
                    $"Graph edge source does not exist: {edge.From}",
                    edge.Key));
            }

            if (!nodes.ContainsKey(edge.To))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-EDGE-003",
                    "error",
                    $"Graph edge target does not exist: {edge.To}",
                    edge.Key));
            }
        }
    }

    private static string InferComponentSubtype(ExtractedComponent component)
        => component.Roles.FirstOrDefault()
            ?? component.Frameworks.FirstOrDefault()
            ?? component.Languages.FirstOrDefault()
            ?? "component";

    private static bool IsRepositoryProfile(DocumentationCatalogEntry entry)
        => string.Equals(entry.Type, "repository-profile", StringComparison.OrdinalIgnoreCase)
            || entry.Path.EndsWith("/references/repository-profile.md", StringComparison.OrdinalIgnoreCase)
            || entry.Path.EndsWith("/repository-profile.md", StringComparison.OrdinalIgnoreCase);

    private static CisGraphEvidence Evidence(
        string sourceKind,
        string path,
        string locatorKind,
        string locatorValue,
        string? contentHash,
        string method,
        string extractor,
        string confidence,
        string? confidenceReason = null)
        => new(
            sourceKind,
            path,
            locatorKind,
            locatorValue,
            contentHash,
            method,
            extractor,
            NormalizeConfidence(confidence),
            confidenceReason);

    private static string NormalizeConfidence(string confidence)
        => confidence.ToLowerInvariant() switch
        {
            "high" => "high",
            "medium" => "medium",
            _ => "low",
        };

    private static IReadOnlyDictionary<string, string> SortedProperties(
        params (string Name, string Value)[] values)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            properties[name] = value;
        }

        return properties;
    }

    private static string CreateNodeKey(string repositoryId, string kind, string localId)
        => $"{repositoryId}::{kind}::{localId}";

    internal static string CreateBuildId(IReadOnlyList<CisGraphInput> inputs)
        => CreateBuildId(inputs, Extractors);

    internal static string CreateBuildId(IReadOnlyList<CisGraphInput> inputs, IReadOnlyList<string> extractors)
        => "sha256:" + HashText(string.Join(
            '\n',
            new[] { $"schema:{GraphSchemaVersion}" }
                .Concat(extractors.Select(extractor => $"extractor:{extractor}"))
                .Concat(inputs
                    .Where(input => !IsManagedChangeDossierPath(input.Path))
                    .Select(input => $"input:{input.Path}={input.Hash}"))));

    internal static string HashInput(string relativePath, string absolutePath)
    {
        if (IsManagedChangeDossierPath(relativePath))
            return "sha256:" + HashText("managed-change-dossier:" + relativePath.Replace('\\', '/').ToLowerInvariant());
        if (IsCatalogPath(relativePath))
        {
            var normalized = NormalizeCatalogForBaseline(File.ReadAllText(absolutePath));
            return "sha256:" + HashText(normalized);
        }
        using var stream = File.OpenRead(absolutePath);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static bool IsManagedChangeDossierPath(string path)
        => Regex.IsMatch(path.Replace('\\', '/'), @"(^|/)changes/CIS-\d{4}(/|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static bool IsCatalogPath(string path)
        => path.Replace('\\', '/').EndsWith("/catalog.yml", StringComparison.OrdinalIgnoreCase)
           || path.Replace('\\', '/').EndsWith("/catalog.yaml", StringComparison.OrdinalIgnoreCase)
           || path.Equals("catalog.yml", StringComparison.OrdinalIgnoreCase)
           || path.Equals("catalog.yaml", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCatalogForBaseline(string content)
    {
        var blocks = Regex.Matches(content, @"(?ms)^  - id:.*?(?=^  - id:|\z)");
        if (blocks.Count == 0) return content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var builder = new StringBuilder();
        builder.Append(content[..blocks[0].Index]
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd());
        builder.Append('\n');
        foreach (Match block in blocks)
        {
            if (Regex.IsMatch(block.Value, @"(?im)^\s+path:\s+.*(?:^|/)changes/CIS-\d{4}(?:/|\s*$)",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                continue;
            builder.Append(block.Value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .TrimEnd());
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static bool TryReadText(
        string absolutePath,
        string relativePath,
        ICollection<CisGraphDiagnostic> diagnostics,
        out string content)
    {
        try
        {
            content = File.ReadAllText(absolutePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            content = string.Empty;
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-INPUT-003",
                "error",
                $"Unable to read graph input '{relativePath}': {exception.Message}",
                relativePath));
            return false;
        }
    }

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static GitState ReadGitState(string repositoryPath)
    {
        var head = RunGit(repositoryPath, "rev-parse", "HEAD");
        var status = RunGit(repositoryPath, "status", "--porcelain");
        return new GitState(
            string.IsNullOrWhiteSpace(head) ? null : head.Trim(),
            !string.IsNullOrWhiteSpace(status));
    }

    private static string? RunGit(string repositoryPath, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo { FileName = "git", WorkingDirectory = repositoryPath };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(2));
            return !result.TimedOut && result.ExitCode == 0 ? result.StandardOutput : null;
        }
        catch (Exception exception) when (exception is Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool FileContentMatches(string path, string content)
    {
        try
        {
            return File.Exists(path)
                && string.Equals(File.ReadAllText(path).TrimEnd('\r', '\n'), content, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static CisGraphDiagnosticSummary CreateSummary(IReadOnlyList<CisGraphDiagnostic> diagnostics)
        => new(
            diagnostics.Count(diagnostic => diagnostic.Severity == "error"),
            diagnostics.Count(diagnostic => diagnostic.Severity == "warning"),
            diagnostics.Count(diagnostic => diagnostic.Severity == "info"));

    private static string DiagnosticKey(CisGraphDiagnostic diagnostic)
        => string.Join(
            '\u001f',
            new[] { diagnostic.Code, diagnostic.Severity, diagnostic.Message }
                .Concat(diagnostic.Evidence));

    private static CisGraphDiagnostic Diagnostic(
        string code,
        string severity,
        string message,
        params string[] evidence)
        => new(code, severity, message, evidence);

    private static int SeverityOrder(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2,
    };

    private static string ToRepositoryPath(string repositoryPath, string path)
        => Path.GetRelativePath(repositoryPath, path).Replace('\\', '/');

    private static GraphBuildResult InvalidRepository(IReadOnlyList<string> errors)
        => new(
            "invalid-repository",
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            errors.Select(error => Diagnostic("CIS-GRAPH-REPO-001", "error", error)).ToArray(),
            Applied: false,
            RepositoryConfigurationValid: false);

    private static GraphBuildResult Failed(
        CisRepositoryContext context,
        IReadOnlyList<CisGraphDiagnostic> diagnostics,
        string? buildId = null,
        int nodeCount = 0,
        int edgeCount = 0)
        => new(
            "failed",
            context.RepositoryPath,
            context.DocumentationRoot,
            buildId,
            SqliteGraphStore.DatabaseRelativePath,
            ".cis/local/graph/manifest.json",
            ".cis/local/graph/diagnostics.json",
            nodeCount,
            edgeCount,
            diagnostics,
            Applied: false,
            RepositoryConfigurationValid: true);

    private sealed record GitState(string? Head, bool Dirty);

    private sealed record ExtractedDecisionRow(
        string Id,
        string Category,
        string Question,
        bool Blocking,
        string RequiredBefore,
        string Status,
        string Resolution,
        string PromotedAdr);

}
