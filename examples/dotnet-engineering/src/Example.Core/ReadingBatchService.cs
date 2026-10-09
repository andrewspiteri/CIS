using Microsoft.Extensions.Logging;

namespace Example.Core;

public sealed partial class ReadingBatchService(IReadingStore store, ProcessingTelemetry telemetry, ILogger<ReadingBatchService> logger)
{
    public async Task<int> ApplyAsync(IEnumerable<Reading> readings, string runId, CancellationToken cancellationToken)
    {
        using var activity = telemetry.Activities.StartActivity("readings.apply");
        activity?.SetTag("run.id", runId);
        var accepted = 0;
        foreach (var reading in readings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reading.Validate();
            if (await store.AppendAsync(reading, cancellationToken).ConfigureAwait(false))
            {
                accepted++;
                telemetry.Accepted(runId);
            }
        }
        BatchApplied(logger, runId, accepted);
        return accepted;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Run {RunId} accepted {Count} readings")]
    private static partial void BatchApplied(ILogger logger, string runId, int count);
}
