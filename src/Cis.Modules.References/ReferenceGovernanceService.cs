using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.References;

public sealed class ReferenceGovernanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ExcludedSegments =
    [".git", ".codex-tmp", "node_modules", "bin", "obj", ".next", "dist", "coverage", ".terraform", "artifacts", ".artifacts", "out", ".cis"];

    private readonly ICisRepositoryContextResolver _resolver;
    private readonly IReadOnlyList<ICisReferenceProvider> _providers;

    public ReferenceGovernanceService(
        ICisRepositoryContextResolver resolver,
        IEnumerable<ICisReferenceProvider> providers)
    {
        _resolver = resolver;
        _providers = providers.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.GetType().FullName, StringComparer.Ordinal).ToArray();
    }

    public ReferenceDiscoveryResult Discover(string repositoryPath)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return DiscoveryError(repositoryPath, resolution.Errors);

        var context = resolution.Context;
        var diagnostics = ProviderDiagnostics().ToList();
        var discoveryContext = new CisReferenceDiscoveryContext(context.RepositoryPath, context.DocumentationPath, context.RepositoryId);
        var families = new List<ReferenceFamilyState>();
        foreach (var group in _providers.GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (group.Count() != 1) continue;
            var provider = group.Single();
            var canonicalPath = Path.Combine(context.DocumentationPath, "references", provider.CanonicalFileName);
            var canonical = File.Exists(canonicalPath)
                ? ParseCanonical(provider.Kind, canonicalPath, context.RepositoryPath, diagnostics)
                : [];
            var observations = DiscoverProvider(provider, discoveryContext);
            var correlated = Correlate(observations, canonical);
            families.Add(new(
                provider.Kind,
                provider.GetType().FullName ?? provider.GetType().Name,
                Relative(context.RepositoryPath, canonicalPath),
                File.Exists(canonicalPath),
                canonical,
                correlated));
        }

        var document = CreateDocument(context, families, diagnostics);
        var output = StatePath(context.RepositoryPath);
        var prior = ReadState(context.RepositoryPath, out _);
        if (prior is not null && prior.Digest == document.Digest
            && prior.RepositoryRevision == document.RepositoryRevision)
            document = document with { CreatedAtUtc = prior.CreatedAtUtc };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var rendered = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        var applied = !File.Exists(output) || !string.Equals(File.ReadAllText(output), rendered, StringComparison.Ordinal);
        if (applied) AtomicWrite(output, rendered);
        return new("discovered", context.RepositoryPath, output, document.Digest, families, diagnostics, applied, true);
    }

    public ReferenceInventoryResult Inventory(string repositoryPath, string? kind = null)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, null, [], resolution.Errors);
        var state = ReadState(resolution.Context.RepositoryPath, out var errors);
        if (state is null) return new("missing-state", resolution.Context.RepositoryPath, null, [], errors);
        var families = string.IsNullOrWhiteSpace(kind) ? state.Families
            : state.Families.Where(item => item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!string.IsNullOrWhiteSpace(kind) && families.Count == 0)
            errors.Add($"Reference kind is not available in normalized state: {kind}");
        return new(errors.Count == 0 ? "available" : "invalid", resolution.Context.RepositoryPath, state.Digest, families, errors);
    }

    public ReferenceValidationResult Validate(string repositoryPath, bool strict, bool refresh = true)
    {
        if (refresh)
        {
            var discovery = Discover(repositoryPath);
            if (!discovery.RepositoryConfigurationValid)
                return new("invalid-repository", discovery.RepositoryPath, strict, discovery.Diagnostics, false);
        }
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, strict,
                resolution.Errors.Select(error => Diagnostic("CIS-REF-REPO-001", "error", "repository", error, "Run cis repo doctor and repair repository configuration.")).ToArray(), false);
        var state = ReadState(resolution.Context.RepositoryPath, out var readErrors);
        if (state is null)
            return new("missing-state", resolution.Context.RepositoryPath, strict,
                readErrors.Select(error => Diagnostic("CIS-REF-STATE-001", "error", "state", error, "Run cis references discover.")).ToArray(), true);

        var diagnostics = state.Diagnostics.ToList();
        foreach (var family in state.Families.Where(item => item.CanonicalAvailable))
        {
            foreach (var duplicate in family.CanonicalEntries.GroupBy(item => Normalize(item.Identity), StringComparer.Ordinal).Where(item => item.Count() > 1))
                diagnostics.Add(Diagnostic("CIS-REF-CANON-001", "error", family.Kind,
                    $"Canonical identity is duplicated: {duplicate.First().Identity}", "Keep one row per stable identity.", duplicate.Select(item => $"{item.Path}:{item.Line}").ToArray()));

            foreach (var observation in family.Observations.Where(item => !item.CanonicalDeclared))
                diagnostics.Add(Diagnostic("CIS-REF-DRIFT-001", "warning", family.Kind,
                    $"Source identity is missing from the canonical reference: {observation.Identity}",
                    $"Review and add or explicitly exclude the identity in {family.CanonicalPath}.", [Location(observation.SourcePath, observation.Line)]));

            foreach (var entry in family.CanonicalEntries.Where(IsCurrent).Where(entry =>
                         !family.Observations.Any(observation => observation.CanonicalIdentity?.Equals(entry.Identity, StringComparison.OrdinalIgnoreCase) == true)))
                diagnostics.Add(Diagnostic("CIS-REF-DRIFT-002", "warning", family.Kind,
                    $"Current canonical identity has no discovered source evidence: {entry.Identity}",
                    "Update its evidence/status or restore the corresponding implementation.", [$"{entry.Path}:{entry.Line}"]));

            foreach (var entry in family.CanonicalEntries)
            {
                foreach (var evidence in entry.Evidence.Where(LooksLikePath))
                {
                    var relative = evidence.Split('#')[0].Split(':')[0].Trim('`', ' ');
                    if (relative.Length == 0 || File.Exists(Path.Combine(resolution.Context.RepositoryPath, relative.Replace('/', Path.DirectorySeparatorChar)))) continue;
                    diagnostics.Add(Diagnostic("CIS-REF-EVIDENCE-001", "warning", family.Kind,
                        $"Canonical evidence path does not exist: {relative}", "Repair or retire the evidence locator.", [$"{entry.Path}:{entry.Line}"]));
                }
            }
        }

        diagnostics = diagnostics.DistinctBy(item => $"{item.Code}|{item.Kind}|{item.Message}", StringComparer.Ordinal).ToList();
        var errors = diagnostics.Count(item => item.Severity == "error");
        var warnings = diagnostics.Count(item => item.Severity == "warning");
        return new(errors > 0 || strict && warnings > 0 ? "invalid" : "valid", resolution.Context.RepositoryPath, strict, diagnostics, true);
    }

    public ReferenceReconcileResult Reconcile(string repositoryPath, IReadOnlyList<string> selectedKinds, bool yes)
    {
        var discovery = Discover(repositoryPath);
        if (!discovery.RepositoryConfigurationValid || discovery.RepositoryPath is null)
            return new("invalid-repository", discovery.RepositoryPath, selectedKinds, 0, [], [],
                discovery.Diagnostics.Select(item => "ERROR: " + item.Message).ToArray(), false, false);
        var supported = new HashSet<string>(["configuration-dictionary", "module-ownership-map", "package-catalogue", "screen-route-map"], StringComparer.OrdinalIgnoreCase);
        var kinds = selectedKinds.Count == 0 ? supported.Order(StringComparer.Ordinal).ToArray()
            : selectedKinds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var diagnostics = kinds.Where(kind => !supported.Contains(kind)).Select(kind => $"ERROR: Reconciliation is not supported for reference kind '{kind}'.").ToList();
        var plans = discovery.Families.Where(family => kinds.Contains(family.Kind, StringComparer.OrdinalIgnoreCase))
            .Select(family => (Family: family, Missing: family.Observations
                .Where(item => !item.CanonicalDeclared)
                .DistinctBy(item => item.Identity, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
            .Where(item => item.Missing.Length > 0).ToArray();
        var total = plans.Sum(item => item.Missing.Length);
        var proposedRows = plans.SelectMany(plan => plan.Missing.Select(item => $"{plan.Family.CanonicalPath}: {RenderReconcileRow(plan.Family.Kind, item)}")).ToArray();
        if (diagnostics.Count > 0) return new("invalid", discovery.RepositoryPath, kinds, 0, [], [], diagnostics, false, false);
        if (total == 0) return new("unchanged", discovery.RepositoryPath, kinds, 0, [], [], [], false, false);
        if (!yes) return new("confirmation-required", discovery.RepositoryPath, kinds, total,
            plans.Select(item => item.Family.CanonicalPath).ToArray(), proposedRows, ["Review proposed additive rows and rerun with --yes."], false, true);

        var updated = new List<string>();
        foreach (var plan in plans)
        {
            var absolute = Path.Combine(discovery.RepositoryPath, plan.Family.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
            var content = File.ReadAllText(absolute); var rows = plan.Missing.Select(item => RenderReconcileRow(plan.Family.Kind, item)).ToArray();
            var reconciled = AppendTableRows(content, rows, out var error);
            if (error is not null) { diagnostics.Add($"ERROR: {plan.Family.CanonicalPath}: {error}"); continue; }
            if (!string.Equals(content, reconciled, StringComparison.Ordinal)) { AtomicWrite(absolute, reconciled); updated.Add(plan.Family.CanonicalPath); }
        }
        if (diagnostics.Count > 0) return new("partial", discovery.RepositoryPath, kinds, total, updated, proposedRows, diagnostics, updated.Count > 0, false);
        Discover(discovery.RepositoryPath);
        return new("reconciled", discovery.RepositoryPath, kinds, total, updated, proposedRows, [], updated.Count > 0, false);
    }

    public ReferenceDiffResult Diff(string repositoryPath, string baseline)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, baseline, null, [],
                resolution.Errors.Select(error => Diagnostic("CIS-REF-REPO-001", "error", "repository", error, "Run cis repo doctor.")).ToArray(), false);
        var context = resolution.Context;
        if (string.IsNullOrWhiteSpace(baseline))
            return new("invalid-request", context.RepositoryPath, baseline, Git(context.RepositoryPath, "rev-parse", "HEAD").Output, [],
                [Diagnostic("CIS-REF-DIFF-001", "error", "git", "Baseline revision is required.", "Supply a commit, tag, or branch.")], true);
        var verify = Git(context.RepositoryPath, "rev-parse", "--verify", $"{baseline}^{{commit}}");
        if (verify.ExitCode != 0)
            return new("invalid-baseline", context.RepositoryPath, baseline, Git(context.RepositoryPath, "rev-parse", "HEAD").Output, [],
                [Diagnostic("CIS-REF-DIFF-002", "error", "git", $"Baseline does not resolve to a commit: {baseline}", "Supply a valid commit, tag, or branch.")], true);

        var diagnostics = ProviderDiagnostics().ToList();
        var changes = new List<ReferenceDiffItem>();
        var discoveryContext = new CisReferenceDiscoveryContext(context.RepositoryPath, context.DocumentationPath, context.RepositoryId);
        var changedFiles = Git(context.RepositoryPath, "diff", "--name-only", baseline, "--").Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(item => item.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _providers.GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).Where(item => item.Count() == 1))
        {
            var provider = group.Single();
            var canonicalAbsolute = Path.Combine(context.DocumentationPath, "references", provider.CanonicalFileName);
            var canonicalRelative = Relative(context.RepositoryPath, canonicalAbsolute);
            var current = File.Exists(canonicalAbsolute) ? ParseCanonical(provider.Kind, canonicalAbsolute, context.RepositoryPath, diagnostics) : [];
            var priorContent = Git(context.RepositoryPath, "show", $"{baseline}:{canonicalRelative}");
            var prior = priorContent.ExitCode == 0 ? ParseCanonicalContent(provider.Kind, canonicalRelative, priorContent.Output, diagnostics) : [];
            CompareCanonical(provider.Kind, prior, current, changes);

            var sourceDrift = false;
            foreach (var path in changedFiles.Where(provider.Supports))
            {
                var oldFile = Git(context.RepositoryPath, "show", $"{baseline}:{path}");
                var currentPath = Path.Combine(context.RepositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
                var oldItems = oldFile.ExitCode == 0
                    ? provider.Discover(discoveryContext, new(path, oldFile.Output)).Select(ObservationKey).ToHashSet(StringComparer.Ordinal)
                    : [];
                var newItems = File.Exists(currentPath)
                    ? provider.Discover(discoveryContext, new(path, File.ReadAllText(currentPath))).Select(ObservationKey).ToHashSet(StringComparer.Ordinal)
                    : [];
                if (!oldItems.SetEquals(newItems)) sourceDrift = true;
            }
            if (sourceDrift && !changedFiles.Contains(canonicalRelative))
                diagnostics.Add(Diagnostic("CIS-REF-DIFF-003", "warning", provider.Kind,
                    $"Source identities changed without updating {canonicalRelative}.",
                    "Review the source delta and co-update or explicitly confirm the canonical reference.", changedFiles.Where(provider.Supports).ToArray()));
        }
        return new(diagnostics.Any(item => item.Severity == "error") ? "invalid" : "compared", context.RepositoryPath,
            verify.Output, Git(context.RepositoryPath, "rev-parse", "HEAD").Output, changes, diagnostics, true);
    }

    private IReadOnlyList<ReferenceDiagnostic> ProviderDiagnostics()
        => _providers.GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).Where(item => item.Count() > 1)
            .Select(group => Diagnostic("CIS-REF-PROVIDER-001", "error", group.Key,
                $"Multiple providers register reference kind '{group.Key}'.", "Remove the conflict or select a distinct stable kind.",
                group.Select(item => item.GetType().FullName ?? item.GetType().Name).ToArray())).ToArray();

    private static IReadOnlyList<CisReferenceObservation> DiscoverProvider(ICisReferenceProvider provider, CisReferenceDiscoveryContext context)
    {
        var items = new Dictionary<string, CisReferenceObservation>(StringComparer.Ordinal);
        void Observe(string path)
        {
            var relative = Relative(context.RepositoryPath, path);
            if (!provider.Supports(relative)) return;
            string content;
            try { content = File.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
            foreach (var item in provider.Discover(context, new(relative, content))) items.TryAdd(ObservationKey(item), item);
        }

        // `.cis` is excluded as derived/internal state, but repository.yml is the canonical
        // repository-boundary configuration and must remain discoverable by configuration providers.
        var repositoryConfiguration = Path.Combine(context.RepositoryPath, ".cis", "repository.yml");
        if (File.Exists(repositoryConfiguration)) Observe(repositoryConfiguration);
        foreach (var path in EnumerateSourceFiles(context.RepositoryPath, context.DocumentationPath))
            Observe(path);
        return items.Values.OrderBy(item => item.Identity, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.SourcePath, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string repositoryPath, string documentationPath)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            foreach (var child in directories)
            {
                if (CisPathSafety.IsUnderRoot(documentationPath, child, allowRoot: true)
                    || ExcludedSegments.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)
                    || CisPathSafety.IsReparsePoint(child))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static IReadOnlyList<ReferenceObservationState> Correlate(
        IReadOnlyList<CisReferenceObservation> observations,
        IReadOnlyList<CanonicalReferenceEntry> canonical)
    {
        var lookup = new Dictionary<string, CanonicalReferenceEntry>(StringComparer.Ordinal);
        foreach (var entry in canonical)
            foreach (var alias in entry.Aliases.Append(entry.Identity).Select(Normalize).Where(item => item.Length > 0))
                lookup.TryAdd(alias, entry);
        return observations.Select(item =>
        {
            var match = item.Aliases.Append(item.Identity).Select(Normalize)
                .Where(lookup.ContainsKey).Select(key => lookup[key]).FirstOrDefault();
            return new ReferenceObservationState(item.Kind, item.Identity, item.DisplayName, item.SourcePath, item.Line,
                item.Aliases, match is not null, match?.Identity);
        }).ToArray();
    }

    private static IReadOnlyList<CanonicalReferenceEntry> ParseCanonical(
        string kind, string path, string repositoryPath, List<ReferenceDiagnostic> diagnostics)
        => ParseCanonicalContent(kind, Relative(repositoryPath, path), File.ReadAllText(path), diagnostics);

    private static IReadOnlyList<CanonicalReferenceEntry> ParseCanonicalContent(
        string kind, string relativePath, string content, List<ReferenceDiagnostic> diagnostics)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith('|') || !IsSeparator(lines[index + 1])) continue;
            var headers = Cells(lines[index]);
            var aliases = AliasColumns(kind);
            var identityHeader = aliases.FirstOrDefault(header => headers.Contains(header, StringComparer.OrdinalIgnoreCase));
            if (identityHeader is null) continue;
            var result = new List<CanonicalReferenceEntry>();
            for (var row = index + 2; row < lines.Length && lines[row].TrimStart().StartsWith('|'); row++)
            {
                var cells = Cells(lines[row]);
                if (cells.Count != headers.Count) continue;
                var values = headers.Select((header, cell) => (header, value: CleanCell(cells[cell])))
                    .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
                var identity = values[identityHeader];
                if (string.IsNullOrWhiteSpace(identity) || identity.Equals("TODO", StringComparison.OrdinalIgnoreCase)) continue;
                var entryAliases = aliases.Where(values.ContainsKey).Select(header => values[header]).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                AddCompositeAliases(kind, values, entryAliases);
                if (kind == "package-catalogue" && values.TryGetValue("Component", out var packageComponent))
                {
                    identity = $"{identity}@{packageComponent}";
                    entryAliases = [identity];
                }
                var display = entryAliases.Skip(1).FirstOrDefault() ?? identity;
                var evidence = values.Where(item => item.Key.Contains("Evidence", StringComparison.OrdinalIgnoreCase)
                                                     || item.Key.Contains("Source location", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(item => item.Value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();
                var status = values.FirstOrDefault(item => item.Key.Equals("Status", StringComparison.OrdinalIgnoreCase)).Value ?? "unknown";
                result.Add(new(kind, identity, display, status, Hash(lines[row]), entryAliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), evidence, relativePath, row + 1));
            }
            return result;
        }
        diagnostics.Add(Diagnostic("CIS-REF-CANON-002", "error", kind,
            $"Canonical reference has no recognized table: {relativePath}", "Restore the seeded table headers or migrate the document explicitly.", [relativePath]));
        return [];
    }

    private static string[] AliasColumns(string kind) => kind switch
    {
        "command-dictionary" => ["Command ID", "Command name"],
        "event-dictionary" => ["Event ID", "Event name"],
        "workflow-state-dictionary" => ["Workflow ID", "Workflow", "State"],
        "business-invariant-catalogue" => ["Invariant ID", "Rule"],
        "projection-dictionary" => ["Projection ID", "Projection / read model"],
        "permissions-dictionary" => ["Permission code", "Permission name"],
        "configuration-dictionary" => ["Name", "Path"],
        "package-catalogue" => ["Package"],
        "screen-route-map" => ["Screen or route", "Destination"],
        "problem-details-catalogue" => ["Problem ID", "Problem type / reason"],
        "module-ownership-map" => ["Module ID", "Module / context", "Code area"],
        "data-dictionary" => ["Entity", "Field"],
        "erd" => ["Entity", "Target"],
        _ => ["ID", "Name"],
    };

    private static void AddCompositeAliases(string kind, IReadOnlyDictionary<string, string> values, List<string> aliases)
    {
        if (kind == "workflow-state-dictionary" && values.TryGetValue("Workflow", out var workflow) && values.TryGetValue("State", out var state)) aliases.Add($"{workflow}.{state}");
        if (kind == "data-dictionary" && values.TryGetValue("Entity", out var entity) && values.TryGetValue("Field", out var field)) aliases.Add($"{entity}.{field}");
        if (kind == "erd" && values.TryGetValue("Entity", out var owner) && values.TryGetValue("Target", out var target)) aliases.Add($"{owner}->{target}");
    }

    private static string RenderReconcileRow(string kind, ReferenceObservationState item)
    {
        var evidence = item.Line is null ? item.SourcePath : $"{item.SourcePath}:{item.Line}";
        static string Cell(string value) => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return kind switch
        {
            "configuration-dictionary" => $"| `{Cell(item.Identity)}` | `environment:{Cell(item.Identity)}` | Runtime-configured value | process start | Repository maintainer | {(SensitiveName(item.Identity) ? "yes" : "no")} | Discovered runtime configuration identity. | Active | `{Cell(evidence)}` |",
            "module-ownership-map" => $"| MOD-{Slug(item.Identity).ToUpperInvariant()} | {Cell(item.Identity)} | {Cell(Path.GetDirectoryName(item.SourcePath)?.Replace('\\', '/') ?? item.SourcePath)} | {(item.SourcePath.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ? "test automation" : "module implementation")} | governed by module CLI or library surface | module-owned state | none detected | dependency direction follows project references | repository profile; module catalog | Active | {Cell(evidence)} |",
            "package-catalogue" => PackageRow(item, evidence),
            "screen-route-map" => ScreenRouteRow(item, evidence),
            _ => throw new InvalidOperationException($"Unsupported reference kind: {kind}"),
        };
    }

    private static string PackageRow(ReferenceObservationState item, string evidence)
    {
        var split = item.Identity.LastIndexOf('@'); var package = split > 0 ? item.Identity[..split] : item.DisplayName;
        var component = split > 0 ? item.Identity[(split + 1)..] : "root";
        return $"| {package.Replace('|', '/')} | {component.Replace('|', '/')} | Declared package dependency | centrally managed | package-reference | Active | {evidence.Replace('|', '/')} |";
    }

    private static string ScreenRouteRow(ReferenceObservationState item, string evidence)
    {
        var normalized = item.SourcePath.Replace('\\', '/');
        var component = normalized.Contains('/') ? normalized[..normalized.IndexOf('/')] : "root";
        var extension = Path.GetExtension(item.SourcePath);
        var platform = extension is ".swift" or ".kt" or ".kts" ? "native" : "web";
        var route = item.Identity.Replace('|', '/');
        return $"| {route} | {component.Replace('|', '/')} | {platform} | application-defined | {route} | Active | {evidence.Replace('|', '/')} | Deterministically discovered route; access semantics require review. |";
    }

    private static string AppendTableRows(string content, IReadOnlyList<string> rows, out string? error)
    {
        error = null; var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal); var lines = normalized.Split('\n').ToList();
        var header = lines.FindIndex(line => line.TrimStart().StartsWith('|'));
        if (header < 0 || header + 1 >= lines.Count || !IsSeparator(lines[header + 1])) { error = "Canonical Markdown table was not found."; return content; }
        var insert = header + 2; while (insert < lines.Count && lines[insert].TrimStart().StartsWith('|')) insert++;
        lines.InsertRange(insert, rows); var rendered = string.Join("\n", lines);
        return content.Contains("\r\n", StringComparison.Ordinal) ? rendered.Replace("\n", "\r\n", StringComparison.Ordinal) : rendered;
    }

    private static bool SensitiveName(string value) => value.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || value.Contains("password", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("api_key", StringComparison.OrdinalIgnoreCase);
    private static string Slug(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

    private static void CompareCanonical(string kind, IReadOnlyList<CanonicalReferenceEntry> prior, IReadOnlyList<CanonicalReferenceEntry> current, List<ReferenceDiffItem> changes)
    {
        var old = prior.ToDictionary(item => Normalize(item.Identity), StringComparer.Ordinal);
        var latest = current.ToDictionary(item => Normalize(item.Identity), StringComparer.Ordinal);
        foreach (var item in latest.Where(item => !old.ContainsKey(item.Key))) changes.Add(new(kind, "added", item.Value.Identity, "Canonical reference identity was added.", [$"{item.Value.Path}:{item.Value.Line}"]));
        foreach (var item in old.Where(item => !latest.ContainsKey(item.Key))) changes.Add(new(kind, "removed", item.Value.Identity, "Canonical reference identity was removed.", [$"{item.Value.Path}:{item.Value.Line}"]));
        foreach (var item in latest.Where(item => old.TryGetValue(item.Key, out var before) && before.RowDigest != item.Value.RowDigest)) changes.Add(new(kind, "changed", item.Value.Identity, "Canonical reference row changed.", [$"{item.Value.Path}:{item.Value.Line}"]));
    }

    private static ReferenceInventoryDocument CreateDocument(CisRepositoryContext context, IReadOnlyList<ReferenceFamilyState> families, IReadOnlyList<ReferenceDiagnostic> diagnostics)
    {
        var revision = Git(context.RepositoryPath, "rev-parse", "HEAD").Output;
        var material = string.Join('\n', families.SelectMany(family => family.CanonicalEntries.Select(item => $"C|{family.Kind}|{item.Identity}|{item.RowDigest}"))
            .Concat(families.SelectMany(family => family.Observations.Select(item => $"S|{family.Kind}|{item.Identity}|{item.SourcePath}|{item.Line}"))));
        return new(1, context.RepositoryId, revision, DateTimeOffset.UtcNow, Hash(material), families, diagnostics);
    }

    private static ReferenceInventoryDocument? ReadState(string repositoryPath, out List<string> errors)
    {
        errors = [];
        var path = StatePath(repositoryPath);
        if (!File.Exists(path)) { errors.Add("Reference state is unavailable. Run cis references discover."); return null; }
        try
        {
            var state = JsonSerializer.Deserialize<ReferenceInventoryDocument>(File.ReadAllText(path), JsonOptions);
            if (state is null) errors.Add("Reference state is empty.");
            else if (state.SchemaVersion != 1) errors.Add($"Unsupported reference-state schema: {state.SchemaVersion}");
            return errors.Count == 0 ? state : null;
        }
        catch (JsonException exception) { errors.Add($"Reference state is invalid JSON: {exception.Message}"); return null; }
    }

    private static bool IsCurrent(CanonicalReferenceEntry entry) => entry.Status is not ("Planned" or "Draft" or "Deprecated" or "Archived" or "Retired");
    private static bool LooksLikePath(string value) => value.Contains('/') || value.Contains('\\') || value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".ts", StringComparison.OrdinalIgnoreCase);
    private static bool IsSeparator(string line) => Cells(line).Count > 0 && Cells(line).All(cell => cell.Trim().Trim(':').All(character => character == '-'));
    private static List<string> Cells(string line) => line.Trim().Trim('|').Split('|').Select(item => item.Replace("\\|", "|", StringComparison.Ordinal).Trim()).ToList();
    private static string CleanCell(string value) => value.Trim().Trim('`').Replace("<br>", ";", StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value)
    {
        var alphanumeric = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return alphanumeric.Length > 0 ? alphanumeric : value.Trim().ToLowerInvariant();
    }
    private static string ObservationKey(CisReferenceObservation item) => $"{Normalize(item.Identity)}|{item.SourcePath}|{item.Line}";
    private static string Location(string path, int? line) => line is null ? path : $"{path}:{line}";
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string StatePath(string repositoryPath) => Path.Combine(repositoryPath, ".cis", "local", "references", "inventory.json");
    private static void AtomicWrite(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
    private static ReferenceDiagnostic Diagnostic(string code, string severity, string kind, string message, string remediation, IReadOnlyList<string>? evidence = null)
        => new(code, severity, kind, message, remediation, evidence ?? []);
    private static ReferenceDiscoveryResult DiscoveryError(string repositoryPath, IReadOnlyList<string> errors)
        => new("invalid-repository", repositoryPath, null, null, [], errors.Select(error => Diagnostic("CIS-REF-REPO-001", "error", "repository", error, "Run cis repo doctor.")).ToArray(), false, false);

    private static (int ExitCode, string Output) Git(string repositoryPath, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repositoryPath };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(30));
            return (result.ExitCode ?? 1, result.StandardOutput.Trim());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (1, string.Empty);
        }
    }
}
