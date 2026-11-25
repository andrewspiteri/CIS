using Cis.Abstractions;
using Cis.Modules.Repository;
using System.Text.Json;

namespace Cis.Modules.Graph.Tests;

public sealed partial class GraphBuilderTests
{
    [Fact]
    public void ReadCurrentBuildsMissingGraphButOrdinaryReadDoesNot()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var reader = new GraphSnapshotReader(new CisRepositoryContextResolver(), builder: CreateBuilder());
        Assert.Equal("missing", reader.Read(repository.Path).Status);
        var current = reader.ReadCurrent(repository.Path);
        Assert.Equal(0, current.ExitCode);
        Assert.Equal("fresh", current.Freshness);
        Assert.NotNull(current.Graph);
    }

    [Fact]
    public void ReadCurrentReusesUnchangedMaterializationAndRetainsStructuredWarnings()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("docs/cis/unlisted.md", "Uncatalogued fixture document.");
        var reader = new GraphSnapshotReader(new CisRepositoryContextResolver(), builder: CreateBuilder());
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(repository.Path);
        var second = reader.ReadCurrent(repository.Path);
        Assert.Same(first, second);
        Assert.Equal("fresh", second.Freshness);
        Assert.NotEmpty(second.Diagnostics);
        Assert.Equal(JsonSerializer.Serialize(second.Diagnostics), JsonSerializer.Serialize(reader.ReadMetadata(repository.Path).Diagnostics));
    }

    [Fact]
    public void CurrentRefreshSeesNewChangedDeletedFilesAndInvalidatesEarlierQueryCache()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/Old.cs", "public class Old {}");
        var reader = new GraphSnapshotReader(new CisRepositoryContextResolver(), builder: CreateBuilder());
        var query = CreateQueryService();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(repository.Path);
        Assert.NotEmpty(query.Find(repository.Path, new(null, "source-file", null, null, "Old.cs", 20)).Nodes);
        File.Delete(Path.Combine(repository.Path, "src/Orders.Api/Old.cs"));
        repository.Write("src/Orders.Api/New.cs", "public class New {}");
        repository.Write("src/Orders.Api/Program.cs", "public class ChangedProgram {}");

        var current = reader.ReadCurrent(repository.Path);
        Assert.Equal("fresh", current.Freshness);
        Assert.NotEqual(first.Graph!.Build.Id, current.Graph!.Build.Id);
        Assert.DoesNotContain(current.Graph.Nodes, node => node.LocalId == "src/Orders.Api/Old.cs");
        Assert.Contains(current.Graph.Nodes, node => node.LocalId == "src/Orders.Api/New.cs");
        Assert.Contains(current.Graph.Nodes, node => node.Kind == "symbol" && node.Label == "ChangedProgram");
        Assert.Empty(query.Find(repository.Path, new(null, "source-file", null, null, "Old.cs", 20)).Nodes);
        Assert.NotEmpty(query.Find(repository.Path, new(null, "source-file", null, null, "New.cs", 20)).Nodes);
    }

    [Fact]
    public void ReadCurrentRechecksAnExistingFileInsideTheSameReadScope()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var reader = new GraphSnapshotReader(new CisRepositoryContextResolver(), builder: CreateBuilder());
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(repository.Path);
        repository.Write("src/Orders.Api/Program.cs", "public class ReplacementProgram {}");
        var second = reader.ReadCurrent(repository.Path);
        Assert.Equal("fresh", second.Freshness);
        Assert.NotEqual(first.Graph!.Build.Id, second.Graph!.Build.Id);
        Assert.Contains(second.Graph.Nodes, node => node.Kind == "symbol" && node.Label == "ReplacementProgram");
    }

    [Fact]
    public void ReadCurrentWithoutRefreshServiceDisclosesStaleAndFailedRefreshKeepsError()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        repository.Write("src/Orders.Api/New.cs", "public class New {}");
        Assert.Equal("stale", new GraphSnapshotReader(new CisRepositoryContextResolver()).ReadCurrent(repository.Path).Freshness);
        repository.Write("docs/cis/catalog.yml", "documents: [invalid");
        var current = new GraphSnapshotReader(new CisRepositoryContextResolver(), builder: CreateBuilder()).ReadCurrent(repository.Path);
        Assert.NotEqual(0, current.ExitCode);
        Assert.Equal("refresh-failed", current.Status);
        Assert.NotEmpty(current.Errors);
        Assert.NotEmpty(current.Diagnostics);
    }
}
