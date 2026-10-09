using Example.Core;
using Example.Persistence;
using Example.TestSupport;
using Npgsql;

namespace Example.BusinessTests;

public sealed class FoundationStoreContractTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Layer", "integration")]
    [Trait("Layer", "security")]
    public async Task ExistingIdentityCannotBeOverwritten(bool changeTime)
    {
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(source);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        using var signals = new TestSignals(output);
        using var telemetry = new ProcessingTelemetry(signals.Name);
        var service = new ReadingBatchService(store, telemetry, signals);
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 0.12345678m);
        Assert.Equal(1, await service.ApplyAsync([reading], "foundation-insert", TestContext.Current.CancellationToken));
        Assert.Equal(0, await service.ApplyAsync([reading], "foundation-duplicate", TestContext.Current.CancellationToken));
        var before = await store.ReadAllAsync(TestContext.Current.CancellationToken);
        var conflicting = changeTime ? reading with { ObservedAt = reading.ObservedAt.AddSeconds(1) } : reading with { Quantity = 2m };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync([conflicting], "foundation-conflict", TestContext.Current.CancellationToken));
        Assert.Equal(before, await store.ReadAllAsync(TestContext.Current.CancellationToken));
        Assert.Contains(reading, before);
        Assert.Equal(1, signals.Accepted);
        Assert.Contains("foundation-conflict", signals.TraceRuns);
        Assert.Contains(signals.Logs, log => log.Contains("foundation-duplicate", StringComparison.Ordinal));
    }
}
