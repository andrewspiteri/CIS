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
