using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.References;

public sealed class SourceEvidenceProjectionService : ICisSourceEvidenceRegistrar, ICisBrdSourceEvidenceProvider
{
    private const long MaximumPackageBytes = 32 * 1024 * 1024;
    private const long MaximumXmlBytes = 16 * 1024 * 1024;
    private const string RegistryFileName = "source-evidence.md";
    private static readonly string[] AllowedAssessments = ["Unreviewed", "Reference", "Adopted", "Rejected"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;
    private readonly ICisGraphSnapshotReader? _graphReader;

    public SourceEvidenceProjectionService(ICisRepositoryContextResolver resolver, DocumentationCatalogMerger catalogMerger,
        ICisWorkspaceRegistry? workspaceRegistry = null, ICisGraphSnapshotReader? graphReader = null,
        Func<DateTimeOffset>? clock = null)
    {
        _resolver = resolver;
        _catalogMerger = catalogMerger;
        _workspaceRegistry = workspaceRegistry;
        _graphReader = graphReader;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public CisSourceEvidenceRegistration Register(CisSourceEvidenceRegistrationRequest request)
    {
        var resolution = _resolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return RegistrationError(resolution.Errors.Select(item => "ERROR: " + item));
        var context = resolution.Context;
        var diagnostics = new List<string>();
        if (!AllowedAssessments.Contains(request.Assessment, StringComparer.Ordinal))
            diagnostics.Add("ERROR: Assessment must be Unreviewed, Reference, Adopted, or Rejected.");
        if (string.IsNullOrWhiteSpace(request.Actor)) diagnostics.Add("ERROR: Actor is required.");
        if (string.IsNullOrWhiteSpace(request.Rationale)) diagnostics.Add("ERROR: Rationale is required.");
        string? absolute; string? relative; string format; string digest;
        if (TryResolveDirectory(request.SourcePath, context.RepositoryPath, out var directory))
        {
            if (!TryResolveRepositorySource(context, directory!, out relative, out var graph, diagnostics))
                return RegistrationError(diagnostics, context);
            absolute = directory;
            format = "repository";
            digest = graph!.Build.Id;
        }
        else
        {
            if (!TryResolveSource(context, request.SourcePath, out absolute, out relative, diagnostics))
                return RegistrationError(diagnostics, context);
            format = Format(absolute!);
            digest = ShaFile(absolute!);
        }
        if (diagnostics.Count > 0) return RegistrationError(diagnostics, context);
        var id = StableId(context.RepositoryId, relative!);
        var entries = ReadRegistry(context, diagnostics).ToList();
        var existing = entries.FindIndex(item => item.Id == id);
        if (existing >= 0)
        {
            var row = entries[existing];
            // A changed binary is projected for review, but its accepted baseline is never advanced here.
            entries[existing] = row with
            {
                Assessment = request.Assessment,
                Actor = request.Actor.Trim(),
                Rationale = request.Rationale.Trim(),
            };
        }
        else
        {
            entries.Add(new SourceEvidenceEntry(id, relative!, format, digest,
                request.Assessment, request.Actor.Trim(), UtcNow(), request.Rationale.Trim()));
        }

        if (!EnsureCatalog(context, diagnostics)) return RegistrationError(diagnostics, context);
        WriteRegistry(context, entries);
        var built = Build(context.RepositoryPath, id);
        var state = built.Sources.FirstOrDefault();
        diagnostics.AddRange(built.Diagnostics);
        return new CisSourceEvidenceRegistration(
            diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? "invalid" : state?.Status ?? "registered",
            id,
            relative,
            entries.Single(item => item.Id == id).RegisteredDigest,
            state?.DetectedDigest,
            Relative(context, RegistryPath(context)),
            state?.ProjectionPath,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }

    public SourceEvidenceResult Import(string repositoryPath, string sourcePath, string assessment, string actor, string rationale)
    {
        var registration = Register(new(repositoryPath, sourcePath, assessment, actor, rationale));
        if (registration.SourceId is null || registration.ExitCode != 0)
            return new SourceEvidenceResult(registration.Status, TryRepository(repositoryPath), registration.RegistryPath,
                [], registration.Diagnostics, false);

        // Register builds the projection, but the public registration contract deliberately stays small.
        // Re-read the derived evidence so import reports source drift and BRD materiality truthfully.
        var status = Status(repositoryPath, registration.SourceId);
        return status with
        {
            Status = registration.Status,
            RegistryPath = registration.RegistryPath,
            Diagnostics = registration.Diagnostics.Concat(status.Diagnostics).Distinct(StringComparer.Ordinal).ToArray(),
            Applied = true,
        };
    }

    public SourceEvidenceResult Build(string repositoryPath, string? sourceId = null)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return ErrorResult(repositoryPath, resolution.Errors);
        var context = resolution.Context;
        var diagnostics = new List<string>();
        var entries = ReadRegistry(context, diagnostics)
            .Where(item => sourceId is null || item.Id.Equals(sourceId, StringComparison.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (sourceId is not null && entries.Length == 0)
            diagnostics.Add($"ERROR: Source evidence '{sourceId}' is not registered.");
        var states = new List<SourceEvidenceState>();
        foreach (var entry in entries)
            states.Add(BuildOne(context, entry, diagnostics));
        var invalid = diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal));
        return new SourceEvidenceResult(invalid ? "invalid" : states.Any(item => item.Status == "changed") ? "changed" : "current",
            context.RepositoryPath, Relative(context, RegistryPath(context)), states,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(), !invalid);
    }

    public SourceEvidenceResult Status(string repositoryPath, string? sourceId = null)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return ErrorResult(repositoryPath, resolution.Errors);
        var context = resolution.Context;
        var diagnostics = new List<string>();
        var entries = ReadRegistry(context, diagnostics)
            .Where(item => sourceId is null || item.Id.Equals(sourceId, StringComparison.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (sourceId is not null && entries.Length == 0)
            diagnostics.Add($"ERROR: Source evidence '{sourceId}' is not registered.");
        var states = entries.Select(entry => InspectOne(context, entry, diagnostics)).ToArray();
        var invalid = diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal));
        return new SourceEvidenceResult(invalid ? "invalid" : states.Any(item => item.Status == "changed") ? "changed" : "current",
            context.RepositoryPath, Relative(context, RegistryPath(context)), states,
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(), false);
    }

    public SourceEvidenceResult Accept(string repositoryPath, string sourceId, string actor, string reason, bool yes)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return ErrorResult(repositoryPath, resolution.Errors);
        var context = resolution.Context;
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(actor)) diagnostics.Add("ERROR: Actor is required.");
        if (string.IsNullOrWhiteSpace(reason)) diagnostics.Add("ERROR: Reason is required.");
        var entries = ReadRegistry(context, diagnostics).ToList();
        var index = entries.FindIndex(item => item.Id == sourceId);
        if (index < 0) diagnostics.Add($"ERROR: Source evidence '{sourceId}' is not registered.");
        if (diagnostics.Count > 0) return new SourceEvidenceResult("invalid", context.RepositoryPath,
            Relative(context, RegistryPath(context)), [], diagnostics, false);
        var current = InspectOne(context, entries[index], diagnostics);
        if (current.DetectedDigest is null) return new SourceEvidenceResult("invalid", context.RepositoryPath,
            Relative(context, RegistryPath(context)), [current], diagnostics, false);
        if (!yes) return new SourceEvidenceResult("confirmation-required", context.RepositoryPath,
            Relative(context, RegistryPath(context)), [current],
            [$"Accept detected digest {current.DetectedDigest} for {sourceId}: {reason.Trim()}"], false, true);
        entries[index] = entries[index] with
        {
            RegisteredDigest = current.DetectedDigest,
            Actor = actor.Trim(),
            RegisteredAtUtc = UtcNow(),
            Rationale = reason.Trim(),
        };
        if (!EnsureCatalog(context, diagnostics))
            return new SourceEvidenceResult("collision", context.RepositoryPath, Relative(context, RegistryPath(context)), [current], diagnostics, false);
        WriteRegistry(context, entries);
        var accepted = current with { RegisteredDigest = current.DetectedDigest, Status = "current", MaterialToBrd = false };
        return new SourceEvidenceResult("accepted", context.RepositoryPath, Relative(context, RegistryPath(context)),
            [accepted], diagnostics, true);
    }

    public IReadOnlyList<CisBrdSourceEvidence> Discover(CisRepositoryContext context)
    {
        var diagnostics = new List<string>();
        return ReadRegistry(context, diagnostics).Select(item =>
        {
            var state = InspectOne(context, item, diagnostics);
            return new CisBrdSourceEvidence(
                item.Id, "authoring-reference", context.RepositoryId, context.RepositoryPath,
                item.SourcePath, item.RegisteredDigest, state.DetectedDigest, item.Assessment, item.Rationale,
                state.MaterialToBrd,
                state.MaterialToBrd
                    ? ["registered-source-evidence", item.Format + "-projection", "material-source-drift"]
                    : ["registered-source-evidence", item.Format + "-projection"]);
        }).ToArray();
    }

    private SourceEvidenceState BuildOne(CisRepositoryContext context, SourceEvidenceEntry entry, List<string> diagnostics)
    {
        string currentDigest;
        Projection projection;
        if (entry.Format == "repository")
        {
            if (!TryResolveRegisteredRepository(context, entry, out var graph, diagnostics))
                return State(entry, null, "missing", true, null, diagnostics);
            currentDigest = graph!.Build.Id;
            projection = ExtractRepository(graph, entry);
        }
        else
        {
            if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, entry.SourcePath, out var source)
                || !File.Exists(source) || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, source))
            {
                diagnostics.Add($"ERROR: Registered source is missing or unsafe: {entry.Id} {entry.SourcePath}");
                return State(entry, null, "missing", true, null, diagnostics);
            }
            currentDigest = ShaFile(source);
            try { projection = Extract(source, entry); }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or XmlException)
            {
                diagnostics.Add($"ERROR: Could not project {entry.Id}: {exception.Message}");
                return State(entry, currentDigest, "invalid", true, null, diagnostics);
            }
        }
        var projectionDirectory = ProjectionDirectory(context, entry.Id);
        Directory.CreateDirectory(projectionDirectory);
        var mapPath = Path.Combine(projectionDirectory, "source-map.json");
        var manifestPath = Path.Combine(projectionDirectory, "manifest.json");
        var previous = ReadAnchors(mapPath);
        var previousDigest = ReadManifestDigest(manifestPath);
        var currentByAnchor = projection.Anchors.ToDictionary(item => item.Anchor, StringComparer.Ordinal);
        var added = currentByAnchor.Keys.Except(previous.Keys, StringComparer.Ordinal).Order().ToArray();
        var removed = previous.Keys.Except(currentByAnchor.Keys, StringComparer.Ordinal).Order().ToArray();
        var changed = currentByAnchor.Keys.Intersect(previous.Keys, StringComparer.Ordinal)
            .Where(anchor => previous[anchor].TextDigest != currentByAnchor[anchor].TextDigest).Order().ToArray();
        var citations = ReadCitations(context, entry);
        var broadCitation = citations.Contains(string.Empty, StringComparer.Ordinal);
        var material = previous.Count > 0 && currentDigest != previousDigest && (broadCitation && (added.Length + removed.Length + changed.Length > 0)
            || citations.Where(item => item.Length > 0).Intersect(changed.Concat(removed), StringComparer.Ordinal).Any());
        var diff = new SourceEvidenceDiff(entry.Id, previousDigest, currentDigest, added, changed, removed,
            citations.Where(item => item.Length > 0).Order().ToArray(), material);
        WriteAtomic(Path.Combine(projectionDirectory, "content.md"), projection.Markdown);
        WriteJson(mapPath, projection.Anchors);
        WriteAtomic(Path.Combine(projectionDirectory, "index-card.md"), RenderIndexCard(entry, projection, currentDigest));
        WriteJson(Path.Combine(projectionDirectory, "diff.json"), diff);
        WriteJson(manifestPath, new
        {
            schemaVersion = 1,
            sourceId = entry.Id,
            sourcePath = entry.SourcePath,
            format = entry.Format,
            registeredDigest = entry.RegisteredDigest,
            detectedDigest = currentDigest,
            previousDigest,
            status = currentDigest == entry.RegisteredDigest ? "current" : "changed",
            materialToBrd = material,
            builtAtUtc = UtcNow(),
            extractor = "cis-source-evidence/2",
            contentDigest = ShaText(projection.Markdown),
            sourceMapDigest = ShaFile(mapPath),
        });
        return State(entry, currentDigest, currentDigest == entry.RegisteredDigest ? "current" : "changed",
            material && currentDigest != entry.RegisteredDigest, Relative(context, projectionDirectory), []);
    }

    private SourceEvidenceState InspectOne(CisRepositoryContext context, SourceEvidenceEntry entry, List<string> diagnostics)
    {
        string detected;
        if (entry.Format == "repository")
        {
            if (!TryResolveRegisteredRepositoryPath(context, entry, out var repositoryPath, diagnostics))
                return State(entry, null, "missing", true, null, diagnostics);
            var snapshot = _graphReader!.ReadMetadata(repositoryPath!);
            if (snapshot.Build is null || snapshot.ExitCode != 0 || snapshot.Freshness != "fresh")
            {
                diagnostics.Add($"ERROR: Repository evidence requires a fresh graph for '{entry.SourcePath["workspace:".Length..]}'. Run `cis graph build --repo {repositoryPath}`.");
                return State(entry, null, "missing", true, null, diagnostics);
            }
            detected = snapshot.Build.Id;
        }
        else
        {
            if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, entry.SourcePath, out var source)
                || !File.Exists(source) || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, source))
            {
                diagnostics.Add($"ERROR: Registered source is missing or unsafe: {entry.Id} {entry.SourcePath}");
                return State(entry, null, "missing", true, null, diagnostics);
            }
            detected = ShaFile(source);
        }
        var projection = ProjectionDirectory(context, entry.Id);
        var diffPath = Path.Combine(projection, "diff.json");
        var manifestPath = Path.Combine(projection, "manifest.json");
        var material = false;
        if (detected != entry.RegisteredDigest)
        {
            if (!File.Exists(manifestPath) || ReadManifestDigest(manifestPath) != detected)
                diagnostics.Add($"WARNING: Projection for {entry.Id} is stale. Run `cis references source build --id {entry.Id}`.");
            else if (File.Exists(diffPath))
            {
                try { material = JsonSerializer.Deserialize<SourceEvidenceDiff>(File.ReadAllText(diffPath), JsonOptions)?.MaterialToBrd ?? true; }
                catch (JsonException) { material = true; }
            }
        }
        return State(entry, detected, detected == entry.RegisteredDigest ? "current" : "changed", material,
            Directory.Exists(projection) ? Relative(context, projection) : null, []);
    }

    private static Projection Extract(string path, SourceEvidenceEntry entry)
        => Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)
            ? ExtractDocx(path, entry)
            : ExtractText(path, entry);

    private static Projection ExtractText(string path, SourceEvidenceEntry entry)
    {
        var content = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (content.IndexOf('\0') >= 0) throw new InvalidDataException("Binary reference files are not supported.");
        var anchors = new List<SourceEvidenceAnchor>();
        var output = Header(entry);
        var heading = "document"; var ordinal = 0; var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n'))
        {
            var match = Regex.Match(line, "^(#{1,6})\\s+(.+)$", RegexOptions.CultureInvariant);
            if (match.Success)
            {
                heading = Unique(Slug(match.Groups[2].Value), used); output.AppendLine($"<a id=\"{heading}\"></a>"); output.AppendLine(line); output.AppendLine(); continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { output.AppendLine(); continue; }
            ordinal++; var anchor = Unique($"{heading}--p-{ordinal:0000}", used);
            anchors.Add(new(anchor, "text", null, ordinal, heading, ShaText(line.Trim()), line.Trim()));
            output.AppendLine($"<a id=\"{anchor}\"></a>"); output.AppendLine(line); output.AppendLine();
        }
        return new(output.ToString(), anchors);
    }

    private static Projection ExtractRepository(CisGraphDocument graph, SourceEvidenceEntry entry)
    {
        var output = Header(entry); var anchors = new List<SourceEvidenceAnchor>();
        output.AppendLine("## Repository snapshot"); output.AppendLine();
        output.AppendLine($"- Repository: `{graph.Build.RepositoryId}`");
        output.AppendLine($"- Graph build: `{graph.Build.Id}`");
        output.AppendLine($"- Revision: `{graph.Build.Head ?? "working-tree"}`");
        output.AppendLine($"- Dirty at build: {graph.Build.Dirty.ToString().ToLowerInvariant()}");
        output.AppendLine($"- Nodes: {graph.Nodes.Count}; edges: {graph.Edges.Count}"); output.AppendLine();
        output.AppendLine("## Evidence inventory"); output.AppendLine();
        output.AppendLine("| Kind | Count |"); output.AppendLine("| --- | ---: |");
        foreach (var group in graph.Nodes.GroupBy(item => item.Kind, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            output.AppendLine($"| {group.Key} | {group.Count()} |");
        output.AppendLine(); output.AppendLine("## Components, contracts, behavior, and source routes"); output.AppendLine();
        output.AppendLine("Dictionary details are evidence at their recorded lifecycle, not approved business intent. Unknown fields are omitted. Declared states do not prove allowed transitions; declared guards do not prove effective authorization.");
        output.AppendLine("NestJS paths are controller-relative declarations; global prefixes/versioning and effective policy require review. Configuration, package and trace-link rows are counted but omitted from business evidence.");
        var selected = graph.Nodes.Where(item => item.Kind is "repository" or "component").OrderBy(item => item.Key, StringComparer.Ordinal)
            .Concat(FairRepositoryEvidence(graph.Nodes.Where(item => (item.Kind is "reference-item" or "contract" or "endpoint" or "entity" or "event" or "command" or "route")
                && item.Subtype is not ("configuration-key" or "package" or "trace-link"))))
            .Concat(graph.Nodes.Where(item => item.Kind is not ("repository" or "component" or "reference-item" or "contract" or "endpoint" or "entity" or "event" or "command" or "route" or "dependency"))
                .OrderBy(item => NodePriority(item.Kind)).ThenBy(item => item.Key, StringComparer.Ordinal).Take(150))
            .Take(2000).ToArray();
        // A source path shared by hundreds of fields is printed once, with short locators in rows.
        var sourcePaths = selected.Where(item => item.Kind == "reference-item").SelectMany(item => item.Properties.Values)
            .SelectMany(value => Regex.Matches(value, @"[A-Za-z0-9_./-]+\.(?:tsx|jsx|cs|ts|js|swift|kt)\b").Select(match => match.Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(500)
            .Select((path, index) => (Path: path, Id: "src" + (index + 1))).ToDictionary(item => item.Path, item => item.Id, StringComparer.Ordinal);
        output.AppendLine("\n### Source location key\n");
        foreach (var sourcePath in sourcePaths) output.AppendLine($"- {sourcePath.Value}: `{sourcePath.Key}`");
        output.AppendLine("\n### Cited declarations\n");
        var replacements = sourcePaths.OrderByDescending(item => item.Key.Length).ToArray();
        var ordinal = 0;
        var included = new List<CisGraphNode>();
        var bytes = Encoding.UTF8.GetByteCount(output.ToString());
        foreach (var node in selected)
        {
            var anchor = "node-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.Key)))[..16].ToLowerInvariant();
            var locations = string.Join(", ", node.Locations.Take(5).Select(item => item.Path));
            var details = RepositoryDetails(node);
            var text = $"{node.Kind}/{node.Subtype}: {node.Label}" + (locations.Length == 0 ? string.Empty : $" — {locations}") + details;
            var renderedDetails = details;
            foreach (var sourcePath in replacements)
                renderedDetails = renderedDetails.Replace(sourcePath.Key, sourcePath.Value, StringComparison.Ordinal);
            var rendered = $"<a id=\"{anchor}\"></a>\n- **{node.Label}** — `{node.Subtype}`{(node.Kind == "reference-item" || locations.Length == 0 ? string.Empty : $" — `{locations}`")}{renderedDetails}\n";
            var size = Encoding.UTF8.GetByteCount(rendered);
            if (bytes + size > 460 * 1024) continue;
            bytes += size; ordinal++; included.Add(node);
            anchors.Add(new(anchor, "graph", node.Key, ordinal, "repository-evidence", ShaText(text), text));
            output.Append(rendered);
        }
        output.AppendLine("\n## Dictionary coverage\n\n| Dictionary kind | Included | Available |\n| --- | ---: | ---: |");
        foreach (var group in graph.Nodes.Where(item => item.Kind == "reference-item").GroupBy(item => item.Subtype).OrderBy(item => item.Key, StringComparer.Ordinal))
            output.AppendLine($"| {group.Key} | {included.Count(item => item.Kind == "reference-item" && item.Subtype == group.Key)} | {group.Count()} |");
        if (graph.Nodes.Count > included.Count)
            output.AppendLine($"\n> Projection bounded to {included.Count} of {graph.Nodes.Count} graph nodes (2,000 nodes / 460 KiB evidence budget). Omitted evidence is not evidence of absence. Use CIS graph/context queries and the original dictionaries for deeper evidence.");
        return new(output.ToString(), anchors);
    }

    private static IEnumerable<CisGraphNode> FairRepositoryEvidence(IEnumerable<CisGraphNode> nodes)
    {
        // Alternate families and entities so large data/API inventories cannot hide workflows or permissions.
        var groups = nodes.GroupBy(item => item.Kind + "/" + item.Subtype).OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => new Queue<CisGraphNode>(RoundRobin(group.GroupBy(item =>
                item.Properties.GetValueOrDefault("Entity") ?? item.Properties.GetValueOrDefault("Workflow") ?? item.Key)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(items => new Queue<CisGraphNode>(items.OrderBy(item => item.Key, StringComparer.Ordinal)))))).ToArray();
        return RoundRobin(groups);
    }

    private static IEnumerable<CisGraphNode> RoundRobin(IEnumerable<Queue<CisGraphNode>> source)
    {
        var queues = source.ToArray();
        while (queues.Any(queue => queue.Count > 0))
            foreach (var queue in queues)
                if (queue.TryDequeue(out var node)) yield return node;
    }

    private static string RepositoryDetails(CisGraphNode node)
    {
        if (node.Kind is not ("reference-item" or "contract" or "endpoint" or "entity" or "event" or "command" or "route")) return string.Empty;
        // Exclude arbitrary graph properties, configuration values and raw source bodies.
        string[] columns = ["Request contract", "Response contract", "Permission / auth", "Notes",
            "Type", "Required", "Constraints", "Relationship", "Target", "Cardinality",
            "Meaning", "Allowed transitions", "Triggering commands", "Blocking rules", "Terminal",
            "Applies to", "Default role mappings", "Access / permission", "Destination",
            "Command name", "Actor / trigger", "Preconditions", "Result", "Emits events", "Event name", "Payload summary",
            "Rule", "Enforcement point", "Projection / read model", "Source of truth", "Fields summary", "Evidence", "Source location"];
        var details = "\n  Lifecycle: " + node.Lifecycle + "; " + string.Join("; ", columns.Where(node.Properties.ContainsKey)
            .Select(key => (Key: key, Value: node.Properties[key].Trim()))
            .Where(item => item.Value.Length > 0 && !item.Value.Equals("unknown", StringComparison.OrdinalIgnoreCase) && item.Value != "TODO")
            .Select(item => item.Key + ": " + Regex.Replace(item.Value, @"\s+", " ")
                .Replace("Controller-relative path; global prefixes/versioning and effective policy require review.", "", StringComparison.Ordinal)
                .Replace("Deterministically discovered; role mapping and least-privilege review required.", "", StringComparison.Ordinal)));
        return details.Length <= 1800 ? details : details[..1770] + " [details truncated]";
    }

    private static int NodePriority(string kind) => kind switch
    {
        "repository" => 0, "component" => 1, "contract" => 2, "reference-item" => 3,
        "endpoint" => 4, "route" => 5, "entity" => 6, "event" => 7, "command" => 8,
        "symbol" => 9, "test" => 10, "document" => 11, "source-file" => 12, _ => 20,
    };

    private static Projection ExtractDocx(string path, SourceEvidenceEntry entry)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumPackageBytes) throw new InvalidDataException("Word package exceeds the extraction limit.");
        using var archive = ZipFile.OpenRead(path);
        if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry("word/document.xml") is null)
            throw new InvalidDataException("The file is not a valid Word Open XML document.");
        var styleNames = ReadStyleNames(archive);
        var parts = archive.Entries.Where(item => item.FullName == "word/document.xml"
                || item.FullName == "word/footnotes.xml" || item.FullName == "word/endnotes.xml"
                || item.FullName.StartsWith("word/header", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal)
                || item.FullName.StartsWith("word/footer", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(item => item.FullName == "word/document.xml" ? 0 : 1).ThenBy(item => item.FullName, StringComparer.Ordinal).ToArray();
        var output = Header(entry); var anchors = new List<SourceEvidenceAnchor>();
        var used = new HashSet<string>(StringComparer.Ordinal); var headings = new string[6]; var ordinal = 0; long xmlBytes = 0;
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace w14 = "http://schemas.microsoft.com/office/word/2010/wordml";
        foreach (var part in parts)
        {
            xmlBytes += part.Length;
            if (part.Length > MaximumXmlBytes || xmlBytes > MaximumXmlBytes) throw new InvalidDataException("Word XML exceeds the extraction limit.");
            var document = LoadXml(part);
            foreach (var paragraph in document.Descendants(w + "p"))
            {
                var text = ParagraphText(paragraph, w).Trim(); if (text.Length == 0) continue;
                var styleId = paragraph.Descendants(w + "pStyle").FirstOrDefault()?.Attribute(w + "val")?.Value;
                var style = styleId is not null && styleNames.TryGetValue(styleId, out var name) ? name : styleId;
                var headingLevel = HeadingLevel(style) ?? InferHeadingLevel(text, paragraph.Descendants(w + "numPr").Any());
                if (headingLevel is >= 1 and <= 6)
                {
                    ordinal++;
                    var slug = Unique(Slug(HeadingLabel(text)), used); headings[headingLevel.Value - 1] = slug;
                    Array.Clear(headings, headingLevel.Value, headings.Length - headingLevel.Value);
                    var headingParagraphId = paragraph.Attribute(w14 + "paraId")?.Value?.ToLowerInvariant();
                    anchors.Add(new(slug, part.FullName, headingParagraphId, ordinal,
                        string.Join('/', headings.Where(item => !string.IsNullOrWhiteSpace(item))), ShaText(text), text));
                    output.AppendLine($"<a id=\"{slug}\"></a>"); output.AppendLine($"{new string('#', headingLevel.Value)} {text}"); output.AppendLine(); continue;
                }
                ordinal++;
                var section = headings.LastOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? "document";
                var paraId = paragraph.Attribute(w14 + "paraId")?.Value?.ToLowerInvariant();
                var anchor = Unique(paraId is null ? $"{section}--p-{ordinal:0000}" : $"{section}--p-{paraId}", used);
                var headingPath = string.Join('/', headings.Where(item => !string.IsNullOrWhiteSpace(item)));
                anchors.Add(new(anchor, part.FullName, paraId, ordinal, headingPath, ShaText(text), text));
                output.AppendLine($"<a id=\"{anchor}\"></a>");
                output.AppendLine(paragraph.Descendants(w + "numPr").Any() ? "- " + text : text); output.AppendLine();
            }
        }
        if (anchors.Count == 0) throw new InvalidDataException("The Word document contains no readable body text.");
        return new(output.ToString(), anchors);
    }

    private static Dictionary<string, string> ReadStyleNames(ZipArchive archive)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var entry = archive.GetEntry("word/styles.xml");
        if (entry is null) return output;
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var style in LoadXml(entry).Descendants(w + "style"))
        {
            var id = style.Attribute(w + "styleId")?.Value; var name = style.Element(w + "name")?.Attribute(w + "val")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) output[id] = name;
        }
        return output;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string ParagraphText(XElement paragraph, XNamespace w)
    {
        var value = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == w + "t") value.Append(element.Value);
            else if (element.Name == w + "tab") value.Append('\t');
            else if (element.Name == w + "br" || element.Name == w + "cr") value.Append(' ');
            else if (element.Name == w + "noBreakHyphen") value.Append('-');
        }
        return value.ToString();
    }

    private static int? HeadingLevel(string? style)
    {
        if (style is null) return null;
        var match = Regex.Match(style.Replace(" ", string.Empty, StringComparison.Ordinal), "^Heading([1-6])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static int? InferHeadingLevel(string text, bool numbered)
    {
        if (numbered || text.Length > 140 || text.Contains('\n')) return null;
        if (text.EndsWith(':') || Regex.IsMatch(text, @"\(.*?\bup to\b.*?\bwords?\b.*?\)\s*:?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return 2;
        if (text.Length <= 80 && !text.EndsWith('.') && !text.EndsWith(';'))
        {
            var words = Regex.Matches(text, @"[A-Za-z][A-Za-z'-]*").Select(item => item.Value).ToArray();
            if (words.Length is >= 2 and <= 8
                && words.Count(word => char.IsUpper(word[0])) >= Math.Ceiling(words.Length * 0.7)) return 2;
        }
        return null;
    }

    private static string HeadingLabel(string text)
    {
        var label = Regex.Replace(text, @"\s*\(.*?\bup to\b.*?\bwords?\b.*?\)\s*:?$", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        label = Regex.Replace(label, @"\s*\([^)]*\)\s*:?$", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return label.Trim().TrimEnd(':').Trim();
    }

    private static StringBuilder Header(SourceEvidenceEntry entry)
    {
        var output = new StringBuilder(); output.AppendLine("---"); output.AppendLine("type: source-evidence-projection");
        output.AppendLine("authority: derived"); output.AppendLine($"source_id: {entry.Id}");
        output.AppendLine($"source_path: {JsonSerializer.Serialize(entry.SourcePath)}"); output.AppendLine("---"); output.AppendLine();
        output.AppendLine($"# Source projection: {Path.GetFileName(entry.SourcePath)}"); output.AppendLine();
        output.AppendLine("> Generated routing projection. The registered source file remains authoritative."); output.AppendLine(); return output;
    }

    private static string RenderIndexCard(SourceEvidenceEntry entry, Projection projection, string digest)
    {
        var headings = Regex.Matches(projection.Markdown, "(?m)^#{1,6}\\s+(.+)$").Select(item => item.Groups[1].Value).Skip(1).Take(20).ToArray();
        var synopsis = projection.Anchors.FirstOrDefault()?.Text ?? "No paragraph summary available.";
        return $"---\ntype: source-evidence-index-card\nauthority: derived\nsource_id: {entry.Id}\n---\n\n# {Path.GetFileName(entry.SourcePath)}\n\n" +
            $"- Source: `{entry.SourcePath}`\n- Format: {entry.Format}\n- Digest: `{digest}`\n- Paragraph anchors: {projection.Anchors.Count}\n\n" +
            $"## Routing synopsis\n\n{synopsis}\n\n## Sections\n\n" + (headings.Length == 0 ? "- Document body\n" : string.Join('\n', headings.Select(item => "- " + item)) + "\n");
    }

    private static IReadOnlyList<string> ReadCitations(CisRepositoryContext context, SourceEvidenceEntry entry)
    {
        var path = Path.Combine(context.DocumentationPath, "specs", "business-requirements.md"); if (!File.Exists(path)) return [];
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, "(?s)<!-- cis:sources:start -->.*?<!-- cis:sources:end -->", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var matches = Regex.Matches(text, $"(?<![A-Za-z0-9-]){Regex.Escape(entry.Id)}(?:#([a-z0-9][a-z0-9-]*))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var citations = matches.Select(item => item.Groups[1].Success ? item.Groups[1].Value.ToLowerInvariant() : string.Empty)
            .Distinct(StringComparer.Ordinal).ToList();
        if (citations.Count == 0 && entry.Format != "repository"
            && (text.Contains(entry.SourcePath, StringComparison.OrdinalIgnoreCase)
                || text.Contains(Path.GetFileName(entry.SourcePath), StringComparison.OrdinalIgnoreCase)))
            citations.Add(string.Empty);
        return citations.ToArray();
    }

    private static Dictionary<string, SourceEvidenceAnchor> ReadAnchors(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try { return (JsonSerializer.Deserialize<SourceEvidenceAnchor[]>(File.ReadAllText(path), JsonOptions) ?? [])
                .ToDictionary(item => item.Anchor, StringComparer.Ordinal); }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }

    private static string? ReadManifestDigest(string path)
    {
        if (!File.Exists(path)) return null;
        try { using var document = JsonDocument.Parse(File.ReadAllText(path)); return document.RootElement.TryGetProperty("detectedDigest", out var value) ? value.GetString() : null; }
        catch (JsonException) { return null; }
    }

    private static bool TryResolveSource(CisRepositoryContext context, string supplied, out string? absolute,
        out string? relative, List<string> diagnostics)
    {
        absolute = null; relative = null;
        try { absolute = Path.GetFullPath(Path.IsPathRooted(supplied) ? supplied : Path.Combine(context.RepositoryPath, supplied)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { diagnostics.Add("ERROR: Source path is invalid: " + exception.Message); return false; }
        if (!CisPathSafety.IsUnderRoot(context.RepositoryPath, absolute))
        { diagnostics.Add("ERROR: Canonical source evidence must be stored inside the initialized repository."); return false; }
        if (!File.Exists(absolute)) { diagnostics.Add("ERROR: Source evidence file does not exist: " + supplied); return false; }
        if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, absolute))
        { diagnostics.Add("ERROR: Linked source evidence is not accepted."); return false; }
        var name = Path.GetFileName(absolute); var extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.StartsWith(".", StringComparison.Ordinal) || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || extension is not (".docx" or ".md" or ".txt"))
        { diagnostics.Add("ERROR: Source evidence must be a non-secret .docx, .md, or .txt file."); return false; }
        if (new FileInfo(absolute).Length > MaximumPackageBytes)
        { diagnostics.Add($"ERROR: Source evidence exceeds {MaximumPackageBytes} bytes."); return false; }
        relative = Relative(context, absolute); return true;
    }

    private static bool TryResolveDirectory(string supplied, string basePath, out string? directory)
    {
        directory = null;
        try { directory = Path.GetFullPath(Path.IsPathRooted(supplied) ? supplied : Path.Combine(basePath, supplied)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        return Directory.Exists(directory);
    }

    private bool TryResolveRepositorySource(CisRepositoryContext authority, string directory, out string? sourceKey,
        out CisGraphDocument? graph, List<string> diagnostics)
    {
        sourceKey = null; graph = null;
        var sourceResolution = _resolver.Resolve(directory);
        if (!sourceResolution.IsSuccess || sourceResolution.Context is null)
        { diagnostics.Add("ERROR: Repository evidence must first be initialized with `cis repo init`: " + directory); return false; }
        var source = sourceResolution.Context;
        if (!string.Equals(source.RepositoryPath, authority.RepositoryPath, CisPathSafety.PlatformComparison))
        {
            var workspace = _workspaceRegistry?.Resolve(authority.RepositoryPath);
            if (workspace is null || !workspace.IsSuccess || workspace.Workspace is null
                || !workspace.Workspace.Repositories.Any(item => string.Equals(item.RepositoryPath, source.RepositoryPath, CisPathSafety.PlatformComparison)))
            { diagnostics.Add("ERROR: External repository evidence must be imported into the authority workspace first."); return false; }
        }
        if (_graphReader is null)
        { diagnostics.Add("ERROR: Repository evidence requires the CIS graph module."); return false; }
        var snapshot = _graphReader.Read(source.RepositoryPath);
        if (snapshot.Graph is null || snapshot.ExitCode != 0 || snapshot.Freshness != "fresh")
        {
            diagnostics.Add($"ERROR: Repository evidence requires a fresh graph for '{source.RepositoryId}'. Run `cis graph build --repo {source.RepositoryPath}`.");
            diagnostics.AddRange(snapshot.Errors.Select(item => "ERROR: " + item)); return false;
        }
        sourceKey = "workspace:" + source.RepositoryId; graph = snapshot.Graph; return true;
    }

    private bool TryResolveRegisteredRepository(CisRepositoryContext authority, SourceEvidenceEntry entry,
        out CisGraphDocument? graph, List<string> diagnostics)
    {
        graph = null;
        if (!TryResolveRegisteredRepositoryPath(authority, entry, out var repositoryPath, diagnostics)) return false;
        var snapshot = _graphReader!.Read(repositoryPath!);
        if (snapshot.Graph is null || snapshot.ExitCode != 0 || snapshot.Freshness != "fresh")
        { diagnostics.Add($"ERROR: Repository evidence requires a fresh graph for '{entry.SourcePath["workspace:".Length..]}'. Run `cis graph build --repo {repositoryPath}`."); return false; }
        graph = snapshot.Graph; return true;
    }

    private bool TryResolveRegisteredRepositoryPath(CisRepositoryContext authority, SourceEvidenceEntry entry,
        out string? repositoryPath, List<string> diagnostics)
    {
        repositoryPath = null;
        if (!entry.SourcePath.StartsWith("workspace:", StringComparison.Ordinal))
        { diagnostics.Add($"ERROR: Repository source key is invalid: {entry.SourcePath}"); return false; }
        var id = entry.SourcePath["workspace:".Length..];
        if (id == authority.RepositoryId) repositoryPath = authority.RepositoryPath;
        else
        {
            var workspace = _workspaceRegistry?.Resolve(authority.RepositoryPath);
            repositoryPath = workspace?.Workspace?.Repositories.SingleOrDefault(item => item.Id == id)?.RepositoryPath;
        }
        if (repositoryPath is null)
        { diagnostics.Add($"ERROR: Registered repository evidence is no longer present in the workspace: {id}"); return false; }
        if (_graphReader is null) { diagnostics.Add("ERROR: Repository evidence requires the CIS graph module."); return false; }
        return true;
    }

    private static IReadOnlyList<SourceEvidenceEntry> ReadRegistry(CisRepositoryContext context, List<string> diagnostics)
    {
        var path = RegistryPath(context); if (!File.Exists(path)) return [];
        var rows = new List<SourceEvidenceEntry>();
        foreach (var line in File.ReadLines(path))
        {
            var cells = TableCells(line); if (cells.Length != 8 || !cells[0].StartsWith("BRD-SRC-", StringComparison.Ordinal)) continue;
            if (!AllowedAssessments.Contains(cells[4], StringComparer.Ordinal))
            { diagnostics.Add($"ERROR: Source registry entry {cells[0]} has invalid assessment '{cells[4]}'."); continue; }
            rows.Add(new(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7]));
        }
        return rows;
    }

    private static void WriteRegistry(CisRepositoryContext context, IEnumerable<SourceEvidenceEntry> entries)
    {
        var builder = new StringBuilder("---\ntitle: \"Source evidence registry\"\ntype: source-evidence-registry\nstatus: Active\nauthority: human-reviewed\n---\n\n# Source evidence registry\n\nOriginal files remain authoritative. Markdown projections under `.cis/local/references/` are derived routing evidence and may be rebuilt without rewriting governed specifications.\n\n");
        builder.AppendLine("| ID | Source path | Format | Registered SHA-256 | Assessment | Actor | Registered at (UTC) | Rationale |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in entries.OrderBy(item => item.Id, StringComparer.Ordinal))
            builder.AppendLine($"| {Cell(item.Id)} | {Cell(item.SourcePath)} | {Cell(item.Format)} | {Cell(item.RegisteredDigest)} | {Cell(item.Assessment)} | {Cell(item.Actor)} | {Cell(item.RegisteredAtUtc)} | {Cell(item.Rationale)} |");
        WriteAtomic(RegistryPath(context), builder.ToString());
    }

    private bool EnsureCatalog(CisRepositoryContext context, List<string> diagnostics)
    {
        if (!File.Exists(context.CatalogPath))
        {
            diagnostics.Add("ERROR: Documentation catalog is missing: " + Relative(context, context.CatalogPath));
            return false;
        }
        var path = Relative(context, RegistryPath(context));
        var merge = _catalogMerger.Merge(context.RepositoryId, File.ReadAllText(context.CatalogPath),
            [new CatalogArtifactEntry($"{context.RepositoryId}:reference:source-evidence", path,
                "source-evidence-registry", "active", "canonical")]);
        if (merge.Collisions.Count > 0)
        {
            diagnostics.AddRange(merge.Collisions.Select(item => "ERROR: " + item));
            return false;
        }
        if (!string.Equals(merge.Content, File.ReadAllText(context.CatalogPath), StringComparison.Ordinal))
            WriteAtomic(context.CatalogPath, merge.Content);
        return true;
    }

    private static string[] TableCells(string line)
    {
        if (!line.TrimStart().StartsWith('|')) return [];
        var cells = new List<string>(); var value = new StringBuilder(); var escaped = false;
        foreach (var character in line.Trim().Trim('|'))
        {
            if (escaped) { value.Append(character); escaped = false; }
            else if (character == '\\') escaped = true;
            else if (character == '|') { cells.Add(value.ToString().Trim()); value.Clear(); }
            else value.Append(character);
        }
        cells.Add(value.ToString().Trim()); return cells.ToArray();
    }

    private static SourceEvidenceState State(SourceEvidenceEntry entry, string? detected, string status, bool material,
        string? projection, IReadOnlyList<string> diagnostics) => new(entry.Id, entry.SourcePath, entry.RegisteredDigest,
            detected, entry.Assessment, status, material, projection, diagnostics);
    private static SourceEvidenceResult ErrorResult(string repositoryPath, IEnumerable<string> errors)
        => new("invalid", TryRepository(repositoryPath), null, [], errors.Select(item => "ERROR: " + item).ToArray(), false);
    private static CisSourceEvidenceRegistration RegistrationError(IEnumerable<string> errors, CisRepositoryContext? context = null)
        => new("invalid", null, null, null, null, context is null ? null : Relative(context, RegistryPath(context)), null, errors.ToArray());
    private static string RegistryPath(CisRepositoryContext context) => Path.Combine(context.DocumentationPath, "references", RegistryFileName);
    private static string ProjectionDirectory(CisRepositoryContext context, string id) => Path.Combine(context.RepositoryPath, ".cis", "local", "references", id);
    private static string StableId(string repositoryId, string path) => "BRD-SRC-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(repositoryId + "\u001f" + path.Replace('\\', '/'))))[..12].ToLowerInvariant();
    private static string Format(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch { "md" => "markdown", var value => value };
    private static string Slug(string value) { var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'); return slug.Length == 0 ? "section" : slug; }
    private static string Unique(string value, ISet<string> used) { var candidate = value; var suffix = 2; while (!used.Add(candidate)) candidate = value + "-" + suffix++; return candidate; }
    private static string Cell(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    private static string Relative(CisRepositoryContext context, string path) => Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
    private static string ShaFile(string path) => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string ShaText(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private string UtcNow() => _clock().ToUniversalTime().ToString("O");
    private static string? TryRepository(string path) { try { return Path.GetFullPath(path); } catch { return null; } }
    private static void WriteJson<T>(string path, T value) => WriteAtomic(path, JsonSerializer.Serialize(value, JsonOptions));
    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false)); File.Move(temporary, path, true);
    }
    private sealed record Projection(string Markdown, IReadOnlyList<SourceEvidenceAnchor> Anchors);
}
