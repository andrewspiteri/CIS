using System.Diagnostics;
using Example.Core;
using Example.Persistence;
using Example.TestSupport;
using Npgsql;

namespace Example.BusinessTests;

public sealed class ReplayAcceptanceTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Fact]
    [Trait("Layer", "business")]
    public async Task RestartReplayAndCliReadbackPreserveEveryReadingAndExactTotal()
    {
        // Representative only of this declared synthetic fixture, not of an arbitrary future product.
        const int records = 500;
        var input = Enumerable.Range(0, records).Select(index => new Reading(
            new Guid(index + 1, 0, 0, new byte[8]), DateTimeOffset.UnixEpoch.AddSeconds(index), (index + 1) * 0.00000001m)).ToArray();
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var clock = Stopwatch.StartNew();
        await using (var firstProcess = NpgsqlDataSource.Create(database.ConnectionString))
        {
            var firstStore = new PostgresReadingStore(firstProcess);
            await firstStore.InitializeAsync(CancellationToken.None);
            var first = new ReadingBatchService(firstStore, telemetry, signals);
            Assert.Equal(records / 2, await first.ApplyAsync(input.Take(records / 2), "before-restart", CancellationToken.None));
        }
        await using var restarted = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(restarted);
        var service = new ReadingBatchService(store, telemetry, signals);
        Assert.Equal(records / 2, await service.ApplyAsync(input.Reverse().Concat(input), "after-restart", CancellationToken.None));
        Assert.Equal(input, await store.ReadAllAsync(CancellationToken.None));
        var expectedTotal = input.Sum(reading => reading.Quantity);
        Assert.Equal(expectedTotal, (await store.ReadAllAsync(CancellationToken.None)).Sum(reading => reading.Quantity));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync([input[0] with { Quantity = 2 }], "identity-conflict", CancellationToken.None));
        Assert.Equal(input, await store.ReadAllAsync(CancellationToken.None));

        var evidence = new BusinessEvidence(output);
        var inputPath = await evidence.WriteAsync("readings.json", input);
        var summary = await CliReplay.RunAsync(evidence.ExampleDirectory, inputPath, database.ConnectionString);
        Assert.Equal(1, summary.SchemaVersion);
        Assert.Equal("synthetic", summary.Mode);
        Assert.Equal(0, summary.Accepted);
        Assert.Equal(records, summary.Stored);
        Assert.Equal(expectedTotal, summary.Quantity);
        Assert.Equal(records, signals.Accepted);
        Assert.Contains("after-restart", signals.TraceRuns);
        Assert.Contains(signals.Logs, log => log.Contains("after-restart", StringComparison.Ordinal));
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "The predeclared 500-record synthetic workflow exceeded its 60-second acceptance budget.");
        await evidence.WriteAsync("business-result.json", new
        {
            schemaVersion = 1,
            workload = "synthetic-500-reading-replay-v1",
            records,
            expectedTotal,
            wallSeconds = clock.Elapsed.TotalSeconds,
            budgetSeconds = 60,
            fullReadback = true,
            status = "passed",
            limitations = "Single local environment; no compatible performance baseline or general product-scale claim.",
        });
    }

}
