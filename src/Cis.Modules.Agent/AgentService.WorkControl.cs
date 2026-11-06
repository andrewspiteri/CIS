using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private const string AllocationScopeNotice = "Allocating work establishes authority-wide finite work accounting for every supported direct and story route. Sibling tasks will also need a canonical owner and allocated capacity, which may be inherited; this does not adopt engineering quality policy.";

    public CisAgentWorkResult AllocateWork(string repositoryPath, string changeId, string inputPath, string actor, string reason)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            if (!WorkRequired(context) && !RegisteredParticipantWorkRequired(context) && !_workLedger.HasCorrectionHistory(context))
                return WorkFailure(["Work allocation requires applicable adopted controls or retained story correction history. A provider profile alone does not opt a minimal repository into accounting."]);
            var request = ReadWorkInput<CisAgentWorkAllocationRequest>(context, inputPath);
            if (request.Tasks is null || request.Tasks.Count is < 1 or > 1000)
                return WorkFailure(["Supply a bounded list of canonical task allocations."]);
            var identities = request.Tasks.Select(task => WorkIdentity(context, changeId, task.TaskId, diagnostics, accountingOnly: true)).ToArray();
            if (diagnostics.Count > 0 || identities.Any(identity => identity is null)) return WorkFailure(diagnostics);
            var canonical = identities.Select(identity => identity!).ToArray();
            request = request with { Tasks = request.Tasks.Select((task, index) => task with { TaskId = canonical[index].TaskId }).ToArray() };
            if (request.Storage is { } storage) request = request with { Storage = NormalizeOwnedStoragePolicy(context, storage) };
            var allocated = CurrentWorkView(context, _workLedger.Allocate(context, changeId.ToUpperInvariant(), request, canonical, actor, reason,
                ReadRunManifests(context).Any(run => run.Work is not null)), changeId.ToUpperInvariant(), null);
            return allocated.Work is null ? allocated : allocated with
            { Work = allocated.Work with { NextAction = AllocationScopeNotice + " " + allocated.Work.NextAction } };
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }

    public CisAgentWorkResult WorkStatus(string repositoryPath, string changeId, string? taskId = null, string? relationshipId = null)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        if (!WorkId(changeId) || taskId is not null && !WorkId(taskId)) return WorkFailure(["Work identities must be canonical change/task identifiers."]);
        return relationshipId is null ? ReadCurrentWork(context, changeId.ToUpperInvariant(), taskId?.ToUpperInvariant())
            : _workLedger.ReadCorrections(context, relationshipId);
    }

    public CisAgentWorkResult RecordWorkProgress(string repositoryPath, string changeId, string taskId, string inputPath, string actor)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            var identity = WorkIdentity(context, changeId, taskId, diagnostics, accountingOnly: true);
            return identity is null || diagnostics.Count > 0 ? WorkFailure(diagnostics)
                : CurrentWorkView(context, _workLedger.Progress(context, identity, ReadWorkInput<CisAgentWorkProgressRequest>(context, inputPath), actor),
                    identity.ChangeId, identity.TaskId);
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }

    public CisAgentWorkResult DisposeWork(string repositoryPath, string changeId, string taskId, string inputPath, string actor, string reason)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return WorkFailure(diagnostics);
        try
        {
            var request = ReadWorkInput<CisAgentWorkDispositionRequest>(context, inputPath);
            if (request.Kind == "cancel-native")
                return CancelNativeWork(context, changeId, taskId, request, actor, reason);
            if (request.CorrectionRelationshipId is { } relationship && request.Kind != "reconcile-story-history"
                && _workLedger.ReadCorrections(context, relationship).Work?.CorrectionOnly == true)
                return _workLedger.DispositionCorrection(context, relationship, request, actor, reason);
            // Reconciliation concerns retained accounting, even after the canonical task
            // has moved or ceased to be eligible for a new execution.
            var identity = request.Kind == "reconcile" && request.ExecutionId is not null
                ? WorkId(changeId) && WorkId(taskId)
                    ? new CisAgentWorkIdentity(context.RepositoryId, changeId.ToUpperInvariant(), taskId.ToUpperInvariant(), [], [])
                    : throw new InvalidDataException("Reconciliation requires canonical change/task identifiers.")
                : WorkIdentity(context, changeId, taskId, diagnostics, accountingOnly: true);
            CisAgentWorkPreparationOwner? verifiedPreparationOwner = null;
            var preparationStopped = false;
            IReadOnlyList<CisAgentWorkExecution>? verifiedNativeTree = null;
            if (identity is not null && request.Kind == "reconcile" && request.ExecutionId is not null)
            {
                var execution = _workLedger.Read(context, identity.ChangeId, identity.TaskId).Work?.Executions
                    .SingleOrDefault(item => item.ExecutionId == request.ExecutionId);
                verifiedPreparationOwner = execution?.PreparationOwner;
                preparationStopped = PreparationOwnerStopped(verifiedPreparationOwner);
                if (execution is { Stage.NativeOperation: not null, Finalized: false })
                {
                    var retained = _workLedger.NativeExecution(context, request.ExecutionId);
                    if (retained is null) return WorkFailure(["The retained native execution disappeared; restore its original accounting."]);
                    verifiedNativeTree = VerifyNativeReconciliation(context, retained.Value.Reference, diagnostics);
                    if (diagnostics.Count > 0)
                        return _workLedger.Read(context, identity.ChangeId, identity.TaskId) with { Status = "held", Errors = diagnostics };
                }
            }
            if (request.Kind == "reconcile-story-history" && identity is not null && diagnostics.Count == 0)
                return ReconcileStoryHistory(context, identity, request, actor, reason, diagnostics);
            return identity is null || diagnostics.Count > 0 ? WorkFailure(diagnostics)
                : CurrentWorkView(context, _workLedger.Disposition(context, identity, request, actor, reason, verifiedPreparationOwner, preparationStopped, verifiedNativeTree),
                    identity.ChangeId, identity.TaskId);
        }
        catch (Exception error) when (WorkException(error)) { return WorkFailure([error.Message]); }
    }

    private static T ReadWorkInput<T>(CisRepositoryContext context, string inputPath)
    {
        var path = Path.GetFullPath(inputPath, context.RepositoryPath);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 128 * 1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Controller input must be a regular UTF-8 JSON file no larger than 128 KiB.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions(JsonOptions)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Controller input must contain the expected JSON object.");
    }

    private static bool WorkException(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or OverflowException;

    private static CisAgentWorkResult WorkFailure(IEnumerable<string> errors)
        => new("held", null, errors.ToArray(), false);

    private static bool WorkId(string value)
        => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,99}\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private bool WorkRequired(CisRepositoryContext context)
        => CisEngineeringPolicy.Read(context.RepositoryPath)?.RequireTaskCompletion == true
            || _workLedger.HasGovernedState(context)
            || ReadRunManifests(context).Any(run => run.Work is not null);

    private bool RegisteredParticipantWorkRequired(CisRepositoryContext authority)
    {
        var resolution = _workspaceRegistry?.Resolve(authority.RepositoryPath);
        if (resolution?.Workspace is not { } workspace) return false;
        if (workspace.AuthorityRepository is not { IsProductOwned: true } registeredAuthority
            || !registeredAuthority.RepositoryPath.Equals(authority.RepositoryPath, CisPathSafety.PlatformComparison)) return false;
        return workspace.Repositories.Where(repository => repository.IsProductOwned && repository.Role == "participant")
            .Any(repository => _resolver.Resolve(repository.RepositoryPath).Context is { } participant && WorkRequired(participant));
    }

    private sealed record StoryWorkBinding(CisRepositoryContext Authority, CisAgentWorkIdentity? Identity,
        string RelationshipId, int MaximumRunSeconds, bool Required, string Actor, IReadOnlyList<string> HistoricalRunIds,
        string? BindingReason = null)
    {
        public string ContractDigest { get; init; } = "";
        public string? ReflectionApproach { get; init; }
        public string? ScopeTitle { get; init; }
    }

    private StoryWorkBinding? ResolveStoryWorkBinding(CisStoryTaskWork work, List<string> diagnostics)
    {
        try
        {
            var context = Resolve(work.WorkspacePath, out var resolutionErrors);
            diagnostics.AddRange(resolutionErrors);
            if (context is null) return null;
            var workspace = _workspaceRegistry?.Resolve(work.WorkspacePath);
            if (workspace?.Workspace?.AuthorityRepository is { } authority)
            {
                context = Resolve(authority.RepositoryPath, out resolutionErrors);
                diagnostics.AddRange(resolutionErrors);
                if (context is null) return null;
                foreach (var repository in work.Repositories)
                    if (!workspace.Workspace.Repositories.Any(registered => registered.IsProductOwned
                        && registered.Id == repository.Id && registered.RepositoryPath.Equals(repository.RepositoryPath, CisPathSafety.PlatformComparison)))
                        diagnostics.Add("ERROR: Story accounting targets must be exact registered product-owned repositories: " + repository.Id);
            }
            else if (workspace?.Errors.Count > 0 && File.Exists(Path.Combine(work.WorkspacePath, ".cis/workspace.yml")))
                diagnostics.AddRange(workspace.Errors.Select(error => "ERROR: " + error));
            var required = WorkRequired(context) || work.Repositories.Any(repository =>
                _resolver.Resolve(repository.RepositoryPath).Context is { } participant ? WorkRequired(participant)
                    : CisEngineeringPolicy.Read(repository.RepositoryPath)?.RequireTaskCompletion == true);
            if (required && string.IsNullOrWhiteSpace(work.Task.InstanceKey))
                diagnostics.Add("ERROR: Adopted story work needs the retained stable task InstanceKey. Resolve its approved instance through story preparation before binding an allowance.");
            var relationship = work.Slug + "/" + (work.Task.InstanceKey ?? work.StoryId + "/" + work.Task.Id);
            if (string.IsNullOrWhiteSpace(relationship) || relationship.Length > 1000 || relationship.Contains('\0'))
                throw new InvalidDataException("Story continuation needs its bounded stable instance relationship.");
            if (work.CanonicalBinding is { } binding && (work.Task.ChangeId is { } declaredChange
                    && !declaredChange.Equals(binding.ChangeId, StringComparison.OrdinalIgnoreCase)
                || work.Task.WorkOwnerTaskId is { } declaredOwner && !declaredOwner.Equals(binding.WorkOwnerTaskId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The approved task and retained story binding disagree about their canonical change or work owner.");
            var changeId = work.Task.ChangeId ?? work.CanonicalBinding?.ChangeId;
            var taskId = work.Task.WorkOwnerTaskId ?? work.CanonicalBinding?.WorkOwnerTaskId;
            CisAgentWorkIdentity? identity = null;
            string? bindingReason = null;
            if (changeId is not null && taskId is not null)
            {
                var identityDiagnostics = new List<string>();
                try { identity = WorkIdentity(context, changeId, taskId, identityDiagnostics, accountingOnly: true); }
                catch (Exception error) when (!required && work.CanonicalBinding is null && WorkException(error))
                { identityDiagnostics.Add(error.Message); }
                if (required || work.CanonicalBinding is not null) diagnostics.AddRange(identityDiagnostics);
                else if (identityDiagnostics.Count > 0)
                {
                    identity = null;
                    bindingReason = "Optional declared ownership is not currently established: " + string.Join(" ", identityDiagnostics)
                        + " Use bind-work against an approved canonical task when accounting is needed; the existing story gates still apply.";
                }
            }
            else if (required || work.CanonicalBinding is not null)
                diagnostics.Add("ERROR: This story task needs one unambiguous approved canonical ChangeId and WorkOwnerTaskId. Use the existing story bind-work preparation route, then cis agent work allocate once at that owning change/task; no provider work has started. " + AllocationScopeNotice);
            else if (changeId is not null || taskId is not null)
                bindingReason = "Optional declared ownership is incomplete. Establish its exact canonical change/task through bind-work when governed accounting is needed.";
            var historical = ReadRunManifests(context).Where(run => run.Work is null && (run.StoryRelationshipId == relationship
                || run.StoryRelationshipId is null && run.ChangeId == "STORY-" + work.StoryId
                    && (run.TaskId == work.Task.Id + "-IMPLEMENT" || run.TaskId == work.Task.Id + "-REVIEW")
                || work.PreviousExecution?.Runs.Any(previous => previous.RunId == run.RunId) == true)).Select(run => run.RunId).ToArray();
            if (diagnostics.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal))) return null;
            var maximum = work.Repositories.Select(repository => _resolver.Resolve(repository.RepositoryPath).Context)
                .Where(participant => participant is not null).Select(participant => RuntimeCeiling(participant!))
                .Append(RuntimeCeiling(context)).Min();
            return new(context, identity, relationship, maximum, required, work.Actor, historical, bindingReason)
            {
                ContractDigest = CisStoryEngineeringContract.Digest(work),
                ReflectionApproach = (work.Task.Validation ?? work.Task.Description) is { Length: <= 4000 } approach ? approach : null,
                ScopeTitle = work.Task.Title is { Length: <= 4000 } title ? title : null
            };
        }
        catch (Exception error) when (WorkException(error))
        { diagnostics.Add("ERROR: Story work binding could not be established: " + error.Message); return null; }
    }

    public CisStoryWorkStatus InspectWork(CisStoryTaskWork work)
    {
        var diagnostics = new List<string>();
        var binding = ResolveStoryWorkBinding(work, diagnostics);
        if (binding is null)
            return new("held", true, false, null, null, diagnostics.FirstOrDefault() ?? "Resolve the exact story work binding before execution.", diagnostics);
        var control = binding.Required && binding.Identity is not null
            ? ReadStoryWork(binding)
            : _workLedger.ReadCorrections(binding.Authority, binding.RelationshipId, binding.ContractDigest, binding.Actor,
                expectedIdentity: binding.Identity, inspectIdentity: true);
        var corrections = _workLedger.ReadCorrections(binding.Authority, binding.RelationshipId, binding.ContractDigest, binding.Actor,
            expectedIdentity: binding.Identity, inspectIdentity: true);
        var historyBindingCurrent = corrections.Work is not { CorrectionOnly: true, TaskId: not null } history
            || binding.Identity is { } expectedBindingIdentity && history.AuthorityRepositoryId == expectedBindingIdentity.AuthorityRepositoryId
                && history.ChangeId == expectedBindingIdentity.ChangeId && history.TaskId == expectedBindingIdentity.TaskId;
        if (control.Work is not null && corrections.Work is not null)
            control = control with
            {
                Work = control.Work with
                {
                    Corrections = corrections.Work.Corrections,
                    CorrectionRelationshipId = binding.RelationshipId,
                    RequiredCorrectionFindingReferences = corrections.Work.RequiredCorrectionFindingReferences,
                    CorrectionContinuationAvailable = corrections.Work.CorrectionContinuationAvailable,
                    RequiredCorrectionContext = corrections.Work.RequiredCorrectionContext,
                    CorrectionContinuation = corrections.Work.CorrectionContinuation
                }
            };
        if (corrections.Status == "held" && corrections.Work?.HoldReason is { } correctionHold)
            control = control with
            {
                Status = "held",
                Errors = control.Errors.Append(correctionHold).Distinct(StringComparer.Ordinal).ToArray(),
                Work = control.Work is null ? corrections.Work : control.Work with
                {
                    HoldReason = string.Join(" ", new[] { control.Work.HoldReason, correctionHold }.Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct(StringComparer.Ordinal)),
                    NextAction = corrections.Work.NextAction
                }
            };
        var historicalHold = binding.Required && binding.Identity is { } currentIdentity
            && !_workLedger.HistoricalReconciled(binding.Authority, currentIdentity, binding.RelationshipId, binding.HistoricalRunIds);
        if (historicalHold)
            control = control with
            {
                Status = "held",
                Errors = control.Errors.Append(HistoricalAction(binding)).ToArray(),
                Work = control.Work is null ? null : control.Work with { NextAction = HistoricalAction(binding) }
            };
        if (control.Work is { HoldReason: AgentWorkLedger.ReflectionAction } reflectionWork)
        {
            control = control with
            {
                Errors = control.Errors.Append(StoryReflectionAction(binding)).Distinct(StringComparer.Ordinal).ToArray()
            };
            if (!historicalHold && corrections.Status != "held")
                control = control with
                {
                    Status = "controller-assessment-required",
                    Work = reflectionWork with { NextAction = StoryReflectionAction(binding) }
                };
        }
        var nextAction = binding.BindingReason ?? control.Work?.NextAction ?? (binding.Required ? "Establish the owning allocation through cis agent work allocate."
            : "Continue the supported story route; its correction count remains durable without adopting an allowance.");
        if (control.Status == "missing-allocation" || binding.Required && control.Work is { CorrectionOnly: false, Task: null }
            || control.Work is { CorrectionOnly: true, RequiredCorrectionFindingReferences.Count: > 0 } && control.Work.Corrections.Count >= 2)
            nextAction += " " + AllocationScopeNotice;
        return new(control.Status == "controller-assessment-required" ? control.Status : control.ExitCode == 0 ? "current" : "held", binding.Required, binding.Identity is not null && historyBindingCurrent,
            binding.Identity, control, nextAction, control.Errors);
    }

    private static string StoryReflectionAction(StoryWorkBinding binding)
        => "Controller assessment is due. Inspect retained implementation and failed/valid review evidence, measured consumption, reservations and remaining capacity. "
            + "The already-authorized orchestrating agent must record its Outcome, Evidence, Approach, Decision and next bounded action through cis agent work progress "
            + binding.Identity?.ChangeId + " " + binding.Identity?.TaskId
            + " --input <assessment.json> --actor <authorized-controller>, using the current revision and closeAttempt:false when continuing this logical attempt. "
            + "CIS supplies measured Cost; candidate reuse and automatic execution summaries do not supply engineering judgment. "
            + "Then resume the same governed story. No routine human acknowledgment or extra reviewer is required; independent holds remain binding.";

    private CisAgentWorkResult ReadStoryWork(StoryWorkBinding binding)
    {
        var identity = binding.Identity ?? throw new InvalidDataException("Story accounting requires its canonical owner.");
        return _workLedger.Read(binding.Authority, identity.ChangeId, identity.TaskId,
            binding.MaximumRunSeconds, new("story-task", binding.RelationshipId, "orchestration", binding.Authority.RepositoryId)
            { ContractDigest = binding.ContractDigest });
    }

    private WorkExecutionScope? AdmitStoryRoot(StoryWorkBinding binding, string executionId, List<string> diagnostics)
    {
        if (!binding.Required) return null;
        if (binding.Identity is null) { diagnostics.Add("ERROR: The story has no canonical work owner."); return null; }
        if (!_workLedger.HistoricalReconciled(binding.Authority, binding.Identity, binding.RelationshipId, binding.HistoricalRunIds))
        { diagnostics.Add("ERROR: " + HistoricalAction(binding)); return null; }
        if (!ReflectStoryBeforeAdmission(binding, diagnostics)) return null;
        var admission = _workLedger.Admit(binding.Authority, binding.Identity, executionId, binding.MaximumRunSeconds,
            binding.MaximumRunSeconds, preparationOwner: CapturePreparationOwner(), clampToRemaining: true,
            stage: new("story-task", binding.RelationshipId, "orchestration", binding.Authority.RepositoryId)
            { ContractDigest = binding.ContractDigest }, controller: binding.Actor);
        if (!admission.Admitted) { WorkDiagnostics(admission.Result, diagnostics); return null; }
        return new(this, binding.Authority, admission.Reference!, admission.TimeoutSeconds, diagnostics);
    }

    private bool ReflectStoryBeforeAdmission(StoryWorkBinding binding, List<string> diagnostics)
    {
        var current = ReadStoryWork(binding);
        if (current.ExitCode != 0) { WorkDiagnostics(current, diagnostics); return false; }
        if (current.Work?.HoldReason != AgentWorkLedger.ReflectionAction) return true;
        diagnostics.Add("ERROR: " + StoryReflectionAction(binding));
        return false;
    }

    private WorkExecutionScope? AdmitStoryStage(StoryWorkBinding binding, WorkExecutionScope? root, string executionId,
        string stage, string repositoryId, string? correctionRoundId, List<string> diagnostics)
    {
        if (!binding.Required) return null;
        if (binding.Identity is null || root is null) { diagnostics.Add("ERROR: A story stage needs its admitted canonical root."); return null; }
        var requestedSeconds = Math.Min(binding.MaximumRunSeconds, checked((int)Math.Floor(root.RemainingProviderTime().TotalSeconds)));
        if (requestedSeconds <= 0)
        { diagnostics.Add("ERROR: The admitted story root has no usable whole-second execution remainder. Retain its measured work before another bounded continuation."); return null; }
        var admission = _workLedger.Admit(binding.Authority, binding.Identity, executionId, requestedSeconds,
            binding.MaximumRunSeconds, root.Reference.ExecutionId, root.Reference.LogicalAttemptId, CapturePreparationOwner(), true,
            new("story-task", binding.RelationshipId, stage, repositoryId, correctionRoundId)
            { ContractDigest = binding.ContractDigest }, binding.Actor);
        if (!admission.Admitted) { WorkDiagnostics(admission.Result, diagnostics); return null; }
        return new(this, binding.Authority, admission.Reference!, admission.TimeoutSeconds, diagnostics);
    }

    private CisAgentWorkCorrectionRound? StartOrResumeStoryCorrection(StoryWorkBinding binding, WorkExecutionScope? root,
        IReadOnlyList<string> requiredFindingReferences, List<string> diagnostics, string? contractDigest = null,
        string? materialBasis = null, IReadOnlyList<CisStoryReviewFinding>? findingDetails = null,
        IReadOnlyList<CisAgentWorkCandidateTree>? candidateTrees = null)
    {
        var admission = _workLedger.BeginCorrection(binding.Authority, binding.Identity, root?.Reference,
            binding.RelationshipId, binding.Actor, requiredFindingReferences, contractDigest, materialBasis, findingDetails, candidateTrees);
        WorkDiagnostics(admission.Result, diagnostics);
        return admission.Round;
    }

    private bool ObserveStoryContract(StoryWorkBinding binding, WorkExecutionScope? root, string contractDigest, List<string> diagnostics)
    {
        var result = _workLedger.ObserveContract(binding.Authority, binding.Identity, root?.Reference,
            binding.RelationshipId, binding.Actor, contractDigest);
        WorkDiagnostics(result, diagnostics); return result.ExitCode == 0;
    }

    private bool RecordStoryCorrectionStage(StoryWorkBinding binding, WorkExecutionScope? root, string roundId,
        string repositoryId, string runId, string candidate, IReadOnlyList<string> evidence, List<string> diagnostics)
    {
        var result = _workLedger.RecordCorrectionStage(binding.Authority, binding.Identity, root?.Reference,
            binding.RelationshipId, roundId, binding.Actor, repositoryId, runId, candidate, evidence);
        WorkDiagnostics(result, diagnostics); return result.ExitCode == 0;
    }

    private bool CompleteStoryCorrection(StoryWorkBinding binding, WorkExecutionScope? root, string roundId, List<string> diagnostics)
    {
        var result = _workLedger.CompleteCorrection(binding.Authority, binding.Identity, root?.Reference,
            binding.RelationshipId, roundId, binding.Actor);
        WorkDiagnostics(result, diagnostics); return result.ExitCode == 0;
    }

    private bool RecordStoryExecutionSummary(StoryWorkBinding binding, WorkExecutionScope? root, string actor, string outcome,
        IReadOnlyList<string> evidence, List<string> diagnostics)
    {
        if (!binding.Required) return true;
        if (binding.Identity is null || root is null) { diagnostics.Add("ERROR: The story execution summary has lost its canonical work reservation."); return false; }
        var current = ReadStoryWork(binding);
        if (current.Work is null) { WorkDiagnostics(current, diagnostics); return false; }
        var checkpoint = new CisAgentWorkCheckpoint(outcome, evidence,
            "Automatic execution observation for the approved story contract; no controller assessment is asserted.",
            "No engineering continuation decision was made by this automatic summary.",
            "Inspect this evidence and the current work status before the next permitted stage.")
        { CloseAttempt = false };
        var recorded = _workLedger.Progress(binding.Authority, binding.Identity, new(checkpoint, current.Work.Revision), actor,
            binding.RelationshipId, binding.ContractDigest);
        WorkDiagnostics(recorded, diagnostics); return recorded.ExitCode == 0;
    }

    private static string HistoricalAction(StoryWorkBinding binding)
        => "Retained story effort is unmeasured. After establishing the owning allocation, use cis agent work disposition "
            + binding.Identity?.ChangeId + " " + binding.Identity?.TaskId
            + " with kind reconcile-story-history, correctionRelationshipId '" + binding.RelationshipId
            + "', historicalRunIds [" + string.Join(",", binding.HistoricalRunIds) + "] and the current revision. CIS conservatively charges retained bounds once; implementation evidence can then be reused without a zero-cost restart.";

    private CisAgentWorkResult ReconcileStoryHistory(CisRepositoryContext context, CisAgentWorkIdentity identity,
        CisAgentWorkDispositionRequest request, string actor, string reason, List<string> diagnostics)
    {
        if (request.CorrectionRelationshipId is null || request.HistoricalRunIds is not { Count: > 0 and <= 100 })
            return WorkFailure(["Historical story reconciliation needs its stable correctionRelationshipId and exact retained historicalRunIds."]);
        var bounds = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var runId in request.HistoricalRunIds.Distinct(StringComparer.Ordinal))
        {
            var view = ReadRun(context, runId, diagnostics);
            if (view is null || view.Manifest.Work is not null || !CisAgentRunStates.IsTerminal(view.Manifest.Status)
                || view.Manifest.ExecutionRoute != "story-task" && !view.Manifest.ChangeId.StartsWith("STORY-", StringComparison.Ordinal)
                || view.Manifest.StoryRelationshipId is { } recorded && recorded != request.CorrectionRelationshipId)
                return WorkFailure(diagnostics.Append("Reconcile only exact terminal retained story runs with their established relationship; direct or already-accounted runs cannot be repriced."));
            var ownershipErrors = new List<string>(); var accountingErrors = new List<string>();
            var retained = RetainedOwnedProcesses(context, view, ownershipErrors, accountingErrors);
            if (ownershipErrors.Count > 0 || accountingErrors.Count > 0
                || retained.Processes.Any(process => !OwnedProcessStopped(process.Id, process.Started)))
                return WorkFailure(["A retained historical story process is live or uncertain. Resolve its exact process ownership before charging and reusing it."]);
            if (!DateTimeOffset.TryParse(view.Manifest.StartedAtUtc, out var started)
                || !DateTimeOffset.TryParse(view.Manifest.CompletedAtUtc, out var completed) || completed < started || view.Manifest.TimeoutSeconds is < 30 or > 3600)
                return WorkFailure(["The retained story timing evidence is incomplete. Restore or reconcile its actual run evidence; no zero-cost usage was assumed."]);
            bounds.Add(runId, Math.Max(checked((long)view.Manifest.TimeoutSeconds * 1000), checked((long)Math.Ceiling((completed - started).TotalMilliseconds))));
        }
        if (diagnostics.Any(error => error.StartsWith("ERROR:", StringComparison.Ordinal))) return WorkFailure(diagnostics);
        return CurrentWorkView(context, _workLedger.ReconcileHistory(context, identity, request.CorrectionRelationshipId,
            bounds, request.ExpectedRevision, actor, reason), identity.ChangeId, identity.TaskId);
    }

    private CisAgentWorkIdentity? WorkIdentity(CisRepositoryContext context, string changeId, string taskId,
        List<string> diagnostics, bool accountingOnly = false, bool nativeVerification = false)
    {
        if (!WorkId(changeId) || !WorkId(taskId))
        { diagnostics.Add("ERROR: Work ownership requires safe canonical change and task identities."); return null; }
        var root = Path.Combine(context.DocumentationPath, "changes", changeId, "agent-tasks");
        if (!CisPathSafety.IsUnderRoot(context.RepositoryPath, root) || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, root))
            throw new InvalidDataException("Work ownership must remain inside its authority task directory.");
        string? Find(string id)
        {
            if (!Directory.Exists(root)) return null;
            var files = Directory.EnumerateFiles(root, "*.md").Take(1001).ToArray();
            if (files.Length > 1000) throw new InvalidDataException("Resolve a bounded task directory before allocating work.");
            var matches = files.Where(path =>
            {
                var total = 0;
                var content = ReadAuthorityArtifact(context, Relative(context.RepositoryPath, path), ref total).Content;
                var canonical = FrontMatter(content, "task_id") ?? Path.GetFileNameWithoutExtension(path);
                return canonical.Equals(id, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(path).Equals(id, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Canonical work task identity is ambiguous: " + id);
            return matches.SingleOrDefault();
        }
        var taskPath = Find(taskId);
        if (taskPath is null) { diagnostics.Add("ERROR: No canonical approved task was found for work ownership: " + taskId); return null; }
        var bytes = 0;
        var task = ReadAuthorityArtifact(context, Relative(context.RepositoryPath, taskPath), ref bytes).Content;
        ValidateTaskEligibility(context, changeId, task, diagnostics, accountingOnly: accountingOnly, nativeVerification: nativeVerification);
        var canonicalTask = (FrontMatter(task, "task_id") ?? Path.GetFileNameWithoutExtension(taskPath)).ToUpperInvariant();
        var parents = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { canonicalTask };
        var current = task;
        while (FrontMatter(current, "parent_id") is { } parent && parent is not ("none" or "None" or "-" or "null"))
        {
            if (!WorkId(parent) || !seen.Add(parent) || parents.Count >= 32) throw new InvalidDataException("Canonical task ancestry is unsafe, cyclic or over the 32-parent bound.");
            var parentPath = Find(parent) ?? throw new InvalidDataException("Canonical accounting parent task is missing: " + parent);
            bytes = 0; current = ReadAuthorityArtifact(context, Relative(context.RepositoryPath, parentPath), ref bytes).Content;
            parents.Add((FrontMatter(current, "task_id") ?? Path.GetFileNameWithoutExtension(parentPath)).ToUpperInvariant());
        }
        return new(context.RepositoryId, changeId.ToUpperInvariant(), canonicalTask, parents,
            new[] { Relative(context.RepositoryPath, taskPath), Relative(context.RepositoryPath, Path.Combine(context.DocumentationPath, "changes", changeId, "plan.md")),
                Relative(context.RepositoryPath, Path.Combine(context.DocumentationPath, "changes", changeId, "impact.md")) }
                .Concat(FrontMatterArray(task, "requirement_ids")).Distinct(StringComparer.Ordinal).ToArray());
    }

    private WorkExecutionScope? AdmitWork(CisRepositoryContext context, string changeId, string taskId,
        string executionId, int timeoutSeconds, string? parentExecutionId, string? logicalAttemptId, List<string> diagnostics,
        out CisAgentWorkView? admissionView)
    {
        admissionView = null;
        try
        {
            if (!WorkRequired(context)) return null;
            var identity = WorkIdentity(context, changeId, taskId, diagnostics);
            if (identity is null || diagnostics.Count > 0) return null;
            var admission = _workLedger.Admit(context, identity, executionId, timeoutSeconds, RuntimeCeiling(context), parentExecutionId, logicalAttemptId,
                CapturePreparationOwner());
            admissionView = admission.Result.Work;
            if (!admission.Admitted) { WorkDiagnostics(admission.Result, diagnostics); return null; }
            return new(this, context, admission.Reference!, admission.TimeoutSeconds, diagnostics);
        }
        catch (Exception error) when (WorkException(error))
        { diagnostics.Add("ERROR: Work admission could not establish authority and remaining capacity: " + error.Message); return null; }
    }

    private static int RuntimeCeiling(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "agent-provider-profile.md");
        if (!File.Exists(path)) return 3600;
        if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, path) || new FileInfo(path).Length > 128 * 1024)
            throw new InvalidDataException("The adopted provider runtime profile must be a bounded regular document.");
        var match = Regex.Match(File.ReadAllText(path), @"(?m)^\|\s*maximum-run-seconds\s*\|\s*(?<value>[^|]+)\|",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return 3600;
        if (!int.TryParse(match.Groups["value"].Value.Trim(), out var value) || value is < 30 or > 3600)
            throw new InvalidDataException("The provider profile maximum-run-seconds must be between 30 and the adopted 3600-second ceiling.");
        return value;
    }

    private static void ValidateRuntimeCeiling(CisRepositoryContext context, int seconds, List<string> diagnostics)
    {
        try { if (seconds > RuntimeCeiling(context)) diagnostics.Add("ERROR: The requested timeout exceeds the current provider-profile runtime ceiling."); }
        catch (Exception error) when (WorkException(error)) { diagnostics.Add("ERROR: Provider runtime policy could not be verified: " + error.Message); }
    }

    private static void WorkDiagnostics(CisAgentWorkResult result, List<string> diagnostics)
    {
        if (result.ExitCode == 0) return;
        diagnostics.AddRange(result.Errors.Select(error => "ERROR: Work control: " + error));
        if (result.Errors.Count == 0) diagnostics.Add("ERROR: Work control: " + (result.Work?.HoldReason ?? result.Work?.NextAction ?? "An authorized work allocation is required before this direct task can launch."));
    }

    private static CisAgentWorkPreparationOwner CapturePreparationOwner()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return new(Environment.MachineName, process.Id, process.StartTime.ToUniversalTime().ToString("O"));
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { throw new InvalidDataException("The preparing controller's lifetime ownership could not be verified; no provider may start.", error); }
    }

    private static bool PreparationOwnerStopped(CisAgentWorkPreparationOwner? owner)
        => owner is not null && owner.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            && OwnedProcessStopped(owner.ProcessId, owner.ProcessStartedAtUtc);

    private CisAgentWorkView? WorkView(CisRepositoryContext context, AgentRunManifest? manifest)
        => manifest?.Work is { } reference ? ReadCurrentWork(context, reference.ChangeId, reference.TaskId).Work : null;

    private CisAgentWorkResult ReadCurrentWork(CisRepositoryContext context, string changeId, string? taskId)
    {
        try { return _workLedger.Read(context, changeId, taskId, RuntimeCeiling(context)); }
        catch (Exception error) when (WorkException(error))
        {
            var retained = _workLedger.Read(context, changeId, taskId);
            var reason = "The current reflection window cannot be verified from the provider profile: " + error.Message;
            return retained with
            {
                Status = "held",
                Errors = retained.Errors.Append(reason).ToArray(),
                Work = retained.Work is null ? null : retained.Work with
                { ReflectionWindowMilliseconds = null, HoldReason = reason, NextAction = "Repair the provider profile before further direct execution; retained accounting remains available." }
            };
        }
    }

    private CisAgentWorkResult CurrentWorkView(CisRepositoryContext context, CisAgentWorkResult result, string changeId, string? taskId)
    {
        if (result.Work is null) return result;
        var current = ReadCurrentWork(context, changeId, taskId).Work ?? result.Work;
        if (result.Status == "held" && result.Work.HoldReason is not null)
            current = current with { HoldReason = result.Work.HoldReason, NextAction = result.Work.NextAction };
        return result with { Work = current };
    }

    private (CisAgentWorkExecution? Accounting, (int Id, string? Started)[] Processes,
        (int Id, string? Started)[] CancellationCandidates) RetainedOwnedProcesses(
        CisRepositoryContext context, AgentRunView view, List<string> ownershipDiagnostics, List<string> accountingDiagnostics)
    {
        var events = view.Events.Where(item => item.Attempt == view.Manifest.Attempt
            && (item.ProcessId is not null || item.ProcessStartedAtUtc is not null)).ToArray();
        var recorded = events.Where(item => item.ProcessId is not null)
            .Select(item => (Id: item.ProcessId!.Value, Started: item.ProcessStartedAtUtc)).ToList();
        if (view.Manifest.ProcessId is { } manifestProcess) recorded.Add((manifestProcess, view.Manifest.ProcessStartedAtUtc));
        CisAgentWorkExecution? execution = null;
        if (view.Manifest.Work is { } reference)
        {
            var accounting = _workLedger.Read(context, reference.ChangeId, reference.TaskId);
            if (accounting.ExitCode != 0) WorkDiagnostics(accounting, accountingDiagnostics);
            execution = accounting.Work?.Executions.SingleOrDefault(item => item.ExecutionId == reference.ExecutionId);
            if (execution is null) accountingDiagnostics.Add("ERROR: The recorded work reservation is unavailable; restore its accounting before settlement.");
            if (execution?.ProcessId is { } ledgerProcess) recorded.Add((ledgerProcess, execution.ProcessStartedAtUtc));
        }
        var identities = recorded.Where(item => item.Started is not null).Distinct().Take(33).ToArray();
        // A PID-only event can inherit the preceding process's timestamp in the
        // manifest. Only a complete event or verified accounting identity can fill it.
        var completeEvents = events.Where(item => item.ProcessId is not null && item.ProcessStartedAtUtc is not null).ToArray();
        var completeEventIds = completeEvents.Select(item => item.ProcessId!.Value).ToHashSet();
        var incomplete = events.Any(item => item.ProcessId is null
            || item.ProcessStartedAtUtc is null && !completeEventIds.Contains(item.ProcessId.Value) && execution?.ProcessId != item.ProcessId);
        var completeManifest = view.Manifest.ProcessId is not null && view.Manifest.ProcessStartedAtUtc is not null;
        var supportedManifest = completeManifest && (events.Length == 0
            || completeEvents.Any(item => item.ProcessId == view.Manifest.ProcessId && item.ProcessStartedAtUtc == view.Manifest.ProcessStartedAtUtc)
            || execution?.ProcessId == view.Manifest.ProcessId && execution?.ProcessStartedAtUtc == view.Manifest.ProcessStartedAtUtc);
        if (events.Length > 0 && completeManifest && !supportedManifest)
            incomplete = true;
        // Candidacy needs independently retained provenance before any OS time
        // comparison. An inherited manifest pair never gains ownership merely
        // because another process started within the timestamp tolerance.
        var supported = completeEvents.Select(item => (Id: item.ProcessId!.Value, Started: item.ProcessStartedAtUtc)).ToList();
        if (execution?.ProcessId is { } verifiedProcess) supported.Add((verifiedProcess, execution.ProcessStartedAtUtc));
        if (supportedManifest) supported.Add((view.Manifest.ProcessId!.Value, view.Manifest.ProcessStartedAtUtc));
        var candidates = supported.Where(item => item.Id > 0 && DateTimeOffset.TryParse(item.Started, out _)).Distinct().Take(33).ToArray();
        if (identities.Length > 32 || incomplete
            || recorded.Any(item => item.Started is null && !identities.Any(known => known.Id == item.Id))
            || identities.Any(item => item.Id <= 0 || !DateTimeOffset.TryParse(item.Started, out _)))
            ownershipDiagnostics.Add("ERROR: Recorded owned-process identities are incomplete or exceed the bounded recovery limit; retain reservations and perform owned-process diagnosis.");
        if (identities.Length > 32 || candidates.Length > 32) candidates = [];
        return (execution, identities, candidates);
    }

    private static void VerifyOwnedProcessesStopped(IEnumerable<(int Id, string? Started)> processes, List<string> diagnostics)
    {
        if (processes.Any(item => !OwnedProcessStopped(item.Id, item.Started)))
            diagnostics.Add("ERROR: A recorded owned process is still active or cannot be proven stopped; its reservation remains held.");
    }

    private AgentResult CompleteWorkResult(CisRepositoryContext context, AgentResult result, WorkExecutionScope? work)
    {
        work?.Dispose();
        return work is null ? result : result with { Work = ReadCurrentWork(context, work.Reference.ChangeId, work.Reference.TaskId).Work };
    }

    private static bool IsDirectWorkRun(AgentRunManifest manifest, AgentTaskEnvelope envelope)
        => manifest.ExecutionRoute == "direct-task" || manifest.Work is not null
            || manifest.ExecutionRoute is null && manifest.ChangeId != ProductAuthoringChange
                && envelope.CanonicalTaskPath.Replace('\\', '/').Contains("/changes/" + manifest.ChangeId + "/agent-tasks/", StringComparison.OrdinalIgnoreCase);

    private sealed class WorkExecutionScope : IDisposable
    {
        private readonly AgentService service;
        private readonly CisRepositoryContext context;
        private readonly List<string> diagnostics;
        private readonly DateTimeOffset started;
        private readonly int seconds;
        private bool launched, returned, uncertainIdentity, attached, settled;
        private int? processId;
        private string? processStarted;
        public CisAgentWorkReference Reference { get; }
        internal int TimeoutSeconds => seconds;

        internal WorkExecutionScope(AgentService service, CisRepositoryContext context, CisAgentWorkReference reference, int seconds, List<string> diagnostics)
        {
            this.service = service; this.context = context; Reference = reference; this.seconds = seconds; this.diagnostics = diagnostics;
            var execution = service._workLedger.Read(context, reference.ChangeId, reference.TaskId).Work?.Executions.SingleOrDefault(item => item.ExecutionId == reference.ExecutionId);
            started = execution is not null && DateTimeOffset.TryParse(execution.StartedAtUtc, out var at) ? at : service._clock();
        }

        internal TimeSpan RemainingProviderTime()
            => TimeSpan.FromTicks(Math.Max(0, TimeSpan.FromSeconds(seconds).Ticks - Math.Max(0, (service._clock() - started).Ticks)));
        internal void Launch()
        {
            var result = service._workLedger.MarkLaunchIntent(context, Reference);
            WorkDiagnostics(result, diagnostics);
            if (result.ExitCode != 0) throw new InvalidDataException("The durable launch intent could not be recorded.");
            launched = true;
        }
        internal void Process(CisAgentProviderEvent item)
        {
            if (uncertainIdentity) return;
            if (item.ProcessId is null && item.ProcessStartedAtUtc is null) return;
            if (item.ProcessId is not { } id || item.ProcessStartedAtUtc is not { } at)
            {
                uncertainIdentity = true;
                WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference,
                    "The reported process identity is incomplete; bounded reconciliation is required."), diagnostics);
                return;
            }
            if (processId == id && processStarted == at && attached) return;
            if (processId is not null && (processId != id || processStarted != at))
            {
                uncertainIdentity = true;
                WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference,
                    "The owned process identity changed during execution; bounded reconciliation is required."), diagnostics);
                return;
            }
            processId = id; processStarted = at;
            if (!ProcessIdentityMatches(id, at))
            {
                uncertainIdentity = true;
                WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference, "The reported owned process identity could not be verified."), diagnostics);
            }
            else
            {
                var result = service._workLedger.AttachProcess(context, Reference, id, at);
                attached = result.ExitCode == 0;
                // The durable launch intent keeps capacity reserved. A busy attachment
                // can retry on the next event without turning valid output into failure.
                if (result.Status != "busy") WorkDiagnostics(result, diagnostics);
            }
        }
        internal void Returned() => returned = true;
        internal void Finish(string status, string? summary, IReadOnlyList<string>? evidence)
        {
            var stopped = !uncertainIdentity && (processId is { } id
                ? OwnedProcessStopped(id, processStarted)
                : !launched || returned);
            var result = service._workLedger.Settle(context, Reference, status, stopped, summary, evidence);
            WorkDiagnostics(result, diagnostics);
            settled = result.ExitCode == 0 || result.Applied;
        }
        public void Dispose()
        {
            if (settled) return;
            if (!launched || returned || processId is { } id && OwnedProcessStopped(id, processStarted))
                Finish(launched ? "interrupted" : "preparation-ended", "Known preparation/execution cost retained after a stopped controller boundary.", []);
            else WorkDiagnostics(service._workLedger.MarkRecoveryUncertain(context, Reference,
                "Launch intent exists without controller-observed completion. Inspect the owned process before releasing capacity."), diagnostics);
        }
    }
}
