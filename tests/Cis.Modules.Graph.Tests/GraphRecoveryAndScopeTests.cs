using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Context;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Cis.Modules.Graph.Tests;

[CollectionDefinition("Graph console", DisableParallelization = true)]
public sealed class GraphConsoleCollection;

[Collection("Graph console")]
public sealed partial class GraphBuilderTests
{
    [Fact]
    public void ProjectReferencesUseCanonicalPathCasing()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/Orders.Api.csproj", """
            <Project><ItemGroup><ProjectReference Include="../library/library.csproj" /></ItemGroup></Project>
            """);
        var input = CSharpProjectScope.CreateInput(repository.Path, "src/Orders.Api/Code.cs", "", "orders-api", true,
            ["src/Orders.Api/Orders.Api.csproj", "src/Library/Library.csproj"]);
        Assert.Equal("src/Library/Library.csproj", Assert.Single(input.ProjectReferences));
    }

    [Fact]
    public void CallersCommandOnlyReturnsIncomingEdges()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/Flow.cs", "public class Flow { public void Start() { Target(); } public void Target() { Outgoing(); } public void Outgoing() {} }");
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var stored = new SqliteGraphStore().Read(repository.Path);
        var target = Assert.Single(stored.Graph!.Nodes, node => node.Label == "Flow.Target()");
        using var app = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new DocsModule())
            .AddModule(new GraphModule()).AddModule(new ContextModule()).Build();
        using var json = JsonDocument.Parse(CaptureCommand(app, ["context", "callers", "--repo", repository.Path, "--id", target.Key, "--format", "json"]));
        var traversals = json.RootElement.GetProperty("traversals").EnumerateArray().ToArray();
        Assert.NotEmpty(traversals);
        Assert.All(traversals, traversal => Assert.Equal("in", traversal.GetProperty("direction").GetString()));
        var labels = json.RootElement.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Flow.Start()", labels);
        Assert.DoesNotContain("Flow.Outgoing()", labels);
    }

    [Fact]
    public void AgentStatusBoundsRepeatedDiagnosticsWhileJsonRetainsAllEvidence()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var store = new SqliteGraphStore();
        var stored = store.Read(repository.Path);
        var diagnostics = Enumerable.Range(0, 200).Select(index => new CisGraphDiagnostic("SAMPLE", "warning",
            "Repeated warning " + index, Enumerable.Range(0, 20).Select(n => $"evidence/{index}/{n}.cs").ToArray())).ToArray();
        store.Write(repository.Path, stored.Graph!, stored.Manifest!, diagnostics);
        using var app = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new DocsModule()).AddModule(new GraphModule()).Build();
        var agent = CaptureCommand(app, ["graph", "status", "--repo", repository.Path, "--format", "agent"]);
        Assert.Contains("count=200", agent);
        Assert.Contains("diagnostics.omitted=195", agent);
        Assert.Equal(5, agent.Split('\n').Count(line => line.StartsWith("diagnostic=")));
        Assert.True(agent.Length < 4000);
        using var json = JsonDocument.Parse(CaptureCommand(app, ["graph", "status", "--repo", repository.Path, "--format", "json"]));
        Assert.Equal(200, json.RootElement.GetProperty("diagnostics").GetArrayLength());
    }

    private static string CaptureCommand(CisApplication application, string[] args)
    {
        var original = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Assert.Equal(0, application.Invoke(args));
            return captured.ToString();
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public void Build_PreservesCorruptGenerationAndReplacesItWithReadableGraph()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var store = new SqliteGraphStore();
        var path = store.DatabasePath(repository.Path);
        File.WriteAllText(path, "corrupt database fixture");
        Assert.Equal(0, CreateBuilder().Build(repository.Path, refresh: true).ExitCode);
        Assert.True(store.Read(repository.Path).Success);
        var backup = Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(path)!, "corrupt-*"));
        Assert.Equal("corrupt database fixture", File.ReadAllText(Path.Combine(backup, "context.db")));
    }

    [Fact]
    public void Build_RecoversHotRollbackJournalAndPublishesReadableGeneration()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var store = new SqliteGraphStore();
        var path = store.DatabasePath(repository.Path);
        byte[] database;
        byte[] journal;
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE recovery_probe(value BLOB); INSERT INTO recovery_probe VALUES(zeroblob(1048576)); PRAGMA cache_size=5;";
            command.ExecuteNonQuery();
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = "UPDATE recovery_probe SET value=randomblob(1048576);";
            command.ExecuteNonQuery();
            // Capture a spilled, uncommitted transaction, as left by a killed writer.
            database = Snapshot(path);
            journal = Snapshot(path + "-journal");
            transaction.Rollback();
        }
        File.WriteAllBytes(path, database);
        File.WriteAllBytes(path + "-journal", journal);
        Assert.False(store.ReadHeader(repository.Path).Success);
        var rebuilt = CreateBuilder().Build(repository.Path, refresh: true);
        Assert.Equal(0, rebuilt.ExitCode);
        Assert.False(File.Exists(path + "-journal"));
        Assert.Equal(rebuilt.BuildId, store.ReadHeader(repository.Path).Build!.Id);
        Assert.Equal("fresh", CreateValidator().Status(repository.Path).Freshness);
        Assert.True(store.Read(repository.Path).Success);

        static byte[] Snapshot(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    [Fact]
    public void InputScopeExcludesSnapshotsAndParticipatesInFreshness()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("history/evidence/Copy.cs", "public class ArchivedOnly { }");
        repository.Write(".cis/graph-inputs.json", """{"schemaVersion":1,"exclude":["history/evidence"]}""");
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var stored = new SqliteGraphStore().Read(repository.Path);
        Assert.DoesNotContain(stored.Graph!.Nodes, node => node.Label == "ArchivedOnly");
        Assert.Contains(stored.Manifest!.Inputs, input => input.Path == ".cis/graph-inputs.json");
        repository.Write("history/evidence/Copy.cs", "public class ChangedArchive { }");
        Assert.Equal("fresh", CreateValidator().Status(repository.Path).Freshness);
        repository.Write(".cis/graph-inputs.json", """{"schemaVersion":1}""");
        Assert.Equal("stale", CreateValidator().Status(repository.Path).Freshness);
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        Assert.Contains(new SqliteGraphStore().Read(repository.Path).Graph!.Nodes, node => node.Label == "ChangedArchive");
    }

    [Theory]
    [InlineData("../history")]
    [InlineData("/history")]
    [InlineData("history/**")]
    public void InputScopeRejectsUnsafeOrUnsupportedPrefixes(string prefix)
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write(".cis/graph-inputs.json", $$"""{"schemaVersion":1,"exclude":["{{prefix}}"]}""");
        Assert.NotEqual(0, CreateBuilder().Build(repository.Path).ExitCode);
    }
}

public sealed class CompilerProjectIsolationTests
{
    [Fact]
    public void DuplicateProjectTypesAndLambdasHaveIndependentIdentities()
    {
        var analysis = CSharpCompilerAnalyzer.Analyze([
            new("a/Code.cs", "namespace Sample; public class Same { public void Run() { System.Action a = () => System.Console.WriteLine(1); System.Action b = () => System.Console.WriteLine(2); } }", "shared") { ProjectPath = "a/A.csproj" },
            new("b/Code.cs", "namespace Sample; public class Same { public void Run() { System.Console.WriteLine(3); } }", "shared") { ProjectPath = "b/B.csproj" }
        ]);
        Assert.Equal(2, analysis.Symbols.Count(symbol => symbol.QualifiedName == "Sample.Same"));
        Assert.All(analysis.Symbols, symbol => Assert.Empty(symbol.OwnershipConflictPaths));
        var lambdaCalls = analysis.Calls.Where(call => call.Path == "a/Code.cs" && call.TargetName == "WriteLine").ToArray();
        Assert.Equal(2, lambdaCalls.Length);
        Assert.NotEqual(lambdaCalls[0].CallerLocalId, lambdaCalls[1].CallerLocalId);
    }

    [Fact]
    public void ProjectReferencePreservesCrossProjectCallTargets()
    {
        var analysis = CSharpCompilerAnalyzer.Analyze([
            new("library/Api.cs", "public class Api { public static void Send() {} }", "library") { ProjectPath = "library/Library.csproj" },
            new("app/Caller.cs", "public class Caller { public void Run() { Api.Send(); } }", "app") { ProjectPath = "app/App.csproj", ProjectReferences = ["library/Library.csproj"] }
        ]);
        var call = Assert.Single(analysis.Calls, call => call.TargetName == "Send");
        Assert.False(call.TargetExternal);
        Assert.Contains(analysis.Symbols, symbol => symbol.LocalId == call.TargetLocalId && symbol.Path == "library/Api.cs");
    }
}
