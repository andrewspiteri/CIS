using Testcontainers.PostgreSql;

namespace Example.BusinessTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16.8-alpine").Build();
    public string ConnectionString => _database.GetConnectionString();
    public async ValueTask InitializeAsync() => await _database.StartAsync();
    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
}
