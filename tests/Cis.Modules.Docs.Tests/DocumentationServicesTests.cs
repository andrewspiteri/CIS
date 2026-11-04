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
    public void ExplicitlyLoadedDocumentOutsideDocumentationRootIsValidatedAndInventoried()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteRepositoryFile("legacy/brd.md", "---\ntitle: Imported BRD\nstatus: Draft\n---\n# Requirements\n");
        repository.WriteRepositoryFile(".cis/product-documents.json", "{\"business\":\"legacy/brd.md\"}");
        repository.WriteCatalog(CatalogEntry("example:spec:business-requirements", "legacy/brd.md", "business-requirements"));
        Assert.Empty(CreateValidationService().Validate(repository.Path, strict: false).Errors);
        var document = Assert.Single(CreateInventoryService().Inventory(repository.Path).Documents, item => item.Path == "legacy/brd.md");
        Assert.True(document.Cataloged);
        repository.WriteRepositoryFile(".cis/product-documents.json", "{\"business\":\"../outside.md\"}");
        Assert.NotEmpty(CreateValidationService().Validate(repository.Path, strict: false).Errors);
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
    public void DocumentationDoctorCheck_DistinguishesPendingWorkFromLiveTodoWarningsWithoutChangingDocuments()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        var states = new[] { "Draft", "Active", "Archived", "Retired", "Awaiting review" };
        var entries = states.Select((status, index) => (Status: status, Path: $"docs/specs/lifecycle-{index}.md", Id: $"example:spec:lifecycle-{index}")).ToArray();
        foreach (var entry in entries)
            repository.WriteDocumentation(entry.Path["docs/".Length..],
                $"---\ntitle: Lifecycle fixture\ntype: specification\nstatus: {entry.Status}\ncis:\n  stable_id: {entry.Id}\n---\n# Lifecycle fixture\n\nTODO: Record the required review evidence.\n");
        repository.WriteCatalog(entries.Select(entry => CatalogEntry(entry.Id, entry.Path, "specification")
            .Replace("status: draft", "status: " + entry.Status, StringComparison.Ordinal)).ToArray());
        var paths = entries.Select(entry => entry.Path).Append("docs/catalog.yml").ToArray();
        var before = paths.ToDictionary(path => path, path => File.ReadAllBytes(System.IO.Path.Combine(repository.Path, path)));
        var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        var check = new DocumentationDoctorCheck(CreateValidationService(), CreateInventoryService());

        var findings = check.Inspect(context);

        var drafts = Assert.Single(findings, finding => finding.Code == "CIS-DOC-004");
        Assert.Equal("info", drafts.Severity);
        Assert.Equal("review-required", drafts.Fixability);
        Assert.Contains(entries[0].Path, drafts.Evidence);
        foreach (var entry in entries)
        {
            var finding = Assert.Single(findings, finding => finding.Code == "CIS-DOC-005" && finding.Evidence.Contains(entry.Path));
            Assert.Equal(entry.Status is "Active" or "Awaiting review" ? "warning" : "info", finding.Severity);
            Assert.Equal(entry.Status is "Archived" or "Retired" ? "none" : "review-required", finding.Fixability);
        }
        var inventory = CreateInventoryService().Inventory(repository.Path);
        foreach (var entry in entries)
            Assert.Equal(entry.Status, Assert.Single(inventory.Documents, document => document.Path == entry.Path).Status);
        check.Inspect(context);
        foreach (var snapshot in before)
            Assert.Equal(snapshot.Value, File.ReadAllBytes(System.IO.Path.Combine(repository.Path, snapshot.Key)));
    }

    [Fact]
    public void DocumentationDoctorCheck_DoesNotTreatProductNameFriendsTodoAsTodoMarker()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        repository.WriteDocumentation(
            "specs/friends-todo.md",
            "---\ntitle: Friends Todo\ntype: specification\nstatus: Draft\n---\n# Friends Todo\n\nThe Friends Todo application is the reference product.\n");
        repository.WriteCatalog(CatalogEntry(
            "example:spec:friends-todo",
            "docs/specs/friends-todo.md",
            "specification"));
        var resolution = new CisRepositoryContextResolver().Resolve(repository.Path);
        var check = new DocumentationDoctorCheck(CreateValidationService(), CreateInventoryService());

        var findings = check.Inspect(resolution.Context!);

        Assert.DoesNotContain(
            findings.Where(finding => finding.Code == "CIS-DOC-005").SelectMany(finding => finding.Evidence),
            path => path.Equals("docs/specs/friends-todo.md", StringComparison.OrdinalIgnoreCase));
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
