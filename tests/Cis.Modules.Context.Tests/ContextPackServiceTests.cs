using Cis.Abstractions;
using Cis.Modules.Context;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Microsoft.Data.Sqlite;

namespace Cis.Modules.Context.Tests;

public sealed class ContextPackServiceTests
{
    [Fact]
    public void Create_WritesDeterministicPackAndIsIdempotent()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateService();
        var request = Request(repository.Path);

        var first = service.Create(request);
        var second = service.Create(request);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("created", first.Status);
        Assert.True(first.Applied);
        Assert.NotNull(first.OutputPath);
        Assert.StartsWith(".cis/local/context/", first.OutputPath, StringComparison.Ordinal);
        Assert.Equal("unchanged", second.Status);
        Assert.False(second.Applied);
        Assert.Equal(first.OutputPath, second.OutputPath);
        var content = File.ReadAllText(ToAbsolute(repository.Path, first.OutputPath!));
        Assert.Contains("# CIS context pack", content, StringComparison.Ordinal);
        Assert.Contains("Purpose: change the orders component", content, StringComparison.Ordinal);
        Assert.Contains("Selection manifest", content, StringComparison.Ordinal);
        Assert.NotEmpty(first.Sources);
        Assert.Contains(
            first.Sources,
            source => source.Path.StartsWith("src/Orders.Api/", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_ReportsContentTruncationAndOmissionsAtBudget()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write("src/Orders.Api/Large.cs", "namespace Orders.Api; " + new string('x', 4_000));
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path) with { MaxCharacters = 1_000 });

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.ContentTruncated);
        Assert.True(result.Sources.Any(source => source.Truncated) || result.Omissions.Count > 0);
        Assert.True(result.Sources.Sum(source => source.IncludedCharacters) <= 1_000);
        Assert.All(result.Sources, source =>
        {
            Assert.True(source.Evidence.Count <= 25);
            Assert.Equal(source.TotalEvidence - source.Evidence.Count, source.OmittedEvidence);
        });
    }

    [Fact]
    public void Create_RejectsOutputOutsideDerivedContextRoot()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path) with
        {
            OutputPath = "docs/context.md",
        });

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-request", result.Status);
        Assert.False(File.Exists(Path.Combine(repository.Path, "docs", "context.md")));
    }

    [Fact]
    public void Create_DoesNotTraverseThroughSharedDependencyIntoUnrelatedComponent()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            result.Sources,
            source => source.Path.Contains("Unrelated.Worker", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_RequiresBuiltGraphWithoutCreatingContextDirectory()
    {
        using var repository = TemporaryRepository.CreateInitialized();

        var result = CreateService().Create(Request(repository.Path));

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("graph-unavailable", result.Status);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis", "local", "context")));
    }

    [Fact]
    public void Create_UsesLineLocatorsInsteadOfEmbeddingWholeSourceFile()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        var lines = Enumerable.Range(1, 200)
            .Select(line => line == 100
                ? "public sealed class LocatedEndpoint { }"
                : $"// filler line {line}");
        repository.Write("src/Orders.Api/LocatedEndpoint.cs", string.Join('\n', lines));
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path));

        var source = Assert.Single(
            result.Sources,
            source => source.Path == "src/Orders.Api/LocatedEndpoint.cs");
        Assert.True(source.SelectedCharacters < source.TotalCharacters);
        Assert.Contains(source.Excerpts, excerpt => excerpt.Locators.Any(locator => locator.StartsWith("line:", StringComparison.Ordinal)));
        Assert.Contains(source.Evidence, evidence => evidence.LocatorKind == "line");
    }

    [Fact]
    public void Create_OmitsRecognizedCredentialMaterialWithoutEchoingIt()
    {
        const string accessKey = "AKIA1234567890ABCDEF";
        using var repository = TemporaryRepository.CreateInitialized();
        repository.Write(
            "src/Orders.Api/Credentials.cs",
            $"namespace Orders.Api; public static class Credentials {{ public const string Key = \"{accessKey}\"; }}");
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path));

        Assert.DoesNotContain(result.Sources, source => source.Path.EndsWith("Credentials.cs", StringComparison.Ordinal));
        Assert.Contains(
            result.Omissions,
            omission => omission.Path.EndsWith("Credentials.cs", StringComparison.Ordinal)
                && omission.Reason == "sensitive-content:aws-access-key");
        Assert.DoesNotContain(accessKey, File.ReadAllText(ToAbsolute(repository.Path, result.OutputPath!)), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ReportsDeterministicTokenEstimates()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path));

        var includedCharacters = result.Sources.Sum(source => source.IncludedCharacters);
        Assert.Equal((includedCharacters + 3) / 4, result.EstimatedTokens);
        Assert.All(
            result.Sources,
            source => Assert.Equal((source.IncludedCharacters + 3) / 4, source.EstimatedTokens));
    }

    [Fact]
    public void Create_ReportsStaleGraphWithoutRebuildingIt()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        File.AppendAllText(
            Path.Combine(repository.Path, "src", "Orders.Api", "OrderEndpoint.cs"),
            " // changed after graph build");

        var result = CreateService().Create(Request(repository.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("stale", result.Freshness);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-QUERY-STALE-001");
    }

    [Fact]
    public void Create_RequiresForceToReplaceDifferentCustomPack()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        var service = CreateService();
        var firstRequest = Request(repository.Path) with { OutputPath = "custom.md" };
        var secondRequest = firstRequest with { Purpose = "a different change" };

        var first = service.Create(firstRequest);
        var collision = service.Create(secondRequest);
        var forced = service.Create(secondRequest with { Force = true });

        Assert.Equal("created", first.Status);
        Assert.Equal(4, collision.ExitCode);
        Assert.Equal("collision", collision.Status);
        Assert.False(collision.Applied);
        Assert.Equal("created", forced.Status);
        Assert.True(forced.Applied);
        Assert.Contains(
            "Purpose: a different change",
            File.ReadAllText(ToAbsolute(repository.Path, forced.OutputPath!)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsAmbiguousLocalIdentity()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path, """
            INSERT INTO nodes(key,repository_id,kind,subtype,local_id,label,authority,lifecycle,properties_json)
            SELECT repository_id || '::document::orders-api',repository_id,'document','test-duplicate',local_id,label,authority,lifecycle,properties_json
            FROM nodes WHERE kind='component' AND local_id='orders-api';
            """);

        var result = CreateService().Create(Request(repository.Path) with { Kind = null });

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-query", result.Status);
        Assert.Null(result.OutputPath);
    }

    [Fact]
    public void Create_ExcludesProposedRelationshipsByDefaultAndLabelsOptInEvidence()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);
        ExecuteSql(repository.Path, """
            UPDATE edges SET state='proposed'
            WHERE type='depends-on' AND from_key LIKE '%::component::orders-api'
              AND to_key LIKE '%system.commandline%';
            """);
        var service = CreateService();

        var strict = service.Create(Request(repository.Path));
        var interpreted = service.Create(Request(repository.Path) with
        {
            IncludeProposed = true,
            Purpose = "include proposed evidence",
        });

        Assert.DoesNotContain(strict.Sources, source => source.Path == "src/Orders.Api/Orders.Api.csproj");
        Assert.Contains(
            interpreted.Sources,
            source => source.Path == "src/Orders.Api/Orders.Api.csproj" && source.FactClass == "interpreted");
    }

    [Fact]
    public void Create_SupportsMultipleRootsWithinOneRepository()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path) with
        {
            AdditionalRoots =
            [
                new ContextPackRoot(repository.Path, "orders-api-tests", "component"),
            ],
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(2, result.Roots.Select(root => root.StartNode.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(result.Sources, source => source.Path.StartsWith("src/Orders.Api/", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Path.StartsWith("tests/Orders.Api.Tests/", StringComparison.Ordinal));
        Assert.Contains("-plus-1-", result.OutputPath!, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_FederatesIndependentRepositoryGraphsAndQualifiesEvidence()
    {
        using var primary = TemporaryRepository.CreateInitialized();
        using var secondary = TemporaryRepository.CreateInitialized();
        secondary.Write(
            "src/Orders.Api/SecondRepositoryMarker.cs",
            "namespace Orders.Api; public sealed class SecondRepositoryMarker { }");
        Assert.Equal(0, CreateBuilder().Build(primary.Path).ExitCode);
        Assert.Equal(0, CreateBuilder().Build(secondary.Path).ExitCode);

        var result = CreateService().Create(Request(primary.Path) with
        {
            AdditionalRoots =
            [
                new ContextPackRoot(secondary.Path, "orders-api", "component"),
            ],
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(2, result.Roots.Select(root => root.RepositoryId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(
            result.Sources,
            source => source.RepositoryPath == secondary.Path
                && source.Path == "src/Orders.Api/SecondRepositoryMarker.cs");
        var markdown = File.ReadAllText(ToAbsolute(primary.Path, result.OutputPath!));
        Assert.All(result.Roots, root => Assert.Contains(root.RepositoryId, markdown, StringComparison.Ordinal));
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Contains(
                result.Roots,
                root => diagnostic.Message.StartsWith($"[{root.RepositoryId}]", StringComparison.Ordinal)));
        Assert.False(Directory.Exists(Path.Combine(secondary.Path, ".cis", "local", "context")));
    }

    [Fact]
    public void Create_RequiresEveryAdditionalRootToResolve()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        Assert.Equal(0, CreateBuilder().Build(repository.Path).ExitCode);

        var result = CreateService().Create(Request(repository.Path) with
        {
            AdditionalRoots =
            [
                new ContextPackRoot(repository.Path, "missing-component", "component"),
            ],
        });

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("invalid-request", result.Status);
        Assert.Null(result.OutputPath);
    }

    private static ContextPackRequest Request(string repositoryPath)
        => new(
            repositoryPath,
            "orders-api",
            "component",
            "change the orders component",
            "connected unit tests",
            Depth: 2,
            Limit: 100,
            MaxCharacters: 20_000,
            IncludeProposed: false,
            OutputPath: null,
            Force: false);

    private static ContextPackService CreateService()
        => new(new GraphQueryService(new CisRepositoryContextResolver()));

    private static GraphBuilder CreateBuilder()
    {
        var resolver = new CisRepositoryContextResolver();
        var reader = new DocumentationCatalogReader();
        var inspector = new MarkdownDocumentInspector();
        var inventory = new DocumentationInventoryService(resolver, reader, inspector);
        var validation = new DocumentationValidationService(resolver, reader, inventory, inspector);
        return new GraphBuilder(resolver, reader, inventory, validation);
    }

    private static string ToAbsolute(string repositoryPath, string relativePath)
        => Path.Combine(repositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void ExecuteSql(string repositoryPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={new SqliteGraphStore().DatabasePath(repositoryPath)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository CreateInitialized()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cis-context-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var repository = new TemporaryRepository(path);
            repository.Write(
                "src/Orders.Api/Orders.Api.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" +
                "<PackageReference Include=\"System.CommandLine\" Version=\"2.0.10\" />" +
                "</ItemGroup></Project>");
            repository.Write(
                "src/Orders.Api/OrderEndpoint.cs",
                "namespace Orders.Api; public sealed class OrderEndpoint { }");
            repository.Write(
                "tests/Orders.Api.Tests/Orders.Api.Tests.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" +
                "<ProjectReference Include=\"../../src/Orders.Api/Orders.Api.csproj\" />" +
                "</ItemGroup></Project>");
            repository.Write(
                "tests/Orders.Api.Tests/OrderEndpointTests.cs",
                "namespace Orders.Api.Tests; public sealed class OrderEndpointTests { }");
            repository.Write(
                "src/Unrelated.Worker/Unrelated.Worker.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk.Worker\"><PropertyGroup>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" +
                "<PackageReference Include=\"System.CommandLine\" Version=\"2.0.10\" />" +
                "</ItemGroup></Project>");
            repository.Write(
                "src/Unrelated.Worker/Worker.cs",
                "namespace Unrelated.Worker; public sealed class Worker { }");
            var initialization = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
                repository.Path,
                "docs/cis",
                DryRun: false,
                Confirmed: true));
            Assert.Equal(0, initialization.ExitCode);
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
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-context-tests")) +
                System.IO.Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
