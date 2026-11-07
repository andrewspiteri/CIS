using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    internal static string[] SettlementErrors(CisAgentWorkResult result, CisAgentWorkReference reference)
        => CisNativeWorkSettlement.Complete(result, reference) ? []
            : result.Errors.Count != 0 ? result.Errors.Take(16).Select(error => error[..Math.Min(error.Length, 4096)]).ToArray()
                : ["Native work settlement remains unresolved (" + result.Status + "). Reconcile the exact retained execution before readiness or reuse."];

    private static MutationRunResult SettlementHeld(MutationRunResult result, IEnumerable<string> errors) => FailedResult(result,
        "Required native work settlement failed: " + string.Join(" ", errors)) with
    {
        Status = "settlement-required",
        NativeOutcomeStatus = result.NativeOutcomeStatus ?? result.Status,
        NativeOutcomeExitCode = result.NativeOutcomeExitCode ?? result.ExitCode,
        NativeOutcomeQualified = result.NativeOutcomeQualified ?? result.Qualified,
    };

    private static MutationRunResult HoldCurrentPilot(MutationRunResult result, ICisNativeWorkScope scope)
        => result.Pilot is { } pilot && pilot.WorkReference == scope.Reference ? result with
        {
            Pilot = pilot with
            {
                NativeReady = pilot.NativeReady ?? pilot.Ready,
                Ready = false,
                Status = "settlement-required",
                NextAction = "Reconcile the original pilot work and storage settlement; retained native observations do not need another execution."
            }
        } : result;

    private static string[] PilotPublicationPaths(string repository, NativeCorrectnessPilotResult pilot)
        => (pilot.SummaryPath is null ? [] : new[] { Resolve(repository, pilot.SummaryPath) })
            .Concat(pilot.Observations.Where(item => item.OutputPath is not null)
                .Select(item => Path.Combine(Path.GetDirectoryName(Resolve(repository, item.OutputPath!))!, "summary.json")))
            .Where(File.Exists).Distinct(CisPathSafety.PlatformComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();

    // One bounded finalization under the existing native lease and exact storage grant.
    // The provisional files remain gated by the durable settlement reference, including
    // after a crash. A failed Finish never needs another native execution to repair them.
    private static MutationRunResult FinishResult(string repository, MutationRunResult result,
        ICisNativeWorkScope? scope, ICisOwnedStorageController? storage, string status)
    {
        if (scope is null) return result;
        var paths = new List<string>();
        if (result.SummaryPath is { } summary) paths.Add(Resolve(repository, summary));
        if (result.ProfileId is { } id && result.RunId is not null)
            paths.Add(Resolve(repository, LocalRoot + "/" + id + "/latest-run.json"));
        if (result.ReportPath is { } report && File.Exists(Resolve(repository, report) + ".assessment.json"))
            paths.Add(Resolve(repository, report) + ".assessment.json");
        var qualification = result.ProfileId is null ? null : Resolve(repository, LocalRoot + "/" + result.ProfileId + "/last-qualification.json.assessment.json");
        if (qualification is not null && File.Exists(qualification) && SidecarRun(qualification) == result.RunId) paths.Add(qualification);
        if (result.Pilot is { } ownPilot && ownPilot.WorkReference == scope.Reference)
            paths.AddRange(PilotPublicationPaths(repository, ownPilot));
        NativeStorageReservation? reservation = null;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result with { WorkControl = null }, Json).LongLength;
            var growth = checked((bytes + paths.Where(File.Exists).Sum(path => new FileInfo(path).Length) + 131072) * 3);
            reservation = paths.Count == 0 ? null : BeginEvidenceMutation(scope, storage, paths, growth);
            // Publish the native candidate while this window is still charged. Readers
            // must resolve its settlement reference; a crash cannot turn it into readiness.
            WriteFinalResult(repository, result, paths);
            var control = scope.Finish(status, "Native mutation result: " + result.Status,
                result.SummaryPath is { } retained ? [retained] : []);
            var errors = SettlementErrors(control, scope.Reference);
            result = (errors.Length == 0 ? result : HoldCurrentPilot(SettlementHeld(result, errors), scope)) with { WorkControl = control };
            if (errors.Length != 0) WriteFinalResult(repository, result, paths);
            var settled = SettleNativeStorage(reservation, storage, true, paths);
            if (settled is { Allowed: false })
            {
                result = HoldCurrentPilot(SettlementHeld(result, settled.Errors), scope);
                WriteFinalResult(repository, result, paths);
            }
            return result;
        }
        catch (Exception error) when (Expected(error))
        {
            // Do not let Dispose turn an unfinished admission/publication boundary
            // into a successful preparation-ended settlement after this return.
            if (result.ExitCode == 0 || result.NativeExecution is not null || result.Pilot?.WorkReference == scope.Reference)
                scope.MarkUncertain("Native result finalization did not complete: " + error.Message);
            result = HoldCurrentPilot(SettlementHeld(result, [error.Message]), scope);
            // Only paths already authorized by the live reservation may be corrected.
            if (reservation is not null)
                try { WriteFinalResult(repository, result, paths); }
                catch (Exception writeError) when (Expected(writeError))
                { result = result with { Diagnostics = result.Diagnostics.Append(writeError.Message).ToArray() }; }
            return result;
        }
        finally { reservation?.Dispose(); }
    }

    private static string? SidecarRun(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.TryGetProperty("cisRun", out var run) && run.TryGetProperty("runId", out var id) ? id.GetString() : null;
    }

    private static void WriteFinalResult(string repository, MutationRunResult result, IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (result.Pilot is { } pilot && PilotPublicationPaths(repository, pilot).Any(candidate => string.Equals(candidate, path, CisPathSafety.PlatformComparison)))
                AtomicJson(path, pilot);
            else if (path.EndsWith(".assessment.json", StringComparison.Ordinal))
            {
                var node = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
                var run = node["cisRun"]!.AsObject();
                // Preserve the completed native candidate independently of accounting.
                run["nativeOutcomeQualified"] ??= run["qualified"]?.DeepClone();
                run["workSettlementRequired"] = true;
                if (result.Status == "settlement-required")
                {
                    node["status"] = "settlement-required";
                    node["mutation"]!["qualified"] = false;
                    run["qualified"] = false;
                }
                AtomicJson(path, node);
            }
            else if (Path.GetFileName(path) == "latest-run.json") AtomicJson(path, LatestState(result));
            else AtomicJson(path, result with { WorkControl = null });
        }
    }
}
