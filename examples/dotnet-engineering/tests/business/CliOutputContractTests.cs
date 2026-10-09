using System.Text.Json;
using Example.Core;
using Example.Persistence;
using Npgsql;

namespace Example.BusinessTests;

public sealed class CliOutputContractTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    private static readonly string[] VersionOneProperties = ["accepted", "mode", "quantity", "schemaVersion", "stored"];

    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 ExistingJsonConsumerReceivesExactVersionOneShapeAndValues")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingJsonConsumerReceivesExactVersionOneShapeAndValues(bool compatibleExtensions)
    {
        var evidence = new BusinessEvidence(output);
        var first = new Reading(Guid.NewGuid(), new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), 1.23456789m);
        var second = new Reading(Guid.NewGuid(), first.ObservedAt.AddMicroseconds(1), 2.5m);
        var input = await evidence.WriteAsync("contract-input.json", compatibleExtensions
            ? (object)new[] {
                new { ID = first.Id, OBSERVEDAT = first.ObservedAt, QUANTITY = "1.23456789", extra = "ignored-extension" },
                new { ID = second.Id, OBSERVEDAT = second.ObservedAt, QUANTITY = "2.5", extra = "ignored-extension" } }
            : new[] { first, second });
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(source);
        await store.InitializeAsync(CancellationToken.None);
        var before = await store.ReadAllAsync(CancellationToken.None);

        var result = await CliProcess.RunAsync(evidence.ExampleDirectory, ["apply", "--input", input], database.ConnectionString, evidence);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var json = document.RootElement;
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.Equal(VersionOneProperties, json.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("synthetic", json.GetProperty("mode").GetString());
        Assert.Equal(2, json.GetProperty("accepted").GetInt32());
        Assert.Equal(before.Count + 2, json.GetProperty("stored").GetInt32());
        Assert.Equal(before.Sum(reading => reading.Quantity) + first.Quantity + second.Quantity, json.GetProperty("quantity").GetDecimal());
        var persisted = await store.ReadAllAsync(CancellationToken.None);
        Assert.Equal(first, Assert.Single(persisted, reading => reading.Id == first.Id));
        Assert.Equal(second, Assert.Single(persisted, reading => reading.Id == second.Id));
    }
}
