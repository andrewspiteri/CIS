using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

/// <summary>Runs native unit and mutation batches, reusing only current completed results.</summary>
public sealed partial class IncrementalTestService
{
    public IncrementalTestResult Plan(IncrementalTestOptions options)
    {
        try
        {
            var input = Read(options);
            var state = State(input);
            var result = Summarize(input, options, Select(input, state, options.Force), state,
                state.RecoveryRequired || state.ActiveExecutionUntilUtc is not null ? "recovery-required" : "planned", 0);
            return StateSettlementErrors(input.Repository, state, Safe(input.Root, "latest-run.json")) is { Length: > 0 } errors
                ? result with { Status = "settlement-required", ExitCode = 4, ScopeComplete = false, Diagnostics = errors } : result;
        }
        catch (Exception error) when (Expected(error)) { return new("invalid-plan", 2) { Diagnostics = [error.Message] }; }
    }

    public IncrementalTestResult Run(IncrementalTestOptions options, int nativeTimeoutSeconds = 0)
        => Run(options, nativeTimeoutSeconds, CancellationToken.None);

    public IncrementalTestResult Run(IncrementalTestOptions options, int nativeTimeoutSeconds, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        Input? input = null;
        IncrementalTestState? state = null;
        var executedUnit = 0;
        var executedMutation = 0;
        var rerunMutations = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        CisNativeWorkAdmission? admission = null;
        ICisNativeWorkScope? work = null;
        string? recoveryOf = null;
        var restorationCompleted = false;
        NativeCorrectnessPilotResult? pilot = null;
        IncrementalTestResult Complete(IncrementalTestResult result) => FinishIncrementalResult(input, state,
            result with { WorkReference = admission?.Reference, WorkCoverage = admission?.Coverage ?? "unaccounted-non-adopted", Pilot = pilot },
            work, state?.RecoveryRequired == true || recoveryOf is not null && !restorationCompleted ? "RecoveryRequired"
                : recoveryOf is not null ? "Restored" : "native-finished", admission?.Control);
        try
        {
            var retained = RetainedWork(options);
            recoveryOf = options.Work?.RecoveryOfExecutionId ?? retained.RecoveryOf;
            var association = options.Work is not { } requested ? retained.Association : requested with
            {
                ChangeId = requested.ChangeId ?? retained.Association?.ChangeId,
                TaskId = requested.TaskId ?? retained.Association?.TaskId,
                AuthorityPath = requested.AuthorityPath ?? retained.Association?.AuthorityPath,
                Actor = requested.Actor ?? retained.Association?.Actor
            };
            admission = MutationRunService.AdmitNative(_nativeWork,
                new(options.RepositoryPath, "native/incremental/" + Guid.NewGuid().ToString("N"), "incremental-" + options.Layer,
                    options.MaximumSeconds, association)
                { RecoveryOfExecutionId = recoveryOf, RecoveryBasis = retained.RecoveryBasis });
            if (!admission.Allowed)
                return new("held", 4)
                {
                    Layer = options.Layer,
                    Diagnostics = admission.Errors,
                    WorkReference = admission.Reference,
                    WorkCoverage = admission.Coverage,
                    WorkControl = admission.Control
                };
            work = admission.Scope;
            input = Read(options) with { Work = work, OwnedStorage = _ownedStorage, Cancellation = cancellationToken };
            Require(nativeTimeoutSeconds is >= 0 and <= 3600, "Native timeout override must be 0-3600 seconds.");
            if (nativeTimeoutSeconds > 0)
            {
                diagnostics.Add($"Native invocation guard override: {nativeTimeoutSeconds} seconds; source, native test timeouts and assessment policy are unchanged.");
            }
            if (work is not null)
            {
                Require(_ownedStorage is not null, "Governed native work requires the installed owned-storage controller.");
                var storage = _ownedStorage.Inspect(work.AuthorityPath,
                    new(work.Reference.ChangeId, work.Reference.TaskId, AuthorityPath: work.AuthorityPath, Actor: work.Actor));
                Require(storage.Allowed, string.Join(" ", storage.Errors));
            }
            Directory.CreateDirectory(input.Root);
            // Share the native build/output lease with the existing mutation command.
            var leasePath = Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
            using var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            state = State(input);
            state = state with
            {
                WorkReference = admission.Reference,
                WorkAuthorityPath = admission.AuthorityPath,
                WorkAssociation = admission.Reference is { } reference ? new(reference.ChangeId, reference.TaskId,
                    AuthorityPath: admission.AuthorityPath, Actor: work?.Actor ?? association?.Actor) : null,
                WorkCoverage = admission.Coverage,
                RecoveryBasis = retained.RecoveryBasis
            };
            Require(state.ActiveExecutionUntilUtc is null || state.ActiveExecutionUntilUtc <= DateTimeOffset.UtcNow
                    || recoveryOf is not null && admission.Required,
                "The previous native guard may still be active. Retry after " + state.ActiveExecutionUntilUtc?.ToString("O") + ".");
            if (recoveryOf is not null)
            {
                Require(input.Profile.RecoveryCommand is not null, "The original native operation has no supported pinned recovery command.");
                IncrementalTestBatch? recoveryBatch = null;
                try
                {
                    var candidates = Select(input, state, false).Batches;
                    recoveryBatch = candidates.FirstOrDefault(batch => batch.SourceFile == state.RecoverySourceFile);
                }
                catch (Exception error) when (Expected(error))
                { diagnostics.Add("Current graph selection is unavailable; restoration will not promote any retained native report. " + error.Message); }
                var source = state.RecoverySourceFile;
                if (source is null)
                {
                    Require(input.Profile.SourceFiles.Length == 1 || !input.Profile.RecoveryCommand.Any(value => value.Contains("{sourceFile}", StringComparison.Ordinal)),
                        "The original per-source recovery context is missing. Recover its protected state before selecting a source-specific restoration command.");
                    source = input.Profile.SourceFiles.First();
                }
                recoveryBatch ??= new("restoration-only", source, "restoration-only", [], input.Profile.FullTestFilter, "", "", false, false, []);
                var reusable = state.Batches.TryGetValue(recoveryBatch.Id, out var saved)
                    && RecoverableNative(input, recoveryBatch, saved);
                restorationCompleted = reusable ? ResumeRecovery(input, recoveryBatch, ref state)
                    : Recover(input, recoveryBatch, ref state, out _, out _, out _);
                Save(input, state);
                var restored = new IncrementalTestResult(restorationCompleted ? "restored" : "recovery-failed", restorationCompleted ? 0 : 4)
                {
                    ProfileId = input.Profile.Id,
                    Layer = options.Layer,
                    StatePath = Relative(input.Repository, input.StatePath),
                    ReusedMutationBatches = reusable && restorationCompleted ? 1 : 0,
                    DurationMilliseconds = watch.ElapsedMilliseconds,
                    Diagnostics = diagnostics,
                    ScopeComplete = false
                };
                SaveLatest(input, restored);
                return Complete(restored);
            }
            var selection = Select(input, state, options.Force);
            pilot = InspectIncrementalCorrectnessPilot(input, admission, _nativeWork);
            var expansion = options.Layer != "unit" && input.Profile.MutationCommand is not null
                && (options.Force || selection.Batches.Any(batch => !batch.MutationCurrent
                    || !batch.UnitCurrent && state.Batches.TryGetValue(batch.Id, out var saved) && saved.Unit is not null));
            if (expansion && PilotBlocksExpansion(pilot, admission))
                return Complete(new("pilot-required", 4)
                {
                    ProfileId = input.Profile.Id,
                    Layer = options.Layer,
                    StatePath = Relative(input.Repository, input.StatePath),
                    Diagnostics = pilot.Diagnostics.Append(pilot.NextAction).ToArray()
                });
            PrepareReportMutations(input, state, selection.Batches, options);
            PruneRetired(input, state, selection.Batches.Select(batch => batch.Id));
            SaveLatest(input, new("running", 4) { ProfileId = input.Profile.Id, Layer = options.Layer });
            if (state.ActiveExecutionUntilUtc is not null)
            {
                state = state with { ActiveExecutionUntilUtc = null };
                Save(input, state);
            }
            CleanWork(input);
            if (state.RecoveryRequired)
            {
                Require(input.Profile.RecoveryCommand is not null, "An interrupted invocation requires a configured recoveryCommand.");
                var batch = selection.Batches.FirstOrDefault(item => item.SourceFile == state.RecoverySourceFile) ?? selection.Batches[0];
                Require(ResumeRecovery(input, batch, ref state), "Baseline recovery did not pass; saved results remain on hold.");
                restorationCompleted = true;
                selection = Select(input, state, options.Force);
            }
            if (options.Force)
            {
                foreach (var batch in selection.Batches)
                {
                    state.Batches.TryGetValue(batch.Id, out var prior);
                    var mutation = options.Layer == "unit"
                        && (prior?.Unit is null || Current(input, batch.SourceFile, prior.Unit, batch.UnitFingerprint, false))
                        && Current(input, batch.SourceFile, prior?.Mutation, batch.MutationFingerprint, true)
                        ? prior!.Mutation : null;
                    state.Batches[batch.Id] = new(batch.SourceFile, null, mutation);
                }
                Save(input, state);
            }
            var attempted = 0;
            var failed = false;
            foreach (var batch in selection.Batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mutationWanted = options.Layer != "unit" && input.Profile.MutationCommand is not null;
                if (batch.UnitCurrent && (!mutationWanted || batch.MutationCurrent)) continue;
                if (attempted >= options.MaximumBatches) break;
                state.Batches.TryGetValue(batch.Id, out var prior);
                var saved = prior ?? new(batch.SourceFile, null, null);
                var retainMutation = (batch.UnitCurrent || saved.Unit is null)
                    && Current(input, batch.SourceFile, saved.Mutation, batch.MutationFingerprint, true);
                var reserve = mutationWanted && !retainMutation ? 2 * input.Profile.RecoveryTimeoutSeconds + 10 : 0;
                if (options.MaximumSeconds - watch.Elapsed.TotalSeconds <= reserve + 2) break;
                attempted++;
                if (!batch.UnitCurrent)
                {
                    var mutation = retainMutation ? saved.Mutation : null;
                    saved = saved with { Unit = null, Mutation = mutation };
                    state.Batches[batch.Id] = saved;
                    Save(input, state);
                    var output = Work(input);
                    var execution = Execute(input, batch, input.Profile.UnitCommand, output,
                        Seconds(input, options, watch, reserve), ref state);
                    var unchanged = Unchanged(input, batch);
                    var result = Assess(input, batch, false, output, execution, unchanged);
                    executedUnit++;
                    saved = saved with { Unit = result, Mutation = result.Completed ? mutation : null };
                    state.Batches[batch.Id] = saved;
                    Save(input, state);
                    CleanWork(input, !result.Completed);
                    if (!result.Completed || execution.SettlementErrors.Count > 0)
                    { diagnostics.AddRange(result.Diagnostics); failed = true; break; }
                }
                if (!mutationWanted || Current(input, batch.SourceFile, saved.Mutation, batch.MutationFingerprint, true)) continue;
                if (options.MaximumSeconds - watch.Elapsed.TotalSeconds <= reserve + 2) break;
                state = state with { RecoveryRequired = true, RecoverySourceFile = batch.SourceFile };
                saved = saved with { Mutation = null };
                state.Batches[batch.Id] = saved;
                Save(input, state); // Durable before any instrumentation can begin.
                var nativeOutput = Work(input);
                var native = Execute(input, batch, input.Profile.MutationCommand!, nativeOutput,
                    Seconds(input, options, watch, reserve, nativeTimeoutSeconds > 0 ? nativeTimeoutSeconds : null), ref state);
                var assessed = Assess(input, batch, true, nativeOutput, native, Unchanged(input, batch));
                executedMutation++;
                rerunMutations.Add(batch.Id);
                // A crash between native completion and recovery cannot publish a reusable mutation result.
                state.Batches[batch.Id] = saved with { Mutation = assessed with { Completed = false, Status = "recovering" } };
                Save(input, state);
                CleanWork(input, !assessed.Completed || assessed.Mutation?.TimedOut > 0);
                var recovered = Recover(input, batch, ref state, out var recovery, out var postflight, out var passed);
                assessed = assessed with
                {
                    Completed = assessed.Completed && recovered && Unchanged(input, batch),
                    RecoveryExecution = recovery,
                    PostflightExecution = postflight,
                    PostflightPassed = passed,
                    Diagnostics = recovered ? assessed.Diagnostics : assessed.Diagnostics.Append("Baseline recovery failed.").ToArray(),
                };
                assessed = assessed with { Status = assessed.Completed ? assessed.Mutation?.TimedOut > 0 ? "completed-with-timeouts" : "completed" : "failed" };
                state.Batches[batch.Id] = saved with { Mutation = assessed };
                Save(input, state);
                if (!assessed.Completed || input.SettlementErrors.Count > 0)
                { diagnostics.AddRange(assessed.Diagnostics); failed = true; break; }
            }
            var final = Select(Read(options), state, false);
            var resultSummary = Summarize(input, options, final, state, failed ? cancellationToken.IsCancellationRequested ? "cancelled" : "failed" : "completed", failed ? 4 : 0) with
            {
                ExecutedUnitBatches = executedUnit,
                ExecutedMutationBatches = executedMutation,
                ReusedUnitBatches = selection.Batches.Count(batch => batch.UnitCurrent),
                ReusedMutationBatches = options.Layer == "unit" ? 0 : selection.Batches.Count(batch => batch.MutationCurrent && !rerunMutations.Contains(batch.Id)
                    && final.Batches.Any(current => current.Id == batch.Id && current.UnitCurrent && current.MutationCurrent)),
                DurationMilliseconds = watch.ElapsedMilliseconds,
                RetainedBytes = Bytes(input.Storage),
                Diagnostics = diagnostics,
            };
            if (!failed && !resultSummary.ScopeComplete) resultSummary = resultSummary with { Status = "batch-limit", ExitCode = 3 };
            SaveLatest(input, resultSummary);
            return Complete(resultSummary);
        }
        catch (Exception error) when (Expected(error))
        {
            var result = new IncrementalTestResult("failed", 4)
            {
                ProfileId = input?.Profile.Id,
                StatePath = input is null ? null : Relative(input.Repository, input.StatePath),
                Layer = options.Layer,
                ExecutedUnitBatches = executedUnit,
                ExecutedMutationBatches = executedMutation,
                DurationMilliseconds = watch.ElapsedMilliseconds,
                Diagnostics = diagnostics.Append(error.Message).ToArray(),
            };
            if (input is not null)
            {
                try { SaveLatest(input, result); }
                catch (Exception saveError) when (Expected(saveError)) { result = result with { Diagnostics = result.Diagnostics.Append(saveError.Message).ToArray() }; }
            }
            return Complete(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Complete(new("cancelled", 4)
            {
                Layer = options.Layer,
                ProfileId = input?.Profile.Id,
                StatePath = input is null ? null : Relative(input.Repository, input.StatePath),
                DurationMilliseconds = watch.ElapsedMilliseconds,
                Diagnostics = diagnostics.Append("Native work was cancelled; unfinished recovery remains held.").ToArray()
            });
        }
        finally { work?.Dispose(); }
    }

    private (CisNativeWorkOptions? Association, string? RecoveryOf, string? RecoveryBasis) RetainedWork(IncrementalTestOptions options)
    {
        var repository = Path.GetFullPath(options.RepositoryPath);
        var profilePath = Safe(repository, options.ProfilePath);
        Require(new FileInfo(profilePath).Length <= 262144, "Incremental profile exceeds its size limit.");
        var profile = ReadJson<IncrementalTestProfile>(profilePath, 262144);
        var id = profile.Id;
        Require(Slug(id), "Incremental profile id is invalid.");
        Require(profile.ToolFiles is { Length: <= 256 } && profile.ConfigFiles is { Length: <= 2048 }
            && profile.Environment is not null, "Recovery inputs require bounded tool/configuration/environment inventories.");
        var recipe = profile.MutationCommand is not null && profile.RecoveryCommand is not null ? HashObject(new
        {
            policy = "cis-native-incremental-recovery-v2",
            profile.WorkingDirectory,
            profile.RecoveryCommand,
            postflight = profile.UnitCommand,
            profile.FullTestFilter,
            tools = profile.ToolFiles.Select(file => new { file, hash = HashFile(Path.GetFullPath(file, repository)) }),
            configuration = profile.ConfigFiles.Select(file => new { file, hash = HashFile(Safe(repository, file)) }),
            environment = profile.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier
        }) : null;
        var path = Safe(repository, LocalRoot + "/" + id + "/state.json");
        if (!File.Exists(path)) return (null, null, recipe);
        var state = ReadJson<IncrementalTestState>(path, 16 * 1024 * 1024);
        if (!state.RecoveryRequired && state.ActiveExecutionUntilUtc is null)
            Require(StateSettlementErrors(repository, state, Safe(repository, LocalRoot + "/" + id + "/latest-run.json")).Length == 0,
                "Required native settlement remains unresolved. Reconcile the exact retained work reference before another invocation; its valid reports do not need to be rerun.");
        if (!state.RecoveryRequired && state.ActiveExecutionUntilUtc is null && state.ActiveWorkReference is null) return (null, null, recipe);
        Require(state.RecoveryBasis is null || state.RecoveryBasis == recipe,
            "The retained recovery recipe changed. Inspect the original native work and obtain a bounded authorized recovery decision; a new profile cannot spend its restoration reserve.");
        var reference = state.WorkReference ?? state.ActiveWorkReference;
        var association = state.WorkAssociation ?? (reference is null ? null
            : new CisNativeWorkOptions(reference.ChangeId, reference.TaskId, AuthorityPath: state.WorkAuthorityPath));
        return (association, reference?.ExecutionId, recipe);
    }

    private bool Unchanged(Input input, IncrementalTestBatch batch)
    {
        var options = new IncrementalTestOptions(input.Repository, Relative(input.Repository, input.ProfilePath));
        var now = Read(options);
        if (now.CommonIdentity != input.CommonIdentity) return false;
        var selected = Select(now, new(), false).Batches.Single(item => item.Id == batch.Id);
        return selected.UnitFingerprint == batch.UnitFingerprint && selected.MutationFingerprint == batch.MutationFingerprint;
    }

    private static int Seconds(Input input, IncrementalTestOptions options, Stopwatch watch, int reserve, int? nativeOverride = null)
        => Math.Max(1, Math.Min(nativeOverride ?? input.Profile.TimeoutSeconds, (int)(options.MaximumSeconds - watch.Elapsed.TotalSeconds - reserve)));

    private static MutationNativeExecution Execute(Input input, IncrementalTestBatch batch, string[] command,
        string output, int seconds, ref IncrementalTestState state, bool fullTests = false)
    {
        input.Cancellation.ThrowIfCancellationRequested();
        var admission = input.Work?.AdmitChild("native/incremental/stage/" + Guid.NewGuid().ToString("N"),
            fullTests ? "native-restoration" : command == input.Profile.UnitCommand ? "native-unit" : "native-mutation", Math.Min(3600, seconds + 15));
        if (admission is { Allowed: false }) throw new InvalidDataException(string.Join(" ", admission.Errors));
        using var work = admission?.Scope;
        var storageResult = FinishNativeStorage(input, state);
        Require(storageResult?.Allowed != false, string.Join(" ", storageResult?.Errors ?? []));
        var limits = new MutationRunProfile
        {
            WorkingDirectory = input.Profile.WorkingDirectory,
            TimeoutSeconds = seconds,
            MaxRunBytes = input.Profile.MaxRunBytes,
            MaxRetainedBytes = input.Profile.MaxRetainedBytes,
            MinFreeBytes = input.Profile.MinFreeBytes,
        };
        input.ActiveStorage = MutationRunService.BeginNativeStorage(input.Repository, output, input.Storage, limits, work, input.OwnedStorage);
        limits = MutationRunService.StorageLimits(limits, input.ActiveStorage);
        state = state with { ActiveExecutionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(seconds + 30) };
        state = state with { ActiveWorkReference = work?.Reference };
        Save(input, state);
        var working = Safe(input.Repository, input.Profile.WorkingDirectory, true);
        string? mutationConfig = null;
        if (command.Any(value => value.Contains("{mutationConfig}", StringComparison.Ordinal)))
        {
            Require(input.Profile.StrykerConfigFile is not null, "Mutation configuration placeholder requires strykerConfigFile.");
            var template = ReadJson<JsonObject>(Safe(input.Repository, input.Profile.StrykerConfigFile), 262144);
            var config = template["stryker-config"]!.AsObject();
            config["mutate"] = new JsonArray(Relative(working, Safe(input.Repository, batch.SourceFile)));
            config["test-case-filter"] = batch.TestFilter;
            if (input.Profile.RerunAdditionalTimeoutMilliseconds is int additionalTimeout)
                config["additional-timeout"] = additionalTimeout;
            mutationConfig = Safe(output, "stryker-config.json");
            File.WriteAllText(mutationConfig, template.ToJsonString(Json));
        }
        var expanded = command.Select(value => value.Replace("{sourceFile}", Relative(working, Safe(input.Repository, batch.SourceFile)), StringComparison.Ordinal)
            .Replace("{testFilter}", fullTests ? input.Profile.FullTestFilter : batch.TestFilter, StringComparison.Ordinal)
            .Replace("{mutationConfig}", mutationConfig ?? "", StringComparison.Ordinal)).ToArray();
        limits = limits with { Environment = NativeEnvironment(input.Profile, working) };
        var execution = MutationRunService.Execute(input.Repository, output, input.Storage, limits, expanded, work, input.Cancellation,
            input.OwnedStorage, input.ActiveStorage) with
        { SettlementReference = input.Work?.Reference, WorkAuthorityPath = work?.AuthorityPath };
        if (work is not null)
        {
            var control = work.Finish("native-finished", "Native incremental stage completed: " + execution.Status, []);
            var errors = MutationRunService.SettlementErrors(control, work.Reference);
            execution = execution with { SettlementErrors = errors };
            input.SettlementErrors.AddRange(errors);
        }
        state = state with
        {
            ActiveExecutionUntilUtc = execution.SafelyStopped ? null : state.ActiveExecutionUntilUtc,
            ActiveWorkReference = execution.SafelyStopped ? null : work?.Reference
        };
        Save(input, state);
        Require(execution.SafelyStopped,
            "Owned native work has not been proven stopped; its output, process evidence and reservation remain held before assessment, cleanup or restoration.");
        return execution;
    }

    private static bool Recover(Input input, IncrementalTestBatch batch, ref IncrementalTestState state,
        out MutationNativeExecution? recovery, out MutationNativeExecution? postflight, out int passed, bool clearRecovery = true)
    {
        recovery = null; postflight = null; passed = 0;
        if (input.Profile.RecoveryCommand is null) return false;
        var output = Work(input);
        recovery = Execute(input, batch, input.Profile.RecoveryCommand, output, input.Profile.RecoveryTimeoutSeconds, ref state, true);
        var healthy = Healthy(recovery);
        CleanWork(input, !healthy);
        if (!healthy) return false;
        output = Work(input);
        postflight = Execute(input, batch, input.Profile.UnitCommand, output, input.Profile.RecoveryTimeoutSeconds, ref state, true);
        try
        {
            var result = ReadUnit(input, Safe(output, input.Profile.UnitReportPath));
            healthy = Healthy(postflight) && result is { Status: "passed", Total: > 0, Failed: 0, Skipped: 0 };
            passed = result.Passed;
        }
        catch (Exception error) when (Expected(error)) { healthy = false; }
        CleanWork(input, !healthy);
        if (healthy && clearRecovery)
        {
            state = state with { RecoveryRequired = false, RecoverySourceFile = null };
            Save(input, state);
        }
        return healthy;
    }

    private static bool Healthy(MutationNativeExecution result)
        => result is { Status: "exited", ExitCode: 0, Error: null, OutputTruncated: false, SafelyStopped: true };

    private static IncrementalTestResult Summarize(Input input, IncrementalTestOptions options,
        Selection selection, IncrementalTestState state, string status, int exitCode)
    {
        var batches = selection.Batches;
        var pendingUnit = batches.Count(batch => !batch.UnitCurrent);
        var pendingMutation = input.Profile.MutationCommand is null ? 0 : batches.Count(batch => !batch.MutationCurrent);
        var complete = pendingUnit == 0 && (options.Layer == "unit" || pendingMutation == 0)
            && !state.RecoveryRequired && state.ActiveExecutionUntilUtc is null;
        // A completed native timeout verdict is a cached miss. A timed-out invocation is unfinished work.
        var mutations = new List<TestMutationSummary>();
        foreach (var batch in batches)
        {
            if (!state.Batches.TryGetValue(batch.Id, out var saved) || saved.Mutation is not { Mutation.ValidEvidence: true } stage
                || stage.Fingerprint != batch.MutationFingerprint || stage.Execution is not { Status: "exited", Error: null, ExitCode: 0 or 2 }) continue;
            try
            {
                var report = Safe(input.Root, stage.ReportPath);
                if (File.Exists(report) && HashFile(report) == stage.ReportSha256)
                {
                    var measured = ReadMutation(input, batch.SourceFile, report).Mutation!;
                    if (measured.ValidEvidence) mutations.Add(measured);
                }
            }
            catch (Exception error) when (Expected(error)) { }
        }
        var killed = mutations.Sum(item => item.Killed);
        var denominator = mutations.Sum(item => item.Denominator);
        var score = denominator > 0 ? (double?)killed * 100 / denominator : null;
        var mutationComplete = pendingUnit == 0 && pendingMutation == 0 && input.Profile.MutationCommand is not null
            && !state.RecoveryRequired && state.ActiveExecutionUntilUtc is null;
        return new(status, exitCode)
        {
            ProfileId = input.Profile.Id,
            GraphBuildId = selection.Snapshot.Graph?.Build.Id,
            StatePath = Relative(input.Repository, input.StatePath),
            Layer = options.Layer,
            ScopeComplete = complete,
            PendingUnitBatches = pendingUnit,
            PendingMutationBatches = pendingMutation,
            Killed = killed,
            Survived = mutations.Sum(item => item.Survived),
            TimedOut = mutations.Sum(item => item.TimedOut),
            NoCoverage = mutations.Sum(item => item.NoCoverage),
            KnownDenominator = denominator,
            KnownScore = score,
            CompleteScopeScore = mutationComplete ? score : null,
            ThresholdMet = mutationComplete && score >= input.Profile.BreakThreshold && mutations.All(item => item.NoCoverage == 0 && item.TimedOut == 0),
            Batches = batches,
            WorkReference = state.WorkReference,
            WorkCoverage = state.WorkCoverage,
        };
    }
}
