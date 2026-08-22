using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed partial class GraphValidator
{
    private const string GraphRelativePath = SqliteGraphStore.DatabaseRelativePath;
    private const string ManifestRelativePath = ".cis/local/graph/manifest.json";

    private static readonly HashSet<string> NodeKinds = new(StringComparer.Ordinal)
    {
        "repository",
        "component",
        "document",
        "document-section",
        "requirement",
        "decision",
        "reference-item",
        "source-file",
        "symbol",
        "invocation",
        "dataflow",
        "test",
        "workflow",
        "dependency",
        "api-rule",
        "openapi-operation",
        "api-finding",
        "external-work-item",
        "tracker-conflict",
    };

    private static readonly HashSet<string> Authorities = new(StringComparer.Ordinal)
    {
        "canonical",
        "routing",
        "derived",
        "proposal",
        "review-required",
        "stale",
    };

    private static readonly HashSet<string> Lifecycles = new(StringComparer.Ordinal)
    {
        "draft",
        "review-required",
        "planned",
        "active",
        "verified",
        "completed",
        "implemented",
        "archived",
        "deprecated",
        "withdrawn",
        "proposal",
    };

    private static readonly HashSet<string> EdgeTypes = new(StringComparer.Ordinal)
    {
        "contains",
        "declares",
        "references",
        "governed-by",
        "describes",
        "owns",
        "belongs-to",
        "implemented-by",
        "consumed-by",
        "produced-by",
        "depends-on",
        "calls",
        "annotated-by",
        "implements",
        "inherits",
        "invokes",
        "targets",
        "precedes",
        "delegates",
        "has-dataflow",
        "binds-options",
        "reads-configuration",
        "logging-call",
        "structured-log",
        "unstructured-log",
        "persistence-call",
        "transaction-call",
        "outbox-write",
        "event-publish",
        "idempotency-check",
        "publishes",
        "subscribes-to",
        "configured-by",
        "deployed-by",
        "verified-by",
        "authorized-by",
        "fails-with",
        "documented-by",
        "has-finding",
        "generated-from",
        "supersedes",
    };

    private static readonly HashSet<string> EdgeStates = new(StringComparer.Ordinal)
    {
        "declared",
        "discovered",
        "proposed",
        "confirmed",
        "rejected",
        "possibly-stale",
    };

    private static readonly HashSet<string> Confidences = new(StringComparer.Ordinal)
    {
        "high",
        "medium",
        "low",
    };

    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly SqliteGraphStore _store;

    public GraphValidator(ICisRepositoryContextResolver repositoryContextResolver, SqliteGraphStore? store = null)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _store = store ?? new SqliteGraphStore();
    }

    public GraphValidationResult Validate(string repositoryPath, bool strict)
    {
        var resolution = _repositoryContextResolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return new GraphValidationResult(
                "invalid-repository",
                null,
                null,
                null,
                "unknown",
                0,
                0,
                resolution.Errors.Select(error => Diagnostic(
                    "CIS-GRAPH-VALIDATE-REPO-001",
                    "error",
                    error)).ToArray(),
                strict,
                RepositoryConfigurationValid: false,
                GraphAvailable: false);
        }

        var context = resolution.Context!;
        var graphPath = AbsolutePath(context.RepositoryPath, GraphRelativePath);
        if (!File.Exists(graphPath))
        {
            return Unavailable(
                context.RepositoryPath,
                strict,
                "Graph generation is missing. Run `cis graph build` first.",
                graphPath);
        }

        var stored = _store.Read(context.RepositoryPath);
        if (!stored.Success || stored.Graph is null || stored.Manifest is null)
        {
            return Unavailable(
                context.RepositoryPath,
                strict,
                $"SQLite graph generation is empty or incompatible: {stored.Error}. Run `cis graph build`.",
                graphPath);
        }
        var graph = stored.Graph;
        var manifest = stored.Manifest;

        var diagnostics = new List<CisGraphDiagnostic>(stored.Diagnostics);
        ValidateGenerationMetadata(context, graph, manifest, diagnostics);
        var nodesByKey = ValidateNodes(context, graph.Nodes, diagnostics);
        ValidateEdges(context.RepositoryPath, graph.Edges, nodesByKey, diagnostics);
        ValidateManifest(context.RepositoryPath, manifest, diagnostics);
        ValidateGitTracking(context.RepositoryPath, diagnostics);

        var ordered = diagnostics
            .GroupBy(DiagnosticKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(diagnostic => SeverityOrder(diagnostic.Severity))
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
        var freshness = ordered.Any(diagnostic => diagnostic.Code.StartsWith(
            "CIS-GRAPH-VALIDATE-STALE-",
            StringComparison.Ordinal))
                ? "stale"
                : "fresh";
        var status = ordered.Any(diagnostic => diagnostic.Severity == "error")
            ? "invalid"
            : ordered.Any(diagnostic => diagnostic.Severity == "warning")
                ? "warnings"
                : "valid";
        return new GraphValidationResult(
            status,
            context.RepositoryPath,
            GraphRelativePath,
            graph.Build.Id,
            freshness,
            graph.Nodes.Count,
            graph.Edges.Count,
            ordered,
            strict,
            RepositoryConfigurationValid: true,
            GraphAvailable: true);
    }

    private static void ValidateGenerationMetadata(
        CisRepositoryContext context,
        CisGraphDocument graph,
        CisGraphManifest manifest,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (graph.SchemaVersion != GraphBuilder.GraphSchemaVersion
            || manifest.SchemaVersion != GraphBuilder.GraphSchemaVersion)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-SCHEMA-001",
                "error",
                $"Unsupported graph schema: graph={graph.SchemaVersion}, manifest={manifest.SchemaVersion}.",
                GraphRelativePath,
                ManifestRelativePath));
        }

        if (!string.Equals(graph.Build.Id, manifest.BuildId, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-BUILD-001",
                "error",
                "Graph and manifest build IDs do not match.",
                graph.Build.Id,
                manifest.BuildId));
        }

        if (manifest.Extractors.SequenceEqual(GraphBuilder.CurrentExtractors, StringComparer.Ordinal)
            && !string.Equals(graph.Build.Id, GraphBuilder.CreateBuildId(manifest.Inputs), StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-BUILD-002",
                "error",
                "Build ID is not content-addressed to the manifest inputs and current extractor contract.",
                graph.Build.Id));
        }

        if (!string.Equals(graph.Build.RepositoryId, context.RepositoryId, StringComparison.Ordinal)
            || !string.Equals(manifest.RepositoryId, context.RepositoryId, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-REPO-002",
                "error",
                "Graph repository identity does not match `.cis/repository.yml`.",
                context.RepositoryId,
                graph.Build.RepositoryId,
                manifest.RepositoryId));
        }

        if (!manifest.Extractors.SequenceEqual(GraphBuilder.CurrentExtractors, StringComparer.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-STALE-001",
                "warning",
                "Graph extractor versions differ from the current CLI. Run `cis graph build`.",
                string.Join(", ", manifest.Extractors)));
        }
    }

    private static IReadOnlyDictionary<string, CisGraphNode> ValidateNodes(
        CisRepositoryContext context,
        IReadOnlyList<CisGraphNode> nodes,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var nodesByKey = new Dictionary<string, CisGraphNode>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var missingComponentIdentity = new List<string>();
        foreach (var node in nodes)
        {
            var identity = $"{node.RepositoryId}\u001f{node.Kind}\u001f{node.LocalId}";
            if (!identities.Add(identity) || !nodesByKey.TryAdd(node.Key, node))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-ID-001",
                    "error",
                    $"Duplicate graph node identity: {node.Key}",
                    node.Key));
                continue;
            }

            var expectedKey = $"{node.RepositoryId}::{node.Kind}::{node.LocalId}";
            if (!string.Equals(node.Key, expectedKey, StringComparison.Ordinal)
                || node.RepositoryId.Contains("::", StringComparison.Ordinal)
                || node.LocalId.Contains("::", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-ID-002",
                    "error",
                    $"Node key does not match its structured identity: {node.Key}",
                    expectedKey));
            }

            if (!string.Equals(node.RepositoryId, context.RepositoryId, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-REPO-003",
                    "error",
                    $"Node belongs to a different repository: {node.Key}",
                    node.RepositoryId,
                    context.RepositoryId));
            }

            if (!NodeKinds.Contains(node.Kind))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-KIND-001",
                    "error",
                    $"Unsupported node kind '{node.Kind}': {node.Key}",
                    node.Key));
            }

            if (!Authorities.Contains(node.Authority))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-AUTHORITY-001",
                    "error",
                    $"Unsupported node authority '{node.Authority}': {node.Key}",
                    node.Key));
            }

            if (!Lifecycles.Contains(node.Lifecycle))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-LIFECYCLE-001",
                    "warning",
                    $"Unregistered node lifecycle '{node.Lifecycle}': {node.Key}",
                    node.Key));
            }

            ValidateFacets(node, diagnostics);
            ValidateLocations(context.RepositoryPath, node, diagnostics);
            ValidateProvenance(context.RepositoryPath, node.Key, node.Provenance, diagnostics);
            ValidateSensitiveProperties(node, diagnostics);
            ValidateKindIdentity(node, missingComponentIdentity, diagnostics);
        }

        if (missingComponentIdentity.Count > 0)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-IDENTITY-001",
                "warning",
                $"{missingComponentIdentity.Count} symbol/test nodes lack a confirmed component identity.",
                missingComponentIdentity.Take(20).ToArray()));
        }

        return nodesByKey;
    }

    private static void ValidateFacets(
        CisGraphNode node,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (node.Facets.Distinct(StringComparer.Ordinal).Count() != node.Facets.Count)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-FACET-001",
                "error",
                $"Node contains duplicate facets: {node.Key}",
                node.Key));
        }

        foreach (var facet in node.Facets.Where(facet => !TokenPattern().IsMatch(facet)))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-FACET-002",
                "warning",
                $"Facet is not a normalized registered token: {facet}",
                node.Key));
        }
    }

    private static void ValidateLocations(
        string repositoryPath,
        CisGraphNode node,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (node.Locations.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-LOCATION-001",
                "error",
                $"Node has no source location: {node.Key}",
                node.Key));
            return;
        }

        foreach (var location in node.Locations)
        {
            ValidateRepositoryPath(
                repositoryPath,
                location.Path,
                node.Key,
                "CIS-GRAPH-VALIDATE-LOCATION-002",
                diagnostics);
        }
    }

    private static void ValidateProvenance(
        string repositoryPath,
        string ownerKey,
        IReadOnlyList<CisGraphEvidence> provenance,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (provenance.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-EVIDENCE-001",
                "error",
                $"Graph fact has no provenance: {ownerKey}",
                ownerKey));
            return;
        }

        foreach (var evidence in provenance)
        {
            ValidateRepositoryPath(
                repositoryPath,
                evidence.Path,
                ownerKey,
                "CIS-GRAPH-VALIDATE-EVIDENCE-002",
                diagnostics);
            if (!Confidences.Contains(evidence.Confidence))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-CONFIDENCE-001",
                    "error",
                    $"Unsupported evidence confidence '{evidence.Confidence}': {ownerKey}",
                    evidence.Path));
            }

            if (string.IsNullOrWhiteSpace(evidence.Method)
                || string.IsNullOrWhiteSpace(evidence.Extractor))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-EVIDENCE-003",
                    "error",
                    $"Evidence is missing method or extractor identity: {ownerKey}",
                    evidence.Path));
            }

            if (evidence.ContentHash is not null && !Sha256Pattern().IsMatch(evidence.ContentHash))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-EVIDENCE-004",
                    "error",
                    $"Evidence content hash is malformed: {ownerKey}",
                    evidence.ContentHash));
            }
        }
    }

    private static void ValidateSensitiveProperties(
        CisGraphNode node,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        foreach (var property in node.Properties)
        {
            if (!SensitiveNamePattern().IsMatch(property.Key)
                || property.Key.Contains("sensitivity", StringComparison.OrdinalIgnoreCase)
                || property.Key.Contains("secret?", StringComparison.OrdinalIgnoreCase)
                || IsSafeSensitivePlaceholder(property.Value))
            {
                continue;
            }

            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-SENSITIVE-001",
                "warning",
                $"Potential sensitive value is stored in graph properties: {property.Key}",
                node.Key));
        }
    }

    private static void ValidateKindIdentity(
        CisGraphNode node,
        ICollection<string> missingComponentIdentity,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (node.Kind == "reference-item"
            && (!node.LocalId.StartsWith(node.Subtype + "/", StringComparison.Ordinal)
                || !node.Properties.TryGetValue("graphIdentity", out var identity)
                || string.IsNullOrWhiteSpace(identity)))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-REFERENCE-001",
                "error",
                $"Reference item lacks its governed stable identity: {node.Key}",
                node.Key));
        }

        if (node.Kind == "source-file"
            && node.Locations.Count > 0
            && !string.Equals(node.LocalId, node.Locations[0].Path, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-SOURCE-001",
                "error",
                $"Source-file identity does not match its repository path: {node.Key}",
                node.LocalId,
                node.Locations[0].Path));
        }

        if (node.Kind is "symbol" or "test"
            && (!node.Properties.TryGetValue("component", out var component)
                || string.IsNullOrWhiteSpace(component)))
        {
            missingComponentIdentity.Add(node.Key);
        }
    }

    private static void ValidateEdges(
        string repositoryPath,
        IReadOnlyList<CisGraphEdge> edges,
        IReadOnlyDictionary<string, CisGraphNode> nodes,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!keys.Add(edge.Key))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-EDGE-001",
                    "error",
                    $"Duplicate edge key: {edge.Key}",
                    edge.Key));
            }

            if (!EdgeTypes.Contains(edge.Type))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-EDGE-002",
                    "error",
                    $"Unsupported edge type '{edge.Type}': {edge.Key}",
                    edge.Key));
            }

            if (!EdgeStates.Contains(edge.State))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-EDGE-003",
                    "error",
                    $"Unsupported edge state '{edge.State}': {edge.Key}",
                    edge.Key));
            }

            if (!Confidences.Contains(edge.Confidence))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-CONFIDENCE-002",
                    "error",
                    $"Unsupported edge confidence '{edge.Confidence}': {edge.Key}",
                    edge.Key));
            }

            var hasFrom = nodes.TryGetValue(edge.From, out var from);
            var hasTo = nodes.TryGetValue(edge.To, out var to);
            if (!hasFrom || !hasTo)
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-ENDPOINT-001",
                    "error",
                    $"Edge has a missing endpoint: {edge.Key}",
                    edge.From,
                    edge.To));
            }
            else if (!ValidEndpointKinds(edge.Type, from!.Kind, to!.Kind))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-ENDPOINT-002",
                    "error",
                    $"Edge type '{edge.Type}' does not allow {from.Kind} -> {to.Kind}.",
                    edge.Key));
            }

            ValidateProvenance(repositoryPath, edge.Key, edge.Observations, diagnostics);
            ValidateRelationshipDisposition(edge, diagnostics);
        }
    }

    private static bool ValidEndpointKinds(string type, string from, string to)
        => type switch
        {
            "contains" => from switch
            {
                "repository" => to is "component" or "document" or "source-file" or "workflow" or "external-work-item",
                "document" => to is "document-section" or "requirement" or "decision" or "reference-item",
                "source-file" => to is "symbol" or "test",
                _ => false,
            },
            "declares" => from == "document" && to is "requirement" or "decision" or "reference-item" or "api-rule",
            "describes" => from is "document" or "document-section"
                && to is "component" or "reference-item" or "workflow" or "symbol",
            "owns" => from == "component" && to is "reference-item" or "symbol" or "workflow",
            "belongs-to" => from is "source-file" or "symbol" or "test" or "workflow" && to == "component",
            "implemented-by" => from is "requirement" or "reference-item"
                && to is "component" or "symbol" or "source-file",
            "depends-on" => from is "component" or "symbol" or "workflow" or "dependency"
                && to is "component" or "dependency",
            "calls" => from is "symbol" or "test" && to == "symbol",
            "annotated-by" or "implements" or "inherits" or "delegates" or "binds-options" or "reads-configuration"
                or "logging-call" or "structured-log" or "unstructured-log" or "persistence-call"
                or "transaction-call" or "outbox-write" or "event-publish" or "idempotency-check"
                => from == "symbol" && to == "symbol",
            "invokes" => from == "symbol" && to == "invocation",
            "targets" => from == "invocation" && to == "symbol",
            "precedes" => from == "invocation" && to == "invocation",
            "has-dataflow" => from == "symbol" && to == "dataflow",
            "authorized-by" => from is "symbol" or "reference-item" && to is "symbol" or "reference-item",
            "verified-by" => from is "requirement" or "reference-item" or "component" or "symbol" or "workflow"
                && to == "test",
            "governed-by" => to is "document" or "decision" or "api-rule",
            "publishes" or "subscribes-to" => from is "component" or "symbol" && to == "reference-item",
            "configured-by" => from is "component" or "workflow" && to == "reference-item",
            "deployed-by" => from == "component" && to == "workflow",
            "mirrored-by" => from == "document" && to == "external-work-item",
            "has-conflict" => from is "repository" or "document" or "external-work-item" && to == "tracker-conflict",
            _ => true,
        };

    private static void ValidateRelationshipDisposition(
        CisGraphEdge edge,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (edge.State == "proposed"
            && !edge.Observations.Any(observation => observation.Method.Contains(
                "propos",
                StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-PROPOSAL-001",
                "error",
                $"Proposed edge lacks durable proposal provenance: {edge.Key}",
                edge.Key));
        }

        if (edge.State == "confirmed"
            && !edge.Observations.Any(observation => observation.Method.Contains(
                "confirm",
                StringComparison.OrdinalIgnoreCase)
                || observation.Method.Contains("review", StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-CONFIRMATION-001",
                "error",
                $"Confirmed edge lacks durable review provenance: {edge.Key}",
                edge.Key));
        }
    }

    private static void ValidateManifest(
        string repositoryPath,
        CisGraphManifest manifest,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in manifest.Inputs)
        {
            if (!paths.Add(input.Path))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-MANIFEST-001",
                    "error",
                    $"Manifest contains a duplicate input: {input.Path}",
                    input.Path));
            }

            if (!TryResolveRepositoryPath(repositoryPath, input.Path, out var absolutePath))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-MANIFEST-002",
                    "error",
                    $"Manifest input escapes the repository: {input.Path}",
                    input.Path));
                continue;
            }

            if (!File.Exists(absolutePath))
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-STALE-002",
                    "warning",
                    $"Graph input is missing: {input.Path}",
                    input.Path));
                continue;
            }

            try
            {
                if (!string.Equals(GraphBuilder.HashInput(input.Path, absolutePath), input.Hash, StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "CIS-GRAPH-VALIDATE-STALE-003",
                        "warning",
                        $"Graph input changed after the build: {input.Path}",
                        input.Path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic(
                    "CIS-GRAPH-VALIDATE-STALE-004",
                    "warning",
                    $"Graph input cannot be read: {input.Path}",
                    exception.Message));
            }
        }

        try
        {
            foreach (var path in ImplementationGraphExtractor.EnumerateInputPaths(repositoryPath))
            {
                if (!paths.Contains(path))
                {
                    diagnostics.Add(Diagnostic(
                        "CIS-GRAPH-VALIDATE-STALE-005",
                        "warning",
                        $"New implementation input is absent from the graph manifest: {path}",
                        path));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic(
                "CIS-GRAPH-VALIDATE-STALE-006",
                "warning",
                "Unable to enumerate current implementation inputs.",
                exception.Message));
        }
    }

    private static void ValidateGitTracking(
        string repositoryPath,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        var tracked = RunGit(repositoryPath, "ls-files", ".cis/local");
        if (string.IsNullOrWhiteSpace(tracked))
        {
            return;
        }

        diagnostics.Add(Diagnostic(
            "CIS-GRAPH-VALIDATE-GIT-001",
            "error",
            "Derived `.cis/local/` graph artifacts are tracked by Git.",
            tracked.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
    }

    private static void ValidateRepositoryPath(
        string repositoryPath,
        string path,
        string ownerKey,
        string code,
        ICollection<CisGraphDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return;
        }

        if (!TryResolveRepositoryPath(repositoryPath, path, out var absolutePath))
        {
            diagnostics.Add(Diagnostic(
                code,
                "error",
                $"Graph locator escapes the repository: {path}",
                ownerKey));
        }
        else if (!File.Exists(absolutePath))
        {
            diagnostics.Add(Diagnostic(
                code,
                "error",
                $"Graph locator does not resolve to a file: {path}",
                ownerKey));
        }
    }

    private static bool TryResolveRepositoryPath(
        string repositoryPath,
        string path,
        out string absolutePath)
    {
        absolutePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            var repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            absolutePath = Path.GetFullPath(Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar)));
            return absolutePath.StartsWith(
                repository + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSafeSensitivePlaceholder(string value)
        => string.IsNullOrWhiteSpace(value)
            || value.Trim().ToLowerInvariant() is "unknown" or "none" or "no" or "false" or "n/a" or "not secret"
            || value.Contains("TODO", StringComparison.OrdinalIgnoreCase)
            || value.Contains("environment", StringComparison.OrdinalIgnoreCase)
            || value.Contains("secret store", StringComparison.OrdinalIgnoreCase);

    private static string? RunGit(string repositoryPath, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = repositoryPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(2000) && process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (exception is Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GraphValidationResult Unavailable(
        string repositoryPath,
        bool strict,
        string message,
        string evidence)
        => new(
            "graph-unavailable",
            repositoryPath,
            GraphRelativePath,
            null,
            "unknown",
            0,
            0,
            [Diagnostic("CIS-GRAPH-VALIDATE-LOAD-001", "error", message, evidence)],
            strict,
            RepositoryConfigurationValid: true,
            GraphAvailable: false);

    private static string AbsolutePath(string repositoryPath, string relativePath)
        => Path.Combine(repositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string DiagnosticKey(CisGraphDiagnostic diagnostic)
        => string.Join(
            '\u001f',
            new[] { diagnostic.Code, diagnostic.Severity, diagnostic.Message }
                .Concat(diagnostic.Evidence));

    private static int SeverityOrder(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2,
    };

    private static CisGraphDiagnostic Diagnostic(
        string code,
        string severity,
        string message,
        params string[] evidence)
        => new(code, severity, message, evidence);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^sha256:[a-f0-9]{64}$")]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("(?:password|secret|token|api[-_ ]?key|connection[-_ ]?string)", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveNamePattern();
}
