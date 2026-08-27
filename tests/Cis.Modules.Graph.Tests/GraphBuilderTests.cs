using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Context;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.Api;
using Cis.Modules.Tracker;
using Microsoft.Data.Sqlite;

namespace Cis.Modules.Graph.Tests;

public sealed class GraphBuilderTests
{
    [Fact]
    public void Build_CreatesRepositoryComponentDocumentAndReferenceNodes()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var builder = CreateBuilder();

        var result = builder.Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.True(result.NodeCount > 4);
        Assert.True(result.EdgeCount > 3);
        var graphPath = Path.Combine(repository.Path, ".cis", "local", "graph", "context.db");
        var manifestPath = Path.Combine(repository.Path, ".cis", "local", "graph", "manifest.json");
        var diagnosticsPath = Path.Combine(repository.Path, ".cis", "local", "graph", "diagnostics.json");
        Assert.True(File.Exists(graphPath));
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(diagnosticsPath));
        Assert.Contains("local/", File.ReadAllText(Path.Combine(repository.Path, ".cis", ".gitignore")));

        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "repository");
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "component" && NodeValue(node, "localId") == "orders-api");
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "document"
            && NodeValue(node, "localId").EndsWith(":reference:api-dictionary", StringComparison.Ordinal));
        var apiNode = Assert.Single(nodes, node =>
            NodeValue(node, "kind") == "reference-item"
            && NodeValue(node, "subtype") == "api-operation");
        Assert.Equal("GET /orders/{id}", NodeValue(apiNode, "label"));
        Assert.StartsWith("api-operation/", NodeValue(apiNode, "localId"), StringComparison.Ordinal);

        var edges = graph.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "declares");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "governed-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "owns");
    }

    [Fact]
    public void Build_ScopesConfigurationKeyIdentityByOwner()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.Api/Orders.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        repository.Write("src/Orders.Api/appsettings.json", "{\"AllowedHosts\":\"*\"}");
        repository.Write("src/Customers.Api/Customers.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        repository.Write("src/Customers.Api/appsettings.json", "{\"AllowedHosts\":\"*\"}");
        var initialized = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, initialized.ExitCode);
        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var configurationKeys = graph.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(node => NodeValue(node, "subtype") == "configuration-key")
            .ToArray();
        Assert.Equal(2, configurationKeys.Length);
        Assert.All(configurationKeys, node => Assert.Contains("AllowedHosts", NodeValue(node, "localId")));
        Assert.Equal(2, configurationKeys.Select(node => NodeValue(node, "localId")).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Build_IsIdempotentForUnchangedInputs()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var builder = CreateBuilder();

        var first = builder.Build(repository.Path);
        var second = builder.Build(repository.Path);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("unchanged", second.Status);
        Assert.False(second.Applied);
        Assert.Equal(first.BuildId, second.BuildId);
        Assert.Equal(first.NodeCount, second.NodeCount);
        Assert.Equal(first.EdgeCount, second.EdgeCount);
    }

    [Fact]
    public void Build_PersistsVersionedNormalizedSQLiteStateWithoutDefaultJson()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();

        var result = CreateBuilder().Build(repository.Path);
        var databasePath = new SqliteGraphStore().DatabasePath(repository.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SqliteGraphStore.DatabaseRelativePath, result.GraphPath);
        Assert.True(File.Exists(databasePath));
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis", "local", "graph", "graph.json")));
        Assert.Equal(SqliteGraphStore.StorageSchemaVersion, Convert.ToInt32(ExecuteScalar(repository.Path, "PRAGMA user_version;")));
        Assert.Equal("ok", Convert.ToString(ExecuteScalar(repository.Path, "PRAGMA integrity_check;")));
        foreach (var table in new[]
        {
            "repositories", "graph_builds", "source_inputs", "nodes", "node_facets", "node_locations",
            "evidence", "node_evidence", "edges", "edge_evidence", "diagnostics", "node_search",
        })
        {
            Assert.Equal(1L, Convert.ToInt64(ExecuteScalar(repository.Path,
                $"SELECT count(*) FROM sqlite_master WHERE name='{table}';")));
        }
    }

    [Fact]
    public void Build_MigratesOlderSQLiteStorageSchemaIdempotently()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var builder = CreateBuilder();
        Assert.Equal(0, builder.Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path, """
            DROP INDEX ix_edges_from_hash_type_state;
            DROP INDEX ix_edges_to_hash_type_state;
            DROP INDEX ix_edges_type_state;
            ALTER TABLE edges DROP COLUMN from_hash;
            ALTER TABLE edges DROP COLUMN to_hash;
            DROP TABLE node_search;
            CREATE VIRTUAL TABLE node_search USING fts5(key UNINDEXED, search_text, tokenize='unicode61');
            CREATE INDEX ix_edges_from_type_state ON edges(from_key,type,state,to_key);
            CREATE INDEX ix_edges_to_type_state ON edges(to_key,type,state,from_key);
            CREATE INDEX ix_edges_type_state ON edges(type,state,key);
            PRAGMA user_version=2;
            """);

        var rebuilt = builder.Build(repository.Path);
        var unchanged = builder.Build(repository.Path);

        Assert.Equal(0, rebuilt.ExitCode);
        Assert.True(rebuilt.Applied);
        Assert.Equal(SqliteGraphStore.StorageSchemaVersion, Convert.ToInt32(ExecuteScalar(repository.Path, "PRAGMA user_version;")));
        Assert.Equal("unchanged", unchanged.Status);
    }

    [Fact]
    public void SQLiteStore_PreservesHealthyGenerationWhenTransactionFails()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var store = new SqliteGraphStore();
        var healthyBytes = File.ReadAllBytes(store.DatabasePath(repository.Path));
        var stored = store.Read(repository.Path);
        Assert.True(stored.Success);
        var graph = stored.Graph! with
        {
            Edges = stored.Graph!.Edges.Append(new CisGraphEdge(
                "broken-edge", "depends-on", "missing::node::from", "missing::node::to",
                "discovered", "high", [], new Dictionary<string, string>())).ToArray(),
        };

        Assert.Throws<SqliteException>(() => store.Write(repository.Path, graph, stored.Manifest!, stored.Diagnostics));
        Assert.Equal(healthyBytes, File.ReadAllBytes(store.DatabasePath(repository.Path)));
        Assert.Equal(stored.Graph.Build.Id, store.Read(repository.Path).Graph!.Build.Id);
    }

    [Fact]
    public void Build_UpdatesOnlyChangedSQLiteNodeRows()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        var builder = CreateBuilder();
        Assert.Equal(0, builder.Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path, """
            CREATE TABLE test_node_updates(key TEXT NOT NULL, kind TEXT NOT NULL);
            CREATE TRIGGER test_node_update AFTER UPDATE ON nodes
            BEGIN INSERT INTO test_node_updates(key,kind) VALUES(old.key,old.kind); END;
            """);
        File.AppendAllText(Path.Combine(repository.Path, "src", "Orders.Api", "Program.cs"), "\n// changed source");

        var rebuilt = builder.Build(repository.Path);

        Assert.Equal(0, rebuilt.ExitCode);
        Assert.True(Convert.ToInt64(ExecuteScalar(repository.Path, "SELECT count(*) FROM test_node_updates;")) > 0);
        Assert.Equal(0L, Convert.ToInt64(ExecuteScalar(repository.Path, "SELECT count(*) FROM test_node_updates WHERE kind='document';")));
    }

    [Fact]
    public void SQLiteQueries_MatchSnapshotReferenceResults()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var snapshot = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path).Graph!;
        var service = CreateQueryService();

        var find = service.Find(repository.Path, new CisGraphFindRequest(null, "symbol", null, null, "OrderEndpoint", 100));
        var expectedFind = snapshot.Nodes.Where(node => node.Kind == "symbol"
            && (node.Label.Contains("OrderEndpoint", StringComparison.OrdinalIgnoreCase)
                || node.LocalId.Contains("OrderEndpoint", StringComparison.OrdinalIgnoreCase)
                || node.Properties.Any(pair => pair.Key.Contains("OrderEndpoint", StringComparison.OrdinalIgnoreCase)
                    || pair.Value.Contains("OrderEndpoint", StringComparison.OrdinalIgnoreCase))))
            .Select(node => node.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedFind, find.Nodes.Select(node => node.Key).ToArray());

        var substringFind = service.Find(repository.Path,
            new CisGraphFindRequest(null, "symbol", null, null, "derEndp", 100));
        var expectedSubstringFind = expectedFind.Where(key =>
            snapshot.Nodes.Single(node => node.Key == key).Label.Contains("derEndp", StringComparison.OrdinalIgnoreCase)
            || snapshot.Nodes.Single(node => node.Key == key).LocalId.Contains("derEndp", StringComparison.OrdinalIgnoreCase)
            || snapshot.Nodes.Single(node => node.Key == key).Properties.Any(pair =>
                pair.Key.Contains("derEndp", StringComparison.OrdinalIgnoreCase)
                || pair.Value.Contains("derEndp", StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.NotEmpty(expectedSubstringFind);
        Assert.Equal(expectedSubstringFind, substringFind.Nodes.Select(node => node.Key).ToArray());

        var root = snapshot.Nodes.Single(node => node.Kind == "component" && node.LocalId == "orders-api");
        var related = service.Related(repository.Path, new CisGraphRelatedRequest(
            root.Key, "component", [], "out", 1, 100, IncludeProposed: false));
        var expectedEdges = snapshot.Edges.Where(edge => edge.From == root.Key
            && edge.State is "declared" or "discovered" or "confirmed")
            .Select(edge => edge.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedEdges, related.Traversals.Select(item => item.Edge.Key).Order(StringComparer.Ordinal).ToArray());

        var referenceEdge = snapshot.Edges.First(edge => edge.From == root.Key
            && edge.State is "declared" or "discovered" or "confirmed");
        var trace = service.Trace(repository.Path, new CisGraphTraceRequest(
            referenceEdge.From, referenceEdge.To, null, null, [], "out", 1, 10, IncludeProposed: false));
        Assert.Contains(trace.Paths, path => path.Traversals.Count == 1 && path.Traversals[0].Edge.Key == referenceEdge.Key);
    }

    [Fact]
    public void Build_LinksApiOperationsToPermissionsProblemsConsumersAndRules()
    {
        using var repository = TemporaryRepository.CreateInitializedGovernedApi();
        var apiValidation = new ApiGovernanceService(new CisRepositoryContextResolver()).Validate(repository.Path, strict: false);
        Assert.Contains(apiValidation.Diagnostics, item => item.Code == "CIS-API-RATE-001");

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var edges = graph.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "api-rule" && NodeValue(node, "localId") == "API-AUTHZ-01");
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "openapi-operation" && NodeValue(node, "label") == "GET /orders/v1/orders/{id}");
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "api-finding" && NodeValue(node, "label").Contains("rate-limit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "authorized-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "fails-with");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "consumed-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "governed-by"
            && NodeValue(edge, "to").Contains("API-AUTHZ-01", StringComparison.Ordinal));
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "documented-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "has-finding");
    }

    [Fact]
    public void Build_ConnectsCanonicalTasksToExternalWorkItemsAndTrackerConflicts()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var taskPath = "docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md";
        repository.Write(taskPath, """
            ---
            title: "WORK-001 Implement feature"
            type: agent-task
            status: Draft
            task_status: NotStarted
            task_id: WORK-001
            task_type: core.backend.behavior
            authority: human-approved-plan
            cis:
              stable_id: tracker-test:change:cis-0001:task:work-001
            ---

            # WORK-001 Implement feature

            ## Objective

            Implement the reviewed behavior.
            """);
        var catalogPath = System.IO.Path.Combine(repository.Path, "docs", "cis", "catalog.yml");
        File.AppendAllText(catalogPath, """

              - id: tracker-test:change:cis-0001:task:work-001
                path: docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md
                type: agent-task
                status: draft
                authority: canonical
            """);
        var trackerJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        repository.Write(TrackerService.StatePath, JsonSerializer.Serialize(new[]
        {
            new TrackerMapping("github", "CIS-0001", "WORK-001", "42", "https://github.test/issues/42",
                "canonical", "remote", "2026-08-14T12:00:00Z", "linked"),
        }, trackerJson));
        repository.Write(TrackerService.ConflictsPath, JsonSerializer.Serialize(new[]
        {
            new TrackerConflict("TRACKER-001", "github", "CIS-0001", "WORK-001", "remote-changed",
                "Remote issue changed.", "canonical", "remote-new", "2026-08-14T12:05:00Z", "open",
                null, null, null, null),
        }, trackerJson));

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var edges = graph.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "external-work-item" && NodeValue(node, "localId") == "github/42");
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "tracker-conflict" && NodeValue(node, "localId") == "TRACKER-001");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "mirrored-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "has-conflict");
    }

    [Fact]
    public void Build_ExtractsImplementationTestsDependenciesAndWorkflowEvidence()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "source-file"
            && NodeValue(node, "localId") == "src/Orders.Api/OrderEndpoint.cs");
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "symbol"
            && NodeValue(node, "label") == "Orders.Api.OrderEndpoint");
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "test"
            && NodeValue(node, "label") == "GetOrderContractIsVerified");
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "dependency"
            && NodeValue(node, "subtype") == "nuget"
            && NodeValue(node, "label") == "FluentValidation");
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "workflow"
            && NodeValue(node, "label") == "build-api");

        var edges = graph.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "belongs-to");
        var nodesByKey = nodes.ToDictionary(node => NodeValue(node, "key"), StringComparer.Ordinal);
        var dependencyEdges = edges.Where(edge => NodeValue(edge, "type") == "depends-on").ToArray();
        Assert.Contains(dependencyEdges, edge =>
            NodeValue(nodesByKey[NodeValue(edge, "from")], "localId") == "orders-api-tests"
            && NodeValue(nodesByKey[NodeValue(edge, "to")], "localId") == "orders-api");
        Assert.Contains(dependencyEdges, edge =>
            NodeValue(nodesByKey[NodeValue(edge, "from")], "kind") == "workflow"
            && NodeValue(nodesByKey[NodeValue(edge, "to")], "localId") == "orders-api");

        var implementationEdge = Assert.Single(edges, edge => NodeValue(edge, "type") == "implemented-by");
        Assert.Equal("reference-item", NodeValue(nodesByKey[NodeValue(implementationEdge, "from")], "kind"));
        Assert.Equal("src/Orders.Api/Program.cs", NodeValue(nodesByKey[NodeValue(implementationEdge, "to")], "localId"));

        var verificationEdge = Assert.Single(edges, edge => NodeValue(edge, "type") == "verified-by");
        Assert.Equal("reference-item", NodeValue(nodesByKey[NodeValue(verificationEdge, "from")], "kind"));
        Assert.Equal("test", NodeValue(nodesByKey[NodeValue(verificationEdge, "to")], "kind"));
    }

    [Fact]
    public void Build_ExcludesDependencyAndReleaseArtifactDirectoriesFromImplementationGraph()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/client.ts", "export const client = true;");
        repository.Write("node_modules/vendor/index.js", "export const vendor = true;");
        repository.Write("apps/web/node_modules/nested/package.json", "{\"name\":\"nested\"}");
        repository.Write(".stryker-tmp/sandbox/Dockerfile", "FROM node:24-alpine");
        repository.Write(".stryker-tmp/sandbox/src/copied.ts", "export const mutant = true;");
        repository.Write("artifacts/release/copied.cs", "public sealed class Copied { }");
        repository.Write(".artifacts/staging/copied.ts", "export const copied = true;");
        repository.Write("_old/Legacy.cs", "public sealed class Legacy { }");
        repository.Write("build_out/release/copied.cs", "public sealed class BuildCopy { }");
        repository.Write(".github/skills-quarantine/retired-skill/scripts/retired.ts", "export const retired = true;");

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "source-file"
            && NodeValue(node, "localId") == "src/client.ts");
        Assert.DoesNotContain(nodes, node =>
            NodeValue(node, "localId").Contains("node_modules", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nodes, node =>
            NodeValue(node, "localId").Contains(".stryker-tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nodes, node =>
            NodeValue(node, "localId").Contains("artifacts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nodes, node =>
            NodeValue(node, "localId").Contains("skills-quarantine", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nodes, node =>
            NodeValue(node, "localId").Contains("_old", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nodes, node =>
            NodeValue(node, "localId").Contains("build_out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_UsesCompilerBindingForCSharpSymbolsCallsAndTestCallers()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        repository.Write(
            "src/Orders.Api/OrderServices.cs",
            "namespace Orders.Api;\n" +
            "public sealed class OrderRepository { public string Load() => \"order\"; }\n" +
            "public sealed class OrderService { private readonly OrderRepository repository = new(); " +
            "public string Execute() => repository.Load(); }");
        repository.Write(
            "tests/Orders.Api.Tests/OrderServiceTests.cs",
            "using Xunit; namespace Orders.Api.Tests; public sealed class OrderServiceTests " +
            "{ [Fact] public void CallsService() { var service = new Orders.Api.OrderService(); service.Execute(); } }");

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var nodesByKey = nodes.ToDictionary(node => NodeValue(node, "key"), StringComparer.Ordinal);
        var execute = Assert.Single(nodes, node =>
            NodeValue(node, "kind") == "symbol"
            && NodeValue(node, "subtype") == "method"
            && NodeValue(node, "label").Contains("OrderService.Execute", StringComparison.Ordinal));
        var load = Assert.Single(nodes, node =>
            NodeValue(node, "kind") == "symbol"
            && NodeValue(node, "subtype") == "method"
            && NodeValue(node, "label").Contains("OrderRepository.Load", StringComparison.Ordinal));
        Assert.Equal("compiler", execute.GetProperty("properties").GetProperty("binding").GetString());

        var calls = graph.RootElement.GetProperty("edges").EnumerateArray()
            .Where(edge => NodeValue(edge, "type") == "calls")
            .ToArray();
        Assert.Contains(calls, edge =>
            NodeValue(edge, "from") == NodeValue(execute, "key")
            && NodeValue(edge, "to") == NodeValue(load, "key")
            && NodeValue(edge, "confidence") == "high");
        Assert.Contains(calls, edge =>
            NodeValue(nodesByKey[NodeValue(edge, "from")], "kind") == "test"
            && NodeValue(nodesByKey[NodeValue(edge, "from")], "label") == "CallsService"
            && NodeValue(edge, "to") == NodeValue(execute, "key"));
        Assert.Contains(calls, edge =>
            edge.GetProperty("properties").GetProperty("scope").GetString() == "repository"
            && NodeValue(nodesByKey[NodeValue(edge, "to")], "subtype") == "constructor"
            && NodeValue(nodesByKey[NodeValue(edge, "to")], "label").Contains("OrderService", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes, node =>
            node.GetProperty("properties").TryGetProperty("assembly", out var assembly)
            && assembly.GetString() == "Cis.Repository.CompilerAnalysis");
    }

    [Fact]
    public void Build_EmitsRichRoslynFactsWithoutPersistingRawArgumentValues()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "src/Semantics/Semantics.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.Write(
            "src/Semantics/SemanticFacts.cs",
            "using System; using System.Linq;\n" +
            "namespace Semantics;\n" +
            "[AttributeUsage(AttributeTargets.Method)] public sealed class HttpGetAttribute : Attribute { }\n" +
            "[AttributeUsage(AttributeTargets.Method)] public sealed class AuthorizeAttribute : Attribute { }\n" +
            "public interface ICommandHandler { }\n" +
            "public abstract class EndpointBase { }\n" +
            "public sealed class OrderRepository { public void Add() { } }\n" +
            "public sealed class UnitOfWork { public void Commit() { } }\n" +
            "public sealed class StateMachine { public void Configure() { } }\n" +
            "public sealed class CreateOrderCommandHandler : EndpointBase, ICommandHandler {\n" +
            "  private readonly OrderRepository repository = new(); private readonly UnitOfWork unit = new(); private readonly StateMachine machine = new();\n" +
            "  [HttpGet, Authorize] public void Handle() { Console.WriteLine(\"sensitive-route-value\"); Enumerable.Empty<int>().ToArray(); machine.Configure(); repository.Add(); unit.Commit(); }\n" +
            "}\n");
        repository.Write(
            "src/Semantics/Generated.g.cs",
            "// <auto-generated />\nnamespace Semantics; public sealed class GeneratedClient { public void Send() { } }");
        var initialized = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, initialized.ExitCode);

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        var validation = CreateValidator().Validate(repository.Path, strict: false);
        Assert.True(validation.ExitCode == 0, string.Join("\n", validation.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var edges = graph.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "symbol"
            && node.GetProperty("properties").TryGetProperty("external", out var external)
            && external.GetString() == "true"
            && NodeValue(node, "label").Contains("Console.WriteLine", StringComparison.Ordinal));
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "invocation"
            && node.GetProperty("properties").GetProperty("genericArguments").GetString()!.Contains("int", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "dataflow"
            && node.GetProperty("properties").GetProperty("invariants").GetString()!.Contains("repository-before-commit", StringComparison.Ordinal));
        Assert.Contains(nodes, node => NodeValue(node, "kind") == "symbol"
            && NodeValue(node, "label").Contains("GeneratedClient", StringComparison.Ordinal)
            && node.GetProperty("facets").EnumerateArray().Any(facet => facet.GetString() == "generated"));
        var handler = Assert.Single(nodes, node => NodeValue(node, "label").Contains("CreateOrderCommandHandler.Handle", StringComparison.Ordinal));
        Assert.DoesNotContain("configuration", handler.GetProperty("properties").GetProperty("callSemantics").GetString(), StringComparison.Ordinal);
        Assert.True(nodes.Count(node => NodeValue(node, "kind") == "invocation") < edges.Count(edge => NodeValue(edge, "type") == "calls"));
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "annotated-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "authorized-by");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "implements");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "inherits");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "precedes");
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "has-dataflow");
        Assert.DoesNotContain("sensitive-route-value", graph.RootElement.GetRawText(), StringComparison.Ordinal);

        var snapshot = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path);
        Assert.Equal(0, snapshot.ExitCode);
        Assert.Contains("compiler.calls", snapshot.GraphCapabilities);
        Assert.Contains("compiler.attributes", snapshot.GraphCapabilities);
        Assert.Contains("compiler.interfaces", snapshot.GraphCapabilities);
        Assert.Contains("compiler.inheritance", snapshot.GraphCapabilities);
        Assert.Contains("compiler.external-calls", snapshot.GraphCapabilities);
        Assert.Contains("compiler.generic-arguments", snapshot.GraphCapabilities);
        Assert.Contains("compiler.invocation-arguments", snapshot.GraphCapabilities);
        Assert.Contains("compiler.generated-markers", snapshot.GraphCapabilities);
        Assert.Contains("compiler.dataflow", snapshot.GraphCapabilities);
    }

    [Fact]
    public void Build_SkipsAmbiguousCompilerCallsWithoutFailingTheGraph()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write(
            "src/Orders.Api/AmbiguousCall.cs",
            "namespace Orders.Api; public sealed class AmbiguousCall " +
            "{ public void Resolve(string value) { } public void Resolve(object value) { } " +
            "public void Execute() => Resolve(default); }");

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(0, result.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var nodes = graph.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Contains(nodes, node =>
            NodeValue(node, "kind") == "symbol"
            && NodeValue(node, "subtype") == "method"
            && NodeValue(node, "label").Contains("AmbiguousCall.Execute", StringComparison.Ordinal)
            && node.GetProperty("properties").GetProperty("binding").GetString() == "compiler");
    }

    [Fact]
    public void Build_DoesNotReplaceHealthyGraphWhenReferenceIdentitySchemaIsInvalid()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var builder = CreateBuilder();
        var first = builder.Build(repository.Path);
        var graphPath = Path.Combine(repository.Path, ".cis", "local", "graph", "context.db");
        var healthyGraph = File.ReadAllBytes(graphPath);
        var apiReferencePath = Path.Combine(
            repository.Path,
            "docs",
            "cis",
            "references",
            "api-dictionary.md");
        File.WriteAllText(
            apiReferencePath,
            File.ReadAllText(apiReferencePath).Replace("| API ID |", "| Identifier |", StringComparison.Ordinal));

        var result = builder.Build(repository.Path);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(5, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-REF-002");
        Assert.Equal(healthyGraph, File.ReadAllBytes(graphPath));
    }

    [Fact]
    public void Build_RejectsRepositoryWithoutCisConfiguration()
    {
        using var repository = TemporaryRepository.Create();

        var result = CreateBuilder().Build(repository.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-repository", result.Status);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis", "local", "graph")));
    }

    [Fact]
    public void Find_FiltersNodesAndReportsFreshness()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateQueryService();

        var result = service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "test", "xunit", "test", "GetOrder", 10));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("matched", result.Status);
        Assert.Equal("fresh", result.Freshness);
        var test = Assert.Single(result.Nodes);
        Assert.Equal("GetOrderContractIsVerified", test.Label);
    }

    [Fact]
    public void Related_ReturnsOnlyBoundedEvidenceBackedRelationships()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateQueryService();
        var contract = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "reference-item", "api-operation", null, null, 10)).Nodes);

        var result = service.Related(
            repository.Path,
            new CisGraphRelatedRequest(
                contract.Key,
                "reference-item",
                ["implemented-by", "verified-by"],
                "both",
                Depth: 1,
                Limit: 10,
                IncludeProposed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("fresh", result.Freshness);
        Assert.Equal(3, result.Nodes.Count);
        Assert.Contains(result.Nodes, node => node.Kind == "source-file");
        Assert.Contains(result.Nodes, node => node.Kind == "test");
        Assert.Equal(2, result.Traversals.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Trace_FindsDeterministicShortestEvidencePath()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateQueryService();
        var contract = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "reference-item", "api-operation", null, null, 10)).Nodes);
        var dependency = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "dependency", "nuget", null, "FluentValidation", 10)).Nodes);

        var result = service.Trace(
            repository.Path,
            new CisGraphTraceRequest(
                contract.Key,
                dependency.Key,
                "reference-item",
                "dependency",
                ["owns", "depends-on"],
                "both",
                MaxDepth: 4,
                MaxPaths: 10,
                IncludeProposed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("matched", result.Status);
        Assert.Equal("fresh", result.Freshness);
        var path = Assert.Single(result.Paths);
        Assert.Equal(3, path.Nodes.Count);
        Assert.Equal(["reference-item", "component", "dependency"], path.Nodes.Select(node => node.Kind));
        Assert.Equal(["owns", "depends-on"], path.Traversals.Select(traversal => traversal.Edge.Type));
        Assert.Equal(["in", "out"], path.Traversals.Select(traversal => traversal.Direction));
    }

    [Fact]
    public void Trace_ReportsNoPathWhenEdgeFilterExcludesConnectivity()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateQueryService();
        var contract = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "reference-item", "api-operation", null, null, 10)).Nodes);
        var dependency = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "dependency", "nuget", null, "FluentValidation", 10)).Nodes);

        var result = service.Trace(
            repository.Path,
            new CisGraphTraceRequest(
                contract.Key,
                dependency.Key,
                null,
                null,
                ["verified-by"],
                "both",
                MaxDepth: 4,
                MaxPaths: 10,
                IncludeProposed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("no-path", result.Status);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void Trace_RejectsInvalidBoundsWithoutReadingGraph()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();

        var result = CreateQueryService().Trace(
            repository.Path,
            new CisGraphTraceRequest(
                "from",
                "to",
                null,
                null,
                [],
                "both",
                MaxDepth: 0,
                MaxPaths: 10,
                IncludeProposed: false));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-query", result.Status);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis", "local", "graph")));
    }

    [Fact]
    public void Trace_BoundsEqualLengthShortestPathsAndReportsTruncation()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        repository.Write(
            "tests/Orders.Api.Tests/SecondOrderContractTests.cs",
            "using Xunit; namespace Orders.Api.Tests; public sealed class SecondOrderContractTests " +
            "{ private const string ContractId = \"orders-api:get:orders-id\"; " +
            "[Fact] public void AlternateContractVerification() { Assert.NotNull(ContractId); } }");
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateQueryService();
        var contract = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "reference-item", "api-operation", null, null, 10)).Nodes);
        var dependency = Assert.Single(service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "dependency", "nuget", null, "xunit", 10)).Nodes);

        var result = service.Trace(
            repository.Path,
            new CisGraphTraceRequest(
                contract.Key,
                dependency.Key,
                null,
                null,
                ["verified-by", "contains", "belongs-to", "depends-on"],
                "both",
                MaxDepth: 6,
                MaxPaths: 1,
                IncludeProposed: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Paths);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.Paths[0].Traversals.Count);
    }

    [Fact]
    public void Query_ReportsStaleGraphWithoutSilentlyRebuilding()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        File.AppendAllText(
            Path.Combine(repository.Path, "src", "Orders.Api", "Program.cs"),
            " // changed after graph build");

        var result = CreateQueryService().Find(
            repository.Path,
            new CisGraphFindRequest(null, "source-file", null, null, "Program.cs", 10));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("stale", result.Freshness);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-QUERY-STALE-001");
    }

    [Fact]
    public void Query_RequiresBuiltGraphAndAtLeastOneFindFilter()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var service = CreateQueryService();

        var missing = service.Find(
            repository.Path,
            new CisGraphFindRequest(null, "component", null, null, null, 10));
        var invalid = service.Find(
            repository.Path,
            new CisGraphFindRequest(null, null, null, null, null, 10));

        Assert.Equal(4, missing.ExitCode);
        Assert.Equal("graph-unavailable", missing.Status);
        Assert.Equal(2, invalid.ExitCode);
        Assert.Equal("invalid-query", invalid.Status);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis", "local", "graph")));
    }

    [Fact]
    public void Validate_AcceptsHealthyGraphAndPreservesBuildWarnings()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.Equal(
            build.Diagnostics.Count(diagnostic => diagnostic.Severity is "info" or "information"),
            build.InformationCount);

        var result = CreateValidator().Validate(repository.Path, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("valid", result.Status);
        Assert.Equal("fresh", result.Freshness);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == "error");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-REF-003" && diagnostic.Severity == "warning");
    }

    [Fact]
    public void Validate_AcceptsGovernedDocumentLifecycles()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path,
            "UPDATE nodes SET lifecycle='review-required' WHERE key=(SELECT key FROM nodes WHERE kind='document' ORDER BY key LIMIT 1);");
        ExecuteSql(repository.Path,
            "UPDATE nodes SET lifecycle='completed' WHERE key=(SELECT key FROM nodes WHERE kind='document' ORDER BY key LIMIT 1 OFFSET 1);");
        ExecuteSql(repository.Path,
            "UPDATE nodes SET lifecycle='implemented' WHERE key=(SELECT key FROM nodes WHERE kind='document' ORDER BY key LIMIT 1 OFFSET 2);");
        ExecuteSql(repository.Path,
            "UPDATE nodes SET lifecycle='archived' WHERE key=(SELECT key FROM nodes WHERE kind='document' ORDER BY key LIMIT 1 OFFSET 3);");

        var result = CreateValidator().Validate(repository.Path, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "CIS-GRAPH-VALIDATE-LIFECYCLE-001");
    }

    [Fact]
    public void Validate_UsesManagedInputHashingAndDoesNotInvalidateItsOwnDossierRoutes()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var taskPath = "docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md";
        repository.Write(taskPath, """
            ---
            title: "WORK-001 Managed delivery task"
            type: agent-task
            status: Draft
            task_status: NotStarted
            task_id: WORK-001
            authority: human-approved-plan
            cis:
              stable_id: graph-test:change:cis-0001:task:work-001
            ---

            # WORK-001 Managed delivery task
            """);
        var catalogPath = Path.Combine(repository.Path, "docs", "cis", "catalog.yml");
        File.AppendAllText(catalogPath, """

              - id: graph-test:change:cis-0001:task:work-001
                path: docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md
                type: agent-task
                status: draft
                authority: canonical
            """);
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        File.AppendAllText(Path.Combine(repository.Path, taskPath.Replace('/', Path.DirectorySeparatorChar)),
            "\n## Completion evidence\n\n- Recorded after graph build.\n");
        File.WriteAllText(catalogPath, File.ReadAllText(catalogPath)
            .Replace("status: draft\n                authority: canonical", "status: completed\n                authority: canonical", StringComparison.Ordinal));

        var result = CreateValidator().Validate(repository.Path, strict: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("fresh", result.Freshness);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is "CIS-GRAPH-VALIDATE-STALE-003" or "CIS-GRAPH-VALIDATE-LIFECYCLE-001");
    }

    [Fact]
    public void Validate_DoesNotBecomeStaleWhenANewManagedDossierIsCatalogued()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var taskPath = "docs/cis/changes/CIS-0002/agent-tasks/WORK-001.md";
        repository.Write(taskPath, """
            ---
            title: "WORK-001 New managed delivery task"
            type: agent-task
            status: Draft
            task_status: NotStarted
            task_id: WORK-001
            authority: human-approved-plan
            cis:
              stable_id: graph-test:change:cis-0002:task:work-001
            ---

            # WORK-001 New managed delivery task
            """);
        var catalogPath = Path.Combine(repository.Path, "docs", "cis", "catalog.yml");
        File.AppendAllText(catalogPath, """


              - id: graph-test:change:cis-0002:task:work-001
                path: docs/cis/changes/CIS-0002/agent-tasks/WORK-001.md
                type: agent-task
                status: draft
                authority: canonical
            """);

        var result = CreateValidator().Validate(repository.Path, strict: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("fresh", result.Freshness);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == "CIS-GRAPH-VALIDATE-STALE-003");
    }

    [Fact]
    public void Validate_ReportsStaleInputsAndStrictModeFailsWarnings()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        File.AppendAllText(
            Path.Combine(repository.Path, "src", "Orders.Api", "Program.cs"),
            " // changed after graph build");
        var validator = CreateValidator();

        var normal = validator.Validate(repository.Path, strict: false);
        var strict = validator.Validate(repository.Path, strict: true);

        Assert.Equal(0, normal.ExitCode);
        Assert.Equal("warnings", normal.Status);
        Assert.Equal("stale", normal.Freshness);
        Assert.Equal(5, strict.ExitCode);
        Assert.Contains(normal.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-VALIDATE-STALE-003");
    }

    [Fact]
    public void Validate_RejectsBrokenEdgeEndpointWithoutRebuilding()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path,
            "PRAGMA foreign_keys=OFF; UPDATE edges SET to_key='missing::component::endpoint' WHERE key=(SELECT key FROM edges ORDER BY key LIMIT 1);");

        var result = CreateValidator().Validate(repository.Path, strict: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-VALIDATE-ENDPOINT-001");
    }

    [Fact]
    public void SnapshotReader_RejectsAnOlderGraphSchemaBeforeInference()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path,
            "UPDATE graph_builds SET schema_version=1 WHERE singleton=1;");

        var result = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path);

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Errors, error => error.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RequiresExistingGraphGeneration()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();

        var result = CreateValidator().Validate(repository.Path, strict: false);

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("graph-unavailable", result.Status);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis", "local", "graph")));
    }

    [Fact]
    public void GraphDoctorCheck_ReportsMissingGraphWithBuildSuggestion()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var resolution = new CisRepositoryContextResolver().Resolve(repository.Path);

        var findings = new GraphDoctorCheck(CreateValidator()).Inspect(resolution.Context!);

        var finding = Assert.Single(findings);
        Assert.Equal("CIS-GRAPH-DOCTOR-001", finding.Code);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal("cis graph build", finding.FixCommand);
    }

    [Fact]
    public void Export_WritesPortableFormatsAndIsIdempotent()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var exporter = CreateExporter();
        var expectedMarkers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["markdown"] = "# CIS context graph export",
            ["json"] = "\"schemaVersion\"",
            ["jsonl"] = "\"recordType\":\"metadata\"",
            ["dot"] = "digraph cis_context_graph",
        };

        foreach (var pair in expectedMarkers)
        {
            var first = exporter.Export(new GraphExportRequest(repository.Path, pair.Key, null, Force: false));
            var second = exporter.Export(new GraphExportRequest(repository.Path, pair.Key, null, Force: false));

            Assert.Equal(0, first.ExitCode);
            Assert.Equal("exported", first.Status);
            Assert.True(first.Applied);
            Assert.Equal("unchanged", second.Status);
            Assert.False(second.Applied);
            var outputPath = Path.Combine(
                repository.Path,
                first.OutputPath!.Replace('/', Path.DirectorySeparatorChar));
            Assert.Contains(pair.Value, File.ReadAllText(outputPath), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Export_RequiresForceForDifferingCustomOutput()
    {
        using var repository = TemporaryRepository.CreateInitializedApiWithDeliveryEvidence();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        repository.Write("exports/context.md", "human content");
        var exporter = CreateExporter();

        var collision = exporter.Export(new GraphExportRequest(
            repository.Path,
            "markdown",
            "exports/context.md",
            Force: false));
        var forced = exporter.Export(new GraphExportRequest(
            repository.Path,
            "markdown",
            "exports/context.md",
            Force: true));

        Assert.Equal(4, collision.ExitCode);
        Assert.Equal("collision", collision.Status);
        Assert.Equal(0, forced.ExitCode);
        Assert.Equal("exported", forced.Status);
        Assert.StartsWith("# CIS context graph export", File.ReadAllText(Path.Combine(
            repository.Path,
            "exports",
            "context.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Export_NeverOverwritesGraphInputsEvenWithForce()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var sourcePath = Path.Combine(repository.Path, "src", "Orders.Api", "Program.cs");
        var original = File.ReadAllText(sourcePath);

        var result = CreateExporter().Export(new GraphExportRequest(
            repository.Path,
            "markdown",
            "src/Orders.Api/Program.cs",
            Force: true));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-request", result.Status);
        Assert.Equal(original, File.ReadAllText(sourcePath));
    }

    [Fact]
    public void GraphBuildCommand_IsRegisteredByExplicitModule()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .Build();

        var exitCode = application.Invoke(["graph", "build", "--help"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void WorkspaceGraphService_BuildsAndValidatesEveryImportedRepository()
    {
        using var workspace = TemporaryRepository.Create();
        using var first = TemporaryRepository.Create();
        using var second = TemporaryRepository.Create();
        first.Write("src/First/First.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        second.Write("src/Second/Second.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        var importer = new RepositoryImporter(new RepositoryInitializer(), registry);
        var imported = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [first.Path, second.Path],
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, imported.ExitCode);
        var graphs = new WorkspaceGraphService(registry, CreateBuilder(), CreateValidator());

        var built = graphs.Build(workspace.Path);
        var validated = graphs.Validate(workspace.Path, strict: false);

        Assert.Equal(0, built.ExitCode);
        Assert.Equal(2, built.Repositories.Count);
        Assert.All(built.Repositories, repository => Assert.True(repository.Result.Applied));
        Assert.True(built.NodeCount > 0);
        Assert.Equal(0, validated.ExitCode);
        Assert.Equal(2, validated.Repositories.Count);
        Assert.All(validated.Repositories, repository =>
            Assert.Equal("fresh", repository.Result.Freshness));
    }

    [Fact]
    public void WorkspaceGraphService_RejectsMissingWorkspaceRegistry()
    {
        using var workspace = TemporaryRepository.Create();
        var resolver = new CisRepositoryContextResolver();
        var graphs = new WorkspaceGraphService(
            new WorkspaceRegistry(resolver),
            CreateBuilder(),
            CreateValidator());

        var result = graphs.Build(workspace.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid", result.Status);
        Assert.Empty(result.Repositories);
        Assert.Contains(result.Errors, error =>
            error.Contains("workspace.yml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContextPackCommand_ResolvesImportedRepositoryIdsFromWorkspace()
    {
        using var workspace = TemporaryRepository.Create();
        using var first = TemporaryRepository.CreateInitializedApi();
        using var second = TemporaryRepository.CreateInitializedApi();
        var resolver = new CisRepositoryContextResolver();
        var registry = new WorkspaceRegistry(resolver);
        var importer = new RepositoryImporter(new RepositoryInitializer(), registry);
        var imported = importer.Import(new RepositoryImportRequest(
            workspace.Path,
            "docs/cis",
            [first.Path, second.Path],
            DryRun: false,
            Confirmed: true));
        Assert.Equal(0, imported.ExitCode);
        Assert.Equal(0, CreateBuilder().Build(first.Path).ExitCode);
        Assert.Equal(0, CreateBuilder().Build(second.Path).ExitCode);
        var registered = registry.Resolve(workspace.Path).Workspace!.Repositories;
        var firstId = registered.Single(repository => repository.RepositoryPath == first.Path).Id;
        var secondId = registered.Single(repository => repository.RepositoryPath == second.Path).Id;
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new ContextModule())
            .Build();

        var exitCode = application.Invoke([
            "context", "pack",
            "--workspace", workspace.Path,
            "--repo", firstId,
            "--id", "orders-api",
            "--kind", "component",
            "--root", $"{secondId}#orders-api#component",
            "--format", "agent",
        ]);

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(Path.Combine(first.Path, ".cis", "local", "context")));
        Assert.False(Directory.Exists(Path.Combine(second.Path, ".cis", "local", "context")));
    }

    [Fact]
    public void GraphAndContextQueryCommands_AreRegisteredByExplicitModules()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new ContextModule())
            .Build();

        Assert.Equal(0, application.Invoke(["graph", "find", "--help"]));
        Assert.Equal(0, application.Invoke(["graph", "related", "--help"]));
        Assert.Equal(0, application.Invoke(["graph", "trace", "--help"]));
        Assert.Equal(0, application.Invoke(["graph", "export", "--help"]));
        Assert.Equal(0, application.Invoke(["graph", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "contract", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "symbol", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "tests-for", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "search", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "references", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "callers", "--help"]));
        Assert.Equal(0, application.Invoke(["context", "pack", "--help"]));
    }

    private static GraphBuilder CreateBuilder()
    {
        var resolver = new CisRepositoryContextResolver();
        var reader = new DocumentationCatalogReader();
        var inspector = new MarkdownDocumentInspector();
        var inventory = new DocumentationInventoryService(resolver, reader, inspector);
        var validation = new DocumentationValidationService(resolver, reader, inventory, inspector);
        return new GraphBuilder(resolver, reader, inventory, validation);
    }

    private static GraphQueryService CreateQueryService()
        => new(new CisRepositoryContextResolver());

    private static GraphValidator CreateValidator()
        => new(new CisRepositoryContextResolver());

    private static GraphExporter CreateExporter()
    {
        var resolver = new CisRepositoryContextResolver();
        return new GraphExporter(resolver, new GraphValidator(resolver));
    }

    private static JsonDocument ReadGraphJson(string repositoryPath)
    {
        var snapshot = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repositoryPath);
        Assert.Equal(0, snapshot.ExitCode);
        Assert.NotNull(snapshot.Graph);
        return JsonDocument.Parse(JsonSerializer.Serialize(snapshot.Graph, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }

    private static void ExecuteSql(string repositoryPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={new SqliteGraphStore().DatabasePath(repositoryPath)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(string repositoryPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={new SqliteGraphStore().DatabasePath(repositoryPath)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static string NodeValue(JsonElement element, string property)
        => element.GetProperty(property).GetString() ?? string.Empty;

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cis-graph-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public static TemporaryRepository CreateInitializedApi()
        {
            var repository = Create();
            repository.Write(
                "src/Orders.Api/Orders.Api.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            repository.Write(
                "src/Orders.Api/Program.cs",
                "var app = builder.Build(); app.MapGet(\"/orders/{id}\", () => Results.Ok());");
            var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
                repository.Path,
                "docs/cis",
                DryRun: false,
                Confirmed: true));
            Assert.Equal(0, result.ExitCode);
            return repository;
        }

        public static TemporaryRepository CreateInitializedApiWithDeliveryEvidence()
        {
            var repository = Create();
            repository.Write(
                "src/Orders.Api/Orders.Api.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" +
                "<PackageReference Include=\"FluentValidation\" Version=\"12.0.0\" />" +
                "</ItemGroup></Project>");
            repository.Write(
                "src/Orders.Api/Program.cs",
                "var app = builder.Build(); app.MapGet(\"/orders/{id}\", () => Results.Ok());");
            repository.Write(
                "src/Orders.Api/OrderEndpoint.cs",
                "namespace Orders.Api;\npublic sealed class OrderEndpoint { }");
            repository.Write(
                "tests/Orders.Api.Tests/Orders.Api.Tests.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" +
                "<ProjectReference Include=\"../../src/Orders.Api/Orders.Api.csproj\" />" +
                "<PackageReference Include=\"xunit\" Version=\"2.9.3\" />" +
                "</ItemGroup></Project>");
            repository.Write(
                "tests/Orders.Api.Tests/OrderContractTests.cs",
                "using Xunit; namespace Orders.Api.Tests; public sealed class OrderContractTests " +
                "{ private const string ContractId = \"orders-api:get:orders-id\"; " +
                "[Fact] public void GetOrderContractIsVerified() { Assert.NotNull(ContractId); } }");
            repository.Write(
                ".github/workflows/build.yml",
                "name: Build\njobs:\n  build-api:\n    runs-on: ubuntu-latest\n    steps:\n" +
                "      - run: dotnet build src/Orders.Api/Orders.Api.csproj\n");
            var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
                repository.Path,
                "docs/cis",
                DryRun: false,
                Confirmed: true));
            Assert.Equal(0, result.ExitCode);
            return repository;
        }

        public static TemporaryRepository CreateInitializedGovernedApi()
        {
            var repository = Create();
            repository.Write(
                "src/Orders.Api/Orders.Api.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            repository.Write(
                "src/Orders.Api/Program.cs",
                "builder.Services.AddAuthorization(); var app = builder.Build(); app.MapGet(\"/orders/v1/orders/{id}\", () => Results.Ok()).RequireAuthorization(\"PERM-ORDERS-READ\");");
            var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
                repository.Path,
                "docs/cis",
                DryRun: false,
                Confirmed: true));
            Assert.Equal(0, result.ExitCode);
            repository.Write(
                "docs/cis/references/api-dictionary.md",
                "---\ntitle: API Dictionary\ntype: api-reference\nstatus: Draft\ncis:\n  stable_id: " + System.IO.Path.GetFileName(repository.Path) + ":reference:api-dictionary\n---\n\n" +
                "Governed by `" + System.IO.Path.GetFileName(repository.Path) + ":spec:api-dictionary`.\n\n" +
                "| API ID | Version | Method | Path | Module | Host | Exposure | Consumer | Request contract | Response contract | Permission / auth | OpenAPI operation | Trusted scope source | Rate-limit policy | Idempotency / concurrency | Collection semantics | Error contract | Cache policy | Data access path | Status | Evidence | Known tests | Notes |\n" +
                "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n" +
                "| API-ORDER-GET | v1 | GET | /orders/v1/orders/{id} | orders-api | orders-api | customer | orders-api | no-body | OrderResponse | PERM-ORDERS-READ | getOrder | identity.customer_id | unknown | GET | single resource | PROB-ORDERS-NOT-FOUND | private | application service | Implemented | src/Orders.Api/Program.cs | unknown | governed |\n");
            repository.Write(
                "docs/cis/references/permissions-dictionary.md",
                "---\ntitle: Permissions Dictionary\ntype: permission-reference\nstatus: Draft\ncis:\n  stable_id: " + System.IO.Path.GetFileName(repository.Path) + ":reference:permissions-dictionary\n---\n\n" +
                "| Permission code | Permission name | Domain | Capability group | Action | Scope type | Applies to | Default role mappings | Customer assignable | Endpoint / API usage | Frontend capability | Source location | Status | Notes |\n" +
                "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n" +
                "| PERM-ORDERS-READ | Read orders | orders | read | read | customer | orders | customer | yes | API-ORDER-GET | orders.read | src/Orders.Api/Program.cs | Verified | none |\n");
            repository.Write(
                "docs/cis/references/problem-details-catalogue.md",
                "---\ntitle: Problem Details Catalogue\ntype: problem-reference\nstatus: Draft\ncis:\n  stable_id: " + System.IO.Path.GetFileName(repository.Path) + ":reference:problem-details-catalogue\n---\n\n" +
                "| Problem ID | Problem type / reason | HTTP status | Raised by | Meaning | Frontend handling | Sensitive fields | Related commands / workflows | Status | Evidence |\n" +
                "|---|---|---|---|---|---|---|---|---|---|\n" +
                "| PROB-ORDERS-NOT-FOUND | order-not-found | 404 | orders-api | missing | show missing | none | get order | Verified | src/Orders.Api/Program.cs |\n");
            repository.Write(
                "docs/cis/references/api-governance-profile.md",
                "---\ntitle: API Governance Profile\ntype: api-governance-profile\nstatus: Active\ncis:\n  stable_id: " + System.IO.Path.GetFileName(repository.Path) + ":reference:api-governance-profile\n---\n\n" +
                "| Setting | Value | Status | Rationale |\n|---|---|---|---|\n| deprecation-window-days | 180 | Active | test |\n\n" +
                "| Document role | Path | Host / deployment | Status | Evidence |\n|---|---|---|---|---|\n| current | docs/openapi/current.json | orders-api | Active | test |\n\n" +
                "| Module | Major version | Status | Sunset | Consumers | Evidence |\n|---|---|---|---|---|---|\n| orders-api | v1 | Active | none | orders-api | test |\n");
            repository.Write(
                "docs/openapi/current.json",
                "{\"openapi\":\"3.0.3\",\"paths\":{\"/orders/v1/orders/{id}\":{\"get\":{\"operationId\":\"getOrder\",\"responses\":{\"200\":{\"description\":\"ok\"}}}}}}");
            return repository;
        }

        public void Write(string relativePath, string content)
        {
            var absolutePath = System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, content);
        }

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            var safeParent = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-graph-tests")) +
                System.IO.Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
