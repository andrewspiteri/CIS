using Example.Core;
using Npgsql;

namespace Example.Persistence;

public sealed class PostgresReadingStore(NpgsqlDataSource dataSource) : IReadingStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("CREATE TABLE IF NOT EXISTS readings (id uuid PRIMARY KEY, observed_at timestamptz NOT NULL, quantity numeric(28,8) NOT NULL CHECK (quantity >= 0))");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AppendAsync(Reading reading, CancellationToken cancellationToken)
    {
        reading.Validate();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var insert = new NpgsqlCommand("INSERT INTO readings VALUES ($1, $2, $3) ON CONFLICT (id) DO NOTHING", connection, transaction);
        insert.Parameters.AddWithValue(reading.Id);
        insert.Parameters.AddWithValue(reading.ObservedAt);
        insert.Parameters.AddWithValue(reading.Quantity);
        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        if (!inserted)
        {
            await using var existing = new NpgsqlCommand("SELECT observed_at, quantity FROM readings WHERE id=$1", connection, transaction);
            existing.Parameters.AddWithValue(reading.Id);
            await using var row = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await row.ReadAsync(cancellationToken).ConfigureAwait(false) || await row.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false) != reading.ObservedAt || row.GetDecimal(1) != reading.Quantity)
                throw new InvalidOperationException("A reading identity cannot be reused with different content.");
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async Task<IReadOnlyList<Reading>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, observed_at, quantity FROM readings ORDER BY observed_at, id");
        await using var rows = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Reading>();
        while (await rows.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(rows.GetGuid(0), await rows.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false), rows.GetDecimal(2)));
        return result;
    }
}
