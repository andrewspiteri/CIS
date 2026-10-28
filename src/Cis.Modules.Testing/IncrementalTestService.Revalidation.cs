using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    /// <summary>Revalidates completed cache entries after a reviewed, compatible testing-module update.</summary>
    public IncrementalTestResult Revalidate(IncrementalTestOptions options, string previousTestingAssembly, string reason,
        bool legacySessionEnvironment = false)
    {
        try
        {
            Require(reason is { Length: > 0 and <= 1024 } && !string.IsNullOrWhiteSpace(reason), "A reviewed compatibility reason is required.");
            Require(!options.Force, "Cache revalidation cannot force execution.");
            var input = Read(options);
            Require(Path.IsPathFullyQualified(previousTestingAssembly) && File.Exists(previousTestingAssembly)
                && !CisPathSafety.IsUnderRoot(input.Repository, previousTestingAssembly, allowRoot: true)
                && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(previousTestingAssembly)!, previousTestingAssembly),
                "The previous installed testing assembly must be an existing absolute, unlinked path outside the target repository.");
            var oldHash = HashFile(previousTestingAssembly);
            var previousHelper = Path.Combine(Path.GetDirectoryName(previousTestingAssembly)!, "Cis.Abstractions.dll");
            Require(File.Exists(previousHelper) && !CisPathSafety.IsUnderRoot(input.Repository, previousHelper, allowRoot: true)
                && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(previousHelper)!, previousHelper),
                "The previous installed helper must be beside the previous testing assembly.");
            var oldHelperHash = HashFile(previousHelper);
            var oldInput = Read(options, oldHash, oldHelperHash, legacySessionEnvironment);
            Directory.CreateDirectory(Path.GetDirectoryName(Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock"))!);
            using var lease = new FileStream(Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var state = State(input);
            Require(state.ActiveExecutionUntilUtc is null, "An active native invocation must finish before cache revalidation.");
            var old = Select(oldInput, state, false);
            var current = Select(input, state, false);
            var pending = old.Batches.Where(batch => state.RecoveryRequired && batch.SourceFile == state.RecoverySourceFile
                && state.Batches.TryGetValue(batch.Id, out var saved) && RecoverableNative(oldInput, batch, saved, oldHash, oldHelperHash))
                .Select(batch => batch.Id).ToHashSet(StringComparer.Ordinal);
            Require(!state.RecoveryRequired || pending.Count == 1,
                "Pending recovery requires an unchanged admissible completed native report before producer revalidation.");
            var units = 0;
            var mutations = 0;
            var awaitingRecovery = 0;
            foreach (var batch in current.Batches)
            {
                var prior = old.Batches.Single(item => item.Id == batch.Id);
                if (!state.Batches.TryGetValue(batch.Id, out var saved)) continue;
                var diagnostic = "Compatible completed cache revalidated from testing producer " + oldHash
                    + (legacySessionEnvironment ? " using the original full inherited environment" : "") + ": " + reason;
                if (prior.UnitCurrent && saved.Unit is { Completed: true })
                {
                    saved = saved with { Unit = RevalidateProducer(saved.Unit, batch.UnitFingerprint) };
                    units++;
                }
                if (prior.UnitCurrent && prior.MutationCurrent && saved.Mutation is { Completed: true })
                {
                    saved = saved with { Mutation = RevalidateProducer(saved.Mutation, batch.MutationFingerprint) };
                    mutations++;
                }
                else if (pending.Contains(batch.Id))
                {
                    saved = saved with { Mutation = RevalidateProducer(saved.Mutation!, batch.MutationFingerprint, false) };
                    awaitingRecovery++;
                }
                state.Batches[batch.Id] = saved;

                IncrementalStageResult RevalidateProducer(IncrementalStageResult stage, string fingerprint, bool addDiagnostic = true)
                {
                    var history = stage.ProducerRevalidations;
                    if (stage.Fingerprint != fingerprint)
                    {
                        Require(history is { Length: < 8 }, "Producer revalidation history reached its eight-link limit; refresh the native stage.");
                        history = [.. history, new(previousTestingAssembly, previousHelper, oldHash, oldHelperHash,
                            HashFile(typeof(IncrementalTestService).Assembly.Location), HashFile(typeof(CisProcessJob).Assembly.Location),
                            stage.Fingerprint, fingerprint, stage.ReportSha256,
                            AmbientEnvironmentIdentity(input.Profile, legacySessionEnvironment), AmbientEnvironmentIdentity(input.Profile, false),
                            legacySessionEnvironment, reason, DateTimeOffset.UtcNow)];
                    }
                    return stage with { Fingerprint = fingerprint, ProducerRevalidations = history,
                        Diagnostics = addDiagnostic ? stage.Diagnostics.Append(diagnostic).Distinct(StringComparer.Ordinal).ToArray() : stage.Diagnostics };
                }
            }
            // Reports and native execution records are unchanged. Failed/incomplete results stay held.
            Save(input, state);
            var final = Select(Read(options), state, false);
            var result = Summarize(input, options, final, state, "cache-revalidated", 0) with
            {
                ReusedUnitBatches = units, ReusedMutationBatches = mutations, RetainedBytes = Bytes(input.Storage),
                Diagnostics = [$"Revalidated {units} unit and {mutations} mutation results; {awaitingRecovery} completed native reports still await recovery. {reason}"],
            };
            SaveLatest(input, result);
            return result;
        }
        catch (Exception error) when (Expected(error)) { return new("revalidation-failed", 4) { Diagnostics = [error.Message] }; }
    }
}
