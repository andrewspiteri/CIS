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

    [Theory]
    [InlineData("external-interface")]
    [InlineData("external-base")]
    public void UnresolvedTypeRelationshipsUseFullScopeWithoutTreatingExternalObservationsAsDeclarations(string scenario)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var result = new IncrementalTestService(new ProjectionReader(fixture, scenario)).Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        Assert.All(result.Batches, batch =>
        {
            Assert.Equal("all-tests", batch.Selection);
            Assert.Equal(fixture.Profile.FullTestFilter, batch.TestFilter);
            Assert.Equal(["tests/ATests.cs", "tests/BTests.cs"], batch.TestFiles);
            Assert.Contains("src/A.cs", batch.DependencyFiles);
            Assert.Contains("src/B.cs", batch.DependencyFiles);
        });
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("unresolved interface or inheritance", StringComparison.Ordinal));
        Assert.Empty(fixture.Calls());
    }

    [Theory]
    [InlineData("calls")]
    [InlineData("references")]
    public void UnresolvedInterfaceMembersBroadenOnlyTheAffectedDependencyFamily(string relation)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var service = new IncrementalTestService(new ProjectionReader(fixture, "external-interface-" + relation));
        var result = service.Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal("all-tests", a.Selection);
        Assert.Equal(fixture.Profile.FullTestFilter, a.TestFilter);
        Assert.Contains("src/B.cs", a.DependencyFiles);
        Assert.Equal("graph", b.Selection);
        Assert.Equal(["tests/BTests.cs"], b.TestFiles);
        Assert.DoesNotContain("src/A.cs", b.DependencyFiles);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("src/A.cs: unresolved", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.StartsWith("src/B.cs: unresolved", StringComparison.Ordinal));
        fixture.Write("src/B.cs", "public static class SuppliedImplementation { public static int Value => 2; }");
        var changed = service.Plan(new(fixture.Path, "profile.json"));
        Assert.NotEqual(a.UnitFingerprint, changed.Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitFingerprint);
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
    public void PartialDeclarationsAreDependenciesWithoutAnEdgeToThePartialType()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var service = new IncrementalTestService(new ProjectionReader(fixture, "partial"));
        var result = service.Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal("graph", a.Selection);
        Assert.Contains("src/B.cs", a.DependencyFiles);
        Assert.DoesNotContain("tests/BTests.cs", a.DependencyFiles);
        Assert.Equal(["tests/ATests.cs"], a.TestFiles);
        Assert.Contains("tests/ATests.cs", b.TestFiles);
        fixture.Write("src/B.cs", "public partial class Shared { private int value = 2; }");
        var changed = service.Plan(new(fixture.Path, "profile.json"));
        Assert.NotEqual(a.UnitFingerprint, changed.Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitFingerprint);
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

    [Theory]
    [InlineData("inherits")]
    [InlineData("inherits-from")]
    public void RepositoryInheritanceIncludesBaseConsumersAndDerivedDispatchCandidates(string relation)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var service = new IncrementalTestService(new ProjectionReader(fixture, relation));
        var result = service.Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal("graph", b.Selection);
        Assert.Contains("tests/ATests.cs", b.TestFiles);
        Assert.Contains("tests/BTests.cs", b.TestFiles);
        Assert.Contains("src/Shared.cs", b.DependencyFiles);
        Assert.Contains("src/B.cs", a.DependencyFiles);
        fixture.Write("src/B.cs", "public class ChangedDerivedImplementation {}");
        var changed = service.Plan(new(fixture.Path, "profile.json"));
        Assert.NotEqual(a.UnitFingerprint, changed.Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitFingerprint);
        Assert.Empty(fixture.Calls());
    }

    [Theory]
    [InlineData("proposed")]
    [InlineData("rejected")]
    public void UnacceptedImplementationRelationshipsDoNotConnectIndependentCallers(string state)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var result = new IncrementalTestService(new ProjectionReader(fixture, "interface", state)).Plan(new(fixture.Path, "profile.json"));
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal(["tests/ATests.cs"], a.TestFiles);
        Assert.Equal(["tests/BTests.cs"], b.TestFiles);
        Assert.DoesNotContain("src/B.cs", a.DependencyFiles);
        Assert.DoesNotContain("src/Shared.cs", b.DependencyFiles);
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void ExternalObservationChangesDoNotInvalidateAnIndependentUnitCacheButConfigAndToolsDo()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Write("tool.txt", "pinned external tool identity");
        fixture.Save(fixture.Profile with
        {
            ToolFiles = [.. fixture.Profile.ToolFiles, "tool.txt"],
            MutationCommand = null,
            RecoveryCommand = null,
            MaxRunBytes = 4 * 1024 * 1024,
            MaxRetainedBytes = 32 * 1024 * 1024
        });
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path);
        var service = new IncrementalTestService(new ProjectionReader(fixture, "external"), authority, authority);
        var executed = service.Run(new(fixture.Path, "profile.json", "unit", 4, 120), 0, TestContext.Current.CancellationToken);
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
        fixture.Save(fixture.Profile with { MaxRunBytes = 4 * 1024 * 1024, MaxRetainedBytes = 32 * 1024 * 1024 });
        // This uninitialized fixture tests synthetic protocol cache behavior.
        // Its diagnostic admission supplies no governed pilot or qualification proof.
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path, required: false);
        var service = new IncrementalTestService(new ProjectionReader(fixture, "external"), authority, authority);
        var executed = service.Run(new(fixture.Path, "profile.json", "all", 4, 120), 0, TestContext.Current.CancellationToken);
        Assert.True(executed.ExitCode == 0, string.Join("; ", executed.Diagnostics));
        var pilot = Assert.IsType<NativeCorrectnessPilotResult>(executed.Pilot);
        Assert.Equal("legacy-unaccounted", pilot.Status);
        Assert.False(pilot.Ready);
        Assert.False(pilot.ControllerAssessed);
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

    private sealed class ProjectionReader(IncrementalTestServiceTests.Fixture inner, string scenario, string relationState = "discovered") : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var result = inner.Read(repositoryPath);
            var extra = new List<CisGraphNode>();
            var edges = new List<CisGraphEdge>();
            if (scenario is "external" or "external-interface" or "external-base")
            {
                extra.Add(Symbol("external", ["src/B.cs", "tests/BTests.cs"], true, []));
                var relation = scenario == "external" ? "calls" : scenario == "external-base" ? "inherits" : "implements";
                edges.Add(Edge("a", "external", relation, "external"));
                edges.Add(Edge("b", "external", relation, "external"));
            }
            else if (scenario.StartsWith("external-interface-", StringComparison.Ordinal))
            {
                var target = Symbol("external", ["src/B.cs", "tests/BTests.cs"], true, []);
                extra.Add(target with { Properties = new Dictionary<string, string>(target.Properties) { ["declaringTypeKind"] = "interface" } });
                edges.Add(Edge("a", "external", scenario["external-interface-".Length..], "external"));
            }
            else if (scenario is "mixed" or "partial")
            {
                extra.Add(Symbol("mixed", ["src/Shared.cs", "src/B.cs", "tests/BTests.cs"], true,
                    [Declaration("src/Shared.cs"), Declaration("src/B.cs"), Observation("tests/BTests.cs")])
                    with
                { Subtype = scenario == "partial" ? "class" : "method" });
                if (scenario == "mixed") edges.Add(Edge("a", "mixed", "calls", "external"));
            }
            else
            {
                extra.Add(Symbol("contract", ["src/Shared.cs"], false, [Declaration("src/Shared.cs")]));
                extra.Add(Symbol("implementation", ["src/B.cs"], false, [Declaration("src/B.cs")]));
                edges.Add(Edge("a", "contract", "calls", "repository"));
                edges.Add(Edge("implementation", "contract", scenario == "interface" ? "implements" : scenario, "repository")
                    with
                { State = relationState });
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
