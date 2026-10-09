using Example.Persistence;
using Npgsql;

namespace Example.BusinessTests;

public sealed class CliInputContractTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 InvalidInputIsRejectedWithoutChangingPersistence")]
    [InlineData("missing-id")]
    [InlineData("missing-observedAt")]
    [InlineData("missing-quantity")]
    [InlineData("null-id")]
    [InlineData("null-observedAt")]
    [InlineData("null-quantity")]
    [InlineData("empty-id")]
    [InlineData("offset-time")]
    [InlineData("sub-microsecond")]
    [InlineData("long-time-fraction")]
    [InlineData("long-time-tail")]
    [InlineData("quantity-rounding")]
    [InlineData("quantity-underflow")]
    [InlineData("quoted-quantity-rounding")]
    [InlineData("quoted-quantity-underflow")]
    [InlineData("quantity-exponent-underflow")]
    [InlineData("quoted-quantity-exponent-underflow")]
    [InlineData("negative-quantity")]
    [InlineData("excess-precision")]
    [InlineData("excess-range")]
    [InlineData("null-entry")]
    [InlineData("null-root")]
    [InlineData("object-root")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    [InlineData("too-many-records")]
    [InlineData("valid-then-invalid")]
    [InlineData("absent-file")]
    [InlineData("directory-input")]
    [Trait("Layer", "business")]
    [Trait("Layer", "security")]
    public async Task InvalidInputIsRejectedWithoutChangingPersistence(string scenario)
    {
        var evidence = new BusinessEvidence(output);
        var valid = CliInputCases.ValidReading();
        var validPath = await evidence.WriteAsync("valid-control.json", new[] { valid });
        var control = await CliReplay.RunAsync(evidence.ExampleDirectory, validPath, database.ConnectionString, evidence);
        Assert.Equal(1, control.SchemaVersion);
        Assert.Equal("synthetic", control.Mode);
        Assert.Equal(1, control.Accepted);
        await using var connection = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(connection);
        var before = await store.ReadAllAsync(CancellationToken.None);

        // Each malformed record has a fresh identity: a duplicate must not mask invalid-input acceptance.
        var invalidPath = Path.Combine(evidence.DirectoryPath, "invalid-input.json");
        if (scenario == "directory-input")
            Directory.CreateDirectory(invalidPath);
        else if (scenario != "absent-file")
            await File.WriteAllTextAsync(invalidPath, CliInputCases.RejectedPayload(scenario), TestContext.Current.CancellationToken);
        await CliReplay.ExpectRejectedAsync(evidence.ExampleDirectory, invalidPath, database.ConnectionString, evidence);
        Assert.Equal(before, await store.ReadAllAsync(CancellationToken.None));
    }

    [Fact]
    [Trait("Layer", "security")]
    public async Task ConnectionFailureDoesNotDiscloseConnectionDetails()
    {
        var evidence = new BusinessEvidence(output);
        var input = await evidence.WriteAsync("valid-input.json", new[] { CliInputCases.ValidReading() });
        const string syntheticConnection = "Host=127.0.0.1;Port=1;Username=synthetic;Password=synthetic-connection-marker;Database=synthetic;Timeout=1";
        await CliReplay.ExpectRejectedAsync(evidence.ExampleDirectory, input, syntheticConnection, evidence);
    }
}
