using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Graph.Tests;

public sealed partial class GraphBuilderTests
{
    [Fact]
    public void CanonicalCataloguePreservesIdsAndMapsLogicalPathsInsideOneProject()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, """
            | TI-MOD-READ | Reading | module | Reads | Reading behavior | Writing | BRD-READ-001 |
            | TI-MOD-WRITE | Writing | module | Writes | Writing behavior | Reading | BRD-WRITE-001 |
            """, """
            | TI-MOD-READ | owns | src/Orders.Api/Reading |
            | TI-MOD-WRITE | owns | src/Orders.Api/Writing |
            """);
        repository.Write("src/Orders.Api/Reading/Reader.cs", "namespace Example; public class Reader {}");
        repository.Write("src/Orders.Api/Writing/Writer.cs", "namespace Example; public class Writer {}");

        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        var read = Assert.Single(graph.Nodes, node => node.Kind == "component" && node.LocalId == "TI-MOD-READ");
        Assert.Equal("Reading", read.Label);
        Assert.Equal("BRD-READ-001", read.Properties["requirementIds"]);
        Assert.Contains(read.Provenance, item => item.Path.EndsWith("component-sheet.md", StringComparison.Ordinal)
            && item.LocatorKind == "line" && item.Extractor == "cis.markdown.component-sheet/1");
        Assert.Equal("TI-MOD-READ", Assert.Single(graph.Nodes, node => node.LocalId == "src/Orders.Api/Reading/Reader.cs").Properties["component"]);
        Assert.Equal("TI-MOD-WRITE", Assert.Single(graph.Nodes, node => node.LocalId == "src/Orders.Api/Writing/Writer.cs").Properties["component"]);
        Assert.Contains(graph.Nodes, node => node.Kind == "component" && node.LocalId == "orders-api");
    }

    [Fact]
    public void CatalogueWithoutLocationsDoesNotClaimTheEntireRepository()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, "| TI-MOD-DECLARED | Declared | module | Declaration | Domain | Other | BRD-READ-001 |", "");
        repository.Write("unmapped/Code.cs", "public class Code {}");
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        Assert.Equal("", Assert.Single(graph.Nodes, node => node.LocalId == "unmapped/Code.cs").Properties["component"]);
    }

    [Fact]
    public void ConflictingEqualRootsRemainUnownedButMoreSpecificRootCanResolve()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, """
            | TI-MOD-A | A | module | A | A | B | BRD-READ-001 |
            | TI-MOD-B | B | module | B | B | A | BRD-WRITE-001 |
            | TI-MOD-C | C | module | C | C | Other | BRD-READ-001 |
            """, """
            | TI-MOD-A | owns | src/Shared |
            | TI-MOD-B | owns | src/Shared |
            | TI-MOD-C | owns | src/Shared/Narrow |
            """);
        repository.Write("src/Shared/Unresolved.cs", "public class Unresolved {}");
        repository.Write("src/Shared/Narrow/Resolved.cs", "public class Resolved {}");
        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-003");
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        Assert.Equal("", Assert.Single(graph.Nodes, node => node.LocalId == "src/Shared/Unresolved.cs").Properties["component"]);
        Assert.Equal("TI-MOD-C", Assert.Single(graph.Nodes, node => node.LocalId == "src/Shared/Narrow/Resolved.cs").Properties["component"]);
    }

    [Fact]
    public void OneDiscoveredTestCanExplicitlyVerifyMultipleComponentsWithoutClaimingPass()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, """
            | TI-MOD-A | A | module | A | A | B | BRD-READ-001 |
            | TI-MOD-B | B | module | B | B | A | BRD-WRITE-001 |
            """, """
            | TI-MOD-A | verified-by | tests/SharedTests.cs |
            | TI-MOD-B | verified-by | tests/SharedTests.cs |
            """);
        repository.Write("tests/SharedTests.cs", "public class SharedTests { [Fact] public void VerifiesBoth() {} }");
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        var test = Assert.Single(graph.Nodes, node => node.Kind == "test" && node.Locations.Any(location => location.Path == "tests/SharedTests.cs"));
        var verifications = graph.Edges.Where(edge => edge.Type == "verified-by" && edge.To == test.Key).ToArray();
        Assert.Equal(2, verifications.Length);
        Assert.All(verifications, edge =>
        {
            Assert.Equal("declared", edge.State);
            Assert.Equal("not-recorded", edge.Properties["result"]);
            Assert.Equal("declared-applicable-check", edge.Properties["role"]);
            Assert.Equal("not-assessed", edge.Properties["coverageAssessment"]);
            Assert.Equal("not-assessed", edge.Properties["meaningAssessment"]);
            Assert.Contains(edge.Observations, evidence => evidence.Method == "declared-path");
        });
    }

    [Fact]
    public void LegacyProfileAndCatalogueMergeTheSameStableComponent()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, "| orders-api | Order host | service | Host | API | Storage | BRD-READ-001 |",
            "| orders-api | owns | src/Shared |");
        repository.Write("src/Shared/Code.cs", "public class SharedCode {}");
        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        var component = Assert.Single(graph.Nodes, node => node.Kind == "component" && node.LocalId == "orders-api");
        Assert.Contains(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-004");
        Assert.Equal(2, component.Locations.Count);
        Assert.Contains(component.Provenance, evidence => evidence.Extractor == "cis.markdown.repository-profile/1");
        Assert.Contains(component.Provenance, evidence => evidence.Extractor == "cis.markdown.component-sheet/1");
        Assert.Equal("orders-api", Assert.Single(graph.Nodes, node => node.LocalId == "src/Shared/Code.cs").Properties["component"]);
        Assert.Equal("orders-api", Assert.Single(graph.Nodes, node => node.LocalId == "src/Orders.Api/Program.cs").Properties["component"]);
    }

    [Fact]
    public void ConflictingCatalogueDeclarationsKeepStableIdentityAndSourceFactsWithDiagnostic()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, """
            | TI-MOD-A | First | module | First responsibility | A | B | BRD-READ-001 |
            | TI-MOD-A | Second | module | Other responsibility | A | B | BRD-READ-001 |
            """, "");
        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-004");
        var component = Assert.Single(new SqliteGraphStore().Read(repository.Path).Graph!.Nodes,
            node => node.Kind == "component" && node.LocalId == "TI-MOD-A");
        Assert.Equal(2, component.Locations.Count);
        Assert.Contains("First responsibility", component.Properties["responsibility"]);
        Assert.Contains("Other responsibility", component.Properties["responsibility"]);
    }

    [Fact]
    public void LocationOnlySheetCanPrecedeCanonicalMeaningWithoutDuplicatingIt()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, "| TI-MOD-A | Canonical meaning | module | Domain behavior | Domain | Other | BRD-READ-001 |", "");
        repository.Write("docs/cis/references/current-ownership.md", """
            ---
            title: Current ownership
            type: component-sheet
            status: Draft
            cis:
              stable_id: fixture:current-ownership
            ---

            # Current ownership

            ## Implementation and verification locations

            | Component ID | Relationship | Path |
            | --- | --- | --- |
            | TI-MOD-A | owns | src/Shared |
            """);
        var catalog = Path.Combine(repository.Path, "docs/cis/catalog.yml");
        File.WriteAllText(catalog, File.ReadAllText(catalog).Replace("documents:", """
            documents:
              - id: fixture:current-ownership
                path: docs/cis/references/current-ownership.md
                type: component-sheet
                status: draft
                authority: derived
            """, StringComparison.Ordinal));
        repository.Write("src/Shared/Code.cs", "public class SharedCode {}");
        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.DoesNotContain(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-002");
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        var component = Assert.Single(graph.Nodes, node => node.Kind == "component" && node.LocalId == "TI-MOD-A");
        Assert.Equal("Canonical meaning", component.Label);
        Assert.Equal("Domain behavior", component.Properties["responsibility"]);
        Assert.Equal("TI-MOD-A", Assert.Single(graph.Nodes, node => node.LocalId == "src/Shared/Code.cs").Properties["component"]);
        Assert.Contains(component.Provenance, item => item.Path == "docs/cis/references/current-ownership.md" && item.Method == "declared-path");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalComponentMeaningWinsOverDerivedRedefinitionInEitherCatalogueOrder(bool canonicalFirst)
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, "| TI-MOD-A | Canonical name | module | Canonical responsibility | Domain | Other | BRD-READ-001 |",
            "| TI-MOD-A | owns | src/Shared |");
        repository.Write("docs/cis/references/derived-components.md", """
            ---
            title: Derived component projection
            type: component-sheet
            status: Active
            cis:
              stable_id: fixture:derived-components
            ---

            # Derived component projection

            ## Component catalogue

            | Component ID | Name | Classification | Responsibility | Owns | Must not own | BRD authority |
            | --- | --- | --- | --- | --- | --- | --- |
            | TI-MOD-A | Derived name | service | Derived responsibility | Derived area | Derived exclusion | BRD-OTHER-001 |

            ## Implementation and verification locations

            | Component ID | Relationship | Path |
            | --- | --- | --- |
            | TI-MOD-A | owns | src/Other |
            """);
        const string derivedEntry = """

              - id: fixture:derived-components
                path: docs/cis/references/derived-components.md
                type: component-sheet
                status: active
                authority: derived
            """;
        var catalog = Path.Combine(repository.Path, "docs/cis/catalog.yml");
        if (canonicalFirst) File.AppendAllText(catalog, derivedEntry);
        else File.WriteAllText(catalog, File.ReadAllText(catalog).Replace("documents:", "documents:" + derivedEntry, StringComparison.Ordinal));
        repository.Write("src/Shared/Canonical.cs", "public class CanonicalCode {}");
        repository.Write("src/Other/Located.cs", "public class LocatedCode {}");

        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-004");
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        var component = Assert.Single(graph.Nodes, node => node.Kind == "component" && node.LocalId == "TI-MOD-A");
        Assert.Equal("Canonical name", component.Label);
        Assert.Equal("canonical", component.Authority);
        Assert.Equal("draft", component.Lifecycle);
        Assert.Equal("Canonical responsibility", component.Properties["responsibility"]);
        Assert.Equal("module", component.Properties["classification"]);
        Assert.Equal("BRD-READ-001", component.Properties["requirementIds"]);
        Assert.Contains("Derived responsibility", component.Properties["conflictingDeclarations"]);
        Assert.Equal(2, component.Locations.Count);
        Assert.Equal("TI-MOD-A", Assert.Single(graph.Nodes, node => node.LocalId == "src/Other/Located.cs").Properties["component"]);
    }

    [Fact]
    public void InvalidPathAndUnknownComponentMappingsProduceDiagnostics()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        WriteComponentSheet(repository, "| TI-MOD-A | A | module | A | A | B | BRD-READ-001 |", """
            | TI-MOD-A | owns | ../outside |
            | TI-MOD-A | owns | src/** |
            | TI-MOD-MISSING | verified-by | tests/SharedTests.cs |
            """);
        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-001");
        Assert.Contains(build.Diagnostics, item => item.Code == "CIS-GRAPH-COMPONENT-002");
    }

    private static void WriteComponentSheet(TemporaryRepository repository, string components, string locations)
    {
        repository.Write("docs/cis/references/component-sheet.md", """
            ---
            title: Components
            type: component-sheet
            status: Draft
            cis:
              stable_id: fixture:component-sheet
            ---

            # Components

            ## Component catalogue

            | Component ID | Name | Classification | Responsibility | Owns | Must not own | BRD authority |
            | --- | --- | --- | --- | --- | --- | --- |
            """ + "\n" + components + "\n\n## Implementation and verification locations\n\n"
            + "| Component ID | Relationship | Path |\n| --- | --- | --- |\n" + locations + "\n");
        File.AppendAllText(Path.Combine(repository.Path, "docs/cis/catalog.yml"), """

              - id: fixture:component-sheet
                path: docs/cis/references/component-sheet.md
                type: component-sheet
                status: draft
                authority: canonical
            """);
    }
}
