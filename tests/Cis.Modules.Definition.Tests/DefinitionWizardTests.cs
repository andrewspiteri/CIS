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
public sealed class DefinitionWizardTests
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
            ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis", "--yes", "--format", "json"]);
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

    [Fact]
    public void ProductDefinitionAuthority_RequiresConsolidatedActivationAndDetectsBaselineDrift()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application,
            ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis", "--yes", "--format", "json"]).ExitCode);
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
        var baseline = ProductDefinitionAuthority.ComputeBaselineHash(documentation, out var missing);
        Assert.Empty(missing);
        File.WriteAllText(backlog, File.ReadAllText(backlog)
            .Replace("sha256:first", "sha256:managed-link-update", StringComparison.Ordinal)
            .Replace("not-created", "docs/cis/specs/features/hlt-fr-001/feature-specification.md", StringComparison.Ordinal));
        var afterFeatureStart = ProductDefinitionAuthority.ComputeBaselineHash(documentation, out _);
        Assert.Equal(baseline, afterFeatureStart);
        var session = Path.Combine(repository.Path, ".cis", "local", "definition-wizard", "session.json");
        File.WriteAllText(session, $$"""
            {
              "schemaVersion": 1,
              "sessionId": "DEF-TEST",
              "currentPage": "review",
              "startedAtUtc": "2026-09-04T09:00:00Z",
              "activatedAtUtc": "2026-09-04T10:00:00Z",
              "active": false,
              "baselineHash": "{{baseline}}"
            }
            """);

        var activated = authority.Evaluate(repository.Path);
        Assert.True(activated.Active, string.Join(Environment.NewLine, activated.Errors));
        Assert.Equal(baseline, activated.BaselineHash);

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
