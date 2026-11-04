using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record StoryTaskEvent(string TaskId, string Status, string Actor, string At, string Evidence)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool NeedsVerificationRenewal { get; init; }
        public IReadOnlyList<CisStoryCompletionContext> CompletionContexts { get; init; } = [];
        public CisStoryExecutionResult? Execution { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<CisStoryReviewFeedback>? Feedback { get; init; }
    }
    private sealed record StoryPlanRecord(string ScopeHash, CisFeatureStoryResult Plan, string Actor, string Reason,
        string ApprovedAt, IReadOnlyList<StoryTaskEvent> Events)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, string>? Basis { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<StoryPlanRecord>? PreviousPlans { get; init; }
    }

    private static string StoryRecordPath(WizardState state, string storyId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(storyId, "^[a-f0-9]{16}$")) throw new InvalidDataException("Select a current story.");
        var path = Path.Combine(Path.GetDirectoryName(state.RequestPath)!, "story-tasks", storyId + ".json");
        if (!SafeAbsolutePath(path)) throw new InvalidDataException("The story task record uses an unsafe path.");
        return path;
    }

    private static StoryPlanRecord? ReadStoryRecord(WizardState state, string storyId)
    {
        var path = StoryRecordPath(state, storyId);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4_194_304) throw new InvalidDataException("The saved task history exceeds its supported size.");
        return JsonSerializer.Deserialize<StoryPlanRecord>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("The saved story task record is empty.");
    }

    // Implementation edits are expected during delivery. Approval binds the feature's
    // saved scope and repository identities, while retaining the original evidence hash.
    private static string StoryScopeHash(WizardState state, DeliveryInput input, string storyId)
        => DeliveryBasisHash(StoryContractBasis(state, input, storyId));

    private static string LegacyStoryScopeHash(WizardState state, DeliveryInput input, string storyId)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            state.Content,
            state.Binding,
            storyId,
            input.Direction,
            input.Constraints,
            repositories = input.Repositories.Select(r => new { r.Id, r.RepositoryPath })
        }, Json)));

    private static bool StoryScopeMatches(StoryPlanRecord record, WizardState state, DeliveryInput input, string storyId)
    {
        if (record.Basis is null) return record.ScopeHash == LegacyStoryScopeHash(state, input, storyId);
        if (record.ScopeHash != DeliveryBasisHash(record.Basis)) return false;
        var current = StoryBasisForRecordedContract(record.Basis, ApprovedStoryBasis(state, input, storyId, record.Plan.Tasks));
        return record.Basis.Count == current.Count && record.Basis.All(item => current.TryGetValue(item.Key, out var value)
            && (item.Value == value || item.Key.StartsWith("task contract/", StringComparison.Ordinal)
                && NormalizeRetainedTaskContract(item.Value) == NormalizeRetainedTaskContract(value)));
    }

    private static string NormalizeRetainedTaskContract(string value)
    {
        try
        {
            var task = JsonSerializer.Deserialize<CisFeatureStoryTask>(value, new JsonSerializerOptions(Json)
            { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            return task is null ? value : StoryTaskContractJson(task).GetRawText();
        }
        catch (JsonException) { return value; }
    }

    private static JsonElement StoryTaskContractJson(CisFeatureStoryTask task)
    {
        var original = new HashSet<string>(["id", "title", "description", "repositoryIds", "requirementNumbers", "acceptanceCriteria", "dependsOn"], StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in JsonSerializer.SerializeToElement(task, Json).EnumerateObject())
            {
                if (!original.Contains(property.Name) && (property.Value.ValueKind == JsonValueKind.Null
                    || property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0)) continue;
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static IReadOnlyDictionary<string, string> ApprovedStoryBasis(WizardState state, DeliveryInput input,
        string storyId, IReadOnlyList<CisFeatureStoryTask> tasks)
    {
        var basis = new SortedDictionary<string, string>(StoryContractBasis(state, input, storyId).ToDictionary(item => item.Key, item => item.Value), StringComparer.Ordinal);
        var decision = state.Review.DeliveryReviews?.LastOrDefault(item => item.StoryId == storyId);
        basis["story delivery decision"] = decision is null ? "none" : JsonSerializer.Serialize(new
        { decision.Treatment, Owners = decision.Owners.Order(StringComparer.Ordinal), decision.Plan }, Json);
        foreach (var id in decision?.Owners ?? [])
            basis["delivery decision ownership/" + id] = DeliveryRepositoryIdentity(input.Repositories.FirstOrDefault(repository => repository.Id == id));
        foreach (var task in tasks)
            basis["task contract/" + task.Id] = StoryTaskContractJson(task).GetRawText();
        foreach (var id in tasks.SelectMany(task => task.RepositoryIds).Distinct(StringComparer.Ordinal))
            basis["task ownership/" + id] = DeliveryRepositoryIdentity(input.Repositories.FirstOrDefault(repository => repository.Id == id));
        var owners = tasks.SelectMany(task => task.RepositoryIds).ToHashSet(StringComparer.Ordinal);
        var work = state.Review.RepositoryWork ?? [];
        var pending = new Queue<string>(work.Where(item => owners.Contains(item.RepositoryId)).Select(item => item.Id));
        var included = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var id))
        {
            if (!included.Add(id)) continue;
            var item = work.SingleOrDefault(item => item.Id == id);
            basis["task repository prerequisite/" + id] = item is null ? "missing" : JsonSerializer.Serialize(new
            { item.Id, item.RepositoryId, item.Title, item.Scope, DependsOn = item.DependsOn.Order(StringComparer.Ordinal), ChangeIds = item.ChangeIds.Order(StringComparer.Ordinal) }, Json);
            foreach (var prerequisite in item?.DependsOn ?? []) pending.Enqueue(prerequisite);
        }
        return WithStoryDeliveryBoundaries(state, basis);
    }

    private static string StoryScopeReason(StoryPlanRecord record, WizardState state, DeliveryInput input, string storyId)
    {
        if (record.Basis is null)
            return "The saved task plan has a legacy whole-feature basis whose compatibility cannot be established. Its approval and task history are retained; review a current task contract before further execution or completion.";
        var current = StoryBasisForRecordedContract(record.Basis, ApprovedStoryBasis(state, input, storyId, record.Plan.Tasks));
        var changed = record.Basis.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)
            .Where(key => record.Basis.GetValueOrDefault(key) != current.GetValueOrDefault(key)).Order(StringComparer.Ordinal);
        return "Changed task contract: " + string.Join("; ", changed)
            + ". The original approval and progress are retained; review only this story's current task plan before further execution or completion.";
    }
    private static string StoryTasksHash(CisFeatureStoryResult result)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { result.InputHash, Tasks = result.Tasks.Select(StoryTaskContractJson).ToArray() }, Json)));
    private static string StoryApprovalHash(string scopeHash, CisFeatureStoryResult result)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { scopeHash, tasksHash = StoryTasksHash(result) }, Json)));
    private static string StoryRevision(StoryPlanRecord? record)
        => record is null ? "none" : Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, Json)));

    private static bool StoryTaskContractsEqual(IReadOnlyList<CisFeatureStoryTask> left, IReadOnlyList<CisFeatureStoryTask> right,
        string slug, string storyId)
    {
        string Contract(CisFeatureStoryTask task) => StoryTaskContractJson(task with
        {
            InstanceKey = task.InstanceKey ?? slug + "/" + storyId + "/" + task.Id,
            StoryId = task.StoryId ?? storyId,
            Targets = task.Targets is { Count: 0 } ? task.RepositoryIds : task.Targets
        }).GetRawText();
        return left.Count == right.Count && left.OrderBy(task => task.Id, StringComparer.Ordinal).Select(Contract)
            .SequenceEqual(right.OrderBy(task => task.Id, StringComparer.Ordinal).Select(Contract), StringComparer.Ordinal);
    }

    private static bool PendingStoryProposal(StoryPlanRecord? record, CisFeatureStoryResult? cached, string inputHash,
        WizardState state, DeliveryInput input)
        => record is not null && cached is { Provider: "controller", Tasks.Count: > 0 }
            && cached.InputHash == inputHash && cached.Revision == StoryRevision(record)
            && (!StoryScopeMatches(record, state, input, cached.StoryId)
                || !StoryTaskContractsEqual(cached.Tasks, record.Plan.Tasks, cached.Slug, cached.StoryId));

    private static CisFeatureStoryPlanHistory ProjectStoryHistory(StoryPlanRecord record)
        => new(StoryRevision(record), StoryApprovalHash(record.ScopeHash, record.Plan), record.Actor, record.ApprovedAt, record.Reason,
            record.Plan.Tasks, record.Plan.Tasks.Select(task =>
            {
                var last = record.Events.LastOrDefault(item => item.TaskId == task.Id);
                return new CisFeatureStoryTaskProgress(task.Id, last?.Status ?? "Ready", false, false,
                    "Historical task-plan evidence; current eligibility is assessed against the active plan.", last?.Evidence)
                { Execution = record.Events.LastOrDefault(item => item.TaskId == task.Id && item.Execution is not null)?.Execution };
            }).ToArray());

    private static IReadOnlyList<StoryTaskEvent> CompatibleStoryEvents(StoryPlanRecord previous,
        CisFeatureStoryResult next, IReadOnlyDictionary<string, string> basis, string actor, string at)
    {
        if (previous.Basis is null || previous.Plan.Story is null || next.Story is null) return [];
        static bool SharedKey(string key) => !key.StartsWith("task contract/", StringComparison.Ordinal)
            && !key.StartsWith("task ownership/", StringComparison.Ordinal) && key is not ("acceptance" or "parent accepted requirements");
        var oldShared = previous.Basis.Where(item => SharedKey(item.Key)).ToDictionary(item => item.Key, item => item.Value);
        var newShared = StoryBasisForRecordedContract(previous.Basis, basis).Where(item => SharedKey(item.Key)).ToDictionary(item => item.Key, item => item.Value);
        if (oldShared.Count != newShared.Count || oldShared.Any(item => newShared.GetValueOrDefault(item.Key) != item.Value)) return [];
        var compatible = new HashSet<string>(StringComparer.Ordinal);
        var renewal = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in next.Tasks)
        {
            var old = previous.Plan.Tasks.SingleOrDefault(item => item.Id == task.Id);
            if (old is null) continue;
            var identical = StoryTaskContractsEqual([old], [task], next.Slug, next.StoryId);
            var legacyAdded = !identical && LegacyStorySemanticsAdded(old, task, next.Slug, next.StoryId);
            if (!identical && !legacyAdded) continue;
            if (task.RepositoryIds.Any(id => previous.Basis.GetValueOrDefault("task ownership/" + id) != basis.GetValueOrDefault("task ownership/" + id))) continue;
            static IReadOnlyList<string>? Criteria(CisFeatureStoryTask selected, CisFeatureDeliveryStory story)
                => selected.RequirementNumbers.Any(number => number < 1 || number > story.Requirements.Count) ? null
                    : selected.RequirementNumbers.Select(number => story.Requirements[number - 1]).ToArray();
            var oldCriteria = Criteria(old, previous.Plan.Story); var newCriteria = Criteria(task, next.Story);
            if (oldCriteria is not null && newCriteria is not null && oldCriteria.SequenceEqual(newCriteria, StringComparer.Ordinal))
            {
                compatible.Add(task.Id);
                if (legacyAdded) renewal.Add(task.Id);
            }
        }
        // A changed prerequisite cannot leave a dependent task's former completion current.
        while (true)
        {
            var removed = compatible.RemoveWhere(id => next.Tasks.Single(task => task.Id == id).DependsOn
                .Any(dependency => !dependency.Contains('/') && !compatible.Contains(dependency)));
            if (removed == 0) break;
        }
        var events = previous.Events.Where(item => compatible.Contains(item.TaskId)).ToList();
        foreach (var id in renewal.Where(id => compatible.Contains(id) && events.Any(item => item.TaskId == id)))
            events.Add(new(id, "InProgress", actor, at,
                "Retained applied work and progress. Renew verification and review against the newly explicit legacy task semantics before completion.")
            { NeedsVerificationRenewal = true });
        return events;
    }

    private static bool LegacyStorySemanticsAdded(CisFeatureStoryTask old, CisFeatureStoryTask current, string slug, string storyId)
    {
        if (old.Category is not null && old.Complexity is not null && old.TaskTypeKey is not null && old.TaskTypeVersion is not null
            && old.VerificationObligations.Count > 0) return false;
        var retained = current with
        {
            Category = old.Category is null ? null : current.Category,
            Complexity = old.Complexity is null ? null : current.Complexity,
            TaskTypeKey = old.TaskTypeKey is null ? null : current.TaskTypeKey,
            TaskTypeVersion = old.TaskTypeVersion is null ? null : current.TaskTypeVersion,
            FrontendType = old.FrontendType is null ? null : current.FrontendType,
            ApprovalGate = old.ApprovalGate is null ? null : current.ApprovalGate,
            Validation = old.Validation is null ? null : current.Validation,
            DecisionIds = old.DecisionIds.Count == 0 ? [] : current.DecisionIds,
            NegativeCriteria = old.NegativeCriteria.Count == 0 ? [] : current.NegativeCriteria,
            RequiredOutputs = old.RequiredOutputs.Count == 0 ? [] : current.RequiredOutputs,
            VerificationObligations = old.VerificationObligations.Count == 0 ? [] : current.VerificationObligations
        };
        return StoryTaskContractsEqual([old], [retained], slug, storyId);
    }

    private CisFeatureStoryResult ProjectStoryWorkflow(WizardState state, DeliveryInput input, CisFeatureStoryResult result)
    {
        result = ProjectStoryPlanning(state, input, result);
        var record = ReadStoryRecord(state, result.StoryId);
        var pending = result.IsRevisionProposal && record is not null;
        var approved = !pending && record is not null && StoryScopeMatches(record, state, input, result.StoryId);
        var historical = !pending && !approved && record is not null && result.Status != "current";
        if (historical)
            result = result with
            {
                Status = "stale",
                Tasks = record!.Plan.Tasks,
                Story = record.Plan.Story,
                Definition = record.Plan.Definition,
                Provider = record.Plan.Provider,
                Model = record.Plan.Model,
                Warnings = result.Warnings.Append(StoryScopeReason(record, state, input, result.StoryId)).ToArray()
            };
        if (approved)
        {
            var tasks = ValidateStoryTasks(record!.Plan.Tasks, input, result.Story!, state, retainLegacy: true);
            result = result with { Status = "current", Tasks = tasks, Provider = record.Plan.Provider, Model = record.Plan.Model, Cached = true };
        }
        var progress = result.Tasks.Select(task =>
        {
            var last = approved || historical ? record!.Events.LastOrDefault(item => item.TaskId == task.Id) : null;
            var remaining = task.DependsOn.Concat(input.Drafts.Single(item => item.Id == result.StoryId).SharedPrerequisiteTaskIds)
                .Distinct(StringComparer.Ordinal).Where(id => !approved || !StoryTaskDependencyComplete(state, input, result.StoryId, record!, id)).ToArray();
            var storyDependencies = input.Drafts.Single(item => item.Id == result.StoryId).StoryDependencies
                .Where(id => !StoryOutcomeComplete(state, input, id)).ToArray();
            var status = last?.Status ?? (approved ? remaining.Length == 0 ? "Ready" : "Blocked" : "Proposed");
            var deliveryBlock = StoryDeliveryBlock(result, task);
            var adopted = new[] { state.Authority }.Concat(state.Workspace.Repositories.Where(repository => task.RepositoryIds.Contains(repository.Id)))
                .Any(repository => CisEngineeringPolicy.Read(repository.RepositoryPath)?.RequireTaskCompletion == true);
            var contractErrors = CisStoryEngineeringContract.Validate(task, adopted).ToList();
            if (task.TaskTypeKey is not null && task.TaskTypeVersion is not null && task.Category is not null)
                contractErrors.AddRange(taskContracts?.ValidateTaskType(state.Authority.RepositoryPath, task.TaskTypeKey, task.TaskTypeVersion, task.Category)
                    ?? ["Load the registered planning reader to reassess the retained typed task contract."]);
            var gateErrors = taskContracts?.ValidateStoryTaskGate(state.Authority.RepositoryPath, task) ??
                (task.ApprovalGate is not (null or "none") ? ["Resolve the exact canonical approval gate before starting this task."] : []);
            var completionGateErrors = taskContracts?.ValidateStoryTaskGate(state.Authority.RepositoryPath, task, completing: true) ??
                (task.ApprovalGate is not (null or "none") ? ["Resolve the exact canonical approval gate before completing this task."] : []);
            var reason = historical ? StoryScopeReason(record!, state, input, result.StoryId)
                : !approved ? result.Status == "current" ? "Approve the task plan before starting work." : "The plan needs regeneration and review."
                : deliveryBlock ?? contractErrors.FirstOrDefault() ?? (storyDependencies.Length > 0 ? "Complete prerequisite story outcomes first: " + string.Join(", ", storyDependencies) + "."
                    : remaining.Length > 0 ? "Complete dependencies first: " + string.Join(", ", remaining) + "."
                    : gateErrors.FirstOrDefault());
            var execution = approved || historical ? record!.Events.LastOrDefault(item => item.TaskId == task.Id && item.Execution is not null)?.Execution : null;
            var renewal = last?.NeedsVerificationRenewal == true;
            return new CisFeatureStoryTaskProgress(task.Id, status, approved && status == "Ready" && reason is null,
                approved && status == "InProgress" && reason is null && completionGateErrors.Count == 0 && (execution is null || execution.Applied),
                reason ?? (renewal ? "Retain the existing work and verify every criterion against the current contract. Record completion with current required evidence, or use scoped agent verification and review."
                    : status == "InProgress" ? completionGateErrors.FirstOrDefault() : null), last?.Evidence)
            {
                Execution = execution,
                NeedsVerificationRenewal = renewal,
                ReviewFeedback = execution is null ? null : StoryFeedback(state, input, record!, task.Id, execution),
                CanExecute = approved && reason is null && (status is "Ready" or "InProgress") && remaining.Length == 0 && (execution?.Applied != true || renewal)
            };
        }).ToArray();
        return result with
        {
            PlanHash = approved ? StoryApprovalHash(record!.ScopeHash, record.Plan) : StoryApprovalHash(StoryScopeHash(state, input, result.StoryId), result),
            Revision = StoryRevision(record),
            ApprovedBy = approved || historical || pending ? record!.Actor : null,
            PlanState = historical ? "Needs review" : approved ? progress.All(task => task.Status == "Complete") ? "Complete" : "Approved" : "Proposed",
            TaskProgress = progress,
            PreviousPlans = (record?.PreviousPlans ?? []).Concat(pending ? [record!] : []).Select(ProjectStoryHistory).ToArray()
        };
    }

    public CisFeatureStoryResult UpdateStoryWorkflow(string workspacePath, string slug, string storyId, string operation,
        string expectedPlanHash, string expectedRevision, string actor, string? reason = null, string? taskId = null,
        string? evidence = null, bool criteriaVerified = false)
    {
        try
        {
            if (!SingleLine(actor, 200)) throw new InvalidDataException("Provide the person recording this task decision.");
            var state = ReadWizard(workspacePath, slug);
            var path = StoryRecordPath(state, storyId);
            // Share the generation lock: an approval cannot race a replacement proposal.
            var lockPath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", slug, storyId + ".json.lock");
            if (!SafeAbsolutePath(lockPath)) throw new InvalidDataException("The story lock uses an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            var result = Story(workspacePath, slug, storyId, false);
            if (result.Errors.Count > 0) return result;
            if (result.Status != "current" || result.Tasks.Count == 0 || result.PlanHash != expectedPlanHash || result.Revision != expectedRevision)
                throw new InvalidDataException("The story plan or task progress changed. Refresh before recording this decision.");
            var input = ReadDeliveryInput(state, false);
            var record = ReadStoryRecord(state, storyId);
            var now = DateTimeOffset.UtcNow.ToString("O");
            IReadOnlyList<CisStoryCompletionContext> completionContexts = [];
            if (operation == "approve")
            {
                if (result.PlanState != "Proposed")
                {
                    if (record is not null && (result.PlanState is "Approved" or "Complete")
                        && record.Actor == actor.Trim() && record.Reason == reason?.Trim())
                    {
                        var current = Story(workspacePath, slug, storyId, false);
                        var latest = ReadWizard(workspacePath, slug);
                        if (current.Errors.Count > 0 || current.PlanHash != expectedPlanHash || current.Revision != expectedRevision
                            || !StoryScopeMatches(record, latest, ReadDeliveryInput(latest, false), storyId))
                            throw new InvalidDataException("The story changed while confirming the existing approval. Refresh and retry.");
                        return current with { Applied = false };
                    }
                    throw new InvalidDataException("This task plan is already approved.");
                }
                if (!SingleLine(reason, 2000)) throw new InvalidDataException("Record why this task plan is approved.");
                if (result.Story!.Conflict is not null && !result.Story.ReviewCurrent || result.Story.ReviewCurrent && result.Story.Review?.Treatment == "out-of-scope")
                    throw new InvalidDataException("Resolve the story's scope decision before approving its tasks.");
                var approvedBasis = ApprovedStoryBasis(state, input, storyId, result.Tasks);
                var previous = record;
                var history = (previous?.PreviousPlans ?? []).ToList();
                if (previous is not null)
                {
                    if (history.Count >= 50) throw new InvalidDataException("This story has reached its retained task-plan revision limit. Preserve its approval history before preparing further revisions.");
                    history.Add(previous with { PreviousPlans = null, Plan = previous.Plan with { PreviousPlans = [] } });
                }
                record = new(DeliveryBasisHash(approvedBasis), result with { IsRevisionProposal = false, PreviousPlans = [], TaskProgress = [] },
                    actor.Trim(), reason!.Trim(), now, previous is null ? [] : CompatibleStoryEvents(previous, result, approvedBasis, actor.Trim(), now))
                { Basis = approvedBasis, PreviousPlans = history.Count > 0 ? history : null };
            }
            else
            {
                if (record is null || result.PlanState != "Approved") throw new InvalidDataException("Approve the current task plan before starting work.");
                var progress = result.TaskProgress.SingleOrDefault(task => task.Id == taskId)
                    ?? throw new InvalidDataException("Select a task from this story.");
                if (operation == "completion-context")
                {
                    var contexts = StoryCompletion(state, input, result, taskId!, true);
                    return result with
                    {
                        CompletionContexts = contexts,
                        Errors = contexts.SelectMany(item => item.Errors.Select(error => item.RepositoryId + ": " + error)).ToArray()
                    };
                }
                if (operation == "start" && !progress.CanStart)
                    throw new InvalidDataException(progress.BlockedReason ?? "Only a ready task can be started.");
                if (operation == "complete" && (!progress.CanComplete || !criteriaVerified || !SingleLine(evidence, 4000)))
                    throw new InvalidDataException("Complete an in-progress task only after verifying every completion criterion and recording the work and checks performed.");
                if (operation == "complete")
                {
                    var contexts = StoryCompletion(state, input, result, taskId!, false);
                    var closingErrors = contexts.SelectMany(item => item.Errors.Select(error => item.RepositoryId + ": " + error)).ToArray();
                    if (closingErrors.Length > 0) return result with { CompletionContexts = contexts, Errors = closingErrors };
                    completionContexts = contexts;
                }
                if (operation is not ("start" or "complete")) throw new InvalidDataException("Choose approve, start or complete.");
                if (record.Events.Count >= 2000) throw new InvalidDataException("This story has reached its task history limit.");
                record = record with
                {
                    Events = record.Events.Append(new StoryTaskEvent(taskId!, operation == "start" ? "InProgress" : "Complete", actor.Trim(), now,
                    operation == "complete" ? evidence!.Trim() : "")
                    { CompletionContexts = completionContexts }).ToArray()
                };
            }
            var checkedResult = Story(workspacePath, slug, storyId, false);
            if (checkedResult.Errors.Count > 0 || checkedResult.PlanHash != expectedPlanHash || checkedResult.Revision != expectedRevision
                || !StoryScopeMatches(record, ReadWizard(workspacePath, slug), input, storyId))
                throw new InvalidDataException("The story changed while saving. Refresh and retry.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (operation == "approve" && File.Exists(path))
            {
                var archive = path + "." + expectedRevision.Replace("sha256:", "") + ".history";
                if (!SafeAbsolutePath(archive)) throw new InvalidDataException("The task history uses an unsafe path.");
                if (!File.Exists(archive)) File.Copy(path, archive);
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var serialized = JsonSerializer.Serialize(record, Json);
            if (Encoding.UTF8.GetByteCount(serialized) > 4_194_304) throw new InvalidDataException("The saved task history exceeds its supported size; prior approval and evidence remain retained.");
            try { File.WriteAllText(temporary, serialized, new UTF8Encoding(false)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Story(workspacePath, slug, storyId, false) with { CompletionContexts = completionContexts };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return new("failed", slug, storyId, null, "", "", null, [], [], [error.Message]); }
    }
}
