using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalSelectionTests
{
    [Fact]
    public void ManyNestedHelpersCannotForceAnOtherwiseProvenSelectionToAllGraphInputs()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var names = Enumerable.Range(0, 600).Select(index => "Fixture.ATests.NestedHelperWithLongDescriptiveName" + index).ToArray();
        Assert.True(string.Join('|', names.Select(name => "FullyQualifiedName~" + name + ".")).Length > 32768);
        var reader = new ClassReader(fixture, names);
        var result = new IncrementalTestService(reader).Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var batch = result.Batches.Single(item => item.SourceFile == "src/A.cs");
        Assert.Equal("graph", batch.Selection);
        Assert.Equal(["tests/ATests.cs"], batch.TestFiles);
        Assert.Contains("src/Shared.cs", batch.DependencyFiles);
        Assert.DoesNotContain("src/B.cs", batch.DependencyFiles);
        Assert.Contains("FullyQualifiedName~Fixture.ATests.", batch.TestFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("NestedHelper", batch.TestFilter, StringComparison.Ordinal);
        Assert.True(batch.TestFilter.Length < 32768);
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void NestedOnlyAndUnrelatedClassNamesRetainTheirOwnPredicates()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var names = new[] { "Fixture.Container.NestedTests", "Fixture.ATestsExtra", "Fixture.ATests+NestedTests", "Other.ATests" };
        var result = new IncrementalTestService(new ClassReader(fixture, names)).Plan(new(fixture.Path, "profile.json"));
        var batch = result.Batches.Single(item => item.SourceFile == "src/A.cs");
        Assert.Equal("graph", batch.Selection);
        foreach (var name in names) Assert.Contains("FullyQualifiedName~" + name + ".", batch.TestFilter, StringComparison.Ordinal);
        Assert.Equal(["tests/ATests.cs"], batch.TestFiles);
        Assert.DoesNotContain("src/B.cs", batch.DependencyFiles);
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void CollapsingNestedPredicatesPreservesEveryPreviouslySelectedCase()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var names = new[] { "Fixture.ATests.Inner", "Fixture.ATests.Inner.Deep", "Fixture.AnotherTests", "Fixture.Container.NestedTests" };
        var result = new IncrementalTestService(new ClassReader(fixture, names)).Plan(new(fixture.Path, "profile.json"));
        var filter = result.Batches.Single(item => item.SourceFile == "src/A.cs").TestFilter;
        var retained = filter[(filter.LastIndexOf("&(", StringComparison.Ordinal) + 2)..^1].Split('|')
            .Select(term => term["FullyQualifiedName~".Length..]).ToArray();
        foreach (var name in names.Append("Fixture.ATests"))
        {
            var executedName = name + ".ARealTestMethod";
            Assert.Contains(retained, predicate => executedName.Contains(predicate, StringComparison.Ordinal));
        }
        Assert.Equal(3, retained.Length);
        Assert.DoesNotContain("FullyQualifiedName~Fixture.ATests.Inner.", filter, StringComparison.Ordinal);
        Assert.Empty(fixture.Calls());
    }

    private sealed class ClassReader(IncrementalTestServiceTests.Fixture inner, string[] names) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var result = inner.Read(repositoryPath);
            var classes = names.Select((name, index) => new CisGraphNode("nested-" + index, "sample", "symbol", "class", "nested-" + index,
                name, ["compiler-bound", "csharp"], "derived", "active", [new("tests/ATests.cs", "line", "1")],
                new Dictionary<string, string> { ["qualifiedName"] = name, ["component"] = "fixture-tests" }, []));
            return result with { Graph = result.Graph! with { Nodes = [.. result.Graph!.Nodes, .. classes] } };
        }
    }
}
