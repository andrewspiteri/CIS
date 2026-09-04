using Cis.Host;
using Cis.Modules.References;
using Cis.Modules.Repository;
using Cis.Abstractions;
using System.IO.Compression;
using System.Text;

namespace Cis.Modules.References.Tests;

public sealed class ReferenceGovernanceTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ReferencesModule())
            .Build();
        Assert.Equal(0, application.Invoke(["references", "discover", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "inventory", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "reconcile", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "diff", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "source", "import", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "source", "build", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "source", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["references", "source", "accept", "--help"]));
    }

    [Fact]
    public void Discover_CorrelatesMultipleReferenceFamiliesAndIsIdempotent()
    {
        using var repository = ReferenceRepository.Create(aligned: true);
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ReferencesModule())
            .Build();

        Assert.Equal(0, application.Invoke(["references", "discover", "--repo", repository.Path, "--format", "agent"]));
        var statePath = System.IO.Path.Combine(repository.Path, ".cis", "local", "references", "inventory.json");
        Assert.True(File.Exists(statePath));
        var first = File.ReadAllText(statePath);
        Assert.Equal(0, application.Invoke(["references", "discover", "--repo", repository.Path, "--format", "agent"]));
        var second = File.ReadAllText(statePath);
        Assert.Equal(first, second);
        Assert.Contains("PERM-LISTS-READ", first, StringComparison.Ordinal);
        Assert.Contains("CreateListCommand", first, StringComparison.Ordinal);
        Assert.Contains("ListCreatedEvent", first, StringComparison.Ordinal);
        Assert.Contains("Features:MaximumLists", first, StringComparison.Ordinal);
        Assert.Contains("schema_version", first, StringComparison.Ordinal);
        Assert.Contains("documentation_root", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateStrict_RejectsUndeclaredSourceIdentity()
    {
        using var repository = ReferenceRepository.Create(aligned: false);
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ReferencesModule())
            .Build();

        Assert.Equal(5, application.Invoke(["references", "validate", "--repo", repository.Path, "--strict", "--format", "agent"]));
        var state = File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "local", "references", "inventory.json"));
        Assert.Contains("PERM-LISTS-DELETE", state, StringComparison.Ordinal);
        Assert.Contains("\"canonicalDeclared\": false", state, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_ReportsCanonicalAndUnaccompaniedSourceChanges()
    {
        using var repository = ReferenceRepository.Create(aligned: true, initializeGit: true);
        repository.Append("src/ListFeature.cs", "\n[Authorize(Policy = \"PERM-LISTS-DELETE\")] public void Delete() { }\n");
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ReferencesModule())
            .Build();

        Assert.Equal(0, application.Invoke(["references", "diff", "--repo", repository.Path, "--base", "HEAD", "--format", "agent"]));
    }

    [Fact]
    public void Reconcile_IsConfirmedAdditiveAndMakesSupportedDriftStrictlyValid()
    {
        using var repository = ReferenceRepository.Create(aligned: true);
        repository.Replace("docs/cis/references/configuration-dictionary.md", ReferenceRepository.Table("Configuration Dictionary",
            "Name | Path | Allowed values | Refresh class | Owner | Sensitive | Description | Status | Evidence",
            "Features:MaximumLists | appsettings.json#Features:MaximumLists | integer | startup | maintainer | no | limit | Active | appsettings.json"));
        repository.Append("src/ListFeature.cs", "\nvar endpoint = Environment.GetEnvironmentVariable(\"CIS_NEW_ENDPOINT\");\n");
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new ReferencesModule()).Build();

        Assert.Equal(3, application.Invoke(["references", "reconcile", "--repo", repository.Path, "--kind", "configuration-dictionary", "--format", "agent"]));
        Assert.Equal(0, application.Invoke(["references", "reconcile", "--repo", repository.Path, "--kind", "configuration-dictionary", "--yes", "--format", "agent"]));
        Assert.Contains("CIS_NEW_ENDPOINT", File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "configuration-dictionary.md")), StringComparison.Ordinal);
        Assert.Equal(0, application.Invoke(["references", "validate", "--repo", repository.Path, "--strict", "--format", "agent"]));
    }

    [Fact]
    public void Discover_IgnoresCisInternalManifestAndReconcileSupportsScreenRoutes()
    {
        using var repository = ReferenceRepository.Create(aligned: true);
        repository.Replace(".cis/starter-manifest.yml", "definition: STATE-INTERNAL-MANIFEST\n");
        repository.Replace("docs/cis/internal.md", "Internal canonical identifier STATE-DOCUMENTATION-ONLY.\n");
        repository.Replace("web/app/page.tsx", "export default function Page() { return null; }\n");
        repository.Replace("web/package.json", "{\"name\":\"sample-web\",\"dependencies\":{\"next\":\"16.0.0\"}}\n");
        repository.Replace("docs/cis/references/package-catalogue.md", ReferenceRepository.Table("Package Catalogue",
            "Package | Component | Purpose | Version policy | Dependency class | Status | Evidence",
            "next | sample-web | runtime | pinned | dependency | Draft | web/package.json"));
        repository.Replace("docs/cis/references/screen-route-map.md", ReferenceRepository.Table("Screen Route Map",
            "Screen or route | Component | Platform | Access / permission | Destination | Status | Evidence | Notes",
            "TODO | TODO | web | TODO | TODO | Draft | TODO | TODO"));
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new ReferencesModule()).Build();

        Assert.Equal(0, application.Invoke(["references", "discover", "--repo", repository.Path, "--format", "agent"]));
        var state = File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "local", "references", "inventory.json"));
        Assert.DoesNotContain("STATE-INTERNAL-MANIFEST", state, StringComparison.Ordinal);
        Assert.DoesNotContain("STATE-DOCUMENTATION-ONLY", state, StringComparison.Ordinal);
        Assert.Contains("\"identity\": \"/\"", state, StringComparison.Ordinal);
        Assert.Contains("\"canonicalIdentity\": \"next@sample-web\"", state, StringComparison.Ordinal);
        Assert.Equal(0, application.Invoke(["references", "reconcile", "--repo", repository.Path,
            "--kind", "screen-route-map", "--yes", "--format", "agent"]));
        var routes = File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "screen-route-map.md"));
        Assert.Contains("| / | web | web | application-defined | / | Active | web/app/page.tsx:1 |", routes, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_PrunesGeneratedArtifactTreesBeforeSourceEnumeration()
    {
        using var repository = ReferenceRepository.Create(aligned: true);
        repository.Replace(
            "artifacts/clean-profile/agent-host/local-endpoint/Generated.cs",
            "[Authorize(Policy = \"PERM-ARTIFACT-ONLY\")] public sealed record GeneratedCommand;");
        repository.Replace(
            "tests/GeneratedFixture.cs",
            "[Authorize(Policy = \"PERM-TEST-ONLY\")] public sealed record TestOnlyCommand;");
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new ReferencesModule())
            .Build();

        Assert.Equal(0, application.Invoke(["references", "discover", "--repo", repository.Path, "--format", "agent"]));
        var state = File.ReadAllText(System.IO.Path.Combine(repository.Path, ".cis", "local", "references", "inventory.json"));
        Assert.DoesNotContain("PERM-ARTIFACT-ONLY", state, StringComparison.Ordinal);
        Assert.DoesNotContain("PERM-TEST-ONLY", state, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedCommand", state, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceImport_ProjectsWordEvidenceAndPreservesStableRegisteredDigest()
    {
        using var repository = ReferenceRepository.Create(aligned: true);
        var source = System.IO.Path.Combine(repository.Path, "docs", "ref", "proposal.docx");
        WriteDocx(source, "Scope", "Original governed scope", "00112233");
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new ReferencesModule()).Build();

        Assert.Equal(0, application.Invoke(["references", "source", "import", "--repo", repository.Path,
            "--file", source, "--assessment", "Reference", "--actor", "Andrew",
            "--reason", "Selected proposal evidence", "--format", "agent"]));
        var registry = File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "source-evidence.md"));
        var sourceId = registry.Split('\n').Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Select(cells => cells.FirstOrDefault(item => item.StartsWith("BRD-SRC-", StringComparison.Ordinal)))
            .First(item => item is not null)!;
        var registeredDigest = registry.Split('|').First(item => item.Trim().StartsWith("sha256:", StringComparison.Ordinal)).Trim();
        var projection = System.IO.Path.Combine(repository.Path, ".cis", "local", "references", sourceId);
        Assert.Contains("Original governed scope", File.ReadAllText(System.IO.Path.Combine(projection, "content.md")), StringComparison.Ordinal);
        Assert.Contains("scope--p-00112233", File.ReadAllText(System.IO.Path.Combine(projection, "source-map.json")), StringComparison.Ordinal);
        Assert.True(File.Exists(System.IO.Path.Combine(projection, "index-card.md")));

        var brd = $"# BRD\n\n## Traceability\n\n- {sourceId}#scope--p-00112233\n";
        repository.Replace("docs/cis/specs/business-requirements.md", brd);
        WriteDocx(source, "Scope", "Changed governed scope", "00112233");
        Assert.Equal(0, application.Invoke(["references", "source", "build", "--repo", repository.Path,
            "--id", sourceId, "--format", "agent"]));

        Assert.Equal(brd, File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "specs", "business-requirements.md")));
        Assert.Contains("\"materialToBrd\": true", File.ReadAllText(System.IO.Path.Combine(projection, "diff.json")), StringComparison.Ordinal);
        var currentRegistry = File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "source-evidence.md"));
        Assert.Contains(registeredDigest, currentRegistry, StringComparison.Ordinal);
        Assert.Contains("Changed governed scope", File.ReadAllText(System.IO.Path.Combine(projection, "content.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void SourceImport_ProjectsInitializedRepositoryGraphInsteadOfDumpingSourceCode()
    {
        using var repository = ReferenceRepository.Create(aligned: true);
        var graph = new CisGraphDocument(2,
            new CisGraphBuildMetadata("sha256:repository-graph", "references-fixture", "abc123", false, "complete"),
            [new CisGraphNode("references-fixture::endpoint::GET-/lists", "references-fixture", "endpoint", "api-operation",
                "GET /lists", "List discovery", ["contract"], "derived", "active",
                [new CisGraphLocation("src/ListFeature.cs", "line", "1")], new Dictionary<string, string>(), [])],
            [], new CisGraphDiagnosticSummary(0, 0, 0));
        var resolver = new CisRepositoryContextResolver();
        var service = new SourceEvidenceProjectionService(resolver, new DocumentationCatalogMerger(),
            new WorkspaceRegistry(resolver), new FakeGraphReader(repository.Path, graph));

        var result = service.Import(repository.Path, repository.Path, "Reference", "Andrew", "Existing product behavior");

        Assert.Equal(0, result.ExitCode);
        var source = Assert.Single(result.Sources);
        Assert.Equal("current", source.Status);
        var projection = File.ReadAllText(System.IO.Path.Combine(repository.Path, source.ProjectionPath!.Replace('/', System.IO.Path.DirectorySeparatorChar), "content.md"));
        Assert.Contains("Repository snapshot", projection, StringComparison.Ordinal);
        Assert.Contains("List discovery", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed record CreateListCommand", projection, StringComparison.Ordinal);
        var registry = File.ReadAllText(System.IO.Path.Combine(repository.Path, "docs", "cis", "references", "source-evidence.md"));
        Assert.Contains("workspace:references-fixture", registry, StringComparison.Ordinal);
        Assert.Contains("| repository |", registry, StringComparison.Ordinal);
    }

    private static void WriteDocx(string path, string heading, string paragraph, string paragraphId)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
        WriteEntry(archive, "word/styles.xml", "<?xml version=\"1.0\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style></w:styles>");
        WriteEntry(archive, "word/document.xml", $"<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w:body><w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>{heading}</w:t></w:r></w:p><w:p w14:paraId=\"{paragraphId}\"><w:r><w:t>{paragraph}</w:t></w:r></w:p></w:body></w:document>");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name); using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content); stream.Write(bytes);
    }

    private sealed class FakeGraphReader(string repositoryPath, CisGraphDocument graph) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string path) => new("available", 0, repositoryPath, "fresh", graph,
            new CisGraphManifest(2, graph.Build.Id, graph.Build.RepositoryId, "docs/cis", graph.Build.Head,
                graph.Build.Dirty, [], ["test"]), ["test"], [], []);
    }

    private sealed class ReferenceRepository : IDisposable
    {
        public string Path { get; }
        private ReferenceRepository(string path) => Path = path;
        public static ReferenceRepository Create(bool aligned, bool initializeGit = false)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-reference-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Write(root, ".cis/repository.yml", "schema_version: 1\nrepository:\n  id: references-fixture\ndocumentation_root: docs/cis\n");
            Write(root, "docs/cis/catalog.yml", "schema_version: 1\nrepository: references-fixture\ndocuments:\n");
            Write(root, "docs/cis/references/configuration-dictionary.md", Table("Configuration Dictionary", "Name | Path | Status | Evidence", "Features:MaximumLists | appsettings.json#Features:MaximumLists | Active | appsettings.json"));
            Write(root, "docs/cis/references/permissions-dictionary.md", Table("Permissions Dictionary", "Permission code | Permission name | Status | Source location", "PERM-LISTS-READ | Read lists | Active | src/ListFeature.cs"));
            Write(root, "docs/cis/references/command-dictionary.md", Table("Command Dictionary", "Command ID | Command name | Status | Evidence", "CMD-LISTS-CREATE | CreateListCommand | Active | src/ListFeature.cs"));
            Write(root, "docs/cis/references/event-dictionary.md", Table("Event Dictionary", "Event ID | Event name | Status | Evidence", "EVT-LISTS-CREATED | ListCreatedEvent | Active | src/ListFeature.cs"));
            Write(root, "appsettings.json", "{\"Features\":{\"MaximumLists\":100}}");
            Write(root, "src/ListFeature.cs", "[Authorize(Policy = \"PERM-LISTS-READ\")] public sealed record CreateListCommand; public sealed record ListCreatedEvent;");
            if (!aligned) Append(root, "src/ListFeature.cs", "\n[Authorize(Policy = \"PERM-LISTS-DELETE\")] public void Delete() { }\n");
            if (initializeGit)
            {
                Git(root, "init"); Git(root, "config", "user.email", "tests@cis.local"); Git(root, "config", "user.name", "CIS Tests");
                Git(root, "add", "."); Git(root, "commit", "-m", "baseline");
            }
            return new(root);
        }
        public void Append(string relative, string content) => Append(Path, relative, content);
        public void Replace(string relative, string content) => Write(Path, relative, content);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
        public static string Table(string title, string headers, string row)
        {
            var count = headers.Split('|').Length;
            return $"# {title}\n\n| {headers} |\n| {string.Join(" | ", Enumerable.Repeat("---", count))} |\n| {row} |\n";
        }
        private static void Write(string root, string relative, string content)
        {
            var path = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
        private static void Append(string root, string relative, string content) => File.AppendAllText(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)), content);
        private static void Git(string root, params string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
    }
}
