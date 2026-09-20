using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Brd;
using Cis.Modules.Definition;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.SolutionDesign;
using Cis.Modules.TechnicalIntent;
using Cis.Modules.UiDirection;
using Xunit;

namespace Cis.Modules.Definition.Tests;

[CollectionDefinition("DefinitionConsole", DisableParallelization = true)]
public sealed class DefinitionConsoleCollection;

[Collection("DefinitionConsole")]
public sealed partial class DefinitionWizardTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = CreateApplication();
        Assert.Equal(0, application.Invoke(["definition", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["definition", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["definition", "prepare", "--help"]));
        Assert.Equal(0, application.Invoke(["definition", "answer", "--help"]));
        Assert.Equal(0, application.Invoke(["definition", "activate", "--help"]));
        Assert.Equal(0, application.Invoke(["definition", "approve", "--help"]));
    }

    [Fact]
    public void Initialize_CreatesEightPageSessionAndClassificationSelectedIndexIdempotently()
    {
        using var repository = TemporaryRepository.Create();
        File.WriteAllText(Path.Combine(repository.Path, "package.json"), """
            { "scripts": { "test": "vitest run" }, "dependencies": { "next": "15.0.0", "react": "19.0.0" } }
            """);
        using var application = CreateApplication();

        var workspace = Invoke(application,
            ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
                "--ecosystem", "sample", "--product", "sample", "--ecosystem-name", "Sample", "--product-name", "Sample",
                "--yes", "--format", "json"]);
        Assert.Equal(0, workspace.ExitCode);
        var technicalIntent = Path.Combine(repository.Path, "docs", "cis", "specs", "technical-intent-spec.md");
        File.AppendAllText(technicalIntent, "\nHuman-managed technical direction.\n");
        var apiDictionary = Path.Combine(repository.Path, "docs", "cis", "references", "api-dictionary.md");
        File.Delete(apiDictionary);

        var initialized = Invoke(application,
            ["definition", "init", "--workspace", repository.Path, "--format", "json"]);
        Assert.Equal(0, initialized.ExitCode);
        Assert.Contains("\"status\": \"initialized\"", initialized.Output, StringComparison.Ordinal);
        Assert.Contains("\"ordinal\": 8", initialized.Output, StringComparison.Ordinal);
        Assert.Contains("\"title\": \"Review and activate\"", initialized.Output, StringComparison.Ordinal);
        Assert.Contains("\"technicalQuestions\": {", initialized.Output, StringComparison.Ordinal);
        Assert.Contains("\"uiQuestions\": {", initialized.Output, StringComparison.Ordinal);
        using (var result = System.Text.Json.JsonDocument.Parse(initialized.Output))
            Assert.Single(result.RootElement.GetProperty("businessInference").GetProperty("repositories").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis", "local", "definition-wizard", "session.json")));
        Assert.True(File.Exists(Path.Combine(repository.Path, "docs", "cis", "references", "dictionary-index.md")));
        Assert.True(File.Exists(apiDictionary));
        Assert.Contains("Human-managed technical direction.", File.ReadAllText(technicalIntent), StringComparison.Ordinal);

        var repeated = Invoke(application,
            ["definition", "init", "--workspace", repository.Path, "--format", "agent"]);
        Assert.Equal(0, repeated.ExitCode);
        Assert.Contains("pages=8", repeated.Output, StringComparison.Ordinal);
        Assert.Contains("currentPage=foundation", repeated.Output, StringComparison.Ordinal);

        var invalidPage = Invoke(application,
            ["definition", "prepare", "--workspace", repository.Path, "--page", "unknown", "--format", "agent"]);
        Assert.Equal(5, invalidPage.ExitCode);
        Assert.Contains("Unknown definition-wizard page", invalidPage.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("business")]
    [InlineData("technical")]
    [InlineData("contracts")]
    public void BusinessInference_SelectsOwnedParticipantsAndExcludesDocsAuthorityAndDependencies(string page)
    {
        using var authority = TemporaryRepository.Create();
        using var owned = TemporaryRepository.Create();
        using var dependency = TemporaryRepository.Create();
        File.WriteAllText(Path.Combine(owned.Path, "package.json"), """{"name":"deposits","dependencies":{"@nestjs/core":"1","@nestjs/common":"1","typeorm":"1"}}""");
        File.WriteAllText(Path.Combine(dependency.Path, "package.json"), """{"name":"external","dependencies":{"@nestjs/core":"1","@nestjs/common":"1"}}""");
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", authority.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        var importer = new RepositoryImporter(new RepositoryInitializer(), new WorkspaceRegistry(new CisRepositoryContextResolver()));
        Assert.Equal(0, importer.Import(new RepositoryImportRequest(authority.Path, "docs/cis", [owned.Path],
            false, true, "owned", "none")).ExitCode);
        Assert.Equal(0, importer.Import(new RepositoryImportRequest(authority.Path, "docs/cis", [dependency.Path],
            false, true, "dependency", "producer")).ExitCode);
        var status = Invoke(application, ["definition", "status", "--workspace", authority.Path, "--format", "json"]);
        using var result = System.Text.Json.JsonDocument.Parse(status.Output);
        var source = Assert.Single(result.RootElement.GetProperty("businessInference").GetProperty("repositories").EnumerateArray());
        Assert.Equal(owned.Path, source.GetProperty("repositoryPath").GetString());
        Assert.Equal("missing", source.GetProperty("graphFreshness").GetString());
        File.WriteAllText(Path.Combine(owned.Path, "controller.ts"), """
            import { Controller, Get } from '@nestjs/common';
            @Controller('products')
            export class ProductsController {
              @Get()
              list(): string { return 'products'; }
            }
            """);
        var dependencyDictionary = Path.Combine(dependency.Path, "docs/cis/references/api-dictionary.md");
        var dependencyBefore = File.ReadAllText(dependencyDictionary);
        var prepared = Invoke(application, ["definition", "prepare", "--page", page, "--workspace", authority.Path, "--format", "json"]);
        Assert.True(prepared.ExitCode == 0, prepared.Output + prepared.Error);
        using var preparation = System.Text.Json.JsonDocument.Parse(prepared.Output);
        var reports = preparation.RootElement.GetProperty("businessInference").GetProperty("lastPreparation").EnumerateArray().ToArray();
        Assert.Equal(2, reports.Length);
        var report = Assert.Single(reports, item => item.GetProperty("repositoryPath").GetString() == owned.Path);
        Assert.Equal(owned.Path, report.GetProperty("repositoryPath").GetString());
        Assert.Contains("/products", File.ReadAllText(Path.Combine(owned.Path, "docs/cis/references/api-dictionary.md")), StringComparison.Ordinal);
        Assert.Equal(dependencyBefore, File.ReadAllText(dependencyDictionary));
        Assert.Contains("/products", File.ReadAllText(Path.Combine(authority.Path, "docs/cis/references/api-dictionary.md")), StringComparison.Ordinal);
        var dictionaries = preparation.RootElement.GetProperty("dictionaries").EnumerateArray().ToArray();
        Assert.Contains(dictionaries, item => item.GetProperty("relativePath").GetString()!.EndsWith("api-dictionary.md") && item.GetProperty("entryCount").GetInt32() == 1);
        Assert.Contains(dictionaries, item => item.GetProperty("relativePath").GetString()!.EndsWith("data-dictionary.md") && item.GetProperty("entryCount").GetInt32() == 0);
    }

    [Fact]
    public void ProductDefinitionAuthority_RequiresConsolidatedActivationAndDetectsBaselineDrift()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application,
            ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
                "--ecosystem", "sample", "--product", "sample", "--ecosystem-name", "Sample", "--product-name", "Sample",
                "--yes", "--format", "json"]).ExitCode);
        Assert.Equal(0, Invoke(application,
            ["definition", "init", "--workspace", repository.Path, "--format", "json"]).ExitCode);
        var authority = new ProductDefinitionAuthority(new CisRepositoryContextResolver());

        var open = authority.Evaluate(repository.Path);
        Assert.True(open.Applicable);
        Assert.False(open.Active);
        Assert.Contains(open.Errors, error => error.Contains("not been consolidated and activated", StringComparison.OrdinalIgnoreCase));

        var documentation = Path.Combine(repository.Path, "docs", "cis");
        ProductDefinitionAuthority.ComputeBaselineHash(documentation, out var initiallyMissing);
        foreach (var relativePath in initiallyMissing)
        {
            var artifact = Path.Combine(documentation, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
            File.WriteAllText(artifact, $"# {Path.GetFileNameWithoutExtension(artifact)}\n");
        }
        var backlog = Path.Combine(documentation, "plans", "high-level-backlog.md");
        File.WriteAllText(backlog, """
            ---
            status: Active
            cis:
              approved_content_hash: sha256:first
            ---
            # High-level backlog
            <!-- cis:brd-backlog-items:start -->
            | ID | Requirement | Outcome | Priority | Repositories | Frontend | Depends on | Feature specification | Notes |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | HLT-FR-001 | BR-FR-001 | Outcome | Must | sample | customer | None | not-created | None |
            <!-- cis:brd-backlog-items:end -->
            """);
        var technicalIntent = Path.Combine(documentation, "specs", "technical-intent-spec.md");
        var technicalContent = File.ReadAllText(technicalIntent);
        if (!technicalContent.Contains("<!-- cis:technical-intent-baseline:start -->", StringComparison.Ordinal))
        {
            technicalContent += """

                <!-- cis:technical-intent-baseline:start -->
                | initial | baseline |
                <!-- cis:technical-intent-baseline:end -->
                """;
            File.WriteAllText(technicalIntent, technicalContent);
        }
        var baseline = ProductDefinitionAuthority.ComputeBaselineHash(documentation, out var missing);
        Assert.Empty(missing);
        File.WriteAllText(backlog, File.ReadAllText(backlog)
            .Replace("sha256:first", "sha256:managed-link-update", StringComparison.Ordinal)
            .Replace("not-created", "docs/cis/specs/features/hlt-fr-001/feature-specification.md", StringComparison.Ordinal));
        var afterFeatureStart = ProductDefinitionAuthority.ComputeBaselineHash(documentation, out _);
        Assert.Equal(baseline, afterFeatureStart);
        var brd = Path.Combine(documentation, "specs", "business-requirements.md");
        var brdContent = File.ReadAllText(brd).Replace(
            "<!-- cis:sources:end -->",
            "| BRD-SRC-feature | feature-specification | Adopted |\n<!-- cis:sources:end -->",
            StringComparison.Ordinal);
        brdContent += """

            <!-- cis:feature-traceability:start -->
            - BRD-SRC-feature: approved downstream feature scope.
            <!-- cis:feature-traceability:end -->
            """;
        File.WriteAllText(brd, brdContent);
        var afterFeatureReconciliation = ProductDefinitionAuthority.ComputeBaselineHash(documentation, out _);
        Assert.Equal(baseline, afterFeatureReconciliation);
        technicalContent = File.ReadAllText(technicalIntent);
        File.WriteAllText(technicalIntent, technicalContent.Replace(
            "| initial | baseline |",
            "| feature-traceability | refreshed |",
            StringComparison.Ordinal));
        var afterTechnicalEvidenceRefresh = ProductDefinitionAuthority.ComputeBaselineHash(documentation, out _);
        Assert.Equal(baseline, afterTechnicalEvidenceRefresh);
        var session = Path.Combine(repository.Path, ".cis", "local", "definition-wizard", "session.json");
        File.WriteAllText(session, $$"""
            {
              "schemaVersion": 1,
              "sessionId": "DEF-TEST",
              "currentPage": "review",
              "startedAtUtc": "2026-09-04T09:00:00Z",
              "activatedAtUtc": "2026-09-04T10:00:00Z",
              "active": false,
              "baselineHash": "sha256:legacy-activation-identity",
              "semanticBaselineHash": "{{baseline}}"
            }
            """);

        var activated = authority.Evaluate(repository.Path);
        Assert.True(activated.Active, string.Join(Environment.NewLine, activated.Errors));
        Assert.Equal("sha256:legacy-activation-identity", activated.BaselineHash);

        File.AppendAllText(Path.Combine(documentation, "design", "ui-direction.md"), "\nChanged after activation.\n");
        var stale = authority.Evaluate(repository.Path);
        Assert.False(stale.Active);
        Assert.Contains(stale.Errors, error => error.Contains("changed after consolidated activation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DraftScope_AcceptsValidatedDraftOnlyInsideTheBoundedCoordinatorScope()
    {
        Assert.False(CisDefinitionDraftScope.IsActive);
        Assert.False(CisDefinitionDraftScope.Accepts(true, true, "Ready for Approval"));
        Assert.True(CisDefinitionDraftScope.Accepts(true, true, "Active"));

        using (CisDefinitionDraftScope.Enter())
        {
            Assert.True(CisDefinitionDraftScope.IsActive);
            Assert.True(CisDefinitionDraftScope.Accepts(true, true, "Ready for Approval"));
            Assert.False(CisDefinitionDraftScope.Accepts(false, true, "Ready for Approval"));
        }

        Assert.False(CisDefinitionDraftScope.IsActive);
        Assert.False(CisDefinitionDraftScope.Accepts(true, true, "Ready for Approval"));
    }

    private static CisApplication CreateApplication() => new CisHostBuilder()
        .AddModule(new RepositoryModule())
        .AddModule(new WorkspaceModule())
        .AddModule(new DocsModule())
        .AddModule(new GraphModule())
        .AddModule(new BrdModule())
        .AddModule(new TechnicalIntentModule())
        .AddModule(new SolutionDesignModule())
        .AddModule(new UiDirectionModule())
        .AddModule(new DefinitionModule())
        .Build();

    private static (int ExitCode, string Output, string Error) Invoke(CisApplication application, string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            return (application.Invoke(args), output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path) => Path = path;
        public string Path { get; }
        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-definition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
