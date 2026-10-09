using Cis.Modules.Graph;
using Microsoft.Data.Sqlite;

namespace Cis.Modules.Graph.Tests;

public sealed partial class GraphBuilderTests
{
    [Fact]
    public void Build_MigratesEvidenceLookupIndexesWithoutLosingGraphData()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var store = new SqliteGraphStore();
        var original = store.Read(repository.Path);
        Assert.True(original.Success);
        ExecuteSql(repository.Path, """
            DROP INDEX IF EXISTS ix_node_evidence_id;
            DROP INDEX IF EXISTS ix_edge_evidence_id;
            PRAGMA user_version=5;
            """);

        Assert.Equal(0, CreateBuilder().Build(repository.Path, refresh: true).ExitCode);
        var migrated = store.Read(repository.Path);
        Assert.True(migrated.Success);
        Assert.Equal(original.Graph!.Nodes.Select(node => node.Key), migrated.Graph!.Nodes.Select(node => node.Key));
        Assert.Equal(original.Graph.Edges.Select(edge => edge.Key), migrated.Graph.Edges.Select(edge => edge.Key));
        Assert.Equal(0, CreateValidator().Validate(repository.Path, strict: true).ExitCode);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store.DatabasePath(repository.Path), ForeignKeys = true, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN DELETE FROM evidence WHERE id=1;";
        using var reader = command.ExecuteReader();
        var steps = new List<string>();
        while (reader.Read()) steps.Add(reader.GetString(3));
        // Deleting each obsolete evidence row must probe its child links by index,
        // rather than scanning both full link tables for every removed row.
        Assert.Contains(steps, step => step.Contains("SEARCH node_evidence USING COVERING INDEX ix_node_evidence_id", StringComparison.Ordinal));
        Assert.Contains(steps, step => step.Contains("SEARCH edge_evidence USING COVERING INDEX ix_edge_evidence_id", StringComparison.Ordinal));
        Assert.DoesNotContain(steps, step => step.Contains("SCAN node_evidence", StringComparison.Ordinal)
            || step.Contains("SCAN edge_evidence", StringComparison.Ordinal));
    }
}
