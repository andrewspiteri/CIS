using System.Diagnostics;
using Example.Core;
using Example.Persistence;
using Npgsql;

namespace Example.BusinessTests;

public sealed class CliRestartAcceptanceTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Fact]
    [Trait("Layer", "business")]
    public async Task SeparateCliProcessesResumePartialReplayWithoutDuplicateEffects()
    {
        const int records = 500;
        var readings = Enumerable.Range(0, records).Select(index => new Reading(
            new Guid(index + 1, 0, 0, new byte[8]), DateTimeOffset.UnixEpoch.AddSeconds(index),
            (index + 1) * 0.00000001m)).ToArray();
        var evidence = new BusinessEvidence(output);
        var partialPath = await evidence.WriteAsync("first-process-input.json", readings.Take(records / 2));
        var replayPath = await evidence.WriteAsync("restarted-process-input.json", readings.Reverse().Concat(readings));

        // Both process lifetimes, persistence and final readback are inside the budget.
        // Database startup, input generation and final report writing are outside it.
        var clock = Stopwatch.StartNew();
        var first = await CliReplay.RunAsync(evidence.ExampleDirectory, partialPath, database.ConnectionString);
        Assert.Equal(1, first.SchemaVersion);
        Assert.Equal("synthetic", first.Mode);
        Assert.Equal(records / 2, first.Accepted);
        Assert.Equal(records / 2, first.Stored);
        Assert.Equal(readings.Take(records / 2).Sum(reading => reading.Quantity), first.Quantity);

        // RunAsync waits for the first OS process to exit before creating the next one.
        var restarted = await CliReplay.RunAsync(evidence.ExampleDirectory, replayPath, database.ConnectionString);
        Assert.Equal(1, restarted.SchemaVersion);
        Assert.Equal("synthetic", restarted.Mode);
        Assert.Equal(records / 2, restarted.Accepted);
        Assert.Equal(records, restarted.Stored);
        Assert.Equal(readings.Sum(reading => reading.Quantity), restarted.Quantity);
        await using var readback = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(readback);
        Assert.Equal(readings, await store.ReadAllAsync(CancellationToken.None));
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "The 500-reading CLI process restart exceeded its 60-second budget.");

        await evidence.WriteAsync("cli-restart-result.json", new
        {
            schemaVersion = 1,
            workload = "synthetic-500-reading-cli-restart-v1",
            records,
            firstProcess = first,
            restartedProcess = restarted,
            wallSeconds = clock.Elapsed.TotalSeconds,
            budgetSeconds = 60,
            fullReadback = true,
            status = "passed",
            limitations = "Orderly process exit and restart with the database retained; not forced termination or database crash recovery.",
        });
    }
}
