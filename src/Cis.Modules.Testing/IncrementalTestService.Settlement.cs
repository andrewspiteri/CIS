using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private string[] StateSettlementErrors(string repository, IncrementalTestState state, string? latestPath = null)
    {
        var references = new HashSet<CisAgentWorkReference>();
        if (state.WorkReference is { } original) references.Add(original);
        if (latestPath is not null && File.Exists(latestPath))
        {
            var latest = ReadJson<IncrementalTestResult>(latestPath, 16 * 1024 * 1024);
            if (latest.WorkReference is { } metadata) references.Add(metadata);
        }
        foreach (var reference in references)
        {
            var result = _nativeWork?.InspectSettlement(state.WorkAuthorityPath ?? repository, reference);
            var errors = result is null ? ["The retained native owner settlement cannot be inspected; use the composed authority service."]
                : MutationRunService.SettlementErrors(result, reference);
            if (errors.Length > 0) return errors;
        }
        return [];
    }

    private static IncrementalTestResult IncrementalSettlementHeld(IncrementalTestResult result, IEnumerable<string> errors)
        => result with
        {
            Status = "settlement-required",
            ExitCode = 4,
            ScopeComplete = false,
            Diagnostics = result.Diagnostics.Concat(errors).Distinct(StringComparer.Ordinal).ToArray()
        };

    private static IncrementalTestResult FinishIncrementalResult(Input? input, IncrementalTestState? state,
        IncrementalTestResult result, ICisNativeWorkScope? scope, string status, CisAgentWorkResult? admissionControl)
    {
        MutationRunService.NativeStorageReservation? publication = null;
        var publish = input is not null && state is not null && Directory.Exists(input.Root);
        var paths = !publish ? [] : new[] { input!.StatePath, Safe(input.Root, "latest-run.json") };
        try
        {
            if (input is not null)
            {
                var storage = FinishNativeStorage(input, state);
                if (storage is { Allowed: false }) result = IncrementalSettlementHeld(result, storage.Errors);
                if (input.SettlementErrors.Count > 0) result = IncrementalSettlementHeld(result, input.SettlementErrors);
                if (scope is not null && publish)
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(result with { WorkControl = null }, Json).LongLength
                        + (state is null ? 0 : JsonSerializer.SerializeToUtf8Bytes(state, Json).LongLength);
                    publication = MutationRunService.BeginEvidenceMutation(scope, input.OwnedStorage, paths, checked((bytes + 131072) * 3));
                }
                if (publish) WriteSettledIncremental(input, state, result);
            }
            var control = scope?.Finish(status, "Native incremental execution: " + result.Status,
                result.StatePath is { } retained ? [retained] : []) ?? admissionControl;
            if (scope is not null && control is not null)
            {
                var errors = MutationRunService.SettlementErrors(control, scope.Reference);
                if (errors.Length > 0)
                {
                    result = IncrementalSettlementHeld(result, errors);
                    if (publish) WriteSettledIncremental(input!, state, result);
                }
            }
            result = result with { WorkControl = control };
            if (publish)
            {
                var storage = MutationRunService.SettleNativeStorage(publication, input!.OwnedStorage, true, paths);
                if (storage is { Allowed: false })
                {
                    result = IncrementalSettlementHeld(result, storage.Errors);
                    WriteSettledIncremental(input, state, result);
                }
            }
            return result;
        }
        catch (Exception error) when (Expected(error))
        {
            if (result.ExitCode == 0 || result.ExecutedUnitBatches > 0 || result.ExecutedMutationBatches > 0)
                scope?.MarkUncertain("Incremental result finalization did not complete: " + error.Message);
            result = IncrementalSettlementHeld(result, [error.Message]);
            if (publish && (scope is null || publication is not null))
                try { WriteSettledIncremental(input!, state, result); }
                catch (Exception writeError) when (Expected(writeError))
                { result = result with { Diagnostics = result.Diagnostics.Append(writeError.Message).ToArray() }; }
            return result;
        }
        finally { publication?.Dispose(); }
    }

    private static void WriteSettledIncremental(Input input, IncrementalTestState? state, IncrementalTestResult result)
    {
        if (state is not null) Atomic(input.StatePath, state);
        Atomic(Safe(input.Root, "latest-run.json"), result with { WorkControl = null });
        static void Atomic<T>(string path, T value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
            Require(bytes.Length <= 16 * 1024 * 1024, "Incremental finalization exceeds its retained metadata bound.");
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
    }

    private static IncrementalRefinementResult FinishRefinementResult(Input? input, IncrementalRefinementResult result,
        ICisNativeWorkScope? scope, CisAgentWorkResult? admissionControl)
    {
        if (scope is null) return result with { WorkControl = admissionControl };
        MutationRunService.NativeStorageReservation? publication = null;
        var path = input is not null && !result.RequiresBaseline && result.SidecarPath is { } relative
            ? Safe(input.Repository, relative) : null;
        IncrementalRefinementResult Held(IEnumerable<string> errors) => result with
        { Status = "settlement-required", ExitCode = 4, Diagnostics = result.Diagnostics.Concat(errors).Distinct(StringComparer.Ordinal).ToArray() };
        void WriteFinal()
        {
            if (path is null) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result with { WorkControl = null }, Json);
            Require(bytes.Length <= 1024 * 1024, "Refinement finalization exceeds its 1 MiB bound.");
            File.WriteAllBytes(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, true);
        }
        try
        {
            if (path is not null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(result with { WorkControl = null }, Json).LongLength;
                publication = MutationRunService.BeginEvidenceMutation(scope, input!.OwnedStorage, [path], checked((bytes + 131072) * 3));
                WriteFinal();
            }
            var control = scope.Finish("native-finished", "Incremental refinement metadata: " + result.Status,
                result.SidecarPath is { } retained ? [retained] : []);
            var errors = MutationRunService.SettlementErrors(control, scope.Reference);
            if (errors.Length > 0) { result = Held(errors); WriteFinal(); }
            result = result with { WorkControl = control };
            var storage = MutationRunService.SettleNativeStorage(publication, input?.OwnedStorage, true, path is null ? [] : [path]);
            if (storage is { Allowed: false }) { result = Held(storage.Errors); WriteFinal(); }
            return result;
        }
        catch (Exception error) when (Expected(error))
        {
            if (result.ExitCode == 0) scope.MarkUncertain("Refinement result finalization did not complete: " + error.Message);
            result = Held([error.Message]);
            if (publication is not null)
                try { WriteFinal(); }
                catch (Exception writeError) when (Expected(writeError))
                { result = result with { Diagnostics = result.Diagnostics.Append(writeError.Message).ToArray() }; }
            return result;
        }
        finally { publication?.Dispose(); }
    }
}
