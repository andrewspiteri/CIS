using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Brd;
using Cis.Modules.Repository;

namespace Cis.Modules.TechnicalIntent;

public sealed partial class TechnicalIntentService : IChangeReadinessCheck
{
    private const string BaselineStart = "<!-- cis:technical-intent-baseline:start -->";
    private const string BaselineEnd = "<!-- cis:technical-intent-baseline:end -->";

    private static readonly string[][] RequiredSectionAliases =
    [
        ["Approved business baseline"],
        ["Design goals and principles"],
        ["Technical surface and ownership", "Detected technical surface"],
        ["Runtime architecture and trust boundaries", "Architecture and boundaries"],
        ["Application model and invariants", "Data and consistency"],
        ["API, integration, and compatibility intent", "Integration and contracts"],
        ["Security and privacy intent", "Security and privacy"],
        ["Operations, migration, and recovery intent", "Operations and observability"],
        ["Quality attributes and verification direction", "Quality attributes"],
        ["Decisions and delivery constraints"],
        ["Open technical decisions"],
    ];

    private readonly BrdService _brd;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ICisGraphSnapshotReader _graphReader;
    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly ICisWorkspaceRegistry _workspaceRegistry;

    public TechnicalIntentService(
        ICisWorkspaceRegistry workspaceRegistry,
        ICisRepositoryContextResolver repositoryResolver,
        ICisGraphSnapshotReader graphReader,
        BrdService brd,
        DocumentationCatalogMerger catalogMerger,
        Func<DateTimeOffset>? clock = null)
    {
        _workspaceRegistry = workspaceRegistry;
        _repositoryResolver = repositoryResolver;
        _graphReader = graphReader;
        _brd = brd;
        _catalogMerger = catalogMerger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TechnicalIntentResult Initialize(string workspacePath)
    {
        var state = ResolveState(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state);
        if (state.ReadinessErrors.Count > 0) return Error("blocked", state, state.ReadinessErrors.ToArray());

        var stableId = $"{state.Authority!.Id}:spec:technical-intent";
        var canonicalPath = state.CanonicalPath!;
        var relativePath = NormalizePath(Path.GetRelativePath(state.Authority.RepositoryPath, canonicalPath));
        var catalog = File.ReadAllText(state.Context!.CatalogPath);
        var merge = _catalogMerger.Merge(
            state.Authority.Id,
            catalog,
            [new CatalogArtifactEntry(stableId, relativePath, "repository-specification", "draft", "canonical")]);
        if (merge.Collisions.Count > 0)
            return new TechnicalIntentResult("collision", state.Workspace!.WorkspacePath, state.Authority.Id,
                relativePath, null, state.Baselines, state.Warnings, merge.Collisions, false);

        string content;
        string? existing = null;
        var approvalCanCarryForward = false;
        if (!File.Exists(canonicalPath))
        {
            content = RenderNew(state.Authority.Id, stableId, state.Baselines);
        }
        else
        {
            existing = File.ReadAllText(canonicalPath);
            content = existing;
            if (!content.Contains($"stable_id: {stableId}", StringComparison.Ordinal))
                return Error("collision", state, "The canonical technical-intent path contains a document with a different stable identity.");
            content = EnsureMetadata(content);
            content = ReplaceOrInsertBaseline(content, state.Baselines);
            approvalCanCarryForward = HasCurrentApproval(existing)
                && BaselinesCanCarryForward(existing, state.Baselines);
        }

        var changed = existing is null || !Equivalent(existing, content);
        if (changed)
        {
            if (approvalCanCarryForward)
                content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
            else
            {
                content = ReplaceFrontMatter(content, "status", "Draft");
                content = ReplaceFrontMatter(content, "last_reviewed", "null");
                content = ReplaceNestedFrontMatter(content, "approved_by", "null");
                content = ReplaceNestedFrontMatter(content, "approved_at", "null");
                content = ReplaceNestedFrontMatter(content, "approval_reason", "null");
                content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
            }
        }

        var nextCatalog = changed
            ? UpdateCatalogStatus(merge.Content, stableId, approvalCanCarryForward ? "active" : "draft")
            : merge.Content;
        var catalogChanged = !Equivalent(catalog, nextCatalog);
        if (!changed && !catalogChanged)
            return ValidateInternal(workspacePath, "unchanged", applied: false);

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
        Write(canonicalPath, content);
        if (catalogChanged) Write(state.Context.CatalogPath, nextCatalog);
        return ValidateInternal(workspacePath, "initialized", applied: true);
    }

    public TechnicalIntentResult Validate(string workspacePath)
        => ValidateInternal(workspacePath, "validated", applied: false);

    public TechnicalIntentResult Status(string workspacePath)
        => ValidateInternal(workspacePath, "status", applied: false);

    public TechnicalIntentResult Approve(string workspacePath, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
            return new TechnicalIntentResult("invalid", workspacePath, null, null, null, [], [],
                ["Reviewer and approval reason are required."], false);

        var assessed = Validate(workspacePath);
        if (assessed.Errors.Count > 0 || assessed.Validation is not { Valid: true, Current: true })
            return assessed with { Status = "blocked" };

        var state = ResolveState(workspacePath);
        var path = state.CanonicalPath!;
        var content = File.ReadAllText(path);
        if (string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadNestedFrontMatter(content, "approved_by"), reviewer.Trim(), StringComparison.Ordinal)
            && string.Equals(ReadNestedFrontMatter(content, "approval_reason"), reason.Trim(), StringComparison.Ordinal)
            && assessed.Validation.EffectiveStatus == "Active")
            return assessed with { Status = "unchanged" };

        var now = _clock();
        content = ReplaceOrInsertBaseline(content, state.Baselines);
        content = ReplaceFrontMatter(content, "status", "Active");
        content = ReplaceFrontMatter(content, "last_reviewed", now.ToString("yyyy-MM-dd"));
        content = ReplaceNestedFrontMatter(content, "approved_by", JsonSerializer.Serialize(reviewer.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_at", JsonSerializer.Serialize(now.ToString("O")));
        content = ReplaceNestedFrontMatter(content, "approval_reason", JsonSerializer.Serialize(reason.Trim()));
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", "null");
        content = ReplaceNestedFrontMatter(content, "approved_content_hash", JsonSerializer.Serialize(ContentDigest(content)));
        Write(path, content);

        var stableId = $"{state.Authority!.Id}:spec:technical-intent";
        var catalog = UpdateCatalogStatus(File.ReadAllText(state.Context!.CatalogPath), stableId, "active");
        Write(state.Context.CatalogPath, catalog);
        return ValidateInternal(workspacePath, "approved", applied: true);
    }

    public ChangeReadinessResult Evaluate(string repositoryPath)
    {
        var workspaceMarker = Path.Combine(Path.GetFullPath(repositoryPath), ".cis", "workspace.yml");
        if (!File.Exists(workspaceMarker))
            return new ChangeReadinessResult("technical-intent", Applicable: false, Ready: true, []);

        var result = Status(repositoryPath);
        var ready = result.Validation is { Valid: true, Current: true, EffectiveStatus: "Active" };
        var errors = ready
            ? []
            : result.Errors.Concat(result.Validation?.Errors ?? [])
                .Append("An Active, current workspace technical intent is required. Run `cis technical-intent status`.")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        return new ChangeReadinessResult("technical-intent", Applicable: true, ready, errors);
    }

    private TechnicalIntentResult ValidateInternal(string workspacePath, string operation, bool applied)
    {
        var state = ResolveState(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state);
        var relativePath = NormalizePath(Path.GetRelativePath(state.Authority!.RepositoryPath, state.CanonicalPath!));
        if (!File.Exists(state.CanonicalPath))
            return new TechnicalIntentResult("missing", state.Workspace!.WorkspacePath, state.Authority.Id,
                relativePath, new TechnicalIntentValidation(false, false, "Missing", "Missing",
                    ["Canonical technical intent was not found. Run `cis technical-intent init`."], state.Warnings),
                state.Baselines, state.Warnings, [], false);

        var content = File.ReadAllText(state.CanonicalPath);
        var errors = new List<string>();
        var warnings = new List<string>(state.Warnings);
        var stableId = $"{state.Authority.Id}:spec:technical-intent";
        if (!content.Contains($"stable_id: {stableId}", StringComparison.Ordinal))
            errors.Add("Canonical technical-intent stable identity is missing or incorrect.");
        if (!string.Equals(ReadFrontMatter(content, "scope"), "Workspace", StringComparison.OrdinalIgnoreCase))
            errors.Add("Authority technical intent must declare `scope: Workspace`.");
        if (!string.Equals(ReadNestedFrontMatter(content, "technical_intent_schema"), "1", StringComparison.Ordinal))
            errors.Add("Technical-intent schema metadata is missing or unsupported.");
        if (!content.Contains(BaselineStart, StringComparison.Ordinal) || !content.Contains(BaselineEnd, StringComparison.Ordinal))
            errors.Add("Managed technical-intent baseline is missing. Run `cis technical-intent init`.");

        foreach (var aliases in RequiredSectionAliases)
        {
            var body = aliases.Select(alias => ExtractSection(content, alias)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(body))
                errors.Add($"Required technical-intent section is missing or empty: {string.Join(" or ", aliases)}");
            else if (PlaceholderPattern().IsMatch(body))
                errors.Add($"Required technical-intent section still contains a placeholder: {aliases[0]}");
        }

        var decisionSection = ExtractSection(content, "Open technical decisions");
        foreach (var decision in ParseDecisionRows(decisionSection))
        {
            if (decision.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)
                || decision.Status.Equals("Proposed", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Technical decision remains unresolved: {decision.Id}");
            else if (decision.Status.Equals("Deferred", StringComparison.OrdinalIgnoreCase)
                     && decision.RequiredBefore.Contains("change dossier", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Technical decision cannot be deferred beyond its required gate: {decision.Id}");
            else if (decision.Status is not ("Resolved" or "Accepted" or "Deferred"))
                errors.Add($"Technical decision has unsupported status '{decision.Status}': {decision.Id}");
            if (string.IsNullOrWhiteSpace(decision.Resolution) || PlaceholderPattern().IsMatch(decision.Resolution))
                errors.Add($"Technical decision requires a resolution or deferral rationale: {decision.Id}");
        }

        var recorded = ParseBaselines(content);
        var current = state.ReadinessErrors.Count == 0;
        warnings.AddRange(state.ReadinessErrors);
        foreach (var baseline in state.Baselines)
        {
            var key = baseline.Kind + "\u001f" + baseline.Id;
            if (!recorded.TryGetValue(key, out var version) || version != baseline.Version)
            {
                warnings.Add($"Technical-intent baseline differs from {baseline.Kind} '{baseline.Id}'. Human review is required.");
                current = false;
            }
        }
        foreach (var key in recorded.Keys.Except(state.Baselines.Select(item => item.Kind + "\u001f" + item.Id), StringComparer.Ordinal))
        {
            warnings.Add($"Technical-intent baseline references an unregistered source: {key.Replace("\u001f", "/", StringComparison.Ordinal)}");
            current = false;
        }

        var documentStatus = ReadFrontMatter(content, "status") ?? "Unknown";
        if (documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            var approvedHash = ReadNestedFrontMatter(content, "approved_content_hash");
            if (string.IsNullOrWhiteSpace(approvedHash) || approvedHash == "null" || !ApprovalDigestMatches(content, approvedHash))
            {
                warnings.Add("Technical-intent content changed after approval.");
                current = false;
            }
            if (string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approved_by"))
                || ReadNestedFrontMatter(content, "approved_by") == "null"
                || string.IsNullOrWhiteSpace(ReadNestedFrontMatter(content, "approval_reason"))
                || ReadNestedFrontMatter(content, "approval_reason") == "null")
                errors.Add("Active technical intent requires approval identity and rationale.");
        }

        var valid = errors.Count == 0;
        var effective = documentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? valid && current ? "Active" : "Stale"
            : valid && current ? "Ready for Approval" : "Review Required";
        var validation = new TechnicalIntentValidation(valid, current, effective, documentStatus,
            errors.Distinct(StringComparer.Ordinal).Order().ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray());
        return new TechnicalIntentResult(operation, state.Workspace!.WorkspacePath, state.Authority.Id,
            relativePath, validation, state.Baselines, validation.Warnings, [], applied);
    }

    private State ResolveState(string workspacePath)
    {
        var resolution = _workspaceRegistry.Resolve(workspacePath);
        if (!resolution.IsSuccess || resolution.Workspace is null)
            return new State(null, null, null, null, [], [], resolution.Errors, []);
        var workspace = resolution.Workspace;
        var authority = workspace.AuthorityRepository;
        if (authority is null)
            return new State(workspace, null, null, null, [], [], ["Workspace has no authority repository."], []);
        var contextResolution = _repositoryResolver.Resolve(authority.RepositoryPath);
        if (!contextResolution.IsSuccess || contextResolution.Context is null)
            return new State(workspace, authority, null, null, [], [], contextResolution.Errors, []);

        var warnings = new List<string>();
        var errors = new List<string>();
        var readinessErrors = new List<string>();
        var baselines = new List<TechnicalIntentBaseline>();
        var brdResult = _brd.Status(workspacePath);
        if (brdResult.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            readinessErrors.Add("An Active, current BRD is required before technical intent. Run `cis brd status`.");
        var brdPath = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "business-requirements.md");
        if (File.Exists(brdPath))
            baselines.Add(new TechnicalIntentBaseline("brd", $"{authority.Id}:spec:business-requirements", "semantic-v1:" + BrdDocumentDigest.Compute(File.ReadAllText(brdPath)),
                $"{brdResult.Validation?.EffectiveStatus ?? "Unknown"} canonical BRD"));

        foreach (var repository in workspace.Repositories)
        {
            var snapshot = _graphReader.Read(repository.RepositoryPath);
            if (snapshot.Graph is null)
            {
                if (repository.Role == "participant") readinessErrors.Add($"Repository '{repository.Id}' graph is unavailable. Run `cis graph build --workspace {workspace.WorkspacePath}`.");
                else warnings.Add($"Authority repository graph is unavailable: {repository.Id}.");
                continue;
            }
            if (repository.Role == "participant")
            {
                baselines.Add(new TechnicalIntentBaseline("repository", repository.Id, snapshot.Graph.Build.Id,
                    snapshot.Graph.Build.Head ?? "uncommitted"));
                if (snapshot.Freshness != "fresh") readinessErrors.Add($"Repository '{repository.Id}' graph is {snapshot.Freshness}. Rebuild the workspace.");
            }
            else if (snapshot.Freshness != "fresh")
                warnings.Add($"Authority repository graph is {snapshot.Freshness}; rebuild after canonical edits.");
        }

        var canonical = Path.Combine(authority.RepositoryPath,
            authority.DocumentationRoot.Replace('/', Path.DirectorySeparatorChar), "specs", "technical-intent-spec.md");
        return new State(workspace, authority, contextResolution.Context, canonical,
            baselines.OrderBy(item => item.Kind).ThenBy(item => item.Id).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(), errors.Distinct(StringComparer.Ordinal).ToArray(),
            readinessErrors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string RenderNew(string authorityId, string stableId, IReadOnlyList<TechnicalIntentBaseline> baselines)
        => $"""
---
title: "{authorityId} Technical Intent"
type: specification
status: Draft
scope: Workspace
owner: Product owner and repository maintainers
last_reviewed: null
review_cadence: on architecture or approved-requirement change
cis:
  stable_id: {stableId}
  technical_intent_schema: 1
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# {authorityId} Technical Intent

## CIS technical baseline

{BaselineStart}
{RenderBaselines(baselines)}{BaselineEnd}

## Approved business baseline

TODO: Summarize the active BRD constraints preserved by this intent.

## Design goals and principles

TODO

## Technical surface and ownership

TODO

## Runtime architecture and trust boundaries

TODO

## Application model and invariants

TODO

## API, integration, and compatibility intent

TODO

## Security and privacy intent

TODO

## Operations, migration, and recovery intent

TODO

## Quality attributes and verification direction

TODO

## Decisions and delivery constraints

TODO

## Open technical decisions

| ID | Decision | Required before | Status | Resolution or rationale |
| --- | --- | --- | --- | --- |
| TI-DEC-001 | TODO | Change dossier creation | Open | TODO |
""";

    private static string EnsureMetadata(string content)
    {
        content = EnsureTopLevelFrontMatter(content, "scope", "Workspace", beforeKey: "owner");
        content = EnsureTopLevelFrontMatter(content, "last_reviewed", "null", beforeKey: "review_cadence");
        content = EnsureNestedFrontMatter(content, "technical_intent_schema", "1");
        content = EnsureNestedFrontMatter(content, "approved_by", "null");
        content = EnsureNestedFrontMatter(content, "approved_at", "null");
        content = EnsureNestedFrontMatter(content, "approval_reason", "null");
        content = EnsureNestedFrontMatter(content, "approved_content_hash", "null");
        return content;
    }

    private static string ReplaceOrInsertBaseline(string content, IReadOnlyList<TechnicalIntentBaseline> baselines)
    {
        var rendered = RenderBaselines(baselines).TrimEnd();
        if (content.Contains(BaselineStart, StringComparison.Ordinal) && content.Contains(BaselineEnd, StringComparison.Ordinal))
            return ReplaceBlock(content, BaselineStart, BaselineEnd, rendered);
        var section = $"## CIS technical baseline\n\n{BaselineStart}\n{rendered}\n{BaselineEnd}\n\n";
        var firstSection = content.IndexOf("\n## ", StringComparison.Ordinal);
        return firstSection < 0 ? content.TrimEnd() + "\n\n" + section : content.Insert(firstSection + 1, section);
    }

    private static string RenderBaselines(IReadOnlyList<TechnicalIntentBaseline> baselines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Kind | ID | Version | Evidence |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var item in baselines)
            builder.AppendLine($"| {Cell(item.Kind)} | {Cell(item.Id)} | {Cell(item.Version)} | {Cell(item.Evidence)} |");
        return builder.ToString();
    }

    private static Dictionary<string, string> ParseBaselines(string content)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in ReadBlock(content, BaselineStart, BaselineEnd).Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length == 4 && cells[0] is not ("Kind" or "---")) rows[cells[0] + "\u001f" + cells[1]] = cells[2];
        }
        return rows;
    }

    private static IReadOnlyList<DecisionRow> ParseDecisionRows(string content)
    {
        var rows = new List<DecisionRow>();
        foreach (var line in content.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Length == 5 && cells[0].StartsWith("TI-DEC-", StringComparison.Ordinal))
                rows.Add(new DecisionRow(cells[0], cells[2], cells[3], cells[4]));
        }
        return rows;
    }

    private static string[] Cells(string line)
        => !line.TrimStart().StartsWith('|') ? [] : line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();

    private static string ExtractSection(string content, string heading)
    {
        var match = Regex.Match(content, $"(?ms)^## {Regex.Escape(heading)}\\s*$\\n(?<body>.*?)(?=^## |\\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
    }

    private static string EnsureTopLevelFrontMatter(string content, string key, string value, string beforeKey)
    {
        if (ReadFrontMatter(content, key) is not null) return ReplaceFrontMatter(content, key, value);
        return Regex.Replace(content, $"(?m)^{Regex.Escape(beforeKey)}:", $"{key}: {value}\n{beforeKey}:",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string EnsureNestedFrontMatter(string content, string key, string value)
    {
        if (ReadNestedFrontMatter(content, key) is not null) return content;
        return Regex.Replace(content, "(?m)^(  stable_id:.*)$", $"$1\n  {key}: {value}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string ReplaceFrontMatter(string content, string key, string value)
        => Regex.Replace(content, $"(?m)^{Regex.Escape(key)}:.*$", $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string ReplaceNestedFrontMatter(string content, string key, string value)
        => Regex.Replace(content, $"(?m)^  {Regex.Escape(key)}:.*$", $"  {key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? ReadFrontMatter(string content, string key) => ReadFrontMatterValue(content, $"^{Regex.Escape(key)}:");
    private static string? ReadNestedFrontMatter(string content, string key) => ReadFrontMatterValue(content, $"^  {Regex.Escape(key)}:");

    private static string? ReadFrontMatterValue(string content, string prefix)
    {
        var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal) || end < 0) return null;
        var match = Regex.Match(content[..end], $"(?m){prefix}\\s*(?<value>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? Unquote(match.Groups["value"].Value.Trim()) : null;
    }

    private static string Unquote(string value)
    {
        if (!value.StartsWith('"')) return value;
        try { return JsonSerializer.Deserialize<string>(value) ?? string.Empty; }
        catch (JsonException) { return value.Trim('"'); }
    }

    private static string ReplaceBlock(string content, string start, string end, string replacement)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex) return content;
        var replacementStart = startIndex + start.Length;
        return content[..replacementStart] + "\n" + replacement.TrimEnd() + "\n" + content[endIndex..];
    }

    private static string ReadBlock(string content, string start, string end)
    {
        var startIndex = content.IndexOf(start, StringComparison.Ordinal);
        var endIndex = content.IndexOf(end, StringComparison.Ordinal);
        return startIndex < 0 || endIndex < startIndex ? string.Empty : content[(startIndex + start.Length)..endIndex].Trim();
    }

    private static string ContentDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        normalized = RemoveManagedBlock(normalized, BaselineStart, BaselineEnd);
        return Hash(normalized.TrimEnd());
    }

    private static string LegacyContentDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
            normalized = Regex.Replace(normalized, $"(?m)^{key}:.*$", $"{key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            normalized = Regex.Replace(normalized, $"(?m)^  {key}:.*$", $"  {key}: <approval-metadata>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Hash(normalized.TrimEnd());
    }

    private static bool ApprovalDigestMatches(string content, string approvedHash)
        => approvedHash == ContentDigest(content) || approvedHash == LegacyContentDigest(content);

    private static bool HasCurrentApproval(string content)
        => string.Equals(ReadFrontMatter(content, "status"), "Active", StringComparison.OrdinalIgnoreCase)
           && ReadNestedFrontMatter(content, "approved_by") is { Length: > 0 } reviewer && reviewer != "null"
           && ReadNestedFrontMatter(content, "approval_reason") is { Length: > 0 } reason && reason != "null"
           && ReadNestedFrontMatter(content, "approved_content_hash") is { Length: > 0 } digest && digest != "null"
           && ApprovalDigestMatches(content, digest);

    private static bool BaselinesCanCarryForward(string content, IReadOnlyList<TechnicalIntentBaseline> baselines)
    {
        var recorded = ParseBaselines(content);
        if (recorded.Count != baselines.Count) return false;
        foreach (var baseline in baselines)
        {
            var key = baseline.Kind + "\u001f" + baseline.Id;
            if (!recorded.ContainsKey(key)) return false;
        }
        return true;
    }

    private static string RemoveManagedBlock(string content, string start, string end)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var startIndex = normalized.IndexOf(start, StringComparison.Ordinal);
        var endIndex = normalized.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < startIndex) return normalized;
        var after = endIndex + end.Length;
        while (after < normalized.Length && normalized[after] == '\n') after++;
        return normalized[..startIndex] + normalized[after..];
    }

    private static string Hash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    private static bool Equivalent(string left, string right) => left.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == right.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private static void Write(string path, string content) => File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));

    private static string UpdateCatalogStatus(string catalog, string id, string status)
    {
        var lines = catalog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim() != $"- id: {id}") continue;
            for (var entry = index + 1; entry < lines.Length && !lines[entry].StartsWith("  - id:", StringComparison.Ordinal); entry++)
                if (lines[entry].TrimStart().StartsWith("status:", StringComparison.Ordinal))
                {
                    lines[entry] = "    status: " + status;
                    return string.Join('\n', lines);
                }
        }
        return catalog;
    }

    private static TechnicalIntentResult Error(string status, State state, params string[] errors)
        => new(status, state.Workspace?.WorkspacePath, state.Authority?.Id,
            state.CanonicalPath is null || state.Authority is null ? null : NormalizePath(Path.GetRelativePath(state.Authority.RepositoryPath, state.CanonicalPath)),
            null, state.Baselines, state.Warnings, errors.Length == 0 ? state.Errors : errors, false);

    [GeneratedRegex("\\b(?:TODO|TBD)\\b|(?i:\\bTO BE COMPLETED\\b)", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    private sealed record State(CisWorkspace? Workspace, CisWorkspaceRepository? Authority,
        CisRepositoryContext? Context, string? CanonicalPath, IReadOnlyList<TechnicalIntentBaseline> Baselines,
        IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, IReadOnlyList<string> ReadinessErrors);

    private sealed record DecisionRow(string Id, string RequiredBefore, string Status, string Resolution);
}
