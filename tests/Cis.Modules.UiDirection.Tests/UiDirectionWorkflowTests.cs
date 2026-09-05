using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Repository;
using Cis.Modules.SolutionDesign;
using Cis.Modules.UiDirection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cis.Modules.UiDirection.Tests;

[CollectionDefinition("UiDirectionConsole", DisableParallelization = true)]
public sealed class UiDirectionConsoleCollection;

[Collection("UiDirectionConsole")]
public sealed class UiDirectionWorkflowTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var fixture = Fixture.Create();
        using var application = new CisHostBuilder().AddModule(new FixtureModule(fixture)).AddModule(new UiDirectionModule()).Build();
        Assert.Equal(0, application.Invoke(["ui-direction", "questions", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["ui-direction", "questions", "answer", "--help"]));
        Assert.Equal(0, application.Invoke(["ui-direction", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["ui-direction", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["ui-direction", "approve", "--help"]));
    }

    [Fact]
    public void QuestionnaireAndDirection_AreGeneratedApprovedAndTrackedIdempotently()
    {
        using var fixture = Fixture.Create();
        var initialized = fixture.Questionnaire.Initialize(fixture.Root);
        Assert.Equal(12, initialized.Questions.Count);
        Assert.Contains(initialized.Questions, question => question.Id == "UI-Q-001" && question.Status == "Derived");
        Assert.Contains(initialized.Questions, question => question.Id == "UI-Q-007" && question.Status == "Derived");
        foreach (var question in initialized.Questions.Where(question => question.Status == "Unanswered"))
            fixture.Questionnaire.Answer(fixture.Root, question.Id, question.SuggestedAnswer, "Andrew Spiteri");
        var complete = fixture.Questionnaire.Status(fixture.Root);
        Assert.True(complete.Complete); Assert.True(complete.Current);

        var generated = fixture.Service.Initialize(fixture.Root);
        Assert.Equal("Ready for Approval", generated.Validation!.EffectiveStatus);
        var content = File.ReadAllText(fixture.DirectionPath);
        Assert.Contains("## Shell and navigation model", content, StringComparison.Ordinal);
        Assert.Contains("## Reusable component system", content, StringComparison.Ordinal);
        Assert.Contains("`UI-Q-012`", content, StringComparison.Ordinal);
        Assert.False(fixture.Service.Evaluate(fixture.Root).Ready);

        var approved = fixture.Service.Approve(fixture.Root, "Andrew Spiteri", "The high-level UI direction is accepted.");
        Assert.Equal("Active", approved.Validation!.EffectiveStatus);
        Assert.True(fixture.Service.Evaluate(fixture.Root).Ready);
        var approvedContent = File.ReadAllText(fixture.DirectionPath);
        Assert.Contains("approved_content_hash: \"sha256:", approvedContent, StringComparison.Ordinal);

        var repeated = fixture.Service.Initialize(fixture.Root);
        Assert.False(repeated.Applied);
        Assert.Equal("Active", repeated.Validation!.EffectiveStatus);
        Assert.Equal(approvedContent, File.ReadAllText(fixture.DirectionPath));
    }

    [Fact]
    public void ChangedQuestionnaireOrGuidelines_MakesApprovedDirectionStaleAndRegenerationPreservesHumanTail()
    {
        using var fixture = Fixture.Create(); fixture.Complete();
        fixture.Service.Initialize(fixture.Root); fixture.Service.Approve(fixture.Root, "Design owner", "Accepted.");
        File.AppendAllText(fixture.DirectionPath, "\nA human-authored design-system exception.\n");
        Assert.Equal("Stale", fixture.Service.Status(fixture.Root).Validation!.EffectiveStatus);
        var reconciled = fixture.Service.Initialize(fixture.Root);
        Assert.Equal("Ready for Approval", reconciled.Validation!.EffectiveStatus);
        Assert.Contains("A human-authored design-system exception.", File.ReadAllText(fixture.DirectionPath), StringComparison.Ordinal);

        fixture.Service.Approve(fixture.Root, "Design owner", "Accepted.");
        File.AppendAllText(fixture.GuidelinesPath, "\nA new approved token direction.\n");
        var stale = fixture.Service.Status(fixture.Root);
        Assert.False(stale.Validation!.Current);
        Assert.Equal("Stale", stale.Validation.EffectiveStatus);
    }

    [Fact]
    public void IncompleteQuestionnaire_BlocksDirectionGeneration()
    {
        using var fixture = Fixture.Create();
        fixture.Questionnaire.Initialize(fixture.Root);
        var result = fixture.Service.Initialize(fixture.Root);
        Assert.Equal("blocked", result.Status); Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("complete, current UI-direction questionnaire", StringComparison.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, UiDirectionQuestionnaireService questionnaire, UiDirectionService service, StubSolutionSource source)
        { Root = root; Questionnaire = questionnaire; Service = service; Source = source; }
        public string Root { get; }
        public string DirectionPath => Path.Combine(Root, "docs", "design", "ui-direction.md");
        public string GuidelinesPath => Path.Combine(Root, "docs", "specs", "design-guidelines.md");
        public UiDirectionQuestionnaireService Questionnaire { get; }
        public UiDirectionService Service { get; }
        public StubSolutionSource Source { get; }
        public void Complete()
        {
            var result = Questionnaire.Initialize(Root);
            foreach (var question in result.Questions.Where(question => question.Status == "Unanswered"))
                Questionnaire.Answer(Root, question.Id, question.SuggestedAnswer, "Andrew Spiteri");
        }
        public static Fixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "cis-ui-direction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, ".cis")); Directory.CreateDirectory(Path.Combine(root, "docs", "architecture"));
            Directory.CreateDirectory(Path.Combine(root, "docs", "references")); Directory.CreateDirectory(Path.Combine(root, "docs", "specs"));
            File.WriteAllText(Path.Combine(root, ".cis", "workspace.yml"), "schema_version: 2\necosystem:\n  id: product\n  name: Product\nproduct:\n  id: product\n  name: Product\nrepositories:\n- id: product\n  path: .\n  documentation_root: docs\n  role: authority\n  participation: owned\n  relationship: none\n  components: []\n");
            File.WriteAllText(Path.Combine(root, ".cis", "repository.yml"), "repository:\n  id: product\ndocumentation_root: docs\n");
            File.WriteAllText(Path.Combine(root, "docs", "catalog.yml"), "schema_version: 1\nrepository: product\ndocuments: []\n");
            File.WriteAllText(Path.Combine(root, "docs", "architecture", "overall-solution-design.md"), SourceDocument("overall-solution-design", "product:architecture:overall-solution-design", "## UI design handoff\n\nCustomer web uses a compact task-oriented surface."));
            File.WriteAllText(Path.Combine(root, "docs", "references", "component-sheet.md"), SourceDocument("component-sheet", "product:reference:component-sheet", "## Component catalogue\n\n| `TI-MOD-WEB` | Customer web |"));
            File.WriteAllText(Path.Combine(root, "docs", "specs", "technical-intent-spec.md"), "# Technical intent\n\n## Product surfaces and entry points\n\n| Customer web | customer | React |\n");
            File.WriteAllText(Path.Combine(root, "docs", "references", "ui-framework-profile.md"), "# UI Framework Profile\n\n| WEB | Next.js | Existing | shadcn/ui | Tailwind CSS | CSS transitions |\n");
            File.WriteAllText(Path.Combine(root, "docs", "specs", "design-guidelines.md"), "---\ntype: design-guidelines\nstatus: Active\n---\n\n# Guidelines\n\nClear, restrained, accessible product UI.\n");
            var workspace = new CisWorkspace(root, Path.Combine(root, ".cis", "workspace.yml"), [new("product", root, "docs", "authority")]);
            var registry = new StubWorkspaceRegistry(workspace); var resolver = new StubRepositoryResolver(new(root, "product", "docs", Path.Combine(root, "docs"), Path.Combine(root, "docs", "catalog.yml")));
            var source = new StubSolutionSource(new("status", root, "product", "docs/architecture/overall-solution-design.md", "docs/references/component-sheet.md", "semantic-v1:test", [], new(true, true, "Active", "Active", "Active", [], []), [], false));
            var questionnaire = new UiDirectionQuestionnaireService(registry, resolver, new(), source, () => new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
            var service = new UiDirectionService(registry, resolver, new(), source, questionnaire, () => new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
            return new(root, questionnaire, service, source);
        }
        public void Dispose() => Directory.Delete(Root, true);
        private static string SourceDocument(string type, string id, string body) => $"""
            ---
            type: {type}
            status: Active
            cis:
              stable_id: {id}
              approved_by: "Andrew Spiteri"
              approved_at: "2026-09-03T10:00:00Z"
              approval_reason: "Accepted"
              approved_bundle_hash: "sha256:fixture"
            ---

            # Source

            {body}
            """;
    }

    private sealed class StubWorkspaceRegistry(CisWorkspace workspace) : ICisWorkspaceRegistry { public CisWorkspaceResolution Resolve(string workspacePath) => new(workspace, []); }
    private sealed class StubRepositoryResolver(CisRepositoryContext context) : ICisRepositoryContextResolver { public CisRepositoryContextResolution Resolve(string repositoryPath) => new(context, []); }
    private sealed class StubSolutionSource(SolutionDesignResult result) : IUiDirectionSolutionDesignSource { public SolutionDesignResult Result { get; set; } = result; public SolutionDesignResult Status(string workspacePath) => Result; }
    private sealed class FixtureModule(Fixture fixture) : ICisModule
    {
        public string Name => "ui-direction-fixture"; public string Description => "Fixture dependencies";
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<ICisWorkspaceRegistry>(new StubWorkspaceRegistry(new(fixture.Root, Path.Combine(fixture.Root, ".cis", "workspace.yml"), [new("product", fixture.Root, "docs", "authority")])));
            services.AddSingleton<ICisRepositoryContextResolver>(new StubRepositoryResolver(new(fixture.Root, "product", "docs", Path.Combine(fixture.Root, "docs"), Path.Combine(fixture.Root, "docs", "catalog.yml"))));
            services.AddSingleton(new DocumentationCatalogMerger()); services.AddSingleton<IUiDirectionSolutionDesignSource>(fixture.Source);
        }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }
}
