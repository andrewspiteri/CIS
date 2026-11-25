using Cis.Abstractions;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalCompilerSelectionTests
{
    [Fact]
    public void CompilerInheritanceSelectsDerivedTestsAndIncludesTheImplicitBaseDependency()
    {
        using var fixture = CompilerFixture.Inheritance();
        var graph = fixture.Snapshot().Graph!;
        Assert.Contains(graph.Edges, edge => edge.Type == "inherits"
            && HasLocation(graph, edge.From, "src/Model/Derived.cs")
            && HasLocation(graph, edge.To, "src/Model/Base.cs"));
        Assert.DoesNotContain(graph.Edges, edge => edge.Type == "inherits-from");
        Assert.DoesNotContain(graph.Edges, edge => (edge.Type is "calls" or "references")
            && HasLocation(graph, edge.From, "tests/Model.Tests/DerivedTests.cs")
            && HasLocation(graph, edge.To, "src/Model/Base.cs"));

        var plan = fixture.Plan();
        var @base = Batch(plan, "src/Model/Base.cs");
        Assert.Equal("graph", @base.Selection);
        Assert.Equal(["tests/Model.Tests/BaseTests.cs", "tests/Model.Tests/DerivedTests.cs"], @base.TestFiles);
        Assert.Contains("Fixture.BaseTests.", @base.TestFilter, StringComparison.Ordinal);
        Assert.Contains("Fixture.DerivedTests.", @base.TestFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture.IndependentTests.", @base.TestFilter, StringComparison.Ordinal);
        var derived = Batch(plan, "src/Model/Derived.cs");
        Assert.Contains("tests/Model.Tests/DerivedTests.cs", derived.TestFiles);
        Assert.Contains("src/Model/Base.cs", derived.DependencyFiles);
        Assert.DoesNotContain("src/Model/Independent.cs", derived.DependencyFiles);
        Assert.DoesNotContain("tests/Model.Tests/IndependentTests.cs", derived.TestFiles);
        Assert.Empty(fixture.Native.Calls());
    }

    [Fact]
    public void CompilerBaseFieldInitializerChangeInvalidatesTheDerivedBatchCache()
    {
        using var fixture = CompilerFixture.Inheritance();
        fixture.EstablishUnitCache();
        var cached = fixture.Plan();
        var before = Batch(cached, "src/Model/Derived.cs").UnitFingerprint;
        Assert.All(cached.Batches, batch => Assert.True(batch.UnitCurrent));

        fixture.Native.Write("src/Model/Base.cs", "namespace Fixture; public class Base { public int Value = 2; }");

        var changed = fixture.Plan();
        Assert.False(Batch(changed, "src/Model/Base.cs").UnitCurrent);
        var derived = Batch(changed, "src/Model/Derived.cs");
        Assert.False(derived.UnitCurrent);
        Assert.NotEqual(before, derived.UnitFingerprint);
        Assert.Contains("src/Model/Base.cs", derived.DependencyFiles);
        Assert.True(Batch(changed, "src/Model/Independent.cs").UnitCurrent);
        Assert.Equal(cached.Batches.Count, fixture.Native.Calls().Length);
    }

    [Fact]
    public void CompilerInterfaceDispatchIncludesEveryRepositoryCandidateAndExcludesIndependentCallers()
    {
        using var fixture = CompilerFixture.InterfaceDispatch();
        var graph = fixture.Snapshot().Graph!;
        Assert.Contains(graph.Edges, edge => edge.Type == "implements"
            && HasLocation(graph, edge.From, "src/Model/FirstValue.cs")
            && HasLocation(graph, edge.To, "src/Model/IValue.cs"));
        Assert.Contains(graph.Edges, edge => edge.Type == "inherits"
            && HasLocation(graph, edge.From, "src/Model/DerivedValue.cs")
            && HasLocation(graph, edge.To, "src/Model/FirstValue.cs"));
        Assert.DoesNotContain(graph.Edges, edge => edge.Type == "implemented-by"
            && HasLocation(graph, edge.From, "src/Model/IValue.cs")
            && ImplementationFiles.Any(path => HasLocation(graph, edge.To, path)));
        Assert.Contains(graph.Edges, edge => edge.Type == "calls"
            && HasLocation(graph, edge.From, "src/Model/Consumer.cs")
            && HasLocation(graph, edge.To, "src/Model/IValue.cs"));
        // The implementation is supplied by a separate composition file. There is
        // no concrete constructor or method call from the consumer or its test.
        Assert.DoesNotContain(graph.Edges, edge => edge.Type == "calls"
            && (HasLocation(graph, edge.From, "src/Model/Consumer.cs")
                || HasLocation(graph, edge.From, "tests/Model.Tests/ConsumerTests.cs"))
            && ImplementationFiles.Any(path => HasLocation(graph, edge.To, path)));

        var plan = fixture.Plan();
        foreach (var implementation in ImplementationFiles)
        {
            var batch = Batch(plan, implementation);
            Assert.Equal("graph", batch.Selection);
            Assert.Contains("tests/Model.Tests/DirectImplementationTests.cs", batch.TestFiles);
            Assert.Contains("tests/Model.Tests/ConsumerTests.cs", batch.TestFiles);
            Assert.DoesNotContain("tests/Model.Tests/IndependentTests.cs", batch.TestFiles);
        }
        var consumer = Batch(plan, "src/Model/Consumer.cs");
        Assert.Equal("graph", consumer.Selection);
        Assert.Equal(["tests/Model.Tests/ConsumerTests.cs"], consumer.TestFiles);
        foreach (var candidate in ImplementationFiles) Assert.Contains(candidate, consumer.DependencyFiles);
        Assert.Contains("src/Model/IValue.cs", consumer.DependencyFiles);
        Assert.DoesNotContain("src/Model/Independent.cs", consumer.DependencyFiles);
        var independent = Batch(plan, "src/Model/Independent.cs");
        Assert.Equal(["tests/Model.Tests/IndependentTests.cs"], independent.TestFiles);
        Assert.DoesNotContain("src/Model/Consumer.cs", independent.DependencyFiles);
        foreach (var candidate in ImplementationFiles) Assert.DoesNotContain(candidate, independent.DependencyFiles);
        Assert.Empty(fixture.Native.Calls());
    }

    [Theory]
    [InlineData("src/Model/FirstValue.cs", "public virtual int Read() => 1;", "public virtual int Read() => 4;")]
    [InlineData("src/Model/SecondValue.cs", "public int Read() => 2;", "public int Read() => 4;")]
    [InlineData("src/Model/DerivedValue.cs", "public override int Read() => 3;", "public override int Read() => 4;")]
    public void CompilerImplementationChangeInvalidatesTheInterfaceConsumerCache(string implementation, string original, string replacement)
    {
        using var fixture = CompilerFixture.InterfaceDispatch();
        fixture.EstablishUnitCache();
        var cached = fixture.Plan();
        Assert.All(cached.Batches, batch => Assert.True(batch.UnitCurrent));
        var before = Batch(cached, "src/Model/Consumer.cs").UnitFingerprint;

        var contents = File.ReadAllText(fixture.Native.Full(implementation));
        Assert.Contains(original, contents, StringComparison.Ordinal);
        fixture.Native.Write(implementation, contents.Replace(original, replacement, StringComparison.Ordinal));

        var changed = fixture.Plan();
        Assert.False(Batch(changed, implementation).UnitCurrent);
        var consumer = Batch(changed, "src/Model/Consumer.cs");
        Assert.False(consumer.UnitCurrent);
        Assert.NotEqual(before, consumer.UnitFingerprint);
        Assert.Contains(implementation, consumer.DependencyFiles);
        Assert.True(Batch(changed, "src/Model/Independent.cs").UnitCurrent);
        Assert.Equal(cached.Batches.Count, fixture.Native.Calls().Length);
    }

    [Fact]
    public void CompilerPartialInterfaceDeclarationsRemainInTheConsumerIdentity()
    {
        using var fixture = CompilerFixture.PartialInterface();
        var graph = fixture.Snapshot().Graph!;
        var relation = Assert.Single(graph.Edges, edge => edge.Type == "implements"
            && HasLocation(graph, edge.From, "src/Model/Value.cs")
            && HasLocation(graph, edge.To, "src/Model/IValue.cs"));
        Assert.True(HasLocation(graph, relation.To, "src/Model/IValue.Members.cs"));
        Assert.Contains(graph.Edges, edge => edge.Type == "calls"
            && HasLocation(graph, edge.From, "src/Model/Consumer.cs")
            && HasLocation(graph, edge.To, "src/Model/IValue.Members.cs"));

        var selected = fixture.Plan();
        foreach (var part in new[] { "src/Model/IValue.cs", "src/Model/IValue.Members.cs" })
        {
            Assert.Contains("tests/Model.Tests/ConsumerTests.cs", Batch(selected, part).TestFiles);
            Assert.Contains(part, Batch(selected, "src/Model/Consumer.cs").DependencyFiles);
        }
        fixture.EstablishUnitCache();
        var before = Batch(fixture.Plan(), "src/Model/Consumer.cs").UnitFingerprint;
        fixture.Native.Write("src/Model/IValue.Members.cs", "namespace Fixture; public partial interface IValue { int Read(); int Version => 2; }");

        var changed = fixture.Plan();
        var consumer = Batch(changed, "src/Model/Consumer.cs");
        Assert.False(consumer.UnitCurrent);
        Assert.NotEqual(before, consumer.UnitFingerprint);
        Assert.True(Batch(changed, "src/Model/Independent.cs").UnitCurrent);
    }

    [Theory]
    [InlineData("property")]
    [InlineData("indexer")]
    [InlineData("event")]
    public void CompilerInterfaceMemberReferencesIncludeAlternateImplementationsInTheConsumerCache(string member)
    {
        using var fixture = CompilerFixture.InterfaceMember(member);
        var graph = fixture.Snapshot().Graph!;
        Assert.Contains(graph.Edges, edge => edge.Type == "references"
            && HasLocation(graph, edge.From, "src/Model/Consumer.cs")
            && HasLocation(graph, edge.To, "src/Model/IValue.cs"));
        var selected = fixture.Plan();
        var alternate = Batch(selected, "src/Model/OtherValue.cs");
        Assert.Equal("graph", alternate.Selection);
        Assert.Contains("tests/Model.Tests/DirectImplementationTests.cs", alternate.TestFiles);
        Assert.Contains("tests/Model.Tests/ConsumerTests.cs", alternate.TestFiles);
        Assert.DoesNotContain("tests/Model.Tests/IndependentTests.cs", alternate.TestFiles);
        var consumer = Batch(selected, "src/Model/Consumer.cs");
        Assert.Contains("src/Model/Value.cs", consumer.DependencyFiles);
        Assert.Contains("src/Model/OtherValue.cs", consumer.DependencyFiles);
        Assert.DoesNotContain("src/Model/Independent.cs", consumer.DependencyFiles);
        fixture.EstablishUnitCache();
        var before = Batch(fixture.Plan(), "src/Model/Consumer.cs").UnitFingerprint;
        var implementation = File.ReadAllText(fixture.Native.Full("src/Model/OtherValue.cs"));
        Assert.Contains("Result = 1", implementation, StringComparison.Ordinal);
        fixture.Native.Write("src/Model/OtherValue.cs", implementation.Replace("Result = 1", "Result = 2", StringComparison.Ordinal));

        var changed = fixture.Plan();
        Assert.False(Batch(changed, "src/Model/Consumer.cs").UnitCurrent);
        Assert.NotEqual(before, Batch(changed, "src/Model/Consumer.cs").UnitFingerprint);
        Assert.True(Batch(changed, "src/Model/Independent.cs").UnitCurrent);
    }

    [Fact]
    public void CompilerBaseTypedVirtualDispatchRetainsEveryDerivedCandidateInTheConsumerCache()
    {
        using var fixture = CompilerFixture.VirtualDispatch();
        var graph = fixture.Snapshot().Graph!;
        Assert.Contains(graph.Edges, edge => edge.Type == "calls"
            && HasLocation(graph, edge.From, "src/Model/Consumer.cs")
            && HasLocation(graph, edge.To, "src/Model/Base.cs"));
        var selected = fixture.Plan();
        foreach (var candidate in new[] { "src/Model/FirstDerived.cs", "src/Model/SecondDerived.cs" })
        {
            Assert.Contains("tests/Model.Tests/ConsumerTests.cs", Batch(selected, candidate).TestFiles);
            Assert.DoesNotContain("tests/Model.Tests/IndependentTests.cs", Batch(selected, candidate).TestFiles);
            Assert.Contains(candidate, Batch(selected, "src/Model/Consumer.cs").DependencyFiles);
        }
        fixture.EstablishUnitCache();
        var before = Batch(fixture.Plan(), "src/Model/Consumer.cs").UnitFingerprint;
        fixture.Native.Write("src/Model/SecondDerived.cs", "namespace Fixture; public sealed class SecondDerived : Base { public override int Read() => 4; }");

        var changed = fixture.Plan();
        Assert.False(Batch(changed, "src/Model/Consumer.cs").UnitCurrent);
        Assert.NotEqual(before, Batch(changed, "src/Model/Consumer.cs").UnitFingerprint);
        Assert.True(Batch(changed, "src/Model/Independent.cs").UnitCurrent);
    }

    [Fact]
    public void CompilerPartialClassDeclarationsIncludeAnInitializerPartWithoutAMemberReference()
    {
        using var fixture = CompilerFixture.PartialClass();
        var graph = fixture.Snapshot().Graph!;
        var type = Assert.Single(graph.Nodes, node => node.Kind == "symbol" && node.Subtype == "class"
            && node.Properties.GetValueOrDefault("qualifiedName") == "Fixture.Value");
        Assert.Contains(type.Locations, location => location.Path == "src/Model/Value.cs");
        Assert.Contains(type.Locations, location => location.Path == "src/Model/Value.Initializer.cs");
        Assert.DoesNotContain(graph.Edges, edge => (edge.Type is "calls" or "references")
            && HasLocation(graph, edge.From, "tests/Model.Tests/ValueTests.cs")
            && HasLocation(graph, edge.To, "src/Model/Value.Initializer.cs"));

        var selected = fixture.Plan();
        foreach (var part in new[] { "src/Model/Value.cs", "src/Model/Value.Initializer.cs" })
        {
            var batch = Batch(selected, part);
            Assert.Equal("graph", batch.Selection);
            Assert.Equal(["tests/Model.Tests/ValueTests.cs"], batch.TestFiles);
            Assert.Contains("src/Model/Value.cs", batch.DependencyFiles);
            Assert.Contains("src/Model/Value.Initializer.cs", batch.DependencyFiles);
            Assert.DoesNotContain("src/Model/Independent.cs", batch.DependencyFiles);
        }
        fixture.EstablishUnitCache();
        var before = Batch(fixture.Plan(), "src/Model/Value.cs").UnitFingerprint;
        fixture.Native.Write("src/Model/Value.Initializer.cs", "namespace Fixture; public partial class Value { public int Result = 2; }");

        var changed = fixture.Plan();
        Assert.False(Batch(changed, "src/Model/Value.cs").UnitCurrent);
        Assert.False(Batch(changed, "src/Model/Value.Initializer.cs").UnitCurrent);
        Assert.NotEqual(before, Batch(changed, "src/Model/Value.cs").UnitFingerprint);
        Assert.True(Batch(changed, "src/Model/Independent.cs").UnitCurrent);
    }

    private static readonly string[] ImplementationFiles =
        ["src/Model/FirstValue.cs", "src/Model/SecondValue.cs", "src/Model/DerivedValue.cs"];

    private static IncrementalTestBatch Batch(IncrementalTestResult result, string source)
        => result.Batches.Single(batch => batch.SourceFile == source);

    private static bool HasLocation(CisGraphDocument graph, string key, string path)
        => graph.Nodes.Single(node => node.Key == key).Locations.Any(location => location.Path == path);

    private sealed class CompilerFixture : IDisposable
    {
        internal IncrementalTestServiceTests.Fixture Native { get; } = new();
        private readonly GraphSnapshotReader _reader;
        private readonly IncrementalTestService _service;

        private CompilerFixture(IReadOnlyDictionary<string, string> sources, IReadOnlyDictionary<string, string> tests)
        {
            // Keep the existing bounded protocol runner and cache machinery, but
            // replace its hand-authored snapshot with the actual compiler graph.
            foreach (var original in new[] { "src/A.cs", "src/B.cs", "src/Shared.cs", "tests/ATests.cs", "tests/BTests.cs" })
                File.Delete(Native.Full(original));
            foreach (var file in sources.Concat(tests)) Native.Write(file.Key, file.Value);
            Native.Write("src/Model/Model.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            Native.Write("tests/Model.Tests/Model.Tests.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../../src/Model/Model.csproj" /></ItemGroup>
                </Project>
                """);
            Native.Write("tests/Model.Tests/FactAttribute.cs", "namespace Fixture; public sealed class FactAttribute : System.Attribute { }");
            Native.Save(Native.Profile with
            {
                SourceFiles = sources.Keys.Order(StringComparer.Ordinal).ToArray(),
                TestFiles = tests.Keys.Where(path => path.EndsWith("Tests.cs", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray(),
                ConfigFiles = ["config.json", "src/Model/Model.csproj", "tests/Model.Tests/Model.Tests.csproj"],
                MutationCommand = null,
                RecoveryCommand = null,
                CanarySourceFile = null,
                MaxRunBytes = 4 * 1024 * 1024,
                MaxRetainedBytes = 32 * 1024 * 1024
            });
            var initialized = new RepositoryInitializer().Initialize(new(Native.Path, "docs/cis", DryRun: false, Confirmed: true));
            Assert.Equal(0, initialized.ExitCode);
            var resolver = new CisRepositoryContextResolver();
            var catalog = new DocumentationCatalogReader();
            var inspector = new MarkdownDocumentInspector();
            var inventory = new DocumentationInventoryService(resolver, catalog, inspector);
            var builder = new GraphBuilder(resolver, catalog, inventory, new DocumentationValidationService(resolver, catalog, inventory, inspector));
            _reader = new(resolver, builder: builder);
            // Reuse supported work/storage admission for this controlled protocol
            // fixture. It does not assert a real ledger allocation or task approval.
            var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(Native.Path);
            _service = new(_reader, authority, authority);
            Snapshot();
        }

        internal static CompilerFixture Inheritance() => new(new Dictionary<string, string>
        {
            ["src/Model/Base.cs"] = "namespace Fixture; public class Base { public int Value = 1; }",
            ["src/Model/Derived.cs"] = "namespace Fixture; public sealed class Derived : Base { }",
            ["src/Model/Independent.cs"] = "namespace Fixture; public sealed class Independent { public int Read() => System.Math.Abs(-1); }"
        }, new Dictionary<string, string>
        {
            ["tests/Model.Tests/BaseTests.cs"] = "namespace Fixture; public sealed class BaseTests { [Fact] public void ValueIsPositive() { if (new Base().Value <= 0) throw new System.InvalidOperationException(); } }",
            // Reflection keeps this inheritance regression dependent on the
            // compiler's type relation, rather than a direct Base.Value reference.
            ["tests/Model.Tests/DerivedTests.cs"] = "namespace Fixture; public sealed class DerivedTests { [Fact] public void InheritsValue() { var instance = new Derived(); var value = (int)instance.GetType().GetField(\"Value\")!.GetValue(instance)!; if (value != 1) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/IndependentTests.cs"] = IndependentTests
        });

        internal static CompilerFixture InterfaceDispatch() => new(new Dictionary<string, string>
        {
            ["src/Model/IValue.cs"] = "namespace Fixture; public interface IValue { int Read(); }",
            ["src/Model/FirstValue.cs"] = "namespace Fixture; public class FirstValue : IValue { public virtual int Read() => 1; }",
            ["src/Model/SecondValue.cs"] = "namespace Fixture; public sealed class SecondValue : IValue { public int Read() => 2; }",
            ["src/Model/DerivedValue.cs"] = "namespace Fixture; public sealed class DerivedValue : FirstValue { public override int Read() => 3; }",
            ["src/Model/Consumer.cs"] = "namespace Fixture; public sealed class Consumer { public int Read(IValue supplied) => System.Math.Abs(supplied.Read()); }",
            ["src/Model/Independent.cs"] = "namespace Fixture; public sealed class Independent { public int Read() => System.Math.Abs(-1); }"
        }, new Dictionary<string, string>
        {
            ["tests/Model.Tests/SuppliedValues.cs"] = "namespace Fixture; public static class SuppliedValues { public static IValue Current = new DerivedValue(); }",
            ["tests/Model.Tests/ConsumerTests.cs"] = "namespace Fixture; public sealed class ConsumerTests { private readonly IValue supplied = SuppliedValues.Current; [Fact] public void ReadsSuppliedValue() { if (new Consumer().Read(supplied) != 3) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/DirectImplementationTests.cs"] = "namespace Fixture; public sealed class DirectImplementationTests { [Fact] public void ChecksConcreteCandidates() { if (new FirstValue().Read() != 1 || new SecondValue().Read() != 2 || new DerivedValue().Read() != 3) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/IndependentTests.cs"] = IndependentTests
        });

        internal static CompilerFixture PartialInterface() => new(new Dictionary<string, string>
        {
            ["src/Model/IValue.cs"] = "namespace Fixture; public partial interface IValue { }",
            ["src/Model/IValue.Members.cs"] = "namespace Fixture; public partial interface IValue { int Read(); }",
            ["src/Model/Value.cs"] = "namespace Fixture; public sealed class Value : IValue { public int Read() => 1; }",
            ["src/Model/Consumer.cs"] = "namespace Fixture; public sealed class Consumer { public int Read(IValue supplied) => System.Math.Abs(supplied.Read()); }",
            ["src/Model/Independent.cs"] = IndependentSource
        }, new Dictionary<string, string>
        {
            ["tests/Model.Tests/SuppliedValues.cs"] = "namespace Fixture; public static class SuppliedValues { public static IValue Current = new Value(); }",
            ["tests/Model.Tests/ConsumerTests.cs"] = ConsumerTests,
            ["tests/Model.Tests/DirectImplementationTests.cs"] = "namespace Fixture; public sealed class DirectImplementationTests { [Fact] public void ChecksConcreteValue() { if (new Value().Read() != 1) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/IndependentTests.cs"] = IndependentTests
        });

        internal static CompilerFixture InterfaceMember(string member)
        {
            var (contract, implementation, read) = member switch
            {
                "property" => ("int Value { get; }", "public int Value => Result;", "public int Read(IValue supplied) => System.Math.Abs(supplied.Value);"),
                "indexer" => ("int this[int index] { get; }", "public int this[int index] => Result;", "public int Read(IValue supplied) => System.Math.Abs(supplied[0]);"),
                "event" => ("event System.Action Changed;", "public event System.Action Changed { add { System.GC.KeepAlive(Result); } remove { } }",
                    "public int Read(IValue supplied) { supplied.Changed += Listener; supplied.Changed -= Listener; return System.Math.Abs(-1); } private static void Listener() { }"),
                _ => throw new ArgumentOutOfRangeException(nameof(member))
            };
            return new(new Dictionary<string, string>
            {
                ["src/Model/IValue.cs"] = "namespace Fixture; public interface IValue { " + contract + " }",
                ["src/Model/Value.cs"] = "namespace Fixture; public sealed class MemberValue : IValue { private const int Result = 1; " + implementation + " }",
                ["src/Model/OtherValue.cs"] = "namespace Fixture; public sealed class OtherValue : IValue { private const int Result = 1; " + implementation + " }",
                ["src/Model/Consumer.cs"] = "namespace Fixture; public sealed class Consumer { " + read + " }",
                ["src/Model/Independent.cs"] = IndependentSource
            }, new Dictionary<string, string>
            {
                ["tests/Model.Tests/SuppliedValues.cs"] = "namespace Fixture; public static class SuppliedValues { public static IValue Current = new MemberValue(); }",
                ["tests/Model.Tests/ConsumerTests.cs"] = ConsumerTests,
                ["tests/Model.Tests/DirectImplementationTests.cs"] = "namespace Fixture; public sealed class DirectImplementationTests { [Fact] public void ConstructsConcreteValues() { _ = new MemberValue(); _ = new OtherValue(); } }",
                ["tests/Model.Tests/IndependentTests.cs"] = IndependentTests
            });
        }

        internal static CompilerFixture VirtualDispatch() => new(new Dictionary<string, string>
        {
            ["src/Model/Base.cs"] = "namespace Fixture; public class Base { public virtual int Read() => 1; }",
            ["src/Model/FirstDerived.cs"] = "namespace Fixture; public sealed class FirstDerived : Base { public override int Read() => 2; }",
            ["src/Model/SecondDerived.cs"] = "namespace Fixture; public sealed class SecondDerived : Base { public override int Read() => 3; }",
            ["src/Model/Consumer.cs"] = "namespace Fixture; public sealed class Consumer { public int Read(Base supplied) => System.Math.Abs(supplied.Read()); }",
            ["src/Model/Independent.cs"] = IndependentSource
        }, new Dictionary<string, string>
        {
            ["tests/Model.Tests/SuppliedValues.cs"] = "namespace Fixture; public static class SuppliedValues { public static Base Current = new FirstDerived(); }",
            ["tests/Model.Tests/ConsumerTests.cs"] = "namespace Fixture; public sealed class ConsumerTests { [Fact] public void ReadsSuppliedValue() { if (new Consumer().Read(SuppliedValues.Current) != 2) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/DirectImplementationTests.cs"] = "namespace Fixture; public sealed class DirectImplementationTests { [Fact] public void ChecksConcreteValues() { if (new Base().Read() != 1 || new FirstDerived().Read() != 2 || new SecondDerived().Read() != 3) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/IndependentTests.cs"] = IndependentTests
        });

        internal static CompilerFixture PartialClass() => new(new Dictionary<string, string>
        {
            ["src/Model/Value.cs"] = "namespace Fixture; public partial class Value { public Value() { } }",
            ["src/Model/Value.Initializer.cs"] = "namespace Fixture; public partial class Value { public int Result = 1; }",
            ["src/Model/Independent.cs"] = IndependentSource
        }, new Dictionary<string, string>
        {
            // The constructor is in the first declaration. Reflection observes
            // the second declaration without introducing a direct member edge.
            ["tests/Model.Tests/ValueTests.cs"] = "namespace Fixture; public sealed class ValueTests { [Fact] public void InitializesTheOtherDeclaration() { var instance = new Value(); var result = (int)instance.GetType().GetField(\"Result\")!.GetValue(instance)!; if (result != 1) throw new System.InvalidOperationException(); } }",
            ["tests/Model.Tests/IndependentTests.cs"] = IndependentTests
        });

        private const string IndependentSource = "namespace Fixture; public sealed class Independent { public int Read() => System.Math.Abs(-1); }";
        private const string ConsumerTests = "namespace Fixture; public sealed class ConsumerTests { [Fact] public void ReadsSuppliedValue() { if (new Consumer().Read(SuppliedValues.Current) != 1) throw new System.InvalidOperationException(); } }";

        private const string IndependentTests = "namespace Fixture; public sealed class IndependentTests { [Fact] public void ReadsIndependentValue() { if (new Independent().Read() != 1) throw new System.InvalidOperationException(); } }";

        internal CisGraphSnapshotReadResult Snapshot()
        {
            var result = _reader.ReadCurrent(Native.Path);
            Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Diagnostics.Select(item => item.Message))));
            Assert.Equal("fresh", result.Freshness);
            Assert.Contains("compiler.calls", result.GraphCapabilities);
            Assert.NotNull(result.Graph);
            return result;
        }

        internal IncrementalTestResult Plan()
        {
            var result = _service.Plan(new(Native.Path, "profile.json", "unit"));
            Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
            return result;
        }

        internal void EstablishUnitCache()
        {
            // These executable-fixture reports validate selection and reuse only;
            // they do not execute the sample classes' domain assertions.
            var result = _service.Run(new(Native.Path, "profile.json", "unit", 16, 120), 0, TestContext.Current.CancellationToken);
            Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
            Assert.Equal(Native.Profile.SourceFiles.Length, result.ExecutedUnitBatches);
            var calls = Native.Calls().Length;
            var reused = _service.Run(new(Native.Path, "profile.json", "unit", 16, 120), 0, TestContext.Current.CancellationToken);
            Assert.Equal(0, reused.ExitCode);
            Assert.Equal(0, reused.ExecutedUnitBatches);
            Assert.Equal(Native.Profile.SourceFiles.Length, reused.ReusedUnitBatches);
            Assert.Equal(calls, Native.Calls().Length);
        }

        public void Dispose() => Native.Dispose();
    }
}
