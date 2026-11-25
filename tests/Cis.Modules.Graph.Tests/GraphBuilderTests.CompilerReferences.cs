using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Graph.Tests;

public sealed partial class GraphBuilderTests
{
    [Fact]
    public void Build_PreservesEveryPartialDeclarationAndCompilerBoundMemberReference()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/IValues.cs", "namespace Orders.Api; public partial interface IValues { }");
        repository.Write("src/Orders.Api/IValues.Members.cs", """
            namespace Orders.Api;
            public partial interface IValues
            {
                int Value { get; }
                int this[int index] { get; }
                event System.Action Changed;
            }
            """);
        repository.Write("src/Orders.Api/Values.cs", """
            namespace Orders.Api;
            public sealed class Values : IValues
            {
                public int Value => 1;
                public int this[int index] => index;
                public event System.Action Changed { add { } remove { } }
            }
            public static class State { public static int Offset = 2; }
            """);
        repository.Write("src/Orders.Api/Consumer.cs", """
            namespace Orders.Api;
            public sealed class Consumer
            {
                public int Read(IValues supplied)
                {
                    supplied.Changed += OnChanged;
                    return supplied.Value + supplied[0] + State.Offset;
                }
                private void OnChanged() { }
                public System.Type Inspect() => typeof(IValues);
            }
            """);

        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var snapshot = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path);
        Assert.Equal(0, snapshot.ExitCode);
        var graph = snapshot.Graph!;
        var contract = Assert.Single(graph.Nodes, node => node.Kind == "symbol" && node.Label == "Orders.Api.IValues");
        string[] declarations = ["src/Orders.Api/IValues.Members.cs", "src/Orders.Api/IValues.cs"];
        Assert.Equal(declarations, contract.Locations.Select(location => location.Path).Order(StringComparer.Ordinal));
        Assert.Equal(declarations, contract.Provenance.Where(item => item.SourceKind == "repository-source"
            && item.Method == "compiler-binding").Select(item => item.Path).Distinct().Order(StringComparer.Ordinal));
        var consumer = Assert.Single(graph.Nodes, node => node.Kind == "symbol" && node.Label == "Orders.Api.Consumer.Read(Orders.Api.IValues)");
        var targets = graph.Edges.Where(edge => edge.Type == "references" && edge.From == consumer.Key)
            .Select(edge => graph.Nodes.Single(node => node.Key == edge.To)).ToArray();
        Assert.Contains(targets, node => node.Subtype == "property" && node.Label == "Orders.Api.IValues.Value");
        Assert.Contains(targets, node => node.Subtype == "indexer" && node.Label.Contains("IValues.this[", StringComparison.Ordinal));
        Assert.Contains(targets, node => node.Subtype == "event" && node.Label == "Orders.Api.IValues.Changed");
        Assert.Contains(targets, node => node.Subtype == "field" && node.Label == "Orders.Api.State.Offset");
        Assert.All(graph.Edges.Where(edge => edge.Type == "references" && edge.From == consumer.Key), edge =>
            Assert.Contains(edge.Observations, item => item.Method == "references"));
        Assert.DoesNotContain(graph.Edges, edge => edge.Type == "calls" && edge.From == consumer.Key
            && targets.Any(node => node.Key == edge.To));
        var typeOnly = Assert.Single(graph.Nodes, node => node.Kind == "symbol" && node.Label == "Orders.Api.Consumer.Inspect()");
        Assert.DoesNotContain(graph.Edges, edge => edge.Type == "references" && edge.From == typeOnly.Key);
    }

    [Fact]
    public void Build_IdentifiesExternalInterfaceTargetsWithoutTreatingCallSitesAsDeclarations()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/ExternalConsumer.cs", """
            namespace Orders.Api;
            public sealed class ExternalConsumer
            {
                public int Read(System.IDisposable supplied, System.Collections.Generic.IReadOnlyCollection<int> values)
                {
                    supplied.Dispose();
                    return System.Math.Abs(values.Count);
                }
            }
            """);

        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var snapshot = new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path);
        Assert.Equal(0, snapshot.ExitCode);
        var graph = snapshot.Graph!;
        var dispose = Assert.Single(graph.Nodes, node => node.Kind == "symbol" && node.Label == "System.IDisposable.Dispose()");
        var count = Assert.Single(graph.Nodes, node => node.Kind == "symbol"
            && node.Label == "System.Collections.Generic.IReadOnlyCollection<T>.Count");
        Assert.Equal("interface", dispose.Properties["declaringTypeKind"]);
        Assert.Equal("interface", count.Properties["declaringTypeKind"]);
        Assert.All(new[] { dispose, count }, node =>
        {
            Assert.Equal("true", node.Properties["external"]);
            Assert.DoesNotContain(node.Provenance, item => item.Method == "compiler-binding");
        });
        Assert.Contains(graph.Edges, edge => edge.Type == "calls" && edge.To == dispose.Key);
        Assert.Contains(graph.Edges, edge => edge.Type == "references" && edge.To == count.Key);
        var abs = Assert.Single(graph.Nodes, node => node.Kind == "symbol" && node.Label == "System.Math.Abs(int)");
        Assert.Equal("class", abs.Properties["declaringTypeKind"]);
    }
}
