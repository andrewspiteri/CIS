using System.CommandLine;
using System.Text.Json;
using Example.Core;
using Example.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Example.Cli;

/// <summary>Composes the public command contract using native parser and output configuration.</summary>
internal static class ReadingCommand
{
    internal static RootCommand Create()
    {
        var root = new RootCommand("Synthetic reading replay. Uses an explicitly configured isolated PostgreSQL database; creates its example table.");
        var input = new Option<FileInfo>("--input") { Required = true, Description = "JSON array of readings; offset-free times use the local timezone. At most 1 MiB and 10,000 records." };
        var apply = new Command("apply", "Apply readings idempotently and emit a versioned JSON summary. Connection comes from EXAMPLE_DATABASE_CONNECTION.");
        apply.Options.Add(input);
        apply.SetAction(async (parse, cancellationToken) =>
        {
            try
            {
                var readings = await ReadingInput.ReadAsync(parse.GetValue(input)!, cancellationToken);
                var connection = Environment.GetEnvironmentVariable("EXAMPLE_DATABASE_CONNECTION");
                if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("Set EXAMPLE_DATABASE_CONNECTION for an isolated example database.");
                await using var source = NpgsqlDataSource.Create(connection);
                var store = new PostgresReadingStore(source);
                await store.InitializeAsync(cancellationToken);
                using var telemetry = new ProcessingTelemetry("Example.Cli");
                var service = new ReadingBatchService(store, telemetry, NullLogger<ReadingBatchService>.Instance);
                var accepted = await service.ApplyAsync(readings, Guid.NewGuid().ToString("N"), cancellationToken);
                var all = await store.ReadAllAsync(cancellationToken);
                await parse.InvocationConfiguration.Output.WriteLineAsync(JsonSerializer.Serialize(new { schemaVersion = 1, mode = "synthetic", accepted, stored = all.Count, quantity = all.Sum(reading => reading.Quantity) }));
                return 0;
            }
            catch (OperationCanceledException) { await parse.InvocationConfiguration.Error.WriteLineAsync("Cancelled; committed readings remain safe to replay."); return 130; }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException or NpgsqlException or InvalidOperationException)
            {
                // Provider messages can contain connection details. Keep errors bounded and credential-free.
                await parse.InvocationConfiguration.Error.WriteLineAsync("Reading application failed. Verify input, database availability and identity conflicts; committed readings can be replayed.");
                return 2;
            }
        });
        root.Subcommands.Add(apply);
        return root;

    }
}
