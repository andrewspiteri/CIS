using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private static bool StoryTaskSensitive(CisFeatureStoryTask task)
        => new[] { task.Title, task.Description, task.Validation, task.Category, task.TaskTypeKey, task.TaskTypeVersion,
            task.InstanceKey, task.WorkOwnerTaskId, task.ChangeId, task.ApprovalGate }
            .Concat(task.AcceptanceCriteria ?? []).Concat(task.NegativeCriteria ?? []).Concat(task.RequiredOutputs ?? [])
            .Concat(task.DecisionIds ?? []).Concat((task.VerificationObligations ?? []).Where(item => item is not null).SelectMany(item =>
                new[] { item.Description, item.EvidenceRoute }.Concat(item.Criteria ?? []).Concat(item.EvidenceExpectations ?? [])
                    .Concat(item.SharedGateIds ?? []).Concat(item.BasisReferences ?? [])))
            .Any(value => value is not null && DeliverySensitive(value));

    private static CisFeatureStoryPlanning? StoryPlanning(WizardState state, string storyId)
        => state.Review.StoryPlanning?.LastOrDefault(item => item.StoryId == storyId);

    private static CisFeatureStoryChild? PlannedChild(WizardState state, string storyId)
        => (state.Review.StoryPlanning ?? []).GroupBy(item => item.StoryId).Select(group => group.Last())
            .SelectMany(item => item.Children).SingleOrDefault(child => child.Id == storyId);

    private static string StoryPlanningRevision(WizardState state, string storyId)
        => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Own = StoryPlanning(state, storyId), Child = PlannedChild(state, storyId) }, Json)));

    private static IReadOnlyList<DeliveryDraft> ExpandStoryPlanning(WizardState state, IReadOnlyList<DeliveryDraft> roots)
    {
        var drafts = roots.ToList();
        for (var index = 0; index < drafts.Count; index++)
        {
            var parent = drafts[index];
            foreach (var child in StoryPlanning(state, parent.Id)?.Children ?? [])
            {
                if (child.Id is null || !Regex.IsMatch(child.Id, "^[a-f0-9]{16}$") || drafts.Any(item => item.Id == child.Id))
                    throw new InvalidDataException("The retained story decomposition has a missing or duplicate identity. Reassess that parent without deleting its history.");
                if (child.Requirements.Count == 0) throw new InvalidDataException("The retained child story has no accepted requirement coverage.");
                drafts.Add(new(child.Id, parent.Phase, new(child.Title, child.Definition, child.Requirements,
                    parent.Story.Source + "/child:" + child.Key, parent.Story.FutureCandidate))
                {
                    ParentId = parent.Id,
                    ParentRequirementNumbers = child.RequirementNumbers,
                    StoryDependencies = child.DependsOn,
                    SharedPrerequisiteTaskIds = child.SharedPrerequisiteTaskIds
                });
                if (drafts.Count > 80) throw new InvalidDataException("This feature exceeds its supported 80-story bound. Preserve the decomposition and split the feature through its existing scope workflow.");
            }
        }
        return drafts;
    }

    private static string StoryPlanningBasis(WizardState state, DeliveryInput input, string storyId)
        => DeliveryBasisHash(ApprovedStoryBasis(state, input, storyId, []));

    private static IReadOnlyList<string> CurrentStoryAcceptance(DeliveryInput input, DeliveryDraft draft)
    {
        if (draft.ParentId is null) return draft.Story.Acceptance;
        var parent = input.Drafts.Single(item => item.Id == draft.ParentId);
        var current = CurrentStoryAcceptance(input, parent);
        return draft.ParentRequirementNumbers.Select(number => number > 0 && number <= current.Count
            ? current[number - 1] : "missing parent requirement " + number).ToArray();
    }

    private static CisFeatureStoryResult ProjectStoryPlanning(WizardState state, DeliveryInput input, CisFeatureStoryResult result)
    {
        var draft = input.Drafts.Single(item => item.Id == result.StoryId);
        var planning = StoryPlanning(state, result.StoryId);
        var assessment = planning?.Assessment ?? PlannedChild(state, result.StoryId)?.Assessment;
        var basis = StoryPlanningBasis(state, input, result.StoryId);
        var coverageChanged = !draft.Story.Acceptance.SequenceEqual(CurrentStoryAcceptance(input, draft), StringComparer.Ordinal);
        var current = assessment is not null && assessment.BasisHash == basis && !coverageChanged;
        var reason = coverageChanged ? "Accepted parent requirements changed. Refresh the parent's explicit decomposition coverage before reassessing this child; its old clauses and history remain retained."
            : assessment is null ? "Record this story's delivery-size assessment before implementation. Planning and investigation remain available."
            : !current ? "The story's material scope or prerequisites changed. Reassess its delivery size; retained decisions and work history remain available."
            : assessment.Size is "high" ? "Decompose this above-medium delivery scope into coherent, assessed children before implementation."
            : assessment.Size is "unknown" ? "Resolve the named delivery-size uncertainty before implementation."
            : "The retained delivery-size assessment is current. Model reasoning strength is selected separately.";
        return result with
        {
            DeliverySize = assessment,
            DeliverySizeState = assessment is null ? "unknown" : current ? "current" : "stale",
            DeliverySizeReason = reason,
            PlanningBasisHash = basis,
            PlanningRevision = StoryPlanningRevision(state, result.StoryId),
            ParentStoryId = draft.ParentId,
            IsFutureCandidate = draft.Story.FutureCandidate,
            IsDeliveryContainer = planning?.Children.Count > 0,
            ChildStories = planning?.Children ?? []
        };
    }

    private static string? StoryDeliveryBlock(CisFeatureStoryResult result, CisFeatureStoryTask task)
    {
        // Bounded planning/investigation remains available to establish executable scope.
        if (StorySharedPreparation(task.Category)) return null;
        if (result.IsFutureCandidate) return "This is an uncommitted future candidate. Use the existing accepted-source/scope workflow to commit it before implementation; planning remains available.";
        if (result.IsDeliveryContainer) return "This story is a delivery container. Execute its assessed child outcomes instead of duplicating parent delivery work.";
        return result.DeliverySizeState != "current" || result.DeliverySize?.Size is not ("low" or "medium")
            ? result.DeliverySizeReason : null;
    }

    private static bool StorySharedPreparation(string? category)
        => category is "coordination" or "investigation" or "planning" or "wireframe" or "design";

    private sealed class StoryCompletionEvaluation
    {
        public Dictionary<string, bool> Completed { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> ScopeCurrent { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StoryPlanRecord?> Records { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Active { get; } = new(StringComparer.Ordinal);
    }

    private static StoryPlanRecord? StoryCompletionRecord(WizardState state, string storyId, StoryCompletionEvaluation evaluation, StoryPlanRecord? known = null)
    {
        if (!evaluation.Records.TryGetValue(storyId, out var record))
            evaluation.Records[storyId] = record = known ?? ReadStoryRecord(state, storyId);
        return record;
    }

    private static bool StoryCompletionScopeCurrent(WizardState state, DeliveryInput input, string storyId, StoryPlanRecord record,
        StoryCompletionEvaluation evaluation)
    {
        if (!evaluation.ScopeCurrent.TryGetValue(storyId, out var current))
            evaluation.ScopeCurrent[storyId] = current = StoryScopeMatches(record, state, input, storyId);
        return current;
    }

    private bool StoryTaskDependencyComplete(WizardState state, DeliveryInput input, string storyId, StoryPlanRecord record, string reference,
        StoryCompletionEvaluation? evaluation = null)
    {
        evaluation ??= new();
        var parts = reference.Split('/');
        return parts.Length == 1 ? StoryTaskCurrentlyComplete(state, input, storyId, reference, evaluation, record)
            : parts.Length == 2 && StoryTaskCurrentlyComplete(state, input, parts[0], parts[1], evaluation);
    }

    private bool StoryTaskCurrentlyComplete(WizardState state, DeliveryInput input, string storyId, string taskId,
        StoryCompletionEvaluation evaluation, StoryPlanRecord? known = null)
    {
        var key = storyId + "/" + taskId;
        if (evaluation.Completed.TryGetValue(key, out var complete)) return complete;
        // The existing story inventory allows twenty tasks plus its whole outcome.
        if (evaluation.Active.Count >= input.Drafts.Count * 21 || !evaluation.Active.Add(key)) return false;
        bool Result(bool current) => evaluation.Completed[key] = current;
        try
        {
            var draft = input.Drafts.SingleOrDefault(item => item.Id == storyId);
            if (draft is null) return Result(false);
            var record = StoryCompletionRecord(state, storyId, evaluation, known);
            var task = record?.Plan.Tasks.SingleOrDefault(item => item.Id == taskId);
            var last = record?.Events.LastOrDefault(item => item.TaskId == taskId);
            if (record is null || task is null || last?.Status != "Complete" || last.NeedsVerificationRenewal
                || !StoryCompletionScopeCurrent(state, input, storyId, record, evaluation)) return Result(false);
            foreach (var reference in task.DependsOn.Concat(draft.SharedPrerequisiteTaskIds).Distinct(StringComparer.Ordinal))
            {
                var parts = reference.Split('/');
                if (!(parts.Length == 1 ? StoryTaskCurrentlyComplete(state, input, storyId, reference, evaluation, record)
                    : parts.Length == 2 && StoryTaskCurrentlyComplete(state, input, parts[0], parts[1], evaluation))) return Result(false);
            }
            return Result(draft.StoryDependencies.All(dependency => StoryOutcomeComplete(state, input, dependency, evaluation)));
        }
        finally { evaluation.Active.Remove(key); }
    }

    private bool StoryContainerOutcomeComplete(WizardState state, DeliveryInput input, string storyId, StoryCompletionEvaluation? evaluation = null)
        => StoryOutcomeComplete(state, input, storyId, evaluation);

    private bool StoryOutcomeComplete(WizardState state, DeliveryInput input, string storyId, StoryCompletionEvaluation? evaluation = null)
    {
        evaluation ??= new();
        var key = "outcome/" + storyId;
        if (evaluation.Completed.TryGetValue(key, out var complete)) return complete;
        if (evaluation.Active.Count >= input.Drafts.Count * 21 || !evaluation.Active.Add(key)) return false;
        bool Result(bool current) => evaluation.Completed[key] = current;
        try
        {
            var draft = input.Drafts.SingleOrDefault(item => item.Id == storyId);
            if (draft is null || !draft.StoryDependencies.All(dependency => StoryOutcomeComplete(state, input, dependency, evaluation))) return Result(false);
            var children = StoryPlanning(state, storyId)?.Children ?? [];
            var record = StoryCompletionRecord(state, storyId, evaluation);
            if (children.Count > 0)
                return Result((record?.Plan.Tasks.Where(task => StorySharedPreparation(
                        (taskContracts?.ResolveStoryTask(state.Authority.RepositoryPath, task) ?? task).Category)) ?? [])
                    .All(task => StoryTaskCurrentlyComplete(state, input, storyId, task.Id, evaluation, record))
                    && children.All(child => StoryOutcomeComplete(state, input, child.Id!, evaluation)));
            return Result(record is not null && record.Plan.Tasks.Count > 0
                && record.Plan.Tasks.All(task => StoryTaskCurrentlyComplete(state, input, storyId, task.Id, evaluation, record)));
        }
        finally { evaluation.Active.Remove(key); }
    }

    private static bool StoryDependencyExists(WizardState state, DeliveryInput input, string storyId, string reference)
    {
        var parts = reference.Split('/');
        if (parts.Length != 2 || parts[0] == storyId || !Regex.IsMatch(parts[0], "^[a-f0-9]{16}$")
            || !Regex.IsMatch(parts[1], "^T[1-9][0-9]*$") || !input.Drafts.Any(item => item.Id == parts[0])) return false;
        var record = ReadStoryRecord(state, parts[0]);
        if (record is not null) return record.Plan.Tasks.Any(task => task.Id == parts[1]);
        var cache = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", state.Record.Plan.Slug, parts[0] + ".json");
        return SafeAbsolutePath(cache) && ReadStoryCache(cache)?.Tasks.Any(task => task.Id == parts[1]) == true;
    }

    private static IReadOnlyList<CisFeatureStoryChild> PreserveParentTaskPrerequisites(WizardState state, DeliveryInput input,
        string parentId, IReadOnlyList<CisFeatureStoryChild> proposed)
    {
        var children = proposed.ToArray();
        foreach (var child in children)
            if (child.SharedPrerequisiteTaskIds is null || child.SharedPrerequisiteTaskIds.Count > 100
                || child.SharedPrerequisiteTaskIds.Distinct(StringComparer.Ordinal).Count() != child.SharedPrerequisiteTaskIds.Count
                || child.SharedPrerequisiteTaskIds.Any(reference => !StoryDependencyExists(state, input, child.Id!, reference)))
                throw new InvalidDataException("Shared prerequisites must resolve to exact existing story-id/task-id instances.");
        var parent = ReadStoryRecord(state, parentId);
        if (parent is null) return children;
        foreach (var task in parent.Plan.Tasks)
            foreach (var dependency in task.DependsOn)
            {
                var local = parent.Plan.Tasks.SingleOrDefault(item => item.Id == dependency);
                var shared = dependency.Contains('/') ? dependency : StorySharedPreparation(local?.Category)
                    ? parentId + "/" + dependency : null;
                var recipients = children.Select((child, index) => (child, index)).Where(item =>
                    item.child.RequirementNumbers.Intersect(task.RequirementNumbers).Any()).ToArray();
                if (shared is not null)
                {
                    if (!recipients.Any(item => item.child.SharedPrerequisiteTaskIds.Contains(shared, StringComparer.Ordinal)))
                        throw new InvalidDataException($"Map retained parent prerequisite {task.Id} -> {shared} to its applicable child's sharedPrerequisiteTaskIds. No dependency is selected by type or position.");
                    continue;
                }
                if (local is null) throw new InvalidDataException("A retained parent prerequisite is unresolved: " + task.Id + " -> " + dependency);
                foreach (var (child, index) in recipients)
                {
                    var needed = local.RequirementNumbers.Except(child.RequirementNumbers).ToArray();
                    if (needed.Length == 0 || child.SharedPrerequisiteTaskIds.Contains(parentId + "/" + local.Id)) continue;
                    var owners = children.Where(other => other.Id != child.Id && other.RequirementNumbers.Intersect(needed).Any()).ToArray();
                    if (owners.Length == 1 && needed.All(owners[0].RequirementNumbers.Contains))
                        children[index] = child with { DependsOn = child.DependsOn.Append(owners[0].Id!).Distinct(StringComparer.Ordinal).ToArray() };
                    else if (!needed.All(number => children.Any(other => child.DependsOn.Contains(other.Id!) && other.RequirementNumbers.Contains(number))))
                        throw new InvalidDataException($"Map ambiguous retained prerequisite {task.Id} -> {local.Id} through exact child outcome dependencies or sharedPrerequisiteTaskIds; its accepted coverage cannot be dropped.");
                }
            }
        return children;
    }

    private IReadOnlyList<CisFeatureStoryTask> StoryTaskInstances(IReadOnlyList<CisFeatureStoryTask>? tasks,
        IReadOnlyList<CisFeatureStoryTask> previous, string slug, string storyId, WizardState state, DeliveryInput input)
    {
        if (tasks is null) throw new InvalidDataException("Supply the complete proposed task list.");
        var accepted = ReadStoryRecord(state, storyId) is not null;
        var ordinal = previous.Concat(tasks).Select(task => task is not null && task.Id is { Length: > 1 }
            && int.TryParse(task.Id[1..], out var number) ? number : 0).DefaultIfEmpty().Max();
        string AllocateId() => ordinal >= 999_999 ? throw new InvalidDataException("The bounded story task identity inventory is exhausted; retain existing instances before preparing further proposals.") : "T" + ++ordinal;
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<CisFeatureStoryTask>();
        var implicitInstances = new List<(string InputId, CisFeatureStoryTask? Previous)>();
        foreach (var task in tasks)
        {
            if (task is null || task.DependsOn is null) throw new InvalidDataException("A proposed task or its dependency list is empty.");
            var key = task.InstanceKey ?? slug + "/" + storyId + "/" + task.Id;
            var old = task.InstanceKey is null ? previous.SingleOrDefault(item => item.Id == task.Id)
                : previous.SingleOrDefault(item => (item.InstanceKey ?? slug + "/" + storyId + "/" + item.Id) == key);
            if (old is not null && task.InstanceKey is null) key = old.InstanceKey ?? key;
            static CisFeatureStoryTask Comparable(CisFeatureStoryTask item) => item with
            { InstanceKey = null, StoryId = null, Targets = item.Targets is { Count: 0 } ? item.RepositoryIds : item.Targets };
            if (old is not null && task.InstanceKey is null && JsonSerializer.Serialize(Comparable(old), Json)
                != JsonSerializer.Serialize(Comparable(task), Json))
            {
                if (accepted && LegacyStorySemanticsAdded(old, task, slug, storyId))
                { /* The exact legacy instance gains semantics; its accepted work identity is unchanged. */ }
                else if (accepted || old.WorkOwnerTaskId is not null)
                    throw new InvalidDataException("Retain the existing instanceKey when changing accepted or owned work; task position or title cannot reassign its identity.");
                else
                {
                    key = slug + "/" + storyId + "/" + AllocateId();
                    old = null;
                }
            }
            if (old?.WorkOwnerTaskId is not null && (task.WorkOwnerTaskId is not null && task.WorkOwnerTaskId != old.WorkOwnerTaskId
                || task.ChangeId is not null && task.ChangeId != old.ChangeId))
                throw new InvalidDataException("Task regeneration cannot change its existing accounting owner or containing change.");
            var id = old?.Id ?? (key == slug + "/" + storyId + "/" + task.Id ? task.Id : task.InstanceKey is null ? key[(key.LastIndexOf('/') + 1)..] : task.Id);
            if (!remap.TryAdd(task.Id, id) || result.Any(item => item.InstanceKey == key))
                throw new InvalidDataException("Each proposed task needs a distinct stable instance identity.");
            result.Add(task with
            {
                Id = id,
                InstanceKey = key,
                StoryId = storyId,
                WorkOwnerTaskId = old?.WorkOwnerTaskId ?? task.WorkOwnerTaskId,
                ChangeId = old?.ChangeId ?? task.ChangeId
            });
            implicitInstances.Add((task.Id, task.InstanceKey is null ? old : null));
        }
        // A derived dependent whose actual prerequisite changed is also new logical work.
        // Remap the whole proposal before validation instead of reusing a positional identity.
        if (!accepted)
            while (true)
            {
                var changed = false;
                for (var index = 0; index < result.Count; index++)
                {
                    var (inputId, old) = implicitInstances[index];
                    if (old is null || old.WorkOwnerTaskId is not null
                        || result[index].DependsOn.Select(id => remap.GetValueOrDefault(id, id)).SequenceEqual(old.DependsOn, StringComparer.Ordinal)) continue;
                    var freshId = AllocateId();
                    result[index] = result[index] with { Id = freshId, InstanceKey = slug + "/" + storyId + "/" + freshId };
                    remap[inputId] = freshId; implicitInstances[index] = (inputId, null); changed = true;
                }
                if (!changed) break;
            }
        foreach (var old in previous.Where(task => task.WorkOwnerTaskId is not null))
            if (!result.Any(task => task.InstanceKey == (old.InstanceKey ?? slug + "/" + storyId + "/" + old.Id)))
                throw new InvalidDataException("Retain owned task instance " + old.Id + " and its instanceKey/work owner. Use the existing source/scope retirement workflow before removing accepted work; a new suffix or renamed proposal cannot reset its accounting identity.");
        var draft = input.Drafts.Single(item => item.Id == storyId);
        var parent = draft.ParentId is { } parentId ? ReadStoryRecord(state, parentId) : null;
        var inherited = parent?.Plan.Tasks.Where(task => task.WorkOwnerTaskId is not null
            && task.RequirementNumbers.Intersect(draft.ParentRequirementNumbers).Any()).ToArray() ?? [];
        for (var index = 0; index < result.Count; index++)
        {
            var task = result[index];
            if (task.RequirementNumbers is null) throw new InvalidDataException("A proposed task needs exact accepted requirement references.");
            var parentRequirements = task.RequirementNumbers.Where(number => number > 0 && number <= draft.ParentRequirementNumbers.Count)
                .Select(number => draft.ParentRequirementNumbers[number - 1]).ToArray();
            var candidates = inherited.Where(old => old.RequirementNumbers.Intersect(parentRequirements).Any())
                .DistinctBy(old => (old.ChangeId, old.WorkOwnerTaskId)).ToArray();
            if (candidates.Length == 1)
            {
                if (task.WorkOwnerTaskId is not null && (task.WorkOwnerTaskId != candidates[0].WorkOwnerTaskId || task.ChangeId != candidates[0].ChangeId))
                    throw new InvalidDataException("Child task " + task.Id + " must retain its applicable parent work owner; decomposition does not create a fresh allowance.");
                task = task with { WorkOwnerTaskId = candidates[0].WorkOwnerTaskId, ChangeId = candidates[0].ChangeId };
            }
            else if (candidates.Length > 1 && !candidates.Any(old => old.WorkOwnerTaskId == task.WorkOwnerTaskId && old.ChangeId == task.ChangeId))
                throw new InvalidDataException("Child task " + task.Id + " overlaps multiple retained parent work owners. Map exact changeId/workOwnerTaskId instances explicitly instead of selecting by type, title or position.");
            if (task.WorkOwnerTaskId is not null || task.ChangeId is not null)
            {
                if (task.WorkOwnerTaskId is null || task.ChangeId is null || taskContracts is null)
                    throw new InvalidDataException("An existing work owner needs its exact canonical change and task-contract reader.");
                _ = taskContracts.ReadTaskDigest(state.Authority.RepositoryPath, task.ChangeId, task.WorkOwnerTaskId);
            }
            result[index] = task;
        }
        foreach (var owner in inherited)
            if (!result.Any(task => task.WorkOwnerTaskId == owner.WorkOwnerTaskId && task.ChangeId == owner.ChangeId))
                throw new InvalidDataException("Explicitly preserve every applicable parent accounting owner in the child task contract; split ambiguous requirement coverage before preparing work.");
        return result.Select(task => task with { DependsOn = task.DependsOn.Select(id => remap.GetValueOrDefault(id, id)).ToArray() }).ToArray();
    }

    private static void ValidateStoryTaskCycles(WizardState state, DeliveryInput input, string storyId, IReadOnlyList<CisFeatureStoryTask> tasks)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string owner, string taskId)
        {
            var key = owner + "/" + taskId;
            if (done.Contains(key)) return;
            if (!visiting.Add(key)) throw new InvalidDataException("Task dependencies contain a cycle across exact story/task instances: " + key);
            if (visiting.Count + done.Count > 1600) throw new InvalidDataException("Task dependency coverage exceeds the bounded feature task inventory.");
            var selected = owner == storyId ? tasks : ReadStoryRecord(state, owner)?.Plan.Tasks
                ?? ReadStoryCache(Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", state.Record.Plan.Slug, owner + ".json"))?.Tasks ?? [];
            var task = selected.SingleOrDefault(item => item.Id == taskId) ?? throw new InvalidDataException("A task dependency no longer resolves to its exact instance: " + key);
            foreach (var dependency in task.DependsOn.Concat(input.Drafts.Single(item => item.Id == owner).SharedPrerequisiteTaskIds).Distinct(StringComparer.Ordinal))
            {
                var parts = dependency.Split('/');
                if (parts.Length == 1) Visit(owner, dependency);
                else if (parts.Length == 2 && input.Drafts.Any(item => item.Id == parts[0])) Visit(parts[0], parts[1]);
                else throw new InvalidDataException("Use a local task ID or an exact story-id/task-id prerequisite: " + dependency);
            }
            visiting.Remove(key); done.Add(key);
        }
        foreach (var task in tasks) Visit(storyId, task.Id);
    }

    public CisFeatureStoryResult PrepareStory(string workspacePath, CisFeatureStoryPrepareRequest request)
    {
        try
        {
            if (!SingleLine(request.Actor, 200)) throw new InvalidDataException("Identify the controller supplying the proposed task contract.");
            var state = ReadWizard(workspacePath, request.Slug);
            var path = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", request.Slug, request.StoryId + ".json");
            if (!Regex.IsMatch(request.StoryId, "^[a-f0-9]{16}$") || !SafeAbsolutePath(path)) throw new InvalidDataException("Select a current story.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            var current = Story(workspacePath, request.Slug, request.StoryId, false);
            if (current.Errors.Count > 0) return current;
            if (request.ExpectedInputHash != current.InputHash || request.ExpectedRevision != current.Revision)
                throw new InvalidDataException("The story input or task history changed. Refresh before preparing the contract.");
            var input = ReadDeliveryInput(state, false);
            var selectedStory = ReadStoryDelivery(state, input).Stories.Single(item => item.Id == request.StoryId);
            var selectedDraft = input.Drafts.Single(item => item.Id == request.StoryId);
            var prior = ReadStoryCache(path);
            var retained = ReadStoryRecord(state, request.StoryId);
            var instances = StoryTaskInstances(request.Tasks, retained?.Plan.Tasks ?? prior?.Tasks ?? [], request.Slug, request.StoryId, state, input);
            var restoresApproval = retained is not null && StoryScopeMatches(retained, state, input, request.StoryId)
                && StoryTaskContractsEqual(instances, retained.Plan.Tasks, request.Slug, request.StoryId);
            var tasks = ValidateStoryTasks(instances, input, selectedStory, state,
                explicitContract: !restoresApproval, retainLegacy: restoresApproval);
            var next = current with
            {
                Status = "current",
                Tasks = tasks,
                Provider = "controller",
                Model = null,
                Story = selectedStory,
                Definition = selectedDraft.Story.Narrative,
                PlanState = "Proposed",
                IsRevisionProposal = retained is not null && !restoresApproval
            };
            var check = Story(workspacePath, request.Slug, request.StoryId, false);
            if (check.InputHash != current.InputHash || check.Revision != current.Revision) throw new InvalidDataException("Story inputs changed during preparation; previous tasks are retained.");
            if (JsonSerializer.Serialize(current.Tasks, Json) == JsonSerializer.Serialize(tasks, Json)
                && current.Status == "current" && !current.IsRevisionProposal)
                return current with { Applied = false };
            if (prior is not null && prior.Revision == current.Revision && prior.InputHash == current.InputHash
                && JsonSerializer.Serialize(prior.Tasks, Json) == JsonSerializer.Serialize(tasks, Json))
                return ProjectStoryWorkflow(state, input, next) with { Applied = false };
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(next, Json), new UTF8Encoding(false)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return ProjectStoryWorkflow(state, input, next) with { Applied = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return new("failed", request.Slug, request.StoryId, null, "", "", null, [], [], [error.Message]); }
    }

    public CisFeatureStoryResult AssessStory(string workspacePath, CisFeatureStoryAssessmentRequest request)
        => SaveStoryPlanning(workspacePath, request.Slug, request.StoryId, request.ExpectedBasisHash,
            request.ExpectedRevision, request.Actor, request.Assessment, null);

    public CisFeatureStoryResult DecomposeStory(string workspacePath, CisFeatureStoryDecompositionRequest request)
        => SaveStoryPlanning(workspacePath, request.Slug, request.StoryId, request.ExpectedBasisHash,
            request.ExpectedRevision, request.Actor, null, request.Children);

    private CisFeatureStoryResult SaveStoryPlanning(string workspacePath, string slug, string storyId, string expectedBasis,
        string expectedRevision, string actor, CisStorySizeAssessment? assessment, IReadOnlyList<CisFeatureStoryChild>? children)
    {
        try
        {
            if (!SingleLine(actor, 200)) throw new InvalidDataException("Identify the authorized controller recording this planning assessment.");
            var state = ReadWizard(workspacePath, slug);
            var lockPath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-intake.lock");
            using var featureLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            state = ReadWizard(workspacePath, slug);
            var input = ReadDeliveryInput(state, false);
            var draft = input.Drafts.SingleOrDefault(item => item.Id == storyId) ?? throw new InvalidDataException("Select a current story.");
            if (!draft.Story.Acceptance.SequenceEqual(CurrentStoryAcceptance(input, draft), StringComparer.Ordinal))
                throw new InvalidDataException("Accepted parent clauses changed. Update the parent's explicit decomposition before assessing or decomposing this retained child.");
            if (StoryPlanningBasis(state, input, storyId) != expectedBasis || StoryPlanningRevision(state, storyId) != expectedRevision)
                throw new InvalidDataException("The story's scope or planning record changed. Refresh its assessment before saving.");
            var previous = StoryPlanning(state, storyId);
            var nextAssessment = assessment is null ? previous?.Assessment : CheckedSize(assessment, expectedBasis);
            var nextChildren = previous?.Children ?? [];
            if (children is not null)
            {
                if (children.Count is < 2 or > 20) throw new InvalidDataException("Decompose into 2 to 20 coherent child outcomes, rather than one task per assertion.");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var coverage = new HashSet<int>();
                nextChildren = children.Select(child =>
                {
                    if (child is null || !Regex.IsMatch(child.Key ?? "", "^[a-z][a-z0-9-]{0,63}$") || !keys.Add(child.Key!)
                        || !SingleLine(child.Title, 160) || string.IsNullOrWhiteSpace(child.Definition) || child.Definition.Length > 4000)
                        throw new InvalidDataException("Each child needs a unique stable key, title and coherent outcome definition.");
                    if (child.RequirementNumbers is not { Count: > 0 } || child.RequirementNumbers.Distinct().Count() != child.RequirementNumbers.Count
                        || child.RequirementNumbers.Any(number => number < 1 || number > draft.Story.Acceptance.Count))
                        throw new InvalidDataException("Each child must reference exact accepted parent requirement numbers.");
                    if (child.DependsOn is null || child.DependsOn.Count > 20 || child.DependsOn.Distinct().Count() != child.DependsOn.Count)
                        throw new InvalidDataException("Child dependencies must be distinct explicit child keys or existing story identities.");
                    coverage.UnionWith(child.RequirementNumbers);
                    return child with
                    {
                        Id = Hash(Encoding.UTF8.GetBytes(storyId + "/" + child.Key))[7..23],
                        Requirements = child.RequirementNumbers.Select(number => draft.Story.Acceptance[number - 1]).ToArray()
                    };
                }).ToArray();
                if (coverage.Count != draft.Story.Acceptance.Count) throw new InvalidDataException("The decomposition omits accepted parent requirements: "
                    + string.Join(", ", Enumerable.Range(1, draft.Story.Acceptance.Count).Except(coverage)) + ". Shared constraints and exclusions remain inherited.");
                if ((previous?.Children ?? []).Any(old => !keys.Contains(old.Key)))
                    throw new InvalidDataException("Retain existing child identities and their history. Use the existing source/scope workflow before withdrawing accepted child work.");
                var ids = nextChildren.ToDictionary(child => child.Key, child => child.Id!, StringComparer.Ordinal);
                nextChildren = nextChildren.Select(child => child with { DependsOn = child.DependsOn.Select(id => ids.GetValueOrDefault(id, id)).ToArray() }).ToArray();
                nextChildren = PreserveParentTaskPrerequisites(state, input, storyId, nextChildren);
                var graph = nextChildren.ToDictionary(child => child.Id!, child => child.DependsOn, StringComparer.Ordinal);
                var pending = graph.Keys.ToHashSet(StringComparer.Ordinal);
                foreach (var child in nextChildren)
                    if (child.DependsOn.Any(id => id == child.Id || id == storyId || !graph.ContainsKey(id) && !input.Drafts.Any(other => other.Id == id)))
                        throw new InvalidDataException("A child dependency is missing, self-referential, or points to its organizing parent.");
                while (pending.Count > 0)
                {
                    var ready = pending.Where(id => graph[id].All(dependency => !pending.Contains(dependency))).ToArray();
                    if (ready.Length == 0) throw new InvalidDataException("Child story dependencies contain a cycle.");
                    pending.ExceptWith(ready);
                }
                var combined = input.Drafts.ToDictionary(item => item.Id, item => item.StoryDependencies, StringComparer.Ordinal);
                foreach (var child in nextChildren) combined[child.Id!] = child.DependsOn;
                foreach (var planning in (state.Review.StoryPlanning ?? []).GroupBy(item => item.StoryId).Select(group => group.Last())
                    .Where(item => item.StoryId != storyId && combined.ContainsKey(item.StoryId)))
                    combined[planning.StoryId] = combined[planning.StoryId].Concat(planning.Children.Select(child => child.Id!)
                        .Where(combined.ContainsKey)).Distinct(StringComparer.Ordinal).ToArray();
                combined[storyId] = combined[storyId].Concat(nextChildren.Select(child => child.Id!)).Distinct(StringComparer.Ordinal).ToArray();
                var active = new HashSet<string>(StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal);
                var route = new List<string>();
                void Visit(string id)
                {
                    if (done.Contains(id)) return;
                    if (!active.Add(id)) throw new InvalidDataException("Story outcome dependency cycle (including container completion): " + string.Join(" -> ", route.Append(id)));
                    route.Add(id);
                    foreach (var dependency in combined.GetValueOrDefault(id) ?? []) Visit(dependency);
                    route.RemoveAt(route.Count - 1); active.Remove(id); done.Add(id);
                }
                foreach (var id in combined.Keys) Visit(id);
                var provisional = new CisFeatureStoryPlanning(storyId, nextAssessment, nextChildren, actor.Trim(), "");
                var candidate = state with { Review = state.Review with { StoryPlanning = [.. state.Review.StoryPlanning ?? [], provisional] } };
                var candidateInput = ReadDeliveryInput(candidate, false);
                nextChildren = nextChildren.Select(child => child with
                {
                    Assessment = CheckedSize(child.Assessment,
                    StoryPlanningBasis(candidate, candidateInput, child.Id!))
                }).ToArray();
            }
            var next = new CisFeatureStoryPlanning(storyId, nextAssessment, nextChildren, actor.Trim(), DateTimeOffset.UtcNow.ToString("O"));
            if (previous is not null && JsonSerializer.Serialize(previous with { SavedAt = "" }, Json) == JsonSerializer.Serialize(next with { SavedAt = "" }, Json))
                return Story(workspacePath, slug, storyId, false) with { Applied = false };
            if (state.Review.StoryPlanning?.Count >= 1000) throw new InvalidDataException("The bounded planning history is full; retain it before another planning update.");
            var review = state.Review with { StoryPlanning = [.. state.Review.StoryPlanning ?? [], next] };
            WriteStoryPlanning(state, review);
            return Story(workspacePath, slug, storyId, false) with { Applied = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return new("failed", slug, storyId, null, "", "", null, [], [], [error.Message]); }
    }

    private static CisStorySizeAssessment CheckedSize(CisStorySizeAssessment assessment, string basis)
    {
        if (assessment is null || assessment.Size is not ("low" or "medium" or "high" or "unknown")
            || new[] { assessment.Rationale, assessment.BehavioralBreadth, assessment.Interfaces, assessment.StateAndConcurrency,
                assessment.Coordination, assessment.Uncertainty }.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 2000 || value.Contains('\0')))
            throw new InvalidDataException("Assess delivery size with a rationale and explicit behavioral breadth, interfaces, state/concurrency, coordination and uncertainty. Model choice or a size label alone is insufficient.");
        return assessment with { BasisHash = basis };
    }

    private static void WriteStoryPlanning(WizardState state, FeatureReview review)
    {
        var block = RenderReview(review, state.Record.Plan);
        var start = state.Content.IndexOf(ReviewStart, StringComparison.Ordinal);
        var end = start < 0 ? -1 : state.Content.IndexOf(ReviewEnd, start, StringComparison.Ordinal);
        if (start >= 0 && end < 0) throw new InvalidDataException("Restore the incomplete feature review block before saving planning.");
        var content = start < 0 ? state.Content.TrimEnd() + "\n\n" + block + "\n"
            : state.Content[..start] + block + state.Content[(end + ReviewEnd.Length)..];
        if (File.ReadAllText(state.RequestPath).Replace("\r\n", "\n", StringComparison.Ordinal) != state.Content
            || state.Inputs.Any(input => FileHash(input.Key) != input.Value))
            throw new InvalidDataException("The feature or accepted inputs changed while saving planning. Refresh; previous planning remains preserved.");
        var temporary = state.RequestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content, new UTF8Encoding(false)); File.Move(temporary, state.RequestPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
