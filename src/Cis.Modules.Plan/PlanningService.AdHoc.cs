using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Docs;
using Cis.Modules.Impact;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private sealed record RetiredAdHocTask(PlanWorkItem Item, string RetiredPath, string Content);

    private PlanResult BuildAdHocPlan(ChangeDossier change, IReadOnlyList<PlanWorkItem> previous,
        IReadOnlyList<ImpactFinding> accepted)
    {
        var applied = false;
        // Decide every binding before writing. Existing Markdown remains the authored contract;
        // the input digest detects a changed impact, not permission to replace that contract.
        try
        {
            if (accepted.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != accepted.Count
                || previous.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != previous.Count
                || previous.Any(item => item.ImpactIds.Count != 1)
                || previous.SelectMany(item => item.ImpactIds).Distinct(StringComparer.Ordinal).Count() != previous.Count)
                throw new InvalidDataException("Ad hoc impact/task ownership is ambiguous. Reconcile the retained task identities before rebuilding; no identity was reassigned.");

            var dossier = Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!;
            var reserved = previous.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ReserveCatalogTaskIdentities(change, reserved);
            var retired = ReadRetiredAdHocTasks(change, previous, reserved);
            var nextNumber = reserved.Select(id => Regex.Match(id, @"^WORK-(\d+)(?:-|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                .Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0).Max();
            var seeds = BuildWorkItems(accepted);
            var selected = new List<PlanWorkItem>();
            var documents = new Dictionary<string, string>(StringComparer.Ordinal);
            var newDocuments = new HashSet<string>(StringComparer.Ordinal);
            var restorations = new Dictionary<string, RetiredAdHocTask>(StringComparer.Ordinal);
            var temporaryIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var seed in seeds)
            {
                var finding = accepted.Single(item => item.Id == seed.ImpactIds[0]);
                var retained = previous.SingleOrDefault(item => item.ImpactIds[0] == finding.Id);
                var archived = retired.SingleOrDefault(item => item.Item.ImpactIds[0] == finding.Id);
                if (retained is not null && archived is not null)
                    throw new InvalidDataException($"Impact {finding.Id} has both active and retired owners. Reconcile the exact retained identity before rebuilding.");
                var item = retained ?? archived?.Item;
                if (item is null)
                {
                    string id;
                    do { id = $"WORK-{checked(++nextNumber):000}"; } while (!reserved.Add(id));
                    item = seed with { Id = id, TaskPath = $"agent-tasks/{id}.md", Status = "Draft", InstanceKey = "ad-hoc-impact:" + finding.Id };
                    item = item with { VerificationObligations = DefaultVerificationObligations(change.RepositoryPath, item) };
                }
                else if (item.TaskPath is null)
                {
                    // A matching ID/title cannot prove the old target/evidence. Do not
                    // stamp today's basis onto an unproved historical task contract.
                    throw new InvalidDataException($"Legacy task {item.Id} has no durable task document/input basis for {finding.Id}. Preserve its recorded identity and row contract; restore a verified retained contract/binding or reconcile it through the existing governed change/impact route before materializing it.");
                }
                if (archived is not null)
                {
                    item = item with { Status = "Draft" };
                    restorations.Add(item.Id, archived);
                }
                temporaryIds.Add(seed.Id, item.Id);
                selected.Add(item);
            }

            for (var index = 0; index < selected.Count; index++)
            {
                var item = selected[index];
                var seed = seeds[index];
                var prerequisites = seed.DependsOn.Select(id => temporaryIds[id]).ToArray();
                if (!previous.Any(prior => prior.Id == item.Id) && !restorations.ContainsKey(item.Id))
                    item = item with { DependsOn = prerequisites };
                else if (prerequisites.Except(item.DependsOn, StringComparer.Ordinal).Any())
                    throw new InvalidDataException($"Task {item.Id} requires reconciliation of its retained dependencies: accepted work now adds a prerequisite. Preserve its authored contract and reconcile the required ordering before rebuilding.");
                if (item.DependsOn.Any(id => !selected.Any(candidate => candidate.Id == id)))
                    throw new InvalidDataException($"Task {item.Id} retains a dependency on removed work. Reconcile its authored dependencies before retiring that work.");
                selected[index] = item;
                var typeErrors = AdHocTaskTypeErrors(change.RepositoryPath, item);
                if (typeErrors.Count > 0) throw new InvalidDataException(string.Join("; ", typeErrors));
                var finding = accepted.Single(candidate => candidate.Id == item.ImpactIds[0]);
                var path = SafeTaskPath(dossier, item.TaskPath!);
                if (path is null || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, path))
                    throw new InvalidDataException($"Task {item.Id} requires an unlinked path inside agent-tasks.");
                var archived = restorations.GetValueOrDefault(item.Id);
                if (archived is not null && File.Exists(path))
                    throw new InvalidDataException($"Task {item.Id} cannot reactivate over an existing document. Reconcile both retained paths.");
                var existing = archived?.Content ?? (File.Exists(path) ? ReadTaskContract(change, item) : null);
                if (existing is not null)
                {
                    var errors = new List<string>();
                    ValidateTaskDocument(change, archived is null ? item : item with { TaskPath = archived.RetiredPath }, errors, false);
                    if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));
                    if (item.Category != finding.Category || TaskMetadata<string>(existing, "category") != item.Category
                        || item.InstanceKey != "ad-hoc-impact:" + finding.Id
                        || TaskMetadata<string>(existing, "ad_hoc_impact_id") != finding.Id
                        || TaskMetadata<string>(existing, "ad_hoc_impact_basis") != AdHocImpactBasis(finding)
                        || TaskMetadata<string>(existing, "ad_hoc_task_path") != item.TaskPath)
                        throw new InvalidDataException(AdHocReconciliationMessage(item.Id, finding.Id));
                    // Do not regenerate even the objective, required changes or unchecked criteria.
                    if (archived is not null)
                        documents.Add(item.Id, ReplaceFrontMatter(ReplaceFrontMatter(existing, "status", "Draft"), "task_status", "Draft"));
                }
                else
                {
                    if (previous.Any(prior => prior.Id == item.Id && prior.TaskPath is not null))
                        throw new InvalidDataException($"Task {item.Id} durable task document is missing. Restore its authored contract before rebuilding.");
                    item = item with
                    {
                        DecisionIds = item.DecisionIds ?? [],
                        RequiredOutputs = item.RequiredOutputs is { Count: > 0 } ? item.RequiredOutputs : ["The bounded change and reproducible evidence for the accepted impact."],
                        NegativeCriteria = item.NegativeCriteria is { Count: > 0 } ? item.NegativeCriteria : ["Do not expand the accepted change/impact scope or bypass product, plan, design, allocation or completion authority."]
                    };
                    selected[index] = item;
                    documents.Add(item.Id, RenderAdHocTask(change, item, finding));
                    newDocuments.Add(item.Id);
                }
            }
            if (previous.Any(item => item.TaskPath is null && selected.All(current => current.Id != item.Id)))
                throw new InvalidDataException("Removed legacy row-only work must be reconciled/materialized before retirement; its identity and prior contract remain in the unchanged plan.");
            foreach (var obsolete in previous.Where(item => selected.All(current => current.Id != item.Id)))
            {
                var errors = new List<string>();
                ValidateTaskDocument(change, obsolete, errors, false);
                if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));
                var retiredRelative = "agent-tasks/retired/" + NormalizePath(obsolete.TaskPath!)["agent-tasks/".Length..];
                var retiredPath = SafeTaskPath(dossier, retiredRelative);
                if (retiredPath is null || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, retiredPath))
                    throw new InvalidDataException($"Task {obsolete.Id} has an unsafe retirement destination. Preserve the contract and restore an unlinked in-dossier retired directory before rebuilding.");
            }

            foreach (var item in selected)
            {
                if (restorations.TryGetValue(item.Id, out var archived))
                {
                    var source = Path.Combine(dossier, archived.RetiredPath);
                    var destination = Path.Combine(dossier, item.TaskPath!);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (CisPathSafety.ContainsReparsePoint(change.RepositoryPath, source)
                        || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, destination))
                        throw new InvalidDataException($"Task {item.Id} reactivation path changed after inspection; preserve its retained document.");
                    // Verify under the ordinary no-write sharing guard and keep that handle
                    // through rename/stamping, rather than rewriting the path from a snapshot.
                    using (var file = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete))
                    {
                        using (var reader = new StreamReader(file, Encoding.UTF8, true, 1024, leaveOpen: true))
                            if (reader.ReadToEnd() != archived.Content)
                                throw new InvalidDataException($"Task {item.Id} retired contract changed after inspection. Re-read/reconcile it before reactivation; no snapshot was written over it.");
                        File.Move(source, destination); // No overwrite if another document appeared.
                        applied = true;
                        file.Position = 0;
                        using var writer = new StreamWriter(file, new UTF8Encoding(false), 1024, leaveOpen: true);
                        writer.Write(documents[item.Id]);
                        writer.Flush();
                        file.SetLength(file.Position);
                    }
                    _changes.AppendEvent(change, "plan-task-reactivated", new Dictionary<string, string>
                    {
                        ["taskId"] = item.Id,
                        ["taskPath"] = item.TaskPath!,
                        ["retiredPath"] = archived.RetiredPath,
                        ["reason"] = "The same accepted ad hoc impact returned; its identity, authored contract and history were retained, with Draft lifecycle requiring ordinary approval."
                    });
                }
                else if (newDocuments.Contains(item.Id))
                {
                    var path = Path.Combine(dossier, item.TaskPath!);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (CisPathSafety.ContainsReparsePoint(change.RepositoryPath, path))
                        throw new InvalidDataException($"Task {item.Id} creation path changed after inspection.");
                    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    applied = true;
                    using var writer = new StreamWriter(file, new UTF8Encoding(false));
                    writer.Write(documents[item.Id]);
                }
                // Retained active documents deliberately have no write path at all.
            }
            applied |= RetireObsoleteTasks(change, previous, selected, null, () => applied = true);
            applied |= WriteIfChanged(_changes.DossierFile(change, "plan.md"), RenderPlan(change, "Draft", selected));
            applied |= RegisterTaskCatalogEntries(change, selected);
            if (applied)
                _changes.AppendEvent(change, "plan-built", new Dictionary<string, string>
                {
                    ["workItems"] = selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["acceptedImpacts"] = accepted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            return new(applied ? "built" : "unchanged", change.Id, "Draft", selected,
                ValidateInternal(change, selected, "Draft"), [], applied);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or OverflowException)
        {
            var message = applied
                ? "The build was partially applied before it stopped. Preserve the retained task documents, plan, catalogue and events; restore a verified consistent identity/contract binding before retrying. No cross-file rollback occurred. " + error.Message
                : error.Message;
            return new("reconciliation-required", change.Id, "Draft", previous, null, [message], applied);
        }
    }

    private IReadOnlyList<RetiredAdHocTask> ReadRetiredAdHocTasks(ChangeDossier change,
        IReadOnlyList<PlanWorkItem> previous, ISet<string> reserved)
    {
        var dossier = Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!;
        var root = Path.Combine(dossier, "agent-tasks");
        if (!Directory.Exists(root)) return [];
        var history = new List<RetiredAdHocTask>();
        var files = CisPathSafety.EnumerateFiles(root, "*.md").Take(10_001).ToArray();
        if (files.Length > 10_000) throw new InvalidDataException("Ad hoc task history exceeds the bounded 10,000-document inspection; retain it and reconcile its scope.");
        foreach (var path in files)
        {
            if (CisPathSafety.ContainsReparsePoint(change.RepositoryPath, path) || new FileInfo(path).Length > 512 * 1024)
                throw new InvalidDataException("Retained task history requires bounded regular documents without linked paths.");
            var content = File.ReadAllText(path);
            var fileId = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^WORK-\d+(?:-[A-Z0-9-]+)?", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (fileId.Success) reserved.Add(fileId.Value);
            var adHoc = HasTaskMetadata(content, "ad_hoc_impact_id")
                || HasTaskMetadata(content, "ad_hoc_impact_basis")
                || HasTaskMetadata(content, "ad_hoc_task_path")
                || ReadFrontMatterValue(content, "instance_key")?.StartsWith("ad-hoc-impact:", StringComparison.Ordinal) == true;
            if (!adHoc) continue;
            var instanceKey = TaskMetadata<string>(content, "instance_key");
            var id = TaskMetadata<string>(content, "task_id");
            if (id is not null) reserved.Add(id);
            var impactId = TaskMetadata<string>(content, "ad_hoc_impact_id");
            if (impactId is null || string.IsNullOrWhiteSpace(TaskMetadata<string>(content, "ad_hoc_impact_basis"))
                || instanceKey != "ad-hoc-impact:" + impactId)
                throw new InvalidDataException($"Retained ad hoc task {id ?? fileId.Value} has missing or inconsistent impact ownership/input basis. Restore its verified retained binding; no new task identity was assigned.");
            var relative = NormalizePath(Path.GetRelativePath(dossier, path));
            if (!relative.StartsWith("agent-tasks/retired/", StringComparison.Ordinal))
            {
                if (!previous.Any(item => item.Id == id && item.TaskPath == relative))
                    throw new InvalidDataException($"Active ad hoc document {relative} has no matching plan identity. Restore/reconcile that existing binding before rebuilding.");
                continue;
            }
            if (id is null || history.Any(item => item.Item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || item.Item.ImpactIds.Contains(impactId, StringComparer.Ordinal)))
                throw new InvalidDataException("Retired ad hoc ownership is missing or ambiguous; reconcile its retained identities without recycling them.");
            var activePath = TaskMetadata<string>(content, "ad_hoc_task_path");
            if (activePath is null || SafeTaskPath(dossier, activePath) is null || activePath.StartsWith("agent-tasks/retired/", StringComparison.Ordinal))
                throw new InvalidDataException($"Retired task {id} has no safe original task path.");
            var title = TaskMetadata<string>(content, "title") ?? id;
            if (title.StartsWith(id + " ", StringComparison.Ordinal)) title = title[(id.Length + 1)..];
            var parent = TaskMetadata<string>(content, "parent_id");
            var item = new PlanWorkItem(id, TaskMetadata<string>(content, "category") ?? "", TaskMetadata<string>(content, "complexity") ?? "",
                parent is null or "none" ? null : parent, activePath, title,
                TaskMetadata<string[]>(content, "requirement_ids") ?? [], [impactId], TaskMetadata<string[]>(content, "depends_on") ?? [],
                TaskMetadata<string>(content, "ad_hoc_acceptance_criteria") ?? throw new InvalidDataException($"Retired task {id} has no retained plan acceptance contract."),
                TaskMetadata<string>(content, "ad_hoc_validation") ?? throw new InvalidDataException($"Retired task {id} has no retained plan validation contract."), "Draft",
                TaskMetadata<string>(content, "task_type") ?? "core.legacy", TaskMetadata<string>(content, "task_type_version") ?? "1.0",
                TaskMetadata<string>(content, "approval_gate") ?? "none", TaskMetadata<string[]>(content, "targets") ?? [],
                TaskMetadata<string[]>(content, "decision_ids") ?? [], TaskBullets(content, "## Constraints and exclusions"), TaskBullets(content, "## Required outputs"),
                TaskMetadata<string>(content, "frontend_type") is { } frontend && frontend != "not-applicable" ? frontend : null)
            {
                InstanceKey = TaskMetadata<string>(content, "instance_key"),
                StoryId = TaskMetadata<string>(content, "story_id"),
                WorkOwnerTaskId = TaskMetadata<string>(content, "work_owner_task_id"),
                VerificationObligations = TaskMetadata<CisTaskVerificationObligation[]>(content, "verification_obligations") ?? []
            };
            history.Add(new(item, relative, content));
        }
        return history;
    }

    private void ReserveCatalogTaskIdentities(ChangeDossier change, ISet<string> reserved)
    {
        var context = _resolver.Resolve(change.RepositoryPath).Context
            ?? throw new InvalidDataException("The canonical task catalogue could not be resolved.");
        var read = new DocumentationCatalogReader().Read(context.CatalogPath);
        if (!read.IsSuccess || read.Catalog is null)
            throw new InvalidDataException("Restore the valid canonical task catalogue before rebuilding ad hoc ownership.");
        var prefix = $"{context.RepositoryId}:change:{change.Id.ToLowerInvariant()}:task:";
        var entries = read.Catalog.Documents.Where(entry => entry.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new InvalidDataException("Canonical task catalogue identities are ambiguous; reconcile their retained routes before rebuilding.");
        foreach (var entry in entries)
        {
            var id = entry.Id[prefix.Length..];
            reserved.Add(id);
            var taskPrefix = change.RelativePath + "/agent-tasks/";
            if (id.Length == 0 || !entry.Path.StartsWith(taskPrefix, StringComparison.Ordinal)
                || !CisPathSafety.TryResolveUnderRoot(change.RepositoryPath, entry.Path, out var path)
                || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, path) || !File.Exists(path)
                || new FileInfo(path).Length > 512 * 1024)
                throw new InvalidDataException($"Retained catalogue task {id} has a missing or unsafe document route. Restore/reconcile that ownership; no identifier was recycled.");
            if (!string.Equals(TaskMetadata<string>(File.ReadAllText(path), "task_id"), id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Retained catalogue task {id} does not match its document identity. Restore/reconcile that exact binding without assigning a new owner.");
        }
    }

    private static string AdHocImpactBasis(ImpactFinding finding)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
            { finding.Id, finding.Category, finding.Target, finding.Label, finding.Evidence })));

    private static string AdHocReconciliationMessage(string taskId, string impactId)
        => $"Task {taskId} requires reconciliation with accepted impact {impactId}: its retained scope/input basis changed or is missing. Preserve its authored contract, identity, evidence and accounting history. Restore verified accepted inputs/binding if corrupted, or use the existing governed change/impact route for genuinely changed scope. Build cannot amend the contract or approve that scope by replacing a hash.";

    private void ValidateAdHocImpactContract(ChangeDossier change, PlanWorkItem item,
        IReadOnlyList<ImpactFinding> findings, ICollection<string> errors)
    {
        if (item.TaskPath is null) return; // Ordinary document validation reports row-only legacy work.
        foreach (var error in AdHocTaskTypeErrors(change.RepositoryPath, item)) errors.Add(error);
        try
        {
            var document = ReadTaskContract(change, item);
            var matching = findings.Where(finding => finding.State == "accepted" && item.ImpactIds.Contains(finding.Id, StringComparer.Ordinal)).ToArray();
            if (item.ImpactIds.Count != 1 || matching.Length != 1 || document is null)
            {
                errors.Add($"{item.Id} requires one unambiguous accepted ad hoc impact binding.");
                return;
            }
            var finding = matching[0];
            if (item.Category != finding.Category || TaskMetadata<string>(document, "category") != item.Category
                || TaskMetadata<string>(document, "ad_hoc_impact_id") != finding.Id
                || TaskMetadata<string>(document, "ad_hoc_impact_basis") != AdHocImpactBasis(finding)
                || TaskMetadata<string>(document, "ad_hoc_task_path") != item.TaskPath
                || item.InstanceKey != "ad-hoc-impact:" + finding.Id)
                errors.Add(AdHocReconciliationMessage(item.Id, finding.Id));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        { errors.Add($"{item.Id} ad hoc contract could not be validated: {error.Message}"); }
    }

    private IReadOnlyList<string> AdHocTaskTypeErrors(string repositoryPath, PlanWorkItem item)
        => item.TaskTypeKey == "core.legacy"
            ? item.TaskTypeVersion == "1.0" ? [] : [$"{item.Id} has an unsupported ad hoc fallback contract version."]
            : ValidateTaskType(repositoryPath, item.TaskTypeKey, item.TaskTypeVersion, item.Category)
                .Select(error => $"{item.Id}: {error}").ToArray();

    private static string RenderAdHocTask(ChangeDossier change, PlanWorkItem item, ImpactFinding finding)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(item.Id + " " + item.Title)}");
        builder.AppendLine("type: agent-task");
        builder.AppendLine("status: Draft");
        builder.AppendLine($"task_status: {CanonicalStatus(item.Status)}");
        builder.AppendLine($"task_id: {item.Id}");
        builder.AppendLine($"task_type: {item.TaskTypeKey}");
        builder.AppendLine($"task_type_version: {item.TaskTypeVersion}");
        builder.AppendLine($"parent_id: {item.ParentId ?? "none"}");
        builder.AppendLine($"category: {item.Category}");
        builder.AppendLine($"complexity: {item.Complexity}");
        builder.AppendLine($"requirement_ids: {JsonSerializer.Serialize(item.RequirementIds)}");
        builder.AppendLine($"impact_ids: {JsonSerializer.Serialize(item.ImpactIds)}");
        builder.AppendLine($"depends_on: {JsonSerializer.Serialize(item.DependsOn)}");
        builder.AppendLine($"approval_gate: {item.ApprovalGate}");
        builder.AppendLine($"frontend_type: {item.FrontendType ?? "not-applicable"}");
        builder.AppendLine($"targets: {JsonSerializer.Serialize(item.Targets ?? [])}");
        builder.AppendLine($"decision_ids: {JsonSerializer.Serialize(item.DecisionIds ?? [])}");
        builder.AppendLine($"instance_key: {JsonSerializer.Serialize(item.InstanceKey)}");
        builder.AppendLine($"work_owner_task_id: {JsonSerializer.Serialize(item.WorkOwnerTaskId)}");
        builder.AppendLine($"verification_obligations: {JsonSerializer.Serialize(item.VerificationObligations)}");
        builder.AppendLine($"ad_hoc_impact_id: {JsonSerializer.Serialize(finding.Id)}");
        builder.AppendLine($"ad_hoc_impact_basis: {JsonSerializer.Serialize(AdHocImpactBasis(finding))}");
        builder.AppendLine($"ad_hoc_task_path: {JsonSerializer.Serialize(item.TaskPath)}");
        builder.AppendLine($"ad_hoc_acceptance_criteria: {JsonSerializer.Serialize(item.AcceptanceCriteria)}");
        builder.AppendLine($"ad_hoc_validation: {JsonSerializer.Serialize(item.Validation)}");
        builder.AppendLine("authority: human-approved-plan");
        builder.AppendLine("---");
        builder.AppendLine($"\n# {item.Id}: {item.Title}\n\n## Objective\n\n{item.Title}");
        builder.AppendLine($"\n## Required changes\n\n- Address the accepted {finding.Category} impact `{finding.Id}`: {finding.Label}.\n- Keep the work bounded to `{finding.Target}` and the reviewed change outcome: {change.Outcome}");
        builder.AppendLine("\n## Required outputs\n");
        foreach (var output in (item.RequiredOutputs ?? []).DefaultIfEmpty("The bounded change and reproducible evidence for the accepted impact.")) builder.AppendLine("- " + output);
        builder.AppendLine("\n## Constraints and exclusions\n");
        foreach (var exclusion in (item.NegativeCriteria ?? []).DefaultIfEmpty("Do not expand the accepted change/impact scope or bypass product, plan, design, allocation or completion authority.")) builder.AppendLine("- " + exclusion);
        builder.AppendLine($"\n## Context and evidence\n\n- Accepted impact `{finding.Id}`: `{finding.Target}`.\n- Impact evidence: {finding.Evidence}");
        builder.AppendLine("\n## Dependencies and approval gates\n");
        foreach (var dependency in item.DependsOn.DefaultIfEmpty("No task dependencies.")) builder.AppendLine("- " + dependency);
        builder.AppendLine("- The current plan requires actual approval; execution requires an eligible task and applicable finite work/storage authority.");
        builder.AppendLine($"\n## Acceptance criteria\n\n- [ ] {item.AcceptanceCriteria}\n\n## Targeted validation\n\n- [ ] {item.Validation}");
        builder.AppendLine("\n## Completion evidence\n\n| Check | Command or artifact | Result | Notes |\n| --- | --- | --- | --- |\n| Accepted impact | No execution yet | Not run | Record actual scoped evidence before completion. |");
        builder.AppendLine("\n## Deferrals and residual risk\n\n- No deferral is approved by generation. Record any actual unmet criterion, owner, follow-up and decision.");
        return builder.ToString();
    }
}
