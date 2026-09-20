using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void PreviewAndConfirmationDoNotCreateRepositoryOrChangeAuthority()
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        Assert.Equal("preview", preview.Status);
        Assert.Equal(2, preview.Plan!.OpenDecisions.Count);
        Assert.Equal("sha256:baseline", preview.Plan.ProductDefinitionHash);
        Assert.Equal(3, f.Service.Import(f.Authority, f.Request, false, false).ExitCode);
        Assert.Equal("stale-preview", f.Service.Import(f.Authority, f.Request, false, true).Status);
        Assert.False(Directory.Exists(f.Request.RepositoryPath));
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void CreatePreservesSourceAndApprovalsRegistersOwnedRepositoryAndIsIdempotent()
    {
        using var f = new Fixture();
        var original = File.ReadAllBytes(f.Request.SourcePath);
        var approvals = File.ReadAllBytes(f.ApprovedBacklog);
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        var created = f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash);
        Assert.True(created.Applied, string.Join("; ", created.Errors));
        Assert.Equal("created", created.Status);
        Assert.True(Directory.Exists(Path.Combine(f.Request.RepositoryPath, ".git")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Authority, created.Plan!.SourcePath)));
        Assert.Equal(approvals, File.ReadAllBytes(f.ApprovedBacklog));
        var participant = Assert.Single(f.Registry.Resolve(f.Authority).Workspace!.Repositories,
            item => item.RepositoryPath == f.Request.RepositoryPath);
        Assert.True(participant.IsProductOwned);
        Assert.Equal("participant", participant.Role);
        var requestPath = Path.Combine(f.Authority, created.Plan.RequestPath);
        Assert.Contains("status: Draft", File.ReadAllText(requestPath), StringComparison.Ordinal);
        Assert.Contains("1. Who owns support?", File.ReadAllText(requestPath), StringComparison.Ordinal);
        var before = f.Snapshot();
        var repeated = f.Service.Import(f.Authority, f.Request, false, true, preview.Plan.PlanHash);
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
        Assert.Equal(before, f.Snapshot());

        var resolver = new CisRepositoryContextResolver();
        var reader = new DocumentationCatalogReader();
        var inspector = new MarkdownDocumentInspector();
        var inventory = new DocumentationInventoryService(resolver, reader, inspector);
        // The request and original-source link must be valid governed Markdown.
        var validation = new DocumentationValidationService(resolver, reader, inventory, inspector).Validate(f.Authority, true);
        Assert.DoesNotContain(validation.Errors.Concat(validation.Warnings), issue => issue.Contains(created.Plan.RequestPath, StringComparison.Ordinal));
    }

    [Fact]
    public void PendingFeatureScaffoldStopsBeingEmptyWhenImplementationOrDocumentsChange()
    {
        using var f = new Fixture();
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        Assert.Equal("created", f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash).Status);
        var repo = f.Registry.Resolve(f.Authority).Workspace!.Repositories.Single(item => item.RepositoryPath == f.Request.RepositoryPath);
        Assert.True(RepositoryStarterState.IsUnchangedScaffold(repo));
        foreach (var relative in new[] { "referral.cs", "docs/cis/specs/referral-brd.md", "docs/cis/specs/technical-intent-spec.md",
                     "docs/cis/README.md", "docs/cis/catalog.yml", ".github/workflows/build.yml" })
        {
            var path = Path.Combine(repo.RepositoryPath, relative);
            var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, "\nReal feature work\n");
            Assert.False(RepositoryStarterState.IsUnchangedScaffold(repo), relative);
            if (original is null) File.Delete(path); else File.WriteAllBytes(path, original);
            Assert.True(RepositoryStarterState.IsUnchangedScaffold(repo), relative);
        }
        File.Delete(Path.Combine(repo.RepositoryPath, "docs/cis/specs/technical-intent-spec.md"));
        Assert.False(RepositoryStarterState.IsUnchangedScaffold(repo));
    }

    [Fact]
    public void ExistingOwnedRepositoryIsReusedAndDependencyRemainsContextOnly()
    {
        using var f = new Fixture();
        var existing = Path.Combine(f.Root, "existing");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "Existing work");
        Assert.Equal(0, f.Importer.Import(new(f.Authority, "docs/cis", [existing], false, true, "owned", "none")).ExitCode);
        var external = Path.Combine(f.Root, "external");
        Directory.CreateDirectory(external);
        Assert.Equal(0, f.Importer.Import(new(f.Authority, "docs/cis", [external], false, true, "dependency", "producer")).ExitCode);
        var externalId = f.Registry.Resolve(f.Authority).Workspace!.Repositories.Single(item => item.RepositoryPath == external).Id;
        var request = f.Request with { RepositoryMode = "existing", RepositoryPath = existing, IntegrationRepositories = [externalId] };
        var preview = f.Service.Import(f.Authority, request, true, false);
        Assert.Equal("preview", preview.Status);
        Assert.Contains(preview.Warnings, warning => warning.Contains("integration context only", StringComparison.Ordinal));
        var created = f.Service.Import(f.Authority, request, false, true, preview.Plan!.PlanHash);
        Assert.Equal("created", created.Status);
        Assert.Equal("Existing work", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        Assert.True(f.Registry.Resolve(f.Authority).Workspace!.Repositories.Single(item => item.Id == externalId).IsDependency);
    }

    [Fact]
    public void ChangedSourceOrRegistryRequiresFreshPreview()
    {
        using var f = new Fixture();
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        File.AppendAllText(f.Request.SourcePath, "\nNew requirement\n");
        Assert.Equal("stale-preview", f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash).Status);
        preview = f.Service.Import(f.Authority, f.Request, true, false);
        var registryPath = f.Registry.Resolve(f.Authority).Workspace!.ConfigurationPath;
        File.AppendAllText(registryPath, "\n# Concurrent edit\n");
        Assert.Equal("stale-preview", f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash).Status);
        Assert.False(Directory.Exists(f.Request.RepositoryPath));
    }

    [Fact]
    public void RejectsExistingNewFolderAndUnsafeOrUnknownScopeWithoutOverwriting()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.Request.RepositoryPath);
        var marker = Path.Combine(f.Request.RepositoryPath, "keep.txt");
        File.WriteAllText(marker, "Keep");
        Assert.Equal("collision", f.Service.Import(f.Authority, f.Request, true, false).Status);
        Assert.Equal("Keep", File.ReadAllText(marker));
        foreach (var request in new[] {
            f.Request with { RepositoryPath = f.Authority, RepositoryMode = "existing" },
            f.Request with { RepositoryPath = Path.Combine(f.Authority, "nested") },
            f.Request with { DocumentationRoot = "../outside" },
            f.Request with { IntegrationRepositories = ["unknown"] },
            f.Request with { Slug = "../escape" },
        }) Assert.NotEmpty(f.Service.Import(f.Authority, request, true, false).Errors);
    }

    [Fact]
    public void CliRegistersAndDispatchesIntakeAndRejectsMalformedInput()
    {
        using var f = new Fixture();
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).Build();
        Assert.Equal(0, application.Invoke(["brd", "feature", "intake", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", "screens", "prepare", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", "architecture", "prepare", "--help"]));
        Assert.Equal(5, application.Invoke(["brd", "feature", "wizard", "architecture", "status", "--slug", "missing", "--workspace", f.Authority, "--format", "json"]));
        Assert.Equal(5, application.Invoke(["brd", "feature", "wizard", "screens", "status", "--slug", "missing", "--workspace", f.Authority, "--format", "json"]));
        var input = Path.Combine(f.Root, "request.json");
        File.WriteAllText(input, JsonSerializer.Serialize(f.Request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        string[] args = ["brd", "feature", "intake", "--input", input, "--workspace", f.Authority, "--format", "json", "--dry-run"];
        Assert.Equal(0, application.Invoke(args));
        File.WriteAllText(input, "{invalid}");
        Assert.Equal(5, application.Invoke(args));
        Assert.False(Directory.Exists(f.Request.RepositoryPath));
    }

    private sealed class Baseline : ICisProductDefinitionAuthority
    {
        public int Calls { get; private set; }
        public CisProductDefinitionAuthority Evaluate(string repositoryPath) { Calls++; return new(true, true, "session", "2026-09-15", "sha256:baseline", []); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-feature-intake-tests", Guid.NewGuid().ToString("N"));
        public string Authority { get; }
        public string ApprovedBacklog { get; }
        public WorkspaceRegistry Registry { get; }
        public RepositoryImporter Importer { get; }
        public FeatureIntakeService Service { get; }
        public CisFeatureIntakeRequest Request { get; }
        public Baseline Baseline { get; } = new();
        public Fixture()
        {
            Authority = Path.Combine(Root, "authority"); Directory.CreateDirectory(Authority);
            Registry = new(new CisRepositoryContextResolver());
            Assert.Equal(0, new WorkspaceInitializer(new RepositoryInitializer(), Registry)
                .Initialize(new(Authority, "docs/cis", false, true, "fixture", "fixture", "Fixture", "Fixture")).ExitCode);
            ApprovedBacklog = Path.Combine(Authority, "docs/cis/specs/high-level-backlog.md");
            Directory.CreateDirectory(Path.GetDirectoryName(ApprovedBacklog)!);
            File.WriteAllText(ApprovedBacklog, "---\nstatus: Active\nbacklog_mode: no-planned-work\n---\n\nNo planned work.\n");
            var source = Path.Combine(Root, "prepared.md");
            File.WriteAllText(source, "# Prepared feature BRD\n\n## Requirements\n\nFR-001 Preserve the exact source.\n\n## 19. Open business decisions\n\n1. Who owns support?\n2. Is direct handoff recorded?\n\n## References\n\nUnchanged source text.\n");
            Request = new("Referrals", "referrals", source, "new", Path.Combine(Root, "referrals"), "docs/cis", [], "Test reviewer");
            Importer = new(new RepositoryInitializer(), Registry);
            Service = new(Registry, Importer, new DocumentationCatalogMerger(), [Baseline]);
        }
        public string[] Snapshot() => Directory.GetFiles(Authority, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(file => Path.GetRelativePath(Authority, file) + ":" + Convert.ToBase64String(File.ReadAllBytes(file))).ToArray();
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cis-feature-intake-tests"));
            if (!CisPathSafety.IsUnderRoot(parent, Path.GetFullPath(Root))) throw new InvalidOperationException("Unsafe test cleanup path.");
            foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
