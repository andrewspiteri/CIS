using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    public CisFeatureStoryResult ExecuteStoryTask(string workspacePath, string slug, string storyId, string taskId,
        bool execute = false, string? expectedPlanHash = null, string? expectedRevision = null, string? expectedExecutionHash = null,
        string actor = "", bool allowRemote = false, CancellationToken cancellation = default, Action<CisAgentProviderEvent>? progress = null)
    {
        try
        {
            if (storyExecutor is null) throw new InvalidDataException("No story task execution service is loaded.");
            var state = ReadWizard(workspacePath, slug);
            var result = Story(workspacePath, slug, storyId, false);
            if (result.Errors.Count > 0) return result;
            var task = result.Tasks.SingleOrDefault(item => item.Id == taskId) ?? throw new InvalidDataException("Select a task in this story.");
            var taskProgress = result.TaskProgress.Single(item => item.Id == taskId);
            if (!taskProgress.CanExecute) return result with { Errors = [taskProgress.BlockedReason ?? "This task is not available for execution."] };
            var input = ReadDeliveryInput(state, false);
            var work = StoryWork(state, input, result, task, actor, previous: taskProgress.Execution,
                responses: (taskProgress.ReviewFeedback ?? []).Where(answer => !string.IsNullOrWhiteSpace(answer.Answer)).ToArray());
            if (StoryTaskSensitive(task) || new[] { result.Definition, work.Direction, work.Constraints }.Concat(result.Story!.Requirements).Any(DeliverySensitive))
                throw new InvalidDataException("The selected task context may contain credentials. Remove them before execution.");
            if (work.ReviewResponses.Any(answer => DeliverySensitive(answer.Answer!)))
                throw new InvalidDataException("A review answer may contain credentials. Remove them before execution.");
            var selection = storyExecutor.SelectModels(work);
            selection = selection with { Work = selection.Work ?? taskProgress.Work };
            result = result with { ExecutionPlan = selection };
            if (!execute) return result with { Errors = selection.Errors };
            if (selection.Errors.Count > 0) return result with { Errors = selection.Errors };
            if (expectedPlanHash != result.PlanHash || expectedRevision != result.Revision || expectedExecutionHash != selection.Hash)
                throw new InvalidDataException("The task, progress or model selection changed. Refresh before execution.");
            if (!SingleLine(actor, 200)) throw new InvalidDataException("Provide the person starting this task.");
            if (!allowRemote && (selection.Implementation?.IsLocal != true || selection.Review?.IsLocal != true))
                throw new InvalidDataException("Authorize sending task and repository context to the implementation and review models.");
            var lockPath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-stories", slug, storyId + ".json.lock");
            if (!SafeAbsolutePath(lockPath)) throw new InvalidDataException("The story execution lock uses an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            var current = Story(workspacePath, slug, storyId, false);
            if (current.PlanHash != expectedPlanHash || current.Revision != expectedRevision || !current.TaskProgress.Single(item => item.Id == taskId).CanExecute)
                throw new InvalidDataException("Task progress changed before execution. Refresh and retry.");
            var path = StoryRecordPath(state, storyId);
            var record = ReadStoryRecord(state, storyId)!;
            record = record with
            {
                Events = record.Events.Append(new StoryTaskEvent(taskId, "InProgress", actor.Trim(), DateTimeOffset.UtcNow.ToString("O"), "Agent execution requested.")
                { Execution = new("running", selection, [], [], [], [], false), NeedsVerificationRenewal = taskProgress.NeedsVerificationRenewal }).ToArray()
            };
            SaveStoryExecutionRecord(path, record);
            var runRevision = StoryRevision(record);
            bool StillCurrent()
            {
                var read = Story(workspacePath, slug, storyId, false);
                return read.Errors.Count == 0 && read.PlanHash == expectedPlanHash && read.Revision == runRevision && read.PlanState == "Approved";
            }
            CisStoryExecutionResult executed;
            try { executed = storyExecutor.Execute(work, selection, allowRemote, StillCurrent, cancellation, progress); }
            catch (OperationCanceledException) { executed = new("cancelled", selection, [], [], [], ["Task execution cancelled."], false); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            { executed = new("failed", selection, [], [], [], [error.Message], false); }
            record = record with
            {
                Events = record.Events.Append(new StoryTaskEvent(taskId, "InProgress", actor.Trim(), DateTimeOffset.UtcNow.ToString("O"),
                executed.Applied ? "Implementation and secondary review finished; reviewed changes applied. Human completion remains pending." : "Execution outcome: " + executed.Status)
                { Execution = executed, NeedsVerificationRenewal = taskProgress.NeedsVerificationRenewal && !executed.Applied }).ToArray()
            };
            SaveStoryExecutionRecord(path, record);
            return Story(workspacePath, slug, storyId, false) with { Errors = executed.Errors };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return new("failed", slug, storyId, null, "", "", null, [], [], [error.Message]); }
    }

    private static void SaveStoryExecutionRecord(string path, StoryPlanRecord record)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(record, Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
