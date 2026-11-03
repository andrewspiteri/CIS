using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record StoryTaskEvent(string TaskId, string Status, string Actor, string At, string Evidence)
    {
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
        => record.Basis is not null ? record.ScopeHash == DeliveryBasisHash(ApprovedStoryBasis(state, input, storyId, record.Plan.Tasks))
            : record.ScopeHash == LegacyStoryScopeHash(state, input, storyId);

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
            basis["task contract/" + task.Id] = JsonSerializer.Serialize(task, Json);
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
        return basis;
    }

    private static string StoryScopeReason(StoryPlanRecord record, WizardState state, DeliveryInput input, string storyId)
    {
        if (record.Basis is null)
            return "The saved task plan has a legacy whole-feature basis whose compatibility cannot be established. Its approval and task history are retained; review a current task contract before further execution or completion.";
        var current = ApprovedStoryBasis(state, input, storyId, record.Plan.Tasks);
        var changed = record.Basis.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)
            .Where(key => record.Basis.GetValueOrDefault(key) != current.GetValueOrDefault(key)).Order(StringComparer.Ordinal);
        return "Changed task contract: " + string.Join("; ", changed)
            + ". The original approval and progress are retained; review only this story's current task plan before further execution or completion.";
    }
    private static string StoryTasksHash(CisFeatureStoryResult result)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { result.InputHash, result.Tasks }, Json)));
    private static string StoryApprovalHash(string scopeHash, CisFeatureStoryResult result)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { scopeHash, tasksHash = StoryTasksHash(result) }, Json)));
    private static string StoryRevision(StoryPlanRecord? record)
        => record is null ? "none" : Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, Json)));

    private static CisFeatureStoryResult ProjectStoryWorkflow(WizardState state, DeliveryInput input, CisFeatureStoryResult result)
    {
        var record = ReadStoryRecord(state, result.StoryId);
        var approved = record is not null && StoryScopeMatches(record, state, input, result.StoryId);
        var historical = !approved && record is not null && result.Status != "current";
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
            var tasks = ValidateStoryTasks(record!.Plan.Tasks, input, result.Story!);
            result = result with { Status = "current", Tasks = tasks, Provider = record.Plan.Provider, Model = record.Plan.Model, Cached = true };
        }
        var progress = result.Tasks.Select(task =>
        {
            var last = approved || historical ? record!.Events.LastOrDefault(item => item.TaskId == task.Id) : null;
            var remaining = task.DependsOn.Where(id => !approved || record!.Events.LastOrDefault(item => item.TaskId == id)?.Status != "Complete").ToArray();
            var status = last?.Status ?? (approved ? remaining.Length == 0 ? "Ready" : "Blocked" : "Proposed");
            var reason = historical ? StoryScopeReason(record!, state, input, result.StoryId)
                : !approved ? result.Status == "current" ? "Approve the task plan before starting work." : "The plan needs regeneration and review."
                : remaining.Length > 0 ? "Complete dependencies first: " + string.Join(", ", remaining) + "." : null;
            var execution = approved || historical ? record!.Events.LastOrDefault(item => item.TaskId == task.Id && item.Execution is not null)?.Execution : null;
            return new CisFeatureStoryTaskProgress(task.Id, status, approved && status == "Ready",
                approved && status == "InProgress" && (execution is null || execution.Applied), reason, last?.Evidence)
            {
                Execution = execution,
                ReviewFeedback = execution is null ? null : StoryFeedback(state, input, record!, task.Id, execution),
                CanExecute = approved && (status is "Ready" or "InProgress") && remaining.Length == 0 && execution?.Applied != true
            };
        }).ToArray();
        return result with
        {
            PlanHash = approved ? StoryApprovalHash(record!.ScopeHash, record.Plan) : StoryApprovalHash(StoryScopeHash(state, input, result.StoryId), result),
            Revision = StoryRevision(record),
            ApprovedBy = approved || historical ? record!.Actor : null,
            PlanState = historical ? "Needs review" : approved ? progress.All(task => task.Status == "Complete") ? "Complete" : "Approved" : "Proposed",
            TaskProgress = progress
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
                record = new(DeliveryBasisHash(approvedBasis), result, actor.Trim(), reason!.Trim(), now, []) { Basis = approvedBasis };
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
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(record, Json), new UTF8Encoding(false)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Story(workspacePath, slug, storyId, false) with { CompletionContexts = completionContexts };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return new("failed", slug, storyId, null, "", "", null, [], [], [error.Message]); }
    }
}
