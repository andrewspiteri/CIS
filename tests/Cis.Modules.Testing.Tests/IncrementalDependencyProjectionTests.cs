using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalDependencyProjectionTests
{
    [Fact]
    public void SharedExternalCallSitesAreNotDependenciesOnAnotherRepositoryCaller()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var reader = new ProjectionReader(fixture, "external");
        var result = new IncrementalTestService(reader).Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal("graph", a.Selection);
        Assert.Equal(["tests/ATests.cs"], a.TestFiles);
        Assert.Equal(["tests/BTests.cs"], b.TestFiles);
        Assert.DoesNotContain("src/B.cs", a.DependencyFiles);
        Assert.DoesNotContain("tests/BTests.cs", a.DependencyFiles);
        Assert.DoesNotContain("src/A.cs", b.DependencyFiles);
        Assert.Contains("src/Shared.cs", a.DependencyFiles);
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void MixedSymbolLocationsKeepAllBoundDeclarationsAndDiscardOnlyCallSiteObservations()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var result = new IncrementalTestService(new ProjectionReader(fixture, "mixed")).Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        Assert.Contains("src/Shared.cs", a.DependencyFiles);
        Assert.Contains("src/B.cs", a.DependencyFiles);
        Assert.DoesNotContain("tests/BTests.cs", a.DependencyFiles);
        Assert.Equal(["tests/ATests.cs"], a.TestFiles);
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void RepositoryInterfaceAndImplementationRelationshipsRemainInSelectionAndIdentity()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var result = new IncrementalTestService(new ProjectionReader(fixture, "interface")).Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Contains("src/Shared.cs", a.DependencyFiles);
        Assert.Contains("src/B.cs", a.DependencyFiles);
        Assert.Contains("tests/ATests.cs", b.TestFiles);
        Assert.Contains("tests/BTests.cs", b.TestFiles);
        var before = b.UnitFingerprint;
        fixture.Write("src/Shared.cs", "public interface IChangedRepositoryContract {}");
        var changed = new IncrementalTestService(new ProjectionReader(fixture, "interface")).Plan(new(fixture.Path, "profile.json"));
        Assert.NotEqual(before, changed.Batches.Single(batch => batch.SourceFile == "src/B.cs").UnitFingerprint);
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void ExternalObservationChangesDoNotInvalidateAnIndependentUnitCacheButConfigAndToolsDo()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Write("tool.txt", "pinned external tool identity");
        fixture.Save(fixture.Profile with { ToolFiles = [.. fixture.Profile.ToolFiles, "tool.txt"] });
        var service = new IncrementalTestService(new ProjectionReader(fixture, "external"));
        var executed = service.Run(new(fixture.Path, "profile.json", "unit", 4, 120));
        Assert.True(executed.ExitCode == 0, string.Join("; ", executed.Diagnostics));
        Assert.Equal(2, executed.ExecutedUnitBatches);

        fixture.Write("tests/BTests.cs", "class BTests { /* independent caller changed */ }");
        var independent = service.Plan(new(fixture.Path, "profile.json", "unit"));
        Assert.True(independent.Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitCurrent);
        Assert.False(independent.Batches.Single(batch => batch.SourceFile == "src/B.cs").UnitCurrent);

        fixture.Write("config.json", "{\"dependencyVersion\":\"changed\"}");
        Assert.All(service.Plan(new(fixture.Path, "profile.json", "unit")).Batches, batch => Assert.False(batch.UnitCurrent));
        fixture.Write("config.json", "{}");
        Assert.True(service.Plan(new(fixture.Path, "profile.json", "unit")).Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitCurrent);
        fixture.Write("tool.txt", "changed external tool identity");
        Assert.All(service.Plan(new(fixture.Path, "profile.json", "unit")).Batches, batch => Assert.False(batch.UnitCurrent));
        Assert.Equal(2, fixture.Calls().Length);
    }

    [Fact]
    public void ACompletedMutationRemainsReusableWhenOnlyAnIndependentExternalCallerChanges()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var service = new IncrementalTestService(new ProjectionReader(fixture, "external"));
        var executed = service.Run(new(fixture.Path, "profile.json", "all", 4, 120));
        Assert.True(executed.ExitCode == 0, string.Join("; ", executed.Diagnostics));
        Assert.Equal(2, executed.ExecutedMutationBatches);
        var calls = fixture.Calls().Length;
        fixture.Write("tests/BTests.cs", "class BTests { /* independent external caller changed */ }");
        var planned = service.Plan(new(fixture.Path, "profile.json"));
        Assert.True(planned.Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitCurrent);
        Assert.True(planned.Batches.Single(batch => batch.SourceFile == "src/A.cs").MutationCurrent);
        Assert.False(planned.Batches.Single(batch => batch.SourceFile == "src/B.cs").UnitCurrent);
        Assert.False(planned.Batches.Single(batch => batch.SourceFile == "src/B.cs").MutationCurrent);
        Assert.Equal(calls, fixture.Calls().Length);
    }

    private sealed class ProjectionReader(IncrementalTestServiceTests.Fixture inner, string scenario) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var result = inner.Read(repositoryPath);
            var extra = new List<CisGraphNode>();
            var edges = new List<CisGraphEdge>();
            if (scenario == "external")
            {
                extra.Add(Symbol("external", ["src/B.cs", "tests/BTests.cs"], true, []));
                edges.Add(Edge("a", "external", "calls", "external"));
                edges.Add(Edge("b", "external", "calls", "external"));
            }
            else if (scenario == "mixed")
            {
                extra.Add(Symbol("mixed", ["src/Shared.cs", "src/B.cs", "tests/BTests.cs"], true,
                    [Declaration("src/Shared.cs"), Declaration("src/B.cs"), Observation("tests/BTests.cs")]));
                edges.Add(Edge("a", "mixed", "calls", "external"));
            }
            else
            {
                extra.Add(Symbol("contract", ["src/Shared.cs"], false, [Declaration("src/Shared.cs")]));
                extra.Add(Symbol("implementation", ["src/B.cs"], false, [Declaration("src/B.cs")]));
                edges.Add(Edge("a", "contract", "calls", "repository"));
                edges.Add(Edge("contract", "implementation", "implemented-by", "repository"));
                edges.Add(Edge("implementation", "contract", "implements", "repository"));
            }
            return result with { Graph = result.Graph! with { Nodes = [.. result.Graph!.Nodes, .. extra], Edges = [.. result.Graph.Edges, .. edges] } };
        }

        private static CisGraphNode Symbol(string key, string[] files, bool external, CisGraphEvidence[] provenance)
            => new(key, "sample", "symbol", "method", key, key, ["compiler-bound", "csharp"], "derived", "active",
                files.Select(file => new CisGraphLocation(file, "line", "1")).ToArray(),
                new Dictionary<string, string> { ["external"] = external ? "true" : "false" }, provenance);
        private static CisGraphEvidence Declaration(string file) => new("repository-source", file, "line", "1", "source-hash",
            "compiler-binding", "cis.csharp.compiler/6", "high", "Repository declaration.");
        private static CisGraphEvidence Observation(string file) => new("repository-source", file, "line", "1", "source-hash",
            "compiler-call", "cis.csharp.semantic-facts/3", "high", "External call-site observation.");
        private static CisGraphEdge Edge(string from, string to, string type, string scope) => new(from + "-" + type + "-" + to,
            type, from, to, "discovered", "high", [], new Dictionary<string, string> { ["scope"] = scope });
    }
}
