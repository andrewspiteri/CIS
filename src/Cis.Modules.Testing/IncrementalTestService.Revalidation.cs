using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    /// <summary>Revalidates completed cache entries after a reviewed, compatible testing-module update.</summary>
    public IncrementalTestResult Revalidate(IncrementalTestOptions options, string previousTestingAssembly, string reason)
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
            var oldInput = Read(options, oldHash, HashFile(previousHelper));
            Directory.CreateDirectory(Path.GetDirectoryName(Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock"))!);
            using var lease = new FileStream(Safe(input.Repository, MutationRunService.LocalRoot + "/run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var state = State(input);
            Require(!state.RecoveryRequired && state.ActiveExecutionUntilUtc is null, "Baseline recovery must finish before cache revalidation.");
            var old = Select(oldInput, state, false);
            var current = Select(input, state, false);
            var units = 0;
            var mutations = 0;
            foreach (var batch in current.Batches)
            {
                var prior = old.Batches.Single(item => item.Id == batch.Id);
                if (!state.Batches.TryGetValue(batch.Id, out var saved)) continue;
                var diagnostic = "Compatible completed cache revalidated from testing producer " + oldHash + ": " + reason;
                if (prior.UnitCurrent && saved.Unit is { Completed: true })
                {
                    saved = saved with { Unit = saved.Unit with { Fingerprint = batch.UnitFingerprint, Diagnostics = saved.Unit.Diagnostics.Append(diagnostic).Distinct(StringComparer.Ordinal).ToArray() } };
                    units++;
                }
                if (prior.UnitCurrent && prior.MutationCurrent && saved.Mutation is { Completed: true })
                {
                    saved = saved with { Mutation = saved.Mutation with { Fingerprint = batch.MutationFingerprint, Diagnostics = saved.Mutation.Diagnostics.Append(diagnostic).Distinct(StringComparer.Ordinal).ToArray() } };
                    mutations++;
                }
                state.Batches[batch.Id] = saved;
            }
            // Reports and native execution records are unchanged. Failed/incomplete results stay held.
            Save(input, state);
            var final = Select(Read(options), state, false);
            var result = Summarize(input, options, final, state, "cache-revalidated", 0) with
            {
                ReusedUnitBatches = units, ReusedMutationBatches = mutations, RetainedBytes = Bytes(input.Storage),
                Diagnostics = [$"Revalidated {units} unit and {mutations} mutation results. {reason}"],
            };
            SaveLatest(input, result);
            return result;
        }
        catch (Exception error) when (Expected(error)) { return new("revalidation-failed", 4) { Diagnostics = [error.Message] }; }
    }
}
