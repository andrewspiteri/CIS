using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Ai;
using Cis.Modules.Index;
using Cis.Modules.Repository;

namespace Cis.Modules.Index.Tests;

public sealed class FileIndexServiceTests
{
    [Fact]
    public void Build_WritesNonAuthoritativeCardAndIsIdempotent()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.cs", "public sealed class Orders { public void Create() { } }");
        var generation = new FakeGenerationService(local: true);
        var service = CreateService(generation);
        var request = Request(repository.Path);

        var first = service.Build(request);
        var second = service.Build(request);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("built", first.Status);
        Assert.True(first.Applied);
        Assert.Equal("unchanged", second.Status);
        Assert.False(second.Applied);
        Assert.Equal(1, generation.Calls);
        var card = Assert.Single(ReadCards(repository.Path));
        Assert.Equal("src/Orders.cs", card.Path);
        Assert.Equal("cis-file-index-card-v3", card.PromptVersion);
        var content = File.ReadAllText(Path.Combine(repository.Path,
            card.CardPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("non-authoritative", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_file: \"src/Orders.cs\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PathScopedRefreshPreservesCardsOutsideSelection()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/First.cs", "public sealed class First { }");
        repository.Write("src/Second.cs", "public sealed class Second { }");
        var generation = new FakeGenerationService(local: true);
        var service = CreateService(generation);
        Assert.Equal(0, service.Build(Request(repository.Path)).ExitCode);

        var refreshed = service.Build(Request(repository.Path) with
        {
            Path = "src/First.cs",
            Refresh = true,
            Limit = 1,
        });

        Assert.Equal(0, refreshed.ExitCode);
        Assert.Equal(2, ReadCards(repository.Path).Count);
        Assert.Contains(ReadCards(repository.Path), card => card.Path == "src/Second.cs");
    }

    [Fact]
    public void Build_NeverSubmitsSensitiveFileContentToModel()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(".env", "API_TOKEN=do-not-send");
        var generation = new FakeGenerationService(local: true);
        var result = CreateService(generation).Build(Request(repository.Path) with { Path = ".env" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, generation.Calls);
        var card = Assert.Single(ReadCards(repository.Path));
        Assert.True(card.Sensitive);
        Assert.Equal("deterministic", card.Provider);
        Assert.DoesNotContain("do-not-send", card.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ExcludesGeneratedAndroidBuildTreesButKeepsBuildScripts()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("build/Build.cs", "public sealed class Build { }");
        repository.Write("games/Test/build/release.ps1", "Write-Host release");
        repository.Write("games/Test/src/android/plugin/build/generated/BuildConfig.java", "class BuildConfig { }");
        repository.Write("games/Test/src/gd/android/build/assets/Loading.cs", "public sealed class Loading { }");

        var result = CreateService(new FakeGenerationService(local: true)).Build(Request(repository.Path) with { Path = null });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4, result.Candidates);
        var cards = ReadCards(repository.Path);
        Assert.Contains(cards, card => card.Path == "build/Build.cs");
        Assert.Contains(cards, card => card.Path == "games/Test/build/release.ps1");
        Assert.DoesNotContain(cards, card => card.Path.Contains("/android/plugin/build/", StringComparison.Ordinal));
        Assert.DoesNotContain(cards, card => card.Path.Contains("/gd/android/build/", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ExcludesDependencyAndReleaseArtifactDirectoriesFromDiscoveryAndCards()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/app.ts", "export const app = true;");
        repository.Write("node_modules/vendor/index.js", "export const vendor = true;");
        repository.Write("apps/web/node_modules/nested/package.json", "{\"name\":\"nested\"}");
        repository.Write("artifacts/release/copied.cs", "public sealed class Copied { }");
        repository.Write(".artifacts/staging/copied.ts", "export const copied = true;");
        repository.Write(".github/skills-quarantine/retired-skill/SKILL.md", "# Retired skill");
        var generation = new FakeGenerationService(local: true);

        var result = CreateService(generation).Build(Request(repository.Path) with { Path = null });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(ReadCards(repository.Path), card => card.Path == "src/app.ts");
        Assert.DoesNotContain(ReadCards(repository.Path), card =>
            card.Path.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ReadCards(repository.Path), card =>
            card.Path.Contains("artifacts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ReadCards(repository.Path), card =>
            card.Path.Contains("skills-quarantine", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Candidates, generation.Calls);
    }

    [Fact]
    public void Build_ReplacesUnsupportedTechnologyHallucinationWithSafeFallback()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("addons/export_plugin.gd", "extends EditorExportPlugin\nfunc _export_begin(): pass");
        var generation = new FakeGenerationService(local: true,
            summary: "This Unity plugin exports Android applications.");

        var result = CreateService(generation).Build(Request(repository.Path) with { Path = null });

        Assert.Equal(0, result.ExitCode);
        var card = Assert.Single(ReadCards(repository.Path), card => card.Path == "addons/export_plugin.gd");
        Assert.DoesNotContain("Unity", card.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Godot script", card.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RequiresExplicitRemoteAuthorization()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.cs", "public sealed class Orders { }");
        var generation = new FakeGenerationService(local: false);

        var result = CreateService(generation).Build(Request(repository.Path) with
        {
            Provider = "remote-test",
        });

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("remote-approval-required", result.Status);
        Assert.Equal(0, generation.Calls);
    }

    [Fact]
    public void StatusAndFind_ReportFreshRoutingMatchesWithoutModelCalls()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.cs", "public sealed class Orders { }");
        var generation = new FakeGenerationService(local: true);
        var service = CreateService(generation);
        Assert.Equal(0, service.Build(Request(repository.Path)).ExitCode);

        var status = service.Status(repository.Path, "src");
        var found = service.Find(repository.Path, "orders routing", 20);

        Assert.Equal("fresh", status.Status);
        Assert.Equal(1, status.Fresh);
        Assert.Equal("found", found.Status);
        Assert.Equal("src/Orders.cs", Assert.Single(found.Cards).Path);
        Assert.Equal(1, generation.Calls);
    }

    [Fact]
    public void Build_ReusesFreshCardsWhenProviderIsOffline()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.cs", "public sealed class Orders { }");
        var initial = new FakeGenerationService(local: true);
        Assert.Equal(0, CreateService(initial).Build(Request(repository.Path)).ExitCode);

        var result = CreateService(new UnavailableTextGenerationService()).Build(Request(repository.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("unchanged", result.Status);
        Assert.Equal(1, result.Reused);
    }

    [Fact]
    public void Build_CheckpointsManifestDuringLargeFirstRun()
    {
        using var repository = TemporaryRepository.Create();
        for (var index = 0; index < 11; index++)
        {
            repository.Write($"src/File{index:00}.cs", $"public sealed class File{index:00} {{ }}");
        }
        var manifestPath = Path.Combine(repository.Path, ".cis", "local", "index-cards", "index.json");
        var generation = new CheckpointGenerationService(manifestPath);

        var result = CreateService(generation).Build(Request(repository.Path));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(10, generation.CardCountObservedAtEleventhCall);
        Assert.Equal(11, ReadCards(repository.Path).Count);
    }

    [Fact]
    public void Doctor_ReportsMissingAndFreshIndexStates()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write("src/Orders.cs", "public sealed class Orders { }");
        var service = CreateService(new FakeGenerationService(local: true));
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        var doctor = new FileIndexDoctorCheck(service);

        var missing = Assert.Single(doctor.Inspect(context));
        Assert.Equal("CIS-INDEX-001", missing.Code);
        Assert.Equal(0, service.Build(Request(repository.Path) with { Path = null }).ExitCode);
        var fresh = Assert.Single(doctor.Inspect(context));
        Assert.Equal("CIS-INDEX-003", fresh.Code);
    }

    [Fact]
    public void AiStatusCommand_IsRegisteredByExplicitModule()
    {
        using var application = new CisHostBuilder()
            .AddModule(new AiModule())
            .Build();

        Assert.Equal(0, application.Invoke(["ai", "status", "--help"]));
    }

    [Fact]
    public void IndexCommands_AreRegisteredByExplicitModule()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new IndexModule())
            .Build();

        Assert.Equal(0, application.Invoke(["index", "build", "--help"]));
        Assert.Equal(0, application.Invoke(["index", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["index", "find", "--help"]));
    }

    private static FileIndexService CreateService(ICisTextGenerationService generation) =>
        new(new CisRepositoryContextResolver(), generation);

    private static FileIndexBuildRequest Request(string repositoryPath) =>
        new(repositoryPath, "src", null, null, false, 0, 12_000, false);

    private static IReadOnlyList<FileIndexCard> ReadCards(string repositoryPath)
    {
        var path = Path.Combine(repositoryPath, ".cis", "local", "index-cards", "index.json");
        return JsonSerializer.Deserialize<FileIndexCard[]>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }) ?? [];
    }

    private sealed class FakeGenerationService(bool local, string? summary = null) : ICisTextGenerationService
    {
        public int Calls { get; private set; }

        public CisAiStatus GetStatus() => new([
            new CisAiProviderStatus(local ? "local-test" : "remote-test", "available",
                "http://test", local, [new CisAiModel("cheap-test", 10)], null),
        ]);

        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++;
            return new("generated", local ? "local-test" : "remote-test", "cheap-test",
                summary ?? "Routes order creation behavior and should be loaded for orders changes.", null, local);
        }
    }

    private sealed class CheckpointGenerationService(string manifestPath) : ICisTextGenerationService
    {
        private int _calls;
        public int CardCountObservedAtEleventhCall { get; private set; } = -1;

        public CisAiStatus GetStatus() => new([
            new CisAiProviderStatus("local-test", "available", "http://test", true,
                [new CisAiModel("cheap-test", 10)], null),
        ]);

        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            _calls++;
            if (_calls == 11 && File.Exists(manifestPath))
            {
                CardCountObservedAtEleventhCall = JsonSerializer.Deserialize<FileIndexCard[]>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })?.Length ?? -1;
            }
            return new("generated", "local-test", "cheap-test",
                "Routes a focused source responsibility for repository changes.", null, true);
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-index-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var repository = new TemporaryRepository(path);
            repository.Write(".cis/repository.yml", """
                schema_version: 1
                repository:
                  id: index-test
                documentation_root: docs
                """);
            repository.Write("docs/catalog.yml", "schema_version: 1\ndocuments: []\n");
            return repository;
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
