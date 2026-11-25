using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class TestingModule
{
    private static int RenderIncrementalSummary(IncrementalTestResult result)
    {
        WriteIncrementalSummary(result.Status, result.ExitCode, result.ProfileId, result.Layer, result.ScopeComplete,
            result.Batches.Count, result.ExecutedUnitBatches, result.ExecutedMutationBatches,
            result.ReusedUnitBatches, result.ReusedMutationBatches, result.PendingUnitBatches, result.PendingMutationBatches,
            result.GraphBuildId, result.StatePath, result.Diagnostics);
        return result.ExitCode;
    }

    private static int RenderIncrementalSummary(IncrementalEvidenceResult result)
    {
        WriteIncrementalSummary(result.Status, result.ExitCode, result.ProfileId, result.Layer, result.ScopeComplete,
            result.Batches.Count, 0, 0, result.ScopeComplete ? result.Batches.Count : 0,
            result.ScopeComplete && result.Layer != "unit" ? result.Batches.Count(batch => batch.MutationReportPath is not null) : 0,
            null, null, result.GraphBuildId, null, result.Diagnostics);
        return result.ExitCode;
    }

    private static void WriteIncrementalSummary(string status, int exitCode, string? profile, string layer,
        bool complete, int selected, int executedUnit, int executedMutation, int reusedUnit, int reusedMutation,
        int? pendingUnit, int? pendingMutation, string? graph, string? state, IReadOnlyList<string> diagnostics)
    {
        int limit = 8;
        int textLimit = 256;
        while (true)
        {
            var messages = diagnostics.Take(limit).Select(message => Short(message, textLimit)).ToArray();
            var output = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = "incremental-summary",
                status = Short(status, 128),
                exitCode,
                profile = Short(profile, 128),
                layer = Short(layer, 16),
                scopeComplete = complete,
                qualified = false,
                selectedSourceBatches = selected,
                executedUnitBatches = executedUnit,
                executedMutationBatches = executedMutation,
                reusedUnitBatches = reusedUnit,
                reusedMutationBatches = reusedMutation,
                pendingUnitBatches = pendingUnit,
                pendingMutationBatches = pendingMutation,
                graphBuildId = Short(graph, 128),
                statePath = Short(state, 512),
                diagnostics = messages,
                omittedDiagnostics = Math.Max(0, diagnostics.Count - messages.Length),
                diagnosticTextTruncated = diagnostics.Any(message => message.Length > textLimit),
                nextAction = exitCode is not (0 or 3) ? "Inspect blockers and retained records before continuing."
                    : complete ? "Retain compatible evidence; no native rerun is needed for unchanged inputs."
                    : "Inspect the pending configured scope before executing it under its current owner.",
                detail = "Use test incremental status or verify with the same profile and without --summary for complete records."
            });
            if (output.Length + Environment.NewLine.Length <= 8000
                && Encoding.UTF8.GetByteCount(output + Environment.NewLine) <= 32000)
            {
                Console.WriteLine(output);
                return;
            }
            limit /= 2;
            textLimit = Math.Max(32, textLimit / 2);
        }
    }

    private static string? Short(string? value, int limit)
        => value is null || value.Length <= limit ? value : value[..limit] + "…";
}
