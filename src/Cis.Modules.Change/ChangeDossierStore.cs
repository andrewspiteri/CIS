using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Change;

public sealed partial class ChangeDossierStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IReadOnlyList<IChangeReadinessCheck> _readinessChecks;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;

    public ChangeDossierStore(
        ICisRepositoryContextResolver resolver,
        Func<DateTimeOffset>? clock = null,
        IEnumerable<IChangeReadinessCheck>? readinessChecks = null,
        ICisWorkspaceRegistry? workspaceRegistry = null)
    {
        _resolver = resolver;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _readinessChecks = (readinessChecks ?? []).ToArray();
        _workspaceRegistry = workspaceRegistry;
    }

    public ChangeResult Create(ChangeCreateRequest request)
    {
        var resolution = _resolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return Failure(resolution.Errors);
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Outcome))
        {
            return Failure(["Title and outcome are required."]);
        }

        var readinessErrors = _readinessChecks
            .Select(check => check.Evaluate(resolution.Context.RepositoryPath))
            .Where(result => result.Applicable && !result.Ready)
            .SelectMany(result => result.Errors.Select(error => $"[{result.Check}] {error}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (readinessErrors.Length > 0)
        {
            return Failure(readinessErrors);
        }

        var context = resolution.Context;
        var manifest = ReadGraphManifest(context.RepositoryPath);
        if (manifest is null)
        {
            return Failure(["A built context graph is required. Run `cis graph build` first."]);
        }

        var changesPath = Path.Combine(context.DocumentationPath, "changes");
        var id = string.IsNullOrWhiteSpace(request.RequestedId)
            ? NextId(changesPath)
            : NormalizeRequestedId(request.RequestedId);
        if (id is null)
        {
            return Failure(["The requested change ID must match CIS-0001."]);
        }

        var dossierPath = Path.Combine(changesPath, id);
        if (Directory.Exists(dossierPath))
        {
            return Failure([$"Change dossier already exists: {id}"]);
        }

        var baselineKind = string.IsNullOrWhiteSpace(manifest.Head) ? "graph" : "git";
        var baseline = manifest.Head ?? manifest.BuildId;
        var relativePath = NormalizePath(Path.GetRelativePath(context.RepositoryPath, dossierPath));
        var roots = request.Roots
            .Where(root => !string.IsNullOrWhiteSpace(root.Id))
            .DistinctBy(root => $"{root.Id}\u001f{root.Kind}", StringComparer.Ordinal)
            .ToArray();
        var repositoryBaselines = CaptureRepositoryBaselines(context, baselineKind, baseline);
        var change = new ChangeDossier(
            id,
            request.Title.Trim(),
            request.Outcome.Trim(),
            "Proposed",
            baselineKind,
            baseline,
            manifest.BuildId,
            context.RepositoryPath,
            context.DocumentationRoot,
            relativePath,
            roots,
            repositoryBaselines);

        Directory.CreateDirectory(dossierPath);
        Directory.CreateDirectory(Path.Combine(dossierPath, "agent-tasks"));
        Directory.CreateDirectory(Path.Combine(dossierPath, "assets"));
        WriteNew(Path.Combine(dossierPath, "proposal.md"), RenderProposal(change));
        WriteNew(Path.Combine(dossierPath, "impact.md"), RenderEmptyImpact(change));
        WriteNew(Path.Combine(dossierPath, "decisions.md"), RenderEmptyDecisions(change));
        WriteNew(Path.Combine(dossierPath, "plan.md"), RenderEmptyPlan(change));
        WriteNew(Path.Combine(dossierPath, "wireframes.md"), RenderEmptyWireframes(change));
        WriteNew(Path.Combine(dossierPath, "design.md"), RenderEmptyDesign(change));
        WriteNew(Path.Combine(dossierPath, "test-cases.md"), RenderEmptyManualTestCases(change));
        WriteNew(Path.Combine(dossierPath, "test-cases.csv"), RenderEmptyManualTestCasesCsv());
        WriteNew(Path.Combine(dossierPath, "verification.md"), RenderEmptyVerification(change));
        WriteNew(Path.Combine(dossierPath, "events.jsonl"), string.Empty);
        AppendEvent(change, "change-created", new Dictionary<string, string>
        {
            ["title"] = change.Title,
            ["baseline"] = change.Baseline,
            ["graphBuildId"] = change.GraphBuildId,
        });
        RegisterCatalogEntries(context, change);
        return new ChangeResult("created", change, [change], [], Applied: true);
    }

    public ChangeResult List(string repositoryPath)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return Failure(resolution.Errors);
        }

        var changesPath = Path.Combine(resolution.Context.DocumentationPath, "changes");
        var changes = Directory.Exists(changesPath)
            ? Directory.EnumerateDirectories(changesPath, "CIS-*", SearchOption.TopDirectoryOnly)
                .Select(path => Read(repositoryPath, Path.GetFileName(path)))
                .Where(change => change is not null)
                .Cast<ChangeDossier>()
                .OrderBy(change => change.Id, StringComparer.Ordinal)
                .ToArray()
            : [];
        return new ChangeResult("listed", null, changes, [], Applied: false);
    }

    public ChangeDossier? Read(string repositoryPath, string changeId)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null || !ChangeIdPattern().IsMatch(changeId))
        {
            return null;
        }

        var proposalPath = Path.Combine(
            resolution.Context.DocumentationPath,
            "changes",
            changeId,
            "proposal.md");
        if (!File.Exists(proposalPath))
        {
            return null;
        }

        var metadata = ReadFrontMatter(proposalPath);
        if (!metadata.TryGetValue("change_id", out var id)
            || !metadata.TryGetValue("title", out var title)
            || !metadata.TryGetValue("outcome", out var outcome)
            || !metadata.TryGetValue("status", out var status)
            || !metadata.TryGetValue("baseline_kind", out var baselineKind)
            || !metadata.TryGetValue("baseline", out var baseline)
            || !metadata.TryGetValue("graph_build_id", out var graphBuildId))
        {
            return null;
        }

        var roots = Array.Empty<ChangeRoot>();
        if (metadata.TryGetValue("impact_roots", out var rootsJson))
        {
            try
            {
                roots = JsonSerializer.Deserialize<ChangeRoot[]>(rootsJson, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var repositoryBaselines = Array.Empty<ChangeRepositoryBaseline>();
        if (metadata.TryGetValue("repository_baselines", out var baselinesJson))
        {
            try
            {
                repositoryBaselines = JsonSerializer.Deserialize<ChangeRepositoryBaseline[]>(baselinesJson, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var dossierPath = Path.GetDirectoryName(proposalPath)!;
        return new ChangeDossier(
            id,
            title,
            outcome,
            status,
            baselineKind,
            baseline,
            graphBuildId,
            resolution.Context.RepositoryPath,
            resolution.Context.DocumentationRoot,
            NormalizePath(Path.GetRelativePath(resolution.Context.RepositoryPath, dossierPath)),
            roots,
            repositoryBaselines);
    }

    public ChangeResult Close(string repositoryPath, string changeId)
    {
        var change = Read(repositoryPath, changeId);
        if (change is null)
        {
            return Failure([$"Change dossier was not found or is invalid: {changeId}"]);
        }

        if (string.Equals(change.Status, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            return new ChangeResult("unchanged", change, [change], [], Applied: false);
        }

        var proposalPath = AbsoluteDossierFile(change, "proposal.md");
        var content = File.ReadAllText(proposalPath);
        File.WriteAllText(proposalPath, ReplaceFrontMatterValue(content, "status", "Closed"));
        var closed = change with { Status = "Closed" };
        AppendEvent(closed, "change-closed", new Dictionary<string, string>());
        return new ChangeResult("closed", closed, [closed], [], Applied: true);
    }

    public ChangeResult Rebaseline(ChangeRebaselineRequest request)
    {
        var change = Read(request.RepositoryPath, request.ChangeId);
        if (change is null)
            return Failure([$"Change dossier was not found or is invalid: {request.ChangeId}"]);
        if (string.Equals(change.Status, "Closed", StringComparison.OrdinalIgnoreCase))
            return Failure(["A closed change cannot be rebaselined."]);
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
            return Failure(["Actor and reason are required for an audited rebaseline."]);

        var impactPath = AbsoluteDossierFile(change, "impact.md");
        var planPath = AbsoluteDossierFile(change, "plan.md");
        var impact = File.Exists(impactPath) ? File.ReadAllText(impactPath) : string.Empty;
        var plan = File.Exists(planPath) ? File.ReadAllText(planPath) : string.Empty;
        if (Regex.IsMatch(impact, @"(?m)^\|\s*IMPACT-[^|]+\|", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))
            || Regex.IsMatch(plan, @"(?m)^\|\s*WORK-\d+\s*\|", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            return Failure([
                "Safe rebaseline is limited to a dossier with no impact findings or generated work items. " +
                "Preserve reviewed evidence by creating a new change or explicitly revising scope instead.",
            ]);

        var manifest = ReadGraphManifest(change.RepositoryPath);
        if (manifest is null)
            return Failure(["A built context graph is required. Run `cis graph build` first."]);
        var baselineKind = string.IsNullOrWhiteSpace(manifest.Head) ? "graph" : "git";
        var baseline = manifest.Head ?? manifest.BuildId;
        var repositoryContext = _resolver.Resolve(change.RepositoryPath).Context!;
        var repositoryBaselines = CaptureRepositoryBaselines(repositoryContext, baselineKind, baseline);
        if (string.Equals(change.BaselineKind, baselineKind, StringComparison.Ordinal)
            && string.Equals(change.Baseline, baseline, StringComparison.Ordinal)
            && string.Equals(change.GraphBuildId, manifest.BuildId, StringComparison.Ordinal))
            return new ChangeResult("unchanged", change, [change], [], Applied: false);

        var proposalPath = AbsoluteDossierFile(change, "proposal.md");
        var proposal = File.ReadAllText(proposalPath);
        proposal = ReplaceFrontMatterValue(proposal, "baseline_kind", baselineKind);
        proposal = ReplaceFrontMatterValue(proposal, "baseline", baseline);
        proposal = ReplaceFrontMatterValue(proposal, "graph_build_id", manifest.BuildId);
        proposal = ReplaceFrontMatterValue(
            proposal,
            "repository_baselines",
            JsonSerializer.Serialize(repositoryBaselines));
        File.WriteAllText(proposalPath, proposal);
        if (File.Exists(impactPath))
            File.WriteAllText(impactPath, ReplaceFrontMatterValue(impact, "graph_build_id", manifest.BuildId));
        if (File.Exists(planPath))
            File.WriteAllText(planPath, ReplaceFrontMatterValue(plan, "graph_build_id", manifest.BuildId));

        var updated = change with
        {
            BaselineKind = baselineKind,
            Baseline = baseline,
            GraphBuildId = manifest.BuildId,
            RepositoryBaselines = repositoryBaselines,
        };
        AppendEvent(updated, "change-rebaselined", new Dictionary<string, string>
        {
            ["actor"] = request.Actor.Trim(),
            ["reason"] = request.Reason.Trim(),
            ["previousBaseline"] = change.Baseline,
            ["previousGraphBuildId"] = change.GraphBuildId,
            ["baseline"] = baseline,
            ["graphBuildId"] = manifest.BuildId,
        });
        return new ChangeResult("rebaselined", updated, [updated], [], Applied: true);
    }

    public string DossierFile(ChangeDossier change, string fileName)
        => AbsoluteDossierFile(change, fileName);

    public void AppendEvent(ChangeDossier change, string eventType, IReadOnlyDictionary<string, string> data)
    {
        var eventPath = AbsoluteDossierFile(change, "events.jsonl");
        var item = new
        {
            timestamp = _clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            changeId = change.Id,
            type = eventType,
            data,
        };
        File.AppendAllText(eventPath, JsonSerializer.Serialize(item) + Environment.NewLine);
    }

    private static CisGraphManifest? ReadGraphManifest(string repositoryPath)
    {
        var manifestPath = Path.Combine(repositoryPath, ".cis", "local", "graph", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CisGraphManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeRequestedId(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var value = requested.Trim().ToUpperInvariant();
        return ChangeIdPattern().IsMatch(value) ? value : null;
    }

    private static string NextId(string changesPath)
    {
        var maximum = Directory.Exists(changesPath)
            ? Directory.EnumerateDirectories(changesPath, "CIS-*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null && ChangeIdPattern().IsMatch(name))
                .Select(name => int.Parse(name![4..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .Max()
            : 0;
        return $"CIS-{maximum + 1:0000}";
    }

    private static string RenderProposal(ChangeDossier change)
    {
        var rootsJson = JsonSerializer.Serialize(change.Roots);
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {Yaml(change.Title)}");
        builder.AppendLine("type: change-proposal");
        builder.AppendLine($"status: {change.Status}");
        builder.AppendLine($"change_id: {change.Id}");
        builder.AppendLine($"outcome: {Yaml(change.Outcome)}");
        builder.AppendLine($"baseline_kind: {change.BaselineKind}");
        builder.AppendLine($"baseline: {Yaml(change.Baseline)}");
        builder.AppendLine($"graph_build_id: {Yaml(change.GraphBuildId)}");
        builder.AppendLine($"impact_roots: {Yaml(rootsJson)}");
        builder.AppendLine($"repository_baselines: {Yaml(JsonSerializer.Serialize(change.RepositoryBaselines ?? []))}");
        builder.AppendLine("authority: human-reviewed");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {change.Id}: {change.Title}");
        builder.AppendLine();
        builder.AppendLine("## Outcome");
        builder.AppendLine();
        builder.AppendLine(change.Outcome);
        builder.AppendLine();
        builder.AppendLine("## Impact roots");
        builder.AppendLine();
        if (change.Roots.Count == 0)
        {
            builder.AppendLine("No roots declared. Supply roots to `cis impact analyse`.");
        }
        else
        {
            foreach (var root in change.Roots)
            {
                builder.AppendLine($"- `{root.Id}` ({root.Kind ?? "any kind"})");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Constraints");
        builder.AppendLine();
        builder.AppendLine("- Human authority is required for impact disposition and plan approval; `cis plan derive` may carry forward an exact current feature approval without requesting it again.");
        builder.AppendLine("- Scope expansion must be recorded as a new finding or decision.");
        builder.AppendLine();
        builder.AppendLine("## Acceptance criteria");
        builder.AppendLine();
        builder.AppendLine("- TODO: define outcome-level acceptance criteria before plan approval.");
        return builder.ToString();
    }

    private IReadOnlyList<ChangeRepositoryBaseline> CaptureRepositoryBaselines(
        CisRepositoryContext context,
        string authorityBaselineKind,
        string authorityBaseline)
    {
        var repositories = _workspaceRegistry?.Resolve(context.RepositoryPath).Workspace?.Repositories
            ?? [new CisWorkspaceRepository(context.RepositoryId, context.RepositoryPath, context.DocumentationRoot, "authority")];
        var baselines = new List<ChangeRepositoryBaseline>();
        foreach (var repository in repositories.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var head = ReadGitHead(repository.RepositoryPath);
            if (head is not null)
            {
                baselines.Add(new ChangeRepositoryBaseline(repository.Id, "git", head));
                continue;
            }

            if (string.Equals(repository.Id, context.RepositoryId, StringComparison.Ordinal))
                baselines.Add(new ChangeRepositoryBaseline(repository.Id, authorityBaselineKind, authorityBaseline));
        }

        return baselines;
    }

    private static string? ReadGitHead(string repositoryPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = repositoryPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("rev-parse");
            process.StartInfo.ArgumentList.Add("HEAD");
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string RenderEmptyImpact(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " impact analysis")}
type: impact-analysis
status: Draft
change_id: {change.Id}
graph_build_id: {Yaml(change.GraphBuildId)}
authority: human-reviewed
---

# Impact analysis

## Findings

| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |

## Coverage

Run `cis impact completeness {change.Id}` for the current coverage result.
""";

    private static string RenderEmptyDecisions(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " decisions")}
type: change-decisions
status: Draft
change_id: {change.Id}
authority: human-reviewed
---

# Decisions

| ID | Category | Question | Blocking | Required before | Status | Options | Resolution | Rationale | Evidence | Promoted ADR |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
""";

    private static string RenderEmptyPlan(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " delivery plan")}
type: delivery-plan
status: NotBuilt
change_id: {change.Id}
graph_build_id: {Yaml(change.GraphBuildId)}
authority: human-approved
---

# Delivery plan

Run `cis plan build {change.Id}` after reviewing impact findings.
""";

    private static string RenderEmptyVerification(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " verification evidence")}
type: verification-record
status: Draft
change_id: {change.Id}
authority: human-reviewed
---

# Verification evidence

Record exact commands, artifacts, results, blockers, deferrals, coverage, and
independent-assurance findings as agent tasks are completed.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
""";

    private static string RenderEmptyManualTestCases(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " manual test cases")}
type: manual-test-cases
status: NotGenerated
change_id: {change.Id}
test_case_count: 0
automated_test_case_count: 0
automation_pending_count: 0
csv_path: test-cases.csv
generation: deterministic
authority: derived
---

# Manual test cases

Run `cis plan import-spec {change.Id} --file <feature-specification>` or
`cis plan derive {change.Id} --file <approved-feature-specification>` to generate
the human-readable catalogue and synchronized CSV import projection.
""";

    private static string RenderEmptyManualTestCasesCsv()
        => "\"ID\",\"Title\",\"Section\",\"Priority\",\"Type\",\"Preconditions\",\"Steps\",\"Expected Result\",\"References\",\"Frontend Type\",\"Automation Status\",\"Automated Test References\"\r\n";

    private static string RenderEmptyWireframes(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " textual wireframes")}
type: textual-wireframes
status: Draft
change_id: {change.Id}
approval_status: NotReviewed
authority: human-reviewed
---

# Textual wireframes

Use one screen section per screen or materially different state. Every action must
record its availability, result, destination path, destination screen, and denied or
failure behavior. Visual design remains blocked until this behavioral/navigation
contract is approved.

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |

## Screen definitions

### TODO-SCREEN-ID: TODO screen name

#### Description

TODO: Describe shell/navigation context, visible regions, fields, controls, states,
responsive behavior, and accessibility intent in reading order.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |

#### States

- [ ] Primary/populated
- [ ] Empty
- [ ] Loading/pending
- [ ] Validation failure
- [ ] Error and recovery
- [ ] Permission denied
- [ ] Lifecycle/read-only/archived
- [ ] Destructive confirmation/result
- [ ] Responsive/platform variants
- [ ] Accessibility behavior

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States | Notes |
| --- | --- | --- | --- | --- |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Not reviewed | TODO | TODO | TODO | TODO |
""";

    private static string RenderEmptyDesign(ChangeDossier change)
        => $"""
---
title: {Yaml(change.Id + " design approval")}
type: design-approval
status: Draft
change_id: {change.Id}
approval_status: NotReviewed
gate_status: Inactive
authority: human-reviewed
---

# Design approval

For UI-bearing work, a completed renderer and PNG pack set `gate_status` to
`PausedForReview`. All non-review work stops until an explicit approval sets the gate
to `Approved`. Rejection keeps the global pause active and permits only wireframe and
design revision.

## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |

## Application shell and component templates

## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |

## Required states

- [ ] Primary flow
- [ ] Empty state
- [ ] Loading state
- [ ] Error state
- [ ] Permission-denied state
- [ ] Responsive states
- [ ] Relevant workflow and lifecycle states
- [ ] Accessibility intent

## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |

## Guideline conformance and deviations

| Rule/token | Renderer mapping | Result | Deviation approval |
| --- | --- | --- | --- |

## Rejected revisions

Rejected PNG files are removed. Preserve their manifest hashes and review evidence.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Not reviewed | TODO | TODO | TODO | TODO | TODO | TODO |
""";

    private static void RegisterCatalogEntries(CisRepositoryContext context, ChangeDossier change)
    {
        var catalog = File.ReadAllText(context.CatalogPath);
        var additions = new StringBuilder();
        foreach (var (file, type, suffix) in new[]
        {
            ("proposal.md", "change-proposal", "proposal"),
            ("impact.md", "impact-analysis", "impact"),
            ("decisions.md", "change-decisions", "decisions"),
            ("plan.md", "delivery-plan", "plan"),
            ("wireframes.md", "textual-wireframes", "wireframes"),
            ("design.md", "design-approval", "design"),
            ("test-cases.md", "manual-test-cases", "manual-test-cases"),
            ("verification.md", "verification-record", "verification"),
        })
        {
            var stableId = $"{context.RepositoryId}:change:{change.Id.ToLowerInvariant()}:{suffix}";
            if (catalog.Contains($"id: {stableId}", StringComparison.Ordinal))
            {
                continue;
            }

            additions.AppendLine($"  - id: {stableId}");
            additions.AppendLine($"    path: {change.RelativePath}/{file}");
            additions.AppendLine($"    type: {type}");
            additions.AppendLine("    status: draft");
            additions.AppendLine("    authority: canonical");
        }

        if (additions.Length > 0)
        {
            var separator = catalog.EndsWith('\n') ? string.Empty : Environment.NewLine;
            File.WriteAllText(context.CatalogPath, catalog + separator + additions);
        }
    }

    private static Dictionary<string, string> ReadFrontMatter(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != "---")
        {
            return values;
        }

        foreach (var line in lines.Skip(1).TakeWhile(line => line != "---"))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var raw = line[(separator + 1)..].Trim();
            values[key] = Unquote(raw);
        }

        return values;
    }

    private static string ReplaceFrontMatterValue(string content, string key, string value)
        => Regex.Replace(
            content,
            $"(?m)^{Regex.Escape(key)}:.*$",
            $"{key}: {Yaml(value)}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static string AbsoluteDossierFile(ChangeDossier change, string fileName)
        => Path.Combine(
            change.RepositoryPath,
            change.RelativePath.Replace('/', Path.DirectorySeparatorChar),
            fileName);

    private static string Yaml(string value) => JsonSerializer.Serialize(value);

    private static string Unquote(string value)
    {
        if (value.StartsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
            }
            catch (JsonException)
            {
                return value;
            }
        }

        return value;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static ChangeResult Failure(IReadOnlyList<string> errors)
        => new("invalid", null, [], errors, Applied: false);

    [GeneratedRegex("^CIS-[0-9]{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChangeIdPattern();
}
