using Cis.Host;
using Cis.Modules.Repository;

namespace Cis.Modules.Docs.Tests;

public sealed class DocumentationServicesTests
{
    [Fact]
    public void Inventory_UsesCatalogMetadataAndKeepsUncatalogedDocumentsVisible()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteDocumentation(
            "specs/delivery.md",
            "---\ntitle: Delivery behavior\ntype: specification\nstatus: Draft\n---\n# Delivery\n");
        repository.WriteDocumentation("references/runtime.md", "# Runtime inventory\n");
        var inferredTypes = new Dictionary<string, string>
        {
            ["policies/security.md"] = "policy",
            ["standards/api.md"] = "standard",
            ["workflows/delivery.md"] = "workflow",
            ["procedures/release.md"] = "procedure",
            ["sops/operations.md"] = "sop",
            ["runbooks/outage.md"] = "runbook",
            ["playbooks/incident.md"] = "playbook",
            ["checklists/review.md"] = "checklist",
            ["plans/roadmap.md"] = "plan",
            ["guides/onboarding.md"] = "guide",
            ["templates/feature.md"] = "template",
        };
        foreach (var path in inferredTypes.Keys)
        {
            repository.WriteDocumentation(path, $"# {path}\n");
        }
        repository.WriteCatalog(
            CatalogEntry(
                "example:spec:delivery",
                "docs/specs/delivery.md",
                "technical-specification"));
        var service = CreateInventoryService();

        var result = service.Inventory(repository.Path);

        Assert.Equal(0, result.ExitCode);
        var cataloged = Assert.Single(
            result.Documents,
            document => document.Path == "docs/specs/delivery.md");
        Assert.Equal("example:spec:delivery", cataloged.Id);
        Assert.Equal("technical-specification", cataloged.Type);
        Assert.Equal("catalog", cataloged.MetadataSource);
        Assert.True(cataloged.Cataloged);

        var inferred = Assert.Single(
            result.Documents,
            document => document.Path == "docs/references/runtime.md");
        Assert.Equal("reference", inferred.Type);
        Assert.Equal("inferred", inferred.MetadataSource);
        Assert.False(inferred.Cataloged);

        foreach (var expected in inferredTypes)
        {
            var document = Assert.Single(
                result.Documents,
                document => document.Path == $"docs/{expected.Key}");
            Assert.Equal(expected.Value, document.Type);
        }
    }

    [Fact]
    public void Validate_AllowsWarningsUnlessStrict()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteDocumentation(
            "specs/delivery.md",
            "---\ntitle: Delivery\ntype: specification\nstatus: Draft\n---\n# Delivery\n");
        repository.WriteCatalog(
            CatalogEntry(
                "example:spec:delivery",
                "docs/specs/delivery.md",
                "technical-specification"));
        var service = CreateValidationService();

        var normal = service.Validate(repository.Path, strict: false);
        var strict = service.Validate(repository.Path, strict: true);

        Assert.True(normal.IsValid);
        Assert.Equal(0, normal.ExitCode);
        Assert.Contains(normal.Warnings, warning => warning.Contains("docs/README.md", StringComparison.Ordinal));
        Assert.False(strict.IsValid);
        Assert.Equal(5, strict.ExitCode);
    }

    [Fact]
    public void Validate_RejectsDuplicateStableIds()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteDocumentation("specs/one.md", "# One\n");
        repository.WriteDocumentation("specs/two.md", "# Two\n");
        repository.WriteCatalog(
            CatalogEntry("example:spec:duplicate", "docs/specs/one.md", "specification"),
            CatalogEntry("EXAMPLE:SPEC:DUPLICATE", "docs/specs/two.md", "specification"));
        var service = CreateValidationService();

        var result = service.Validate(repository.Path, strict: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("duplicate id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsMissingCatalogTarget()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteCatalog(
            CatalogEntry("example:spec:missing", "docs/specs/missing.md", "specification"));
        var service = CreateValidationService();

        var result = service.Validate(repository.Path, strict: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("missing file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsCatalogPathOutsideDocumentationRoot()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        File.WriteAllText(System.IO.Path.Combine(repository.Path, "outside.md"), "# Outside\n");
        repository.WriteCatalog(
            CatalogEntry("example:spec:outside", "outside.md", "specification"));
        var service = CreateValidationService();

        var result = service.Validate(repository.Path, strict: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("inside docs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsMalformedFrontMatterInCatalogedDocument()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteDocumentation("specs/broken.md", "---\ntitle: [\n# Broken\n");
        repository.WriteCatalog(
            CatalogEntry("example:spec:broken", "docs/specs/broken.md", "specification"));
        var service = CreateValidationService();

        var result = service.Validate(repository.Path, strict: false);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inventory_RejectsRepositoryWithoutCisConfiguration()
    {
        using var repository = TemporaryRepository.Create();
        var service = CreateInventoryService();

        var result = service.Inventory(repository.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("configuration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DocsCommands_AreDispatchedThroughExplicitModules()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new DocsModule())
            .Build();

        var inventoryExitCode = application.Invoke(
            ["docs", "inventory", "--repo", repository.Path, "--format", "json"]);
        var validateExitCode = application.Invoke(
            ["docs", "validate", "--repo", repository.Path, "--format", "agent"]);

        Assert.Equal(0, inventoryExitCode);
        Assert.Equal(0, validateExitCode);
    }

    [Fact]
    public void DocumentationDoctorCheck_ReportsDraftsAndTodoMarkers()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        var resolution = new CisRepositoryContextResolver().Resolve(repository.Path);
        var check = new DocumentationDoctorCheck(
            CreateValidationService(),
            CreateInventoryService());

        var findings = check.Inspect(resolution.Context!);

        Assert.Contains(findings, finding => finding.Code == "CIS-DOC-004");
        Assert.Contains(findings, finding => finding.Code == "CIS-DOC-005");
        Assert.DoesNotContain(findings, finding => finding.Severity == "error");
    }

    [Fact]
    public void Validate_StrictlyAcceptsClassificationGeneratedDocumentation()
    {
        using var repository = TemporaryRepository.Create();
        repository.WriteRepositoryFile(
            "src/Example.Api/Example.Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repository.WriteRepositoryFile(
            "src/Example.Api/Program.cs",
            "builder.Services.AddControllers(); builder.Services.AddAuthorization(); app.MapControllers();");
        var initResult = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
            repository.Path,
            "docs/cis",
            DryRun: false,
            Confirmed: true));
        var service = CreateValidationService();

        var result = service.Validate(repository.Path, strict: true);

        Assert.Equal(0, initResult.ExitCode);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    private static DocumentationInventoryService CreateInventoryService()
    {
        return new DocumentationInventoryService(
            new CisRepositoryContextResolver(),
            new DocumentationCatalogReader(),
            new MarkdownDocumentInspector());
    }

    private static DocumentationValidationService CreateValidationService()
    {
        var resolver = new CisRepositoryContextResolver();
        var reader = new DocumentationCatalogReader();
        var inspector = new MarkdownDocumentInspector();
        var inventory = new DocumentationInventoryService(resolver, reader, inspector);
        return new DocumentationValidationService(resolver, reader, inventory, inspector);
    }

    private static string CatalogEntry(string id, string path, string type) =>
        $"  - id: {id}\n" +
        $"    path: {path}\n" +
        $"    type: {type}\n" +
        "    status: draft\n" +
        "    authority: canonical\n";

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cis-docs-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public static TemporaryRepository CreateInitialized()
        {
            var repository = Create();
            var result = new RepositoryInitializer().Initialize(new RepositoryInitRequest(
                repository.Path,
                "docs",
                DryRun: false,
                Confirmed: false));
            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Applied);
            return repository;
        }

        public void WriteCatalog(params string[] entries)
        {
            File.WriteAllText(
                System.IO.Path.Combine(Path, "docs", "catalog.yml"),
                "schema_version: 1\n" +
                "repository: example\n" +
                "documents:\n" +
                string.Concat(entries));
        }

        public void WriteDocumentation(string relativePath, string content)
        {
            var absolutePath = System.IO.Path.Combine(
                Path,
                "docs",
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, content);
        }

        public void WriteRepositoryFile(string relativePath, string content)
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
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-docs-tests")) +
                System.IO.Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
