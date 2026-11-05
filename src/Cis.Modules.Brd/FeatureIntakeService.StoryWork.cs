using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record StoryWorkBindingAudit(CisStoryWorkBinding Binding, string Actor, string Reason, string BoundAtUtc);

    private static string StoryWorkKey(string slug, string storyId, CisFeatureStoryTask task)
        => task.InstanceKey ?? slug + "/" + storyId + "/" + task.Id;

    private static CisStoryWorkBinding? StoryCanonicalBinding(WizardState state, DeliveryInput input, string storyId,
        CisFeatureStoryTask task, StoryPlanRecord? record = null, int depth = 0)
    {
        if (depth >= input.Drafts.Count) throw new InvalidDataException("The story work relationship has a recursive ownership ambiguity.");
        var key = StoryWorkKey(state.Record.Plan.Slug, storyId, task);
        var saved = (record ?? ReadStoryRecord(state, storyId))?.WorkBindings?.GetValueOrDefault(key)?.Binding;
        if (saved is not null) return saved;
        if (task.ChangeId is not null && task.WorkOwnerTaskId is not null) return new(task.ChangeId, task.WorkOwnerTaskId);
        var draft = input.Drafts.Single(item => item.Id == storyId);
        if (draft.ParentId is not { } parentId) return null;
        var parent = ReadStoryRecord(state, parentId);
        if (parent is null) return null;
        var requirements = task.RequirementNumbers.Where(number => number > 0 && number <= draft.ParentRequirementNumbers.Count)
            .Select(number => draft.ParentRequirementNumbers[number - 1]).ToArray();
        var candidates = parent.Plan.Tasks.Where(item => item.RequirementNumbers.Intersect(requirements).Any())
            .Select(item => StoryCanonicalBinding(state, input, parentId, item, parent, depth + 1))
            .OfType<CisStoryWorkBinding>().Distinct().ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidDataException("This task overlaps multiple retained work owners: "
                + string.Join(", ", candidates.Select(item => item.ChangeId + "/" + item.WorkOwnerTaskId))
                + ". Resolve the exact accepted task mapping through existing preparation before executing it.")
        };
    }

    private CisStoryTaskWork StoryWork(WizardState state, DeliveryInput input, CisFeatureStoryResult result,
        CisFeatureStoryTask task, string actor = "", CisStoryWorkBinding? binding = null,
        CisStoryExecutionResult? previous = null, IReadOnlyList<CisStoryReviewFeedback>? responses = null)
    {
        var record = ReadStoryRecord(state, result.StoryId);
        var repositories = task.RepositoryIds.Select(id => state.Workspace.Repositories.SingleOrDefault(item => item.Id == id && item.IsProductOwned)
            ?? throw new InvalidDataException("A task repository is no longer registered as product-owned.")).ToArray();
        var version = StoryContractVersion(record?.Basis);
        return new(state.Workspace.WorkspacePath, result.Slug, result.StoryId, result.PlanHash ?? "", result.Definition,
            task, task.RequirementNumbers.Select(number => result.Story!.Requirements[number - 1]).ToArray(), repositories, actor)
        {
            Direction = input.Direction,
            Constraints = EffectiveStoryConstraints(input, version),
            ContractVersion = version,
            CanonicalBinding = binding ?? StoryCanonicalBinding(state, input, result.StoryId, task, record),
            PreviousExecution = previous,
            ReviewResponses = responses ?? [],
            NeedsVerificationRenewal = result.TaskProgress.SingleOrDefault(item => item.Id == task.Id)?.NeedsVerificationRenewal == true
        };
    }

    private CisStoryWorkStatus InspectStoryWork(CisStoryTaskWork work)
    {
        var inspected = storyExecutor?.InspectWork(work) ?? new("unavailable", false, false, null, null,
            "Load the story execution service to inspect canonical ownership and allowance.", []);
        if (inspected.Status != "unavailable") return inspected;
        const string nextAction = "Load the current story work-control inspector before executing adopted story work; inspection must prove the exact owner and allowance.";
        try
        {
            var authority = workspaces.Resolve(work.WorkspacePath).Workspace?.AuthorityRepository?.RepositoryPath ?? work.WorkspacePath;
            var repositories = work.Repositories.Where(repository => repository.IsProductOwned).Select(repository => repository.RepositoryPath)
                .Prepend(authority).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var repository in repositories)
            {
                // A legacy executor cannot remove adopted obligations. A correction-only
                // ledger is deliberately not evidence of full work-control adoption.
                var marker = Path.Combine(repository, CisAgentWorkPaths.Adoption);
                if (File.Exists(marker) || Directory.Exists(marker) || CisEngineeringPolicy.Read(repository)?.RequireTaskCompletion == true)
                    return inspected with { Required = true, BindingCurrent = false, NextAction = nextAction, Errors = [nextAction] };
            }
            return inspected;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            return inspected with
            {
                Required = true,
                BindingCurrent = false,
                NextAction = nextAction,
                Errors = [nextAction, "Adopted story work-control applicability cannot be verified: " + error.Message]
            };
        }
    }

    private static string? StoryWorkHold(CisStoryWorkStatus work)
        => work.Errors.FirstOrDefault() ?? work.Control?.Errors.FirstOrDefault() ?? work.Control?.Work?.HoldReason
            ?? (work.Required && (!work.BindingCurrent || work.Control?.Work is null || work.Control?.ExitCode != 0) ? work.NextAction : null);

    public CisFeatureStoryResult StoryWorkStatus(string workspacePath, string slug, string storyId, string taskId)
    {
        var result = Story(workspacePath, slug, storyId, false);
        if (result.Errors.Count > 0) return result;
        var task = result.TaskProgress.SingleOrDefault(item => item.Id == taskId);
        if (task is null) return result with { Errors = ["Select a task from the current story to inspect its work context."] };
        var hold = task.Work is null ? null : StoryWorkHold(task.Work);
        return hold is null ? result : result with { Status = "held", Errors = [hold, task.Work!.NextAction] };
    }

    public CisFeatureStoryResult BindStoryWork(string workspacePath, CisStoryWorkBindingRequest request)
    {
        try
        {
            if (!SingleLine(request.Actor, 200) || !SingleLine(request.Reason, 2000)
                || !Regex.IsMatch(request.ChangeId ?? "", "^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")
                || !Regex.IsMatch(request.WorkOwnerTaskId ?? "", "^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$"))
                throw new InvalidDataException("Supply the existing canonical change/task, controller identity and binding rationale.");
            var state = ReadWizard(workspacePath, request.Slug);
            var lockPath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", request.Slug, request.StoryId + ".json.lock");
            _ = StoryRecordPath(state, request.StoryId);
            if (!SafeAbsolutePath(lockPath)) throw new InvalidDataException("The story lock uses an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            var result = Story(workspacePath, request.Slug, request.StoryId, false);
            if (result.Errors.Count > 0) return result;
            var record = ReadStoryRecord(state, request.StoryId);
            var input = ReadDeliveryInput(state, false);
            if (record is null || result.IsRevisionProposal || !StoryScopeMatches(record, state, input, request.StoryId)
                || result.PlanHash != request.ExpectedPlanHash || result.Revision != request.ExpectedRevision)
                throw new InvalidDataException("Bind only a current approved story task using its exact plan hash and revision. Refresh before binding.");
            var task = result.Tasks.SingleOrDefault(item => item.Id == request.TaskId) ?? throw new InvalidDataException("Select an approved story task.");
            var binding = new CisStoryWorkBinding(request.ChangeId!.ToUpperInvariant(), request.WorkOwnerTaskId!.ToUpperInvariant());
            var existing = StoryCanonicalBinding(state, input, request.StoryId, task, record);
            if (existing is not null && existing != binding)
                throw new InvalidDataException("This logical task already has a retained work owner. Binding cannot replace its ownership or reset its allowance.");
            var work = StoryWork(state, input, result, task, request.Actor, binding);
            _ = work.ForCanonicalGate();
            var inspected = InspectStoryWork(work);
            if (!inspected.BindingCurrent)
                return result with { Errors = inspected.Errors.Count > 0 ? inspected.Errors : [inspected.NextAction] };
            var key = StoryWorkKey(request.Slug, request.StoryId, task);
            if (record.WorkBindings?.GetValueOrDefault(key)?.Binding == binding) return result with { Applied = false };
            var bindings = record.WorkBindings?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            if (bindings.Count >= 20 && !bindings.ContainsKey(key)) throw new InvalidDataException("The bounded story work-binding inventory is full.");
            bindings[key] = new(binding, request.Actor.Trim(), request.Reason.Trim(), DateTimeOffset.UtcNow.ToString("O"));
            var check = Story(workspacePath, request.Slug, request.StoryId, false);
            if (check.Revision != request.ExpectedRevision || check.PlanHash != request.ExpectedPlanHash
                || !InspectStoryWork(work).BindingCurrent)
                throw new InvalidDataException("The story or canonical binding changed while saving. Refresh and retry.");
            var next = record with { WorkBindings = bindings };
            if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(next, Json).Length > 4_194_304)
                throw new InvalidDataException("The retained story history exceeds its supported size; its previous binding and approval are unchanged.");
            SaveStoryExecutionRecord(StoryRecordPath(state, request.StoryId), next);
            return Story(workspacePath, request.Slug, request.StoryId, false) with { Applied = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        { return new("failed", request.Slug, request.StoryId, null, "", "", null, [], [], [error.Message]); }
    }
}
