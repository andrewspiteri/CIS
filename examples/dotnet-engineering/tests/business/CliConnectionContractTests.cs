using Example.Persistence;
using Npgsql;

namespace Example.BusinessTests;

public sealed class CliConnectionContractTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrRejectedDatabaseCredentialsHaveBoundedFailureAndNoMutation(bool rejectedCredentials)
    {
        var evidence = new BusinessEvidence(output);
        var input = await evidence.WriteAsync("valid-control.json", new[] { CliInputCases.ValidReading() });
        await CliReplay.RunAsync(evidence.ExampleDirectory, input, database.ConnectionString, evidence);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var store = new PostgresReadingStore(source);
        var before = await store.ReadAllAsync(CancellationToken.None);
        var next = await evidence.WriteAsync("next-input.json", new[] { CliInputCases.ValidReading() });
        var deniedConnection = rejectedCredentials
            ? new NpgsqlConnectionStringBuilder(database.ConnectionString) { Password = Guid.NewGuid().ToString("N") }.ConnectionString
            : null;

        var result = await CliProcess.RunAsync(evidence.ExampleDirectory, ["apply", "--input", next], deniedConnection, evidence);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.True(result.StandardError.Trim() == "Reading application failed. Verify input, database availability and identity conflicts; committed readings can be replayed.",
            "Missing or rejected credentials must not reveal connection or provider details.");
        Assert.Equal(before, await store.ReadAllAsync(CancellationToken.None));
    }
}
