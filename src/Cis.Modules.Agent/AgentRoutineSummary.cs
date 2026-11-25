using System.Text;
using System.Text.Json;

namespace Cis.Modules.Agent;

internal static class AgentRoutineSummary
{
    internal static string Render(AgentResult result, string format)
    {
        if (format is not ("human" or "agent" or "json"))
        {
            return "Unsupported output format.";
        }
        int messageLimit = 320;
        int itemLimit = 8;
        int metadataLimit = 512;
        while (true)
        {
            string? Metadata(string? value, int maximum) => Cut(value, Math.Min(maximum, metadataLimit));
            string[] blockers = result.Diagnostics.Where(value => value.StartsWith("ERROR:", StringComparison.Ordinal)).ToArray();
            string[] diagnostics = blockers.Concat(result.Diagnostics.Where(value => !value.StartsWith("ERROR:", StringComparison.Ordinal)))
                .Take(itemLimit).Select(value => Cut(value, messageLimit)!).ToArray();
            object projection = new
            {
                SchemaVersion = 1,
                Kind = "routine-summary",
                Status = Metadata(result.Status, 128),
                result.Applied,
                result.ExitCode,
                ChangeId = Metadata(result.Envelope?.ChangeId, 128),
                TaskId = Metadata(result.Envelope?.TaskId, 128),
                EnvelopeId = Metadata(result.Envelope?.Id, 256),
                CanonicalTaskPath = Metadata(result.Envelope?.CanonicalTaskPath, 512),
                ContextStatus = Metadata(result.Envelope?.TaskContext?.Status, 128),
                ContextFreshness = Metadata(result.Envelope?.TaskContext?.Freshness, 128),
                ContextOmittedItems = result.Envelope?.TaskContext?.OmittedItems,
                ContextDetailPath = Metadata(result.Envelope?.TaskContext?.DetailPath, 512),
                MetadataTextTruncated = new (string? Value, int Maximum)[] { (result.Status, 128),
                    (result.Envelope?.ChangeId, 128), (result.Envelope?.TaskId, 128), (result.Envelope?.Id, 256),
                    (result.Envelope?.CanonicalTaskPath, 512), (result.Envelope?.TaskContext?.Status, 128),
                    (result.Envelope?.TaskContext?.Freshness, 128), (result.Envelope?.TaskContext?.DetailPath, 512) }
                    .Any(item => item.Value?.Length > Math.Min(metadataLimit, item.Maximum)),
                TotalRuns = result.Runs.Count,
                Runs = result.Runs.Take(itemLimit).Select(run => new { Id = Cut(run.RunId, 128), Status = Cut(run.Status, 128), run.Attempt }).ToArray(),
                OmittedRuns = Math.Max(0, result.Runs.Count - itemLimit),
                TotalImports = result.Imports.Count,
                TotalProviders = result.Providers.Count,
                TotalDiagnostics = result.Diagnostics.Count,
                BlockerCount = blockers.Length,
                Diagnostics = diagnostics,
                OmittedDiagnostics = Math.Max(0, result.Diagnostics.Count - diagnostics.Length),
                DiagnosticTextTruncated = result.Diagnostics.Any(value => value.Length > messageLimit),
                Detail = "Use agent status --format json for full records; prepared envelopes remain under .cis/local/agents/envelopes.",
            };
            string output = JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (output.Length + Environment.NewLine.Length <= 12_000 && Encoding.UTF8.GetByteCount(output + Environment.NewLine) <= 48_000)
            {
                return output;
            }
            itemLimit = Math.Max(0, itemLimit / 2);
            messageLimit = Math.Max(32, messageLimit / 2);
            metadataLimit = Math.Max(32, metadataLimit / 2);
        }
    }

    private static string? Cut(string? value, int maximum)
    {
        if (value is null || value.Length <= maximum)
        {
            return value;
        }
        int end = char.IsHighSurrogate(value[maximum - 1]) ? maximum - 1 : maximum;
        return value[..end] + "…";
    }
}
