using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private bool RecoverableNative(Input input, IncrementalTestBatch batch, IncrementalBatchState saved,
        string? testing = null, string? helper = null)
    {
        var stage = saved.Mutation;
        if (stage is not { Completed: false, Status: "failed" or "recovering" }
            || stage.Fingerprint != batch.MutationFingerprint || stage.Diagnostics.Contains(ChangedExecutionInputs, StringComparer.Ordinal)
            || stage.Execution is not { Status: "exited", ExitCode: 0 or 2, Error: null, OutputTruncated: false }
            || saved.Unit is not { Execution: { } unitExecution } || !Healthy(unitExecution)
            || !Current(input, batch.SourceFile, saved.Unit, batch.UnitFingerprint, false)) return false;
        // A completed healthy recovery on a failed native stage is not a recovery-only failure.
        if (stage.Status == "failed" && stage.RecoveryExecution is { } recovery && Healthy(recovery)
            && stage.PostflightExecution is { } postflight && Healthy(postflight) && stage.PostflightPassed > 0) return false;
        try
        {
            var report = Safe(input.Root, stage.ReportPath);
            if (!File.Exists(report) || HashFile(report) != stage.ReportSha256) return false;
            var measured = ReadMutation(input, batch.SourceFile, report).Mutation;
            if (measured is not { ValidEvidence: true, RuntimeErrors: 0, Pending: 0, Unknown: 0 }
                || HashObject(measured) != HashObject(stage.Mutation)
                || PathComparer.Equals(batch.SourceFile, input.Profile.CanarySourceFile) && measured.Killed < input.Profile.MinimumCanaryKills) return false;
            var nativeHelper = RefinementProducer(input, batch, stage, true, testing, helper);
            VerifyRefinementCommand(input, batch, stage.Execution, input.Profile.MutationCommand!, false, nativeHelper);
            var unitHelper = RefinementProducer(input, batch, saved.Unit, false, testing, helper);
            VerifyRefinementCommand(input, batch, unitExecution, input.Profile.UnitCommand, false, unitHelper);
            return true;
        }
        catch (Exception error) when (Expected(error)) { return false; }
    }

    private bool ResumeRecovery(Input input, IncrementalTestBatch batch, ref IncrementalTestState state)
    {
        state.Batches.TryGetValue(batch.Id, out var saved);
        var resumable = saved is not null && RecoverableNative(input, batch, saved);
        if (resumable) RetainRecoveryAttempt(input, batch, saved!.Mutation!);
        var healthy = Recover(input, batch, ref state, out var recovery, out var postflight, out var passed, clearRecovery: !resumable);
        if (!resumable) return healthy;
        var unchanged = Unchanged(input, batch);
        var admissible = unchanged && RecoverableNative(input, batch, saved!);
        var stage = saved!.Mutation!;
        var completed = healthy && admissible;
        var diagnostics = stage.Diagnostics.Append(completed
            ? "Completed native report reused after healthy recovery and full-filter postflight; earlier failures are retained in recovery history."
            : unchanged ? "Baseline recovery failed." : ChangedExecutionInputs).Distinct(StringComparer.Ordinal).ToArray();
        state.Batches[batch.Id] = saved with
        {
            Mutation = stage with
            {
                Completed = completed,
                Status = completed ? stage.Mutation!.TimedOut > 0 ? "completed-with-timeouts" : "completed" : "failed",
                RecoveryExecution = recovery, PostflightExecution = postflight, PostflightPassed = passed, Diagnostics = diagnostics,
            },
        };
        state = state with { RecoveryRequired = !completed, RecoverySourceFile = completed ? null : batch.SourceFile };
        Save(input, state);
        return completed;
    }

    private static void RetainRecoveryAttempt(Input input, IncrementalTestBatch batch, IncrementalStageResult stage)
    {
        var path = Safe(input.Root, "recovery-history-" + batch.Id + ".json");
        var history = File.Exists(path) ? ReadJson<JsonObject>(path, 262144) : new JsonObject
        {
            ["schemaVersion"] = 1, ["sourceFile"] = batch.SourceFile, ["attempts"] = new JsonArray(),
        };
        Require(history["schemaVersion"]?.GetValue<int>() == 1 && history["sourceFile"]?.GetValue<string>() == batch.SourceFile
            && history["attempts"] is JsonArray, "Recovery history is invalid.");
        var attempts = history["attempts"]!.AsArray();
        Require(attempts.Count < 8, "Recovery history reached its eight-attempt bound; saved native work remains held.");
        Require(attempts.All(item => item is JsonObject entry && entry["attempt"] is JsonObject
            && entry["sha256"]?.GetValue<string>() == HashObject(entry["attempt"])), "Recovery history contains a changed attempt.");
        var attempt = JsonSerializer.SerializeToNode(new
        {
            stage.ReportSha256, stage.Fingerprint, stage.Status, stage.Diagnostics,
            stage.RecoveryExecution, stage.PostflightExecution, stage.PostflightPassed,
        }, Json)!;
        var digest = HashObject(attempt);
        if (attempts.Any(item => item?["sha256"]?.GetValue<string>() == digest)) return;
        attempts.Add(new JsonObject { ["sha256"] = digest, ["recordedUtc"] = DateTimeOffset.UtcNow, ["attempt"] = attempt });
        Require(JsonSerializer.SerializeToUtf8Bytes(history, Json).Length <= 262144, "Recovery history exceeds its 256 KiB bound; saved native work remains held.");
        Write(input, path, history);
    }
}
