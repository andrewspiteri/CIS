using System.Globalization;
using System.Text.Json;
using Example.Persistence;
using Npgsql;

namespace Example.BusinessTests;

public sealed class CliPrecisionContractTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task LocalTimestampPersistsAsUtcAndReplaysWithExplicitUtcIdentity()
    {
        var evidence = new BusinessEvidence(output);
        var id = Guid.NewGuid();
        const string localText = "2026-06-15T12:30:00.123456";
        var local = DateTime.Parse(localText, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var expected = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local));
        var reading = new Dictionary<string, object?> { ["id"] = id, ["observedAt"] = localText, ["quantity"] = 1.25m };
        var path = await evidence.WriteAsync("local-input.json", new[] { reading });
        var first = await CliReplay.RunAsync(evidence.ExampleDirectory, path, database.ConnectionString, evidence);
        Assert.Equal(1, first.Accepted);
        await using var connection = NpgsqlDataSource.Create(database.ConnectionString);
        var persisted = Assert.Single(await new PostgresReadingStore(connection).ReadAllAsync(CancellationToken.None), item => item.Id == id);
        Assert.Equal(expected, persisted.ObservedAt);
        Assert.Equal(TimeSpan.Zero, persisted.ObservedAt.Offset);
        Assert.Equal(1.25m, persisted.Quantity);

        reading["observedAt"] = expected.ToString("O", CultureInfo.InvariantCulture);
        var replayPath = await evidence.WriteAsync("utc-replay.json", new[] { reading });
        var replay = await CliReplay.RunAsync(evidence.ExampleDirectory, replayPath, database.ConnectionString, evidence);
        Assert.Equal(0, replay.Accepted);
        Assert.Equal(persisted, Assert.Single(await new PostgresReadingStore(connection).ReadAllAsync(CancellationToken.None), item => item.Id == id));
        await evidence.WriteAsync("local-time-result.json", new { localTimeZone = TimeZoneInfo.Local.Id, localText, expectedUtc = expected, replay.Accepted });
    }

    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 ExactJsonFormsRemainCompatible")]
    [InlineData("1970-01-01T00:00:00.123456000Z", "1e-8", "0.00000001")]
    [InlineData("1970-01-01T00:00:00.1234560+00:00", "\"10e-9\"", "0.00000001")]
    [InlineData("1970-01-01T00:00:00.0000000000000000Z", "\"100.0000000000\"", "100")]
    [InlineData("1970-01-01T00:00:00Z", "99999999999999999999.99999999", "99999999999999999999.99999999")]
    [InlineData("1970-01-01T00:00:00Z", "1e19", "10000000000000000000")]
    [InlineData("1970-01-01T00:00:00Z", "1000000000000000000000000000000e-28", "100")]
    [InlineData("1970-01-01T00:00:00Z", "-0.00000000000000000000000000000", "0")]
    [InlineData("1970-01-01T00:00:00Z", "\"0e-999\"", "0")]
    [InlineData("1970-01-01T00:00:00Z", "\"0.00000001000\"", "0.00000001")]
    public async Task ExactJsonFormsRemainCompatible(string timestamp, string quantityJson, string expectedQuantity)
    {
        var evidence = new BusinessEvidence(output);
        var id = Guid.NewGuid();
        var reading = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["observedAt"] = timestamp,
            ["quantity"] = JsonSerializer.Deserialize<JsonElement>(quantityJson),
        };
        var path = await evidence.WriteAsync("exact-input.json", new[] { reading });
        var result = await CliReplay.RunAsync(evidence.ExampleDirectory, path, database.ConnectionString, evidence);
        Assert.Equal(1, result.Accepted);
        await using var connection = NpgsqlDataSource.Create(database.ConnectionString);
        var persisted = Assert.Single(await new PostgresReadingStore(connection).ReadAllAsync(CancellationToken.None), item => item.Id == id);
        Assert.Equal(DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture), persisted.ObservedAt);
        Assert.Equal(decimal.Parse(expectedQuantity, CultureInfo.InvariantCulture), persisted.Quantity);
    }
}
