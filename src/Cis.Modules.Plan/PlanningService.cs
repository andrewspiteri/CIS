using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Decision;
using Cis.Modules.Feedback;
using Cis.Modules.Impact;

namespace Cis.Modules.Plan;

public sealed class PlanningService
{
    private readonly ChangeDossierStore _changes;
    private readonly DecisionService _decisions;
    private readonly ImpactAnalysisService _impacts;
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly TaskTypeRegistry _taskTypes;
    private readonly TaskTypeCapabilityStore _capabilities;
    private readonly ToolUsageStore? _toolUsage;
    private readonly IReadOnlyList<IChangeReadinessCheck> _readinessChecks;
    private readonly IReadOnlyList<ICisFeatureApprovalAuthority> _featureAuthorities;
    private readonly ManualTestAutomationScanner _automationScanner;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;
    private readonly Func<DateTimeOffset> _clock;

    public PlanningService(
        ChangeDossierStore changes,
        ImpactAnalysisService impacts,
        DecisionService decisions,
        ICisRepositoryContextResolver resolver,
        TaskTypeRegistry? taskTypes = null,
        ToolUsageStore? toolUsage = null,
        TaskTypeCapabilityStore? capabilities = null,
        IEnumerable<IChangeReadinessCheck>? readinessChecks = null,
        Func<DateTimeOffset>? clock = null,
        IEnumerable<ICisFeatureApprovalAuthority>? featureAuthorities = null,
        ICisWorkspaceRegistry? workspaceRegistry = null)
    {
        _changes = changes;
        _impacts = impacts;
        _decisions = decisions;
        _resolver = resolver;
        _taskTypes = taskTypes ?? TaskTypeRegistry.CreateDefault();
        _toolUsage = toolUsage;
        _capabilities = capabilities ?? new TaskTypeCapabilityStore(resolver);
        _readinessChecks = (readinessChecks ?? []).ToArray();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _featureAuthorities = (featureAuthorities ?? []).ToArray();
        _workspaceRegistry = workspaceRegistry;
        _automationScanner = new ManualTestAutomationScanner(resolver, workspaceRegistry);
    }

    public PlanResult Build(string repositoryPath, string changeId)
    {
        var readinessErrors = EvaluateReadiness(repositoryPath);
        if (readinessErrors.Count > 0)
            return Error(changeId, readinessErrors.ToArray());

        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var existing = ReadPlan(change);
        if (string.Equals(existing.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return Error(change.Id, "An approved plan cannot be rebuilt. Record a new decision or change baseline first.");
        }

        if (existing.Source is not null)
        {
            return Error(change.Id,
                $"This plan was imported from {existing.Source.Path}; rerun `cis plan import-spec` to preserve feature-requirement coverage.");
        }

        var impact = _impacts.ReadImpact(change);
        var completeness = ImpactAnalysisService.CalculateCompleteness(impact.Findings, impact.Truncated);
        if (!completeness.PlanReady)
        {
            return new PlanResult("blocked", change.Id, existing.Status, existing.WorkItems, null, completeness.Gaps, false);
        }

        var accepted = impact.Findings.Where(item => item.State == "accepted").ToArray();
        var workItems = BuildWorkItems(accepted);
        var content = RenderPlan(change, "Draft", workItems);
        var path = _changes.DossierFile(change, "plan.md");
        var unchanged = File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal);
        if (!unchanged)
        {
            File.WriteAllText(path, content);
            _changes.AppendEvent(change, "plan-built", new Dictionary<string, string>
            {
                ["workItems"] = workItems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["acceptedImpacts"] = accepted.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        var validation = ValidateInternal(change, workItems, "Draft");
        return new PlanResult(unchanged ? "unchanged" : "built", change.Id, "Draft", workItems, validation, [], !unchanged);
    }

    public PlanResult ImportSpec(FeatureSpecImportRequest request, string? ignoredReadinessCheck = null)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null)
        {
            return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        }

        var existing = ReadPlan(change);
        var approvedPlan = string.Equals(existing.Status, "Approved", StringComparison.OrdinalIgnoreCase);
        var derivedOnlyRefresh = approvedPlan && IsExactImportedSource(request, existing.Source);
        var readinessErrors = EvaluateReadiness(request.RepositoryPath,
            derivedOnlyRefresh ? "technical-intent" : ignoredReadinessCheck);
        if (readinessErrors.Count > 0)
            return Error(request.ChangeId, readinessErrors.ToArray());

        var impact = _impacts.ReadImpact(change);
        var completeness = ImpactAnalysisService.CalculateCompleteness(impact.Findings, impact.Truncated);
        if (!completeness.PlanReady)
        {
            return new PlanResult("blocked", change.Id, existing.Status, existing.WorkItems, null,
                completeness.Gaps, false, existing.Source);
        }

        var capabilityStatus = _capabilities.Read(change.RepositoryPath);
        if (capabilityStatus.Errors.Count > 0)
            return new PlanResult("invalid", change.Id, existing.Status, existing.WorkItems, null,
                capabilityStatus.Errors, false, existing.Source);
        var pendingMigrations = existing.WorkItems.Where(item =>
                !item.TaskTypeKey.StartsWith("core.", StringComparison.OrdinalIgnoreCase)
                && capabilityStatus.Selections.Any(selection =>
                    selection.ReplacesTypeKeys.Contains(item.TaskTypeKey, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        if (pendingMigrations.Length > 0)
            return new PlanResult("migration-required", change.Id, existing.Status, existing.WorkItems, null,
                pendingMigrations.Select(item =>
                    $"Task {item.Id} uses replaced extension type '{item.TaskTypeKey}'. Run `cis plan task migrate-type` before re-import.").ToArray(),
                false, existing.Source);
        var accepted = impact.Findings.Where(item => item.State == "accepted").ToArray();
        var automationReferences = _automationScanner.Scan(change.RepositoryPath);
        var built = FeatureIssuePackBuilder.Build(change.RepositoryPath, request.FeatureSpecPath, change.Id, accepted, _taskTypes,
            capabilityStatus.Selections, existing.WorkItems, automationReferences);
        if (built.Pack is null)
        {
            PersistCapabilityConflicts(change, built.Conflicts);
            return new PlanResult("invalid", change.Id, existing.Status, existing.WorkItems, null,
                built.Errors, false, existing.Source);
        }

        var pack = built.Pack;
        if (approvedPlan && (existing.Source is null
            || !string.Equals(existing.Source.Path, pack.Source.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return Error(change.Id, "An approved plan cannot import a different feature specification path.");
        }
        var revisedApprovedSource = approvedPlan
            && existing.Source is not null
            && !string.Equals(existing.Source.Sha256, pack.Source.Sha256, StringComparison.OrdinalIgnoreCase);
        var workItems = pack.WorkItems;
        var source = pack.Source;
        var planStatus = approvedPlan && !revisedApprovedSource ? "Approved" : "Draft";
        var content = RenderPlan(change, planStatus, workItems, source);
        var path = _changes.DossierFile(change, "plan.md");
        var applied = WriteIfChanged(path, content);
        applied |= WriteIfChanged(_changes.DossierFile(change, "test-cases.md"), pack.ManualTestCasesMarkdown);
        applied |= WriteIfChanged(_changes.DossierFile(change, "test-cases.csv"), pack.ManualTestCasesCsv);
        var taskRoot = Path.Combine(Path.GetDirectoryName(path)!, "agent-tasks");
        Directory.CreateDirectory(taskRoot);
        foreach (var task in pack.TaskDocuments)
        {
            var taskPath = Path.Combine(Path.GetDirectoryName(path)!, task.Key.Replace('/', Path.DirectorySeparatorChar));
            var restored = RestoreRetiredTask(change, task.Key);
            applied |= restored;
            applied |= WriteTaskPreservingEvidence(taskPath, task.Value, preserveStatus: !restored);
        }
        applied |= RetireObsoleteTasks(change, existing.WorkItems, workItems, source);
        applied |= RegisterFeatureSpecCatalogEntry(change, source);
        applied |= RegisterManualTestCasesCatalogEntry(change);
        applied |= RegisterTaskCatalogEntries(change, workItems);
        if (applied)
        {
            if (revisedApprovedSource)
            {
                _changes.AppendEvent(change, "plan-approval-invalidated", new Dictionary<string, string>
                {
                    ["source"] = source.Path,
                    ["previousSha256"] = existing.Source!.Sha256,
                    ["currentSha256"] = source.Sha256,
                    ["reason"] = "The canonical feature specification changed after plan approval; the regenerated plan requires renewed human approval.",
                });
            }
            _changes.AppendEvent(change, "feature-spec-plan-built", new Dictionary<string, string>
            {
                ["source"] = source.Path,
                ["sourceSha256"] = source.Sha256,
                ["requirements"] = source.Requirements.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["manualTestCases"] = pack.ManualTestCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["automatedTestCases"] = pack.AutomatedManualTestCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["workItems"] = workItems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        var validation = ValidateInternal(change, workItems, planStatus, source);
        return new PlanResult(applied ? "imported" : "unchanged", change.Id, planStatus, workItems,
            validation, [], applied, source);
    }

    public PlanResult DeriveFromApprovedFeature(FeatureSpecImportRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null)
            return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");

        var applicable = _featureAuthorities
            .Select(authority => authority.Evaluate(request.RepositoryPath, request.FeatureSpecPath))
            .Where(result => result.Applicable)
            .ToArray();
        if (applicable.Length == 0)
            return Blocked(change.Id,
                "No current governed feature approval applies to this specification. Use explicit impact disposition and `cis plan approve`.");
        if (applicable.Length > 1)
            return Blocked(change.Id, "Multiple feature-approval authorities apply; resolve the authority conflict before planning.");
        var feature = applicable[0];
        if (!feature.Ready)
            return Blocked(change.Id, feature.Errors.Count > 0
                ? feature.Errors.ToArray()
                : ["The feature approval is not current enough to carry forward."]);

        var dossier = Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!;
        var context = _resolver.Resolve(request.RepositoryPath).Context;
        if (context is null) return Error(change.Id, "The repository context could not be resolved.");
        Dictionary<string, byte[]> originals;
        byte[] catalog;
        try
        {
            originals = SnapshotTree(dossier);
            catalog = File.ReadAllBytes(context.CatalogPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error(change.Id, $"Could not snapshot the planning state before derivation: {exception.Message}");
        }
        PlanResult Rollback(PlanResult failure)
        {
            RestoreTree(dossier, originals);
            File.WriteAllBytes(context.CatalogPath, catalog);
            return failure;
        }

        try
        {
            var impact = _impacts.CarryForwardApprovedFeature(
                request.RepositoryPath,
                change.Id,
                feature.ItemId!,
                feature.FeaturePath!,
                feature.ApprovedContentHash!,
                feature.Reviewer!);
            if (impact.ExitCode != 0)
                return Rollback(Blocked(change.Id, impact.Diagnostics
                    .Select(message => message.StartsWith("ERROR: ", StringComparison.Ordinal) ? message[7..] : message)
                    .ToArray()));

            var parsedFeature = ParseFeatureSpec(request.RepositoryPath, request.FeatureSpecPath);
            if (parsedFeature.Spec is null)
                return Rollback(Blocked(change.Id, parsedFeature.Errors.ToArray()));
            CarryForwardOutcomeAcceptance(change, parsedFeature.Spec);

            var imported = ImportSpec(request, "technical-intent");
            if (imported.ExitCode != 0 || imported.Validation is not { Valid: true })
                return Rollback(imported with { Status = "blocked" });

            var basis = $"approved-feature:{feature.ItemId}:{feature.ApprovedContentHash}";
            var approved = ApproveCore(
                request.RepositoryPath,
                change.Id,
                feature.Reviewer!,
                $"Authority carried forward from the approved feature {feature.ItemId}: {feature.Rationale}",
                basis);
            if (approved.ExitCode != 0)
                return Rollback(approved);

            _changes.AppendEvent(change, "delivery-plan-authority-carried-forward", new Dictionary<string, string>
            {
                ["featureItemId"] = feature.ItemId!,
                ["featurePath"] = feature.FeaturePath!,
                ["approvedContentHash"] = feature.ApprovedContentHash!,
                ["approvedBy"] = feature.Reviewer!,
            });
            return approved with { Status = "derived-and-approved" };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Rollback(Error(change.Id, $"Derived planning failed atomically: {exception.Message}"));
        }
    }

    public PlanResult Show(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var plan = ReadPlan(change);
        return new PlanResult("shown", change.Id, plan.Status, plan.WorkItems, null, [], false, plan.Source);
    }

    private void PersistCapabilityConflicts(
        ChangeDossier change,
        IReadOnlyList<TaskTypeCapabilityConflict> conflicts)
    {
        if (conflicts.Count == 0) return;
        var eventPath = _changes.DossierFile(change, "events.jsonl");
        var existing = File.Exists(eventPath) ? File.ReadAllText(eventPath) : string.Empty;
        foreach (var conflict in conflicts)
        {
            var basis = $"{conflict.CapabilityKey}|{string.Join('|', conflict.CandidateTypeKeys.Order(StringComparer.Ordinal))}|{conflict.Reason}";
            var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(basis))).ToLowerInvariant();
            if (existing.Contains(fingerprint, StringComparison.Ordinal)) continue;
            _changes.AppendEvent(change, "task-capability-conflict-detected", new Dictionary<string, string>
            {
                ["capability"] = conflict.CapabilityKey,
                ["candidates"] = string.Join(",", conflict.CandidateTypeKeys),
                ["reason"] = conflict.Reason,
                ["fingerprint"] = fingerprint,
            });
            existing += fingerprint;
        }
    }

    public TaskTypeCapabilityResult CapabilityStatus(string repositoryPath)
    {
        var read = _capabilities.Read(repositoryPath);
        if (read.Errors.Count > 0)
            return new("invalid", read.Selections, [], read.Errors, false);
        var resolution = _taskTypes.ResolveApplicable(_taskTypes.Definitions, read.Selections);
        return new(resolution.IsSuccess ? "resolved" : "conflict", read.Selections,
            resolution.Conflicts, resolution.Errors, false);
    }

    public TaskTypeCapabilityResult SelectCapability(TaskTypeCapabilitySelectRequest request)
    {
        var selected = _capabilities.Select(request, _taskTypes);
        if (selected.Errors.Count > 0) return selected;
        var resolution = _taskTypes.ResolveApplicable(_taskTypes.Definitions, selected.Selections);
        return selected with { Conflicts = resolution.Conflicts, Errors = resolution.Errors };
    }

    public PlanResult MigrateTaskType(TaskTypeMigrationRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            return Error(change.Id, "Task-type migration requires human reviewer identity and rationale.");
        var plan = ReadPlan(change);
        var item = plan.WorkItems.FirstOrDefault(candidate => candidate.Id.Equals(request.TaskId, StringComparison.OrdinalIgnoreCase));
        if (item is null) return Error(change.Id, $"Task was not found: {request.TaskId}");
        if (item.TaskTypeKey.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, $"Core task types are non-replaceable: {item.TaskTypeKey}");
        var target = _taskTypes.Find(request.TargetTypeKey);
        if (target is null) return Error(change.Id, $"Target task type is not registered: {request.TargetTypeKey}");
        if (target.Key.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
            return Error(change.Id, "Extension task instances cannot migrate into a reserved core task type.");

        var read = _capabilities.Read(change.RepositoryPath);
        if (read.Errors.Count > 0) return new PlanResult("invalid", change.Id, plan.Status, plan.WorkItems, null,
            read.Errors, false, plan.Source);
        var selection = read.Selections.FirstOrDefault(candidate =>
            candidate.CapabilityKey.Equals(target.CapabilityKey, StringComparison.OrdinalIgnoreCase)
            && candidate.SelectedTypeKey.Equals(target.Key, StringComparison.OrdinalIgnoreCase));
        if (selection is null)
            return Error(change.Id, $"Repository capability '{target.CapabilityKey}' does not select task type '{target.Key}'.");
        var sameKey = item.TaskTypeKey.Equals(target.Key, StringComparison.OrdinalIgnoreCase);
        if (!sameKey
            && (!selection.ReplacesTypeKeys.Contains(item.TaskTypeKey, StringComparer.OrdinalIgnoreCase)
                || !(target.ReplacesTypeKeys ?? []).Contains(item.TaskTypeKey, StringComparer.OrdinalIgnoreCase)))
            return Error(change.Id, $"Selection and provider definition do not both authorize replacement of '{item.TaskTypeKey}' by '{target.Key}'.");
        if (sameKey && item.TaskTypeVersion.Equals(target.Version, StringComparison.OrdinalIgnoreCase))
            return new PlanResult("unchanged", change.Id, plan.Status, plan.WorkItems, null, [], false, plan.Source);

        var byType = plan.WorkItems.GroupBy(candidate => candidate.TaskTypeKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var missingDependencies = target.DependsOnTypeKeys.Where(key => !byType.ContainsKey(key)).ToArray();
        if (missingDependencies.Length > 0)
            return Error(change.Id, $"Target task type dependencies are not instantiated: {string.Join(", ", missingDependencies)}");
        var dependencies = item.DependsOn
            .Concat(target.DependsOnTypeKeys.Select(key => byType[key]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var migrated = item with
        {
            Category = target.Category,
            Title = target.Title,
            DependsOn = dependencies,
            AcceptanceCriteria = target.AcceptanceCriteria,
            Validation = target.Validation,
            Status = "Draft",
            TaskTypeKey = target.Key,
            TaskTypeVersion = target.Version,
            ApprovalGate = target.ApprovalGate,
        };
        var updatedItems = plan.WorkItems.Select(candidate => candidate.Id == item.Id ? migrated : candidate).ToArray();
        var planPath = _changes.DossierFile(change, "plan.md");
        File.WriteAllText(planPath, RenderPlan(change, "Draft", updatedItems, plan.Source));
        if (item.TaskPath is null) return Error(change.Id, "Task-type migration requires a durable task document.");
        var taskPath = Path.Combine(Path.GetDirectoryName(planPath)!, item.TaskPath.Replace('/', Path.DirectorySeparatorChar));
        var task = File.ReadAllText(taskPath);
        task = ReplaceFrontMatter(task, "title", JsonSerializer.Serialize(item.Id + " " + target.Title));
        task = ReplaceFrontMatter(task, "task_status", "Draft");
        task = ReplaceFrontMatter(task, "task_type", target.Key);
        task = ReplaceFrontMatter(task, "task_type_version", target.Version);
        task = ReplaceFrontMatter(task, "category", target.Category);
        task = ReplaceFrontMatter(task, "depends_on", JsonSerializer.Serialize(dependencies));
        task = ReplaceFrontMatter(task, "approval_gate", target.ApprovalGate);
        task = Regex.Replace(task, @"(?m)^#\s+" + Regex.Escape(item.Id) + @":.*$",
            $"# {item.Id}: {target.Title}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var currentContract = "## Current task-type contract\n\n" +
            $"Provider: `{target.ProviderKey}`. Capability: `{target.CapabilityKey}`. Type: `{target.Key}` version `{target.Version}`.\n\n" +
            $"- [ ] Acceptance: {target.AcceptanceCriteria}\n" +
            $"- [ ] Validation: {target.Validation}\n";
        task = ReplaceMarkdownSection(task, "## Current task-type contract", currentContract);
        task = AppendMigrationHistory(task, item, target, request.Reviewer.Trim(), request.Rationale.Trim());
        File.WriteAllText(taskPath, task);
        _changes.AppendEvent(change, "task-type-migrated", new Dictionary<string, string>
        {
            ["taskId"] = item.Id,
            ["fromType"] = item.TaskTypeKey,
            ["fromVersion"] = item.TaskTypeVersion,
            ["toType"] = target.Key,
            ["toVersion"] = target.Version,
            ["actor"] = request.Reviewer.Trim(),
            ["reason"] = request.Rationale.Trim(),
        });
        var validation = ValidateInternal(change, updatedItems, "Draft", plan.Source);
        return new PlanResult("task-type-migrated", change.Id, "Draft", updatedItems, validation, [], true, plan.Source);
    }

    public PlanResult Validate(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var plan = ReadPlan(change);
        var validation = ValidateInternal(change, plan.WorkItems, plan.Status, plan.Source);
        return new PlanResult(validation.Valid ? "valid" : "invalid", change.Id, plan.Status, plan.WorkItems, validation, [], false, plan.Source);
    }

    public PlanResult Approve(string repositoryPath, string changeId)
        => Approve(repositoryPath, changeId, Environment.UserName, "Explicit plan approval recorded by the caller.");

    public PlanResult Approve(string repositoryPath, string changeId, string reviewer, string rationale)
        => ApproveCore(repositoryPath, changeId, reviewer, rationale, null);

    private PlanResult ApproveCore(
        string repositoryPath,
        string changeId,
        string reviewer,
        string rationale,
        string? approvalBasis)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(rationale))
        {
            return Error(change.Id, "Plan approval requires reviewer identity and rationale.");
        }

        var plan = ReadPlan(change);
        var validation = ValidateInternal(change, plan.WorkItems, plan.Status, plan.Source);
        if (!validation.Valid)
        {
            return new PlanResult("blocked", change.Id, plan.Status, plan.WorkItems, validation, [], false, plan.Source);
        }

        if (string.Equals(plan.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return new PlanResult("unchanged", change.Id, "Approved", plan.WorkItems, validation, [], false, plan.Source);
        }

        var path = _changes.DossierFile(change, "plan.md");
        var content = File.ReadAllText(path);
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            "(?m)^status:.*$",
            "status: Approved",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var approval = $"approved_by: {JsonSerializer.Serialize(reviewer.Trim())}{Environment.NewLine}" +
            $"approval_rationale: {JsonSerializer.Serialize(rationale.Trim())}{Environment.NewLine}" +
            (approvalBasis is null ? string.Empty : $"approval_basis: {JsonSerializer.Serialize(approvalBasis)}{Environment.NewLine}") +
            "authority: human-approved";
        content = content.Replace("authority: human-approved", approval, StringComparison.Ordinal);
        File.WriteAllText(path, content);
        _changes.AppendEvent(change, "plan-approved", new Dictionary<string, string>
        {
            ["workItems"] = plan.WorkItems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["reviewer"] = reviewer.Trim(),
            ["rationale"] = rationale.Trim(),
            ["approvalBasis"] = approvalBasis ?? "direct-human-plan-review",
        });
        return new PlanResult("approved", change.Id, "Approved", plan.WorkItems, validation, [], true, plan.Source);
    }

    private static Dictionary<string, byte[]> SnapshotTree(string root)
        => CisPathSafety.EnumerateFiles(root)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static void RestoreTree(string root, IReadOnlyDictionary<string, byte[]> snapshot)
    {
        foreach (var path in CisPathSafety.EnumerateFiles(root).ToArray())
        {
            var relative = Path.GetRelativePath(root, path);
            if (!snapshot.ContainsKey(relative)) File.Delete(path);
        }
        foreach (var item in snapshot)
        {
            var path = Path.Combine(root, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, item.Value);
        }
    }

    public PlanResult Status(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var plan = ReadPlan(change);
        var validation = plan.WorkItems.Count == 0 ? null : ValidateInternal(change, plan.WorkItems, plan.Status, plan.Source);
        return new PlanResult("status", change.Id, plan.Status, plan.WorkItems, validation, [], false, plan.Source);
    }

    public PlanResult TransitionTask(PlanTaskTransitionRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null) return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
            return Error(change.Id, "Task transitions require actor and reason.");
        var plan = ReadPlan(change);
        var sourceCurrencyErrors = ValidateSourceCurrency(change, plan.Source);
        if (sourceCurrencyErrors.Count > 0)
            return Error(change.Id, sourceCurrencyErrors.ToArray());
        var item = plan.WorkItems.FirstOrDefault(candidate => candidate.Id.Equals(request.TaskId, StringComparison.OrdinalIgnoreCase));
        if (item is null) return Error(change.Id, $"Task was not found: {request.TaskId}");
        var target = CanonicalTaskStatus(request.Status);
        if (target is null) return Error(change.Id, $"Unsupported task status: {request.Status}");
        var current = CanonicalTaskStatus(item.Status) ?? (item.Status.Equals("decomposed", StringComparison.OrdinalIgnoreCase) ? "Decomposed" : "Draft");
        if (current.Equals(target, StringComparison.Ordinal))
            return new PlanResult("unchanged", change.Id, plan.Status, plan.WorkItems, null, [], false, plan.Source);
        if (!AllowedTransition(current, target))
            return Error(change.Id, $"Invalid task transition: {current} -> {target}");

        if (plan.Source?.FrontendChanges == true && item.Category is not ("coordination" or "wireframe" or "design")
            && target is "ReadyForReview" or "Approved" or "InProgress" or "Complete")
        {
            var design = File.ReadAllText(_changes.DossierFile(change, "design.md"));
            var gate = Regex.Match(design, @"(?m)^gate_status:\s*(?<value>.*)$").Groups["value"].Value.Trim();
            if (!gate.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                return Error(change.Id, $"Global design gate is {gate}; downstream task work is blocked until explicit design approval.");
        }

        if (target == "Complete" && item.TaskPath is not null)
        {
            var taskPath = Path.Combine(Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!, item.TaskPath.Replace('/', Path.DirectorySeparatorChar));
            var task = File.ReadAllText(taskPath);
            if (task.Contains("| TODO | TODO | Not run |", StringComparison.Ordinal)
                || task.Contains("- [ ]", StringComparison.Ordinal))
                return Error(change.Id, "Task completion requires resolved acceptance/validation checklists and completion evidence.");
        }

        if (target == "Complete" && item.TaskTypeKey == "core.delivery.final-sweep")
        {
            var unresolved = plan.WorkItems.Where(candidate =>
                    candidate.Id != item.Id
                    && candidate.Category != "coordination"
                    && !PlanTaskDispositionPolicy.IsTerminal(candidate.Status, candidate.Category))
                .Select(candidate => $"{candidate.Id}:{candidate.Status}")
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unresolved.Length > 0)
                return Error(change.Id, "Final Delivery Sweep requires a terminal disposition for every delivery task: " + string.Join(", ", unresolved));
        }

        if (target == "Complete" && item.TaskTypeKey == "core.coordination.scope-guard")
        {
            var finalSweep = plan.WorkItems.FirstOrDefault(candidate =>
                candidate.TaskTypeKey == "core.delivery.final-sweep");
            if (finalSweep is null || !CanonicalStatus(finalSweep.Status).Equals("Complete", StringComparison.Ordinal))
                return Error(change.Id, "Coordination final acceptance requires a completed Final Delivery Sweep.");
            var unresolved = plan.WorkItems.Where(candidate =>
                    candidate.Id != item.Id
                    && !PlanTaskDispositionPolicy.IsTerminal(candidate.Status, candidate.Category))
                .Select(candidate => $"{candidate.Id}:{candidate.Status}")
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unresolved.Length > 0)
                return Error(change.Id, "Coordination final acceptance requires every child to have a terminal disposition: " + string.Join(", ", unresolved));
        }

        var updatedItems = plan.WorkItems.Select(candidate => candidate.Id == item.Id ? candidate with { Status = target } : candidate).ToArray();
        File.WriteAllText(_changes.DossierFile(change, "plan.md"), RenderPlan(change, plan.Status, updatedItems, plan.Source));
        if (item.TaskPath is not null)
        {
            var taskPath = Path.Combine(Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!, item.TaskPath.Replace('/', Path.DirectorySeparatorChar));
            var task = File.ReadAllText(taskPath);
            task = Regex.Replace(task, @"(?m)^task_status:.*$", $"task_status: {target}",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            File.WriteAllText(taskPath, task);
            if (target == "Complete") SnapshotToolUsage(change, item, taskPath);
        }
        _changes.AppendEvent(change, "task-status-changed", new Dictionary<string, string>
        {
            ["taskId"] = item.Id, ["from"] = current, ["to"] = target,
            ["actor"] = request.Actor.Trim(), ["reason"] = request.Reason.Trim(),
        });
        var validation = ValidateInternal(change, updatedItems, plan.Status, plan.Source);
        return new PlanResult("task-transitioned", change.Id, plan.Status, updatedItems, validation, [], true, plan.Source);
    }

    private void SnapshotToolUsage(ChangeDossier change, PlanWorkItem item, string taskPath)
    {
        if (_toolUsage is null) return;
        var usage = _toolUsage.Read(change.RepositoryPath, limit: 10_000);
        if (usage.ExitCode != 0 || usage.Entries.Count == 0) return;
        var ordered = usage.Entries.OrderBy(entry => entry.StartedAtUtc).ToArray();
        var digestSource = string.Join('\n', ordered.Select(entry =>
            $"{entry.InvocationId}|{entry.StartedAtUtc:O}|{entry.Command}|{entry.ExitCode}|{entry.PossibleTokenSavings}"));
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestSource))).ToLowerInvariant();
        var savings = ordered.Sum(entry => entry.PossibleTokenSavings);
        var failed = ordered.Count(entry => entry.ExitCode != 0);
        var snapshot = $"invocations={ordered.Length}; failed={failed}; possibleTokenSavings={savings}; ledgerDigest={digest}";

        var task = File.ReadAllText(taskPath);
        if (!task.Contains(digest, StringComparison.Ordinal))
        {
            task = AppendTableRow(task, "## Completion evidence",
                $"| CIS tool-usage snapshot | `{ToolUsageStore.RelativeLedgerPath}` | Recorded | {snapshot} |");
            File.WriteAllText(taskPath, task);
        }

        var verificationPath = _changes.DossierFile(change, "verification.md");
        var verification = File.ReadAllText(verificationPath);
        if (!verification.Contains(digest, StringComparison.Ordinal))
        {
            verification = verification.TrimEnd() + Environment.NewLine
                + $"| {Cell(item.Id)} | CIS tool-usage snapshot | `{ToolUsageStore.RelativeLedgerPath}` | Recorded | {snapshot} |"
                + Environment.NewLine;
            File.WriteAllText(verificationPath, verification);
        }
    }

    private static string AppendTableRow(string content, string heading, string row)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return content.TrimEnd() + Environment.NewLine + Environment.NewLine + heading + Environment.NewLine + Environment.NewLine + row + Environment.NewLine;
        var next = content.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        if (next < 0) next = content.Length;
        return content[..next].TrimEnd() + Environment.NewLine + row + Environment.NewLine + content[next..].TrimStart('\r', '\n');
    }

    private static string Cell(string value) => value.Replace('|', '/').Replace("\r", " ").Replace("\n", " ").Trim();

    private static string? CanonicalTaskStatus(string value) => value.Trim().ToLowerInvariant() switch
    {
        "draft" or "not-started" => "Draft",
        "readyforreview" or "ready-for-review" => "ReadyForReview",
        "approved" => "Approved",
        "inprogress" or "in-progress" => "InProgress",
        "blocked" => "Blocked",
        "deferred" => "Deferred",
        "complete" or "completed" => "Complete",
        "cancelled" or "canceled" => "Cancelled",
        "decomposed" => "Decomposed",
        _ => null,
    };

    private static string CanonicalStatus(string value)
        => CanonicalTaskStatus(value)
            ?? (value.Equals("decomposed", StringComparison.OrdinalIgnoreCase) ? "Decomposed" : value);

    private static bool AllowedTransition(string current, string target) => (current, target) switch
    {
        ("Draft", "ReadyForReview" or "InProgress" or "Blocked" or "Cancelled") => true,
        ("ReadyForReview", "Approved" or "InProgress" or "Blocked" or "Cancelled") => true,
        ("Approved", "InProgress" or "Blocked" or "Cancelled") => true,
        ("InProgress", "ReadyForReview" or "Complete" or "Blocked" or "Deferred" or "Cancelled") => true,
        ("Blocked", "InProgress" or "Deferred" or "Cancelled") => true,
        ("Deferred", "InProgress" or "Cancelled") => true,
        ("Decomposed", "Complete" or "Blocked" or "Deferred" or "Cancelled") => true,
        _ => false,
    };

    private PlanValidation ValidateInternal(
        ChangeDossier change,
        IReadOnlyList<PlanWorkItem> workItems,
        string status,
        PlanSource? source = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var impact = _impacts.ReadImpact(change);
        var completeness = ImpactAnalysisService.CalculateCompleteness(impact.Findings, impact.Truncated);
        if (!completeness.PlanReady)
        {
            errors.AddRange(completeness.Gaps);
        }

        var accepted = impact.Findings.Where(item => item.State == "accepted").Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var covered = workItems.SelectMany(item => item.ImpactIds).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in accepted.Except(covered).Order(StringComparer.Ordinal))
        {
            errors.Add($"Accepted impact is not covered by a work item: {missing}");
        }

        foreach (var unexpected in covered.Except(accepted).Order(StringComparer.Ordinal))
        {
            errors.Add($"Work item references an impact that is not accepted: {unexpected}");
        }

        var ids = workItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != workItems.Count)
        {
            errors.Add("Work item IDs must be unique.");
        }

        foreach (var item in workItems)
        {
            if (item.ImpactIds.Count == 0 || string.IsNullOrWhiteSpace(item.AcceptanceCriteria) || string.IsNullOrWhiteSpace(item.Validation))
            {
                errors.Add($"{item.Id} requires impact coverage, acceptance criteria, and validation.");
            }

            foreach (var dependency in item.DependsOn.Where(dependency => !ids.Contains(dependency)))
            {
                errors.Add($"{item.Id} references missing dependency {dependency}.");
            }

            if (item.Complexity is not ("low" or "medium" or "high"))
            {
                errors.Add($"{item.Id} has invalid complexity '{item.Complexity}'.");
            }

            if (item.ParentId is not null && !ids.Contains(item.ParentId))
            {
                errors.Add($"{item.Id} references missing parent {item.ParentId}.");
            }

            if (source is not null)
            {
                ValidateTaskDocument(change, item, errors, source.PublicEndpoints);
                var registered = _taskTypes.Find(item.TaskTypeKey);
                if (registered is null)
                {
                    errors.Add($"{item.Id} uses unavailable task type provider definition '{item.TaskTypeKey}@{item.TaskTypeVersion}'.");
                }
                else if (!registered.Version.Equals(item.TaskTypeVersion, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"{item.Id} uses task type version {item.TaskTypeVersion}; loaded provider supplies {registered.Version}. Re-import or explicitly migrate before completion.");
                }
            }
        }

        foreach (var high in workItems.Where(item => item.Complexity == "high"))
        {
            var children = workItems.Count(item => item.ParentId == high.Id);
            if (children < 2)
            {
                errors.Add($"{high.Id} is high complexity and must be a parent with at least two child tasks.");
            }
        }

        if (source is not null)
        {
            var workspace = _workspaceRegistry?.Resolve(change.RepositoryPath);
            if (workspace is { IsSuccess: true, Workspace: not null })
            {
                var deliveryIds = workspace.Workspace.DeliveryRepositories.Select(repository => repository.Id)
                    .ToHashSet(StringComparer.Ordinal);
                if (deliveryIds.Count == 0 && workspace.Workspace.AuthorityRepository is { } authority)
                    deliveryIds.Add(authority.Id);
                var dependencyIds = workspace.Workspace.DependencyRepositories.Select(repository => repository.Id)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var target in source.Targets.Where(target => !deliveryIds.Contains(target)))
                    errors.Add(dependencyIds.Contains(target)
                        ? $"Feature delivery cannot target dependency repository '{target}'; create a separately governed dependency change or coordination record."
                        : $"Feature delivery target is outside the product boundary: {target}");
            }
            errors.AddRange(ValidateSourceCurrency(change, source));
            var coveredRequirements = workItems.SelectMany(item => item.RequirementIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in source.RequirementIds.Where(id => !coveredRequirements.Contains(id)))
            {
                errors.Add($"Feature requirement is not covered by a work item: {missing}");
            }

            foreach (var unexpected in coveredRequirements.Where(id =>
                         !source.RequirementIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add($"Work item references a requirement outside the imported feature specification: {unexpected}");
            }

            if (source.Requirements != source.RequirementIds.Count)
            {
                errors.Add("Imported feature-specification requirement provenance is inconsistent.");
            }

            if (source.FrontendChanges)
            {
                var frontendTypes = source.FrontendTypes is { Count: > 0 }
                    ? source.FrontendTypes
                    : workItems.Where(item => item.Category is "wireframe" or "design" or "frontend")
                        .Select(item => item.FrontendType ?? "customer").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var frontendType in frontendTypes)
                {
                    var wireframes = workItems.Where(item => item.Category == "wireframe"
                        && string.Equals(item.FrontendType ?? "customer", frontendType, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var designs = workItems.Where(item => item.Category == "design"
                        && string.Equals(item.FrontendType ?? "customer", frontendType, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var frontends = workItems.Where(item => item.Category == "frontend"
                        && string.Equals(item.FrontendType ?? "customer", frontendType, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var wireframe = wireframes.FirstOrDefault();
                    var design = designs.FirstOrDefault();
                    var frontend = frontends.FirstOrDefault();
                    if (wireframes.Length != 1 || designs.Length != 1 || frontends.Length != 1
                        || wireframe is null || design is null || frontend is null
                        || !design.DependsOn.Contains(wireframe.Id, StringComparer.Ordinal)
                        || !frontend.DependsOn.Contains(design.Id, StringComparer.Ordinal))
                    {
                        errors.Add($"UI-bearing `{frontendType}` scope requires a matched coordination -> wireframe -> design -> frontend ordering.");
                    }
                }
                var designIds = workItems.Where(item => item.Category == "design").Select(item => item.Id).ToArray();
                foreach (var downstream in workItems.Where(item => item.Category is not ("coordination" or "wireframe" or "design")))
                {
                    var required = downstream.FrontendType is null
                        ? designIds
                        : workItems.Where(item => item.Category == "design"
                            && string.Equals(item.FrontendType, downstream.FrontendType, StringComparison.OrdinalIgnoreCase)).Select(item => item.Id);
                    foreach (var designId in required.Where(id => !downstream.DependsOn.Contains(id, StringComparer.Ordinal)))
                    {
                        errors.Add($"{downstream.Id} must depend on approved design {designId} as the global UI delivery gate.");
                    }
                }

                var wireframePath = _changes.DossierFile(change, "wireframes.md");
                if (!File.Exists(wireframePath)) errors.Add("UI-bearing scope requires canonical wireframes.md.");
                var designPath = _changes.DossierFile(change, "design.md");
                if (!File.Exists(designPath))
                {
                    errors.Add("Frontend feature scope requires a durable design.md approval record.");
                }
                else
                {
                    var designContent = File.ReadAllText(designPath);
                    foreach (var section in new[]
                    {
                        "approval_status:", "gate_status:", "## Inputs and renderer",
                        "## Application shell and component templates", "## Required states",
                        "## PNG manifest", "## Guideline conformance and deviations",
                        "## Rejected revisions", "## Approval decision",
                    })
                    {
                        if (!designContent.Contains(section, StringComparison.Ordinal))
                        {
                            errors.Add($"Design approval record is missing required content: {section}");
                        }
                    }
                }
            }

            if (source.PublicEndpoints)
            {
                foreach (var category in new[] { "security", "contract", "backend", "observability", "verification", "assurance" })
                {
                    if (!workItems.Any(item => item.Category == category))
                    {
                        errors.Add($"PUBLIC-ENDPOINT-CACHE requires a {category} task for unauthenticated endpoint scope.");
                    }
                }
            }

            if (workItems.Count(item => item.TaskTypeKey == "core.coordination.scope-guard") != 1)
            {
                errors.Add("Imported feature plans require exactly one registered Coordination / scope guard task type.");
            }
            var finalSweeps = workItems.Where(item => item.TaskTypeKey == "core.delivery.final-sweep").ToArray();
            var finalSweep = finalSweeps.FirstOrDefault();
            if (finalSweeps.Length != 1 || finalSweep is null)
            {
                errors.Add("Imported feature plans require exactly one Final Delivery Sweep task type.");
            }
            else
            {
                foreach (var predecessor in workItems.Where(item =>
                             item.Id != finalSweep.Id
                             && item.TaskTypeKey != "core.coordination.scope-guard"
                             && !finalSweep.DependsOn.Contains(item.Id, StringComparer.Ordinal)))
                {
                    errors.Add($"{finalSweep.Id} must directly depend on {predecessor.Id} before Coordination may accept the feature.");
                }
            }
            foreach (var duplicateType in workItems.GroupBy(item => $"{item.TaskTypeKey}|{item.FrontendType ?? "none"}", StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                errors.Add($"Task type/frontend classification is instantiated more than once without provider-qualified decomposition: {duplicateType.Key}");
            }

            if (!File.Exists(_changes.DossierFile(change, "verification.md")))
            {
                errors.Add("Imported feature plans require a durable verification.md evidence ledger.");
            }
            ValidateManualTestCaseArtifacts(change, source, errors);
        }

        if (HasCycle(workItems))
        {
            errors.Add("Work item dependencies contain a cycle.");
        }

        var decisionRead = _decisions.Read(change);
        errors.AddRange(decisionRead.Errors);
        var openDecisions = decisionRead.BlockingOpen;
        if (openDecisions > 0)
        {
            errors.Add($"{openDecisions} blocking decision(s) remain open or deferred.");
        }

        if (decisionRead.AdvisoryOpen > 0)
        {
            warnings.Add($"{decisionRead.AdvisoryOpen} advisory decision(s) remain open or deferred.");
        }

        if (workItems.Count == 0)
        {
            errors.Add("The plan has no bounded work items.");
        }

        if (!HasOutcomeAcceptanceCriteria(change))
        {
            errors.Add("The change proposal must define outcome-level acceptance criteria before plan approval.");
        }

        if (string.Equals(status, "NotBuilt", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("The plan has not been built.");
        }

        return new PlanValidation(errors.Count == 0, errors, warnings, accepted.Count, covered.Intersect(accepted).Count(), openDecisions);
    }

    private void ValidateManualTestCaseArtifacts(ChangeDossier change, PlanSource source, ICollection<string> errors)
    {
        var markdownPath = _changes.DossierFile(change, "test-cases.md");
        var csvPath = _changes.DossierFile(change, "test-cases.csv");
        if (!File.Exists(markdownPath))
        {
            errors.Add("Imported feature plans require a human-readable test-cases.md manual-test catalogue.");
            return;
        }
        if (!File.Exists(csvPath))
        {
            errors.Add("Imported feature plans require a test-cases.csv test-management import projection.");
            return;
        }

        var markdown = File.ReadAllText(markdownPath);
        var csv = File.ReadAllText(csvPath);
        if (!Regex.IsMatch(markdown, @"(?im)^type:\s*manual-test-cases\s*$"))
            errors.Add("test-cases.md must declare type: manual-test-cases.");
        if (!markdown.Contains($"feature_spec_path: {JsonSerializer.Serialize(source.Path)}", StringComparison.Ordinal)
            || !markdown.Contains($"feature_spec_sha256: {JsonSerializer.Serialize(source.Sha256)}", StringComparison.Ordinal))
            errors.Add("Manual test cases are stale relative to the imported feature specification.");
        if (!Regex.IsMatch(markdown, $@"(?im)^test_case_count:\s*{source.Requirements}\s*$"))
            errors.Add($"Manual test case count must equal the {source.Requirements} imported requirements.");

        var expectedHeader = string.Join(',', new[]
        {
            "ID", "Title", "Section", "Priority", "Type", "Preconditions", "Steps",
            "Expected Result", "References", "Frontend Type", "Automation Status", "Automated Test References",
        }.Select(value => $"\"{value}\""));
        if (!csv.StartsWith(expectedHeader + "\r\n", StringComparison.Ordinal))
            errors.Add("test-cases.csv does not use the supported portable test-management header.");
        var actualCsvSha = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant();
        if (!markdown.Contains($"csv_sha256: {JsonSerializer.Serialize(actualCsvSha)}", StringComparison.Ordinal))
            errors.Add("test-cases.csv differs from the projection bound by test-cases.md; rerun feature planning.");
        foreach (var requirementId in source.RequirementIds)
        {
            if (!markdown.Contains($"`{requirementId}`", StringComparison.Ordinal)
                || !csv.Contains($"\"{requirementId.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", StringComparison.Ordinal))
                errors.Add($"Manual test artifacts do not cover feature requirement: {requirementId}");
        }
    }

    public IReadOnlyList<string> ValidateManualTestAutomationCoverage(string repositoryPath, string changeId)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null) return [$"Change dossier was not found: {changeId}"];
        var markdownPath = _changes.DossierFile(change, "test-cases.md");
        if (!File.Exists(markdownPath)) return ["Automated-test traceability requires test-cases.md."];

        var markdown = File.ReadAllText(markdownPath);
        var matches = Regex.Matches(markdown, @"(?im)^###\s+(?<id>TC-[A-Z0-9]+(?:-[A-Z0-9]+)*):[^\r\n]*\r?$")
            .Cast<Match>()
            .ToArray();
        if (matches.Length == 0) return ["Automated-test traceability requires at least one generated TC-* test case."];

        var discovered = _automationScanner.Scan(change.RepositoryPath);
        var errors = new List<string>();
        for (var index = 0; index < matches.Length; index++)
        {
            var id = matches[index].Groups["id"].Value.ToUpperInvariant();
            var sectionEnd = index + 1 < matches.Length ? matches[index + 1].Index : markdown.Length;
            var section = markdown[matches[index].Index..sectionEnd];
            var references = discovered.TryGetValue(id, out var found) ? found : [];
            if (references.Count == 0)
            {
                errors.Add($"Manual test case {id} is not referenced by any recognized automated test source.");
                continue;
            }

            if (!Regex.IsMatch(section, @"(?im)^- Automation status:\s*Automated\s*$"))
            {
                errors.Add($"Manual test case {id} has automated coverage but its catalogue is stale; rerun `cis plan import-spec`.");
                continue;
            }

            foreach (var reference in references.Where(reference => !section.Contains($"`{reference}`", StringComparison.Ordinal)))
            {
                errors.Add($"Manual test case {id} is missing current automated-test reference {reference}; rerun `cis plan import-spec`.");
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> ValidateSourceCurrency(ChangeDossier change, PlanSource? source)
    {
        if (source is null) return [];
        if (!CisPathSafety.TryResolveUnderRoot(change.RepositoryPath, source.Path, out var sourcePath)
            || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, sourcePath))
            return [$"Imported feature specification path escapes the repository or traverses a linked directory: {source.Path}"];
        if (!File.Exists(sourcePath))
            return [$"Imported feature specification no longer exists: {source.Path}"];

        var currentSha256 = "sha256:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath)))).ToLowerInvariant();
        if (!string.Equals(currentSha256, source.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return [$"Imported feature specification changed after plan generation: {source.Path}. "
                + "Run `cis plan import-spec` with the same path to regenerate a Draft plan for renewed approval."];
        }

        return [];
    }

    private static IReadOnlyList<PlanWorkItem> BuildWorkItems(IReadOnlyList<ImpactFinding> accepted)
    {
        var ordered = accepted
            .OrderBy(item => CategoryOrder(item.Category))
            .ThenBy(item => item.Target, StringComparer.Ordinal)
            .ToArray();
        var items = new List<PlanWorkItem>();
        foreach (var finding in ordered)
        {
            var id = $"WORK-{items.Count + 1:000}";
            var dependencies = items
                .Where(item => CategoryOrder(item.Category) < CategoryOrder(finding.Category))
                .Where(item => CategoryOrder(finding.Category) - CategoryOrder(item.Category) <= 1
                    || finding.Category == "verification")
                .Select(item => item.Id)
                .ToArray();
            items.Add(new PlanWorkItem(
                id,
                finding.Category,
                "medium",
                null,
                null,
                $"Address {finding.Category} impact: {finding.Label}",
                [],
                [finding.Id],
                dependencies,
                $"The accepted impact {finding.Id} is satisfied without unrecorded scope expansion.",
                ValidationFor(finding),
                "not-started"));
        }

        return items;
    }

    private static IReadOnlyList<PlanWorkItem> BuildFeatureSpecWorkItems(
        IReadOnlyList<ImpactFinding> accepted,
        FeatureSpecification spec)
    {
        var items = new List<PlanWorkItem>();
        foreach (var requirement in spec.Requirements)
        {
            var parentId = requirement.Complexity == "high"
                ? Add(
                    "coordination",
                    "high",
                    null,
                    $"Decompose {requirement.Id}: {requirement.Text}",
                    [requirement.Id],
                    accepted.Select(item => item.Id).ToArray(),
                    [],
                    $"All child tasks collectively satisfy: {requirement.AcceptanceCriteria}",
                    "Validate every child task and demonstrate the requirement-level acceptance criteria end to end.",
                    "decomposed")
                : null;
            var dependencies = new List<string>();

            if (requirement.Frontend)
            {
                var wireframe = Add(
                    "wireframe",
                    "low",
                    parentId,
                    $"Create wireframes for {requirement.Id}",
                    [requirement.Id],
                    RelevantImpacts(accepted, "implementation"),
                    [],
                    "Wireframes cover the primary flow, loading, empty, error, permission-denied, and responsive states relevant to the requirement.",
                    "Review the wireframes against the feature scenarios and requirement acceptance criteria.",
                    "not-started");
                var design = Add(
                    "design",
                    "medium",
                    parentId,
                    $"Define visual and interaction design for {requirement.Id}",
                    [requirement.Id],
                    RelevantImpacts(accepted, "implementation"),
                    [wireframe],
                    "The reviewed design specifies reusable components, interaction states, responsive behavior, and applicable accessibility requirements.",
                    "Review component states, keyboard and assistive-technology behavior, contrast, focus, and responsive layouts.",
                    "not-started");
                dependencies.Add(design);
            }

            string? contract = null;
            if (requirement.Backend && requirement.HasContract)
            {
                contract = Add(
                    "contract",
                    requirement.Complexity == "low" ? "low" : "medium",
                    parentId,
                    $"Define contracts and state changes for {requirement.Id}",
                    [requirement.Id],
                    RelevantImpacts(accepted, "contract"),
                    [],
                    "API, command, event, data, permission, and failure contracts required by the feature are explicit and backward-compatibility effects are recorded.",
                    "Validate governed reference changes and connected contract or schema checks.",
                    "not-started");
            }

            var implementationTasks = new List<string>();
            if (requirement.Backend)
            {
                var backendDependencies = contract is null ? [] : new[] { contract };
                implementationTasks.Add(Add(
                    "backend",
                    requirement.Complexity == "low" ? "low" : "medium",
                    parentId,
                    $"Implement backend behavior for {requirement.Id}",
                    [requirement.Id],
                    RelevantImpacts(accepted, "implementation"),
                    backendDependencies,
                    requirement.AcceptanceCriteria,
                    "Build the affected backend components and run focused unit, integration, contract, permission, and state-transition checks as applicable.",
                    "not-started"));
            }

            if (requirement.Frontend)
            {
                var frontendDependencies = dependencies
                    .Concat(contract is null ? [] : new[] { contract })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                implementationTasks.Add(Add(
                    "frontend",
                    requirement.Complexity == "low" ? "low" : "medium",
                    parentId,
                    $"Implement frontend behavior for {requirement.Id}",
                    [requirement.Id],
                    RelevantImpacts(accepted, "implementation"),
                    frontendDependencies,
                    requirement.AcceptanceCriteria,
                    "Build the affected frontend and run focused component, interaction, accessibility, and responsive-layout checks.",
                    "not-started"));
            }

            if (implementationTasks.Count == 0)
            {
                implementationTasks.Add(Add(
                    "implementation",
                    requirement.Complexity,
                    parentId,
                    $"Implement {requirement.Id}: {requirement.Text}",
                    [requirement.Id],
                    RelevantImpacts(accepted, "implementation"),
                    contract is null ? [] : [contract],
                    requirement.AcceptanceCriteria,
                    "Build the affected component and run its connected deterministic checks.",
                    "not-started"));
            }

            Add(
                "verification",
                requirement.Complexity == "low" ? "low" : "medium",
                parentId,
                $"Verify {requirement.Id} acceptance and regressions",
                [requirement.Id],
                RelevantImpacts(accepted, "verification"),
                implementationTasks,
                $"Automated and independent evidence demonstrates: {requirement.AcceptanceCriteria}",
                "Run focused tests, the proportionate affected suite, and an independent acceptance review; record failures and evidence.",
                "not-started");
        }

        return items;

        string Add(
            string category,
            string complexity,
            string? parentId,
            string title,
            IReadOnlyList<string> requirementIds,
            IReadOnlyList<string> impactIds,
            IReadOnlyList<string> dependsOn,
            string acceptance,
            string validation,
            string status)
        {
            var id = $"WORK-{items.Count + 1:000}";
            items.Add(new PlanWorkItem(id, category, complexity, parentId, $"agent-tasks/{id}.md", title, requirementIds,
                impactIds, dependsOn, acceptance, validation, status));
            return id;
        }
    }

    private static string[] RelevantImpacts(IReadOnlyList<ImpactFinding> accepted, string category)
    {
        var matching = accepted
            .Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id)
            .ToArray();
        return matching.Length > 0 ? matching : accepted.Select(item => item.Id).ToArray();
    }

    private static FeatureSpecParseResult ParseFeatureSpec(string repositoryPath, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath) || Path.IsPathRooted(requestedPath))
        {
            return new(null, ["Feature specification path must be repository-relative."]);
        }

        var repository = Path.GetFullPath(repositoryPath);
        var absolute = Path.GetFullPath(Path.Combine(repository, requestedPath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(repository + Path.DirectorySeparatorChar, comparison)
            || !File.Exists(absolute)
            || !string.Equals(Path.GetExtension(absolute), ".md", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, ["Feature specification must be an existing Markdown file inside the repository."]);
        }

        var relative = Path.GetRelativePath(repository, absolute).Replace('\\', '/');
        if (relative.StartsWith(".cis/local/", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, ["Derived .cis/local files cannot be imported as feature specifications."]);
        }

        var content = File.ReadAllText(absolute);
        if (!string.Equals(ReadFrontMatterValue(content, "type"), "feature-specification", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, ["Feature specification front matter must declare `type: feature-specification`."]);
        }

        var requirements = ParseRequirements(content, out var errors);
        if (requirements.Count == 0 && errors.Count == 0)
        {
            errors.Add("Feature specification contains no functional requirements.");
        }

        if (errors.Count > 0)
        {
            return new(null, errors);
        }

        var featureFrontend = HasMeaningfulSection(content, "UX and accessibility")
            || requirements.Any(requirement => IsFrontend(requirement.Text));
        var classified = requirements.Select(requirement =>
        {
            var frontend = featureFrontend || SurfaceIncludes(requirement.Surface, "frontend", "mobile", "native", "full-stack")
                || IsFrontend(requirement.Text);
            var backend = SurfaceIncludes(requirement.Surface, "backend", "api", "data", "security", "contract", "full-stack")
                || IsBackend(requirement.Text)
                || (!frontend && string.IsNullOrWhiteSpace(requirement.Surface));
            var hasContract = SurfaceIncludes(requirement.Surface, "api", "data", "security", "contract", "full-stack")
                || HasContract(requirement.Text);
            return requirement with
            {
                Frontend = frontend,
                Backend = backend,
                HasContract = hasContract,
                Complexity = ComplexityFor(requirement.Text, requirement.AcceptanceCriteria, frontend, backend),
            };
        }).ToArray();
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        return new(new FeatureSpecification(relative, hash, classified, classified.Any(item => item.Frontend)), []);
    }

    private static IReadOnlyList<FeatureRequirement> ParseRequirements(string content, out List<string> errors)
    {
        errors = [];
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var headers = SplitTableRow(lines[index]);
            var idIndex = Array.FindIndex(headers, value => value.Equals("ID", StringComparison.OrdinalIgnoreCase));
            var requirementIndex = Array.FindIndex(headers, value => value.Equals("Requirement", StringComparison.OrdinalIgnoreCase));
            var acceptanceIndex = Array.FindIndex(headers, value => value.Equals("Acceptance criteria", StringComparison.OrdinalIgnoreCase));
            var surfaceIndex = Array.FindIndex(headers, value =>
                value.Equals("Surface", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Affected surfaces", StringComparison.OrdinalIgnoreCase));
            if (idIndex < 0 || requirementIndex < 0 || acceptanceIndex < 0)
            {
                continue;
            }

            var result = new List<FeatureRequirement>();
            for (var row = index + 2; row < lines.Length && lines[row].TrimStart().StartsWith('|'); row++)
            {
                var cells = SplitTableRow(lines[row]);
                if (cells.Length <= Math.Max(idIndex, Math.Max(requirementIndex, acceptanceIndex)))
                {
                    continue;
                }

                var id = cells[idIndex].Trim();
                var requirement = cells[requirementIndex].Trim();
                var acceptance = cells[acceptanceIndex].Trim();
                var surface = surfaceIndex >= 0 && cells.Length > surfaceIndex ? cells[surfaceIndex].Trim() : null;
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(requirement))
                {
                    continue;
                }

                if (IsPlaceholder(id) || IsPlaceholder(requirement) || IsPlaceholder(acceptance))
                {
                    errors.Add($"Functional requirement row '{id}' must replace all TODO or placeholder values.");
                    continue;
                }

                if (!ValidSurfaces(surface))
                {
                    errors.Add($"Functional requirement '{id}' has unsupported surface '{surface}'.");
                    continue;
                }

                result.Add(new FeatureRequirement(id, requirement, acceptance, surface, false, false, false, "medium"));
            }

            foreach (var duplicate in result.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                errors.Add($"Functional requirement ID is duplicated: {duplicate.Key}");
            }

            return result;
        }

        errors.Add("Feature specification requires a functional-requirements table with ID, Requirement, and Acceptance criteria columns.");
        return [];
    }

    private static string? ReadFrontMatterValue(string content, string key)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        foreach (var line in lines.Skip(1).TakeWhile(line => line.Trim() != "---"))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim().Trim('"', '\'');
            }
        }

        return null;
    }

    private static bool HasMeaningfulSection(string content, string heading)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var start = Array.FindIndex(lines, line =>
            line.Trim().Equals($"## {heading}", StringComparison.OrdinalIgnoreCase));
        return start >= 0 && lines.Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Any(line => !string.IsNullOrWhiteSpace(line)
                && !IsPlaceholder(line)
                && !line.Equals("none", StringComparison.OrdinalIgnoreCase)
                && !line.Equals("not applicable", StringComparison.OrdinalIgnoreCase)
                && !line.Equals("n/a", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlaceholder(string value)
        => string.IsNullOrWhiteSpace(value)
            || Regex.IsMatch(value, @"\b(?:TODO|TBD)\b|(?i:\bTO BE COMPLETED\b)",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || Regex.IsMatch(value, @"^\s*<[^<>]+>\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string[] SplitTableRow(string line)
        => line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();

    private static bool IsFrontend(string text) => ContainsAny(text,
        "frontend", "front end", "user interface", " ui ", "screen", "page", "view", "form",
        "browser", "responsive", "accessibility", "swiftui", "compose", "angular", "react", "next.js",
        "ios", "android", "mobile", "navigation", "dashboard");

    private static bool IsBackend(string text) => ContainsAny(text,
        "backend", "back end", "api", "endpoint", "service", "server", "database", "persist",
        "repository", "command", "event", "queue", "permission", "authoriz", "authenticat", "workflow");

    private static bool HasContract(string text) => ContainsAny(text,
        "api", "endpoint", "contract", "command", "event", "schema", "database", "data", "permission",
        "authoriz", "workflow", "state", "configuration");

    private static string ComplexityFor(string requirement, string acceptance, bool frontend, bool backend)
    {
        var text = $"{requirement} {acceptance}";
        var score = 0;
        if (frontend && backend) score += 2;
        if (text.Length > 180) score++;
        if (ContainsAny(text, "migration", "backward compatibility", "integration", "third-party", "external")) score += 2;
        if (ContainsAny(text, "permission", "security", "authoriz", "payment", "personal data", "sensitive")) score++;
        if (ContainsAny(text, "event", "workflow", "state", "database", "persist")) score++;
        if (text.Split(" and ", StringSplitOptions.RemoveEmptyEntries).Length >= 4) score++;
        return score >= 4 ? "high" : score >= 2 ? "medium" : "low";
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        var normalized = $" {text.ToLowerInvariant()} ";
        return values.Any(normalized.Contains);
    }

    private static bool SurfaceIncludes(string? surface, params string[] values)
        => surface is not null && surface
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => values.Contains(item, StringComparer.OrdinalIgnoreCase));

    private static bool ValidSurfaces(string? surface)
    {
        if (string.IsNullOrWhiteSpace(surface))
        {
            return true;
        }

        var allowed = new[]
        {
            "frontend", "backend", "full-stack", "mobile", "native", "api", "contract",
            "data", "security", "delivery", "documentation",
        };
        return surface.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(item => allowed.Contains(item, StringComparer.OrdinalIgnoreCase));
    }

    private static string ValidationFor(ImpactFinding finding) => finding.Category switch
    {
        "documentation" => "Run `cis docs validate --strict` and review the canonical document diff.",
        "contract" => "Validate the governed reference and run its connected contract tests.",
        "dependency" => "Restore and build the affected component; verify package or project compatibility.",
        "delivery" => "Validate the affected workflow and its deterministic execution checks.",
        "verification" => "Run the identified test and the proportionate affected test suite.",
        _ => "Build the affected component and run its connected deterministic tests.",
    };

    private PlanDocument ReadPlan(ChangeDossier change)
    {
        var path = _changes.DossierFile(change, "plan.md");
        if (!File.Exists(path))
        {
            return new PlanDocument("NotBuilt", [], null);
        }

        var lines = File.ReadAllLines(path);
        var status = lines.FirstOrDefault(line => line.StartsWith("status:", StringComparison.Ordinal))?[7..].Trim() ?? "NotBuilt";
        var items = lines
            .Where(line => line.StartsWith("| WORK-", StringComparison.Ordinal))
            .Select(ParseWorkItem)
            .Where(item => item is not null)
            .Cast<PlanWorkItem>()
            .ToArray();
        var sourcePath = lines.FirstOrDefault(line => line.StartsWith("feature_spec_path:", StringComparison.Ordinal))?[18..].Trim();
        var sourceHash = lines.FirstOrDefault(line => line.StartsWith("feature_spec_sha256:", StringComparison.Ordinal))?[20..].Trim();
        var requirementCount = int.TryParse(
            lines.FirstOrDefault(line => line.StartsWith("feature_spec_requirements:", StringComparison.Ordinal))?[26..].Trim(),
            out var parsedCount) ? parsedCount : 0;
        var frontend = bool.TryParse(
            lines.FirstOrDefault(line => line.StartsWith("feature_spec_frontend:", StringComparison.Ordinal))?[22..].Trim(),
            out var parsedFrontend) && parsedFrontend;
        var documentType = lines.FirstOrDefault(line =>
            line.StartsWith("feature_spec_document_type:", StringComparison.Ordinal))?[27..].Trim();
        var requirementIdsValue = lines.FirstOrDefault(line =>
            line.StartsWith("feature_spec_requirement_ids:", StringComparison.Ordinal))?[29..].Trim();
        IReadOnlyList<string> requirementIds = [];
        if (!string.IsNullOrWhiteSpace(requirementIdsValue))
        {
            try
            {
                requirementIds = JsonSerializer.Deserialize<string[]>(requirementIdsValue) ?? [];
            }
            catch (JsonException)
            {
                requirementIds = [];
            }
        }
        var targets = ParseJsonArray(lines, "feature_spec_targets:");
        var stack = ParseJsonArray(lines, "feature_spec_stack:");
        var references = ParseJsonArray(lines, "feature_spec_references:");
        var frontendTypes = ParseJsonArray(lines, "feature_spec_frontend_types:");
        var publicEndpoints = bool.TryParse(
            lines.FirstOrDefault(line => line.StartsWith("feature_spec_public_endpoints:", StringComparison.Ordinal))?[30..].Trim(),
            out var parsedPublicEndpoints) && parsedPublicEndpoints;
        var source = string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(sourceHash)
            ? null
            : new PlanSource(Unquote(sourcePath), Unquote(sourceHash), Unquote(documentType ?? "feature-specification"), requirementCount, frontend, requirementIds,
                targets, stack, references, frontendTypes, publicEndpoints);
        return new PlanDocument(status, items, source);
    }

    private static PlanWorkItem? ParseWorkItem(string line)
    {
        var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
        if (cells.Length >= 18)
        {
            return new PlanWorkItem(
                cells[1], cells[2], cells[3],
                string.IsNullOrWhiteSpace(cells[4]) ? null : cells[4],
                string.IsNullOrWhiteSpace(cells[5]) ? null : cells[5],
                cells[6], SplitList(cells[7]), SplitList(cells[8]), SplitList(cells[9]),
                cells[10], cells[11], cells[12], cells[13], cells[14], cells[15], SplitList(cells[16]),
                FrontendType: FrontendTypeOrNull(cells.ElementAtOrDefault(17)));
        }

        if (cells.Length >= 14)
        {
            return new PlanWorkItem(
                cells[1],
                cells[2],
                cells[3],
                string.IsNullOrWhiteSpace(cells[4]) ? null : cells[4],
                string.IsNullOrWhiteSpace(cells[5]) ? null : cells[5],
                cells[6],
                SplitList(cells[7]),
                SplitList(cells[8]),
                SplitList(cells[9]),
                cells[10],
                cells[11],
                cells[12]);
        }

        if (cells.Length >= 13)
        {
            return new PlanWorkItem(
                cells[1], cells[2], cells[3],
                string.IsNullOrWhiteSpace(cells[4]) ? null : cells[4],
                null, cells[5], SplitList(cells[6]), SplitList(cells[7]), SplitList(cells[8]),
                cells[9], cells[10], cells[11]);
        }

        return cells.Length >= 10
            ? new PlanWorkItem(
                cells[1],
                cells[2],
                "medium",
                null,
                null,
                cells[3],
                [],
                SplitList(cells[4]),
                SplitList(cells[5]),
                cells[6],
                cells[7],
                cells[8])
            : null;
    }

    private string RenderPlan(
        ChangeDossier change,
        string status,
        IReadOnlyList<PlanWorkItem> workItems,
        PlanSource? source = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(change.Id + " delivery plan")}");
        builder.AppendLine("type: delivery-plan");
        builder.AppendLine($"status: {status}");
        builder.AppendLine($"change_id: {change.Id}");
        builder.AppendLine($"graph_build_id: {JsonSerializer.Serialize(change.GraphBuildId)}");
        if (source is not null)
        {
            builder.AppendLine($"feature_spec_path: {JsonSerializer.Serialize(source.Path)}");
            builder.AppendLine($"feature_spec_sha256: {JsonSerializer.Serialize(source.Sha256)}");
            builder.AppendLine($"feature_spec_document_type: {JsonSerializer.Serialize(source.DocumentType)}");
            builder.AppendLine($"feature_spec_requirements: {source.Requirements}");
            builder.AppendLine($"feature_spec_frontend: {source.FrontendChanges.ToString().ToLowerInvariant()}");
            builder.AppendLine($"feature_spec_requirement_ids: {JsonSerializer.Serialize(source.RequirementIds)}");
            builder.AppendLine($"feature_spec_targets: {JsonSerializer.Serialize(source.Targets)}");
            builder.AppendLine($"feature_spec_stack: {JsonSerializer.Serialize(source.Stack)}");
            builder.AppendLine($"feature_spec_references: {JsonSerializer.Serialize(source.References)}");
            builder.AppendLine($"feature_spec_frontend_types: {JsonSerializer.Serialize(source.FrontendTypes ?? [])}");
            builder.AppendLine($"feature_spec_public_endpoints: {source.PublicEndpoints.ToString().ToLowerInvariant()}");
        }
        builder.AppendLine("authority: human-approved");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Delivery plan");
        builder.AppendLine();
        builder.AppendLine("This is the human approval view. Review scope, task boundaries, sequencing, complexity, repositories, and approval gates.");
        builder.AppendLine("Detailed execution constraints, validation procedures, provenance, and evidence fields remain in the linked agent task documents.");
        builder.AppendLine();
        builder.AppendLine("## Approval summary");
        builder.AppendLine();
        builder.AppendLine($"- Requirements covered: {source?.Requirements ?? workItems.SelectMany(item => item.RequirementIds).Distinct(StringComparer.OrdinalIgnoreCase).Count()}.");
        builder.AppendLine($"- Work items: {workItems.Count} ({workItems.Count(item => !string.Equals(item.Status, "decomposed", StringComparison.OrdinalIgnoreCase))} actionable).");
        builder.AppendLine($"- Repositories: {HumanList(source?.Targets ?? workItems.SelectMany(item => item.Targets ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())}.");
        builder.AppendLine($"- UI review: {(source?.FrontendChanges == true ? "required" : "not required")}.");
        builder.AppendLine($"- Public-endpoint cache and database-isolation policy: {(source?.PublicEndpoints == true ? "applies" : "not applicable")}.");
        builder.AppendLine();
        builder.AppendLine("## Approval gates");
        builder.AppendLine();
        builder.AppendLine("- All impact findings reviewed; no deferred impact hidden from scope.");
        builder.AppendLine("- All required decisions resolved.");
        builder.AppendLine("- Human authority recorded through `cis plan approve` or carried from a current approved feature by `cis plan derive`.");
        if (source?.FrontendChanges == true)
        {
            builder.AppendLine("- Global UI gate: coordination -> validated wireframe -> combined wireframe/design approval -> all downstream work.");
            builder.AppendLine("- `PausedForReview` or rejected design stops every non-review task until explicit design approval.");
        }
        builder.AppendLine();
        builder.AppendLine("## Tasks for approval");
        builder.AppendLine();
        builder.AppendLine("| Task | Area | Complexity | Depends on | Requirements | Repositories | Status |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in workItems)
        {
            var taskLabel = string.IsNullOrWhiteSpace(item.TaskPath)
                ? $"{item.Id} — {Clean(item.Title)}"
                : $"[{item.Id} — {Clean(item.Title)}]({item.TaskPath})";
            builder.AppendLine($"| {taskLabel} | {Clean(item.Category)} | {item.Complexity} | {HumanDependencies(item.DependsOn)} | {HumanRequirements(item.RequirementIds, source?.RequirementIds ?? [])} | {HumanTargets(item.Targets ?? [], source?.Targets ?? [])} | {item.Status} |");
        }

        builder.AppendLine();
        builder.AppendLine("<!-- cis:execution-manifest");
        builder.AppendLine("| ID | Category | Complexity | Parent | Task document | Title | Requirement IDs | Impact IDs | Depends on | Acceptance criteria | Validation | Status | Task type | Type version | Approval gate | Targets | Frontend type |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in workItems)
        {
            builder.AppendLine($"| {item.Id} | {Clean(item.Category)} | {item.Complexity} | {item.ParentId} | {item.TaskPath} | {Clean(item.Title)} | {string.Join(", ", item.RequirementIds)} | {string.Join(", ", item.ImpactIds)} | {string.Join(", ", item.DependsOn)} | {Clean(item.AcceptanceCriteria)} | {Clean(item.Validation)} | {item.Status} | {item.TaskTypeKey} | {item.TaskTypeVersion} | {item.ApprovalGate} | {string.Join(", ", item.Targets ?? [])} | {item.FrontendType ?? "not-applicable"} |");
        }
        builder.AppendLine("cis:execution-manifest -->");
        return builder.ToString();
    }

    private static string HumanList(IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length == 0 ? "—" : string.Join(", ", items);
    }

    private static string HumanDependencies(IReadOnlyList<string> values)
        => values.Count switch
        {
            0 => "—",
            <= 4 => string.Join(", ", values),
            _ => $"{values.Count} prerequisite tasks",
        };

    private static string HumanRequirements(IReadOnlyList<string> values, IReadOnlyList<string> allValues)
    {
        if (values.Count == 0) return "—";
        if (allValues.Count > 0 && values.Count == allValues.Count
            && values.All(value => allValues.Contains(value, StringComparer.OrdinalIgnoreCase)))
            return $"All {allValues.Count}";
        return string.Join(", ", values.Select(value => value[(value.LastIndexOf('-') + 1)..]));
    }

    private static string HumanTargets(IReadOnlyList<string> values, IReadOnlyList<string> allValues)
    {
        if (values.Count == 0) return "—";
        if (allValues.Count > 0 && values.Count == allValues.Count
            && values.All(value => allValues.Contains(value, StringComparer.OrdinalIgnoreCase)))
            return $"All {allValues.Count}";
        return string.Join(", ", values);
    }

    private void ValidateTaskDocument(ChangeDossier change, PlanWorkItem item, List<string> errors, bool publicEndpoints)
    {
        if (string.IsNullOrWhiteSpace(item.TaskPath))
        {
            errors.Add($"{item.Id} requires a durable task document.");
            return;
        }

        var dossier = Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!;
        var path = Path.GetFullPath(Path.Combine(dossier, item.TaskPath.Replace('/', Path.DirectorySeparatorChar)));
        var expectedRoot = Path.GetFullPath(Path.Combine(dossier, "agent-tasks")) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(expectedRoot, comparison) || !File.Exists(path))
        {
            errors.Add($"{item.Id} durable task document is missing or outside agent-tasks: {item.TaskPath}");
            return;
        }

        var content = File.ReadAllText(path);
        foreach (var required in new[]
                 {
                     $"task_id: {item.Id}", $"task_type: {item.TaskTypeKey}", "## Objective", "## Required changes", "## Required outputs", "## Constraints and exclusions",
                     "## Dependencies and approval gates", "## Acceptance criteria", "## Targeted validation",
                     "## Completion evidence", "## Deferrals and residual risk",
                 })
        {
            if (!content.Contains(required, StringComparison.Ordinal))
            {
                errors.Add($"{item.Id} task document is missing required content: {required}");
            }
        }
        if (item.Category is "wireframe" or "design" or "frontend"
            && !content.Contains($"frontend_type: {item.FrontendType}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{item.Id} task document does not record its frontend type '{item.FrontendType}'.");
        }
        if (publicEndpoints
            && item.Category is "security" or "contract" or "backend" or "observability" or "verification" or "assurance"
            && !content.Contains("PUBLIC-ENDPOINT-CACHE", StringComparison.Ordinal))
        {
            errors.Add($"{item.Id} task document is missing the PUBLIC-ENDPOINT-CACHE policy obligations.");
        }
    }

    private bool RegisterTaskCatalogEntries(ChangeDossier change, IReadOnlyList<PlanWorkItem> items)
    {
        var resolution = _resolver.Resolve(change.RepositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return false;
        }

        var catalog = File.ReadAllText(resolution.Context.CatalogPath);
        var additions = new StringBuilder();
        var applied = false;
        foreach (var item in items.Where(item => item.TaskPath is not null))
        {
            var stableId = $"{resolution.Context.RepositoryId}:change:{change.Id.ToLowerInvariant()}:task:{item.Id.ToLowerInvariant()}";
            var registeredPath = $"{change.RelativePath}/{item.TaskPath}";
            if (TryReplaceCatalogEntry(catalog, stableId, registeredPath, "draft", out var updated))
            {
                if (!string.Equals(updated, catalog, StringComparison.Ordinal))
                {
                    catalog = updated;
                    applied = true;
                }
                continue;
            }
            additions.AppendLine($"  - id: {stableId}");
            additions.AppendLine($"    path: {registeredPath}");
            additions.AppendLine("    type: agent-task");
            additions.AppendLine("    status: draft");
            additions.AppendLine("    authority: canonical");
        }
        if (additions.Length == 0 && !applied)
        {
            return false;
        }
        File.WriteAllText(resolution.Context.CatalogPath,
            catalog + (catalog.EndsWith('\n') ? string.Empty : Environment.NewLine) + additions);
        return true;
    }

    private bool RegisterFeatureSpecCatalogEntry(ChangeDossier change, PlanSource source)
    {
        var resolution = _resolver.Resolve(change.RepositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return false;
        }
        var catalog = File.ReadAllText(resolution.Context.CatalogPath);
        if (catalog.Contains($"path: {source.Path}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var stableId = $"{resolution.Context.RepositoryId}:change:{change.Id.ToLowerInvariant()}:feature-spec-source";
        var addition = $"""
  - id: {stableId}
    path: {source.Path}
    type: {source.DocumentType}
    status: draft
    authority: canonical
""";
        File.WriteAllText(resolution.Context.CatalogPath,
            catalog + (catalog.EndsWith('\n') ? string.Empty : Environment.NewLine) + addition);
        return true;
    }

    private bool RegisterManualTestCasesCatalogEntry(ChangeDossier change)
    {
        var resolution = _resolver.Resolve(change.RepositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null) return false;
        var catalog = File.ReadAllText(resolution.Context.CatalogPath);
        var stableId = $"{resolution.Context.RepositoryId}:change:{change.Id.ToLowerInvariant()}:manual-test-cases";
        var registeredPath = $"{change.RelativePath}/test-cases.md";
        if (TryReplaceCatalogEntry(catalog, stableId, registeredPath, "draft", out var updated))
        {
            if (string.Equals(updated, catalog, StringComparison.Ordinal)) return false;
            File.WriteAllText(resolution.Context.CatalogPath, updated);
            return true;
        }
        var addition = $"""
  - id: {stableId}
    path: {registeredPath}
    type: manual-test-cases
    status: draft
    authority: derived
""";
        File.WriteAllText(resolution.Context.CatalogPath,
            catalog + (catalog.EndsWith('\n') ? string.Empty : Environment.NewLine) + addition);
        return true;
    }

    private static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return true;
    }

    private static bool WriteTaskPreservingEvidence(string path, string generated, bool preserveStatus = true)
    {
        if (!File.Exists(path)) return WriteIfChanged(path, generated);
        var existing = File.ReadAllText(path);
        if (string.Equals(existing, generated, StringComparison.Ordinal)) return false;
        foreach (var heading in new[]
                 {
                     "## Completion evidence", "## Deferrals and residual risk", "## External issue links", "## External synchronization decisions",
                     "## Current task-type contract", "## Task-type migration history", "## Retirement history",
                 })
        {
            var body = MarkdownSection(existing, heading);
            if (body is not null) generated = ReplaceMarkdownSection(generated, heading, body);
        }
        var existingStatus = Regex.Match(existing, @"(?m)^task_status:\s*(?<value>.*)$");
        if (preserveStatus && existingStatus.Success)
        {
            generated = Regex.Replace(generated, @"(?m)^task_status:.*$", $"task_status: {existingStatus.Groups["value"].Value.Trim()}",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        generated = PreserveCheckedChecklistItems(generated, existing);
        return WriteIfChanged(path, generated);
    }

    private static string PreserveCheckedChecklistItems(string generated, string existing)
    {
        var checkedItems = Regex.Matches(existing, @"(?m)^[ \t]*-[ \t]*\[[xX]\][ \t]*(?<text>[^\r\n]+?)[ \t]*\r?$")
            .Select(match => match.Groups["text"].Value.Trim())
            .Where(text => text.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (checkedItems.Count == 0) return generated;

        return Regex.Replace(
            generated,
            @"(?m)^(?<prefix>[ \t]*-[ \t]*)\[[ \t]*\][ \t]*(?<text>[^\r\n]+?)[ \t]*(?<ending>\r?)$",
            match => checkedItems.Contains(match.Groups["text"].Value.Trim())
                ? $"{match.Groups["prefix"].Value}[x] {match.Groups["text"].Value.Trim()}{match.Groups["ending"].Value}"
                : match.Value,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private bool RetireObsoleteTasks(
        ChangeDossier change,
        IReadOnlyList<PlanWorkItem> previousItems,
        IReadOnlyList<PlanWorkItem> currentItems,
        PlanSource source)
    {
        var currentIds = currentItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var obsolete in previousItems.Where(item => item.TaskPath is not null && !currentIds.Contains(item.Id)))
        {
            var dossier = Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!;
            var activePath = SafeTaskPath(dossier, obsolete.TaskPath!);
            if (activePath is null || !File.Exists(activePath)) continue;
            var relativeWithinTasks = obsolete.TaskPath!["agent-tasks/".Length..];
            var retiredRelative = "agent-tasks/retired/" + relativeWithinTasks;
            var retiredPath = SafeTaskPath(dossier, retiredRelative)!;
            retiredPath = AvailableRetiredPath(retiredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(retiredPath)!);
            File.Move(activePath, retiredPath);
            var retired = File.ReadAllText(retiredPath);
            retired = ReplaceFrontMatter(retired, "status", "Archived");
            retired = ReplaceFrontMatter(retired, "task_status", "Retired");
            retired = AppendRetirementHistory(retired, source);
            File.WriteAllText(retiredPath, retired);
            var actualRelative = NormalizePath(Path.GetRelativePath(dossier, retiredPath));
            SetTaskCatalogEntry(change, obsolete.Id, actualRelative, "archived");
            _changes.AppendEvent(change, "plan-task-retired", new Dictionary<string, string>
            {
                ["taskId"] = obsolete.Id,
                ["previousPath"] = obsolete.TaskPath!,
                ["retiredPath"] = actualRelative,
                ["sourceSha256"] = source.Sha256,
                ["reason"] = "Task is no longer selected by the re-imported feature specification.",
            });
            changed = true;
        }
        return changed;
    }

    private bool RestoreRetiredTask(ChangeDossier change, string taskPath)
    {
        var dossier = Path.GetDirectoryName(_changes.DossierFile(change, "plan.md"))!;
        var activePath = SafeTaskPath(dossier, taskPath);
        if (activePath is null || File.Exists(activePath)) return false;
        var fileName = Path.GetFileName(activePath);
        var retiredRoot = Path.Combine(dossier, "agent-tasks", "retired");
        if (!Directory.Exists(retiredRoot)) return false;
        var candidate = CisPathSafety.EnumerateFiles(retiredRoot, fileName)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (candidate is null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        File.Move(candidate, activePath);
        _changes.AppendEvent(change, "plan-task-reactivated", new Dictionary<string, string>
        {
            ["taskPath"] = taskPath,
            ["retiredPath"] = NormalizePath(Path.GetRelativePath(dossier, candidate)),
            ["reason"] = "Task was selected again by feature-specification re-import; prior evidence was preserved and lifecycle status was reset.",
        });
        return true;
    }

    private string AppendRetirementHistory(string content, PlanSource source)
    {
        const string heading = "## Retirement history";
        if (!content.Contains(heading, StringComparison.Ordinal))
            content = content.TrimEnd() + Environment.NewLine + Environment.NewLine + heading + Environment.NewLine + Environment.NewLine
                + "| Timestamp UTC | Actor | Source digest | Rationale |" + Environment.NewLine
                + "|---|---|---|---|" + Environment.NewLine;
        var row = $"| {_clock().ToUniversalTime():O} | `cis plan import-spec` | `{Cell(source.Sha256)}` | Task is no longer selected by the re-imported feature specification. |";
        return AppendTableRow(content, heading, row);
    }

    private void SetTaskCatalogEntry(ChangeDossier change, string taskId, string taskPath, string status)
    {
        var resolution = _resolver.Resolve(change.RepositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null) return;
        var catalog = File.ReadAllText(resolution.Context.CatalogPath);
        var stableId = $"{resolution.Context.RepositoryId}:change:{change.Id.ToLowerInvariant()}:task:{taskId.ToLowerInvariant()}";
        var registeredPath = $"{change.RelativePath}/{taskPath}";
        if (TryReplaceCatalogEntry(catalog, stableId, registeredPath, status, out var updated)
            && !string.Equals(updated, catalog, StringComparison.Ordinal))
            File.WriteAllText(resolution.Context.CatalogPath, updated);
    }

    private static bool TryReplaceCatalogEntry(string catalog, string stableId, string path, string status, out string updated)
    {
        var match = Regex.Match(catalog,
            $@"(?ms)^  - id:\s*{Regex.Escape(stableId)}\s*$.*?(?=^  - id:|\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            updated = catalog;
            return false;
        }
        var block = Regex.Replace(match.Value, @"(?m)^    path:[^\r\n]*(?=\r?$)", $"    path: {path}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        block = Regex.Replace(block, @"(?m)^    status:[^\r\n]*(?=\r?$)", $"    status: {status}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        updated = catalog[..match.Index] + block + catalog[(match.Index + match.Length)..];
        return true;
    }

    private static string? SafeTaskPath(string dossier, string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        if (!normalized.StartsWith("agent-tasks/", StringComparison.Ordinal)
            || normalized.Contains("../", StringComparison.Ordinal)) return null;
        var path = Path.GetFullPath(Path.Combine(dossier, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(Path.Combine(dossier, "agent-tasks")) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(root, comparison) ? path : null;
    }

    private static string AvailableRetiredPath(string requested)
    {
        if (!File.Exists(requested)) return requested;
        var directory = Path.GetDirectoryName(requested)!;
        var name = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name}.{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string ReplaceFrontMatter(string content, string key, string value)
    {
        var pattern = @"(?m)^" + Regex.Escape(key) + @":.*$";
        return Regex.IsMatch(content, pattern)
            ? Regex.Replace(content, pattern, $"{key}: {value}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            : content;
    }

    private static string AppendMigrationHistory(
        string content,
        PlanWorkItem previous,
        CisTaskTypeDefinition target,
        string reviewer,
        string rationale)
    {
        const string heading = "## Task-type migration history";
        if (!content.Contains(heading, StringComparison.Ordinal))
        {
            content = content.TrimEnd() + Environment.NewLine + Environment.NewLine + heading + Environment.NewLine + Environment.NewLine
                + "| From type/version | To type/version | Reviewer | Timestamp UTC | Rationale |" + Environment.NewLine
                + "|---|---|---|---|---|" + Environment.NewLine;
        }
        var row = $"| `{Cell(previous.TaskTypeKey)}@{Cell(previous.TaskTypeVersion)}` | " +
            $"`{Cell(target.Key)}@{Cell(target.Version)}` | {Cell(reviewer)} | {DateTimeOffset.UtcNow:O} | {Cell(rationale)} |";
        return AppendTableRow(content, heading, row);
    }

    private static string? MarkdownSection(string content, string heading)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return null;
        var next = content.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        if (next < 0) next = content.Length;
        return content[start..next].TrimEnd();
    }

    private static string ReplaceMarkdownSection(string content, string heading, string replacement)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return content.TrimEnd() + Environment.NewLine + Environment.NewLine + replacement + Environment.NewLine;
        var next = content.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        if (next < 0) next = content.Length;
        return content[..start] + replacement.TrimEnd() + Environment.NewLine + content[next..].TrimStart('\r', '\n');
    }

    private static IReadOnlyList<string> ParseJsonArray(IReadOnlyList<string> lines, string key)
    {
        var raw = lines.FirstOrDefault(line => line.StartsWith(key, StringComparison.Ordinal))?[key.Length..].Trim();
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private bool HasOutcomeAcceptanceCriteria(ChangeDossier change)
    {
        var path = _changes.DossierFile(change, "proposal.md");
        if (!File.Exists(path))
        {
            return false;
        }

        var lines = File.ReadAllLines(path);
        var heading = Array.FindIndex(lines, line => line.Equals("## Acceptance criteria", StringComparison.OrdinalIgnoreCase));
        if (heading < 0)
        {
            return false;
        }

        return lines.Skip(heading + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Any(line => line.StartsWith("- ", StringComparison.Ordinal)
                && !line.Contains("TODO", StringComparison.OrdinalIgnoreCase));
    }

    private void CarryForwardOutcomeAcceptance(ChangeDossier change, FeatureSpecification feature)
    {
        if (HasOutcomeAcceptanceCriteria(change)) return;

        var path = _changes.DossierFile(change, "proposal.md");
        var content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var bullets = feature.Requirements.Select(requirement =>
            $"- `{requirement.Id}`: {requirement.AcceptanceCriteria.Trim()}");
        var section = "## Acceptance criteria" + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, bullets);
        var revised = ReplaceMarkdownSection(content, "## Acceptance criteria", section);
        if (!WriteIfChanged(path, revised)) return;

        _changes.AppendEvent(change, "proposal-acceptance-authority-carried-forward", new Dictionary<string, string>
        {
            ["source"] = feature.RelativePath,
            ["sourceSha256"] = feature.Sha256,
            ["requirements"] = feature.Requirements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    private static bool HasCycle(IReadOnlyList<PlanWorkItem> items)
    {
        var dependencies = items.ToDictionary(item => item.Id, item => item.DependsOn, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (visiting.Contains(id))
            {
                return true;
            }

            if (!visited.Add(id))
            {
                return false;
            }

            visiting.Add(id);
            foreach (var dependency in dependencies.GetValueOrDefault(id) ?? [])
            {
                if (dependencies.ContainsKey(dependency) && Visit(dependency))
                {
                    return true;
                }
            }

            visiting.Remove(id);
            return false;
        }

        return items.Any(item => Visit(item.Id));
    }

    private static int CategoryOrder(string category) => category switch
    {
        "documentation" => 0,
        "contract" => 1,
        "dependency" => 2,
        "implementation" => 3,
        "delivery" => 4,
        "verification" => 5,
        _ => 3,
    };

    private static string[] SplitList(string value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? FrontendTypeOrNull(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "public" or "customer" or "backoffice" ? normalized : null;
    }

    private static string Clean(string value)
        => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string NormalizePath(string value)
        => value.Replace('\\', '/');

    private static bool IsExactImportedSource(FeatureSpecImportRequest request, PlanSource? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(request.FeatureSpecPath)
            || Path.IsPathRooted(request.FeatureSpecPath)) return false;

        var repository = Path.GetFullPath(request.RepositoryPath);
        var absolute = Path.GetFullPath(Path.Combine(repository,
            request.FeatureSpecPath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(repository + Path.DirectorySeparatorChar, comparison)
            || !File.Exists(absolute)) return false;

        var relative = Path.GetRelativePath(repository, absolute).Replace('\\', '/');
        if (!string.Equals(relative, source.Path, comparison)) return false;
        var content = File.ReadAllText(absolute);
        var digest = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        return string.Equals(digest, source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> EvaluateReadiness(string repositoryPath, string? ignoredCheck = null)
        => _readinessChecks
            .Select(check => check.Evaluate(repositoryPath))
            .Where(result => result.Applicable && !result.Ready)
            .Where(result => ignoredCheck is null
                || !string.Equals(result.Check, ignoredCheck, StringComparison.OrdinalIgnoreCase))
            .SelectMany(result => result.Errors.Select(error => $"[{result.Check}] {error}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static PlanResult Error(string? changeId, params string[] messages)
        => new("invalid", changeId, null, [], null, messages, false);

    private static PlanResult Blocked(string? changeId, params string[] messages)
        => new("blocked", changeId, null, [], new(false, messages, [], 0, 0, 0), [], false);

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? JsonSerializer.Deserialize<string>(value) ?? string.Empty
            : value;

    private sealed record PlanDocument(string Status, IReadOnlyList<PlanWorkItem> WorkItems, PlanSource? Source);

    private sealed record FeatureSpecification(
        string RelativePath,
        string Sha256,
        IReadOnlyList<FeatureRequirement> Requirements,
        bool FrontendChanges);

    private sealed record FeatureRequirement(
        string Id,
        string Text,
        string AcceptanceCriteria,
        string? Surface,
        bool Frontend,
        bool Backend,
        bool HasContract,
        string Complexity);

    private sealed record FeatureSpecParseResult(FeatureSpecification? Spec, IReadOnlyList<string> Errors);
}
