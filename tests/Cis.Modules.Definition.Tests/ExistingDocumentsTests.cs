using System.Text.Json;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Fact]
    public void SuppliedDocumentsAreCopiedIntoTheProjectAndOriginalsRemainUnchanged()
    {
        using var repository = TemporaryRepository.Create();
        using var supplied = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        var source = Path.Combine(supplied.Path, "Business requirements.md");
        const string original = "# Business requirements\n\n## Functional requirements\n\nKeep the supplied requirements.\n";
        File.WriteAllText(source, original);
        File.SetAttributes(source, FileAttributes.ReadOnly);
        try
        {
            var load = Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", "business", "--source", source, "--format", "json"]);
            Assert.Equal(0, load.ExitCode);
            var result = JsonSerializer.Deserialize<ExistingDocumentsResult>(load.Output, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(result.Applied);
            var selected = result.Documents.Single(item => item.Role == "business").SelectedPath!;
            Assert.StartsWith("docs/cis/imports/business/", selected);
            var copy = CisProductDocumentPaths.ValidatePath(repository.Path, selected);
            Assert.Equal(original, File.ReadAllText(copy));
            Assert.False(File.GetAttributes(copy).HasFlag(FileAttributes.ReadOnly));
            Assert.Equal(0, Invoke(application, ["graph", "build", "--workspace", repository.Path, "--format", "json"]).ExitCode);
            Assert.Equal(0, Invoke(application, ["brd", "reconcile", "--workspace", repository.Path, "--format", "json"]).ExitCode);
            Assert.Equal(original, File.ReadAllText(source));
            var reconciled = File.ReadAllText(copy);
            Assert.Contains("Keep the supplied requirements.", reconciled);
            Assert.Contains("status: Review Required", reconciled);
            // Selecting the same filename again cannot overwrite reviewed or reconciled work.
            Assert.Equal(0, Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", "business", "--source", source, "--format", "json"]).ExitCode);
            Assert.NotEqual(selected, CisProductDocumentPaths.Read(repository.Path)["business"]);
            Assert.Equal(reconciled, File.ReadAllText(copy));
            var selection = File.ReadAllText(Path.Combine(repository.Path, CisProductDocumentPaths.SelectionFile));
            foreach (var invalid in new[] { "relative.md", Path.Combine(supplied.Path, "missing.md"), Path.Combine(supplied.Path, "file.pdf") })
                Assert.Equal(2, Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", "business", "--source", invalid, "--format", "json"]).ExitCode);
            Assert.Equal(2, Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", "business", "--path", selected, "--source", source, "--format", "json"]).ExitCode);
            Assert.Equal(selection, File.ReadAllText(Path.Combine(repository.Path, CisProductDocumentPaths.SelectionFile)));
            Assert.Equal(original, File.ReadAllText(source));
        }
        finally { File.SetAttributes(source, FileAttributes.Normal); }
    }

    [Fact]
    public void Documents_DiscoverAcrossProjectAndLoadInPlaceWithoutChangingApproval()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        var roles = new Dictionary<string, string>
        {
            ["business"] = "brd-spec.md", ["technical"] = "technical-intent-spec.md", ["architecture"] = "solution-design.md",
            ["components"] = "component-sheet.md", ["diagrams"] = "architecture-diagrams.md",
            ["experience"] = "design-guidelines.md", ["delivery"] = "product-backlog.md",
        };
        const string content = "---\nstatus: Draft\n---\n# Existing project document\n\nKeep human-authored content.\n";
        foreach (var file in roles.Values)
        {
            var absolute = Path.Combine(repository.Path, "docs/applications/product", file);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, content);
        }
        var generated = Path.Combine(repository.Path, ".artifacts/validation-deps/BRD.md");
        Directory.CreateDirectory(Path.GetDirectoryName(generated)!);
        File.WriteAllText(generated, "# Business requirements");
        using var locked = new FileStream(generated, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var discovery = Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--format", "json"]);
        Assert.Equal(0, discovery.ExitCode);
        var found = JsonSerializer.Deserialize<ExistingDocumentsResult>(discovery.Output, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Empty(found.Warnings);
        Assert.False(found.Applied);
        Assert.False(File.Exists(Path.Combine(repository.Path, CisProductDocumentPaths.SelectionFile)));
        foreach (var (role, file) in roles)
        {
            var relative = "docs/applications/product/" + file;
            var candidate = Assert.Single(found.Documents.Single(item => item.Role == role).Candidates, item => item.Path == relative);
            Assert.Equal(new FileInfo(Path.Combine(repository.Path, relative)).Length, candidate.SizeBytes);
            Assert.NotNull(candidate.LastModifiedUtc);
            Assert.Contains("Keep human-authored content.", candidate.Summary);
            Assert.DoesNotContain("status: Draft", candidate.Summary);
            var loaded = Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", role, "--path", relative, "--format", "json"]);
            Assert.Equal(0, loaded.ExitCode);
            Assert.Equal(Path.Combine(repository.Path, relative.Replace('/', Path.DirectorySeparatorChar)),
                CisProductDocumentPaths.Resolve(Path.Combine(repository.Path, "docs/cis"), CisProductDocumentPaths.Defaults[role]));
            Assert.Equal(content, File.ReadAllText(Path.Combine(repository.Path, relative)));
        }
        Assert.Equal(0, Invoke(application, ["graph", "build", "--repo", repository.Path, "--format", "json"]).ExitCode);
        var brd = Invoke(application, ["brd", "status", "--workspace", repository.Path, "--format", "json"]);
        using var status = JsonDocument.Parse(brd.Output);
        Assert.True(status.RootElement.GetProperty("validation").ValueKind == JsonValueKind.Object, brd.Output);
        Assert.Equal("docs/applications/product/brd-spec.md", status.RootElement.GetProperty("canonicalPath").GetString());
        Assert.Equal("Draft", status.RootElement.GetProperty("validation").GetProperty("documentStatus").GetString());
        Assert.NotEqual("Active", status.RootElement.GetProperty("validation").GetProperty("effectiveStatus").GetString());

        var inventory = new ProductDefinitionAuthority(new Cis.Modules.Repository.CisRepositoryContextResolver()).EvidencePaths(repository.Path);
        Assert.Contains("docs/applications/product/brd-spec.md", inventory);
        Assert.DoesNotContain("docs/cis/specs/business-requirements.md", inventory);
        var selectionFile = Path.Combine(repository.Path, CisProductDocumentPaths.SelectionFile);
        var before = File.ReadAllText(selectionFile);
        foreach (var (role, invalid) in new[] { ("business", "../outside.md"), ("business", "missing.md"), ("unknown", "docs/applications/product/brd-spec.md"), ("technical", "docs/applications/product/brd-spec.md") })
        {
            Assert.Equal(2, Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", role, "--path", invalid, "--format", "json"]).ExitCode);
            Assert.Equal(before, File.ReadAllText(selectionFile));
        }
        var docs = Path.Combine(repository.Path, "docs/cis");
        var hash = ProductDefinitionAuthority.ComputeBaselineHash(docs, out _);
        File.AppendAllText(Path.Combine(repository.Path, "docs/applications/product/brd-spec.md"), "\nChanged scope.\n");
        Assert.NotEqual(hash, ProductDefinitionAuthority.ComputeBaselineHash(docs, out _));
    }

    [Fact]
    public void SelectedLegacyBrdIsBackedUpAndReconciledWithoutApprovingOrRewritingRequirements()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        const string relative = "docs/legacy/brd.md";
        var file = Path.Combine(repository.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        const string narrative = "\n\n# Existing requirements\n\n## Functional requirements\n\nUsers retain their original workflow.\n";
        const string original = "---\ntitle: Legacy requirements\nstatus: Draft\nowner: Existing owner\n---" + narrative;
        File.WriteAllText(file, original);
        Assert.Equal(0, Invoke(application, ["definition", "documents", "--workspace", repository.Path, "--role", "business", "--path", relative, "--format", "json"]).ExitCode);
        Assert.Equal(0, Invoke(application, ["graph", "build", "--workspace", repository.Path, "--format", "json"]).ExitCode);
        var reconcile = Invoke(application, ["brd", "reconcile", "--workspace", repository.Path, "--format", "json"]);
        Assert.Equal(0, reconcile.ExitCode);
        var migrated = File.ReadAllText(file);
        Assert.Contains(narrative, migrated);
        Assert.Contains("owner: Existing owner", migrated);
        using var result = JsonDocument.Parse(reconcile.Output);
        Assert.Contains($"stable_id: {result.RootElement.GetProperty("authorityRepositoryId").GetString()}:spec:business-requirements", migrated);
        Assert.Contains("status: Review Required", migrated);
        Assert.Contains("<!-- cis:sources:start -->", migrated);
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(repository.Path, ".cis/local/document-import"), "*.md"));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(0, Invoke(application, ["graph", "build", "--workspace", repository.Path, "--format", "json"]).ExitCode);
        Assert.Equal(0, Invoke(application, ["brd", "reconcile", "--workspace", repository.Path, "--format", "json"]).ExitCode);
        Assert.Equal(migrated, File.ReadAllText(file));
        // An explicit selection is not permission to replace a different CIS identity.
        File.WriteAllText(file, "---\nstatus: Draft\ncis:\n  stable_id: other:spec:requirements\n---\n# Existing BRD\n");
        var conflict = File.ReadAllText(file);
        Assert.Equal(4, Invoke(application, ["brd", "reconcile", "--workspace", repository.Path, "--format", "json"]).ExitCode);
        Assert.Equal(conflict, File.ReadAllText(file));
    }
}
