using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    /// <summary>Revalidates completed cache entries after a reviewed, compatible testing-module update.</summary>
    public IncrementalTestResult Revalidate(IncrementalTestOptions options, string reason)
        => Revalidate(options, null, reason);

    public IncrementalTestResult Revalidate(IncrementalTestOptions options, string? previousTestingAssembly, string reason,
        bool legacySessionEnvironment = false)
    {
        CisNativeWorkAdmission? admission = null;
        ICisNativeWorkScope? work = null;
        FileStream? lease = null;
        Input? input = null;
        IncrementalTestState? retainedState = null;
        // Settle admitted work after any failure, but publish only while owning the native lease.
        IncrementalTestResult Complete(IncrementalTestResult result) => FinishIncrementalResult(lease is null ? null : input, retainedState,
            result with { WorkReference = admission?.Reference, WorkCoverage = admission?.Coverage ?? "unaccounted-non-adopted" },
            work, "native-finished", admission?.Control);
        try
        {
            Require(reason is { Length: > 0 and <= 1024 } && !string.IsNullOrWhiteSpace(reason) && !reason.Contains('\0'), "A reviewed compatibility reason is required.");
            Require(!options.Force, "Cache revalidation cannot force execution.");
            admission = AdmitMetadata(options, "incremental-revalidate");
            if (!admission.Allowed) return Complete(new("held", 4) { Diagnostics = admission.Errors });
            work = admission.Scope;
            input = MetadataInput(options, work);
            // An old installed assembly is an optional legacy digest-discovery
            // source. Modern results and retained legacy digest links need no old
            // installation files. Never load code from the supplied file.
            var oldHash = HashFile(typeof(IncrementalTestService).Assembly.Location);
            if (!string.IsNullOrEmpty(previousTestingAssembly) && File.Exists(previousTestingAssembly))
            {
                Require(Path.IsPathFullyQualified(previousTestingAssembly)
                    && !CisPathSafety.IsUnderRoot(input.Repository, previousTestingAssembly, allowRoot: true)
                    && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(previousTestingAssembly)!, previousTestingAssembly),
                    "An explicit legacy producer discovery file must be an absolute, unlinked installed file outside the target repository.");
                oldHash = HashFile(previousTestingAssembly);
            }
            var oldHelperHash = HashFile(typeof(CisProcessJob).Assembly.Location);
            var oldInput = Read(options, oldHash, oldHelperHash, legacySessionEnvironment);
            Directory.CreateDirectory(Path.GetDirectoryName(Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock"))!);
            lease = new FileStream(Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var state = State(input);
            retainedState = state;
            Require(StateSettlementErrors(input.Repository, state, Safe(input.Root, "latest-run.json")).Length == 0,
                "Reconcile the original native and metadata work before producer revalidation; compatibility cannot clear unsettled accounting.");
            Require(state.ActiveExecutionUntilUtc is null, "An active native invocation must finish before cache revalidation.");
            var old = Select(oldInput, state, false);
            var current = Select(input, state, false);
            var legacySelections = new Dictionary<string, (Input Input, Selection Selection)>(StringComparer.Ordinal);
            var units = 0;
            var mutations = 0;
            var awaitingRecovery = 0;
            var unprovenLegacy = 0;
            foreach (var batch in current.Batches)
            {
                if (!state.Batches.TryGetValue(batch.Id, out var saved)) continue;
                var unitProof = LegacyProof(saved.Unit);
                var mutationProof = LegacyProof(saved.Mutation);
                var unitCurrent = batch.UnitCurrent || unitProof is { } priorUnit && priorUnit.Batch.UnitCurrent;
                var mutationCurrent = batch.MutationCurrent || mutationProof is { } priorMutation && priorMutation.Batch.MutationCurrent;
                var recoveryProof = mutationProof ?? (input, batch);
                var pendingRecovery = state.RecoveryRequired && batch.SourceFile == state.RecoverySourceFile
                    && RecoverableNative(recoveryProof.Item1, recoveryProof.Item2, saved);
                if (saved.Unit?.AssessmentContractVersion == 0 && !unitCurrent) unprovenLegacy++;
                if (saved.Mutation?.AssessmentContractVersion == 0 && !mutationCurrent && !pendingRecovery) unprovenLegacy++;
                var diagnostic = "Completed native report rechecked under result-assessment contract "
                    + CisIncrementalResultContract.Version + ": " + reason;
                if (unitCurrent && saved.Unit is { Completed: true })
                {
                    saved = saved with { Unit = RevalidateProducer(saved.Unit, batch.UnitFingerprint) };
                    units++;
                }
                if (unitCurrent && mutationCurrent && saved.Mutation is { Completed: true })
                {
                    saved = saved with { Mutation = RevalidateProducer(saved.Mutation, batch.MutationFingerprint) };
                    mutations++;
                }
                else if (pendingRecovery)
                {
                    saved = saved with { Mutation = RevalidateProducer(saved.Mutation!, batch.MutationFingerprint, false) };
                    awaitingRecovery++;
                }
                state.Batches[batch.Id] = saved;

                IncrementalStageResult RevalidateProducer(IncrementalStageResult stage, string fingerprint, bool addDiagnostic = true)
                {
                    return stage with
                    {
                        AssessmentContractVersion = CisIncrementalResultContract.Version,
                        TestingProducerSha256 = HashFile(typeof(IncrementalTestService).Assembly.Location),
                        HelperProducerSha256 = stage.Execution!.CisToolAssemblySha256,
                        Fingerprint = fingerprint,
                        // Current material identity and original native producer
                        // facts replace an unbounded installation transition chain.
                        ProducerRevalidations = [],
                        Diagnostics = addDiagnostic ? stage.Diagnostics.Where(message =>
                            !message.StartsWith("Completed native report rechecked under result-assessment contract ", StringComparison.Ordinal)
                            && !message.StartsWith("Compatible completed cache revalidated from testing producer ", StringComparison.Ordinal))
                            .Append(diagnostic).ToArray() : stage.Diagnostics
                    };
                }

                (Input Input, IncrementalTestBatch Batch)? LegacyProof(IncrementalStageResult? stage)
                {
                    if (stage?.AssessmentContractVersion != 0) return null;
                    if (stage.ProducerRevalidations is null) return null;
                    var link = stage.ProducerRevalidations.LastOrDefault();
                    var testing = link?.TestingSha256 ?? oldHash;
                    var helper = link?.HelperSha256 ?? stage.Execution?.CisToolAssemblySha256 ?? oldHelperHash;
                    if (!ProducerDigest(testing) || !ProducerDigest(helper)) return null;
                    var environment = link?.EnvironmentSha256;
                    var legacyEnvironment = link is null && legacySessionEnvironment;
                    var key = testing + ":" + helper + ":" + environment + ":" + legacyEnvironment;
                    if (!legacySelections.TryGetValue(key, out var proof))
                    {
                        var priorInput = link is null && testing == oldHash && helper == oldHelperHash
                            ? oldInput : Read(options, testing, helper, legacyEnvironment, environment);
                        proof = (priorInput, ReferenceEquals(priorInput, oldInput) ? old : Select(priorInput, state, false));
                        legacySelections.Add(key, proof);
                    }
                    return (proof.Input, proof.Selection.Batches.Single(item => item.Id == batch.Id));
                }
            }
            Require(!state.RecoveryRequired || awaitingRecovery == 1,
                "Pending recovery requires an unchanged admissible completed native report before producer revalidation.");
            // Reports and native execution records are unchanged. Failed/incomplete results stay held.
            Save(input, state);
            var final = Select(Read(options), state, false);
            var result = Summarize(input, options, final, state, "cache-revalidated", 0) with
            {
                ReusedUnitBatches = units,
                ReusedMutationBatches = mutations,
                RetainedBytes = Bytes(input.Storage),
                WorkReference = admission.Reference,
                WorkCoverage = admission.Coverage,
                Diagnostics = [$"Revalidated {units} unit and {mutations} mutation results; {awaitingRecovery} completed native reports still await recovery. {reason}",
                    .. (unprovenLegacy == 0 ? Array.Empty<string>() : new[] { $"{unprovenLegacy} legacy stages remain pending because their original inputs could not be verified. Their reports are retained; establish that input identity or rerun only the unverified scope within the existing allocation." })],
            };
            SaveLatest(input, result);
            return Complete(result);
        }
        catch (Exception error) when (Expected(error)) { return Complete(new("revalidation-failed", 4) { Diagnostics = [error.Message] }); }
        finally
        {
            try { work?.Dispose(); }
            finally { lease?.Dispose(); }
        }
    }
}
