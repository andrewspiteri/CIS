using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    private static void ValidateCorrectnessPilot(string repository, MutationRunProfile profile)
    {
        if (profile.CorrectnessPilotProfile is { } reference) Resolve(repository, reference);
        if (profile.CorrectnessPilot is not { } pilot) return;
        Require(profile.CorrectnessPilotProfile is null, "A representative profile cannot delegate its own correctness controls.");
        Require(profile.SourceFiles.Length is >= 2 and <= 4 && profile.CanaryCommand is { Length: > 0 },
            "A correctness pilot requires two through four fixture source files and its separate narrow canary command.");
        Require(profile.SourceFiles.Any(file => string.Equals(Resolve(repository, file), Resolve(repository, pilot.ControlSourceFile), CisPathSafety.PlatformComparison)),
            "The deliberately surviving control must be a declared fixture source file.");
        ValidateCommand(pilot.BaselineCommand); ValidateCommand(pilot.InfrastructureCommand);
        ValidateCommand(pilot.RestorationCommand, requireOutput: false);
        Require(pilot.UnitResultFormat is "trx" or "junit" && pilot.RecoveryTimeoutSeconds is >= 1 and <= 600,
            "Pilot unit format or recovery timeout is invalid.");
        Resolve(repository, pilot.BaselineReportPath);
        var working = Resolve(repository, profile.WorkingDirectory, true);
        var missing = Resolve(working, pilot.MissingInfrastructurePath);
        Require(!File.Exists(missing) && !Directory.Exists(missing)
            && pilot.InfrastructureCommand.Contains(pilot.MissingInfrastructurePath, StringComparer.Ordinal),
            "Infrastructure control must invoke its declared missing native configuration path, which must remain absent.");
        static int ConfigurationSelector(string[] command)
        {
            var selected = -1;
            for (var index = 0; index < command.Length; index++)
                if (command[index] is "--config-file" or "-f")
                {
                    Require(selected < 0 && index + 1 < command.Length,
                        "Pilot mutation and infrastructure controls need one explicit native configuration selector each.");
                    selected = ++index;
                }
            Require(selected >= 0, "Pilot mutation and infrastructure controls need an explicit --config-file or -f.");
            return selected;
        }
        var mutationConfig = ConfigurationSelector(profile.Command);
        var infrastructureConfig = ConfigurationSelector(pilot.InfrastructureCommand);
        Require(pilot.InfrastructureCommand[infrastructureConfig] == pilot.MissingInfrastructurePath,
            "The infrastructure control must select its exact absent native configuration, not merely pass it as an unrelated argument.");
        var selectedConfig = NativePilotPath(repository, profile.WorkingDirectory, profile.Command[mutationConfig]);
        Require(File.Exists(selectedConfig) && profile.ConfigFiles.Contains(Path.GetRelativePath(repository, selectedConfig).Replace('\\', '/'), StringComparer.Ordinal),
            "Pin the existing native mutation configuration exercised by the representative controls.");
        Require(profile.Command.Select((value, index) => index == mutationConfig ? "<native-config>" : value)
            .SequenceEqual(pilot.InfrastructureCommand.Select((value, index) => index == infrastructureConfig ? "<native-config>" : value), StringComparer.Ordinal),
            "The infrastructure control must exercise the same actual native mutation runner and arguments, changing only its configuration selector to the absent path.");
        Require(pilot.ToolFiles is { Length: > 0 and <= 128 }, "Pilot requires a bounded native tool inventory.");
        foreach (var tool in pilot.ToolFiles)
            Require(Path.IsPathFullyQualified(tool) && File.Exists(tool)
                && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(tool)!, tool), "Missing or linked pilot tool input: " + tool);
        Require(pilot.ChildTestName is null or { Length: > 0 and <= 512 }, "Pilot child test identity is invalid.");
    }

    private static string PilotBasis(string repository, MutationRunProfile profile, string path)
        => Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy = "cis-native-correctness-pilot-v1",
            fixture = Identity(repository, profile, path).Digest,
            tools = profile.CorrectnessPilot!.ToolFiles.Distinct(CisPathSafety.PlatformComparison == StringComparison.OrdinalIgnoreCase
                    ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).Select(tool => new { path = Path.GetFullPath(tool), sha256 = FileHash(tool) }),
            testing = FileHash(typeof(MutationRunService).Assembly.Location),
            helper = FileHash(typeof(CisProcessJob).Assembly.Location),
            platform = RuntimeInformation.RuntimeIdentifier,
            environment = IncrementalTestService.PilotEnvironmentIdentity(profile.Environment),
        }, Json));

    private static string PilotSummaryPath(string repository, MutationRunProfile profile)
        => Resolve(repository, LocalRoot + "/" + profile.Id + "/representative-pilot.json");

    private static string PilotRecoveryBasis(string repository, MutationRunProfile profile)
        => "native-pilot-recovery-v2:" + Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            profile.WorkingDirectory,
            profile.CorrectnessPilot!.BaselineCommand,
            profile.CorrectnessPilot.BaselineReportPath,
            profile.CorrectnessPilot.UnitResultFormat,
            profile.CorrectnessPilot.RestorationCommand,
            profile.CorrectnessPilot.RecoveryTimeoutSeconds,
            tools = profile.CorrectnessPilot.ToolFiles.Order(StringComparer.Ordinal).Select(path => new { path, sha256 = FileHash(path) }),
            configuration = profile.ConfigFiles.Order(StringComparer.Ordinal).Select(path => new { path, sha256 = FileHash(Resolve(repository, path)) }),
            environment = profile.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            platform = RuntimeInformation.RuntimeIdentifier,
        }, Json));

    internal static NativePilotWorkContext? CorrectnessPilotWorkContext(string repository,
        MutationRunProfile profile, string profilePath, string? requestedRecoveryOfExecutionId = null)
    {
        if (profile.CorrectnessPilot is null) return null;
        var basis = PilotRecoveryBasis(repository, profile);
        var path = PilotSummaryPath(repository, profile);
        if (File.Exists(path))
        {
            Require(new FileInfo(path).Length is > 0 and <= 1048576, "Pilot recovery record exceeds its bound.");
            var retained = JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(File.ReadAllBytes(path), Json)
                ?? throw new InvalidDataException("Pilot recovery record is empty.");
            var originalExecutionId = retained.RecoveryOfExecutionId ?? retained.WorkReference?.ExecutionId;
            if (requestedRecoveryOfExecutionId is not null)
                Require(requestedRecoveryOfExecutionId == originalExecutionId,
                    "The requested pilot recovery does not match the retained original execution; recover its protected context before selecting a different recipe.");
            if (retained.RecoveryRequired || !retained.Restored && retained.WorkReference is not null
                || requestedRecoveryOfExecutionId is not null)
            {
                Require(retained.RecoveryBasis is { Length: > 0 }, "Pilot recovery lacks its original pinned recipe.");
                Require(retained.RecoveryBasis == basis,
                    "The original pilot recovery recipe changed or uses a legacy opaque environment basis; retain its hold and original execution context rather than inventing a replacement recipe.");
                var reference = retained.WorkReference;
                return new(retained.RecoveryBasis, retained.RecoveryOfExecutionId ?? reference?.ExecutionId,
                    reference is null ? null : new(reference.ChangeId, reference.TaskId, AuthorityPath: retained.AuthorityPath))
                { RestorationRequired = true };
            }
        }
        return new(basis, null, null);
    }

    /// <summary>Rechecks retained native facts without executing commands or trusting a saved readiness flag.</summary>
    public static NativeCorrectnessPilotResult InspectCorrectnessPilot(string repository, string? referencePath,
        CisNativeWorkAdmission? admission = null, bool requireControllerDecision = true,
        ICisNativeWorkController? workController = null)
    {
        var recoveryPending = false;
        NativeCorrectnessPilotResult? retainedSnapshot = null;
        var nativeVerified = false;
        var settlementPending = false;
        try
        {
            Require(referencePath is not null, "Configure correctnessPilotProfile with the existing small native representative profile before broadening this governed runner path.");
            var input = ReadProfile(repository, referencePath, "canary");
            Require(input.Profile.CorrectnessPilot is not null, "The referenced profile does not declare native correctness controls.");
            var basis = PilotBasis(repository, input.Profile, input.Path);
            var summary = PilotSummaryPath(repository, input.Profile);
            Require(File.Exists(summary) && new FileInfo(summary).Length is > 0 and <= 1048576,
                "The representative native pilot has not completed. Run cis test mutation run --kind canary --profile " + referencePath + ".");
            var saved = JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(File.ReadAllBytes(summary), Json)
                ?? throw new InvalidDataException("The representative native pilot record is empty.");
            retainedSnapshot = saved;
            recoveryPending = saved.RecoveryRequired;
            Require(saved.SchemaVersion == 1 && saved.Policy == "cis-native-correctness-pilot-v1" && saved.BasisSha256 == basis,
                "The runner, adapter, fixture, native tools, configuration or interpretation basis changed; repeat its small native correctness pilot.");
            Require(!saved.RecoveryRequired, "Pilot baseline restoration remains unresolved; resume its canary route after reconciling owned processes.");
            Require((saved.Status is "ready" or "settlement-required") && (saved.NativeReady ?? saved.Ready),
                "The native pilot has no complete native candidate; inspect its original failure before expansion.");
            VerifyPilotObservations(repository, input.Profile, saved);
            nativeVerified = true;
            if (admission?.Required == true)
            {
                Require(workController is not null && saved.AuthorityPath is not null && saved.WorkReference is not null
                    && saved.WorkReference.AuthorityRepositoryId == admission.Reference?.AuthorityRepositoryId,
                    "Unaccounted pilot observations cannot supply governed execution coverage. Run the bounded pilot under its existing canonical work authority.");
                foreach (var observation in saved.Observations)
                {
                    Require(observation.Execution.WorkReference is not null, "Pilot native execution lacks its durable work association.");
                    var retained = workController.InspectExecution(saved.AuthorityPath, observation.Execution.WorkReference);
                    var execution = retained.Reference == observation.Execution.WorkReference
                        ? retained.Control?.Work?.Executions.SingleOrDefault(item => item.ExecutionId == observation.Execution.WorkReference.ExecutionId)
                        : null;
                    settlementPending = execution is { Stage.NativeOperation: not null, LaunchIntent: true, ProcessId: > 0, ProcessStartedAtUtc: not null }
                        && (execution.CompletedAtUtc is null || execution.RecoveryReason is not null);
                    Require(retained.Errors.Count == 0 && retained.Status == "verified" && retained.Reference == observation.Execution.WorkReference
                        && retained.Control?.Work?.Executions.Any(execution => execution.ExecutionId == observation.Execution.WorkReference.ExecutionId
                            && execution.CompletedAtUtc is not null && execution.LaunchIntent && execution.ProcessId > 0 && execution.RecoveryReason is null) == true,
                        "Pilot native launch is missing, live or unresolved in its authoritative work ledger.");
                }
            }
            if (saved.WorkReference is not null || saved.AuthorityPath is not null || saved.RecoveryOfExecutionId is not null
                || saved.Observations.Any(item => item.Execution.WorkReference is not null
                    || item.Execution.SettlementReference is not null || item.Execution.WorkAuthorityPath is not null)
                || admission?.Required == true || CisNativeWorkEnvironment.RequiresController(repository, null))
            {
                Require(saved.WorkReference is not null && workController is not null && saved.AuthorityPath is not null,
                    "Read the exact retained pilot settlement through its composed work authority; native observations do not settle accounting.");
                foreach (var observation in saved.Observations.Where(item => item.Execution.SettlementErrors.Count != 0))
                    Require(observation.Execution.WorkReference is not null,
                        "The retained child settlement has no exact work reference; restore its original provenance before reuse.");
                settlementPending = true;
                var pilotWork = saved.WorkReference!;
                var settlement = workController.InspectSettlement(saved.AuthorityPath, pilotWork);
                Require(CisNativeWorkSettlement.Complete(settlement, pilotWork),
                    "The pilot orchestration root, its native children or owned storage still requires settlement. Reconcile the exact retained execution before reusing these native observations.");
                foreach (var observation in saved.Observations.Where(item => item.Execution.SettlementErrors.Count != 0))
                {
                    var childWork = observation.Execution.WorkReference!;
                    var childSettlement = workController.InspectSettlement(saved.AuthorityPath, childWork);
                    Require(CisNativeWorkSettlement.Complete(childSettlement, childWork),
                        "A retained pilot child still requires accounting or storage settlement; reconcile it without rerunning native controls.");
                }
            }
            var reference = "native-pilot:" + basis;
            var assessed = admission?.Control?.Work?.Checkpoints.Any(record => record.Origin == "controller-progress"
                && record.Checkpoint.Evidence.Contains(reference, StringComparer.Ordinal)
                && DateTimeOffset.TryParse(record.RecordedAtUtc, out var assessedAt) && assessedAt >= saved.FinishedUtc) == true;
            var action = "Retain the native observations and use the current bounded runner path; this pilot does not qualify production scope.";
            if (requireControllerDecision && admission?.Required == true && !assessed)
                action = "The authorized controller must assess the first representative outcome and approach through cis agent work progress "
                    + admission.Reference?.ChangeId + " " + admission.Reference?.TaskId
                    + " --input <assessment.json> --actor <controller>, using the current revision and evidence '" + reference
                    + "', then resume. No routine human acknowledgment or new reviewer is required.";
            return saved with
            {
                Status = requireControllerDecision && admission?.Required == true && !assessed
                ? "controller-assessment-required" : "ready",
                Ready = true,
                NativeReady = true,
                ControllerAssessed = assessed,
                NextAction = action
            };
        }
        catch (Exception error) when (Expected(error) || error is System.Xml.XmlException)
        {
            if (retainedSnapshot is not null)
                return retainedSnapshot with
                {
                    Status = nativeVerified && settlementPending ? "settlement-required" : "pilot-required",
                    Ready = false,
                    NativeReady = nativeVerified && settlementPending,
                    NextAction = error.Message,
                    Diagnostics = retainedSnapshot.Diagnostics.Append(error.Message).Distinct(StringComparer.Ordinal).ToArray()
                };
            return new("pilot-required", false, null, error.Message)
            { ProfilePath = referencePath, RecoveryRequired = recoveryPending, Diagnostics = [error.Message] };
        }
    }

    internal static NativeCorrectnessPilotResult RunCorrectnessPilot(string repository, MutationRunProfile profile,
        string profilePath, ICisNativeWorkScope? scope = null, CancellationToken cancellationToken = default,
        ICisOwnedStorageController? ownedStorage = null, ICisNativeWorkController? workController = null,
        bool restorationOnly = false, string? recoveryOfExecutionId = null)
    {
        var relativeProfile = Path.GetRelativePath(repository, profilePath).Replace('\\', '/');
        var work = scope is null ? null : new CisNativeWorkAdmission(true, "admitted", "governed-native", scope.Reference, null, []);
        var reusable = InspectCorrectnessPilot(repository, relativeProfile, work, false, workController);
        if (reusable.Ready && !restorationOnly) return reusable with { Status = "reused" };
        if (reusable is { NativeReady: true, Ready: false, Status: "settlement-required" } && !restorationOnly) return reusable;
        var pilot = profile.CorrectnessPilot ?? throw new InvalidDataException("The selected profile lacks correctness controls.");
        var watch = Stopwatch.StartNew();
        var started = DateTimeOffset.UtcNow;
        var basis = PilotBasis(repository, profile, profilePath);
        NativeCorrectnessPilotResult? retainedSnapshot = null;
        var summaryPath = PilotSummaryPath(repository, profile);
        if (File.Exists(summaryPath) && new FileInfo(summaryPath).Length is > 0 and <= 1048576)
            retainedSnapshot = JsonSerializer.Deserialize<NativeCorrectnessPilotResult>(File.ReadAllBytes(summaryPath), Json);
        var supersededRunSummaries = (retainedSnapshot?.Observations ?? []).Select(item => item.OutputPath)
            .Where(path => path is not null).Select(path => Path.GetDirectoryName(Resolve(repository, path!))!)
            .Distinct(CisPathSafety.PlatformComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        foreach (var directory in supersededRunSummaries)
        {
            var runs = Resolve(repository, LocalRoot + "/" + profile.Id + "/runs");
            Require(string.Equals(Path.GetDirectoryName(directory), runs, CisPathSafety.PlatformComparison)
                && Path.GetFileName(directory).StartsWith("pilot-", StringComparison.Ordinal),
                "The retained pilot output does not identify its exact owned run directory.");
        }
        var recoveryBasis = PilotRecoveryBasis(repository, profile);
        var retainedRestorationRequired = retainedSnapshot is { Restored: false, WorkReference: not null }
            || retainedSnapshot?.RecoveryRequired == true;
        if ((restorationOnly || retainedRestorationRequired) && retainedSnapshot is not null
            && retainedSnapshot.RecoveryBasis != recoveryBasis)
        {
            return new("recovery-required", false, retainedSnapshot?.BasisSha256,
                "Restore the original pilot recovery recipe before retrying its unresolved restoration; profile changes or missing records cannot reset that hold.")
            {
                RecoveryRequired = true,
                WorkReference = retainedSnapshot?.WorkReference,
                AuthorityPath = retainedSnapshot?.AuthorityPath,
                RecoveryBasis = retainedSnapshot?.RecoveryBasis,
                ProfilePath = relativeProfile
            };
        }
        var root = Resolve(repository, LocalRoot + "/" + profile.Id);
        var storage = Resolve(repository, LocalRoot);
        var run = Resolve(root, "runs/pilot-" + Guid.NewGuid().ToString("N"));
        var observations = new List<NativePilotObservation>();
        var diagnostics = new List<string>();
        // This maintained route is entered after its native canary, so baseline
        // health remains an obligation even when the next phase cannot launch.
        var recoveryRequired = true;
        var restorationPassed = false;
        var ready = false;
        var nativeReady = false;
        var pendingSettlement = false;
        long executeWall = 0;
        var preparation = watch.ElapsedMilliseconds;
        NativeCorrectnessPilotResult Snapshot(string status) => new(status, ready, basis,
            ready ? "Inspect the native pilot and record the authorized approach decision with evidence 'native-pilot:" + basis + "' before expansion."
                : nativeReady ? "Retain the complete native controls and restored baseline. Reconcile the exact retained work/storage settlement, then inspect readiness without rerunning native commands."
                : restorationPassed && !recoveryRequired ? "The native baseline is restored. Inspect missing or failed correctness observations and run the bounded canary before broadening."
                : "Inspect the retained failure and resume this bounded canary only after its prerequisite or approach changes.")
        {
            ProfilePath = relativeProfile,
            SummaryPath = Path.GetRelativePath(repository, PilotSummaryPath(repository, profile)).Replace('\\', '/'),
            WorkReference = scope?.Reference,
            AuthorityPath = scope?.AuthorityPath,
            Restored = restorationPassed,
            NativeReady = nativeReady,
            RecoveryBasis = recoveryBasis,
            RecoveryOfExecutionId = restorationOnly ? recoveryOfExecutionId ?? retainedSnapshot?.RecoveryOfExecutionId ?? retainedSnapshot?.WorkReference?.ExecutionId : null,
            RecoveryRequired = recoveryRequired,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            PreparationMilliseconds = preparation + Math.Max(0, executeWall - observations.Sum(item => item.Execution.DurationMilliseconds)),
            NativeMilliseconds = observations.Sum(item => item.Execution.DurationMilliseconds),
            ResultAdmissionMilliseconds = Math.Max(0, watch.ElapsedMilliseconds - preparation - executeWall),
            Observations = observations.ToArray(),
            Diagnostics = diagnostics.ToArray(),
        };
        string FinalStatus() => ready ? "ready" : nativeReady ? "settlement-required" : recoveryRequired ? "recovery-required" : "failed";
        void Save(string status)
        {
            var snapshot = Snapshot(status);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json).LongLength;
            CisOwnedStorageAdmission? reservation = null;
            if (scope is not null && ownedStorage is not null)
            {
                reservation = ownedStorage.Reserve(scope.AuthorityPath, scope.Reference, "pilot-admission-" + Guid.NewGuid().ToString("N"), bytes * 3,
                    [new(PilotSummaryPath(repository, profile), Required: true) { MutatesExisting = true },
                        new(Path.Combine(run, "summary.json")) { MutatesExisting = true }]);
                Require(reservation.Allowed, string.Join("; ", reservation.Result.Errors));
            }
            try
            {
                Directory.CreateDirectory(root); Directory.CreateDirectory(run);
                EnsureBudget(run, storage, profile, bytes * 3, bytes);
                AtomicJson(PilotSummaryPath(repository, profile), snapshot);
                AtomicJson(Path.Combine(run, "summary.json"), snapshot);
            }
            finally
            {
                if (reservation?.ReservationId is { } id)
                {
                    var settled = ownedStorage!.Settle(scope!.AuthorityPath, scope.Reference, id, true,
                        [PilotSummaryPath(repository, profile)],
                        !restorationOnly && retainedSnapshot?.Restored == true && restorationPassed && !recoveryRequired
                            ? supersededRunSummaries.Select(directory => Path.Combine(directory, "summary.json")).ToArray() : null);
                    Require(settled.Allowed, string.Join("; ", settled.Errors));
                }
            }
        }
        NativePilotObservation Phase(string role, string[] command, int maximumSeconds, string? reportPath = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Resolve(run, role);
            var seconds = scope is null ? maximumSeconds : Math.Min(maximumSeconds, (int)Math.Floor(scope.Remaining.TotalSeconds));
            Require(seconds >= 1, "The admitted pilot has no usable time remaining for " + role + ".");
            ICisNativeWorkScope? child = null;
            if (scope is not null)
            {
                var admitted = scope.AdmitChild(scope.Reference.ExecutionId + "/pilot-" + role + "-" + Guid.NewGuid().ToString("N"), "pilot-" + role, seconds);
                Require(admitted.Allowed, string.Join("; ", admitted.Errors)); child = admitted.Scope;
            }
            var limits = profile with
            {
                TimeoutSeconds = seconds,
            };
            NativeStorageReservation? storageReservation = null;
            var enteredExecute = false;
            MutationNativeExecution? native = null;
            var phaseWatch = Stopwatch.StartNew();
            string? retained = null;
            var requiredEvidence = new List<string>();
            var phaseSettlementErrors = new List<string>();
            void RetainSettlementFailure(IReadOnlyList<string> errors)
            {
                pendingSettlement = true;
                phaseSettlementErrors.AddRange(errors);
                diagnostics.Add("Pilot " + role + " settlement requires reconciliation: " + string.Join(" ", errors));
                for (var index = 0; index < observations.Count; index++)
                    if (observations[index].OutputPath == Path.GetRelativePath(repository, output).Replace('\\', '/'))
                        observations[index] = observations[index] with
                        {
                            Execution = observations[index].Execution with
                            { SettlementErrors = observations[index].Execution.SettlementErrors.Concat(errors).Distinct(StringComparer.Ordinal).ToArray() }
                        };
            }
            void MoveEvidence(string source, string destination)
            {
                using var mutation = BeginEvidenceMutation(child, ownedStorage, [source, destination], 0);
                File.Move(source, destination, true);
                try
                {
                    var settled = SettleNativeStorage(mutation, ownedStorage, true, [destination]);
                    if (settled is { Allowed: false }) RetainSettlementFailure(settled.Errors);
                }
                catch (Exception error) when (Expected(error)) { RetainSettlementFailure([error.Message]); }
            }
            try
            {
                storageReservation = BeginNativeStorage(repository, output, storage, limits, child, ownedStorage);
                enteredExecute = true;
                native = Execute(repository, output, storage, limits, command, child, cancellationToken, ownedStorage, storageReservation, workController);
                executeWall += phaseWatch.ElapsedMilliseconds;
                Require(native.SafelyStopped,
                    "Pilot native ownership or child cleanup is uncertain; retain its original output and resolve the owned execution before report movement or cleanup.");
                var observedReport = reportPath ?? (role == "infrastructure" ? profile.ReportPath : null);
                if (observedReport is not null && File.Exists(Resolve(output, observedReport)))
                {
                    var source = Resolve(output, observedReport);
                    Require(new FileInfo(source).Length is > 0 and <= 100L * 1024 * 1024, "Pilot native report exceeds its bound.");
                    var unitReport = role is "baseline" or "postflight" or "preflight-postflight";
                    retained = Resolve(root, "representative-" + role + (unitReport ? pilot.UnitResultFormat == "trx" ? ".trx" : ".xml" : ".json"));
                    MoveEvidence(source, retained);
                    requiredEvidence.Add(retained);
                }
                var artifacts = new List<TestArtifact>();
                if (role == "infrastructure")
                    foreach (var name in new[] { "stdout.log", "stderr.log" })
                    {
                        var source = Resolve(output, name);
                        if (!File.Exists(source)) continue;
                        Require(new FileInfo(source).Length <= 256 * 1024, "Pilot infrastructure diagnostics exceed the bounded current evidence limit.");
                        var destination = Resolve(root, "representative-infrastructure-" + name);
                        MoveEvidence(source, destination);
                        requiredEvidence.Add(destination);
                        artifacts.Add(new("native-pilot-infrastructure", Path.GetRelativePath(repository, destination).Replace('\\', '/'), FileHash(destination), new FileInfo(destination).Length));
                    }
                var observedExecution = phaseSettlementErrors.Count == 0 ? native : native with
                { SettlementErrors = native.SettlementErrors.Concat(phaseSettlementErrors).Distinct(StringComparer.Ordinal).ToArray() };
                var observation = new NativePilotObservation(role, observedExecution,
                    retained is null ? null : Path.GetRelativePath(repository, retained).Replace('\\', '/'), retained is null ? null : FileHash(retained))
                { Artifacts = artifacts, OutputPath = Path.GetRelativePath(repository, output).Replace('\\', '/') };
                observations.Add(observation);
                Prune(output, native.Status == "exited" && native.Error is null);
                return observation;
            }
            finally
            {
                if (!enteredExecute) child?.Returned(true);
                if (phaseWatch.IsRunning && native is null) executeWall += phaseWatch.ElapsedMilliseconds;
                try
                {
                    var settled = SettleNativeStorage(storageReservation, ownedStorage,
                        native?.SafelyStopped ?? storageReservation?.SafelyStopped ?? !enteredExecute,
                        requiredEvidence);
                    if (settled is { Allowed: false }) RetainSettlementFailure(settled.Errors);
                }
                catch (Exception error) when (Expected(error)) { RetainSettlementFailure([error.Message]); }
                finally
                {
                    try
                    {
                        if (child is not null)
                        {
                            var control = child.Finish(native?.Status ?? "failed", "Native correctness pilot " + role, requiredEvidence);
                            var errors = SettlementErrors(control, child.Reference);
                            if (errors.Length > 0) RetainSettlementFailure(errors);
                        }
                    }
                    catch (Exception error) when (Expected(error)) { RetainSettlementFailure([error.Message]); }
                    finally { child?.Dispose(); }
                }
            }
        }
        try
        {
            if (restorationOnly)
            {
                if (retainedSnapshot?.BasisSha256 == basis)
                {
                    observations.AddRange(retainedSnapshot.Observations.Where(item => item.Role is "baseline" or "mutation" or "infrastructure"));
                    pendingSettlement = observations.Any(item => item.Execution.SettlementErrors.Count != 0);
                }
                recoveryRequired = true; Save("restoring");
                var restore = Phase("restoration", pilot.RestorationCommand, pilot.RecoveryTimeoutSeconds);
                var postflight = Phase("postflight", pilot.BaselineCommand, pilot.RecoveryTimeoutSeconds, pilot.BaselineReportPath);
                restorationPassed = PilotHealthy(restore.Execution) && PilotHealthy(postflight.Execution)
                    && PilotUnit(repository, profile, postflight) is { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 };
                recoveryRequired = !restorationPassed;
                Require(restorationPassed, "The pinned native restoration and healthy postflight did not complete.");
                if (observations.Count == 5)
                {
                    VerifyPilotObservations(repository, profile, Snapshot("assessing"));
                    nativeReady = true;
                    ready = !pendingSettlement;
                }
                Save(nativeReady ? FinalStatus() : "restored");
                return Snapshot(nativeReady ? FinalStatus() : "restored");
            }
            Save("running");
            // A prior interrupted phase is held by the same work ledger. Once ownership
            // is safely reconciled, restoration remains a prerequisite to another pilot.
            if (reusable.RecoveryRequired)
            {
                var restored = Phase("preflight-restoration", pilot.RestorationCommand, pilot.RecoveryTimeoutSeconds);
                Require(PilotHealthy(restored.Execution), "Interrupted pilot restoration failed.");
                var healthy = Phase("preflight-postflight", pilot.BaselineCommand, pilot.RecoveryTimeoutSeconds, pilot.BaselineReportPath);
                Require(PilotUnit(repository, profile, healthy).Status == "passed", "Interrupted pilot ordinary execution remains unhealthy.");
            }
            var baseline = Phase("baseline", pilot.BaselineCommand, profile.TimeoutSeconds, pilot.BaselineReportPath);
            Require(PilotHealthy(baseline.Execution) && PilotUnit(repository, profile, baseline) is { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 },
                "Pilot native baseline did not pass.");
            recoveryRequired = true; Save("instrumenting");
            try
            {
                var mutationSeconds = scope is null ? profile.TimeoutSeconds
                    : Math.Min(profile.TimeoutSeconds, (int)Math.Floor(scope.Remaining.TotalSeconds) - pilot.RecoveryTimeoutSeconds * 2 - 2);
                Require(mutationSeconds >= 1, "Pilot expansion cannot preserve its bounded restoration and healthy-postflight time.");
                var mutation = Phase("mutation", profile.Command, mutationSeconds, profile.ReportPath);
                VerifyPilotMutation(repository, profile, mutation);
                var infrastructure = Phase("infrastructure", pilot.InfrastructureCommand, Math.Min(profile.TimeoutSeconds, pilot.RecoveryTimeoutSeconds));
                Require(infrastructure.ReportPath is null && InfrastructureRejected(infrastructure.Execution),
                    "Pilot infrastructure failure was not rejected without a valid mutation report.");
            }
            finally
            {
                var restored = Phase("restoration", pilot.RestorationCommand, pilot.RecoveryTimeoutSeconds);
                var healthy = Phase("postflight", pilot.BaselineCommand, pilot.RecoveryTimeoutSeconds, pilot.BaselineReportPath);
                recoveryRequired = !PilotHealthy(restored.Execution) || !PilotHealthy(healthy.Execution)
                    || PilotUnit(repository, profile, healthy) is not { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 };
                restorationPassed = !recoveryRequired;
                Require(!recoveryRequired, "Pilot restoration and subsequent ordinary native execution must both pass.");
            }
            Require(PilotBasis(repository, profile, profilePath) == basis, "Pilot material inputs changed during native execution.");
            VerifyPilotObservations(repository, profile, Snapshot("assessing"));
            nativeReady = true;
            ready = !pendingSettlement;
        }
        catch (Exception error) when (Expected(error) || error is OperationCanceledException or System.Xml.XmlException)
        { ready = false; diagnostics.Add(error.Message); }
        try { Save(FinalStatus()); }
        catch (Exception error) when (Expected(error)) { ready = false; diagnostics.Add("Pilot result admission failed: " + error.Message); }
        return Snapshot(FinalStatus());
    }

    private static bool PilotHealthy(MutationNativeExecution execution)
        => execution is { Status: "exited", ExitCode: 0, Error: null, OutputTruncated: false, SafelyStopped: true };

    private static bool InfrastructureRejected(MutationNativeExecution execution)
        => execution is { Status: "exited", Error: null, OutputTruncated: false, SafelyStopped: true }
            && execution.ExitCode is not null and not 0 and not 2;

    private static string PilotReport(string repository, NativePilotObservation observation)
    {
        Require(observation.ReportPath is not null && observation.ReportSha256 is { Length: 64 }, "Pilot native report is missing.");
        var path = Resolve(repository, observation.ReportPath);
        Require(File.Exists(path) && FileHash(path) == observation.ReportSha256, "Pilot native report is missing or changed.");
        return path;
    }

    private static TestSuiteExecution PilotUnit(string repository, MutationRunProfile profile, NativePilotObservation observation)
        => IncrementalTestService.ReadNativeUnitReport(repository, profile.Id, profile.WorkingDirectory,
            profile.CorrectnessPilot!.UnitResultFormat, PilotReport(repository, observation));

    private static void VerifyPilotMutation(string repository, MutationRunProfile profile, NativePilotObservation observation)
    {
        Require(observation.Execution is { Status: "exited", ExitCode: 0 or 2, Error: null, OutputTruncated: false, SafelyStopped: true },
            "Pilot mutation did not complete with healthy native execution.");
        var path = PilotReport(repository, observation);
        var suite = new TestSuiteProfile(profile.Id, profile.Id, "mutation", "native", "run", profile.WorkingDirectory,
            "stryker-json", path, "-", "-", "-", "always", "local", "current-result");
        var assessment = new StrykerJsonResultAdapter().Read(new(repository, suite, path, null, null, profile.BreakThreshold));
        Require(assessment.Mutation is { ValidEvidence: true, TimedOut: 0, NoCoverage: 0, RuntimeErrors: 0, Pending: 0, Unknown: 0 } mutation
            && mutation.Killed >= profile.MinimumCanaryKills, "Pilot mutation lacks valid detected native outcomes.");
        var diagnostics = new List<string>();
        Require(MatchScope(repository, profile, path, true, diagnostics), "Pilot fixture source scope is incomplete or changed. " + string.Join("; ", diagnostics));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var working = Resolve(repository, profile.WorkingDirectory, true);
        var control = Resolve(repository, profile.CorrectnessPilot!.ControlSourceFile);
        var file = document.RootElement.GetProperty("files").EnumerateObject().Single(item =>
            string.Equals(Path.GetFullPath(item.Name.Replace('\\', Path.DirectorySeparatorChar), working), control, CisPathSafety.PlatformComparison));
        Require(file.Value.GetProperty("mutants").EnumerateArray().Any(mutant => mutant.GetProperty("status").GetString() == "Survived"),
            "The deliberately surviving native control was not observed as Survived; an all-killed adapter cannot establish readiness.");
    }

    private static void VerifyPilotObservations(string repository, MutationRunProfile profile, NativeCorrectnessPilotResult result)
    {
        Require(result.FinishedUtc >= result.StartedUtc, "Pilot completion predates its native observations.");
        Require(result.Observations.Count is >= 5 and <= 7, "Pilot observation set is incomplete or exceeds its bounded roles.");
        var roles = result.Observations.ToDictionary(item => item.Role, StringComparer.Ordinal);
        foreach (var role in new[] { "baseline", "mutation", "infrastructure", "restoration", "postflight" })
            Require(roles.ContainsKey(role), "Pilot is missing native observation: " + role);
        foreach (var observation in result.Observations)
        {
            var pilot = profile.CorrectnessPilot!;
            var command = observation.Role switch
            {
                "baseline" or "postflight" or "preflight-postflight" => pilot.BaselineCommand,
                "mutation" => profile.Command,
                "infrastructure" => pilot.InfrastructureCommand,
                "restoration" or "preflight-restoration" => pilot.RestorationCommand,
                _ => throw new InvalidDataException("Unknown native pilot observation role."),
            };
            Require(observation.OutputPath is not null, "Pilot native output location is missing.");
            var output = Resolve(repository, observation.OutputPath);
            var runs = Resolve(repository, LocalRoot + "/" + profile.Id + "/runs");
            Require(CisPathSafety.IsUnderRoot(runs, output, false) && Path.GetFileName(output) == observation.Role
                && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(Path.GetDirectoryName(output)!), @"\Apilot-[a-f0-9]{32}\z"),
                "Pilot native output does not belong to its bounded representative attempt.");
            Require(observation.Execution.Command is { } actual && actual.Length == command.Length
                && observation.Execution.ExecutablePath is { } executable
                && profile.CorrectnessPilot!.ToolFiles.Any(tool => string.Equals(Path.GetFullPath(tool), executable, CisPathSafety.PlatformComparison))
                && string.Equals(actual[0], executable, CisPathSafety.PlatformComparison)
                && actual.Skip(1).SequenceEqual(command.Skip(1).Select(value => value.Replace("{output}", output, StringComparison.Ordinal)), StringComparer.Ordinal),
                "Pilot native command or declared executable does not match the retained execution.");
            Require(observation.Execution.CisToolAssemblySha256 == FileHash(typeof(CisProcessJob).Assembly.Location),
                "Pilot native helper identity changed.");
            foreach (var artifact in observation.Artifacts)
            {
                var path = Resolve(repository, artifact.Path);
                Require(File.Exists(path) && new FileInfo(path).Length == artifact.Bytes && FileHash(path) == artifact.Digest,
                    "Pilot retained native diagnostic changed.");
            }
        }
        foreach (var role in new[] { "baseline", "postflight" })
            Require(PilotHealthy(roles[role].Execution) && PilotUnit(repository, profile, roles[role]) is { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 },
                "Pilot native ordinary execution is not healthy: " + role);
        Require(roles["infrastructure"].ReportPath is null && InfrastructureRejected(roles["infrastructure"].Execution),
            "Pilot has no rejected native infrastructure failure.");
        Require(PilotHealthy(roles["restoration"].Execution), "Pilot has no successful native restoration.");
        VerifyPilotMutation(repository, profile, roles["mutation"]);
        if (profile.CorrectnessPilot!.ChildTestName is { } childTest)
            foreach (var role in new[] { "baseline", "postflight" })
            {
                Require(roles[role].Execution.OwnedProcessPeak >= 2
                    && PilotUnit(repository, profile, roles[role]).Cases.Any(test => test.Name == childTest && test.Status == "passed"),
                    "The claimed child-process transport has no observed owned child and passing native transport case.");
            }
    }
}
