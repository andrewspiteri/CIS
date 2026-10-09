namespace Cis.Modules.Graph.Tests;

public sealed partial class GraphBuilderTests
{
    [Theory]
    [InlineData("app/Reading.cs")]
    [InlineData("unmapped/Reading.cs")]
    public void Build_UnmappedDuplicateCSharpDeclarationCannotMergeWithOwnedSymbol(string unmappedPath)
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        var repositoryId = Path.GetFileName(repository.Path);
        var profilePath = Path.Combine(repository.Path, "docs/cis/references/repository-profile.md");
        File.WriteAllText(profilePath, File.ReadAllText(profilePath).Replace("### orders-api", "### " + repositoryId));
        repository.Write("src/Orders.Api/Reading.cs", "namespace Example; public class Reading { public int Read() => 1; }");
        repository.Write(unmappedPath, "namespace Example; public class Reading { public int Read() => System.Math.Abs(-2); }");

        var build = CreateBuilder().Build(repository.Path);
        Assert.Equal(0, build.ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var methods = graph.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(node => NodeValue(node, "kind") == "symbol" && NodeValue(node, "label") == "Example.Reading.Read()")
            .ToArray();
        Assert.Equal(2, methods.Length);
        var unmapped = Assert.Single(methods, node => node.GetProperty("properties").GetProperty("component").GetString() == "");
        var owned = Assert.Single(methods, node => node.GetProperty("properties").GetProperty("component").GetString() == repositoryId);
        var edges = graph.RootElement.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(edges, edge => NodeValue(edge, "type") == "calls" && NodeValue(edge, "from") == NodeValue(unmapped, "key"));
        Assert.DoesNotContain(edges, edge => NodeValue(edge, "type") == "calls" && NodeValue(edge, "from") == NodeValue(owned, "key"));
        Assert.DoesNotContain(edges, edge => NodeValue(edge, "type") == "belongs-to" && NodeValue(edge, "from") == NodeValue(unmapped, "key"));
        var ambiguousType = Assert.Single(graph.RootElement.GetProperty("nodes").EnumerateArray(), node =>
            NodeValue(node, "kind") == "symbol" && NodeValue(node, "label") == "Example.Reading");
        Assert.Equal("", ambiguousType.GetProperty("properties").GetProperty("component").GetString());
        Assert.DoesNotContain(edges, edge => NodeValue(edge, "type") == "belongs-to" && NodeValue(edge, "from") == NodeValue(ambiguousType, "key"));
        Assert.Contains(build.Diagnostics, finding => finding.Code == "CIS-GRAPH-CSHARP-002"
            && finding.Evidence.Contains(unmappedPath) && finding.Evidence.Contains("src/Orders.Api/Reading.cs"));
        Assert.NotEqual(0, CreateValidator().Validate(repository.Path, strict: true).ExitCode);
    }

    [Fact]
    public void Build_CSharpDeclarationsUseCompilerIdentityWithoutLexicalPhantoms()
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/Declarations.cs", """
            namespace Orders.Api;
            public readonly record struct Reading(int Value);
            public class Outer
            {
                public class Nested { }
                private const string Example = "class Phantom { }";
            }
            // class CommentOnly { }
            """);

        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var declarations = graph.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(node => NodeValue(node, "kind") == "symbol"
                && (!node.GetProperty("properties").TryGetProperty("external", out var external)
                    || external.GetString() != "true")
                && node.GetProperty("locations").EnumerateArray().Any(location =>
                    NodeValue(location, "path") == "src/Orders.Api/Declarations.cs"))
            .ToArray();

        Assert.Single(declarations, node => NodeValue(node, "label") == "Orders.Api.Reading");
        Assert.Single(declarations, node => NodeValue(node, "label") == "Orders.Api.Outer.Nested");
        Assert.DoesNotContain(declarations, node => NodeValue(node, "label") is
            "Orders.Api.struct" or "Orders.Api.Nested" or "Orders.Api.Phantom" or "Orders.Api.CommentOnly");
        Assert.All(declarations, node => Assert.Equal("compiler",
            node.GetProperty("properties").GetProperty("binding").GetString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_UnmappedCSharpSymbolsDoNotClaimRepositoryNamedComponent(bool matchingRepositoryName)
    {
        using var repository = TemporaryRepository.CreateInitializedApi();
        repository.Write("src/Orders.Api/Reading.cs", "namespace Orders.Api; public class Reading { public int Read() => 1; }");
        var profilePath = Path.Combine(repository.Path, "docs/cis/references/repository-profile.md");
        var profile = File.ReadAllText(profilePath).Replace("`src/Orders.Api`", "`unrelated`");
        if (matchingRepositoryName)
        {
            profile = profile.Replace("### orders-api", "### " + Path.GetFileName(repository.Path));
        }
        File.WriteAllText(profilePath, profile);

        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        using var graph = ReadGraphJson(repository.Path);
        var symbols = graph.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(node => NodeValue(node, "kind") == "symbol"
                && node.GetProperty("properties").TryGetProperty("binding", out var binding)
                && binding.GetString() == "compiler").ToArray();
        Assert.NotEmpty(symbols);
        Assert.All(symbols, node => Assert.Equal("", node.GetProperty("properties").GetProperty("component").GetString()));
        var symbolKeys = symbols.Select(node => NodeValue(node, "key")).ToHashSet();
        Assert.DoesNotContain(graph.RootElement.GetProperty("edges").EnumerateArray(), edge =>
            NodeValue(edge, "type") == "belongs-to" && symbolKeys.Contains(NodeValue(edge, "from"))
            || NodeValue(edge, "type") == "owns" && symbolKeys.Contains(NodeValue(edge, "to")));
        var validation = CreateValidator().Validate(repository.Path, strict: true);
        Assert.NotEqual(0, validation.ExitCode);
        Assert.Contains(validation.Diagnostics, finding => finding.Code == "CIS-GRAPH-VALIDATE-IDENTITY-001");
    }
}
