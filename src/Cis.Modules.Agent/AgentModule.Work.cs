using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentModule
{
    private static Command Work(AgentService service)
    {
        var group = new Command("work", "Allocate, amend and inspect durable task/story work, record progress, and resolve a bounded continuation.");
        var allocate = new Command("allocate", "Establish or update an authorized change and task allocation without resetting consumption.");
        var allocationChange = new Argument<string>("change-id");
        var allocationInput = Required("--input"); var allocationActor = Required("--actor"); var allocationReason = Required("--reason");
        var adoptAccounting = new Option<bool>("--adopt-accounting")
        { Description = "Explicitly adopt authority-wide finite accounting for a fresh minimal repository using approved canonical work and finite storage; does not adopt engineering quality policy." };
        var allocationRepo = Repo(); var allocationFormat = Format();
        allocate.Arguments.Add(allocationChange);
        foreach (var option in new Option[] { allocationInput, allocationActor, allocationReason, adoptAccounting, allocationRepo, allocationFormat }) allocate.Options.Add(option);
        allocate.SetAction(result =>
        {
            var format = result.GetValue(allocationFormat)!;
            if (!WorkFormatSupported(format)) return 2;
            return RenderWork(service.AllocateWork(result.GetValue(allocationRepo)!, result.GetValue(allocationChange)!,
                result.GetValue(allocationInput)!, result.GetValue(allocationActor)!, result.GetValue(allocationReason)!,
                result.GetValue(adoptAccounting)), format);
        });
        group.Subcommands.Add(allocate);

        var amendTime = new Command("amend-time", "Amend an existing task's absolute time ceiling by an explicit original-authority decision.");
        var amendmentChange = new Argument<string>("change-id"); var amendmentTask = new Argument<string>("task-id");
        var amendmentInput = Required("--input"); var amendmentActor = Required("--actor"); var amendmentReason = Required("--reason");
        var amendmentRepo = Repo(); var amendmentFormat = Format();
        amendTime.Arguments.Add(amendmentChange); amendTime.Arguments.Add(amendmentTask);
        foreach (var option in new Option[] { amendmentInput, amendmentActor, amendmentReason, amendmentRepo, amendmentFormat }) amendTime.Options.Add(option);
        amendTime.SetAction(result =>
        {
            var format = result.GetValue(amendmentFormat)!;
            if (!WorkFormatSupported(format)) return 2;
            return RenderWork(service.AmendWorkTaskTime(result.GetValue(amendmentRepo)!, result.GetValue(amendmentChange)!, result.GetValue(amendmentTask)!,
                result.GetValue(amendmentInput)!, result.GetValue(amendmentActor)!, result.GetValue(amendmentReason)!), format);
        });
        group.Subcommands.Add(amendTime);

        var status = new Command("status", "Read current work ownership, capacity, history, and holds without changing accounting.");
        var statusChange = new Argument<string>("change-id"); var statusTask = new Option<string?>("--task");
        var statusRelationship = new Option<string?>("--relationship") { Description = "Inspect the stable story correction relationship shown by execution-plan." };
        var statusRepo = Repo(); var statusFormat = Format();
        status.Arguments.Add(statusChange); status.Options.Add(statusTask); status.Options.Add(statusRelationship); status.Options.Add(statusRepo); status.Options.Add(statusFormat);
        status.SetAction(result =>
        {
            var format = result.GetValue(statusFormat)!;
            if (!WorkFormatSupported(format)) return 2;
            return RenderWork(service.WorkStatus(result.GetValue(statusRepo)!, result.GetValue(statusChange)!, result.GetValue(statusTask), result.GetValue(statusRelationship)), format);
        });
        group.Subcommands.Add(status);

        var progress = new Command("progress", "Record one concise outcome, evidence, measured cost context, approach, and next decision.");
        var progressChange = new Argument<string>("change-id"); var progressTask = new Argument<string>("task-id");
        var progressInput = Required("--input"); var progressActor = Required("--actor"); var progressRepo = Repo(); var progressFormat = Format();
        progress.Arguments.Add(progressChange); progress.Arguments.Add(progressTask);
        foreach (var option in new Option[] { progressInput, progressActor, progressRepo, progressFormat }) progress.Options.Add(option);
        progress.SetAction(result =>
        {
            var format = result.GetValue(progressFormat)!;
            if (!WorkFormatSupported(format)) return 2;
            return RenderWork(service.RecordWorkProgress(result.GetValue(progressRepo)!, result.GetValue(progressChange)!, result.GetValue(progressTask)!,
                result.GetValue(progressInput)!, result.GetValue(progressActor)!), format);
        });
        group.Subcommands.Add(progress);

        var disposition = new Command("disposition", "Record a justified continuation, simplification, hold, bounded reconciliation, or read-only restart qualification.");
        var dispositionChange = new Argument<string>("change-id"); var dispositionTask = new Argument<string>("task-id");
        var dispositionInput = Required("--input"); var dispositionActor = Required("--actor"); var dispositionReason = Required("--reason");
        var dispositionRepo = Repo(); var dispositionFormat = Format();
        disposition.Arguments.Add(dispositionChange); disposition.Arguments.Add(dispositionTask);
        foreach (var option in new Option[] { dispositionInput, dispositionActor, dispositionReason, dispositionRepo, dispositionFormat }) disposition.Options.Add(option);
        disposition.SetAction(result =>
        {
            var format = result.GetValue(dispositionFormat)!;
            if (!WorkFormatSupported(format)) return 2;
            return RenderWork(service.DisposeWork(result.GetValue(dispositionRepo)!, result.GetValue(dispositionChange)!, result.GetValue(dispositionTask)!,
                result.GetValue(dispositionInput)!, result.GetValue(dispositionActor)!, result.GetValue(dispositionReason)!), format);
        });
        group.Subcommands.Add(disposition);
        return group;
    }

    private static bool WorkFormatSupported(string format) => format is "human" or "json" or "agent";

    private static int RenderWork(CisAgentWorkResult result, string format)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else if (format == "agent")
        {
            Console.WriteLine($"status={Safe(result.Status)};exitCode={result.ExitCode};applied={result.Applied.ToString().ToLowerInvariant()}");
            RenderWorkView(result.Work, format);
            if (result.NativeRestartClosure is { } restart)
                Console.WriteLine($"restartClosure={Safe(restart.State)};operation={Safe(restart.Operation)};root={Safe(restart.RootExecutionId)};revision={Safe(restart.Revision)};treeDigest={Safe(restart.TreeDigest)};executions={restart.ExecutionCount};gaps={string.Join(',', restart.Gaps.Select(Safe))}");
            foreach (var error in result.Errors) Console.WriteLine($"error={Safe(error)}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"Agent work: {result.Status}");
            RenderWorkView(result.Work, format);
            if (result.NativeRestartClosure is { } restart)
            {
                Console.WriteLine($"Restart closure: {restart.State}; retained tree {restart.RootExecutionId} ({restart.ExecutionCount} executions). No restart challenge or closure receipt was created.");
                Console.WriteLine("Missing proof: " + string.Join(", ", restart.Gaps));
            }
            foreach (var error in result.Errors) Console.WriteLine(error);
        }
        else return 2;
        return result.ExitCode;
    }

    private static void RenderWorkView(CisAgentWorkView? work, string format)
    {
        if (work is null) return;
        if (format == "agent")
        {
            Console.WriteLine($"work={Safe(work.AuthorityRepositoryId)};change={Safe(work.ChangeId)};task={Safe(work.TaskId)};owner={Safe(work.OwnerTaskId)};revision={Safe(work.Revision)};adoption={Safe(work.AdoptionId)}");
            RenderWorkAllowance("change", work.Change, format);
            if (work.Task is not null) RenderWorkAllowance("task", work.Task, format);
            Console.WriteLine($"workCoverage={Safe(work.Coverage)};recovery={Safe(work.RecoveryState)};hold={Safe(work.HoldReason)};nextAction={Safe(work.NextAction)};logicalAttempt={Safe(work.LogicalAttemptId)};repeatedBlockers={work.RepeatedBlockerCount}");
            Console.WriteLine($"storyRelationship={Safe(work.CorrectionRelationshipId)};correctionOnly={work.CorrectionOnly.ToString().ToLowerInvariant()};boundedContinuation={work.CorrectionContinuationAvailable.ToString().ToLowerInvariant()}");
            foreach (var round in work.Corrections)
                Console.WriteLine($"correctionRound={Safe(round.Id)};relationship={Safe(round.RelationshipId)};number={round.Number};status={Safe(round.Status)};maximumSeconds={round.MaximumSeconds};repositories={string.Join(',', round.Stages.Select(stage => Safe(stage.RepositoryId)))};disposition={Safe(round.DispositionReference)}");
            foreach (var finding in work.RequiredCorrectionFindingReferences) Console.WriteLine("requiredCorrectionFinding=" + Safe(finding));
            Console.WriteLine($"workAuthority={Safe(work.DecisionReference)};storageOwner={Safe(work.StorageOwner)};storageCeiling={Safe(work.StorageCeilingReference)}");
            foreach (var amendment in work.TaskTimeAmendments.TakeLast(5))
                Console.WriteLine($"taskTimeAmendment={Safe(amendment.Reference)};task={Safe(amendment.TaskId)};owner={Safe(amendment.OwnerTaskId)};previousAllocatedMilliseconds={amendment.PreviousAllocatedMilliseconds};allocatedMilliseconds={amendment.AllocatedMilliseconds};actor={Safe(amendment.Actor)};decisionReference={Safe(amendment.DecisionReference)};recordedAt={Safe(amendment.RecordedAtUtc)};previousRevision={Safe(amendment.PreviousRevision)};revision={Safe(amendment.Revision)};reason={Safe(amendment.Reason)}");
            foreach (var execution in work.Executions.TakeLast(5))
                Console.WriteLine($"workExecution={Safe(execution.ExecutionId)};status={Safe(execution.Status)};activeMilliseconds={execution.ActiveMilliseconds};elapsedMilliseconds={execution.ElapsedMilliseconds};reservedMilliseconds={execution.ReservedMilliseconds};parent={Safe(execution.ParentExecutionId)};recovery={Safe(execution.RecoveryReason)}");
            if (work.Checkpoints.LastOrDefault() is { } checkpoint)
                Console.WriteLine($"workCheckpoint={Safe(checkpoint.LogicalAttemptId)};actor={Safe(checkpoint.Actor)};recordedAt={Safe(checkpoint.RecordedAtUtc)};outcome={Safe(checkpoint.Checkpoint.Outcome)};activeMilliseconds={checkpoint.ActiveMilliseconds};reservedMilliseconds={checkpoint.ReservedMilliseconds};approach={Safe(checkpoint.Checkpoint.Approach)};decision={Safe(checkpoint.Checkpoint.Decision)};nextAction={Safe(checkpoint.Checkpoint.NextAction)};evidence={string.Join(',', checkpoint.Checkpoint.Evidence.Select(Safe))}");
        }
        else if (format == "human")
        {
            Console.WriteLine($"Work {work.AuthorityRepositoryId}/{work.ChangeId}/{work.TaskId ?? "all tasks"}; accounting owner={work.OwnerTaskId ?? "change"}; revision={work.Revision}");
            RenderWorkAllowance("Change", work.Change, format);
            if (work.Task is not null) RenderWorkAllowance("Task", work.Task, format);
            Console.WriteLine($"Coverage: {work.Coverage}; recovery: {work.RecoveryState}; repeated unchanged blockers: {work.RepeatedBlockerCount}.");
            if (work.CorrectionRelationshipId is not null) Console.WriteLine($"Story relationship: {work.CorrectionRelationshipId}; correction-only history: {work.CorrectionOnly}; specific continuation available: {work.CorrectionContinuationAvailable}.");
            foreach (var round in work.Corrections)
                Console.WriteLine($"Correction {round.Number} ({round.Id}): {round.Status}; completed repositories={string.Join(", ", round.Stages.Select(stage => stage.RepositoryId))}; bounded seconds={round.MaximumSeconds?.ToString() ?? "owning allowance"}.");
            if (work.HoldReason is not null) Console.WriteLine($"Hold: {work.HoldReason}");
            Console.WriteLine($"Next action: {work.NextAction}");
            Console.WriteLine($"Allocation decision: {work.DecisionReference}; storage owner: {work.StorageOwner}; storage ceiling: {work.StorageCeilingReference}.");
            foreach (var amendment in work.TaskTimeAmendments.TakeLast(5))
                Console.WriteLine($"Task time amendment {amendment.Reference}: {amendment.TaskId}/{amendment.OwnerTaskId}, {amendment.PreviousAllocatedMilliseconds} -> {amendment.AllocatedMilliseconds} ms; authority={amendment.Actor}; decision={amendment.DecisionReference}; revision={amendment.Revision}. Reason: {amendment.Reason}");
            foreach (var execution in work.Executions.TakeLast(5))
                Console.WriteLine($"Execution {execution.ExecutionId}: {execution.Status}; active={execution.ActiveMilliseconds} ms; elapsed={execution.ElapsedMilliseconds} ms; reserved={execution.ReservedMilliseconds} ms; recovery={execution.RecoveryReason ?? "none"}.");
            if (work.Checkpoints.LastOrDefault() is { } checkpoint)
            {
                Console.WriteLine($"Checkpoint {checkpoint.LogicalAttemptId}, {checkpoint.Actor}, {checkpoint.RecordedAtUtc}:");
                Console.WriteLine($"  Outcome: {checkpoint.Checkpoint.Outcome}");
                Console.WriteLine($"  Evidence: {string.Join(", ", checkpoint.Checkpoint.Evidence)}");
                Console.WriteLine($"  Cost: {checkpoint.ActiveMilliseconds} ms active; {checkpoint.ReservedMilliseconds} ms reserved.");
                Console.WriteLine($"  Approach: {checkpoint.Checkpoint.Approach}");
                Console.WriteLine($"  Decision: {checkpoint.Checkpoint.Decision}; next: {checkpoint.Checkpoint.NextAction}");
            }
        }
    }

    private static void RenderWorkAllowance(string name, CisAgentWorkAllowance allowance, string format)
    {
        if (format == "agent")
            Console.WriteLine($"workAllowance={name};allocatedMilliseconds={allowance.AllocatedMilliseconds};consumedMilliseconds={allowance.ConsumedMilliseconds};reservedMilliseconds={allowance.ReservedMilliseconds};restorationMilliseconds={allowance.RestorationMilliseconds};remainingMilliseconds={allowance.RemainingMilliseconds};allocatedAttempts={allowance.AllocatedAttempts};usedAttempts={allowance.UsedAttempts};remainingAttempts={allowance.RemainingAttempts}");
        else
            Console.WriteLine($"{name}: allocated={allowance.AllocatedMilliseconds} ms; consumed={allowance.ConsumedMilliseconds} ms; reserved={allowance.ReservedMilliseconds} ms; restoration={allowance.RestorationMilliseconds} ms; remaining={allowance.RemainingMilliseconds} ms; logical attempts={allowance.UsedAttempts}/{allowance.AllocatedAttempts}.");
    }
}
