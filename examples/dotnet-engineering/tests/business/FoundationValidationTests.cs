using Example.Core;
using Example.Persistence;
using Npgsql;

namespace Example.BusinessTests;

public sealed class FoundationValidationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 DirectAdapterRejectsInvalidValuesWithoutChangingExistingReadings")]
    [InlineData("identity")]
    [InlineData("offset")]
    [InlineData("precision")]
    [InlineData("negative")]
    [InlineData("scale")]
    [InlineData("range")]
    [Trait("Layer", "integration")]
    public async Task DirectAdapterRejectsInvalidValuesWithoutChangingExistingReadings(string scenario)
    {
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(source);
        var token = TestContext.Current.CancellationToken;
        await store.InitializeAsync(token);
        var valid = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMicroseconds(1), 0.12345678m);
        Assert.True(await store.AppendAsync(valid, token));
        var before = await store.ReadAllAsync(token);
        Assert.Contains(valid, before);
        var candidate = valid with { Id = Guid.NewGuid() };
        var invalid = scenario switch
        {
            "identity" => candidate with { Id = Guid.Empty },
            "offset" => candidate with { ObservedAt = candidate.ObservedAt.ToOffset(TimeSpan.FromHours(1)) },
            "precision" => candidate with { ObservedAt = candidate.ObservedAt.AddTicks(1) },
            "negative" => candidate with { Quantity = -1m },
            "scale" => candidate with { Quantity = 0.123456789m },
            _ => candidate with { Quantity = 100000000000000000000m },
        };

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => store.AppendAsync(invalid, token));
        var expectedMessage = scenario switch
        {
            "identity" => "A reading needs a stable identity.",
            "offset" => "Event time must be UTC.",
            "precision" => "Event time must have microsecond precision for exact persistence.",
            _ => "Quantity must be nonnegative, less than 10^20, with at most eight decimal places.",
        };
        Assert.StartsWith(expectedMessage, exception.Message, StringComparison.Ordinal);

        Assert.Equal(before, await store.ReadAllAsync(token));
    }
}
