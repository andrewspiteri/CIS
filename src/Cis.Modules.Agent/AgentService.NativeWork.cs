using System.Diagnostics;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService : ICisNativeWorkController
{
    private static IReadOnlyDictionary<string, string> NativeProviderEnvironment(CisRepositoryContext authority,
        CisAgentWorkReference? reference, string actor)
    {
        var environment = AllowedEnvironment().ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        if (reference is not null)
        {
            environment[CisNativeWorkEnvironment.Authority] = authority.RepositoryPath;
            environment[CisNativeWorkEnvironment.ParentExecution] = reference.ExecutionId;
            environment[CisNativeWorkEnvironment.Change] = reference.ChangeId;
            environment[CisNativeWorkEnvironment.Task] = reference.TaskId;
            environment[CisNativeWorkEnvironment.Actor] = actor;
        }
        return environment;
    }

    private sealed record NativeWorkBinding(CisRepositoryContext Authority, CisAgentWorkIdentity? Identity,
        string Actor, string? ParentExecutionId, bool Required, CisRepositoryContext Target);

    public CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null)
    {
        var errors = new List<string>();
        try
        {
            if (NativePlainDiagnostic(repositoryPath, association))
                return new(false, "unaccounted", "Uninitialized non-adopted diagnostic; no governed accounting coverage.", null, null, [])
                { AuthorityPath = Path.GetFullPath(repositoryPath) };
            var binding = ResolveNativeBinding(repositoryPath, association, errors);
            if (binding is null) return NativeHold(errors);
            if (!binding.Required) return new(false, "unaccounted", "Non-adopted diagnostic; no governed accounting coverage.", null, null, [])
            { AuthorityPath = binding.Authority.RepositoryPath };
            if (binding.Identity is null) return NativeHold(errors);
            var control = ReadNativeControl(binding);
            return new(true, control.ExitCode == 0 && control.Work?.HoldReason is null ? "current" : "held",
                "Canonical owner and shared finite allowance.", null, control, control.Errors)
            { AuthorityPath = binding.Authority.RepositoryPath };
        }
        catch (Exception error) when (WorkException(error)) { return NativeHold([error.Message]); }
    }

    public CisNativeWorkAdmission Admit(CisNativeWorkRequest request)
    {
        var errors = new List<string>();
        try
        {
            if (request.RecoveryOfExecutionId is null && NativePlainDiagnostic(request.RepositoryPath, request.Association))
                return new(false, "unaccounted", "Uninitialized non-adopted diagnostic; no governed accounting coverage.", null, null, [])
                { AuthorityPath = Path.GetFullPath(request.RepositoryPath) };
            var binding = ResolveNativeBinding(request.RepositoryPath, request.Association, errors, request.RecoveryOfExecutionId, request.RecoveryBasis);
            if (binding is null) return NativeHold(errors);
            if (!binding.Required) return new(false, "unaccounted", "Non-adopted diagnostic; no governed accounting coverage.", null, null, [])
            { AuthorityPath = binding.Authority.RepositoryPath };
            if (binding.Identity is null) return NativeHold(errors);
            if (string.IsNullOrWhiteSpace(request.Operation) || request.Operation.Length > 200)
                return NativeHold(["Name the bounded native operation before admission."]);
            return AdmitNativeBound(binding, request);
        }
        catch (Exception error) when (WorkException(error)) { return NativeHold([error.Message]); }
    }

    public CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference reference)
    {
        try
        {
            var authority = Resolve(authorityPath, out var errors);
            if (authority is null) return NativeHold(errors);
            var stored = _workLedger.NativeExecution(authority, reference.ExecutionId);
            if (stored is null || stored.Value.Reference != reference || reference.AuthorityRepositoryId != authority.RepositoryId)
                return NativeHold(["The native receipt does not match its exact retained authority/owner/attempt/execution reference."]);
            var execution = stored.Value.Execution;
            var control = _workLedger.Read(authority, reference.ChangeId, reference.TaskId);
            var verified = execution.Stage?.NativeOperation is not null && execution.CompletedAtUtc is not null && execution.LaunchIntent
                && execution.ProcessId is not null && execution.ProcessStartedAtUtc is not null && execution.RecoveryReason is null;
            return new(true, verified ? "verified" : "held", "Read-only durable native execution provenance; this does not qualify the native result.",
                reference, control, verified ? [] : ["The native observation has not retained complete, safely settled launch/process provenance."])
            { AuthorityPath = authority.RepositoryPath };
        }
        catch (Exception error) when (WorkException(error)) { return NativeHold([error.Message]); }
    }

    public CisAgentWorkResult InspectSettlement(string authorityPath, CisAgentWorkReference reference)
    {
        try
        {
            var authority = Resolve(authorityPath, out var errors);
            if (authority is null) return new("held", null, errors, false);
            var retained = _workLedger.NativeExecution(authority, reference.ExecutionId);
            if (retained is null || retained.Value.Reference != reference || reference.AuthorityRepositoryId != authority.RepositoryId)
                return new("held", null, ["The settlement reference does not match its exact retained authority/owner/attempt."], false);
            var result = _workLedger.Read(authority, reference.ChangeId, reference.TaskId) with { Status = "unchanged", Applied = false };
            return CisNativeWorkSettlement.Complete(result, reference) && _workLedger.NativeStorageSettled(authority, reference) ? result
                : result with { Status = "held", Errors = ["The exact native execution or one of its owned children still requires settlement/reconciliation."] };
        }
        catch (Exception error) when (WorkException(error)) { return new("held", null, [error.Message], false); }
    }

    private CisNativeWorkAdmission AdmitNativeBound(NativeWorkBinding binding, CisNativeWorkRequest request, bool internalChild = false)
    {
        try
        {
            var restoration = request.RecoveryOfExecutionId is not null || binding.ParentExecutionId is { } parentId
                && _workLedger.NativeExecution(binding.Authority, parentId)?.Execution.RestorationOfExecutionId is not null;
            var stage = (restoration ? new CisAgentWorkStage("native-testing", binding.Target.RepositoryId, "native-restoration", binding.Target.RepositoryId)
            { NativeOperation = request.Operation, RepositoryPath = binding.Target.RepositoryPath } : NativeStage(binding, request.Operation))
                with
            { NativeRecoveryBasis = request.RecoveryBasis };
            var ceiling = Math.Min(RuntimeCeiling(binding.Authority), RuntimeCeiling(binding.Target));
            var admission = _workLedger.Admit(binding.Authority, binding.Identity!, request.ExecutionId,
                Math.Min(request.MaximumSeconds, ceiling), ceiling, binding.ParentExecutionId,
                preparationOwner: CapturePreparationOwner(), clampToRemaining: true, stage: stage, controller: binding.Actor,
                restorationOfExecutionId: request.RecoveryOfExecutionId, nativeChildContinuation: internalChild);
            if (!admission.Admitted)
                return new(true, "held", "Governed native work held before preparation.", null, admission.Result,
                    admission.Result.Errors.Count > 0 ? admission.Result.Errors : [admission.Result.Work?.HoldReason ?? admission.Result.Work?.NextAction ?? "Inspect the owning allocation."])
                { AuthorityPath = binding.Authority.RepositoryPath };
            return new(true, "admitted", "Canonical owner; preparation, native execution and result admission share inclusive accounting.",
                admission.Reference, admission.Result, [])
            {
                AuthorityPath = binding.Authority.RepositoryPath,
                Scope = new NativeWorkScope(this, binding, admission.Reference!, admission.TimeoutSeconds, stage)
            };
        }
        catch (Exception error) when (WorkException(error)) { return NativeHold([error.Message]); }
    }

    private static CisNativeWorkAdmission NativeHold(IReadOnlyList<string> errors)
        => new(true, "held", "Native work has not been admitted; no native process started.", null, null,
            errors.Count > 0 ? errors : ["Resolve one approved change/task and its existing finite allocation; use --change, --task and --actor, with --authority when needed."]);

    private bool NativePlainDiagnostic(string repositoryPath, CisNativeWorkOptions? association)
    {
        if (!Directory.Exists(repositoryPath) || CisNativeWorkEnvironment.RequiresController(repositoryPath, association)) return false;
        if (_resolver.Resolve(repositoryPath).Context is not null) return false;
        var workspace = _workspaceRegistry?.Resolve(repositoryPath);
        if (workspace?.Workspace is not null || workspace?.Errors.Count > 0) return false;
        // A plain subdirectory cannot hide an initialized authority above it.
        for (var directory = new DirectoryInfo(Path.GetFullPath(repositoryPath)); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, ".cis", "repository.yml"))
                || File.Exists(Path.Combine(directory.FullName, ".cis", "workspace.yml"))) return false;
        return true;
    }

    private NativeWorkBinding? ResolveNativeBinding(string repositoryPath, CisNativeWorkOptions? association, List<string> errors,
        string? recoveryOfExecutionId = null, string? recoveryBasis = null)
    {
        var inherited = CisNativeWorkEnvironment.Read();
        association ??= new();
        if (inherited.ParentExecutionId is not null && association.ParentExecutionId is not null
            && inherited.ParentExecutionId != association.ParentExecutionId)
        { errors.Add("An inherited governed parent cannot be replaced by a different caller-selected execution."); return null; }
        if (inherited.AuthorityPath is not null && association.AuthorityPath is not null
            && !Path.GetFullPath(inherited.AuthorityPath).Equals(Path.GetFullPath(association.AuthorityPath), CisPathSafety.PlatformComparison))
        { errors.Add("The explicit authority differs from the inherited governed execution."); return null; }
        var target = Resolve(repositoryPath, out var diagnostics);
        errors.AddRange(diagnostics);
        if (target is null) return null;
        var registration = _workspaceRegistry?.Resolve(target.RepositoryPath);
        var authorityPath = association.AuthorityPath ?? inherited.AuthorityPath ?? registration?.Workspace?.AuthorityRepository?.RepositoryPath ?? target.RepositoryPath;
        var authority = Resolve(authorityPath, out diagnostics);
        errors.AddRange(diagnostics);
        if (authority is null) return null;
        var recovery = recoveryOfExecutionId is null ? null : _workLedger.NativeExecution(authority, recoveryOfExecutionId);
        if (recoveryOfExecutionId is not null && (recovery is null || !recovery.Value.Execution.NativeRestorationRequired
            || recovery.Value.Execution.Stage?.NativeRecoveryBasis != recoveryBasis
            || !string.Equals(recovery.Value.Execution.Stage?.RepositoryPath, target.RepositoryPath, CisPathSafety.PlatformComparison)))
        { errors.Add("Restoration must retain its original native execution, repository and exact recovery recipe; inspect cis agent work status for the required execution ID."); return null; }
        var parentId = association.ParentExecutionId ?? inherited.ParentExecutionId;
        var required = WorkRequired(authority) || WorkRequired(target) || RegisteredParticipantWorkRequired(authority)
            || parentId is not null || association.ChangeId is not null || association.TaskId is not null || recovery is not null;
        if (!required) return new(authority, null, association.Actor ?? inherited.Actor ?? "", null, false, target);
        var parent = parentId is null ? null : _workLedger.NativeExecution(authority, parentId);
        if (parentId is not null && (parent is null || !NativeParentOwned(parent.Value.Execution)))
        { errors.Add("The supplied parent is not a live, verified ancestor/controller of this native invocation. Inspect its durable ownership; an execution ID is not authority."); return null; }
        var registered = registration?.Workspace?.Repositories.Any(repository => repository.IsProductOwned
            && repository.Id == target.RepositoryId && repository.RepositoryPath.Equals(target.RepositoryPath, CisPathSafety.PlatformComparison)) == true;
        var isolated = parent is not null && NativeIsolatedTarget(authority, target, parent.Value.Reference, parent.Value.Execution)
            || recovery is not null && NativeIsolatedTarget(authority, target, recovery.Value.Reference, recovery.Value.Execution);
        if (!authority.RepositoryPath.Equals(target.RepositoryPath, CisPathSafety.PlatformComparison) && !registered && !isolated)
        { errors.Add("Native work must target its authority or an exact registered owned repository/verified owned execution workspace."); return null; }
        var changeId = association.ChangeId ?? inherited.ChangeId ?? parent?.Reference.ChangeId ?? recovery?.Reference.ChangeId;
        var taskId = association.TaskId ?? inherited.TaskId ?? parent?.Reference.TaskId ?? recovery?.Reference.TaskId;
        if (changeId is null && taskId is null)
        {
            var owners = _workLedger.NativeOwners(authority);
            if (owners.Count == 1) { changeId = owners[0].ChangeId; taskId = owners[0].TaskId; }
        }
        var actor = association.Actor ?? inherited.Actor;
        if (changeId is null || taskId is null || string.IsNullOrWhiteSpace(actor))
        { errors.Add("Native work needs one unambiguous approved owner and controller. Supply --change <id> --task <id> --actor <controller>, then resolve the existing allocation through cis agent work status/allocate; a profile timeout is not an allocation."); return null; }
        if (recovery is not null && (changeId != recovery.Value.Reference.ChangeId || taskId != recovery.Value.Reference.TaskId))
        { errors.Add("Native recovery cannot transfer to a different change or task."); return null; }
        var identity = recovery is { } original
            ? new CisAgentWorkIdentity(authority.RepositoryId, original.Reference.ChangeId, original.Reference.TaskId,
                original.Reference.OwnerTaskId == original.Reference.TaskId ? [] : [original.Reference.OwnerTaskId],
                _workLedger.Read(authority, original.Reference.ChangeId, original.Reference.TaskId).Work?.AcceptedOutcomeReferences ?? [])
            : WorkIdentity(authority, changeId, taskId, errors, accountingOnly: true, nativeVerification: true);
        if (identity is null || errors.Count > 0) return null;
        if (parent is not null && (identity.ChangeId != parent.Value.Reference.ChangeId
            || identity.TaskId != parent.Value.Reference.TaskId && !identity.AncestorTaskIds.Contains(parent.Value.Reference.TaskId, StringComparer.Ordinal)))
        { errors.Add("The native association does not inherit the parent's canonical change/task owner."); return null; }
        return new(authority, identity, actor, parentId, true, target);
    }

    private bool NativeIsolatedTarget(CisRepositoryContext authority, CisRepositoryContext target,
        CisAgentWorkReference reference, CisAgentWorkExecution execution)
        => execution.Stage?.RepositoryPath is { } ownedPath
            && ownedPath.Equals(target.RepositoryPath, CisPathSafety.PlatformComparison)
            && execution.Stage.RepositoryId == target.RepositoryId
            || ReadRunManifests(authority).Any(run => run.Work?.ExecutionId == reference.ExecutionId
                && run.TargetRepositoryId == target.RepositoryId
                && Path.GetFullPath(run.WorkingDirectory).Equals(target.RepositoryPath, CisPathSafety.PlatformComparison));

    private static bool NativeParentOwned(CisAgentWorkExecution execution)
    {
        if (execution.Finalized || execution.CompletedAtUtc is not null || execution.RecoveryReason is not null) return false;
        if (execution.PreparationOwner is { } controller && controller.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            && controller.ProcessId == Environment.ProcessId && ProcessIdentityMatches(controller.ProcessId, controller.ProcessStartedAtUtc)) return true;
        return execution.PreparationOwner?.HostId.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) == true
            && execution.ProcessId is { } processId && execution.ProcessStartedAtUtc is { } started
            && ProcessIdentityMatches(processId, started)
            && CisProcessSafety.IsSameOrDescendant(Environment.ProcessId, processId)
            && ProcessIdentityMatches(processId, started);
    }

    private CisAgentWorkStage NativeStage(NativeWorkBinding binding, string operation)
    {
        var current = ReadNativeControl(binding).Work;
        var rounds = current?.Corrections.Where(round => round.Status == "started").ToArray() ?? [];
        if (rounds.Length > 1) throw new InvalidDataException("More than one retained correction context applies; resume through its owning controller.");
        var round = rounds.SingleOrDefault();
        return new(round is null ? "native-testing" : "story-task", round?.RelationshipId ?? binding.Target.RepositoryId,
            "native-verification", binding.Target.RepositoryId, round?.Id)
        {
            NativeOperation = operation,
            RepositoryPath = binding.Target.RepositoryPath,
            ContractDigest = round?.Context?.ContractDigest
        };
    }

    private CisAgentWorkResult ReadNativeControl(NativeWorkBinding binding)
        => _workLedger.Read(binding.Authority, binding.Identity!.ChangeId, binding.Identity.TaskId, RuntimeCeiling(binding.Authority));

    private sealed class NativeWorkScope(AgentService service, NativeWorkBinding binding,
        CisAgentWorkReference reference, int seconds, CisAgentWorkStage stage) : ICisNativeWorkScope
    {
        private readonly DateTimeOffset started = service._workLedger.NativeExecution(binding.Authority, reference.ExecutionId) is { } retained
            && DateTimeOffset.TryParse(retained.Execution.StartedAtUtc, out var recorded) ? recorded
                : throw new InvalidDataException("The admitted native start time could not be recovered; retain its reservation.");
        private bool launched, safelyReturned, uncertain, settled, finishAttempted;
        private int? processId;
        private string? processStarted;
        public string AuthorityPath => binding.Authority.RepositoryPath;
        public string Actor => binding.Actor;
        public CisAgentWorkReference Reference => reference;
        public int MaximumSeconds => seconds;
        public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0, TimeSpan.FromSeconds(seconds).Ticks
            - Math.Max(0, (service._clock() - started).Ticks)));
        public CisNativeWorkAdmission AdmitChild(string executionId, string operation, int maximumSeconds)
            => service.AdmitNativeBound(binding with { ParentExecutionId = reference.ExecutionId },
                new(binding.Target.RepositoryPath, executionId, operation, Math.Min(maximumSeconds, (int)Math.Floor(Remaining.TotalSeconds)))
                { RecoveryBasis = stage.NativeRecoveryBasis }, internalChild: true);
        public void Launch()
        {
            var result = service._workLedger.MarkLaunchIntent(binding.Authority, reference);
            if (result.ExitCode != 0) throw new InvalidDataException(result.Work?.HoldReason ?? "Native launch intent could not be retained.");
            launched = true;
        }
        public void Started(int id, string at, CisNativeWorkContainment? containment = null)
        {
            if (processId is not null && (processId != id || processStarted != at) || !ProcessIdentityMatches(id, at))
            { MarkUncertain("The native driver's exact owned process identity could not be verified."); throw new InvalidDataException("Native process ownership is uncertain."); }
            if (containment is not null)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    if (!OperatingSystem.IsWindows() || !CisProcessJob.IsNamedNativeJobMember(containment, process))
                        throw new InvalidDataException("The actual native driver is not inside the declared non-breakaway kill-on-close job.");
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                { throw new InvalidDataException("Native Windows job assignment could not be verified before target launch.", error); }
            }
            processId = id; processStarted = at;
            var result = service._workLedger.AttachProcess(binding.Authority, reference, id, at, containment);
            if (result.ExitCode != 0) { MarkUncertain("Native launch intent exists but durable process attachment failed."); throw new InvalidDataException("Native process attachment failed."); }
        }
        public void Returned(bool safelyStopped) => safelyReturned = safelyStopped;
        public void MarkUncertain(string reason)
        {
            uncertain = true;
            service._workLedger.MarkRecoveryUncertain(binding.Authority, reference, reason);
        }
        public CisAgentWorkResult Finish(string status, string? summary, IReadOnlyList<string>? evidence)
        {
            finishAttempted = true;
            var stopped = !uncertain && (!launched || safelyReturned && (processId is null || OwnedProcessStopped(processId.Value, processStarted)));
            var result = service._workLedger.Settle(binding.Authority, reference, status, stopped, summary, evidence);
            settled = CisNativeWorkSettlement.Complete(result, reference);
            return result;
        }
        public void Dispose()
        {
            if (settled || finishAttempted) return;
            if (!launched || safelyReturned) Finish(launched ? "interrupted" : "preparation-ended", "Native cost retained at the stopped controller boundary.", []);
            else MarkUncertain("Native launch intent has no complete driver/child cleanup proof; reconcile its owned execution before releasing capacity.");
        }
    }
}
