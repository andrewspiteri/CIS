using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private static readonly JsonSerializerOptions OutcomeJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record OutcomePlan(string? AuthorizationId, string Actor, IReadOnlyList<PlanWorkItem> WorkItems, PlanSource? Source = null);
    private string OutcomePlanPath(ChangeDossier change) => _changes.DossierFile(change, "plan.json");

    private CisOutcomeAuthorization? OutcomeAuthority(ChangeDossier change)
        => CisOutcomeAuthority.Read(_resolver.Resolve(change.RepositoryPath).Context
            ?? throw new InvalidDataException("Repository context is unavailable."), change.Id);

    private PlanResult BuildOutcomePlan(ChangeDossier change)
    {
        try
        {
            var authority = OutcomeAuthority(change);
            var previous = ReadPlan(change);
            var items = previous.WorkItems.ToArray();
            if (authority is { Request.OrganizeTasks: false }
                && (items.Length == 0 || items.Any(item => item.AuthorizationScopeIds.Count == 0)))
                return Error(change.Id, "The retained outcome does not delegate task organization. Present that specific missing delegation before deriving tasks.");
            if (items.Any(item => item.ContractErrors.Count > 0))
                return Error(change.Id, items.SelectMany(item => item.ContractErrors).ToArray());
            if (items.Length == 0)
            {
                var scope = authority?.Request.Scope.Select(item => item.Id).ToArray() ?? [];
                items = [NewOutcomeTask(change, "WORK-001", new(change.Title, scope,
                    "Deliver the shared outcome and its applicable checks.", "Build the affected component and run its connected checks."), authority)];
            }
            else if (authority is not null)
            {
                // Only the unapproved generated whole-outcome seed may acquire its first scope binding.
                items = items.Select(item => item.AuthorizationScopeIds.Count == 0
                    && item.InstanceKey == "outcome:initial" && item.Status == "Draft"
                    ? item with { AuthorizationScopeIds = authority.Request.Scope.Select(scope => scope.Id).ToArray(),
                        AuthorizationScopeBasis = CisOutcomeAuthority.ScopeBasis(authority, authority.Request.Scope.Select(scope => scope.Id).ToArray()) }
                    : item).ToArray();
            }
            var actor = "CIS planner";
            var written = WriteOutcomePlan(change, new(authority?.Id, actor, items, previous.Source));
            foreach (var item in items) EnsureOutcomeTaskDocument(change, item, authority, actor);
            EnsureOutcomeVerification(change);
            RegisterTaskCatalogEntries(change, items);
            _changes.RegisterExistingDocuments(change);
            return OutcomeResult(change, items, authority, written ? "derived" : "unchanged", written);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OverflowException)
        { return Error(change.Id, error.Message); }
    }

    public PlanResult OrganizeOutcomeTasks(OutcomeTaskRequest request, string operation = "add")
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, "Change dossier was not found.");
        try
        {
            var authority = OutcomeAuthority(change);
            if (authority is null || !authority.Request.OrganizeTasks || !CisOutcomeAuthority.IsController(authority, request.Actor))
                return Error(change.Id, "Task organization needs an applicable outcome authorization and its delegated controller; prepare the whole outcome and limits before requesting that decision.");
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Tasks is null || request.Tasks.Count == 0)
                return Error(change.Id, "Supply task-specific work and a derivation reason.");
            var existing = ReadPlan(change);
            if (existing.WorkItems.Any(item => item.ContractErrors.Count > 0))
                return Error(change.Id, existing.WorkItems.SelectMany(item => item.ContractErrors).ToArray());
            var current = existing.WorkItems.ToList();
            var target = request.TaskId is null ? null : current.SingleOrDefault(item => item.Id.Equals(request.TaskId, StringComparison.OrdinalIgnoreCase));
            if (operation is "split" or "update" && target is null) return Error(change.Id, "The task to organize was not found.");
            if (target is { Status: "Complete" })
                return Error(change.Id, "Preserve completed task contracts and their history; add supporting work instead.");
            if (operation == "split" && request.Tasks.Count < 2) return Error(change.Id, "A split requires at least two bounded children.");
            if (operation == "update" && request.Tasks.Count != 1) return Error(change.Id, "An update describes one unfinished task.");
            var reserved = current.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var taskRoot = Path.Combine(Path.GetDirectoryName(OutcomePlanPath(change))!, "agent-tasks");
            if (Directory.Exists(taskRoot))
                foreach (var file in CisPathSafety.EnumerateFiles(taskRoot, "*.md")) reserved.Add(Path.GetFileNameWithoutExtension(file));
            var number = reserved.Select(id => int.TryParse(id.AsSpan(Math.Min(5, id.Length)), out var value) ? value : 0).DefaultIfEmpty(0).Max();
            var added = new List<PlanWorkItem>();
            foreach (var definition in request.Tasks)
            {
                var errors = OutcomeDefinitionErrors(authority, definition);
                if (errors.Count > 0) return Error(change.Id, errors.ToArray()); // No partial write and no downgrade of unrelated work.
                string id;
                if (operation == "update") id = target!.Id;
                else { do { id = $"WORK-{++number:000}"; } while (!reserved.Add(id)); }
                var item = NewOutcomeTask(change, id, definition, authority);
                if (operation == "split") item = item with { ParentId = target!.Id, WorkOwnerTaskId = target.WorkOwnerTaskId ?? target.Id,
                    DependsOn = target.DependsOn.Concat(item.DependsOn).Distinct(StringComparer.Ordinal).ToArray() };
                if (operation == "update") item = item with { ParentId = target!.ParentId, WorkOwnerTaskId = target.WorkOwnerTaskId, InstanceKey = target.InstanceKey };
                added.Add(item);
            }
            if (operation == "split" && (added.SelectMany(item => item.AuthorizationScopeIds).Except(target!.AuthorizationScopeIds).Any()
                || target!.AuthorizationScopeIds.Except(added.SelectMany(item => item.AuthorizationScopeIds)).Any()))
                return Error(change.Id, "Split children must preserve and cover their retained parent's authorized scope; add separate supporting work for another scope.");
            var metadataOnlyUpdate = operation == "update" && OutcomeTaskDigest(target!) == OutcomeTaskDigest(added[0]);
            if (operation == "update" && target!.Status != "Draft" && !metadataOnlyUpdate)
                return Error(change.Id, "Preserve the started task's executable contract and history; add supporting work or split the unfinished task instead.");
            if (operation == "update") added[0] = added[0] with { Status = target!.Status };
            if (operation == "update") current.RemoveAll(item => item.Id == target!.Id);
            if (operation == "split")
            {
                var index = current.FindIndex(item => item.Id == target!.Id);
                current[index] = target! with { Status = "Decomposed", Complexity = "high" };
            }
            current.AddRange(added);
            var ids = current.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (current.Any(item => item.DependsOn.Any(id => !ids.Contains(id))) || HasCycle(current))
                return Error(change.Id, "Task dependencies must resolve without a cycle; existing work was not changed.");
            // All validation precedes publication. Existing unrelated task documents are never regenerated.
            foreach (var item in added)
                if (!metadataOnlyUpdate) EnsureOutcomeTaskDocument(change, item, authority, request.Actor, replace: operation == "update");
            if (operation == "split") SetOutcomeTaskStatus(change, current.Single(item => item.Id == target!.Id));
            WriteOutcomePlan(change, new(authority.Id, request.Actor, current, existing.Source));
            EnsureOutcomeVerification(change);
            RegisterTaskCatalogEntries(change, current);
            _changes.RegisterExistingDocuments(change);
            _changes.AppendEvent(change, "tasks-derived", new Dictionary<string, string>
            { ["authorizationId"] = authority.Id, ["controller"] = request.Actor, ["operation"] = operation,
                ["taskIds"] = string.Join(',', added.Select(item => item.Id)), ["reason"] = request.Reason });
            return OutcomeResult(change, current, authority, "derived", true, added.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OverflowException)
        { return Error(change.Id, error.Message); }
    }

    private static IReadOnlyList<string> OutcomeDefinitionErrors(CisOutcomeAuthorization authority, OutcomeTaskDefinition task)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(task.Title) || string.IsNullOrWhiteSpace(task.AcceptanceCriteria) || string.IsNullOrWhiteSpace(task.Validation))
            errors.Add("A task needs its own title, acceptance and validation.");
        if (task.Complexity is not ("low" or "medium")) errors.Add("An executable delivery leaf must be at most medium; split larger work.");
        if (task.ScopeIds is null || task.ScopeIds.Count == 0 || task.ScopeIds.Except(authority.Request.Scope.Select(scope => scope.Id), StringComparer.Ordinal).Any())
            errors.Add("Scope expansion needs a targeted human decision for new scope references: "
                + string.Join(", ", (task.ScopeIds ?? []).Except(authority.Request.Scope.Select(scope => scope.Id), StringComparer.Ordinal))
                + ". Existing tasks retain their authority.");
        else
        {
            var roots = authority.Request.Scope.Where(scope => task.ScopeIds.Contains(scope.Id, StringComparer.Ordinal)).SelectMany(scope => scope.Paths).ToArray();
            foreach (var path in task.Paths ?? [])
                if (Path.IsPathRooted(path) || path.Replace('\\', '/').Split('/').Any(part => part is "." or "..")
                    || !roots.Any(root => root == "." || path.Replace('\\', '/').Equals(root.TrimEnd('/'), StringComparison.Ordinal)
                        || path.Replace('\\', '/').StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal)))
                    errors.Add("Scope expansion needs a targeted human decision for path: " + path);
        }
        if (authority.Request.VisualUi == false && task.Category is "frontend" or "wireframe" or "design")
            errors.Add("Visual scope is outside this accepted no-UI outcome; present that material difference for human decision.");
        return errors;
    }

    private PlanWorkItem NewOutcomeTask(ChangeDossier change, string id, OutcomeTaskDefinition task, CisOutcomeAuthorization? authority)
        => new(id, task.Category, task.Complexity, null, $"agent-tasks/{id}.md", task.Title, [], [], task.DependsOn,
            task.AcceptanceCriteria, task.Validation, "Draft")
        {
            InstanceKey = authority is null ? "outcome:initial" : "outcome:" + Guid.NewGuid().ToString("N"),
            AuthorizationScopeIds = task.ScopeIds,
            AuthorizationScopeBasis = authority is null ? null : CisOutcomeAuthority.ScopeBasis(authority, task.ScopeIds),
            WorkPaths = task.Paths, Assignee = task.Assignee,
            Targets = [], DecisionIds = [], NegativeCriteria = [], RequiredOutputs = [],
            VerificationObligations = [new("task-contract", [task.AcceptanceCriteria], task.Validation, "review", [task.Validation])]
        };

    private bool WriteOutcomePlan(ChangeDossier change, OutcomePlan plan)
    {
        var path = OutcomePlanPath(change);
        var persisted = plan with { WorkItems = plan.WorkItems.Select(item => item with { ContractErrors = [] }).ToArray() };
        var applied = WriteIfChanged(path, JsonSerializer.Serialize(persisted, OutcomeJson) + "\n");
        var status = plan.AuthorizationId is null ? "Draft" : "Approved";
        var view = new StringBuilder($"---\ntitle: {JsonSerializer.Serialize(change.Id + " delivery plan")}\ntype: delivery-plan\nstatus: {status}\nchange_id: {change.Id}\nauthority: {(plan.AuthorizationId is null ? "proposed" : "derived-outcome-authorization")}\n---\n\n# Delivery plan\n\n[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.\n");
        view.AppendLine(plan.AuthorizationId is null ? "This is a proposal; execution has no authorization yet."
            : $"Derived under `{plan.AuthorizationId}` by {plan.Actor}; no separate human review of these task views is claimed.");
        view.AppendLine("\n| Task | Work | Status | Depends on |\n| --- | --- | --- | --- |");
        foreach (var item in plan.WorkItems) view.AppendLine($"| {item.Id} | [{Cell(item.Title)}]({item.TaskPath}) | {item.Status} | {string.Join(", ", item.DependsOn)} |");
        return WriteIfChanged(_changes.DossierFile(change, "plan.md"), view.ToString()) || applied;
    }

    private void EnsureOutcomeTaskDocument(ChangeDossier change, PlanWorkItem task, CisOutcomeAuthorization? authority, string actor, bool replace = false)
    {
        var root = Path.GetDirectoryName(OutcomePlanPath(change))!;
        if (!CisPathSafety.TryResolveUnderRoot(root, task.TaskPath!, out var path) || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, path))
            throw new InvalidDataException("Unsafe task document path.");
        if (File.Exists(path) && !replace)
        {
            if (authority is not null && ReadFrontMatterValue(File.ReadAllText(path), "outcome_authorization_id") is null)
            {
                var text = File.ReadAllText(path);
                text = text.Replace("authority: proposed", $"authority: derived-outcome-authorization\noutcome_authorization_id: {JsonSerializer.Serialize(authority.Id)}\nauthorization_scope_ids: {JsonSerializer.Serialize(task.AuthorizationScopeIds)}\nauthorization_scope_basis: {JsonSerializer.Serialize(task.AuthorizationScopeBasis)}", StringComparison.Ordinal);
                File.WriteAllText(path, text);
            }
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var textView = $"---\ntitle: {JsonSerializer.Serialize(task.Id + " " + task.Title)}\ntype: agent-task\nstatus: Draft\ntask_status: {task.Status}\ntask_id: {task.Id}\nparent_id: {task.ParentId ?? "none"}\ncategory: {task.Category}\ncomplexity: {task.Complexity}\ntask_type: core.legacy\ntask_type_version: 1.0\nwork_owner_task_id: {JsonSerializer.Serialize(task.WorkOwnerTaskId)}\ninstance_key: {JsonSerializer.Serialize(task.InstanceKey)}\ndepends_on: {JsonSerializer.Serialize(task.DependsOn)}\nassignee: {JsonSerializer.Serialize(task.Assignee)}\nauthority: {(authority is null ? "proposed" : "derived-outcome-authorization")}\n";
        if (authority is not null) textView += $"outcome_authorization_id: {JsonSerializer.Serialize(authority.Id)}\nauthorization_scope_ids: {JsonSerializer.Serialize(task.AuthorizationScopeIds)}\nauthorization_scope_basis: {JsonSerializer.Serialize(task.AuthorizationScopeBasis)}\n";
        textView += $"---\n\n# {task.Id}: {task.Title}\n\n[Shared outcome, constraints and limits](../proposal.md); organized by {actor}.\n\n## Objective\n\n{task.Title}\n\n## Acceptance criteria\n\n- [ ] {task.AcceptanceCriteria}\n\n## Targeted validation\n\n- [ ] {task.Validation}\n\n## Completion evidence\n\n| Check | Result | Evidence |\n| --- | --- | --- |\n";
        File.WriteAllText(path, textView);
    }

    private void SetOutcomeTaskStatus(ChangeDossier change, PlanWorkItem item)
    {
        if (item.TaskPath is null) return;
        var path = Path.Combine(Path.GetDirectoryName(OutcomePlanPath(change))!, item.TaskPath);
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"(?m)^task_status:.*$", "task_status: " + item.Status);
        text = Regex.Replace(text, @"(?m)^complexity:.*$", "complexity: " + item.Complexity);
        File.WriteAllText(path, text);
    }

    private void EnsureOutcomeVerification(ChangeDossier change)
    {
        var path = _changes.DossierFile(change, "verification.md");
        if (!File.Exists(path)) File.WriteAllText(path, $"---\ntitle: {change.Id} verification\ntype: verification-record\nstatus: Draft\nchange_id: {change.Id}\n---\n\n# Verification\n\n| Task ID | Check | Command or artifact | Result | Notes |\n| --- | --- | --- | --- | --- |\n");
    }

    private PlanDocument? ReadOutcomePlan(ChangeDossier change)
    {
        var path = OutcomePlanPath(change);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024 || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, path))
            throw new InvalidDataException("Unsafe or oversized canonical plan state.");
        var plan = JsonSerializer.Deserialize<OutcomePlan>(File.ReadAllText(path), OutcomeJson)
            ?? throw new InvalidDataException("Canonical plan state is unreadable.");
        var authority = OutcomeAuthority(change);
        if (plan.WorkItems is null || plan.WorkItems.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)
            || item.AuthorizationScopeIds is null || item.WorkPaths is null || item.DependsOn is null
            || item.ContractErrors is null || item.VerificationObligations is null))
            throw new InvalidDataException("The canonical plan has malformed task contracts; restore its retained state.");
        if (plan.AuthorizationId is not null && plan.AuthorizationId != authority?.Id)
            throw new InvalidDataException("The canonical plan refers to a different or missing outcome authorization.");
        var items = plan.WorkItems.Select(task => ReadOutcomeTaskContract(change, task,
            plan.AuthorizationId is null ? null : authority)).ToArray();
        return new(authority is null || plan.AuthorizationId is null ? "Draft" : "Approved", items, plan.Source);
    }

    private PlanWorkItem ReadOutcomeTaskContract(ChangeDossier change, PlanWorkItem task, CisOutcomeAuthorization? authority)
    {
        try
        {
            // Use the existing in-dossier, bounded, unlinked task reader before any mutation.
            var dossier = Path.GetDirectoryName(OutcomePlanPath(change))!;
            if (task.TaskPath is null || SafeTaskPath(dossier, task.TaskPath) is null)
                throw new InvalidDataException("The task contract must stay inside its canonical agent-tasks directory.");
            var document = ReadTaskContract(change, task) ?? throw new InvalidDataException("The task document is missing.");
            if (TaskMetadata<string>(document, "task_id") != task.Id
                || TaskMetadata<string>(document, "task_type") != task.TaskTypeKey
                || TaskMetadata<string>(document, "task_type_version") != task.TaskTypeVersion
                || TaskMetadata<string>(document, "parent_id") != (task.ParentId ?? "none")
                || TaskMetadata<string>(document, "work_owner_task_id") != task.WorkOwnerTaskId
                || TaskMetadata<string>(document, "instance_key") != task.InstanceKey)
                throw new InvalidDataException("The task document identity, type or retained ownership conflicts with its canonical row.");
            if (authority is not null && (TaskMetadata<string>(document, "outcome_authorization_id") != authority.Id
                || TaskMetadata<string>(document, "authorization_scope_basis") != task.AuthorizationScopeBasis
                || !(TaskMetadata<string[]>(document, "authorization_scope_ids") ?? []).Order(StringComparer.Ordinal)
                    .SequenceEqual(task.AuthorizationScopeIds.Order(StringComparer.Ordinal), StringComparer.Ordinal)))
                throw new InvalidDataException("The task document's outcome/scope references conflict with its canonical row.");
            if (task.InstanceKey?.StartsWith("outcome:", StringComparison.Ordinal) == true)
            {
                static string Normalize(string value) => Regex.Replace(
                    Regex.Replace(value, @"(?m)^\s*[-*]\s*\[[ xX]\]\s*", ""), @"\s+", " ").Trim();
                foreach (var (heading, expected) in new[] { ("## Acceptance criteria", task.AcceptanceCriteria), ("## Targeted validation", task.Validation) })
                    if (!Normalize(MarkdownSection(document, heading) ?? "").Contains(Normalize(expected), StringComparison.Ordinal))
                        throw new InvalidDataException("The task-specific acceptance or validation differs from its canonical contract; reconcile that affected task through the existing update command.");
                return task;
            }
            // Imported handoffs retain their authored lists and verification obligations.
            return HydrateTaskContract(change, task);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return task with { ContractErrors = [$"{task.Id}: {error.Message}"] }; }
    }

    private PlanResult OutcomeResult(ChangeDossier change, IReadOnlyList<PlanWorkItem> items, CisOutcomeAuthorization? authority, string status, bool applied, IReadOnlySet<string>? selected = null)
        => new(status, change.Id, authority is null ? "Draft" : "Approved", items,
            ValidateOutcomePlan(change, items, authority, selected), [], applied);

    private PlanValidation ValidateOutcomePlan(ChangeDossier change, IReadOnlyList<PlanWorkItem> items, CisOutcomeAuthorization? authority, IReadOnlySet<string>? selected = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (items.Count == 0) errors.Add("The plan has no bounded work items.");
        if (items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Count) errors.Add("Task identities are duplicated.");
        foreach (var task in items)
        {
            if (selected is not null && !selected.Contains(task.Id)) continue;
            errors.AddRange(task.ContractErrors);
            if (authority is null) { warnings.Add(task.Id + ": proposed; outcome authorization is not yet recorded."); continue; }
            if (task.AuthorizationScopeIds.Count == 0 || task.AuthorizationScopeIds.Except(authority.Request.Scope.Select(scope => scope.Id), StringComparer.Ordinal).Any())
            { errors.Add(task.Id + ": scope expansion requires a targeted human decision; unrelated work remains authorized."); continue; }
            if (task.AuthorizationScopeBasis != CisOutcomeAuthority.ScopeBasis(authority, task.AuthorizationScopeIds))
                errors.Add(task.Id + ": accepted scope changed; reconcile this task's authority.");
            errors.AddRange(OutcomeDefinitionErrors(authority, new(task.Title, task.AuthorizationScopeIds, task.AcceptanceCriteria, task.Validation)
                { Paths = task.WorkPaths, Complexity = task.Complexity == "high" ? "medium" : task.Complexity, Category = task.Category }));
            if (task.Complexity == "high" && items.Count(item => item.ParentId == task.Id) < 2) errors.Add(task.Id + ": high work requires bounded children.");
            if (string.IsNullOrWhiteSpace(task.AcceptanceCriteria) || string.IsNullOrWhiteSpace(task.Validation)) errors.Add(task.Id + ": missing acceptance or validation.");
            if (task.DependsOn.Any(id => !items.Any(item => item.Id == id))) errors.Add(task.Id + ": missing dependency.");
            if (task.TaskPath is null || !File.Exists(Path.Combine(Path.GetDirectoryName(OutcomePlanPath(change))!, task.TaskPath))) errors.Add(task.Id + ": missing task document.");
        }
        if (HasCycle(items)) errors.Add("Task dependencies contain a cycle.");
        var decisions = _decisions.Read(change);
        errors.AddRange(decisions.Errors);
        if (decisions.BlockingOpen > 0) errors.Add("Blocking decisions remain open.");
        if (ReadOutcomePlan(change)?.Source is { } source)
        {
            if (selected is null || items.Any(item => selected.Contains(item.Id) && item.RequirementIds.Count > 0))
                errors.AddRange(ValidateSourceCurrency(change, source));
            ValidateManualTestCaseArtifacts(change, source, errors);
        }
        else errors.AddRange(ValidateOutcomeManualCatalogue(change));
        return new(errors.Count == 0, errors, warnings, 0, 0, decisions.BlockingOpen);
    }

    private IReadOnlyList<string> ValidateOutcomeManualCatalogue(ChangeDossier change)
    {
        if (!_changes.DocumentApplicability(change).ManualTestCatalogue) return [];
        var path = _changes.DossierFile(change, "test-cases.md");
        var csvPath = _changes.DossierFile(change, "test-cases.csv");
        if (!File.Exists(path) || !File.Exists(csvPath)) return ["The applicable manual-test catalogue and its CSV projection are missing."];
        var text = File.ReadAllText(path);
        var cases = Regex.Matches(text, @"(?im)^###\s+TC-[A-Z0-9]+(?:-[A-Z0-9]+)*:").Count;
        if (cases == 0 || Regex.IsMatch(text, @"(?im)^status:\s*NotGenerated\s*$")
            || File.ReadLines(csvPath).Skip(1).All(string.IsNullOrWhiteSpace))
            return ["The applicable manual-test catalogue is unpopulated; record its actual cases before validating delivery."];
        return [];
    }

    private string OutcomeTaskDigest(PlanWorkItem task)
    {
        var contract = task with { Status = "", Title = "", Complexity = "", ParentId = null, DependsOn = [], Assignee = null, ContractErrors = [],
            AuthorizationScopeIds = task.AuthorizationScopeIds.Order(StringComparer.Ordinal).ToArray() };
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contract, OutcomeJson)));
    }
}
